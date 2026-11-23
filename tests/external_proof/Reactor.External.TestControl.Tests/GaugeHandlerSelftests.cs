using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Xunit;

namespace Reactor.External.TestControl.Tests;

/// <summary>
/// Issue #206 — hermetic (no-dispatcher) proof that the external Slider-shaped
/// <see cref="GaugeHandler"/> registers through the public extension path. The
/// live echo-suppression proof (user edit fires, framework write does not)
/// requires a real WinUI dispatcher and lives in
/// <c>Reactor.AppTests.Host</c> as
/// <c>Spec047ExternalProofFixtures.GaugeWriteSuppressedEcho</c>.
///
/// <para>The very fact that this file compiles — with no
/// <c>InternalsVisibleTo</c> from Microsoft.UI.Reactor.dll into this assembly — is the
/// proof that <see cref="ReactorBinding{TElement}.WriteSuppressed"/> and the
/// rest of the value-control surface are sufficient from outside the
/// framework.</para>
/// </summary>
public class GaugeHandlerSelftests
{
    [Fact]
    public void RegisterHandler_Succeeds_ForExternalValueControl()
    {
        var reconciler = new Reconciler();
        reconciler.RegisterHandler<GaugeElement, GaugeControl>(new GaugeHandler());
        // No exception — the external value-bearing handler is accepted.
    }

    [Fact]
    public void RegisterHandler_Twice_Throws()
    {
        var reconciler = new Reconciler();
        reconciler.RegisterHandler<GaugeElement, GaugeControl>(new GaugeHandler());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            reconciler.RegisterHandler<GaugeElement, GaugeControl>(new GaugeHandler()));

        Assert.Contains("GaugeElement", ex.Message);
    }

    [Fact(Skip = "Requires WinUI dispatcher; lives in AppTests.Host fixture Spec047ExternalProof_Gauge_WriteSuppressed")]
    public void Gauge_WriteSuppressed_NumericEchoSuppressed()
    {
        // See Spec047ExternalProofFixtures.GaugeWriteSuppressedEcho — a numeric
        // (Slider-shaped) value-bearing control suppressing its own write echo
        // through the public surface only.
    }
}
