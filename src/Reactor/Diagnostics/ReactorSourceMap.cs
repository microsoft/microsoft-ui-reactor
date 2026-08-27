using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Diagnostics;

/// <summary>
/// Spec 010 — runtime switch and read path for Reactor source mapping.
///
/// <para><b>Why a runtime flag at all.</b> The reconciler only attaches a
/// <c>ReactorState</c> (the control → element back-pointer) to controls that
/// something will read back — callbacks, a key, extras, or reference modifiers
/// (see <c>Reconciler.NeedsTag</c>). Display leaves such as
/// <c>TextBlock</c>/<c>Border</c>/<c>StackPanel</c>/<c>Image</c> stay untagged,
/// which is exactly the allocation win PR #468 landed. Source mapping needs the
/// back-pointer on <em>every</em> control, so it flips that gate — but only
/// while it is on. With the flag off, <c>NeedsTag</c> evaluates identically to
/// before (one extra static bool read, zero extra allocation).</para>
///
/// <para><b>Who turns it on.</b> The devtools session switch. <c>ReactorApp</c>
/// sets <see cref="Enabled"/> when the process was launched with
/// <c>--devtools app</c> / <c>--devtools run</c>, so a retail session never
/// pays. It is public-settable so a host that embeds its own inspector (or a
/// test) can opt in without going through the CLI.</para>
/// </summary>
public static class ReactorSourceMap
{
    /// <summary>
    /// Seeded from the <c>REACTOR_SOURCEMAP</c> environment variable so a
    /// process that does not go through the devtools CLI (a benchmark host, a
    /// harness, a one-off repro) can still turn source mapping on.
    ///
    /// <para>Deliberately a field INITIALIZER and not a static constructor:
    /// declaring an explicit cctor would strip <c>beforefieldinit</c> from this
    /// type, and <see cref="Enabled"/> is read on the mount hot path via
    /// <c>Reconciler.NeedsTag</c>, where an unhoistable class-init check would
    /// show up.</para>
    /// </summary>
    private static int s_enabled =
        string.Equals(
            global::System.Environment.GetEnvironmentVariable("REACTOR_SOURCEMAP"),
            "1",
            StringComparison.Ordinal) ? 1 : 0;

    /// <summary>
    /// True when source mapping is active for this process. Read on the mount
    /// hot path, so it is an <c>int</c> + <c>Volatile</c> rather than a lock.
    /// </summary>
    public static bool Enabled
    {
        get => Volatile.Read(ref s_enabled) != 0;
        set => Volatile.Write(ref s_enabled, value ? 1 : 0);
    }

    /// <summary>
    /// Resolves the DSL call site that produced <paramref name="control"/>.
    ///
    /// <para>This is the whole read chain, and it deliberately reuses the
    /// back-pointer that already exists rather than adding a second attached
    /// property: <c>UIElement</c> → <c>ReactorAttached.StateProperty</c> →
    /// <c>ReactorState.Element</c> → <see cref="Element.CallSite"/>.</para>
    ///
    /// <para>Returns <c>null</c> when the control was not produced by Reactor,
    /// when it was mounted while <see cref="Enabled"/> was false and therefore
    /// never tagged, or when no source-map provider stamped the element.</para>
    /// </summary>
    public static SourceLocation? GetSource(UIElement control)
        => Reconciler.GetElementTag(control)?.CallSite;
}
