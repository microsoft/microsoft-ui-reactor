namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Named HRESULT / Win32 constants used in <c>catch (COMException) when
/// (ex.HResult is HResults.X or HResults.Y)</c> filters.
///
/// These values are intentionally <see cref="int"/> (not <see cref="uint"/>)
/// because that is how the runtime exposes <c>Exception.HResult</c>.
/// Use the <c>unchecked((int)0x...)</c> form so the negative two's-complement
/// representation matches the runtime's value byte-for-byte.
///
/// Add new constants here (named, with a one-line comment) rather than
/// inlining the magic number at the catch site. Centralizing them lets
/// the swallowed-error audit (docs/specs/044/swallowed-error-audit.md)
/// cite specific values per site without forcing each catch to repeat
/// the hex literal.
/// </summary>
internal static class HResults
{
    /// <summary>The object invoked has disconnected from its clients (e.g. AppWindow proxy after WM_CLOSE).</summary>
    public const int RPC_E_DISCONNECTED = unchecked((int)0x80010108);

    /// <summary>The handle is invalid (HWND already destroyed / released).</summary>
    public const int E_HANDLE = unchecked((int)0x80070006);

    /// <summary>The server threw an exception (out-of-process COM call into a torn-down server).</summary>
    public const int RPC_E_SERVERFAULT = unchecked((int)0x80010105);

    /// <summary>The object is not connected to the server (COM proxy released before the call landed).</summary>
    public const int CO_E_OBJNOTCONNECTED = unchecked((int)0x800401FD);

    /// <summary>Element not found (e.g. ThumbnailToolbar button index that no longer exists).</summary>
    public const int TYPE_E_ELEMENTNOTFOUND = unchecked((int)0x8002802B);

    /// <summary>The system cannot find the file specified.</summary>
    public const int ERROR_FILE_NOT_FOUND = unchecked((int)0x80070002);

    /// <summary>Access is denied.</summary>
    public const int ERROR_ACCESS_DENIED = unchecked((int)0x80070005);
}
