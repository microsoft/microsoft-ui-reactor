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

        try
        {
            _status.SetGeneratingStatus($"Preparing {model.Steps.Count} step{(model.Steps.Count == 1 ? "" : "s")}…");
            var userPrompt = BuildUserPrompt(model, projectRoot);
            System.Diagnostics.Debug.WriteLine($"[Pipeline] userPrompt built ({userPrompt.Length} bytes); calling StreamAndApplyAsync");
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
                await StreamAndApplyAsync(model, projectRoot, BuildUserPrompt(model, projectRoot), ct).ConfigureAwait(false);
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
            System.Diagnostics.Debug.WriteLine($"[Parser] StepStarted n={n}");
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
            System.Diagnostics.Debug.WriteLine($"[Parser] CodeBlockStarted n={n} path='{path}'");
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
            System.Diagnostics.Debug.WriteLine($"[Parser] StepCompleted n={n}");
            var step = FindStep(model, n);
            if (step is null) { System.Diagnostics.Debug.WriteLine($"[Parser] StepCompleted: no step n={n}"); return; }
            if (!fileBuffers.TryGetValue(n, out var buf)) { System.Diagnostics.Debug.WriteLine($"[Parser] StepCompleted: no buffer for n={n}"); return; }
            var snapshot = buf.Snapshot();
            System.Diagnostics.Debug.WriteLine($"[Parser] StepCompleted: snapshot has {snapshot.Count} files for n={n}");
            if (snapshot.Count == 0) return;

            var primary = _writer.Write(step.Number, snapshot, projectRoot, model.IsMultiFile);
            System.Diagnostics.Debug.WriteLine($"[Parser] StepCompleted: wrote primary='{primary}'");
            step.SetOutputPath(primary);

            // Replace the streamed code buffer with the canonical file content.
            // The streamed buffer can pick up multiple code blocks emitted by
            // the model under one ===STEP=== (e.g. step-NN.cs followed by an
            // accidental step-(N+1).cs) AND any concurrent fix-mode chunks
            // racing to mutate it — both of which produce the scrambled
            // GENERATED CODE pane we saw without this swap.
            try
            {
                if (System.IO.File.Exists(primary))
                    step.ReplaceCode(System.IO.File.ReadAllText(primary));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Parser] StepCompleted: ReplaceCode read failed: {ex.Message}");
            }

            // Stamp the step with the model id + the moment it produced this
            // content so the per-card provenance footer ("✨ generated by
            // claude-sonnet-4.5 · 2 min ago") survives a restart.
            step.SetGenerationProvenance(_client.ModelId, DateTimeOffset.Now);

            // Hash the file bytes so we can detect later edits made outside
            // the app. Stored in the delta sidecar's frontmatter; on Open
            // Folder the shell re-hashes the disk file and flags the step as
            // out-of-sync when they don't match.
            try
            {
                if (System.IO.File.Exists(primary))
                    step.SetSourceHash(ComputeFileHash(primary));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Parser] StepCompleted: ComputeFileHash failed: {ex.Message}");
            }

            // Persist the presenter delta + provenance + content hash as a
            // sibling file so Open Folder can restore them next launch.
            WriteDeltaSidecar(step, projectRoot);

            // Build-and-fix loop happens off the streaming thread to avoid blocking
            // subsequent steps' tokens.
            _ = Task.Run(() => RunBuildAndFixAsync(step, model, projectRoot, ct), ct);
        };

        parser.Warning += msg => { System.Diagnostics.Debug.WriteLine($"[Parser] Warning: {msg}"); _status.ShowToast(msg, StatusSeverity.Warning); };

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
            System.Diagnostics.Debug.WriteLine($"[BuildFix] step {step.Number}: starting initial build");
            var result = await _runner.BuildAsync(step, projectRoot, model.IsMultiFile, ct).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[BuildFix] step {step.Number}: initial build exit={result.ExitCode} succeeded={result.Succeeded} outputBytes={result.CombinedOutput.Length}");
            if (!result.Succeeded)
            {
                System.Diagnostics.Debug.WriteLine($"[BuildFix] step {step.Number} build output (last 500 chars): {result.CombinedOutput[Math.Max(0, result.CombinedOutput.Length - 500)..]}");
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
                System.Diagnostics.Debug.WriteLine($"[BuildFix] step {step.Number}: fix attempt {attempt}");
                if (!await ApplyFixAttemptAsync(step, model, projectRoot, lastOutput, ct).ConfigureAwait(false))
                    break;

                step.IncrementFixAttempts();
                var rebuild = await _runner.BuildAsync(step, projectRoot, model.IsMultiFile, ct).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"[BuildFix] step {step.Number}: fix-attempt {attempt} build exit={rebuild.ExitCode} succeeded={rebuild.Succeeded}");
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
        var fixedPrimary = _writer.Write(step.Number, snapshot, projectRoot, model.IsMultiFile);
        // Same canonicalize-on-write pattern as the initial generation —
        // collapse the streamed buffer to the actual file body.
        try
        {
            if (System.IO.File.Exists(fixedPrimary))
                step.ReplaceCode(System.IO.File.ReadAllText(fixedPrimary));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BuildFix] ReplaceCode read failed: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"[Pipeline] WriteDeltaSidecar failed: {ex.Message}");
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

    static StepModel? FindStep(DemoScriptModel model, int n)
    {
        foreach (var step in model.Steps)
            if (step.Number == n) return step;
        return null;
    }

    static string BuildUserPrompt(DemoScriptModel model, string projectRoot)
    {
        var sb = new StringBuilder();
        sb.Append("# Demo: ").Append(model.Title).Append('\n').Append('\n');
        sb.Append("## Demo Prompt (Layer 2)\n").Append(model.DemoPrompt).Append('\n').Append('\n');
        sb.Append("## Mode\n").Append(model.IsMultiFile ? "multi-file" : "single-file").Append('\n').Append('\n');

        // Help the model emit a correct relative #:project path when Reactor
        // is in scope. We compute the relative path from the demo's project
        // root to Reactor.csproj if it can be discovered; otherwise we hand
        // over the absolute paths and let the model figure it out.
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

        sb.Append("## Steps\n");
        foreach (var step in model.Steps)
        {
            sb.Append(step.Number).Append(". **").Append(step.Title).Append("**\n");
            sb.Append(step.Prompt).Append('\n').Append('\n');
        }
        sb.Append("Generate every step in order using the envelope from the system prompt.\n");
        return sb.ToString();
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
