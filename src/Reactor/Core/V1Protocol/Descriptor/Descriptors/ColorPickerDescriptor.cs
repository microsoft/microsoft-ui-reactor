using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 2) — descriptor variant of the hand-coded
/// <c>MountColorPicker</c> / <c>UpdateColorPicker</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Color</c> — controlled (<c>ColorChanged</c> event,
///   <c>TypedEventHandler&lt;ColorPicker, ColorChangedEventArgs&gt;</c>
///   bridged to <c>EventHandler&lt;ColorChangedEventArgs&gt;</c>,
///   <c>OnColorChanged</c> callback). Echo suppression on programmatic
///   <c>Color=</c> writes guards against the synchronous ColorChanged echo
///   that triggers the cross-row value-swap regression noted in the
///   hand-coded arm.</item>
///   <item>13 one-way props: <c>IsAlphaEnabled</c>,
///   <c>IsMoreButtonVisible</c>, <c>IsColorSpectrumVisible</c>,
///   <c>IsColorSliderVisible</c>, <c>IsColorChannelTextInputVisible</c>,
///   <c>IsHexInputVisible</c>, <c>ColorSpectrumShape</c>, <c>MinHue</c>,
///   <c>MaxHue</c>, <c>MinSaturation</c>, <c>MaxSaturation</c>,
///   <c>MinValue</c>, <c>MaxValue</c>.</item>
/// </list></para>
/// </summary>
internal static class ColorPickerDescriptor
{
    public static readonly ControlDescriptor<ColorPickerElement, WinUI.ColorPicker> Descriptor =
        new ControlDescriptor<ColorPickerElement, WinUI.ColorPicker>
        {
            Children = new None<ColorPickerElement, WinUI.ColorPicker>(),
            GetSetters = static e => e.Setters,
        }
        .Controlled<global::Windows.UI.Color, ColorChangedEventArgs>(
            get:         static e => e.Color,
            set:         static (c, v) => c.Color = v,
            // See ToggleSwitchDescriptor for the closure / CWT-gate invariant.
            subscribe:   static (fe, h) => ((WinUI.ColorPicker)fe).ColorChanged += (s, e) => h(s, e),
            unsubscribe: static (fe, h) => { /* trampoline lives for control lifetime — see CWT gate in PropEntry */ },
            callback:    static e => e.OnColorChanged,
            readBack:    static c => c.Color)
        .OneWay(
            get: static e => e.IsAlphaEnabled,
            set: static (c, v) => c.IsAlphaEnabled = v)
        .OneWay(
            get: static e => e.IsMoreButtonVisible,
            set: static (c, v) => c.IsMoreButtonVisible = v)
        .OneWay(
            get: static e => e.IsColorSpectrumVisible,
            set: static (c, v) => c.IsColorSpectrumVisible = v)
        .OneWay(
            get: static e => e.IsColorSliderVisible,
            set: static (c, v) => c.IsColorSliderVisible = v)
        .OneWay(
            get: static e => e.IsColorChannelTextInputVisible,
            set: static (c, v) => c.IsColorChannelTextInputVisible = v)
        .OneWay(
            get: static e => e.IsHexInputVisible,
            set: static (c, v) => c.IsHexInputVisible = v)
        .OneWay(
            get: static e => e.ColorSpectrumShape,
            set: static (c, v) => c.ColorSpectrumShape = v)
        .OneWay(
            get: static e => e.MinHue,
            set: static (c, v) => c.MinHue = v)
        .OneWay(
            get: static e => e.MaxHue,
            set: static (c, v) => c.MaxHue = v)
        .OneWay(
            get: static e => e.MinSaturation,
            set: static (c, v) => c.MinSaturation = v)
        .OneWay(
            get: static e => e.MaxSaturation,
            set: static (c, v) => c.MaxSaturation = v)
        .OneWay(
            get: static e => e.MinValue,
            set: static (c, v) => c.MinValue = v)
        .OneWay(
            get: static e => e.MaxValue,
            set: static (c, v) => c.MaxValue = v);
}
