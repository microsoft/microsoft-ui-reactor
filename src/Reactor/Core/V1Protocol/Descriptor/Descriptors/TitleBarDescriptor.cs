using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 deferred specialized controls — descriptor variant of
/// the hand-coded TitleBar mount/update arms.
/// </summary>
internal static class TitleBarDescriptor
{
    private static readonly NamedSlots<TitleBarElement, WinUI.TitleBar> ChildrenStrategy =
        new NamedSlots<TitleBarElement, WinUI.TitleBar>(new[]
        {
            new NamedSlot<TitleBarElement, WinUI.TitleBar>(
                Name: "Content",
                GetChild: static e => e.Content,
                SetChild: static (c, ui) => c.Content = ui)
            {
                GetCurrentChild = static c => c.Content as UIElement,
            },
            new NamedSlot<TitleBarElement, WinUI.TitleBar>(
                Name: "RightHeader",
                GetChild: static e => e.RightHeader,
                SetChild: static (c, ui) => c.RightHeader = ui)
            {
                GetCurrentChild = static c => c.RightHeader as UIElement,
            },
        });

    private static readonly TypedEventHandler<WinUI.TitleBar, object>
        BackRequestedTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as TitleBarElement)?.OnBackRequested?.Invoke();

    private static readonly TypedEventHandler<WinUI.TitleBar, object>
        PaneToggleRequestedTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as TitleBarElement)?.OnPaneToggleRequested?.Invoke();

    public static readonly ControlDescriptor<TitleBarElement, WinUI.TitleBar> Descriptor =
        new ControlDescriptor<TitleBarElement, WinUI.TitleBar>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Title,
            set: static (c, v) => c.Title = v)
        .OneWayConditional(
            get:         static e => e.Subtitle,
            set:         static (c, v) => c.Subtitle = v!,
            shouldWrite: static e => e.Subtitle is not null)
        .OneWay(
            get: static e => e.IsBackButtonVisible,
            set: static (c, v) => c.IsBackButtonVisible = v)
        .OneWay(
            get: static e => e.IsBackButtonEnabled,
            set: static (c, v) => c.IsBackButtonEnabled = v)
        .OneWay(
            get: static e => e.IsPaneToggleButtonVisible,
            set: static (c, v) => c.IsPaneToggleButtonVisible = v)
        .OneWay(
            get: static e => e.Icon,
            set: static (c, v) => c.IconSource = IconResolver.ResolveIconSource(v))
        .Imperative(
            mount: static (c, _) => RegisterWindowTitleBar(c),
            update: static (_, _, _) => { })
        .HandCodedEvent<TitleBarEventPayload,
            TypedEventHandler<WinUI.TitleBar, object>>(
            subscribe:        static (c, h) => c.BackRequested += h,
            callbackPresent:  static e => e.OnBackRequested,
            trampoline:       BackRequestedTrampoline,
            slotIsNull:       static p => p.BackRequestedTrampoline is null,
            setSlot:          static (p, h) => p.BackRequestedTrampoline = h)
        .HandCodedEvent<TitleBarEventPayload,
            TypedEventHandler<WinUI.TitleBar, object>>(
            subscribe:        static (c, h) => c.PaneToggleRequested += h,
            callbackPresent:  static e => e.OnPaneToggleRequested,
            trampoline:       PaneToggleRequestedTrampoline,
            slotIsNull:       static p => p.PaneToggleRequestedTrampoline is null,
            setSlot:          static (p, h) => p.PaneToggleRequestedTrampoline = h);

    // Issue #511 / regression from PR #455: ExtendsContentIntoTitleBar must
    // flip BEFORE the WinUI TitleBar's own Loaded handler runs
    // UpdatePaddingsForCaptionButtons() — otherwise AppWindow.TitleBar.RightInset
    // is still 0 when it's queried, RightPaddingColumn stays at 0 width, and
    // RightHeader content draws on top of the system caption buttons.
    // The legacy MountTitleBar path applied these synchronously before tree
    // attach; mirror that here in the Imperative mount lambda rather than
    // deferring to Loaded.
    private static void RegisterWindowTitleBar(WinUI.TitleBar titleBar)
    {
        if (Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal is { } host)
        {
            var explicitValue = host.OwningWindow?.Spec.ExtendsContentIntoTitleBar;
            if (explicitValue == false) return;
            if (explicitValue is null)
                host.Window.ExtendsContentIntoTitleBar = true;
            host.Window.SetTitleBar(titleBar);
        }
    }
}

/// <summary>
/// Spec 048 §7 — thin <see cref="DescriptorHandler{TElement,TControl}"/>
/// subclass so the Reactor.Factories DSL can reach this descriptor via
/// the <c>Reg&lt;&gt;</c> registration touch without leaking
/// <c>DescriptorHandler&lt;,&gt;</c> as a public surface.
/// </summary>
internal sealed class TitleBarDescriptorHandler() : DescriptorHandler<TitleBarElement, WinUI.TitleBar>(TitleBarDescriptor.Descriptor);