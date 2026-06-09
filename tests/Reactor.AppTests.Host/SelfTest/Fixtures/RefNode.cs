using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed partial class RefNode : Control
{
    public RefNode()
    {
        IsTabStop = false;
        Width = 1;
        Height = 1;
    }

    public string NodeId { get; set; } = "";

    public static readonly DependencyProperty LeftProperty =
        DependencyProperty.Register(nameof(Left), typeof(FrameworkElement), typeof(RefNode), new PropertyMetadata(null));

    public FrameworkElement? Left
    {
        get => (FrameworkElement?)GetValue(LeftProperty);
        set => SetValue(LeftProperty, value);
    }

    public static readonly DependencyProperty RightProperty =
        DependencyProperty.Register(nameof(Right), typeof(FrameworkElement), typeof(RefNode), new PropertyMetadata(null));

    public FrameworkElement? Right
    {
        get => (FrameworkElement?)GetValue(RightProperty);
        set => SetValue(RightProperty, value);
    }

    public static readonly DependencyProperty UpProperty =
        DependencyProperty.Register(nameof(Up), typeof(FrameworkElement), typeof(RefNode), new PropertyMetadata(null));

    public FrameworkElement? Up
    {
        get => (FrameworkElement?)GetValue(UpProperty);
        set => SetValue(UpProperty, value);
    }

    public static readonly DependencyProperty DownProperty =
        DependencyProperty.Register(nameof(Down), typeof(FrameworkElement), typeof(RefNode), new PropertyMetadata(null));

    public FrameworkElement? Down
    {
        get => (FrameworkElement?)GetValue(DownProperty);
        set => SetValue(DownProperty, value);
    }

    public static readonly DependencyProperty ParentProperty =
        DependencyProperty.Register(nameof(Parent), typeof(FrameworkElement), typeof(RefNode), new PropertyMetadata(null));

    public new FrameworkElement? Parent
    {
        get => (FrameworkElement?)GetValue(ParentProperty);
        set => SetValue(ParentProperty, value);
    }

    public static readonly DependencyProperty PeerProperty =
        DependencyProperty.Register(nameof(Peer), typeof(FrameworkElement), typeof(RefNode), new PropertyMetadata(null));

    public FrameworkElement? Peer
    {
        get => (FrameworkElement?)GetValue(PeerProperty);
        set => SetValue(PeerProperty, value);
    }

    public IList<FrameworkElement> Related { get; } = new List<FrameworkElement>();
}

internal sealed record RefNodeElement(
    string NodeId,
    ElementRef<RefNode>? Left = null,
    ElementRef<RefNode>? Right = null,
    ElementRef<RefNode>? Up = null,
    ElementRef<RefNode>? Down = null,
    ElementRef<RefNode>? Parent = null,
    ElementRef<RefNode>? Peer = null,
    IReadOnlyList<ElementRef<RefNode>>? Related = null) : Element
{
    internal Action<RefNode>[] Setters { get; init; } = [];
}

internal static class RefNodeDescriptor
{
    public static readonly ControlDescriptor<RefNodeElement, RefNode> Descriptor =
        new ControlDescriptor<RefNodeElement, RefNode>
        {
            GetSetters = static e => e.Setters,
        }
        .OneWay(static e => e.NodeId, static (c, v) => c.NodeId = v)
        .Reference<RefNode>(get: static e => e.Left, set: static (c, t) => c.Left = t)
        .Reference<RefNode>(get: static e => e.Right, set: static (c, t) => c.Right = t)
        .Reference<RefNode>(get: static e => e.Up, set: static (c, t) => c.Up = t)
        .Reference<RefNode>(get: static e => e.Down, set: static (c, t) => c.Down = t)
        .Reference<RefNode>(get: static e => e.Parent, set: static (c, t) => c.Parent = t)
        .Reference<RefNode>(get: static e => e.Peer, set: static (c, t) => c.Peer = t)
        .ReferenceList<RefNode>(
            get: static e => e.Related,
            apply: static (c, list) =>
            {
                c.Related.Clear();
                foreach (var target in list)
                    c.Related.Add(target);
            });
}

internal sealed class RefNodeDescriptorHandler()
    : DescriptorHandler<RefNodeElement, RefNode>(RefNodeDescriptor.Descriptor);

internal static class RefNodeFactory
{
    static RefNodeFactory() =>
        ControlRegistry.Register<RefNodeElement, RefNode>(static () => new RefNodeDescriptorHandler());

    public static RefNodeElement Of(
        string nodeId,
        ElementRef<RefNode>? left = null,
        ElementRef<RefNode>? right = null,
        ElementRef<RefNode>? up = null,
        ElementRef<RefNode>? down = null,
        ElementRef<RefNode>? parent = null,
        ElementRef<RefNode>? peer = null,
        IReadOnlyList<ElementRef<RefNode>>? related = null) =>
        new(nodeId, left, right, up, down, parent, peer, related);
}

internal static class RefNodeElementExtensions
{
    public static RefNodeElement Left(this RefNodeElement e, ElementRef<RefNode> r) => e with { Left = r };

    public static RefNodeElement Right(this RefNodeElement e, ElementRef<RefNode> r) => e with { Right = r };

    public static RefNodeElement Up(this RefNodeElement e, ElementRef<RefNode> r) => e with { Up = r };

    public static RefNodeElement Down(this RefNodeElement e, ElementRef<RefNode> r) => e with { Down = r };

    public static RefNodeElement Parent(this RefNodeElement e, ElementRef<RefNode> r) => e with { Parent = r };

    public static RefNodeElement Peer(this RefNodeElement e, ElementRef<RefNode> r) => e with { Peer = r };

    public static RefNodeElement Related(this RefNodeElement e, IReadOnlyList<ElementRef<RefNode>> refs) =>
        e with { Related = refs };
}

internal static class RefNodeFixtures
{
    internal class Smoke(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? bRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                bRef = ctx.UseElementRef<RefNode>();
                var (showB, setShowB) = ctx.UseState(true);
                return VStack(
                    RefNodeFactory.Of("A", right: bRef),
                    showB ? RefNodeFactory.Of("B").Ref(bRef) : Empty(),
                    Button("ToggleB", () => setShowB(!showB)));
            });

            await Harness.Render();

            var linked = await Harness.WaitFor(() =>
            {
                var a = H.FindControl<RefNode>(n => n.NodeId == "A");
                var b = H.FindControl<RefNode>(n => n.NodeId == "B");
                return a is not null && b is not null
                    && ReferenceEquals(bRef?.Current, b)
                    && ReferenceEquals(a.Right, b);
            });
            H.Check("RefNode_Smoke_RightResolvesToMountedTarget", linked);

            var initialSubscriberCount = bRef?.Inner.CurrentChangedSubscriberCount ?? -1;
            H.Check("RefNode_Smoke_CellHasOneSubscriber", initialSubscriberCount == 1);

            H.ClickButton("ToggleB");
            await Harness.Render();

            var cleared = await Harness.WaitFor(() =>
            {
                var a = H.FindControl<RefNode>(n => n.NodeId == "A");
                var b = H.FindControl<RefNode>(n => n.NodeId == "B");
                return a is not null && b is null && a.Right is null && bRef?.Current is null;
            });
            H.Check("RefNode_Smoke_TargetUnmountClearsRight", cleared);
            H.Check("RefNode_Smoke_CellSubscriberSurvivesSourceUnmount", bRef?.Inner.CurrentChangedSubscriberCount == 1);
        }
    }
}
