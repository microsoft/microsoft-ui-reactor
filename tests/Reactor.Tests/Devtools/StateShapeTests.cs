using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Shape tests for <c>reactor.state</c>. Exercises the two contracts that
/// matter most for the tool's safety story: primitives pass through, complex
/// values return <c>{ $type, $shape }</c> without leaking values (spec §12).
/// End-to-end hook-table reads need a rendered component and live in the
/// self-host fixture (§3.11).
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: reflection-based System.Text.Json serialization of devtools/MCP state-shape payload objects (no source-gen context; some via DevtoolsMcpServer.JsonOpts). Issue #70 documents this devtools JSON surface as RUC/RDC-by-design and not-yet-AOT-clean; the standard `dotnet test` path is JIT (never trimmed). Behaviour-neutral.")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test-only: reflection-based System.Text.Json serialization of devtools/MCP state-shape payloads (see the IL2026 note). Standard test run is JIT, not AOT-compiled. Behaviour-neutral.")]
public class StateShapeTests
{
    [Theory]
    [InlineData(42)]
    [InlineData(3.14)]
    [InlineData("hello")]
    [InlineData(true)]
    [InlineData(false)]
    public void PrimitivesPassThroughUnchanged(object primitive)
    {
        var shaped = DevtoolsStateTool.ShapeValue(primitive);
        // Primitives shape to a JSON literal that round-trips to the same value.
        Assert.Equal(JsonSerializer.Serialize(primitive), shaped!.ToJsonString());
    }

    [Fact]
    public void NullStaysNull()
    {
        Assert.Null(DevtoolsStateTool.ShapeValue(null));
    }

    [Fact]
    public void EnumRendersAsString()
    {
        var shaped = DevtoolsStateTool.ShapeValue(SampleEnum.Second);
        Assert.Equal("Second", shaped!.GetValue<string>());
    }

    [Fact]
    public void ComplexObject_ReturnsTypeAndShape_NotValues()
    {
        var person = new Person { Name = "Kim", Age = 40, Secret = "do-not-leak" };
        var shaped = DevtoolsStateTool.ShapeValue(person);

        var obj = Assert.IsType<JsonObject>(shaped);
        Assert.True(obj.ContainsKey("$type"));
        Assert.True(obj.ContainsKey("$shape"));

        var json = shaped!.ToJsonString();
        // Type name is present — agents can reason about it.
        Assert.Contains("Person", json);
        // Property names are present as shape metadata (object keys are authored
        // verbatim, bypassing any property-naming policy).
        Assert.Contains("\"Name\":\"String\"", json);
        Assert.Contains("\"Age\":\"Int32\"", json);
        // Actual values never leak — the string "Kim" and "do-not-leak" must be absent.
        Assert.DoesNotContain("Kim", json);
        Assert.DoesNotContain("do-not-leak", json);
        Assert.DoesNotContain("40", json);
    }

    [Fact]
    public void Collection_ReturnsCountAndType_NotContents()
    {
        var list = new List<Person>
        {
            new() { Name = "A", Age = 1, Secret = "s1" },
            new() { Name = "B", Age = 2, Secret = "s2" },
        };
        var shaped = DevtoolsStateTool.ShapeValue(list);

        Assert.IsType<JsonObject>(shaped);
        var json = shaped!.ToJsonString();

        Assert.Contains("List", json);
        Assert.Contains("\"kind\":\"collection\"", json);
        Assert.Contains("\"count\":2", json);
        // Per-element contents must not leak.
        Assert.DoesNotContain("\"A\"", json);
        Assert.DoesNotContain("\"B\"", json);
        Assert.DoesNotContain("s1", json);
    }

    [Fact]
    public void BuildPayload_WithNullRoot_ThrowsNotReady()
    {
        var ex = Assert.Throws<McpToolException>(() => DevtoolsStateTool.BuildPayload(null));
        Assert.Equal(JsonRpcErrorCodes.ToolExecution, ex.Code);
        Assert.Contains("not-ready", JsonSerializer.Serialize(ex.Payload));
    }

    [Fact]
    public void BuildPayload_EmptyComponent_ReturnsEmptyHooksArray()
    {
        // A component that never calls a hook should report an empty hooks
        // list — exercising the snapshot pipeline without any hook cells.
        var c = new EmptyComponent();
        var payload = DevtoolsStateTool.BuildPayload(c);

        var json = JsonSerializer.Serialize(payload, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"hooks\":[]", json);
    }

    // §3.13 exit criterion: the state tool works for every hook type Reactor
    // ships. `SnapshotHooks` in RenderContext handles:
    //   useState, useRef (both via ValueHookState<T>),
    //   useMemo (MemoHookState<T>),
    //   usePersisted (PersistedHookState<T>),
    //   useEffect (EffectHookState),
    //   useContext (ContextHookState),
    //   useNavigationLifecycle (NavigationLifecycleHookState).
    // We mount a component calling every one and assert each shows up with
    // the expected `hook` name — any future additional hook type should fail
    // this test until SnapshotHooks is taught to recognize it, which keeps
    // the state tool honest about "every hook" coverage.
    [Fact]
    public void BuildPayload_OneOfEveryHookType_AllNamesRepresented()
    {
        var comp = new AllHooksComponent();
        var scope = new ContextScope();
        comp.Context.BeginRender(() => { }, scope);
        _ = comp.Render();
        comp.Context.FlushEffects();

        var payload = DevtoolsStateTool.BuildPayload(comp);
        var json = JsonSerializer.Serialize(payload, DevtoolsMcpServer.JsonOpts);

        using var doc = JsonDocument.Parse(json);
        var hookNames = doc.RootElement
            .GetProperty("hooks")
            .EnumerateArray()
            .Select(h => h.GetProperty("hook").GetString()!)
            .ToArray();

        Assert.Contains("useState", hookNames);
        Assert.Contains("useRef", hookNames);
        Assert.Contains("useMemo", hookNames);
        Assert.Contains("useEffect", hookNames);
        Assert.Contains("usePersisted", hookNames);
        Assert.Contains("useContext", hookNames);
        Assert.Contains("useNavigationLifecycle", hookNames);
    }

    private enum SampleEnum { First, Second, Third }

    private sealed class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public string Secret { get; set; } = "";
    }

    private sealed class EmptyComponent : Component
    {
        public override Element Render() => null!;
    }

    private static readonly Context<string> ThemeCtx = new("light");

    // Exercises every hook type SnapshotHooks knows how to name. Keep this
    // list in sync with RenderContext.SnapshotHooks — adding a new hook type
    // should add a call here AND a SnapshotHooks arm, so `BuildPayload_OneOfEveryHookType_*`
    // starts failing until both are done.
    private sealed class AllHooksComponent : Component
    {
        public override Element Render()
        {
            _ = UseState(0);
            _ = UseRef(0);
            _ = UseMemo(() => 42, Array.Empty<object>());
            UseEffect(() => { }, Array.Empty<object>());
            _ = UsePersisted("persist-key", 1);
            _ = UseContext(ThemeCtx);
            UseNavigationLifecycle(onNavigatedTo: _ => { });
            return null!;
        }
    }
}
