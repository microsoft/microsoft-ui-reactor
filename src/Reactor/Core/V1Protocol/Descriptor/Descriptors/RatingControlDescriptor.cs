using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 1) — descriptor variant of the hand-coded
/// <c>MountRatingControl</c> / <c>UpdateRatingControl</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Value</c> — controlled (<c>ValueChanged</c> event,
///   <c>TypedEventHandler&lt;RatingControl, object&gt;</c> bridged to
///   <c>EventHandler&lt;object&gt;</c>, <c>OnValueChanged</c> callback).</item>
///   <item><c>MaxRating</c>, <c>IsReadOnly</c>, <c>PlaceholderValue</c>,
///   <c>InitialSetValue</c> — one-way.</item>
///   <item><c>Caption</c> — one-way; matches the hand-coded port's
///   <c>Caption ?? ""</c> coercion.</item>
/// </list></para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class RatingControlDescriptor
{
    public static readonly ControlDescriptor<RatingControlElement, WinUI.RatingControl> Descriptor =
        new ControlDescriptor<RatingControlElement, WinUI.RatingControl>
        {
            Children = new None<RatingControlElement, WinUI.RatingControl>(),
            GetSetters = static e => e.Setters,
        }
        .Controlled<double, object>(
            get:         static e => e.Value,
            set:         static (c, v) => c.Value = v,
            // See ToggleSwitchDescriptor for the closure / CWT-gate invariant.
            subscribe:   static (fe, h) => ((WinUI.RatingControl)fe).ValueChanged += (s, e) => h(s, e),
            unsubscribe: static (fe, h) => { /* trampoline lives for control lifetime — see CWT gate in PropEntry */ },
            callback:    static e => e.OnValueChanged,
            readBack:    static c => c.Value)
        .OneWay(
            get: static e => e.MaxRating,
            set: static (c, v) => c.MaxRating = v)
        .OneWay(
            get: static e => e.IsReadOnly,
            set: static (c, v) => c.IsReadOnly = v)
        .OneWay(
            get: static e => e.Caption ?? "",
            set: static (c, v) => c.Caption = v)
        .OneWay(
            get: static e => e.PlaceholderValue,
            set: static (c, v) => c.PlaceholderValue = v)
        .OneWay(
            get: static e => e.InitialSetValue,
            set: static (c, v) => c.InitialSetValue = v);
}
