namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Per-render allocation byte-gate targets for the leaf-element microbenchmarks
/// (spec 047 §11.6). Each constant is the post-optimization target ceiling for
/// the steady-state managed bytes allocated by a single re-render of the named
/// leaf scenario, derived from the measured pre-optimization baseline × 0.4
/// (the §11.6 budget reduction goal):
///
/// <list type="bullet">
///   <item><description>M1 — TextBlock, no callbacks: baseline ~1018 B → target 407 B.</description></item>
///   <item><description>M2 — ToggleSwitch, 1 callback: baseline ~3800 B → target 1520 B.</description></item>
///   <item><description>M3 — Button + 2 pointer modifiers (3 callbacks): baseline ~48000 B → target 19200 B.</description></item>
/// </list>
///
/// These are the landed <em>target</em> constants only. The actual measurement
/// harness and gate enforcement are ARM64-baseline-blocked and deferred to
/// spec 047 §4.9 (perf validation); there is no gate-check wired up yet.
/// </summary>
// TODO(spec-047 §4.9): these constants are consumed only once the byte-gate
// harness is wired (ARM64-baseline-blocked). They will read as dead code until
// then — do not remove in a dead-code sweep; they are the agreed target
// ceilings the harness asserts against.
internal static class PerformanceBudgets
{
    /// <summary>
    /// M1 byte-gate target: TextBlock leaf with no callbacks.
    /// Spec 047 §11.6 measured baseline ~1018 B × 0.4 = 407 B.
    /// </summary>
    internal const int ByteGate_LeafNoCallbacks = 407;

    /// <summary>
    /// M2 byte-gate target: ToggleSwitch leaf with one callback.
    /// Spec 047 §11.6 measured baseline ~3800 B × 0.4 = 1520 B.
    /// </summary>
    internal const int ByteGate_LeafOneCallback = 1520;

    /// <summary>
    /// M3 byte-gate target: Button leaf with two pointer modifiers (three callbacks).
    /// Spec 047 §11.6 measured baseline ~48000 B × 0.4 = 19200 B.
    /// </summary>
    internal const int ByteGate_LeafThreeCallbacks = 19200;
}
