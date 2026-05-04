using System.Collections.Generic;
using System.IO;
using DemoScriptTool.App.Models;

namespace DemoScriptTool.App.Services;

/// <summary>
/// Writes generated step artifacts to disk under the project root. Single-file
/// mode: <c>step-NN.cs</c> directly under the root. Multi-file mode: anything
/// under <c>step-NN/</c>; if the model did not emit a <c>.csproj</c> we
/// scaffold a minimal one so <c>dotnet run</c> works.
/// </summary>
public sealed class StepFileWriter
{
    /// <summary>
    /// Persist accumulated files for one step. <paramref name="files"/> is keyed
    /// by the relative path the model provided (e.g. <c>step-01.cs</c> or
    /// <c>step-02/Program.cs</c>). Returns the path to the step's primary
    /// artifact (used for <c>dotnet run</c>).
    /// </summary>
    public string Write(int stepNumber, IReadOnlyDictionary<string, string> files, string projectRoot, bool multiFile)
    {
        Directory.CreateDirectory(projectRoot);

        if (!multiFile)
        {
            // Single-file mode: if the model emitted an absolute or stepped path,
            // collapse to the canonical step-NN.cs at the project root.
            var name = $"step-{stepNumber:D2}.cs";
            string body = string.Empty;
            foreach (var kv in files)
            {
                if (kv.Key.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase))
                {
                    body = kv.Value;
                    break;
                }
            }
            var path = Path.Combine(projectRoot, name);
            WriteWithRetry(path, body);
            return path;
        }

        // Multi-file mode: write each file under step-NN/ verbatim.
        var stepDir = Path.Combine(projectRoot, $"step-{stepNumber:D2}");
        Directory.CreateDirectory(stepDir);

        string? primary = null;
        bool sawCsproj = false;

        foreach (var kv in files)
        {
            // Normalise: drop a leading "step-NN/" component if the model included it.
            var rel = kv.Key.Replace('\\', '/');
            var prefix = $"step-{stepNumber:D2}/";
            if (rel.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                rel = rel[prefix.Length..];

            var target = Path.Combine(stepDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            WriteWithRetry(target, kv.Value);

            if (rel.EndsWith(".csproj", System.StringComparison.OrdinalIgnoreCase))
                sawCsproj = true;
            if (rel.EndsWith("Program.cs", System.StringComparison.OrdinalIgnoreCase))
                primary ??= target;
        }

        if (!sawCsproj)
        {
            var fallbackCsproj = Path.Combine(stepDir, $"step-{stepNumber:D2}.csproj");
            WriteWithRetry(fallbackCsproj, ScaffoldCsproj());
        }

        return primary ?? stepDir;
    }

    /// <summary>
    /// Write a file with bounded retry-with-backoff against transient sharing
    /// violations. The .cs file is the source the user runs via `dotnet run`;
    /// when the prior run is still alive (or the SDK's file-based-app
    /// host process holds a handle), regeneration would otherwise fail with
    /// IOException 0x80070020 ("being used by another process") and abort
    /// the whole pipeline.
    /// </summary>
    static void WriteWithRetry(string path, string content)
    {
        const int maxAttempts = 6;
        var delayMs = 50;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.WriteAllText(path, content);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                System.Threading.Thread.Sleep(delayMs);
                delayMs = System.Math.Min(delayMs * 2, 800); // 50→100→200→400→800
            }
        }
    }

    static string ScaffoldCsproj() => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;
}

/// <summary>Mutable in-progress collector for one step's emitted files.</summary>
internal sealed class StepFileBuffer
{
    readonly Dictionary<string, System.Text.StringBuilder> _files = new(System.StringComparer.OrdinalIgnoreCase);
    string? _current;

    public void OpenFile(string relativePath)
    {
        _current = relativePath;
        if (!_files.ContainsKey(relativePath))
            _files[relativePath] = new System.Text.StringBuilder();
    }

    public void AppendChunk(string chunk)
    {
        if (_current is null) return;
        _files[_current].Append(chunk);
    }

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        var result = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _files)
            result[kv.Key] = kv.Value.ToString();
        return result;
    }

    public string? PrimaryCode()
    {
        // Best-effort: the longest file body is usually Program.cs.
        string? winner = null;
        int max = -1;
        foreach (var kv in _files)
        {
            if (kv.Value.Length > max) { max = kv.Value.Length; winner = kv.Value.ToString(); }
        }
        return winner;
    }
}
