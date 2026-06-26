namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Thrown when a <see cref="WinAppUi"/> operation fails (element not found,
/// winapp returned a non-zero exit code, or the JSON envelope reported failure).
/// Replaces the old Appium <c>WebDriverException</c> as the harness's generic
/// "the UI automation call failed" signal.
/// </summary>
public class WinAppException : Exception
{
    public WinAppException(string message) : base(message) { }
    public WinAppException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a wait/poll helper times out. Carries the same semantics the
/// test suite previously got from Appium's <c>WebDriverTimeoutException</c>.
/// </summary>
public class WinAppTimeoutException : WinAppException
{
    public WinAppTimeoutException(string message) : base(message) { }
}
