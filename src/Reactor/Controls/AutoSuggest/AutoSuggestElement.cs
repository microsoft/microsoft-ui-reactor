using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// State for the search operation: loading, results, error, or empty.
/// </summary>
public enum SearchState
{
    Idle,
    Loading,
    Results,
    Empty,
    Error
}

/// <summary>
/// A type-safe auto-suggest element with async search, debounce, and custom templates.
/// </summary>
public sealed record AutoSuggestElement<T>(
    T? Selected,
    Action<T?>? OnSelected = null,
    Func<string, CancellationToken, Task<IReadOnlyList<T>>>? Search = null,
    Func<T, string>? DisplayText = null,
    string? PlaceholderText = null) : Element
{
    /// <summary>Debounce delay in milliseconds before triggering search. Default: 300ms.</summary>
    public int DebounceMs { get; init; } = 300;

    /// <summary>Custom template for rendering suggestion items.</summary>
    public Func<T, Element>? Template { get; init; }

    /// <summary>Error message to display when search fails.</summary>
    public string ErrorMessage { get; init; } = "Search failed. Please try again.";

    /// <summary>Message to display when search returns no results.</summary>
    public string EmptyMessage { get; init; } = "No results found.";
}

/// <summary>
/// DSL factory for AutoSuggest.
/// </summary>
public static class AutoSuggestDsl
{
    /// <summary>
    /// Creates an auto-suggest element with async search.
    /// </summary>
    public static AutoSuggestElement<T> AutoSuggest<T>(
        T? selected,
        Action<T?>? onSelected = null,
        Func<string, CancellationToken, Task<IReadOnlyList<T>>>? search = null,
        Func<T, string>? displayText = null,
        string? placeholderText = null,
        int debounceMs = 300,
        Func<T, Element>? template = null) =>
        new(selected, onSelected, search, displayText, placeholderText)
        {
            DebounceMs = debounceMs,
            Template = template
        };
}

/// <summary>
/// Manages async search with debounce and cancellation for AutoSuggest.
/// </summary>
public sealed class SearchManager<T> : IDisposable
{
    // SECURITY (TASK-098): all mutable state accessed under _lock so the
    // threadpool Timer callback and the UI-thread Search call can't race.
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Timer? _debounceTimer;
    private long _generation; // increments per Search; old continuations discard
    private readonly int _debounceMs;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<T>>> _search;
    private bool _disposed;

    public SearchState State { get; private set; } = SearchState.Idle;
    public IReadOnlyList<T> Results { get; private set; } = Array.Empty<T>();
    public string? ErrorText { get; private set; }

    /// <summary>Fired when state changes (for triggering re-renders).</summary>
    public event Action? StateChanged;

    public SearchManager(Func<string, CancellationToken, Task<IReadOnlyList<T>>> search, int debounceMs = 300)
    {
        _search = search;
        _debounceMs = debounceMs;
    }

    private void RaiseStateChanged()
    {
        var dq = global::Microsoft.UI.Reactor.ReactorApp.UIDispatcher;
        if (dq is not null && !dq.HasThreadAccess)
        {
            // SECURITY (TASK-098): marshal StateChanged onto the UI thread
            // so subscribers (typically components) don't race the renderer.
            var handler = StateChanged;
            if (handler is not null) dq.TryEnqueue(() => handler.Invoke());
            return;
        }
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Triggers a debounced search. Cancels any in-flight search.
    /// </summary>
    public void Search(string query)
    {
        CancellationToken token;
        long mygen;
        lock (_lock)
        {
            if (_disposed) return;
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            _cts?.Dispose();
            _debounceTimer?.Dispose();

            mygen = ++_generation;

            if (string.IsNullOrEmpty(query))
            {
                State = SearchState.Idle;
                Results = Array.Empty<T>();
                RaiseStateChanged();
                return;
            }

            _cts = new CancellationTokenSource();
            token = _cts.Token;

            _debounceTimer = new Timer(async _ =>
            {
                lock (_lock)
                {
                    if (_disposed || mygen != _generation) return;
                    State = SearchState.Loading;
                }
                RaiseStateChanged();

                IReadOnlyList<T>? results = null;
                Exception? failure = null;
                try
                {
                    results = await _search(query, token);
                    token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { failure = ex; }

                lock (_lock)
                {
                    if (_disposed || mygen != _generation) return;
                    if (failure is not null && !token.IsCancellationRequested)
                    {
                        State = SearchState.Error;
                        ErrorText = failure.Message;
                    }
                    else if (results is not null)
                    {
                        Results = results;
                        State = results.Count > 0 ? SearchState.Results : SearchState.Empty;
                    }
                }
                RaiseStateChanged();
            }, null, _debounceMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Cancels any in-flight search and resets state.
    /// </summary>
    public void Cancel()
    {
        lock (_lock)
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            _debounceTimer?.Dispose();
            _generation++;
            State = SearchState.Idle;
            Results = Array.Empty<T>();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            _cts?.Dispose();
            _debounceTimer?.Dispose();
            _cts = null;
            _debounceTimer = null;
        }
    }
}
