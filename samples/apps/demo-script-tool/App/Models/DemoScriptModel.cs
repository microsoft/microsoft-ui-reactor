using System.Collections.Generic;
using System.Linq;

namespace DemoScriptTool.App.Models;

/// <summary>
/// Top-level model for one <c>demo-script.md</c> document. Mutable so the UI
/// can edit prompts in place without the caller re-parsing on every keystroke.
/// </summary>
public sealed class DemoScriptModel
{
    readonly List<StepModel> _steps;

    public DemoScriptModel(string title, string demoPrompt, IEnumerable<StepModel> steps, string? rawTail = null)
    {
        Title = title;
        DemoPrompt = demoPrompt;
        _steps = steps.ToList();
        RawTail = rawTail;
    }

    public string Title { get; private set; }

    public string DemoPrompt { get; private set; }

    public IReadOnlyList<StepModel> Steps => _steps;

    /// <summary>
    /// Trailing markdown content the parser did not own (sections after
    /// <c>## Steps</c>). Preserved verbatim for round-trip serialization.
    /// </summary>
    public string? RawTail { get; }

    /// <summary>
    /// True when the demo prompt declares multi-file mode. Detected by the
    /// sentinel phrases <c>multi-file</c> / <c>multi file mode</c> /
    /// <c>multifile mode</c>; otherwise single-file is assumed (spec §File Layout).
    /// </summary>
    public bool IsMultiFile
    {
        get
        {
            var p = DemoPrompt;
            if (string.IsNullOrEmpty(p)) return false;
            return p.Contains("multi-file", System.StringComparison.OrdinalIgnoreCase)
                || p.Contains("multi file mode", System.StringComparison.OrdinalIgnoreCase)
                || p.Contains("multifile mode", System.StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Fires on title / demo-prompt edits. NOT on collection mutations.</summary>
    public event Action? FieldsChanged;

    /// <summary>Fires when steps are added / removed / replaced. NOT on field edits.</summary>
    public event Action? StepsChanged;

    public void UpdateTitle(string title)
    {
        if (Title == title) return;
        Title = title;
        FieldsChanged?.Invoke();
    }

    public void UpdateDemoPrompt(string prompt)
    {
        if (DemoPrompt == prompt) return;
        DemoPrompt = prompt;
        FieldsChanged?.Invoke();
    }

    public void ReplaceSteps(IEnumerable<StepModel> steps)
    {
        _steps.Clear();
        _steps.AddRange(steps);
        StepsChanged?.Invoke();
    }

    /// <summary>Append a new empty step at the next available number.</summary>
    public StepModel AddStep(string title = "", string prompt = "")
    {
        var nextNumber = _steps.Count == 0 ? 1 : _steps[^1].Number + 1;
        var step = new StepModel(nextNumber, title, prompt);
        _steps.Add(step);
        StepsChanged?.Invoke();
        return step;
    }

    /// <summary>Remove a step by number and renumber survivors so the list stays contiguous.</summary>
    public bool RemoveStep(int number)
    {
        var idx = _steps.FindIndex(s => s.Number == number);
        if (idx < 0) return false;
        _steps.RemoveAt(idx);
        // Renumber so step-NN files line up with positions for the next generation.
        for (int i = 0; i < _steps.Count; i++)
            _steps[i].Renumber(i + 1);
        StepsChanged?.Invoke();
        return true;
    }

    public static DemoScriptModel Empty() =>
        new(title: "", demoPrompt: "", steps: System.Array.Empty<StepModel>());
}
