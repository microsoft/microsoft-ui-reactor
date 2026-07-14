using System;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Marks a method or property setter that must be invoked on the UI thread — the
/// annotated method (or the property's setter) calls
/// <see cref="ThreadAffinity.ThrowIfNotOnUIThread"/>, which throws
/// <see cref="InvalidOperationException"/> when reached from a background thread
/// once the UI dispatcher has been captured. (The guard is a no-op before the
/// first window bootstraps, while <see cref="Microsoft.UI.Reactor.ReactorApp.UIDispatcher"/>
/// is still null.) Property getters are not guarded, so the REACTOR_THREAD_001
/// analyzer flags writes to an annotated property, not reads.
/// </summary>
/// <remarks>
/// <para>
/// The marker exists for the <c>REACTOR_THREAD_001</c> analyzer
/// (<c>UIThreadAffinityAnalyzer</c>). In a consumer compilation the Reactor
/// framework is a metadata-only reference, so the analyzer cannot inspect a
/// callee's body for the runtime guard — <c>DeclaringSyntaxReferences</c> is
/// empty and IL is not available. A metadata-visible attribute is the committed
/// mechanism the analyzer keys off, so this type must be <see langword="public"/>.
/// </para>
/// <para>
/// This is a plain marker attribute with no members and no reflection surface,
/// so it is trim- and AOT-safe. Keep it in sync with the members that call
/// <see cref="ThreadAffinity.ThrowIfNotOnUIThread"/>; the analyzer reads the
/// attribute rather than a hard-coded member list.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
public sealed class UIThreadOnlyAttribute : Attribute
{
}
