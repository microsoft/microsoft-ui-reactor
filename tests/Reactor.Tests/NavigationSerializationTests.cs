using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Reactor.Navigation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Phase 6 tests: Navigation state snapshot/restore, deep linking, and related functionality.
/// </summary>
/// <remarks>
/// Reactor's <see cref="NavigationHandle{TRoute}.GetState"/> and <see cref="NavigationHandle{TRoute}.SetState"/>
/// expose a POCO <see cref="NavigationState{TRoute}"/> snapshot. These tests verify both the
/// POCO round-trip and that the snapshot is JSON-friendly (with the right property names
/// and polymorphism support) when the caller plugs in their own serializer.
/// </remarks>
public partial class NavigationSerializationTests
{
    // Polymorphic route hierarchy for JSON round-trip tests
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(Home), "home")]
    [JsonDerivedType(typeof(Detail), "detail")]
    [JsonDerivedType(typeof(Settings), "settings")]
    [JsonDerivedType(typeof(Profile), "profile")]
    private abstract record Route;
    private sealed record Home : Route;
    private sealed record Detail(int Id) : Route;
    private sealed record Settings : Route;
    private sealed record Profile(string Name) : Route;

    // App-side source-gen context — mirrors the AOT-safe pattern callers should use.
    [JsonSourceGenerationOptions]
    [JsonSerializable(typeof(NavigationState<Route>))]
    private partial class RouteJsonContext : JsonSerializerContext { }

    // ════════════════════════════════════════════════════════════════
    //  GetState — POCO snapshot
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetState_Returns_Current_Route()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        var state = handle.GetState();

        Assert.IsType<Home>(state.Current);
        Assert.Empty(state.BackStack);
        Assert.Empty(state.ForwardStack);
    }

    [Fact]
    public void GetState_Includes_Back_And_Forward_Stacks()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        handle.Navigate(new Detail(1));
        handle.Navigate(new Detail(2));
        handle.GoBack(); // Detail(2) in forward stack

        var state = handle.GetState();

        Assert.Single(state.BackStack); // [Home]
        Assert.Single(state.ForwardStack); // [Detail(2)]
        Assert.IsType<Home>(state.BackStack[0]);
        Assert.Equal(new Detail(2), state.ForwardStack[0]);
        Assert.Equal(new Detail(1), state.Current);
    }

    [Fact]
    public void GetState_RoundTrips_Through_SetState_POCO()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        handle.Navigate(new Detail(1));
        handle.Navigate(new Settings());
        handle.Navigate(new Profile("Alice"));
        handle.GoBack(); // Profile in forward stack

        var snapshot = handle.GetState();

        // Create a fresh stack and restore directly from the POCO
        var stack2 = new NavigationStack<Route>(new Home());
        var handle2 = new NavigationHandle<Route>(stack2);

        handle2.SetState(snapshot);

        Assert.Equal(handle.CurrentRoute, handle2.CurrentRoute);
        Assert.Equal(handle.BackStack, handle2.BackStack);
        Assert.Equal(handle.ForwardStack, handle2.ForwardStack);
    }

    [Fact]
    public void GetState_Snapshot_Serializes_To_Json_With_Default_Property_Names()
    {
        // The framework deliberately ships NavigationState<T> with no serializer
        // attributes — callers control naming via their own JsonSerializerOptions /
        // context. Default STJ emits the property names verbatim (PascalCase here).
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        handle.Navigate(new Detail(1));

        var snapshot = handle.GetState();
        var json = JsonSerializer.Serialize(snapshot, RouteJsonContext.Default.NavigationStateRoute);

        Assert.Contains("\"Current\"", json);
        Assert.Contains("\"BackStack\"", json);
        Assert.Contains("\"ForwardStack\"", json);
    }

    [Fact]
    public void SetState_With_Polymorphic_Route_Hierarchy_RoundTrips_Through_Json()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        handle.Navigate(new Detail(42));
        handle.Navigate(new Settings());

        // Caller picks JSON; framework just hands them the POCO.
        var json = JsonSerializer.Serialize(handle.GetState(), RouteJsonContext.Default.NavigationStateRoute);

        var stack2 = new NavigationStack<Route>(new Home());
        var handle2 = new NavigationHandle<Route>(stack2);
        var restored = JsonSerializer.Deserialize(json, RouteJsonContext.Default.NavigationStateRoute);
        Assert.NotNull(restored);
        handle2.SetState(restored);

        Assert.IsType<Settings>(handle2.CurrentRoute);
        Assert.Equal(2, handle2.BackStack.Count);
        Assert.IsType<Home>(handle2.BackStack[0]);
        Assert.Equal(new Detail(42), handle2.BackStack[1]);
    }

    [Fact]
    public void SetState_Fires_Navigated_Event_With_Mode_Reset()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);

        NavigationEventArgs<Route>? eventArgs = null;
        handle.Navigated += args => eventArgs = args;

        // Build some state in another handle
        var source = new NavigationStack<Route>(new Home());
        var sourceHandle = new NavigationHandle<Route>(source);
        sourceHandle.Navigate(new Detail(1));

        handle.SetState(sourceHandle.GetState());

        Assert.NotNull(eventArgs);
        Assert.Equal(NavigationMode.Reset, eventArgs!.Mode);
        Assert.Equal(new Detail(1), eventArgs.Route);
        Assert.Equal(new Home(), eventArgs.PreviousRoute);
    }

    [Fact]
    public void SetState_Fires_RouteChanged()
    {
        var stack = new NavigationStack<Route>(new Home());
        var handle = new NavigationHandle<Route>(stack);
        var iHandle = (INavigationHandle)handle;

        int changeCount = 0;
        iHandle.RouteChanged += () => changeCount++;

        var source = new NavigationStack<Route>(new Detail(1));
        var sourceHandle = new NavigationHandle<Route>(source);
        handle.SetState(sourceHandle.GetState());

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void SetState_Throws_For_Null_State()
    {
        var handle = new NavigationHandle<Route>(new NavigationStack<Route>(new Home()));
        Assert.Throws<ArgumentNullException>(() => handle.SetState(null!));
    }

    // ════════════════════════════════════════════════════════════════
    //  DeepLinkMap
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DeepLinkMap_Resolve_Matches_Simple_Pattern()
    {
        var map = new DeepLinkMap<Route>()
            .Map("/settings", _ => new Settings());

        var result = map.Resolve("/settings");

        Assert.True(result.Matched);
        Assert.Single(result.Routes);
        Assert.IsType<Settings>(result.Routes[0]);
    }

    [Fact]
    public void DeepLinkMap_Resolve_Extracts_Int_Parameter()
    {
        var map = new DeepLinkMap<Route>()
            .Map("/detail/{id:int}", args => new Detail(args.Get<int>("id")));

        var result = map.Resolve("/detail/42");

        Assert.True(result.Matched);
        Assert.Equal(new Detail(42), result.Routes[0]);
    }

    [Fact]
    public void DeepLinkMap_Resolve_Extracts_String_Parameter()
    {
        var map = new DeepLinkMap<Route>()
            .Map("/profile/{userId}", args => new Profile(args.Get<string>("userId")));

        var result = map.Resolve("/profile/alice");

        Assert.True(result.Matched);
        Assert.Equal(new Profile("alice"), result.Routes[0]);
    }

    [Fact]
    public void DeepLinkMap_Resolve_Returns_False_For_Unknown_URI()
    {
        var map = new DeepLinkMap<Route>()
            .Map("/settings", _ => new Settings());

        var result = map.Resolve("/unknown");

        Assert.False(result.Matched);
        Assert.Empty(result.Routes);
    }

    [Fact]
    public void DeepLinkMap_WithBackStack_Produces_Synthetic_BackStack()
    {
        var map = new DeepLinkMap<Route>()
            .Map("/detail/{id:int}", args => new Detail(args.Get<int>("id")),
                 () => new Route[] { new Home() });

        var result = map.Resolve("/detail/7");

        Assert.True(result.Matched);
        Assert.Equal(2, result.Routes.Length);
        Assert.IsType<Home>(result.Routes[0]); // Back stack
        Assert.Equal(new Detail(7), result.Routes[1]); // Current
    }

    [Fact]
    public void DeepLinkMap_Multiple_Patterns()
    {
        var map = new DeepLinkMap<Route>()
            .Map("/", _ => new Home())
            .Map("/settings", _ => new Settings())
            .Map("/detail/{id:int}", args => new Detail(args.Get<int>("id")))
            .Map("/profile/{name}", args => new Profile(args.Get<string>("name")));

        Assert.IsType<Home>(map.Resolve("/").Routes[0]);
        Assert.IsType<Settings>(map.Resolve("/settings").Routes[0]);
        Assert.Equal(new Detail(99), map.Resolve("/detail/99").Routes[0]);
        Assert.Equal(new Profile("bob"), map.Resolve("/profile/bob").Routes[0]);
        Assert.False(map.Resolve("/nonexistent").Matched);
    }

    [Fact]
    public void DeepLinkMap_Resolve_With_Uri_Object()
    {
        var map = new DeepLinkMap<Route>()
            .Map("/detail/{id:int}", args => new Detail(args.Get<int>("id")));

        var result = map.Resolve(new Uri("myapp://host/detail/42"));

        Assert.True(result.Matched);
        Assert.Equal(new Detail(42), result.Routes[0]);
    }

    [Fact]
    public void DeepLinkMap_Trims_Trailing_Slash()
    {
        var map = new DeepLinkMap<Route>()
            .Map("/settings", _ => new Settings());

        var result = map.Resolve("/settings/");

        Assert.True(result.Matched);
    }

    // ════════════════════════════════════════════════════════════════
    //  RouteArgs
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void RouteArgs_Get_Throws_For_Missing_Parameter()
    {
        var args = new RouteArgs(new Dictionary<string, string>());
        Assert.Throws<KeyNotFoundException>(() => args.Get<string>("missing"));
    }

    [Fact]
    public void RouteArgs_GetString_Returns_Null_For_Missing()
    {
        var args = new RouteArgs(new Dictionary<string, string>());
        Assert.Null(args.GetString("missing"));
    }
}
