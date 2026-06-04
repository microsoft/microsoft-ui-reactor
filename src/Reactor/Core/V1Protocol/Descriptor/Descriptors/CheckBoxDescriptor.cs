using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Descriptor-backed CheckBox handler. <c>IsChecked</c> uses
/// <see cref="Optional{T}"/> authority so <c>Unset</c> leaves the WinUI control
/// in charge; explicit <c>true</c>, <c>false</c>, or <c>null</c> values are
/// asserted through the controlled path.
/// </summary>
internal static class CheckBoxDescriptor
{
    public static readonly ControlDescriptor<CheckBoxElement, WinUI.CheckBox> Descriptor =
        new ControlDescriptor<CheckBoxElement, WinUI.CheckBox>
        {
            Children = new None<CheckBoxElement, WinUI.CheckBox>(),
            GetSetters = static e => e.Setters,
        }
        .Controlled<bool?, RoutedEventArgs>(
            get:         static e => e.IsChecked,
            set:         static (c, v) => c.IsChecked = v,
            subscribe:   static (fe, h) =>
            {
                var cb = (WinUI.CheckBox)fe;
                cb.Checked       += (s, e) => h(s, e);
                cb.Unchecked     += (s, e) => h(s, e);
                cb.Indeterminate += (s, e) => h(s, e);
            },
            unsubscribe: static (fe, h) => { /* trampolines live for control lifetime — see CWT gate in PropEntry */ },
            callback:    static e => GetCheckedCallback(e),
            readBack:    static c => c.IsChecked)
        .OneWayConditional(
            get:         static e => e.Label,
            set:         static (c, v) => c.Content = v,
            shouldWrite: static e => e.Label is not null)
        .OneWay(
            get: static e => e.IsThreeState,
            set: static (c, v) => c.IsThreeState = v);

    private static Action<bool?>? GetCheckedCallback(CheckBoxElement element)
    {
        if (element.OnCheckedStateChanged is not null)
            return element.OnCheckedStateChanged;
        if (element.OnIsCheckedChanged is null)
            return null;
        return value =>
        {
            if (value.HasValue)
                element.OnIsCheckedChanged(value.Value);
        };
    }
}

internal sealed class CheckBoxDescriptorHandler()
    : DescriptorHandler<CheckBoxElement, WinUI.CheckBox>(CheckBoxDescriptor.Descriptor);
