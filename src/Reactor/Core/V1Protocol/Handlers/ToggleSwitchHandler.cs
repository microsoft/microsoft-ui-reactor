using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 Phase 1 (1.11) — first port onto the v1 handler protocol.
/// Vanilla value-bearing leaf: <see cref="WinUI.ToggleSwitch"/> with one
/// control-intrinsic event (<c>Toggled</c>) that round-trips against
/// <c>IsOn</c>.
///
/// <para><b>Event wiring (§9.2):</b> the Toggled trampoline lives in the
/// typed <see cref="ToggleSwitchEventPayload"/> slot on the control's
/// <see cref="ControlEventStateBox"/>. Allocated and subscribed exactly
/// once per control lifetime — pool rent/return preserves the box per the
/// new pool-reset contract, so subsequent mounts of the same control hit
/// the null-check fast path with zero allocations. Matches the legacy
/// <c>EnsureToggleSwitchWiring</c> shape (Reconciler.Mount.cs).</para>
///
/// <para><b>Echo handling:</b> the trampoline calls
/// <see cref="ChangeEchoSuppressor.ShouldSuppress(UIElement)"/> as its
/// first line so programmatic writes wrapped in
/// <see cref="ReactorBinding{TElement}.WriteSuppressed"/> drain a token
/// and short-circuit. Initial-value writes during Mount are bare —
/// nothing is subscribed yet to echo from.</para>
///
/// <para><b>§8.2 invariant:</b> <c>OnIsOnChangedFireCount = 0</c> for
/// <c>Set(...)</c>-driven writes — the engine's <c>ApplySetters</c> scope
/// (see <see cref="Reconciler.ApplySetters{T}"/>) takes care of it; this
/// handler does not add additional suppression around setters.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal sealed class ToggleSwitchHandler : IElementHandler<ToggleSwitchElement, WinUI.ToggleSwitch>
{
    // Static trampoline — captures nothing. Reads the live element via
    // GetElementTag on each fire (handles pool rent + element re-render).
    private static readonly RoutedEventHandler ToggledTrampoline = (s, _) =>
    {
        var ts = (WinUI.ToggleSwitch)s!;
        if (ChangeEchoSuppressor.ShouldSuppress(ts)) return;
        (Reconciler.GetElementTag(ts) as ToggleSwitchElement)?.OnIsOnChanged?.Invoke(ts.IsOn);
    };

    public WinUI.ToggleSwitch Mount(MountContext ctx, ToggleSwitchElement el)
    {
        var ctrl = ctx.RentControl<WinUI.ToggleSwitch>();

        // Bare initial-value writes — Toggled subscription happens after, so
        // the synchronous event has no trampoline to fire (no echo possible).
        if (ctrl.IsOn != el.IsOn) ctrl.IsOn = el.IsOn;
        ctrl.OnContent = el.OnContent;
        ctrl.OffContent = el.OffContent;
        if (el.Header is not null) ctrl.Header = el.Header;

        EnsureToggledWiring(ctrl, el);
        ctx.ApplySetters(el.Setters, ctrl);
        return ctrl;
    }

    public void Update(UpdateContext ctx, ToggleSwitchElement oldEl, ToggleSwitchElement newEl, WinUI.ToggleSwitch ctrl)
    {
        // V1HandlerAdapter refreshed ElementTag for us; the Toggled
        // subscription from Mount is still live and reads the new element
        // via GetElementTag.
        if (ctrl.IsOn != newEl.IsOn)
            ctx.BindFor(ctrl, newEl).WriteSuppressed(() => ctrl.IsOn = newEl.IsOn);
        if (oldEl.OnContent != newEl.OnContent) ctrl.OnContent = newEl.OnContent;
        if (oldEl.OffContent != newEl.OffContent) ctrl.OffContent = newEl.OffContent;
        if (newEl.Header is not null && !ReferenceEquals(oldEl.Header, newEl.Header))
            ctrl.Header = newEl.Header;

        // Lazy-wire on null→non-null callback transition (matches legacy
        // UpdateToggleSwitch's EnsureToggleSwitchWiring call).
        EnsureToggledWiring(ctrl, newEl);

        ctx.ApplySetters(newEl.Setters, ctrl);
    }

    // Gate the per-control event wiring on the user actually having
    // wired a callback — matches legacy EnsureToggleSwitchWiring's
    // `if (ts.OnIsOnChanged is null) return` early exit. Without this gate
    // every callback-less ToggleSwitch pays subscription cost that legacy
    // skips entirely (M4 / M5 dispatch suites).
    private static void EnsureToggledWiring(WinUI.ToggleSwitch ctrl, ToggleSwitchElement el)
    {
        if (el.OnIsOnChanged is null) return;
        var payload = Reconciler.GetOrCreateControlEventPayload<ToggleSwitchEventPayload>(ctrl);
        if (payload.ToggledTrampoline is null)
        {
            payload.ToggledTrampoline = ToggledTrampoline;
            ctrl.Toggled += ToggledTrampoline;
        }
    }

    public ChildrenStrategy<ToggleSwitchElement, WinUI.ToggleSwitch>? Children { get; } =
        new None<ToggleSwitchElement, WinUI.ToggleSwitch>();
}
