using System.Text;

namespace DemoScriptTool.App.Models;

/// <summary>
/// One step of a demo script — author prompt, generated code, presenter delta,
/// and build state. The model is a single source of truth; UI components
/// subscribe to <see cref="Changed"/> for streaming updates without forcing the
/// parent component to re-render on every token.
/// </summary>
public sealed class StepModel
{
    readonly StringBuilder _code = new();
    readonly StringBuilder _delta = new();
    readonly object _gate = new();

    public StepModel(int number, string title, string prompt)
    {
        Number = number;
        Title = title;
        Prompt = prompt;
    }

    public int Number { get; private set; }

    public string Title { get; private set; }

    public string Prompt { get; private set; }

    public string Code
    {
        get { lock (_gate) return _code.ToString(); }
    }

    public string? Delta
    {
        get
        {
            lock (_gate)
                return _delta.Length == 0 ? null : _delta.ToString();
        }
    }

    public BuildState BuildState { get; private set; } = BuildState.NotBuilt;

    public string? BuildOutput { get; private set; }

    public int FixAttempts { get; private set; }

    /// <summary>
    /// Path on disk where the generated artifact lives once written.
    /// Single-file mode: <c>step-NN.cs</c>; multi-file mode: <c>step-NN/</c>.
    /// </summary>
    public string? OutputPath { get; private set; }

    /// <summary>
    /// Fires after any mutation. UI components subscribe via <see cref="UseEffect"/>
    /// and pull whatever fields they need.
    /// </summary>
    public event Action? Changed;

    public void UpdatePrompt(string prompt)
    {
        if (Prompt == prompt) return;
        Prompt = prompt;
        RaiseChanged();
    }

    public void UpdateTitle(string title)
    {
        if (Title == title) return;
        Title = title;
        RaiseChanged();
    }

    public void AppendCodeToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        lock (_gate)
            _code.Append(token);
        RaiseChanged();
    }

    public void AppendDeltaToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        lock (_gate)
            _delta.Append(token);
        RaiseChanged();
    }

    public void ResetForRegeneration()
    {
        lock (_gate)
        {
            _code.Clear();
            _delta.Clear();
        }
        BuildState = BuildState.NotBuilt;
        BuildOutput = null;
        FixAttempts = 0;
        OutputPath = null;
        RaiseChanged();
    }

    public void ResetCodeForFix()
    {
        lock (_gate) _code.Clear();
        BuildState = BuildState.Fixing;
        RaiseChanged();
    }

    public void SetBuildState(BuildState state, string? output = null)
    {
        BuildState = state;
        BuildOutput = output;
        RaiseChanged();
    }

    public void IncrementFixAttempts()
    {
        FixAttempts++;
        RaiseChanged();
    }

    public void SetOutputPath(string path)
    {
        OutputPath = path;
        RaiseChanged();
    }

    /// <summary>Renumber this step (used when a sibling is removed and the list compacts).</summary>
    public void Renumber(int newNumber)
    {
        if (Number == newNumber) return;
        Number = newNumber;
        RaiseChanged();
    }

    void RaiseChanged() => Changed?.Invoke();
}
