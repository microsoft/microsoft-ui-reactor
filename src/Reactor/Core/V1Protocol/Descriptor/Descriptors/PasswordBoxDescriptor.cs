using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 5) — descriptor variant of the hand-coded
/// <c>MountPasswordBox</c> / <c>UpdatePasswordBox</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Password</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   with a <c>RoutedEventHandler</c> trampoline. The trampoline gates on
///   <c>ChangeEchoSuppressor.ShouldSuppress(c)</c> — when Update's
///   programmatic write triggered PasswordChanged the suppressor short-
///   circuits before the user callback fires (mirrors the legacy arm).
///   <c>HandCodedControlled</c> itself wraps the programmatic write in
///   <c>WriteSuppressed</c>, which begins the suppressor token; the manual
///   check inside the trampoline preserves the legacy invariant that even
///   manual <c>BeginSuppress</c> calls (e.g. from a future setter that
///   coerces Password) still gate the echo.</item>
///   <item><c>PlaceholderText</c> — one-way (write empty string for null,
///   matching the legacy default).</item>
///   <item><c>Header</c>, <c>MaxLength</c>, <c>PasswordRevealMode</c>,
///   <c>PasswordChar</c> — one-way conditional per the legacy guards.</item>
/// </list></para>
/// </summary>
internal static class PasswordBoxDescriptor
{
    private static readonly RoutedEventHandler PasswordChangedTrampoline = (s, _) =>
    {
        var pb = (WinUI.PasswordBox)s!;
        if (ChangeEchoSuppressor.ShouldSuppress(pb)) return;
        (Reconciler.GetElementTag(pb) as PasswordBoxElement)?.OnPasswordChanged?.Invoke(pb.Password);
    };

    public static readonly ControlDescriptor<PasswordBoxElement, WinUI.PasswordBox> Descriptor =
        new ControlDescriptor<PasswordBoxElement, WinUI.PasswordBox>
        {
            Children = new None<PasswordBoxElement, WinUI.PasswordBox>(),
            GetSetters = static e => e.Setters,
        }
        .HandCodedControlled<PasswordBoxEventPayload, string, RoutedEventHandler>(
            get:         static e => e.Password,
            set:         static (c, v) => c.Password = v,
            readBack:    static c => c.Password,
            subscribe:   static (c, h) => c.PasswordChanged += h,
            callback:    static e => e.OnPasswordChanged,
            trampoline:  PasswordChangedTrampoline,
            slotIsNull:  static p => p.PasswordChangedTrampoline is null,
            setSlot:     static (p, h) => p.PasswordChangedTrampoline = h)
        .OneWay(
            get: static e => e.PlaceholderText ?? "",
            set: static (c, v) => c.PlaceholderText = v)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null)
        .OneWay(
            get: static e => e.MaxLength,
            set: static (c, v) => c.MaxLength = v)
        .OneWay(
            get: static e => e.PasswordRevealMode,
            set: static (c, v) => c.PasswordRevealMode = v)
        .OneWayConditional(
            get:         static e => e.PasswordChar,
            set:         static (c, v) => c.PasswordChar = v,
            shouldWrite: static e => e.PasswordChar is not null);
}
