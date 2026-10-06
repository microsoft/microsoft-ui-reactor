using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Positive control for <see cref="AsyncTeardownGuard"/> (issue #1345): the guard must mark a
/// native failure delivered to <c>Application.UnhandledException</c> as handled, and must not
/// match a managed fault.
/// </summary>
internal static class AsyncTeardownGuardFixtures
{
    private const int E_INVALIDARG = unchecked((int)0x80070057);

    [DllImport("api-ms-win-core-winrt-error-l1-1-0.dll", PreserveSig = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RoOriginateError(int error, IntPtr message);

    [DllImport("api-ms-win-core-winrt-error-l1-1-0.dll", PreserveSig = true)]
    private static extern int GetRestrictedErrorInfo(out IntPtr restrictedErrorInfo);

    [DllImport("api-ms-win-core-winrt-error-l1-1-1.dll", PreserveSig = true)]
    private static extern int RoReportUnhandledError(IntPtr restrictedErrorInfo);

    /// <summary>
    /// Raises <c>E_FAIL</c> the way native code does: originate it, then report it as unhandled.
    /// XAML surfaces it through <c>Application.UnhandledException</c> as a <c>COMException</c>
    /// with no managed frames, which is exactly what the #1345 crashes printed.
    /// </summary>
    private static int ReportNativeFailure()
    {
        RoOriginateError(AsyncTeardownGuard.E_FAIL, IntPtr.Zero);
        var hr = GetRestrictedErrorInfo(out var info);
        if (hr < 0 || info == IntPtr.Zero) return hr < 0 ? hr : AsyncTeardownGuard.E_FAIL;
        try { return RoReportUnhandledError(info); }
        finally { Marshal.Release(info); }
    }

    internal class HandlesNativeReportedFailure(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // The predicate: native-origin teardown HRESULTs only. A COMException that was never
            // thrown carries no managed stack, like one WinUI hands to UnhandledException.
            H.Check("AsyncTeardownGuard_Predicate_NativeEFail",
                AsyncTeardownGuard.IsNativeTeardownFailure(new COMException("native", AsyncTeardownGuard.E_FAIL)));
            H.Check("AsyncTeardownGuard_Predicate_RejectsOtherHResult",
                !AsyncTeardownGuard.IsNativeTeardownFailure(new COMException("native", E_INVALIDARG)));
            COMException thrown;
            try { throw new COMException("managed", AsyncTeardownGuard.E_FAIL); }
            catch (COMException ex) { thrown = ex; }
            H.Check("AsyncTeardownGuard_Predicate_RejectsManagedThrow",
                !AsyncTeardownGuard.IsNativeTeardownFailure(thrown));
            H.Check("AsyncTeardownGuard_Predicate_RejectsOtherType",
                !AsyncTeardownGuard.IsNativeTeardownFailure(new InvalidOperationException()));

            // End to end through WinRT's unhandled-error channel. This delivers the exact #1345
            // signature to Application.UnhandledException (COMException 0x80004005 with no managed
            // frames), but unlike a native XAML callback failure it does not fail-fast when left
            // unhandled, so surviving it proves nothing on its own. The oracle is therefore what
            // the guard did to the event: a subscriber added after the guard's sees Handled set.
            // (That Handled is what stops the fail-fast was measured when the guard landed: a
            // native-origin exception from XAML's own callback path ended the host with
            // 0xC000027B unguarded and was survived with the guard.)
            var reportHr = 0;
            bool? handledSeenAfterGuard = null;
            void Observe(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
                handledSeenAfterGuard = e.Handled;
            var app = Microsoft.UI.Xaml.Application.Current;
            int handled;
            try
            {
                handled = await AsyncTeardownGuard.CloseAndSettleAsync("SelfTest.AsyncTeardownGuard.Probe", () =>
                {
                    app.UnhandledException += Observe;
                    Console.WriteLine("# probe: the next 'Unhandled exception' line is this fixture's deliberate report");
                    reportHr = ReportNativeFailure();
                }, settleMs: 200);
            }
            finally { app.UnhandledException -= Observe; }

            Console.WriteLine($"# report: hr=0x{reportHr:X8} handled={handled} handledSeen={handledSeenAfterGuard}");
            H.Check("AsyncTeardownGuard_ReportDelivered", reportHr >= 0 && handledSeenAfterGuard is not null);
            H.Check("AsyncTeardownGuard_HandledNativeReport", handled == 1 && handledSeenAfterGuard == true);
        }
    }
}
