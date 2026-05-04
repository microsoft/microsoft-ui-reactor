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
///
/// Steps are generated SEQUENTIALLY: step N's stream + build-and-fix loop
/// runs to completion before step N+1 starts. The per-step user prompt
/// includes the prior step's post-fix on-disk code so each step's output
/// is an accretion on what came before, not an unrelated rewrite.
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
    public Task GenerateAllAsync(DemoScriptModel model, string projectRoot, CancellationToken ct) =>
        GenerateFromAsync(model, projectRoot, startIndex: 0, ct);

    /// <summary>
    /// Generate from <paramref name="startIndex"/> through the end of the
    /// script. The "Re-gen" affordance on each step card calls this with the
    /// chosen step's index — regenerating one step inevitably changes the
    /// baseline code that downstream steps were built on, so we always
    /// re-generate the chain from the chosen point onward.
    /// </summary>
    public async Task GenerateFromAsync(DemoScriptModel model, string projectRoot, int startIndex, CancellationToken ct)
    {
        SessionLog.Write($"[Pipeline] GenerateFrom start root='{projectRoot}' steps={model.Steps.Count} startIndex={startIndex} multiFile={model.IsMultiFile}");
        if (model.Steps.Count == 0)
        {
            _status.ShowToast("Add at least one step to your demo script before generating.", StatusSeverity.Warning);
            return;
        }
        if (startIndex < 0 || startIndex >= model.Steps.Count)
        {
            SessionLog.Write($"[Pipeline] GenerateFrom: invalid startIndex {startIndex}, falling back to 0");
            startIndex = 0;
        }

        try
        {
            for (int i = startIndex; i < model.Steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var step = model.Steps[i];
                var prior = i > 0 ? model.Steps[i - 1] : null;

                _status.SetGeneratingStatus($"Generating step {step.Number} of {model.Steps.Count}…");
                SessionLog.Write($"[Pipeline] streaming step {step.Number} (prior={(prior?.Number.ToString() ?? "<none>")})");

                if (!await StreamSingleStepWithAuthRetryAsync(model, projectRoot, step, prior, ct).ConfigureAwait(false))
                {
                    // No code emitted (model returned nothing or only delta).
                    // Halt: continuing would feed the next step a poisoned (empty
                    // or stale) baseline, which we observed cascading into a
                    // chain of "No code produced" failures. The user can fix
                    // the prompt and click Re-gen.
                    SessionLog.Write($"[Pipeline] step {step.Number}: no code produced; halting");
                    step.SetBuildState(BuildState.Failed, "No code produced.");
                    _status.SetBanner($"Step {step.Number} produced no code — generation halted. Edit the prompt or earlier steps and click Re-gen.");
                    return;
                }

                _status.SetGeneratingStatus($"Building step {step.Number} of {model.Steps.Count}…");
                await RunBuildAndFixAsync(step, model, projectRoot, ct).ConfigureAwait(false);

                // After the build-and-fix loop settles, the step is either
                // Succeeded or Failed. A Failed step means we exhausted the fix
                // attempts; passing its broken code to step N+1 produced
                // cascade-failure runs in practice, so halt here. The user can
                // tweak the failing step's prompt or an earlier step's code
                // and click Re-gen to pick up where we stopped.
                if (step.BuildState == BuildState.Failed)
                {
                    SessionLog.Write($"[Pipeline] step {step.Number}: build failed after {MaxFixAttempts} fix attempts; halting");
                    _status.SetBanner($"Step {step.Number} failed to build after {MaxFixAttempts} fix attempts — generation halted. Click Re-gen after editing the step.");
                    return;
                }
            }
            SessionLog.Write("[Pipeline] GenerateFrom completed normally");
        }
        catch (OperationCanceledException)
        {
            SessionLog.Write($"[Pipeline] Cancelled with {CompletedCount(model)} of {model.Steps.Count} done");
            _status.ShowToast($"Cancelled — {CompletedCount(model)} of {model.Steps.Count} steps generated.", StatusSeverity.Info);
        }
        catch (AuthUnavailableException ex)
        {
            SessionLog.Write($"[Pipeline] AuthUnavailable: {ex.Message}");
            _status.SetBanner(ex.Message);
        }
        catch (Exception ex)
        {
            SessionLog.Write($"[Pipeline] Generation failed: {ex}");
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

    /// <summary>
    /// Stream one step. On AuthExpired, re-authenticate and retry the SAME step
    /// once. Returns true when at least one CODE block was produced and written
    /// to disk; false on empty output.
    /// </summary>
    async Task<bool> StreamSingleStepWithAuthRetryAsync(DemoScriptModel model, string projectRoot, StepModel step, StepModel? prior, CancellationToken ct)
    {
        try
        {
            return await StreamSingleStepAsync(model, projectRoot, step, prior, ct).ConfigureAwait(false);
        }
        catch (AuthExpiredException ex)
        {
            SessionLog.Write($"[Pipeline] AuthExpired on step {step.Number}, retrying after re-auth: {ex.Message}");
            _status.SetGeneratingStatus("Re-authenticating with GitHub…");
            await _auth.EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            // Drop any cached client state (e.g. CopilotSdkClient's long-lived
            // CopilotClient session) so the retry actually picks up the new
            // gh auth credentials. Without this, refreshing gh auth state
            // changes nothing the next StreamAsync call can see.
            await _client.ResetAsync(ct).ConfigureAwait(false);
            _status.SetGeneratingStatus($"Generating step {step.Number} of {model.Steps.Count}…");
            return await StreamSingleStepAsync(model, projectRoot, step, prior, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stream a single step's response, parse it, write the canonical files to
    /// disk, and stamp provenance + content hash on <paramref name="step"/>.
    /// Returns false when the model produced no code blocks.
    /// </summary>
    async Task<bool> StreamSingleStepAsync(DemoScriptModel model, string projectRoot, StepModel step, StepModel? prior, CancellationToken ct)
    {
        // Wipe the streamed code/delta buffers and any prior build state up front
        // so the user sees this step's content arrive fresh — same animation as
        // an initial generation.
        step.ResetForRegeneration();

        var perStepPrompt = BuildSingleStepUserPrompt(model, projectRoot, step, prior);
        SessionLog.Write($"[Pipeline] step {step.Number} per-step user prompt ({perStepPrompt.Length} bytes)");

        var parser = new GeneratedOutputParser();
        var buffer = new StepFileBuffer();
        bool sawCode = false;

        parser.StepStarted += n =>
        {
            if (n != step.Number)
                SessionLog.Write($"[Parser] step number mismatch: expected {step.Number}, model emitted {n}");
        };

        parser.CodeBlockStarted += (_, path) =>
        {
            SessionLog.Write($"[Parser] step {step.Number} CodeBlockStarted path='{path}'");
            buffer.OpenFile(path);
        };

        parser.CodeChunk += (_, chunk) =>
        {
            buffer.AppendChunk(chunk);
            step.AppendCodeToken(chunk);
            sawCode = true;
        };

        parser.DeltaChunk += (_, chunk) => step.AppendDeltaToken(chunk);

        parser.StepCompleted += n =>
        {
            SessionLog.Write($"[Parser] step {step.Number} StepCompleted (model emitted n={n})");
        };

        parser.Warning += msg =>
        {
            SessionLog.Write($"[Parser] Warning: {msg}");
            _status.ShowToast(msg, StatusSeverity.Warning);
        };

        await foreach (var token in _client.StreamAsync(SystemPrompt, perStepPrompt, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            parser.Feed(token);
        }
        parser.Complete();

        if (!sawCode)
        {
            SessionLog.Write($"[Pipeline] step {step.Number}: no CodeChunk seen");
            return false;
        }

        var snapshot = buffer.Snapshot();
        if (snapshot.Count == 0)
        {
            SessionLog.Write($"[Pipeline] step {step.Number}: empty file snapshot");
            return false;
        }

        var primary = _writer.Write(step.Number, snapshot, projectRoot, model.IsMultiFile);
        SessionLog.Write($"[Pipeline] step {step.Number}: wrote primary='{primary}'");
        step.SetOutputPath(primary);

        // Replace the streamed buffer with the canonical file body. The streamed
        // buffer can pick up extra blocks emitted under one ===STEP=== envelope;
        // collapsing to disk content guarantees the GENERATED CODE pane matches
        // what `dotnet run` will execute.
        try
        {
            if (System.IO.File.Exists(primary))
                step.ReplaceCode(System.IO.File.ReadAllText(primary));
        }
        catch (Exception ex)
        {
            SessionLog.Write($"[Pipeline] step {step.Number}: ReplaceCode read failed: {ex.Message}");
        }

        // Stamp model id + timestamp so the per-card provenance footer survives
        // restart via the delta sidecar's YAML frontmatter.
        step.SetGenerationProvenance(_client.ModelId, DateTimeOffset.Now);

        // Hash the bytes so Open Folder can flag artifacts that drifted from
        // what we generated (manual edit, git checkout, etc.).
        try
        {
            if (System.IO.File.Exists(primary))
                step.SetSourceHash(ComputeFileHash(primary));
        }
        catch (Exception ex)
        {
            SessionLog.Write($"[Pipeline] step {step.Number}: ComputeFileHash failed: {ex.Message}");
        }

        WriteDeltaSidecar(step, projectRoot);
        return true;
    }

    async Task RunBuildAndFixAsync(StepModel step, DemoScriptModel model, string projectRoot, CancellationToken ct)
    {
        try
        {
            step.SetBuildState(BuildState.Building);
            SessionLog.Write($"[BuildFix] step {step.Number}: starting initial build");
            var result = await _runner.BuildAsync(step, projectRoot, model.IsMultiFile, ct).ConfigureAwait(false);
            SessionLog.Write($"[BuildFix] step {step.Number}: initial build exit={result.ExitCode} succeeded={result.Succeeded} outputBytes={result.CombinedOutput.Length}");
            if (!result.Succeeded)
            {
                // 800 chars: 500 was clipping the leading lines of compiler
                // output (the success-cascade from upstream project builds
                // pushes the actual CSC error past 500 chars from the tail).
                SessionLog.Write($"[BuildFix] step {step.Number} build output (last 800 chars): {result.CombinedOutput[Math.Max(0, result.CombinedOutput.Length - 800)..]}");
            }
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
                _status.SetGeneratingStatus($"Fixing step {step.Number} of {model.Steps.Count} (attempt {attempt})…");
                SessionLog.Write($"[BuildFix] step {step.Number}: fix attempt {attempt}");
                if (!await ApplyFixAttemptAsync(step, model, projectRoot, lastOutput, ct).ConfigureAwait(false))
                    break;

                step.IncrementFixAttempts();
                _status.SetGeneratingStatus($"Building step {step.Number} of {model.Steps.Count} (re-build {attempt})…");
                var rebuild = await _runner.BuildAsync(step, projectRoot, model.IsMultiFile, ct).ConfigureAwait(false);
                SessionLog.Write($"[BuildFix] step {step.Number}: fix-attempt {attempt} build exit={rebuild.ExitCode} succeeded={rebuild.Succeeded} outputBytes={rebuild.CombinedOutput.Length}");
                if (!rebuild.Succeeded)
                {
                    // Mirror the initial-build logging so we can see what each
                    // fix actually broke or left broken. The model's own diff
                    // is one big LLM blob; the COMPILER tells us whether it
                    // converged. Without this we can't tell "AI made it worse"
                    // from "AI made same mistake" from "different error each
                    // attempt" — they all just show "exit=1" otherwise.
                    SessionLog.Write($"[BuildFix] step {step.Number} fix-attempt {attempt} build output (last 800 chars): {rebuild.CombinedOutput[Math.Max(0, rebuild.CombinedOutput.Length - 800)..]}");
                }
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
        catch (OperationCanceledException) { /* leave whatever state we last set */ throw; }
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
            // Same reason as in the streaming-retry path above: refreshing gh
            // auth alone doesn't help an SDK that cached a long-lived session.
            // We still return false here (the outer build-and-fix loop will
            // count this as a consumed attempt), but at least the NEXT fix
            // attempt will start from a fresh client.
            await _client.ResetAsync(ct).ConfigureAwait(false);
            return false;
        }

        if (!gotCode) return false;

        var snapshot = buffer.Snapshot();
        if (snapshot.Count == 0) return false;
        var fixedPrimary = _writer.Write(step.Number, snapshot, projectRoot, model.IsMultiFile);
        // Same canonicalize-on-write pattern as the initial generation —
        // collapse the streamed buffer to the actual file body.
        try
        {
            if (System.IO.File.Exists(fixedPrimary))
            {
                var fixedBody = System.IO.File.ReadAllText(fixedPrimary);
                step.ReplaceCode(fixedBody);
                // Log the first 600 chars of what the AI emitted as its fix.
                // Without this we couldn't tell whether a 3rd-attempt failure
                // was the model repeating the same broken code or making a
                // new mistake — the fix prompt and the SDK events are opaque.
                var preview = fixedBody.Length > 600 ? fixedBody[..600] + "…[truncated]" : fixedBody;
                SessionLog.Write($"[BuildFix] step {step.Number} fix-attempt produced ({fixedBody.Length} bytes):\n{preview}");
            }
        }
        catch (Exception ex)
        {
            SessionLog.Write($"[BuildFix] ReplaceCode read failed: {ex.Message}");
        }
        return true;
    }

    /// <summary>
    /// Write the step's presenter delta to <c>step-NN.delta.md</c> next to the
    /// generated code so Open Folder can restore it on next launch. The
    /// per-step sidecar is independent of the aggregated <c>speaker-notes.txt</c>
    /// the user produces via Export Speaker Notes — that file is for sharing
    /// with co-presenters, this file is for the tool itself. Empty deltas
    /// remove an existing sidecar so stale notes don't survive a regenerate.
    /// </summary>
    public static string DeltaSidecarPath(int stepNumber, string projectRoot) =>
        System.IO.Path.Combine(projectRoot, $"step-{stepNumber:D2}.delta.md");

    /// <summary>
    /// SHA-256 of the generated artifact bytes. Stamped at generate time,
    /// persisted in the delta sidecar, and re-checked against the live
    /// disk content on Open Folder so we can flag artifacts that drifted
    /// from what the AI produced (hand edit, git checkout, etc.).
    /// </summary>
    public static string ComputeFileHash(string path) =>
        ComputeBytesHash(System.IO.File.ReadAllBytes(path));

    public static string ComputeBytesHash(byte[] bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        var hex = new StringBuilder(64);
        for (int i = 0; i < hash.Length; i++) hex.Append(hash[i].ToString("x2"));
        return hex.ToString();
    }
    static void WriteDeltaSidecar(StepModel step, string projectRoot)
    {
        var path = DeltaSidecarPath(step.Number, projectRoot);
        try
        {
            var delta = step.Delta;
            if (string.IsNullOrWhiteSpace(delta) && step.GeneratedAt is null)
            {
                // Nothing useful to persist — clear any stale sidecar.
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                return;
            }

            // YAML-style frontmatter at the top so a human reading the file in
            // VS Code sees ordinary markdown after the metadata block. Format:
            //
            //   ---
            //   generatedBy: claude-sonnet-4.5
            //   generatedAt: 2026-05-03T17:43:21.123Z
            //   ---
            //   <delta body>
            //
            // Frontmatter is optional — if both fields are missing we just
            // write the body. Reader tolerates either shape.
            var sb = new StringBuilder();
            if (step.GeneratedBy is not null || step.GeneratedAt is not null || step.SourceHash is not null)
            {
                sb.Append("---\n");
                if (step.GeneratedBy is not null)
                    sb.Append("generatedBy: ").Append(step.GeneratedBy).Append('\n');
                if (step.GeneratedAt is not null)
                    sb.Append("generatedAt: ").Append(step.GeneratedAt.Value.ToString("o", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                if (step.SourceHash is not null)
                    sb.Append("contentHash: ").Append(step.SourceHash).Append('\n');
                sb.Append("---\n");
            }
            if (!string.IsNullOrWhiteSpace(delta))
                sb.Append(delta);

            System.IO.File.WriteAllText(path, sb.ToString());
        }
        catch (Exception ex)
        {
            SessionLog.Write($"[Pipeline] WriteDeltaSidecar failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse a delta sidecar's contents, returning the body and (optionally)
    /// the model id + generated-at extracted from a YAML-ish frontmatter
    /// block. Tolerates files without frontmatter — those return body only.
    /// </summary>
    public static (string Body, string? GeneratedBy, DateTimeOffset? GeneratedAt, string? ContentHash) ParseDeltaSidecar(string raw)
    {
        if (raw is null) return ("", null, null, null);
        if (!raw.StartsWith("---", StringComparison.Ordinal))
            return (raw, null, null, null);

        // Find the closing ---. Allow either CRLF or LF line endings.
        var nl = raw.IndexOf('\n');
        if (nl < 0) return (raw, null, null, null);
        var rest = raw[(nl + 1)..];
        var closeIdx = rest.IndexOf("---", StringComparison.Ordinal);
        if (closeIdx < 0) return (raw, null, null, null);

        var fm = rest[..closeIdx];
        var afterClose = rest[(closeIdx + 3)..];
        if (afterClose.StartsWith('\n')) afterClose = afterClose[1..];
        else if (afterClose.StartsWith("\r\n", StringComparison.Ordinal)) afterClose = afterClose[2..];

        string? generatedBy = null;
        DateTimeOffset? generatedAt = null;
        string? contentHash = null;
        foreach (var rawLine in fm.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "generatedBy":
                    generatedBy = value;
                    break;
                case "generatedAt":
                    if (DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                            out var parsed))
                        generatedAt = parsed;
                    break;
                case "contentHash":
                    contentHash = value;
                    break;
            }
        }
        return (afterClose, generatedBy, generatedAt, contentHash);
    }

    /// <summary>
    /// Build the user prompt for ONE step. Includes the demo-wide context
    /// (title, demo prompt, mode, paths) and — when <paramref name="prior"/>
    /// is non-null — the prior step's post-fix on-disk code as a baseline
    /// the model should accrete onto.
    /// </summary>
    static string BuildSingleStepUserPrompt(DemoScriptModel model, string projectRoot, StepModel step, StepModel? prior)
    {
        var sb = new StringBuilder();
        sb.Append("# Demo: ").Append(model.Title).Append('\n').Append('\n');
        sb.Append("## Demo Prompt (Layer 2)\n").Append(model.DemoPrompt).Append('\n').Append('\n');
        sb.Append("## Mode\n").Append(model.IsMultiFile ? "multi-file" : "single-file").Append('\n').Append('\n');

        AppendPathsBlock(sb, projectRoot);

        // Prior step's final code becomes the baseline for this step. We prefer
        // the on-disk file (post-fix, post-canonicalize) because that's what the
        // user just saw built. Fall back to in-memory step.Code if the file is
        // missing for any reason.
        if (prior is not null)
        {
            var priorCode = ReadPriorStepCode(model, projectRoot, prior);
            if (!string.IsNullOrEmpty(priorCode))
            {
                var hint = prior.BuildState == BuildState.Failed
                    ? " (this code did not build cleanly — do your best to incorporate its intent while emitting a working version)"
                    : "";
                sb.Append("## Previous step's final code\n");
                sb.Append("Step ").Append(prior.Number).Append(" final source").Append(hint).Append(":\n");
                sb.Append("```csharp\n").Append(priorCode);
                if (!priorCode.EndsWith('\n')) sb.Append('\n');
                sb.Append("```\n\n");
                sb.Append("Use the source above as your baseline. The current step's code MUST keep every existing line of behavior intact unless this step's prompt explicitly asks to change or remove it.\n\n");
            }
        }

        sb.Append("## Step to generate\n");
        sb.Append(step.Number).Append(". **").Append(step.Title).Append("**\n");
        sb.Append(step.Prompt).Append('\n').Append('\n');
        sb.Append("Emit ONLY step ").Append(step.Number).Append(" using the envelope from the system prompt. Do not preview or include other steps.\n");
        return sb.ToString();
    }

    /// <summary>
    /// Append the shared "Paths" block: project root, Reactor source dir,
    /// suggested `#:project` relative path, and host RID. Same content the
    /// previous batch prompt produced — split out so per-step prompts can
    /// reuse it without copy/pasting.
    /// </summary>
    static void AppendPathsBlock(StringBuilder sb, string projectRoot)
    {
        var (reactorDir, reactorRel) = ResolveReactorPath(projectRoot);
        var rid = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "win-arm64",
            System.Runtime.InteropServices.Architecture.X64 => "win-x64",
            System.Runtime.InteropServices.Architecture.X86 => "win-x86",
            _ => "win-x64",
        };
        sb.Append("## Paths\n");
        sb.Append("- Project root (where step-NN.cs files are written): ").Append(projectRoot).Append('\n');
        if (reactorDir is not null)
        {
            sb.Append("- Reactor source directory: ").Append(reactorDir).Append('\n');
            sb.Append("- Suggested `#:project` directive: `#:project ").Append(reactorRel).Append("`\n");
        }
        else
        {
            sb.Append("- Reactor source directory: NOT FOUND on this machine. ");
            sb.Append("Use `#:package Microsoft.UI.Reactor` if a published NuGet package exists, ");
            sb.Append("or fall back to a non-Reactor framework like Spectre.Console for the demo.\n");
        }
        sb.Append("- Host runtime identifier (use this in `#:property RuntimeIdentifier=...`): ")
            .Append(rid).Append('\n');
        sb.Append('\n');
    }

    /// <summary>
    /// Resolve the prior step's most-recently-generated source. Prefers the
    /// on-disk file (post-fix, post-canonicalize) so we feed the model what
    /// `dotnet run` would actually execute; falls back to <see cref="StepModel.Code"/>
    /// for the rare case where the file vanished between steps (e.g. user
    /// deleted it manually).
    /// </summary>
    static string? ReadPriorStepCode(DemoScriptModel model, string projectRoot, StepModel prior)
    {
        if (!string.IsNullOrEmpty(prior.OutputPath) && System.IO.File.Exists(prior.OutputPath))
        {
            try { return System.IO.File.ReadAllText(prior.OutputPath); }
            catch (Exception ex)
            {
                SessionLog.Write($"[Pipeline] read prior step file failed: {ex.Message}");
            }
        }
        // Fallback paths for the standard layout — same shape the writer uses.
        var fallback = model.IsMultiFile
            ? System.IO.Path.Combine(projectRoot, $"step-{prior.Number:D2}", "Program.cs")
            : System.IO.Path.Combine(projectRoot, $"step-{prior.Number:D2}.cs");
        if (System.IO.File.Exists(fallback))
        {
            try { return System.IO.File.ReadAllText(fallback); }
            catch (Exception ex)
            {
                SessionLog.Write($"[Pipeline] read prior step fallback failed: {ex.Message}");
            }
        }
        var inMem = prior.Code;
        return string.IsNullOrEmpty(inMem) ? null : inMem;
    }

    /// <summary>
    /// Find Reactor's source directory by walking up from <paramref name="projectRoot"/>
    /// looking for a sibling repo or an ancestor `src/Reactor/Reactor.csproj`.
    /// Returns (absoluteDir, relativeFromProjectRoot) or (null, "") if not found.
    /// </summary>
    static (string? Absolute, string Relative) ResolveReactorPath(string projectRoot)
    {
        // Walk up from projectRoot — handles "demo lives inside the Reactor repo"
        // and "demo lives in a sibling folder of the Reactor repo" alike.
        var dir = new System.IO.DirectoryInfo(projectRoot);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            // Same-tree: <dir>/src/Reactor/Reactor.csproj
            var inTree = System.IO.Path.Combine(dir.FullName, "src", "Reactor", "Reactor.csproj");
            if (System.IO.File.Exists(inTree))
                return (System.IO.Path.GetDirectoryName(inTree)!, RelPath(projectRoot, System.IO.Path.GetDirectoryName(inTree)!));

            // Sibling: <dir>/<repo>/src/Reactor/Reactor.csproj
            try
            {
                foreach (var sub in dir.EnumerateDirectories())
                {
                    var siblingTree = System.IO.Path.Combine(sub.FullName, "src", "Reactor", "Reactor.csproj");
                    if (System.IO.File.Exists(siblingTree))
                        return (System.IO.Path.GetDirectoryName(siblingTree)!, RelPath(projectRoot, System.IO.Path.GetDirectoryName(siblingTree)!));
                }
            }
            catch { /* permission etc. — ignore */ }
        }
        return (null, "");
    }

    static string RelPath(string fromDir, string toDir)
    {
        var rel = System.IO.Path.GetRelativePath(fromDir, toDir);
        // Normalize to forward slashes — `dotnet run` accepts both, but the
        // file-based-app `#:project` directive is more portable with `/`.
        return rel.Replace('\\', '/');
    }
}
