using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
    /// Model identifier that produced the current code/delta (e.g.
    /// <c>claude-sonnet-4.5</c>). Persisted alongside the delta sidecar so
    /// the provenance footer survives an app restart.
    /// </summary>
    public string? GeneratedBy { get; private set; }

    /// <summary>
    /// Wall-clock time when the current code/delta was produced. Persisted
    /// alongside the delta sidecar so the "generated 5 min ago" footer
    /// stays accurate across restarts.
    /// </summary>
    public DateTimeOffset? GeneratedAt { get; private set; }

    /// <summary>
    /// SHA-256 of the generated artifact bytes — set at generate time and
    /// re-set after Open Folder reads the live disk content. Persisted in
    /// the delta sidecar's frontmatter as <c>contentHash</c>.
    /// </summary>
    public string? SourceHash { get; private set; }

    /// <summary>
    /// True when the sidecar's stored hash disagrees with the live disk hash
    /// at load time — the artifact was modified outside the app between
    /// generation and the most recent Open Folder. The shell drives this;
    /// the UI shows a warning indicator. Cleared on the next regenerate.
    /// </summary>
    public bool StaleSinceLoad { get; private set; }

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

    /// <summary>
    /// Replace the entire code buffer with <paramref name="content"/>. Used by
    /// the generation pipeline once <see cref="Services.StepFileWriter"/> has
    /// produced the canonical on-disk file for this step — the streamed buffer
    /// can hold noise (alternate code blocks, multi-file emissions, fix-mode
    /// partials), so the post-write swap pins the UI to the actual file the
    /// user will run.
    /// </summary>
    public void ReplaceCode(string content)
    {
        lock (_gate)
        {
            _code.Clear();
            if (!string.IsNullOrEmpty(content))
                _code.Append(content);
        }
        RaiseChanged();
    }

    public void AppendDeltaToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        lock (_gate)
            _delta.Append(token);
        RaiseChanged();
    }

    /// <summary>
    /// Replace the entire delta buffer. Used when loading speaker notes from
    /// a previously-exported <c>speaker-notes.txt</c> on Open Folder so Copy
    /// Delta is wired up without re-generating.
    /// </summary>
    public void ReplaceDelta(string? content)
    {
        lock (_gate)
        {
            _delta.Clear();
            if (!string.IsNullOrEmpty(content))
                _delta.Append(content);
        }
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
        StaleSinceLoad = false;
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

    /// <summary>Stamp the current code/delta with the model id + a UTC timestamp.</summary>
    public void SetGenerationProvenance(string? generatedBy, DateTimeOffset? generatedAt)
    {
        GeneratedBy = generatedBy;
        GeneratedAt = generatedAt;
        RaiseChanged();
    }

    /// <summary>Stamp the current artifact's content hash.</summary>
    public void SetSourceHash(string? sourceHash)
    {
        SourceHash = sourceHash;
        RaiseChanged();
    }

    /// <summary>Mark the step as out-of-sync (or back in sync) at load time.</summary>
    public void SetStaleSinceLoad(bool stale)
    {
        if (StaleSinceLoad == stale) return;
        StaleSinceLoad = stale;
        RaiseChanged();
    }

    /// <summary>Renumber this step (used when a sibling is removed and the list compacts).</summary>
    public void Renumber(int newNumber)
    {
        if (Number == newNumber) return;
        Number = newNumber;
        RaiseChanged();
    }

    /// <summary>
    /// Coalesce window for the <see cref="Changed"/> event. Streaming-token
    /// mutations (<see cref="AppendCodeToken"/>, <see cref="AppendDeltaToken"/>)
    /// can fire hundreds of times per second; without coalescing the UI thread
    /// drowns in setState dispatches and visible side-effects (tooltip dismiss
    /// animations, scroll inertia, focus tracking) get wedged. 16 ms is one
    /// frame at 60 Hz — barely perceptible smoothing for one-shot mutations,
    /// dramatic relief for high-frequency streaming bursts.
    /// </summary>
    const int CoalesceMs = 16;
    int _changeScheduled;

    void RaiseChanged()
    {
        // Interlocked CAS ensures only one outstanding flush task per StepModel
        // regardless of which thread mutated state. The flush task clears the
        // flag BEFORE invoking handlers, so a mutation that races during the
        // invoke phase schedules a follow-up flush rather than getting lost.
        if (Interlocked.Exchange(ref _changeScheduled, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(CoalesceMs).ConfigureAwait(false);
            Interlocked.Exchange(ref _changeScheduled, 0);
            try { Changed?.Invoke(); }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StepModel] Changed handler threw: {ex}");
            }
        });
    }
}
