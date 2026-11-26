using System;

namespace WinUIGalleryReactor;

/// <summary>
/// Interpretation of the Win32 <c>GetCurrentPackageFullName</c> status code that tells
/// the gallery whether it is running with MSIX package identity.
/// </summary>
/// <remarks>
/// Split out from <c>GalleryProtocol</c>, which owns the P/Invoke itself, so the
/// mapping is unit-testable: the call can only ever report what the current test process
/// happens to be, whereas the mapping has four cases that all matter. Getting it wrong is
/// silent — the app would simply take the other flavour's registration path.
/// </remarks>
public static class GalleryPackageIdentity
{
    /// <summary><c>ERROR_SUCCESS</c> — identity exists and was returned.</summary>
    public const int ErrorSuccess = 0;

    /// <summary>
    /// <c>ERROR_INSUFFICIENT_BUFFER</c> — identity exists, but the name doesn't fit in the
    /// zero-length buffer the probe deliberately passes. This is the expected success code
    /// for a packaged process.
    /// </summary>
    public const int ErrorInsufficientBuffer = 122;

    /// <summary><c>APPMODEL_ERROR_NO_PACKAGE</c> — the process has no package identity.</summary>
    public const int AppModelErrorNoPackage = 15700;

    /// <summary>
    /// Whether <paramref name="status"/> means "this process has package identity".
    /// Anything unexpected reports unpackaged, which is the conservative answer: it keeps
    /// the app managing its own protocol registration rather than assuming Windows did.
    /// </summary>
    public static bool IsPackaged(int status) =>
        status is ErrorSuccess or ErrorInsufficientBuffer;
}
