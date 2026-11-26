namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest;

/// <summary>
/// The modifier fields whose merge and skip-equality checks run in the
/// <c>ModifierFieldContract_BrushAndFont</c> selftest instead of headless, because a
/// <c>Brush</c> or <c>FontFamily</c> can't be constructed off the UI thread (spec 067 §10.1).
/// Linked into Reactor.Tests: <c>ModifierFieldContractTests</c> asserts this equals the
/// table's non-headless fields, and the fixture asserts it covers exactly these names.
/// </summary>
internal static class ModifierFieldContractSelfTestFields
{
    internal static readonly string[] Names = ["Background", "Foreground", "BorderBrush", "FontFamily"];
}
