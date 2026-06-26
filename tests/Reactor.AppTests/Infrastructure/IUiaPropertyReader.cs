namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Read-only accessor for the handful of UIA properties that winapp 0.3.2
/// <c>get-property</c> cannot surface (it returns <c>null</c> for them — see
/// <see cref="UiaPropertyReader"/> for the exact list). The accessibility suite needs
/// these to keep parity with the old WinAppDriver harness, so the current implementation
/// reads them in-process via a minimal CUIAutomation COM client.
///
/// This is deliberately behind an interface so it can be swapped for a winapp-backed
/// implementation once winappCli exposes the missing properties through
/// <c>winapp ui get-property</c> — at which point <see cref="Handles"/> can simply return
/// <c>false</c> (no fallback needed) and the COM interop in <see cref="UiaPropertyReader"/>
/// can be deleted. See the TODO in that file and the empirically-verified null-property
/// list it documents.
/// </summary>
public interface IUiaPropertyReader
{
    /// <summary>True when this reader knows how to map the named GetAttribute property.</summary>
    bool Handles(string property);

    /// <summary>Read a UIA property of the element with the given AutomationId. Null if unset / absent.</summary>
    string? ReadByAutomationId(string automationId, string property);

    /// <summary>Read a UIA property of the first element whose Name matches. Null if unset / absent.</summary>
    string? ReadByName(string name, string property);

    /// <summary>AutomationId of the system-wide focused element, or empty string if none / on error.</summary>
    string GetFocusedAutomationId();
}
