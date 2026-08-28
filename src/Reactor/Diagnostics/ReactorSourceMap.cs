using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Diagnostics;

/// <summary>
/// Spec 010 — runtime switch and read path for Reactor source mapping.
///
/// <para><b>Why a runtime flag at all.</b> Interceptors are baked in at compile
/// time, so without a runtime gate every Debug build would pay the stamping cost
/// whether or not anyone is inspecting. This flag is what the emitted interceptor
/// checks before writing a location, so an un-inspected Debug session allocates
/// nothing extra.</para>
///
/// <para><b>It does not change control tagging.</b> The reconciler attaches a
/// <c>ReactorState</c> (the control → element back-pointer) only to controls that
/// something will read back — callbacks, a key, extras, or reference modifiers
/// (see <c>Reconciler.NeedsTag</c>), which is the allocation win PR #468 landed.
/// <c>NeedsTag</c> has no arm for this flag: a stamped element carries its
/// <c>CallSite</c> in the <c>Extensions</c> bucket and so already satisfies the
/// existing <c>Extensions is not null</c> test. Adding one would only tag
/// <em>unstamped</em> elements, which have no location to return, while
/// re-introducing the per-leaf allocation. Elements the generator does not reach
/// (wrapper factories, bare-string children) therefore stay untagged and report
/// no location rather than a wrong one.</para>
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
    /// type, and <see cref="Enabled"/> is read by every generated factory
    /// interceptor, where an unhoistable class-init check would show up.</para>
    /// </summary>
    private static int s_enabled =
        IsEnabledByEnvironment(global::System.Environment.GetEnvironmentVariable("REACTOR_SOURCEMAP")) ? 1 : 0;

    /// <summary>
    /// The <c>REACTOR_SOURCEMAP</c> contract: exactly <c>"1"</c> enables, anything else
    /// (including <c>"true"</c>, <c>"0"</c>, empty and unset) does not.
    ///
    /// <para>Factored out so the contract can be tested directly. Asserting it through
    /// <see cref="Enabled"/> instead would be testing a mutable process-global that this
    /// very environment variable is allowed to initialize to true — the test would fail
    /// under its own supported opt-in, and could also observe another class's write.</para>
    /// </summary>
    internal static bool IsEnabledByEnvironment(string? value)
        => string.Equals(value, "1", StringComparison.Ordinal);

    /// <summary>
    /// True when source mapping is active for this process. Read by every generated
    /// factory interceptor before it stamps, so it is an <c>int</c> + <c>Volatile</c>
    /// rather than a lock.
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
    /// <para>Returns <c>null</c> when the control was not produced by Reactor, or when
    /// the element behind it carries no <see cref="Element.CallSite"/> — because it was
    /// built while <see cref="Enabled"/> was false, or because no provider reaches that
    /// factory (see the known limitations in the source-mapping guide). Note this is a
    /// property of the ELEMENT, not of tagging: a control can be tagged for unrelated
    /// reasons (callbacks, a key, other extras) and still report no location, and an
    /// element hand-stamped with a <c>CallSite</c> reports one even with the flag
    /// off.</para>
    /// </summary>
    public static SourceLocation? GetSource(UIElement control)
        => Reconciler.GetElementTag(control)?.CallSite;
}
