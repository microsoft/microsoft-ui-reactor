using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Controls.AutoSuggestDsl;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

public class AutoSuggestTests
{
    /// <summary>
    /// Waits until the SearchManager reaches the specified state via its StateChanged event.
    /// </summary>
    private static Task WaitForState<T>(SearchManager<T> manager, SearchState target)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.StateChanged += () =>
        {
            if (manager.State == target)
                tcs.TrySetResult();
        };
        // Check after subscribing to avoid TOCTOU race
        if (manager.State == target)
            tcs.TrySetResult();
        // Generous budget: under thread-pool starvation on a contended CI runner
        // a 5s ceiling races debounce+continuation scheduling. 30s still surfaces
        // a genuine "state never reached" bug with a clear TimeoutException.
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    // ════════════════════════════════════════════════════════════════
    //  Element creation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void AutoSuggest_Creates_With_Defaults()
    {
        var el = AutoSuggest<string>(null, placeholder: "Search...");
        Assert.Null(el.Selected);
        Assert.Equal("Search...", el.Placeholder);
        Assert.Equal(300, el.DebounceMs);
    }

    [Fact]
    public void AutoSuggest_Creates_With_Selected()
    {
        var el = AutoSuggest("item1");
        Assert.Equal("item1", el.Selected);
    }

    [Fact]
    public void AutoSuggest_Custom_DebounceMs()
    {
        var el = AutoSuggest<string>(null, debounceMs: 500);
        Assert.Equal(500, el.DebounceMs);
    }

    [Fact]
    public void AutoSuggest_With_Template()
    {
        var el = AutoSuggest<string>(null,
            template: item => TextBlock(item));
        Assert.NotNull(el.Template);
    }

    // ════════════════════════════════════════════════════════════════
    //  SearchManager - debounce
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SearchManager_Debounce_Delays_Search()
    {
        var searchCalled = false;
        var manager = new SearchManager<string>(
            async (query, ct) =>
            {
                searchCalled = true;
                return new[] { "result" };
            },
            debounceMs: 50);

        manager.Search("test");
        Assert.False(searchCalled); // not called yet (debounce)

        await WaitForState(manager, SearchState.Results);
        Assert.True(searchCalled);

        manager.Dispose();
    }

    [Fact]
    public async Task SearchManager_Rapid_Typing_Cancels_Previous()
    {
        var searchCount = 0;
        var manager = new SearchManager<string>(
            async (query, ct) =>
            {
                await Task.Delay(50, ct);
                Interlocked.Increment(ref searchCount);
                return new[] { query };
            },
            debounceMs: 20);

        // Rapid typing
        manager.Search("a");
        manager.Search("ab");
        manager.Search("abc");

        await WaitForState(manager, SearchState.Results);

        // Only the last search should complete
        Assert.True(searchCount <= 1);

        manager.Dispose();
    }

    [Fact]
    public async Task SearchManager_Loading_State_During_Search()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<string>>();
        var manager = new SearchManager<string>(
            async (query, ct) => await tcs.Task,
            debounceMs: 10);

        manager.Search("test");
        await WaitForState(manager, SearchState.Loading);

        Assert.Equal(SearchState.Loading, manager.State);

        tcs.SetResult(new[] { "result" });
        await WaitForState(manager, SearchState.Results);

        Assert.Equal(SearchState.Results, manager.State);
        Assert.Single(manager.Results);

        manager.Dispose();
    }

    [Fact]
    public async Task SearchManager_Empty_State_On_No_Results()
    {
        var manager = new SearchManager<string>(
            async (query, ct) =>
            {
                await Task.Yield();
                return Array.Empty<string>();
            },
            debounceMs: 10);

        manager.Search("xyz");
        await WaitForState(manager, SearchState.Empty);

        Assert.Equal(SearchState.Empty, manager.State);
        Assert.Empty(manager.Results);

        manager.Dispose();
    }

    [Fact]
    public async Task SearchManager_Error_State_On_Exception()
    {
        var manager = new SearchManager<string>(
            async (query, ct) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("API down");
            },
            debounceMs: 10);

        manager.Search("test");
        await WaitForState(manager, SearchState.Error);

        Assert.Equal(SearchState.Error, manager.State);
        Assert.Equal("API down", manager.ErrorText);

        manager.Dispose();
    }

    [Fact]
    public void SearchManager_Cancel_Resets_State()
    {
        var manager = new SearchManager<string>(
            async (query, ct) => new[] { "result" },
            debounceMs: 1000);

        manager.Search("test");
        manager.Cancel();

        Assert.Equal(SearchState.Idle, manager.State);
        Assert.Empty(manager.Results);

        manager.Dispose();
    }

    [Fact]
    public void SearchManager_Empty_Query_Stays_Idle()
    {
        var manager = new SearchManager<string>(
            async (query, ct) => new[] { "result" },
            debounceMs: 10);

        manager.Search("");
        Assert.Equal(SearchState.Idle, manager.State);

        manager.Dispose();
    }

    // ════════════════════════════════════════════════════════════════
    //  Selection
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void AutoSuggest_Selection_Callback()
    {
        string? selectedItem = null;
        var el = AutoSuggest<string>(
            null,
            onSelected: item => selectedItem = item);

        el.OnSelected?.Invoke("chosen");
        Assert.Equal("chosen", selectedItem);
    }

    [Fact]
    public void AutoSuggest_DisplayText_Function()
    {
        var el = AutoSuggest<(string Name, int Id)>(
            default,
            displayText: item => item.Name);

        Assert.NotNull(el.DisplayText);
        Assert.Equal("John", el.DisplayText!(("John", 1)));
    }

    // ════════════════════════════════════════════════════════════════
    //  Dispose cancels
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SearchManager_Dispose_Cancels_InFlight()
    {
        var manager = new SearchManager<string>(
            async (query, ct) =>
            {
                await Task.Delay(5000, ct);
                return new[] { "result" };
            },
            debounceMs: 10);

        manager.Search("test");
        manager.Dispose(); // should cancel without throwing
    }
}
