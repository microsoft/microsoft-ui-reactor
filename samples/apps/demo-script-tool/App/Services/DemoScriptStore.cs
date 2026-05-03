using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DemoScriptTool.App.Models;

namespace DemoScriptTool.App.Services;

/// <summary>
/// Reads and writes <c>demo-script.md</c> in a project root. Concurrent writes
/// are serialised through a semaphore so the UI's debounced saves and the
/// generation pipeline cannot tear the file (spec §Store).
/// </summary>
public sealed class DemoScriptStore
{
    public const string FileName = "demo-script.md";

    readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>
    /// Load the model from <paramref name="projectRoot"/>. Returns a typed parse
    /// error on malformed markdown; returns a scaffolded empty model when the
    /// file is missing.
    /// </summary>
    public async Task<(DemoScriptModel? Model, DemoScriptParseError? Error)> LoadAsync(string projectRoot, CancellationToken ct)
    {
        var path = System.IO.Path.Combine(projectRoot, FileName);
        if (!File.Exists(path))
            return (DemoScriptModel.Empty(), null);

        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return DemoScriptParser.Parse(text);
    }

    /// <summary>
    /// Save the model atomically: write to <c>demo-script.md.tmp</c>, then
    /// <see cref="File.Move(string, string, bool)"/> over the destination.
    /// Concurrent calls are serialised internally.
    /// </summary>
    public async Task SaveAsync(DemoScriptModel model, string projectRoot, CancellationToken ct)
    {
        var text = DemoScriptParser.Serialise(model);
        var path = System.IO.Path.Combine(projectRoot, FileName);
        var tmp = path + ".tmp";

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(tmp, text, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
