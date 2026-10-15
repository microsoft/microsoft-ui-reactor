using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest;

/// <summary>
/// Closes a fixture's secondary window while tolerating a native WinUI teardown failure that
/// arrives <em>asynchronously</em>, after <c>Close()</c> has returned. (issue #1345)
/// </summary>
/// <remarks>
/// <para>A <c>try/catch</c> around <c>Close()</c> only sees synchronous throws. A native XAML
/// component that fails while the window is being destroyed (seen with WinUI <c>TitleBar</c>
/// windows) instead reports the error through <c>Application.UnhandledException</c> a few
/// ticks later. Nothing marks it handled, so XAML fail-fasts the whole host with
/// <c>STATUS_STOWED_EXCEPTION</c> (0xC000027B). That truncates the TAP stream and fails every
/// later fixture in the shard, even though the fixture under test already passed.</para>
/// <para>The guard is narrow. It applies only while the close settles, and only to a
/// <see cref="COMException"/> with no managed stack (it originated in native code, not in a
/// Reactor or fixture callback) whose HRESULT is <c>E_FAIL</c> or one of the
/// teardown-reentry set. Each one it handles is printed as a TAP comment, so the event stays
/// visible in the log rather than being silenced. Anything else still reaches the runner's
/// printer unhandled and ends the run as before.</para>
/// </remarks>
internal static class AsyncTeardownGuard
{
    /// <summary>The generic "Unspecified error" HRESULT both #1345 crashes reported.</summary>
    internal const int E_FAIL = unchecked((int)0x80004005);

    /// <summary>
    /// Runs <paramref name="close"/>, then waits <paramref name="settleMs"/>, handling
    /// native teardown failures reported in the meantime. Returns how many it handled.
    /// </summary>
    internal static async Task<int> CloseAndSettleAsync(string site, Action close, int settleMs)
    {
        var app = Application.Current;
        var handled = 0;
        void OnUnhandled(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            if (e.Handled || !IsNativeTeardownFailure(e.Exception)) return;
            e.Handled = true;
            handled++;
            Console.WriteLine(
                $"# {site}: handled an async native teardown exception (issue #1345): " +
                $"{e.Exception.GetType().Name} 0x{e.Exception.HResult:X8}: " +
                (e.Message ?? "").ReplaceLineEndings(" "));
            Console.Out.Flush();
        }

        if (app is not null) app.UnhandledException += OnUnhandled;
        try
        {
            close();
            await Task.Delay(settleMs);
        }
        finally
        {
            if (app is not null) app.UnhandledException -= OnUnhandled;
        }
        return handled;
    }

    /// <summary>
    /// True for an exception that WinUI raised from native code while tearing a window down:
    /// a <see cref="COMException"/> that was never thrown through managed frames, carrying
    /// <c>E_FAIL</c> or a teardown-reentry HRESULT.
    /// </summary>
    internal static bool IsNativeTeardownFailure(Exception? ex) =>
        ex is COMException
        && string.IsNullOrEmpty(ex.StackTrace)
        && (ex.HResult == E_FAIL || HResults.IsTeardownReentry(ex.HResult));
}
