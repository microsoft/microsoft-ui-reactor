namespace Microsoft.UI.Reactor.Hosting.Shell;

/// <summary>
/// Runtime detection of MSIX vs. unpackaged execution. WinRT classes that
/// implicitly query the package context (<c>JumpList</c>, <c>SecondaryTile</c>)
/// throw on unpackaged apps; this helper lets the caller pick the unpackaged
/// fallback path before the WinRT call. (spec 036 §11.3, §0.5 — no
/// <c>#if PACKAGED</c> branching anywhere.)
/// </summary>
internal static class PackageRuntime
{
    private static int _isPackagedComputed;
    private static int _isPackaged;

    /// <summary>True iff this process runs under an MSIX package identity.</summary>
    public static bool IsPackaged
    {
        get
        {
            if (Volatile.Read(ref _isPackagedComputed) != 0)
                return Volatile.Read(ref _isPackaged) != 0;

            bool packaged;
            try
            {
                // Package.Current throws InvalidOperationException on unpackaged.
                _ = global::Windows.ApplicationModel.Package.Current;
                packaged = true;
            }
            catch
            {
                packaged = false;
            }
            Volatile.Write(ref _isPackaged, packaged ? 1 : 0);
            Volatile.Write(ref _isPackagedComputed, 1);
            return packaged;
        }
    }

    internal static void ResetForTests()
    {
        Volatile.Write(ref _isPackagedComputed, 0);
        Volatile.Write(ref _isPackaged, 0);
    }
}
