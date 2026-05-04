using System.Collections.Generic;
using System.Text;
using DemoScriptTool.App.Models;

namespace DemoScriptTool.App.Services;

/// <summary>
/// Parses <c>demo-script.md</c> into a <see cref="DemoScriptModel"/>.
///
/// The format is intentionally tight (spec §The Markdown Format) — a top-level
/// <c># Title</c>, an <c>## Demo Prompt</c> section, an <c>## Steps</c> section
/// containing a numbered list whose items begin with a bold title. Any other
/// content after the steps section is preserved as <c>RawTail</c> so we can
/// round-trip without losing information.
///
/// We hand-roll the recogniser (rather than dispatching to the framework's
/// internal SAX visitor) because the format is small, line-oriented, and the
/// public API surface of <c>Reactor.Documents.Markdown</c> is render-only as
/// of this writing — open question §2 in the spec, resolved by writing the
/// recogniser inline and filing a follow-up if a SAX surface lands.
/// </summary>
public static class DemoScriptParser
{
    /// <summary>Parse markdown text into a model, returning either the model or a parse error.</summary>
    public static (DemoScriptModel? Model, DemoScriptParseError? Error) Parse(string markdown)
    {
        if (markdown is null) markdown = string.Empty;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        string? title = null;
        var demoPrompt = new StringBuilder();
        var steps = new List<StepModel>();
        var rawTail = new StringBuilder();

        // Section state: 0 = preamble, 1 = demo prompt, 2 = steps, 3 = trailing/unknown
        int section = 0;
        StringBuilder? currentStepBody = null;
        int currentStepNumber = 0;
        string? currentStepTitle = null;

        void FlushCurrentStep()
        {
            if (currentStepBody is null || currentStepTitle is null) return;
            var body = currentStepBody.ToString().TrimEnd();
            steps.Add(new StepModel(currentStepNumber, currentStepTitle, body));
            currentStepBody = null;
            currentStepTitle = null;
            currentStepNumber = 0;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.TrimStart();

            // Top-level title — first non-empty H1 wins.
            if (title is null && trimmed.StartsWith("# ", System.StringComparison.Ordinal))
            {
                title = trimmed[2..].Trim();
                continue;
            }

            // Section transitions on H2 boundaries.
            if (trimmed.StartsWith("## ", System.StringComparison.Ordinal))
            {
                FlushCurrentStep();
                var name = trimmed[3..].Trim();
                if (Equals(name, "Demo Prompt"))
                {
                    section = 1;
                    continue;
                }
                if (Equals(name, "Steps"))
                {
                    section = 2;
                    continue;
                }
                section = 3;
                rawTail.AppendLine(raw);
                continue;
            }

            switch (section)
            {
                case 0:
                    // Pre-section content between the H1 title and the first H2.
                    // Capturing into rawTail would round-trip incorrectly: the
                    // serializer always writes rawTail at the end of the file,
                    // which would silently move preamble below the steps section
                    // on the next save. The format spec doesn't reserve space for
                    // a preamble, so we drop section-0 content rather than
                    // reorder it. (If a real round-trip need shows up, switch
                    // to a separate RawHeader field that the serializer emits
                    // immediately after the H1.)
                    break;

                case 1:
                    demoPrompt.AppendLine(raw);
                    break;

                case 2:
                    // Numbered list item with bold title prefix:
                    //   "1. **Title**\n   body line\n   another body line"
                    if (TryRecogniseStepStart(raw, out var num, out var stepTitle, out var inlineBody))
                    {
                        FlushCurrentStep();
                        currentStepNumber = num;
                        currentStepTitle = stepTitle;
                        currentStepBody = new StringBuilder();
                        if (!string.IsNullOrWhiteSpace(inlineBody))
                            currentStepBody.AppendLine(inlineBody.TrimStart());
                    }
                    else if (currentStepBody is not null)
                    {
                        // Continuation lines — strip the conventional 3-space indent.
                        var body = raw.StartsWith("   ", System.StringComparison.Ordinal) ? raw[3..] : raw;
                        currentStepBody.AppendLine(body);
                    }
                    else if (!string.IsNullOrWhiteSpace(raw))
                    {
                        // Non-empty non-list content under "## Steps" with no current step
                        // is a malformed list — surface it.
                        return (null, new DemoScriptParseError(
                            i + 1, 1,
                            "Step entries must begin with a numbered list item whose body starts with a **bold title**."));
                    }
                    break;

                case 3:
                    rawTail.AppendLine(raw);
                    break;
            }
        }

        FlushCurrentStep();

        var model = new DemoScriptModel(
            title: title ?? string.Empty,
            demoPrompt: demoPrompt.ToString().Trim('\n', '\r', ' '),
            steps: steps,
            rawTail: rawTail.Length == 0 ? null : rawTail.ToString());

        return (model, null);
    }

    /// <summary>
    /// Recognise a numbered list step start. Accepts patterns like
    /// <c>"1. **Title**"</c> or <c>"1. **Title** trailing inline body"</c>.
    /// </summary>
    static bool TryRecogniseStepStart(string line, out int number, out string title, out string trailingBody)
    {
        number = 0;
        title = string.Empty;
        trailingBody = string.Empty;

        if (string.IsNullOrEmpty(line)) return false;
        int idx = 0;
        while (idx < line.Length && (line[idx] == ' ' || line[idx] == '\t')) idx++;

        int digitsStart = idx;
        while (idx < line.Length && char.IsDigit(line[idx])) idx++;
        if (idx == digitsStart) return false;
        if (idx >= line.Length || (line[idx] != '.' && line[idx] != ')')) return false;
        if (!int.TryParse(line[digitsStart..idx], out number)) return false;
        idx++; // skip the . or )

        if (idx >= line.Length || line[idx] != ' ') return false;
        idx++;

        // Expect "**Title**"
        if (idx + 1 >= line.Length || line[idx] != '*' || line[idx + 1] != '*') return false;
        int titleStart = idx + 2;
        int closeBold = line.IndexOf("**", titleStart, System.StringComparison.Ordinal);
        if (closeBold < 0) return false;

        title = line[titleStart..closeBold].Trim();
        trailingBody = line[(closeBold + 2)..].TrimStart();
        // Empty bold titles are valid — a freshly-added step has no title yet;
        // the UI renders the placeholder "Step title" against the empty value.
        return true;
    }

    /// <summary>Serialise a model back to markdown text (UTF-8, LF newlines).</summary>
    public static string Serialise(DemoScriptModel model)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(model.Title.Length == 0 ? "Untitled Demo" : model.Title).Append('\n');
        sb.Append('\n');
        sb.Append("## Demo Prompt\n\n");
        if (!string.IsNullOrEmpty(model.DemoPrompt))
        {
            sb.Append(model.DemoPrompt.TrimEnd()).Append('\n');
            sb.Append('\n');
        }
        sb.Append("## Steps\n\n");
        for (int i = 0; i < model.Steps.Count; i++)
        {
            var step = model.Steps[i];
            sb.Append(i + 1).Append(". **").Append(step.Title).Append("**\n");
            if (!string.IsNullOrWhiteSpace(step.Prompt))
            {
                foreach (var line in step.Prompt.Split('\n'))
                    sb.Append("   ").Append(line.TrimEnd('\r')).Append('\n');
            }
            sb.Append('\n');
        }

        if (!string.IsNullOrEmpty(model.RawTail))
            sb.Append(model.RawTail.TrimEnd()).Append('\n');

        return sb.ToString();
    }

    static bool Equals(string a, string b) =>
        string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
}
