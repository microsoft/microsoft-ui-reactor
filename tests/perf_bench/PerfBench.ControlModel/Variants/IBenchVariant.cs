using Microsoft.UI.Xaml;
using Microsoft.UI.Reactor.Core;

namespace PerfBench.ControlModel.Variants;

/// <summary>
/// Spec 047 §15.2 three-way baselining contract. Every M-series bench
/// exists in three implementations:
///
///  - <see cref="Direct"/>      — raw WinUI hand-written, no Reactor.
///  - <see cref="ReactorToday"/> — current `main`-branch Reactor dispatch.
///  - <see cref="ReactorV2"/>   — work-in-progress new control model.
///
/// At Phase-0 freeze, ReactorV2 ≡ ReactorToday; the V2 implementation
/// delegates so V2 numbers ≈ Today numbers and Phase 1+ work shows up as
/// the delta.
/// </summary>
public enum BenchVariant
{
    Direct,
    ReactorToday,
    ReactorV2,
    /// <summary>Spec 047 §14 Phase 2 (Q1 spike) — descriptor interpreter
    /// variant. Same v1 protocol + dispatch shell as <see cref="ReactorV2"/>,
    /// but the three ported controls (ToggleSwitch / Slider / Border) are
    /// driven by a declarative <c>ControlDescriptor</c> + interpreter
    /// instead of hand-coded <c>IElementHandler</c> bodies. Any delta vs.
    /// <see cref="ReactorV2"/> is the interpreter's tax. Drives the §13 Q1
    /// decision matrix.</summary>
    ReactorDescriptors,
}

/// <summary>
/// One bench scenario (M1..M13). Encapsulates the per-variant
/// implementations. Each bench is responsible for any setup, the timed
/// section, and the per-iteration teardown.
/// </summary>
public interface IBench
{
    /// <summary>Bench identifier per spec §15.3 (e.g., "M1").</summary>
    string Id { get; }

    /// <summary>Short human label (e.g., "Mount_Leaf_NoCallback").</summary>
    string Name { get; }

    /// <summary>
    /// Run one iteration of the bench in the given variant. Called inside a
    /// repetition loop; allocations/timing are aggregated by the harness.
    /// Receives a parent <see cref="Panel"/> children list and a Reconciler
    /// so the bench can mount real UIElements when needed.
    /// </summary>
    void RunOne(BenchVariant variant, BenchContext ctx);

    /// <summary>
    /// Mount a representative final state for visual verification. Default
    /// implementation runs <see cref="RunOne"/> once but leaves the mounted
    /// UI in the parent (instead of the mount/unmount churn that perf
    /// timing requires). Override when the default leaves the parent empty.
    /// </summary>
    void DemoMount(BenchVariant variant, BenchContext ctx)
    {
        ctx.Iteration = 0;
        RunOne(variant, ctx);
    }
}

/// <summary>
/// Per-bench context — a parent panel, a reconciler (for ReactorToday /
/// ReactorV2 paths), and a scratch state slot.
/// </summary>
public sealed class BenchContext
{
    public required Microsoft.UI.Xaml.Controls.Panel Parent { get; init; }
    public required Reconciler Reconciler { get; init; }

    /// <summary>
    /// Iteration index inside the current rep — useful for benches that
    /// need to vary input (e.g., M4's cold-dispatch scan across element
    /// types).
    /// </summary>
    public int Iteration;

    /// <summary>
    /// Scratch slot for benches that need to thread state across
    /// iterations (e.g., M5's warm-pre-mount cache, M12's pool fixture).
    /// </summary>
    public object? Scratch;
}
