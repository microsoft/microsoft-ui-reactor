#nullable enable

using System;
using Microsoft.UI.Reactor;
using Xunit;

namespace Reactor.Tests;

/// <summary>
/// Regression test for the embed P/Invoke that took hours to find in spec-056:
/// the managed name <c>GetProcessDpiAwarenessContext</c> previously declared a
/// Win32 entry point that does not exist in user32.dll. The first call from
/// the embed-Child code path threw <see cref="EntryPointNotFoundException"/>,
/// which escaped through WinUI's startup callbacks as a fail-fast (0xC000027B)
/// before <c>PreviewCaptureServer</c> could emit <c>CAPTURE_PORT=</c>. The VS
/// embedded preview surface then reported "Build failed" with no actionable
/// signal. The fix points the same managed name at the correct entry point
/// (<c>GetThreadDpiAwarenessContext</c>, since the process-wide context is
/// inherited by the thread that called <c>SetProcessDpiAwarenessContext</c>).
/// </summary>
public sealed class EmbedDpiPInvokeTests
{
    [Fact]
    public void GetProcessDpiAwarenessContext_ResolvesAndReturnsNonZero()
    {
        // Should not throw EntryPointNotFoundException. The exact returned
        // pointer depends on whether the test host called
        // SetProcessDpiAwarenessContext, but it must be non-zero on any modern
        // Windows runtime.
        var context = ReactorWindow.GetCurrentDpiAwarenessContextForTests();
        Assert.NotEqual(IntPtr.Zero, context);
    }
}
