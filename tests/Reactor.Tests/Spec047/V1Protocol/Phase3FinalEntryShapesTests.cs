using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol;

/// <summary>
/// Spec 047 §14 Phase 3-final — surface tests for the new entry shapes
/// (Immediate, CollectionDiffControlled, OneWayBridged) and the
/// extended Panel&lt;&gt;.PerChildAttached / new ItemsHost&lt;&gt; shapes.
///
/// These are pure data-surface tests — full descriptor execution against
/// a WinUI control runs in the AppTests.Host SelfTest fixtures that
/// Batches B-G will add per-control. The goal here is to lock the
/// builder signatures so subagent ports compile against them.
/// </summary>
public class Phase3FinalEntryShapesTests
{
    private record TestEl(string Label) : Element;

    [Fact]
    public void OneWayBridged_Builder_Adds_Entry()
    {
        var d = new ControlDescriptor<TestEl, WinUI.Button>()
            .OneWayBridged<string?>(
                get:         el => el.Label,
                set:         (c, v, rec, rr) => { /* would touch the control */ },
                shouldWrite: el => el.Label is not null);
        Assert.Single(d.Properties);
    }

    [Fact]
    public void Immediate_Builder_Adds_Entry()
    {
        var d = new ControlDescriptor<TestEl, WinUI.Button>()
            .Immediate<FakePayload>(
                callbackGate:      el => null,
                observeProperty:   FrameworkElement.TagProperty,
                observeCallback:   (_, _) => { },
                observeSlotIsNull: p => p.ObserveSlot is null,
                setObserveSlot:    (p, cb) => p.ObserveSlot = cb,
                loadedHook:        (_, _) => { });
        Assert.Single(d.Properties);
    }

    [Fact]
    public void CollectionDiffControlled_Builder_Adds_Entry()
    {
        var d = new ControlDescriptor<TestEl, WinUI.Button>()
            .CollectionDiffControlled<FakePayload, int, int, EventHandler>(
                get:             el => Array.Empty<int>(),
                getVector:       c => new List<int>(),
                key:             i => i,
                subscribe:       (c, h) => { },
                callbackPresent: el => null,
                trampoline:      (s, e) => { },
                slotIsNull:      p => p.ListSlot is null,
                setSlot:         (p, h) => p.ListSlot = h);
        Assert.Single(d.Properties);
    }

    [Fact]
    public void PanelStrategy_PerChildAttached_Optional()
    {
        var attached = new List<(UIElement, Element)>();
        var strategy = new Panel<TestEl, FrameworkElement>(
            GetChildren:   el => Array.Empty<Element>(),
            GetCollection: c => null!)
        {
            PerChildAttached = (ctrl, ui, child) => attached.Add((ui, child)),
        };
        Assert.NotNull(strategy.PerChildAttached);
    }

    [Fact]
    public void PanelStrategy_PerChildAttached_Defaults_Null()
    {
        var strategy = new Panel<TestEl, FrameworkElement>(
            GetChildren:   el => Array.Empty<Element>(),
            GetCollection: c => null!);
        Assert.Null(strategy.PerChildAttached);
    }

    [Fact]
    public void ItemsHost_ItemEquals_Defaults_Null()
    {
        var items = new object[] { "a", "b" };
        var strategy = new ItemsHost<TestEl, FrameworkElement>(
            GetItems:      el => items,
            GetCollection: c => new List<object>());
        Assert.Null(strategy.ItemEquals);
    }

    public sealed class FakePayload
    {
        public DependencyPropertyChangedCallback? ObserveSlot;
        public Delegate? ListSlot;
    }
}
