using System;

namespace WidgetCreator.Services;

/// <summary>
/// A generated widget saved in the library. The folder at <see cref="Dir"/>
/// holds the source (<c>widget.cs</c>), the project, <c>meta.json</c>, and the
/// published binaries under <see cref="PublishDir"/>. <see cref="Icon"/> is a
/// single emoji; <see cref="Title"/> a short name — both produced by the model.
/// </summary>
public sealed record WidgetApp(
    string Id,
    string Title,
    string Icon,
    string Prompt,
    string Model,
    DateTime CreatedAt,
    string Dir,
    string ExePath,
    string PublishDir,
    string SessionId)
{
    /// <summary>Path to the generated source file.</summary>
    public string SourcePath => System.IO.Path.Join(Dir, "widget.cs");

    /// <summary>Path to the per-widget MXC permission policy (absent = default policy).</summary>
    public string PolicyPath => System.IO.Path.Join(Dir, "policy.json");

    /// <summary>Read the current generated source (empty if missing).</summary>
    public string ReadSource()
    {
        try { return System.IO.File.Exists(SourcePath) ? System.IO.File.ReadAllText(SourcePath) : ""; }
        catch (Exception ex)
        {
            SessionLog.Write($"[WidgetApp] read source failed for {Id}: {ex.Message}");
            return "";
        }
    }

    /// <summary>True when the published executable still exists on disk.</summary>
    public bool IsRunnable => !string.IsNullOrEmpty(ExePath) && System.IO.File.Exists(ExePath);
}
