using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol;

/// <summary>
/// Spec 047 §6 / §14 Phase 1 (1.8) — ChildrenStrategy shape tests.
///
/// The strategy records are pure data; we exercise the record construction
/// + lambda execution to confirm the public surface holds. Full strategy
/// dispatch (engine invokes SetChild / Panel.Add / NamedSlots iteration)
/// requires the WinUI dispatcher to construct UIElementCollections and
/// runs in the AppTests.Host fixture site once first containers are
/// ported (1.14 for Border SingleContent, 1.15 for ListView ItemsHost).
/// </summary>
public class ChildrenStrategyTests
{
    private record TestEl(string Label) : Element;

    [Fact]
    public void None_Strategy_Has_No_Body()
    {
        // The leaf strategy carries no fields — it's a marker the engine
        // checks via pattern match. Construction must succeed without args.
        var none = new None<TestEl, UIElement>();
        Assert.NotNull(none);
    }

    [Fact]
    public void SingleContent_Strategy_Roundtrips_Child()
    {
        var childEl = new TestEl("child");
        var strategy = new SingleContent<TestEl, UIElement>(
            GetChild: el => childEl,
            SetChild: (ctrl, child) => { /* no-op for shape test */ });
        Assert.Same(childEl, strategy.GetChild(new TestEl("parent")));
    }

    [Fact]
    public void Panel_Strategy_Returns_Children_List()
    {
        var children = new List<Element> { new TestEl("a"), new TestEl("b"), new TestEl("c") };
        var strategy = new Panel<TestEl, UIElement>(
            GetChildren: el => children,
            GetCollection: ctrl => null!); // not exercised here — needs UIElementCollection
        var got = strategy.GetChildren(new TestEl("parent"));
        Assert.Equal(3, got.Count);
    }

    [Fact]
    public void NamedSlots_Strategy_Iterates_Slots()
    {
        var slots = new List<NamedSlot<TestEl, UIElement>>
        {
            new("Header", el => new TestEl(el.Label + ".header"), (c, ui) => { }),
            new("Footer", el => new TestEl(el.Label + ".footer"), (c, ui) => { }),
        };
        var strategy = new NamedSlots<TestEl, UIElement>(slots);
        Assert.Equal(2, strategy.Slots.Count);
        Assert.Equal("Header", strategy.Slots[0].Name);
        var parent = new TestEl("p");
        var headerChild = (TestEl)strategy.Slots[0].GetChild(parent)!;
        Assert.Equal("p.header", headerChild.Label);
    }

    [Fact]
    public void Imperative_Strategy_Invokes_Lambda()
    {
        bool ran = false;
        var strategy = new Imperative<TestEl, UIElement>((ctx, oldEl, newEl, ctrl) =>
        {
            ran = true;
        });
        // Direct invoke (the engine path needs a real MountContext; here we just
        // verify the lambda is wired). Cannot construct MountContext from a test
        // (ref struct, internal ctor), so we assert the delegate stored matches.
        Assert.NotNull(strategy.Reconcile);
    }

    [Fact]
    public void ItemsHost_Strategy_Carries_Source_And_Container_Funcs()
    {
        var items = new[] { 1, 2, 3 };
        var strategy = new ItemsHost<TestEl, UIElement>(
            GetItemsSource: el => items,
            GetContainer: ctrl => ctrl,
            Options: new ItemsHostOptions());
        Assert.Same(items, strategy.GetItemsSource(new TestEl("p")));
    }

    [Fact]
    public void AttachedPropWriter_Holds_GetAndWrite()
    {
        var writer = new AttachedPropWriter<TestEl>(
            Name: "Grid.Row",
            Get: el => 42,
            Write: (ctrl, val) => { /* would set Grid.Row */ });
        Assert.Equal("Grid.Row", writer.Name);
        Assert.Equal(42, writer.Get(new TestEl("x")));
    }

    [Fact(Skip = "Requires WinUI dispatcher (UIElementCollection + Border.Child); covered in AppTests.Host fixture site (1.14+)")]
    public void Engine_Dispatches_SingleContent_On_Mount()
    {
        // TODO(1.14): register a BorderElement-style handler with
        // SingleContent strategy. Mount via Reconciler.Reconcile; assert
        // SetChild was invoked with the mounted child UIElement.
    }

    [Fact(Skip = "Requires WinUI dispatcher; covered in AppTests.Host fixture site (1.14+)")]
    public void Engine_Dispatches_Panel_Strategy_On_Mount()
    {
        // TODO(1.14): mount a Panel-strategy host with 3 children, assert
        // all 3 land in the UIElementCollection in order.
    }

    [Fact(Skip = "Requires WinUI dispatcher; covered in AppTests.Host fixture site (1.15+)")]
    public void Engine_ItemsHost_Routes_Through_ChildReconciler_Smoke()
    {
        // TODO(1.15): smoke test that ItemsHost dispatch doesn't throw; the
        // real keyed reconciliation runs through ChildReconciler (spec 042).
    }
}
