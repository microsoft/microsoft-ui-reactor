using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.ModifierMatrix;

/// <summary>
/// One (modifier slot, receiver) pair from <c>ModifierSchema</c>, as typed code: the generated
/// <see cref="ModifierProbes.All"/> table (tools/Reactor.ModifierGen) supplies one per row A–D
/// receiver, so the matrix fixture needs no reflection (spec 067 §8).
/// </summary>
/// <param name="Slot">The modifier field name.</param>
/// <param name="ReceiverType">The control type the schema says the modifier is written to.</param>
/// <param name="DpName">The DP the schema says it writes, as <c>Owner.NameProperty</c>.</param>
/// <param name="Dp">That DP. Activates WinUI, so call it on the UI thread.</param>
/// <param name="With">Sets the slot on a modifier bag (null unsets it).</param>
/// <param name="Converter">Row C: how the field value maps to the DP value.</param>
internal sealed record ModifierProbe(
    string Slot,
    Type ReceiverType,
    string DpName,
    Func<DependencyProperty> Dp,
    Func<ElementModifiers, object?, ElementModifiers> With,
    string? Converter);

/// <summary>The generated table lives in <c>ModifierProbes.Generated.cs</c>.</summary>
internal static partial class ModifierProbes;
