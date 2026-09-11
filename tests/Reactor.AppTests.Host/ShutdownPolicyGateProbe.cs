using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.AppTests.Host;

/// <summary>
/// Issue #1204 — the negative half of the ownership gate in
/// <c>ReactorApp.SyncDispatcherShutdownMode</c>: when Reactor does <b>not</b>
/// own the <see cref="Application"/>, setting
/// <see cref="ReactorApp.ShutdownPolicy"/> must leave that app's
/// <see cref="Application.DispatcherShutdownMode"/> alone.
/// </summary>
/// <remarks>
/// <para>The gate exists because an app embedding <c>ReactorHostControl</c> in
/// its own <see cref="Application"/> never calls <c>Application.Start</c>, so
/// the platform has already defaulted its mode to
/// <see cref="DispatcherShutdownMode.OnExplicitShutdown"/>. Writing
/// <see cref="DispatcherShutdownMode.OnLastWindowClose"/> there would make the
/// host process exit when its last window closes — a lifetime change Reactor has
/// no business making. <c>ReactorHost</c> also seeds
/// <see cref="ReactorApp.UIDispatcher"/> in exactly that scenario, so the
/// dispatcher check upstream of the gate does not stand in for it.</para>
/// <para>This needs its own process and its own mode because the gate's
/// condition is <c>Application.Current is ReactorApplication</c>: inside the
/// selftest host that is always true, so no in-process fixture can reach the
/// branch. Here a plain <see cref="Application"/> subclass owns the process
/// instead.</para>
/// <para>The seeded mode is deliberately the one Reactor would write for the
/// policy under test's <i>opposite</i>, so a gate that failed open would flip it
/// and be caught. <c>GATE-VIOLATED</c> is therefore a reachable outcome, not a
/// branch that can only ever print success.</para>
/// </remarks>
internal static partial class ShutdownPolicyGateProbe
{
    internal const string Flag = "--shutdown-policy-gate-probe";

    internal const int GateHeldExitCode = 43;
    internal const int GateViolatedExitCode = 44;
    internal const int InconclusiveExitCode = 46;

    internal const string GateHeldMarker = "GATE-HELD";
    internal const string GateViolatedMarker = "GATE-VIOLATED";

    private static int _exitCode = InconclusiveExitCode;

    public static int Run()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start(_ =>
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcher));

            // A plain Application — NOT ReactorApplication. This is the whole
            // point of the probe.
            var app = new EmbedderApplication();

            dispatcher.TryEnqueue(() =>
            {
                try
                {
                    Console.WriteLine($"OWNER {Application.Current.GetType().FullName}");

                    if (Application.Current is ReactorApplication)
                    {
                        // Would make every assertion below meaningless.
                        Console.WriteLine("INCONCLUSIVE Application.Current is a ReactorApplication");
                        _exitCode = InconclusiveExitCode;
                        return;
                    }

                    // Stand in for the embedder having seeded the dispatcher, as
                    // ReactorHost's constructor does for ReactorHostControl.
                    ReactorApp.UIDispatcher = dispatcher;

                    // OnLastWindowClose is what Reactor writes for the DEFAULT
                    // policy, so seeding it here means a gate that failed open
                    // would visibly flip it to OnExplicitShutdown below.
                    Application.Current.DispatcherShutdownMode = DispatcherShutdownMode.OnLastWindowClose;
                    var seeded = Application.Current.DispatcherShutdownMode;
                    Console.WriteLine($"SEEDED {seeded}");

                    if (seeded != DispatcherShutdownMode.OnLastWindowClose)
                    {
                        Console.WriteLine("INCONCLUSIVE seed did not take");
                        _exitCode = InconclusiveExitCode;
                        return;
                    }

                    ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;

                    var after = Application.Current.DispatcherShutdownMode;
                    Console.WriteLine($"AFTER {after} policy={ReactorApp.ShutdownPolicy}");

                    if (after == DispatcherShutdownMode.OnLastWindowClose)
                    {
                        Console.WriteLine($"{GateHeldMarker} embedder mode untouched");
                        _exitCode = GateHeldExitCode;
                    }
                    else
                    {
                        Console.WriteLine($"{GateViolatedMarker} Reactor rewrote a host app's lifetime");
                        _exitCode = GateViolatedExitCode;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"INCONCLUSIVE {ex.GetType().Name}: {ex.Message}");
                    _exitCode = InconclusiveExitCode;
                }
                finally
                {
                    Console.Out.Flush();
                    // No window was ever opened, so nothing will end this loop
                    // for us under either mode.
                    app.Exit();
                }
            });
        });

        Console.Out.Flush();
        return _exitCode;
    }

    /// <remarks>
    /// <c>partial</c> because CsWinRT requires it (and any parent type) for a
    /// class deriving from a WinRT type: <c>CsWinRT1028</c> is an error under
    /// this repo's Release warnings-as-errors settings.
    /// </remarks>
    private sealed partial class EmbedderApplication : Application;
}
