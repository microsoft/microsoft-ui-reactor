using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DemoScriptTool.App.Models;

namespace DemoScriptTool.App.Services;

/// <summary>
/// Writes <c>speaker-notes.txt</c> in the project root by concatenating each
/// step's presenter delta. Matches the format documented in the design spec
/// §Speaker notes format.
/// </summary>
public sealed class SpeakerNotesExporter
{
    public const string FileName = "speaker-notes.txt";

    public async Task<string> ExportAsync(DemoScriptModel model, string projectRoot, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(model.Title) ? "Untitled Demo" : model.Title;
        sb.Append("DEMO SCRIPT — ").AppendLine(title);
        sb.Append("Generated: ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine();

        bool any = false;
        foreach (var step in model.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Delta)) continue;
            any = true;

            sb.AppendLine("─────────────────────────────────────");
            sb.Append("STEP ").Append(step.Number).Append(" — ").AppendLine(step.Title);
            sb.AppendLine("─────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine(step.Delta!.TrimEnd());
            sb.AppendLine();
        }

        if (!any)
            sb.AppendLine("No deltas generated yet. Run Generate All to populate speaker notes.");

        var path = Path.Combine(projectRoot, FileName);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, sb.ToString(), ct).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
        return path;
    }
}
