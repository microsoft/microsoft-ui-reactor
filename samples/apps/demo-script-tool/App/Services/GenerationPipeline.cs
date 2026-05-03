using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DemoScriptTool.App.Models;

namespace DemoScriptTool.App.Services;

/// <summary>
/// Drives the full generation flow for one demo: build the prompt, stream
/// tokens, parse the envelope, write step files, and run the build-and-fix
/// loop. The pipeline keeps no UI state — it mutates the supplied
/// <see cref="DemoScriptModel"/> and reports progress through
/// <see cref="StatusReporter"/>.
/// </summary>
public sealed class GenerationPipeline
{
    const int MaxFixAttempts = 3;

    readonly IModelClient _client;
    readonly DotnetRunner _runner;
    readonly StepFileWriter _writer;
    readonly GhAuth _auth;
    readonly StatusReporter _status;

    public GenerationPipeline(IModelClient client, DotnetRunner runner, StepFileWriter writer, GhAuth auth, StatusReporter status)
    {
        _client = client;
        _runner = runner;
        _writer = writer;
        _auth = auth;
        _status = status;
    }

    /// <summary>Lazy-loaded Layer 1 system prompt embedded at build time.</summary>
    public static string SystemPrompt => _systemPrompt ??= LoadEmbeddedPrompt();
    static string? _systemPrompt;

    static string LoadEmbeddedPrompt()
    {
        var asm = Assembly.GetExecutingAssembly();
        // Embedded resource ids look like "DemoScriptTool.Resources.SystemPrompt.txt".
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (name.EndsWith("SystemPrompt.txt", StringComparison.Ordinal))
            {
                using var s = asm.GetManifestResourceStream(name)!;
                using var r = new System.IO.StreamReader(s);
                return r.ReadToEnd();
            }
        }
        throw new InvalidOperationException("SystemPrompt.txt embedded resource missing — check DemoScriptTool.csproj <EmbeddedResource>.");
    }

    /// <summary>Generate every step in <paramref name="model"/> sequentially.</summary>
    public async Task GenerateAllAsync(DemoScriptModel model, string projectRoot, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[Pipeline] GenerateAll start root='{projectRoot}' steps={model.Steps.Count} multiFile={model.IsMultiFile}");
        if (model.Steps.Count == 0)
        {
            _status.ShowToast("Add at least one step to your demo script before generating.", StatusSeverity.Warning);
            return;
        }

        _status.SetGeneratingStatus($"Preparing {model.Steps.Count} step{(model.Steps.Count == 1 ? "" : "s")}…");
        var userPrompt = BuildUserPrompt(model);

        try
        {
            await StreamAndApplyAsync(model, projectRoot, userPrompt, ct).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine("[Pipeline] GenerateAll completed normally");
        }
        catch (AuthExpiredException ex)
        {
            // Spec §5.1: retry once after re-auth.
            System.Diagnostics.Debug.WriteLine($"[Pipeline] AuthExpired, retrying after re-auth: {ex.Message}");
            _status.SetGeneratingStatus("Re-authenticating with GitHub…");
            try
            {
                await _auth.EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
                await StreamAndApplyAsync(model, projectRoot, userPrompt, ct).ConfigureAwait(false);
            }
            catch (AuthExpiredException ex2)
            {
                System.Diagnostics.Debug.WriteLine($"[Pipeline] AuthExpired after retry: {ex2.Message}");
                _status.SetBanner($"Authentication failed. {ex2.Message}");
            }
            catch (AuthUnavailableException ex2)
            {
                System.Diagnostics.Debug.WriteLine($"[Pipeline] AuthUnavailable: {ex2.Message}");
                _status.SetBanner(ex2.Message);
            }
        }
        catch (AuthUnavailableException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Pipeline] AuthUnavailable: {ex.Message}");
            _status.SetBanner(ex.Message);
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[Pipeline] Cancelled with {CompletedCount(model)} of {model.Steps.Count} done");
            _status.ShowToast($"Cancelled — {CompletedCount(model)} of {model.Steps.Count} steps generated.", StatusSeverity.Info);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Pipeline] Generation failed: {ex}");
            _status.SetBanner($"Generation failed: {ex.Message}");
        }
        finally
        {
            _status.SetGeneratingStatus(null);
        }
    }

    static int CompletedCount(DemoScriptModel model)
    {
        int n = 0;
        foreach (var s in model.Steps)
            if (s.OutputPath is not null) n++;
        return n;
    }

    async Task StreamAndApplyAsync(DemoScriptModel model, string projectRoot, string userPrompt, CancellationToken ct)
    {
        var parser = new GeneratedOutputParser();
        var fileBuffers = new Dictionary<int, StepFileBuffer>();

        StepModel? currentStep = null;

        parser.StepStarted += n =>
        {
            currentStep = FindStep(model, n);
            if (currentStep is not null)
            {
                currentStep.ResetForRegeneration();
                _status.SetGeneratingStatus($"Generating step {n} of {model.Steps.Count}…");
            }
            fileBuffers[n] = new StepFileBuffer();
        };

        parser.CodeBlockStarted += (n, path) =>
        {
            if (fileBuffers.TryGetValue(n, out var buf)) buf.OpenFile(path);
        };

        parser.CodeChunk += (n, chunk) =>
        {
            if (fileBuffers.TryGetValue(n, out var buf)) buf.AppendChunk(chunk);
            var step = FindStep(model, n);
            // Stream into the primary code viewer regardless of which file is open
            // — for single-file mode this is the only file; for multi-file mode the
            // viewer shows whichever file the model is currently writing.
            step?.AppendCodeToken(chunk);
        };

        parser.DeltaChunk += (n, chunk) =>
        {
            var step = FindStep(model, n);
            step?.AppendDeltaToken(chunk);
        };

        parser.StepCompleted += n =>
        {
            var step = FindStep(model, n);
            if (step is null) return;
            if (!fileBuffers.TryGetValue(n, out var buf)) return;
            var snapshot = buf.Snapshot();
            if (snapshot.Count == 0) return;

            var primary = _writer.Write(step.Number, snapshot, projectRoot, model.IsMultiFile);
            step.SetOutputPath(primary);

            // Build-and-fix loop happens off the streaming thread to avoid blocking
            // subsequent steps' tokens.
            _ = Task.Run(() => RunBuildAndFixAsync(step, model, projectRoot, ct), ct);
        };

        parser.Warning += msg => _status.ShowToast(msg, StatusSeverity.Warning);

        await foreach (var token in _client.StreamAsync(SystemPrompt, userPrompt, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            parser.Feed(token);
        }
        parser.Complete();
    }

    async Task RunBuildAndFixAsync(StepModel step, DemoScriptModel model, string projectRoot, CancellationToken ct)
    {
        try
        {
            step.SetBuildState(BuildState.Building);
            var result = await _runner.BuildAsync(step, projectRoot, model.IsMultiFile, ct).ConfigureAwait(false);
            if (result.Succeeded)
            {
                step.SetBuildState(BuildState.Succeeded);
                return;
            }

            step.SetBuildState(BuildState.Fixing, result.CombinedOutput);
            var lastOutput = result.CombinedOutput;

            for (int attempt = 1; attempt <= MaxFixAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if (!await ApplyFixAttemptAsync(step, model, projectRoot, lastOutput, ct).ConfigureAwait(false))
                    break;

                step.IncrementFixAttempts();
                var rebuild = await _runner.BuildAsync(step, projectRoot, model.IsMultiFile, ct).ConfigureAwait(false);
                if (rebuild.Succeeded)
                {
                    step.SetBuildState(BuildState.Succeeded);
                    return;
                }
                lastOutput = rebuild.CombinedOutput;
                step.SetBuildState(BuildState.Fixing, lastOutput);
            }

            step.SetBuildState(BuildState.Failed, lastOutput);
        }
        catch (OperationCanceledException) { /* leave whatever state we last set */ }
        catch (Exception ex)
        {
            step.SetBuildState(BuildState.Failed, ex.Message);
        }
    }

    async Task<bool> ApplyFixAttemptAsync(StepModel step, DemoScriptModel model, string projectRoot, string compilerOutput, CancellationToken ct)
    {
        var previousCode = step.Code;
        var fixPrompt = new StringBuilder();
        fixPrompt.Append("FIX_MODE\nstep ").Append(step.Number).Append('\n');
        fixPrompt.Append("Mode: ").Append(model.IsMultiFile ? "multi-file" : "single-file").Append('\n');
        fixPrompt.Append("Filename: ").Append(model.IsMultiFile
            ? $"step-{step.Number:D2}/Program.cs"
            : $"step-{step.Number:D2}.cs").Append('\n');
        fixPrompt.Append("\n# Previous code\n```csharp\n").Append(previousCode).Append("\n```\n");
        fixPrompt.Append("\n# Compiler output\n```\n").Append(compilerOutput).Append("\n```\n");
        fixPrompt.Append("\nReturn ONLY the corrected file in a single ===CODE <path>=== block.\n");

        // Reset the visible code stream so the user sees the fix arrive char-by-char,
        // matching the initial-generation animation.
        step.ResetCodeForFix();

        var parser = new GeneratedOutputParser();
        var buffer = new StepFileBuffer();
        bool gotCode = false;

        parser.CodeBlockStarted += (_, path) => { buffer.OpenFile(path); gotCode = true; };
        parser.CodeChunk += (_, chunk) =>
        {
            buffer.AppendChunk(chunk);
            step.AppendCodeToken(chunk);
        };

        try
        {
            await foreach (var token in _client.StreamAsync(SystemPrompt, fixPrompt.ToString(), ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                parser.Feed(token);
            }
            parser.Complete();
        }
        catch (AuthExpiredException)
        {
            await _auth.EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            return false;
        }

        if (!gotCode) return false;

        var snapshot = buffer.Snapshot();
        // Fix-mode replies may omit the relative path prefix in single-file mode;
        // synthesise one if missing.
        if (snapshot.Count == 0)
        {
            return false;
        }
        _writer.Write(step.Number, snapshot, projectRoot, model.IsMultiFile);
        return true;
    }

    static StepModel? FindStep(DemoScriptModel model, int n)
    {
        foreach (var step in model.Steps)
            if (step.Number == n) return step;
        return null;
    }

    static string BuildUserPrompt(DemoScriptModel model)
    {
        var sb = new StringBuilder();
        sb.Append("# Demo: ").Append(model.Title).Append('\n').Append('\n');
        sb.Append("## Demo Prompt (Layer 2)\n").Append(model.DemoPrompt).Append('\n').Append('\n');
        sb.Append("## Mode\n").Append(model.IsMultiFile ? "multi-file" : "single-file").Append('\n').Append('\n');
        sb.Append("## Steps\n");
        foreach (var step in model.Steps)
        {
            sb.Append(step.Number).Append(". **").Append(step.Title).Append("**\n");
            sb.Append(step.Prompt).Append('\n').Append('\n');
        }
        sb.Append("Generate every step in order using the envelope from the system prompt.\n");
        return sb.ToString();
    }
}
