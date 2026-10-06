using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Xunit;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Headless coverage for the component-inspection half of <see cref="ReactorDiagnostics"/>.
/// The UIElement → component lookup needs live wrapper controls and is covered by the
/// <c>Diagnostics_ComponentInspection</c> selftest; everything it delegates to (the text
/// snapshot of hooks, props and contexts, state edits from text, re-render, identity, the
/// modifier map, the reconciler registry) is pure-managed and pinned here.
/// </summary>
public class ReactorDiagnosticsComponentsTests
{
    private static readonly Context<string> ThemeName = new("light");

    private sealed class Renderer
    {
        public RenderContext Context { get; } = new();
        public int Rerenders;
        public ContextScope Scope { get; } = new();
        public void Begin() => Context.BeginRender(() => Rerenders++, Scope);
    }

    private static ComponentHandle ForContext(RenderContext ctx, Func<bool>? isAlive = null)
    {
        var node = new Reconciler.ComponentNode { Context = ctx, Element = new FuncElement(_ => new EmptyElement()) };
        return ComponentHandle.FromNode(node, isAlive ?? (() => true));
    }

    // ── Hook description ────────────────────────────────────────────

    [Fact]
    public void State_DescribesEveryPrimitiveHookInCallOrder_AsText()
    {
        var r = new Renderer();
        r.Scope.Push(new Dictionary<ContextBase, object?> { [ThemeName] = "dark" });
        r.Begin();
        r.Context.UseState(5);
        r.Context.UseReducer("a");
        r.Context.UseReducer<int, string>((s, a) => s + a.Length, 7);
        r.Context.UseRef(3);
        r.Context.UseMemo(() => "memo", 1);
        r.Context.UseEffect(() => { }, 1);
        r.Context.UseContext(ThemeName);

        var hooks = ForContext(r.Context).Describe().State;

        Assert.Equal(new[] { "state", "reducer", "reducer", "ref", "memo", "effect", "context" }, hooks.Select(h => h.Kind));
        Assert.Equal(Enumerable.Range(0, 7), hooks.Select(h => h.Index));
        // A ref hook reports what it points at (Ref<T>.Current), typed T.
        Assert.Equal(new[] { "5", "\"a\"", "7", "3", "\"memo\"", "", "\"dark\"" }, hooks.Select(h => h.Value));
        Assert.Equal(new[] { "int", "string", "int", "int", "string", "", "string" }, hooks.Select(h => h.Type));
        Assert.Equal(new[] { true, true, true, false, false, false, false }, hooks.Select(h => h.Editable));
        Assert.All(hooks, h => Assert.Equal("", h.Name));
        Assert.All(hooks, h => Assert.False(h.Redacted));
    }

    [Fact]
    public void State_PersistedCellIsEditable()
    {
        var r = new Renderer();
        r.Begin();
        r.Context.UsePersisted($"diag-{Guid.NewGuid():N}", 11, PersistedScope.Application);

        var hook = Assert.Single(ForContext(r.Context).Describe().State);
        Assert.Equal(("persisted", "11", "int", true), (hook.Kind, hook.Value, hook.Type, hook.Editable));
    }

    [Fact]
    public void State_NonTextValueIsReadableButNotEditable()
    {
        var r = new Renderer();
        r.Begin();
        r.Context.UseState(new List<int> { 1, 2, 3 });

        var hook = Assert.Single(ForContext(r.Context).Describe().State);
        Assert.Equal("List<int> (3 items)", hook.Value);
        Assert.False(hook.Editable);
    }

    [Fact]
    public void State_SecretTypeIsRedactedAndNotEditable()
    {
        var r = new Renderer();
        r.Begin();
        r.Context.UseState(new UserCredential("me", "hunter2"));
        r.Context.UseState<string?>("plain");

        var hooks = ForContext(r.Context).Describe().State;
        Assert.Equal(("<redacted>", true, false), (hooks[0].Value, hooks[0].Redacted, hooks[0].Editable));
        Assert.DoesNotContain("hunter2", hooks[0].Value);
        Assert.Equal(("\"plain\"", false, true), (hooks[1].Value, hooks[1].Redacted, hooks[1].Editable));
    }

    [Fact]
    public void Snapshot_IsDetached_FromLaterStateChanges()
    {
        var r = new Renderer();
        r.Begin();
        var (_, set) = r.Context.UseState(1);
        var handle = ForContext(r.Context);
        var before = handle.Describe();

        set(2);

        Assert.Equal("1", before.State[0].Value);
        Assert.Equal("2", handle.Describe().State[0].Value);
    }

    // ── State edits from text ───────────────────────────────────────

    [Fact]
    public void TrySetState_ParsesTextToTheHookType_WritesAndRequestsRerender()
    {
        var r = new Renderer();
        r.Begin();
        r.Context.UseState(1);

        Assert.True(ForContext(r.Context).TrySetState(0, "42", out var error), error);
        Assert.Null(error);
        Assert.Equal(1, r.Rerenders);

        r.Begin();
        Assert.Equal(42, r.Context.UseState(1).Value);
    }

    [Fact]
    public void TrySetState_EqualValue_AcceptsWithoutRerender()
    {
        var r = new Renderer();
        r.Begin();
        r.Context.UseState(9);

        Assert.True(ForContext(r.Context).TrySetState(0, "9", out _));
        Assert.Equal(0, r.Rerenders);
    }

    [Fact]
    public void TrySetState_ThreadSafeReducerAndPersistedCells_Write()
    {
        var r = new Renderer();
        var key = $"diag-{Guid.NewGuid():N}";
        r.Begin();
        r.Context.UseState("x", threadSafe: true);
        r.Context.UseReducer<int, string>((s, a) => s + a.Length, 0);
        r.Context.UsePersisted(key, "p", PersistedScope.Application);
        var handle = ForContext(r.Context);

        Assert.True(handle.TrySetState(0, "y", out _));
        Assert.True(handle.TrySetState(1, "100", out _));
        Assert.True(handle.TrySetState(2, "q", out _));
        Assert.Equal(3, r.Rerenders);

        r.Begin();
        Assert.Equal("y", r.Context.UseState("x", threadSafe: true).Value);
        Assert.Equal(100, r.Context.UseReducer<int, string>((s, a) => s + a.Length, 0).Value);
        Assert.Equal("q", r.Context.UsePersisted(key, "p", PersistedScope.Application).Value);
    }

    private enum Mode { Off, On }

    [Fact]
    public void TrySetState_NullablesEnumsAndNull()
    {
        var r = new Renderer();
        r.Begin();
        r.Context.UseState<string?>("s");
        r.Context.UseState<int?>(1);
        r.Context.UseState(Mode.Off);
        var handle = ForContext(r.Context);

        Assert.True(handle.TrySetState(0, "null", out _));
        Assert.True(handle.TrySetState(1, "null", out _));
        Assert.True(handle.TrySetState(2, "on", out _));
        Assert.Equal(new[] { "null", "null", "On" }, handle.Describe().State.Select(h => h.Value));
    }

    [Fact]
    public void TrySetState_RefusesWithAReason_WithoutWriting()
    {
        var r = new Renderer();
        r.Begin();
        r.Context.UseState(1);
        r.Context.UseRef(5);
        r.Context.UseEffect(() => { }, 1);
        r.Context.UseState(new List<int>());
        r.Context.UseState(new UserCredential("me", "pw"));
        r.Context.UseState("Server=db;Password=hunter2");
        var handle = ForContext(r.Context);

        AssertRefused(handle, 0, "abc", "'abc' is not a valid int");
        AssertRefused(handle, 0, "null", "'null' is not a valid int");
        AssertRefused(handle, 1, "6", "is a ref hook; only state, reducer and persisted hooks can be set");
        AssertRefused(handle, 2, "1", "is a effect hook");
        AssertRefused(handle, 3, "x", "a List<int> value cannot be typed as text");
        AssertRefused(handle, 3, "null", "a List<int> value cannot be typed as text");
        AssertRefused(handle, 4, "x", "holds a secret");
        AssertRefused(handle, 5, "x", "holds a secret");
        AssertRefused(handle, 6, "1", "has no hook 6");
        AssertRefused(handle, -1, "1", "has no hook -1");

        Assert.Equal("1", handle.Describe().State[0].Value);
        Assert.Equal(0, r.Rerenders);
    }

    private static void AssertRefused(ComponentHandle handle, int index, string text, string reason)
    {
        Assert.False(handle.TrySetState(index, text, out var error));
        Assert.NotNull(error);
        Assert.Contains(reason, error);
    }

    [Fact]
    public void TrySetState_RefusesAValueItsNextSnapshotWouldRedact()
    {
        var r = new Renderer();
        r.Begin();
        r.Context.UseState("plain");
        var handle = ForContext(r.Context);

        AssertRefused(handle, 0, "AccessToken=abc123", "looks like a secret; it is not written from diagnostics");

        Assert.Equal("\"plain\"", handle.Describe().State[0].Value);
        Assert.Equal(0, r.Rerenders);
        // An ordinary value on the same hook is still written.
        Assert.True(handle.TrySetState(0, "hello", out var error), error);
        Assert.Equal("\"hello\"", handle.Describe().State[0].Value);
    }

    [Fact]
    public void State_HoldingARef_IsAStateHook_NotARefHook()
    {
        var r = new Renderer();
        r.Begin();
        var box = new Ref<int>(4);
        r.Context.UseState(box);
        r.Context.UseRef(5);
        var handle = ForContext(r.Context);

        var hooks = handle.Describe().State;
        Assert.Equal(("state", "Ref<int>", false), (hooks[0].Kind, hooks[0].Type, hooks[0].Editable));
        Assert.Equal(("ref", "int", "5"), (hooks[1].Kind, hooks[1].Type, hooks[1].Value));
        // Refused for its type (a Ref<int> cannot be typed as text), not as though it came from UseRef.
        AssertRefused(handle, 0, "1", "a Ref<int> value cannot be typed as text");
        AssertRefused(handle, 1, "1", "is a ref hook");
        // reactor.state keeps naming a Ref<T> cell "useRef", as it always has.
        Assert.Equal(new[] { "useRef", "useRef" }, r.Context.SnapshotHooks().Select(s => s.Hook));
    }

    [Fact]
    public void Unmounted_RefusesWritesAndRerender()
    {
        var r = new Renderer();
        r.Begin();
        r.Context.UseState(1);
        var handle = ForContext(r.Context, isAlive: () => false);

        Assert.False(handle.TrySetState(0, "2", out var error));
        Assert.Contains("no longer mounted", error);
        Assert.False(handle.Rerender());
        Assert.Equal("1", handle.Describe().State[0].Value);
        Assert.Equal(0, r.Rerenders);
    }

    [Fact]
    public void Writes_OffTheRenderThread_Throw()
    {
        var r = new Renderer();
        r.Begin();
        r.Context.UseState(1);
        var handle = ForContext(r.Context);

        // A dedicated thread, not Task.Run: a pool task can be scheduled onto the very
        // pool thread that rendered, which would make this check pass vacuously.
        Exception? setError = null, rerenderError = null;
        var other = new global::System.Threading.Thread(() =>
        {
            setError = Record.Exception(() => handle.TrySetState(0, "2", out _));
            rerenderError = Record.Exception(() => handle.Rerender());
        });
        other.Start();
        other.Join();

        Assert.IsType<InvalidOperationException>(setError);
        Assert.IsType<InvalidOperationException>(rerenderError);
        Assert.Equal("1", handle.Describe().State[0].Value);
        Assert.Equal(0, r.Rerenders);
    }

    // ── Re-render ───────────────────────────────────────────────────

    [Fact]
    public void Rerender_InvokesTheComponentsOwnRerenderCallback()
    {
        var r = new Renderer();
        r.Begin();

        Assert.True(ForContext(r.Context).Rerender());
        Assert.Equal(1, r.Rerenders);
    }

    [Fact]
    public void Rerender_NeverRenderedContext_ReturnsFalse()
    {
        Assert.False(ForContext(new RenderContext()).Rerender());
    }

    // ── Contexts ────────────────────────────────────────────────────

    [Fact]
    public void Contexts_ReportProvidedAndDefaultValues()
    {
        var other = new Context<int>(4, "Other");
        var r = new Renderer();
        r.Scope.Push(new Dictionary<ContextBase, object?> { [ThemeName] = "dark" });
        r.Begin();
        r.Context.UseState(0);
        r.Context.UseContext(ThemeName);
        r.Context.UseContext(other);

        var contexts = ForContext(r.Context).Describe().Contexts;

        Assert.Equal(new[]
        {
            new DiagnosticValue(1, "ThemeName", "provided", "string", "\"dark\"", false, false, false),
            new DiagnosticValue(2, "Other", "default", "int", "4", false, false, false),
        }, contexts);
    }

    [Fact]
    public void Contexts_SecretNamedContextIsRedacted_InContextsAndState()
    {
        var apiSecret = new Context<string>("", "ApiSecret");
        var r = new Renderer();
        r.Scope.Push(new Dictionary<ContextBase, object?> { [apiSecret] = "s3cr3t" });
        r.Begin();
        r.Context.UseContext(apiSecret);

        var snapshot = ForContext(r.Context).Describe();
        Assert.True(snapshot.Contexts.Single().Redacted);
        Assert.True(snapshot.State.Single().Redacted);
        Assert.DoesNotContain("s3cr3t", snapshot.Contexts.Single().Value + snapshot.State.Single().Value);
    }

    private sealed class EqualsThrows
    {
        public override bool Equals(object? obj) => throw new InvalidOperationException("boom");
        public override int GetHashCode() => 0;
        public override string ToString() => "eq-throws";
    }

    [Fact]
    public void Contexts_AThrowingEqualsIsReportedProvided_InsteadOfFailing()
    {
        var ctx = new Context<EqualsThrows?>(null, "Odd");
        var r = new Renderer();
        r.Scope.Push(new Dictionary<ContextBase, object?> { [ctx] = new EqualsThrows() });
        r.Begin();
        r.Context.UseContext(ctx);

        var row = Assert.Single(ForContext(r.Context).Describe().Contexts);
        Assert.Equal(("provided", "eq-throws"), (row.Kind, row.Value));
    }

    [Fact]
    public void Contexts_NoContextHooks_ReturnsEmpty()
    {
        var r = new Renderer();
        r.Begin();
        r.Context.UseState(0);
        Assert.Empty(ForContext(r.Context).Describe().Contexts);
    }

    // ── Identity and props ──────────────────────────────────────────

    private sealed record CounterProps(int Start, string Label, string AdminPassword);

    private sealed class Counter : Component<CounterProps>
    {
        public override Element Render() => new EmptyElement();
    }

    private sealed class Plain : Component
    {
        public override Element Render() => new EmptyElement();
    }

    private sealed class Generic<T> : Component<int>
    {
        public override Element Render() => new EmptyElement();
    }

    [Fact]
    public void ClassComponent_ReportsNameKindAndLivePropsAsRows()
    {
        var component = new Counter { Props = new CounterProps(3, "go", "pw") };
        var element = new ComponentElement<CounterProps>(typeof(Counter), new CounterProps(1, "old", "x"));
        var node = new Reconciler.ComponentNode { Component = component, Element = element };

        var s = ComponentHandle.FromNode(node, () => true).Describe();

        Assert.Equal(("Counter", "class", false), (s.Name, s.Kind, s.IsRoot));
        // The instance's current props win over the element's (they diverge when a memoized
        // parent skips re-rendering this child); a *Password member is redacted.
        Assert.Equal(new[]
        {
            new DiagnosticValue(0, "Start", "prop", "int", "3", false, false, false),
            new DiagnosticValue(1, "Label", "prop", "string", "\"go\"", false, false, false),
            new DiagnosticValue(2, "AdminPassword", "prop", "string", "<redacted>", false, true, false),
        }, s.Props);
    }

    [Fact]
    public void ScalarProps_AreOneRowNamedProps_AndGenericNamesDropArity()
    {
        var node = new Reconciler.ComponentNode { Component = new Generic<string> { Props = 7 } };

        var s = ComponentHandle.FromNode(node, () => true).Describe();

        Assert.Equal("Generic", s.Name);
        Assert.Equal(new[] { new DiagnosticValue(0, "Props", "prop", "int", "7", false, false, false) }, s.Props);
    }

    [Fact]
    public void PropslessClassComponent_FallsBackToElementProps()
    {
        var element = new ComponentElement(typeof(Plain), "payload");
        var node = new Reconciler.ComponentNode { Component = new Plain(), Element = element };

        Assert.Equal("\"payload\"", ComponentHandle.FromNode(node, () => true).Describe().Props.Single().Value);
    }

    private sealed class ThrowingProps
    {
        public int Good => 1;
        public int Bad => throw new InvalidOperationException("boom");
    }

    private sealed class FieldOnlyProps(string password)
    {
        public readonly string AdminPassword = password;
        public override string ToString() => $"pw={AdminPassword}";
    }

    [Fact]
    public void Props_AThrowingGetterIsReportedUnavailable_NotFatal()
    {
        var node = new Reconciler.ComponentNode { Component = new Plain(), Element = new ComponentElement(typeof(Plain), new ThrowingProps()) };

        var props = ComponentHandle.FromNode(node, () => true).Describe().Props;

        Assert.Equal(new[]
        {
            new DiagnosticValue(0, "Good", "prop", "int", "1", false, false, false),
            new DiagnosticValue(1, "Bad", "prop", "int", "<value unavailable>", false, false, false),
        }, props);
    }

    [Fact]
    public void Props_WithNoReadableMembers_NameTheTypeAndNeverItsToString()
    {
        // The same fallback a trimmed app hits when property metadata is gone: the object's
        // own text could print a secret member, so it must not be used.
        var node = new Reconciler.ComponentNode { Component = new Plain(), Element = new ComponentElement(typeof(Plain), new FieldOnlyProps("hunter2")) };

        var row = ComponentHandle.FromNode(node, () => true).Describe().Props.Single();

        Assert.Equal(new DiagnosticValue(0, "Props", "prop", "FieldOnlyProps", "FieldOnlyProps (members unavailable)", false, false, false), row);
    }

    [Fact]
    public void State_ARecordWithASecretMemberIsRedacted()
    {
        var r = new Renderer();
        r.Begin();
        r.Context.UseState(new LoginState("me", "hunter2"));

        var hook = Assert.Single(ForContext(r.Context).Describe().State);
        Assert.Equal(("<redacted>", true, false), (hook.Value, hook.Redacted, hook.Editable));
    }

    private sealed record LoginState(string User, string Password);

    private static Element RenderList(RenderContext _) => new EmptyElement();

    [Fact]
    public void FunctionAndMemoComponents_AreNamedAfterTheirRenderMethod()
    {
        var func = ComponentHandle.FromNode(
            new Reconciler.ComponentNode { Context = new RenderContext(), Element = new FuncElement(RenderList) }, () => true).Describe();
        var memo = ComponentHandle.FromNode(
            new Reconciler.ComponentNode { Context = new RenderContext(), Element = new MemoElement(_ => new EmptyElement(), new object?[] { 1 }) }, () => true).Describe();

        Assert.Equal(("ReactorDiagnosticsComponentsTests.RenderList", "function"), (func.Name, func.Kind));
        Assert.Empty(func.Props);
        Assert.Equal("memo", memo.Kind);
        Assert.Equal("Memo in ReactorDiagnosticsComponentsTests.FunctionAndMemoComponents_AreNamedAfterTheirRenderMethod", memo.Name);
    }

    [Fact]
    public void FunctionRoot_IsRootNamedRender_AndTracksLiveness()
    {
        var ctx = new RenderContext();
        bool alive = true;
        var handle = ComponentHandle.FromRoot(new RootComponentSource(null, ctx, (Func<RenderContext, Element>)(_ => new EmptyElement()), null, null, () => alive));

        var s = handle.Describe();
        Assert.Equal(("function", true), (s.Kind, s.IsRoot));
        Assert.Equal("render in ReactorDiagnosticsComponentsTests.FunctionRoot_IsRootNamedRender_AndTracksLiveness", s.Name);
        Assert.True(handle.IsMounted);
        alive = false;
        Assert.False(handle.IsMounted);
    }

    [Fact]
    public void ClassRoot_ReportsNameAndProps()
    {
        var component = new Counter { Props = new CounterProps(8, "x", "y") };
        var s = ComponentHandle.FromRoot(new RootComponentSource(component, null, null, null, null, () => true)).Describe();

        Assert.Equal(("Counter", "class", true), (s.Name, s.Kind, s.IsRoot));
        Assert.Equal("8", s.Props[0].Value);
    }

    // ── Public surface holds no live objects ────────────────────────

    [Fact]
    public void PublicRecords_ExposeOnlyText_NumbersFlagsAndLists()
    {
        var allowed = new HashSet<Type> { typeof(string), typeof(int), typeof(bool), typeof(IReadOnlyList<DiagnosticValue>) };
        void AssertTextOnly(IEnumerable<PropertyInfo> props, string owner)
        {
            foreach (var p in props)
                Assert.True(allowed.Contains(p.PropertyType), $"{owner}.{p.Name} is {p.PropertyType.Name}");
        }
        AssertTextOnly(PublicProperties(typeof(ComponentSnapshot)), nameof(ComponentSnapshot));
        AssertTextOnly(PublicProperties(typeof(DiagnosticValue)), nameof(DiagnosticValue));
        AssertTextOnly(PublicProperties(typeof(AppliedProperty)), nameof(AppliedProperty));

        // A reference edge carries the mounted target control (a WinUI UIElement) and text.
        foreach (var p in PublicProperties(typeof(ReferenceEdgeSnapshot)))
            Assert.True(allowed.Contains(p.PropertyType) || p.PropertyType == typeof(UIElement),
                $"ReferenceEdgeSnapshot.{p.Name} is {p.PropertyType.Name}");
    }

    private static IEnumerable<PropertyInfo> PublicProperties(
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract");

    // ── Reactor.Devtools reactor.state shape (SnapshotHooks) ────────

    [Fact]
    public void SnapshotHooks_KeepsTheReactorStateShape()
    {
        var r = new Renderer();
        r.Scope.Push(new Dictionary<ContextBase, object?> { [ThemeName] = "dark" });
        r.Begin();
        r.Context.UseState(5, threadSafe: true);
        r.Context.UseReducer<int, string>((s, a) => s + a.Length, 7);
        var cell = r.Context.UseRef(3);
        r.Context.UseMemo(() => "memo", 1);
        r.Context.UseEffect(() => { }, 1);
        r.Context.UseContext(ThemeName);

        var snap = r.Context.SnapshotHooks();

        Assert.Equal(new[] { "useState", "useState", "useRef", "useMemo", "useEffect", "useContext" }, snap.Select(s => s.Hook));
        // A ref is reported as its Ref<T> box, as it always was — unlike the text surface,
        // which reports Ref.Current.
        Assert.Same(cell, snap[2].Value);
        Assert.Equal(typeof(Ref<int>), snap[2].ValueType);
        Assert.Equal(new object?[] { 5, 7 }, snap.Take(2).Select(s => s.Value));
        Assert.Equal(typeof(int), snap[0].ValueType);
        Assert.Equal(("dark", typeof(string)), (snap[5].Value, snap[5].ValueType));
        Assert.Null(snap[4].ValueType);
    }

    // ── Reconciler registry ─────────────────────────────────────────

    [Fact]
    public void Reconciler_RegistersOnConstruction_AndUnregistersOnDispose()
    {
        var reconciler = new Reconciler();
        Assert.True(reconciler.IsRegisteredForDiagnostics);

        reconciler.Dispose();
        Assert.False(reconciler.IsRegisteredForDiagnostics);
    }

    // ── Applied modifier map ────────────────────────────────────────

    [Fact]
    public void AppliedModifierMap_ResolvesOwnerByControlType_LikeApplyModifiers()
    {
        var m = new ElementModifiers
        {
            Width = 10,
            IsVisible = false,
            Padding = new Thickness(2),
            IsEnabled = false,
            AutomationName = "Go",
            Accessibility = new AccessibilityModifiers { HelpText = "help" },
        };

        var onButton = AppliedModifierMap.Describe(m, typeof(WinUI.Button));
        Assert.Equal(
            new[]
            {
                new AppliedModifier("Padding", "Control.Padding", new Thickness(2)),
                new AppliedModifier("Width", "FrameworkElement.Width", 10d),
                new AppliedModifier("IsVisible", "UIElement.Visibility", false),
                new AppliedModifier("IsEnabled", "Control.IsEnabled", false),
                new AppliedModifier("AutomationName", "AutomationProperties.Name", "Go"),
                new AppliedModifier("Accessibility.HelpText", "AutomationProperties.HelpText", "help"),
            },
            onButton);

        // A Border is not a Control: padding goes to Border.Padding and IsEnabled is
        // skipped, exactly as ApplyModifiers skips it.
        var onBorder = AppliedModifierMap.Describe(m, typeof(WinUI.Border));
        Assert.Contains(new AppliedModifier("Padding", "Border.Padding", new Thickness(2)), onBorder);
        Assert.DoesNotContain(onBorder, p => p.Modifier == "IsEnabled");
    }

    [Fact]
    public void AppliedProperty_ValuesAreText()
    {
        var m = new ElementModifiers { Width = 10.5, IsVisible = false, AutomationName = "Go", Opacity = 0.25 };

        var text = AppliedModifierMap.Describe(m, typeof(WinUI.Button)).Select(ReactorDiagnostics.ToText).ToList();

        Assert.Contains(new AppliedProperty("Width", "FrameworkElement.Width", "10.5"), text);
        Assert.Contains(new AppliedProperty("IsVisible", "UIElement.Visibility", "False"), text);
        Assert.Contains(new AppliedProperty("AutomationName", "AutomationProperties.Name", "Go"), text);
        Assert.Contains(new AppliedProperty("Opacity", "UIElement.Opacity", "0.25"), text);
    }

    [Fact]
    public void AppliedProperty_ReferenceModifiersFormatAsTheirRef()
    {
        var typed = new Microsoft.UI.Reactor.Input.ElementRef<WinUI.Button>(new Microsoft.UI.Reactor.Input.ElementRef());
        var m = new ElementModifiers
        {
            XYFocusRightRef = typed,
            ToolTipPlacementTargetRef = new Microsoft.UI.Reactor.Input.ElementRef(),
            Accessibility = new AccessibilityModifiers { LabeledByRef = typed },
        };

        var text = AppliedModifierMap.Describe(m, typeof(WinUI.Button)).Select(ReactorDiagnostics.ToText).ToList();

        Assert.Contains(new AppliedProperty("XYFocusRightRef", "UIElement.XYFocusRight", "ElementRef<Button>"), text);
        Assert.Contains(new AppliedProperty("ToolTipPlacementTargetRef", "ToolTipService.PlacementTarget", "ElementRef"), text);
        Assert.Contains(new AppliedProperty("Accessibility.LabeledByRef", "AutomationProperties.LabeledBy", "ElementRef<Button>"), text);
    }

    [Fact]
    public void AppliedModifierMap_RichToolTipShadowsStringToolTip()
    {
        var rich = new EmptyElement();
        var both = AppliedModifierMap.Describe(new ElementModifiers { ToolTip = "t", RichToolTip = rich }, typeof(WinUI.Button));
        var plain = AppliedModifierMap.Describe(new ElementModifiers { ToolTip = "t" }, typeof(WinUI.Button));

        Assert.Equal(new[] { new AppliedModifier("RichToolTip", "ToolTipService.ToolTip", rich) }, both);
        Assert.Equal(new[] { new AppliedModifier("ToolTip", "ToolTipService.ToolTip", "t") }, plain);
        Assert.Equal("EmptyElement", ReactorDiagnostics.ToText(both[0]).Value);
    }

    [Fact]
    public void AppliedModifierMap_AttachedFlyoutFollowsResolveFlyoutSlot()
    {
        var flyout = new EmptyElement();
        var m = new ElementModifiers { AttachedFlyout = flyout };

        Assert.Equal("SplitButton.Flyout", AppliedModifierMap.Describe(m, typeof(WinUI.SplitButton)).Single().Property);
        Assert.Equal("Button.Flyout", AppliedModifierMap.Describe(m, typeof(WinUI.Button)).Single().Property);
        Assert.Equal("FlyoutBase.AttachedFlyout", AppliedModifierMap.Describe(m, typeof(WinUI.TextBlock)).Single().Property);
    }

    [Fact]
    public void AppliedModifierMap_EmptyModifiers_ReportsNothing()
    {
        Assert.Empty(AppliedModifierMap.Describe(new ElementModifiers(), typeof(WinUI.Button)));
    }

    /// <summary>
    /// Members of <see cref="ElementModifiers"/> that are deliberately not in the map
    /// because applying them is not a dependency-property write. A new modifier must be
    /// added either to <see cref="AppliedModifierMap.Entries"/> or here.
    /// </summary>
    private static readonly HashSet<string> NotPropertyWrites = new(StringComparer.Ordinal)
    {
        "Layout", "Visual", "Accessibility",
        "Scale", "Rotation", "Translation", "CenterPoint",
        "OnMountAction", "OnUnmountAction",
        "Pan", "Pinch", "Rotate", "LongPress", "DragSource", "DropTarget",
        "Ref", "Backdrop",
        // Element-level dictionaries applied by their own paths, not single DPs.
        "ThemeBindings", "ResourceOverrides", "ContextValues",
    };

    private static IEnumerable<string> PublicModifierNames(
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Where(p => !typeof(Delegate).IsAssignableFrom(p.PropertyType))
            .Select(p => p.Name);

    [Fact]
    public void AppliedModifierMap_CoversEveryModifier_OrExcludesItOnPurpose()
    {
        var mapped = AppliedModifierMap.Entries.Select(e => e.Modifier).ToHashSet(StringComparer.Ordinal);

        var unaccounted = PublicModifierNames(typeof(ElementModifiers))
            .Where(n => !mapped.Contains(n) && !NotPropertyWrites.Contains(n))
            .Concat(PublicModifierNames(typeof(AccessibilityModifiers))
                .Where(n => !mapped.Contains("Accessibility." + n)))
            .ToList();

        Assert.True(unaccounted.Count == 0,
            "Modifiers neither mapped in AppliedModifierMap nor listed in NotPropertyWrites: " + string.Join(", ", unaccounted));

        var stale = mapped.Where(n => n.StartsWith("Accessibility.", StringComparison.Ordinal)
                ? typeof(AccessibilityModifiers).GetProperty(n["Accessibility.".Length..]) is null
                : typeof(ElementModifiers).GetProperty(n) is null)
            .ToList();
        Assert.True(stale.Count == 0, "AppliedModifierMap names no longer on the modifier records: " + string.Join(", ", stale));
    }
}

/// <summary>A credential-like state value: its type name marks it a secret.</summary>
internal sealed record UserCredential(string User, string Password);
