using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Mxc.Sdk.V1;

namespace WidgetCreator.Services;

/// <summary>
/// C-3 (runtime substitution): binds the MXC SDK to a <b>verified</b> native
/// runtime before the first MXC call.
/// <para>
/// The SDK loads <c>mxc_ffi.dll</c> into this (unsandboxed) host process and,
/// left to itself, searches the <c>MXC_FFI_DIR</c> environment override and
/// repo-relative Cargo output before the packaged asset — without checking what
/// it loads. NuGet signing only authenticates the package at restore time, not
/// the file on disk at run time, and the later out-of-band containment check
/// can't protect the host from code already loaded into it.
/// </para>
/// <para>
/// So the host claims the SDK assembly's <see cref="NativeLibrary.SetDllImportResolver"/>
/// slot first (the SDK tolerates losing that race: it writes a one-line
/// "could not register the native library resolver" note to stderr and defers
/// to ours), resolves
/// <c>mxc_ffi</c> only from the app's own packaged native assets, holds every
/// MXC native file open read-share-only (so it can't be swapped after the
/// check), requires a valid Authenticode signature from Microsoft Corporation
/// chained to a Microsoft root, and loads that exact file. Any failure leaves
/// the sandbox unavailable — fail closed, never fall back to an unverified load.
/// </para>
/// </summary>
internal static class MxcNativeRuntime
{
    const string LibraryName = "mxc_ffi";
    const string LibraryFile = "mxc_ffi.dll";

    // Other natives the MXC runtime loads or launches from its own directory.
    static readonly string[] CompanionFiles = ["plm.exe", "wslcsdk.dll", "wxc-wslc-daemon.exe"];

    static readonly System.Threading.Lock Gate = new();
    static readonly List<FileStream> Pinned = [];
    static bool _initialized;
    static string? _error;
    static IntPtr _handle;

    /// <summary>
    /// Verify and bind the MXC native runtime (once). Returns <c>null</c> when the
    /// SDK is bound to a verified <c>mxc_ffi.dll</c>; otherwise why it isn't.
    /// Call before any other MXC API.
    /// </summary>
    public static string? EnsureVerified()
    {
        lock (Gate)
        {
            if (!_initialized)
            {
                _error = Initialize();
                _initialized = true;
                if (_error is not null)
                    ReleasePins();
                SessionLog.Write(_error is null
                    ? "[Sandbox] MXC native runtime verified (Microsoft-signed) and bound"
                    : $"[Sandbox] MXC native runtime NOT trusted — {_error}");
            }
            return _error;
        }
    }

    static string? Initialize()
    {
        var dir = LocateNativeDirectory();
        if (dir is null)
            return $"{LibraryFile} was not found in the app's packaged native assets.";

        var libraryPath = Path.Join(dir, LibraryFile);
        var files = new List<string> { libraryPath };
        files.AddRange(CompanionFiles.Select(companion => Path.Join(dir, companion)).Where(File.Exists));

        foreach (var path in files)
        {
            FileStream pin;
            try
            {
                // No FileShare.Write/Delete: the file can't be modified, replaced or
                // renamed while the host holds it, so what we verify is what loads.
                pin = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"could not open {Path.GetFileName(path)} for verification ({ex.Message}).";
            }
            Pinned.Add(pin);

            if (VerifyMicrosoftSignature(pin, path) is { } signatureError)
                return $"{Path.GetFileName(path)} failed signature verification: {signatureError}";
        }

        if (!NativeLibrary.TryLoad(libraryPath, out _handle))
            return $"{LibraryFile} could not be loaded.";

        try
        {
            NativeLibrary.SetDllImportResolver(typeof(MxcContainer).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // The SDK registered its own (unverified) resolver first — something used
            // MXC before this gate ran. Its native library may already be loaded.
            return "the MXC SDK resolved its native runtime before the host could verify it.";
        }
        return null;
    }

    static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) =>
        string.Equals(libraryName, LibraryName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(libraryName, LibraryFile, StringComparison.OrdinalIgnoreCase)
            ? _handle
            : IntPtr.Zero;

    /// <summary>
    /// The app's packaged native-asset directory: the app directory itself for
    /// RID-specific builds and publish, or <c>runtimes/&lt;rid&gt;/native</c> for
    /// RID-less builds. Never environment overrides or paths outside the app.
    /// </summary>
    static string? LocateNativeDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            baseDir,
            Path.Join(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native"),
            Path.Join(baseDir, "runtimes", ProcessRid(), "native"),
        ];
        return candidates.FirstOrDefault(dir => File.Exists(Path.Join(dir, LibraryFile)));
    }

    static string ProcessRid() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "win-arm64",
        Architecture.X86 => "win-x86",
        _ => "win-x64",
    };

    static void ReleasePins()
    {
        foreach (var pin in Pinned)
            pin.Dispose();
        Pinned.Clear();
    }

    // ── Authenticode ─────────────────────────────────────────────────────────

    /// <summary>
    /// WinVerifyTrust (Authenticode, read through the pinned handle) plus a signer
    /// check: leaf <c>O=Microsoft Corporation</c>, chain root a Microsoft root CA.
    /// Revocation isn't checked (offline-friendly); trust still requires a valid
    /// chain to a trusted root. Returns <c>null</c> when trusted.
    /// </summary>
    static string? VerifyMicrosoftSignature(FileStream pinned, string path)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = pathPtr,
            hFile = pinned.SafeFileHandle.DangerousGetHandle(),
        };
        var fileInfoHandle = GCHandle.Alloc(fileInfo, GCHandleType.Pinned);
        var data = new WINTRUST_DATA
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
            dwUIChoice = WTD_UI_NONE,
            fdwRevocationChecks = WTD_REVOKE_NONE,
            dwUnionChoice = WTD_CHOICE_FILE,
            pFile = fileInfoHandle.AddrOfPinnedObject(),
            dwStateAction = WTD_STATEACTION_VERIFY,
            dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
        };
        var action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
        try
        {
            var status = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            if (status != 0)
                return $"Authenticode signature is not valid (WinVerifyTrust 0x{status:X8}).";

            var provData = WTHelperProvDataFromStateData(data.hWVTStateData);
            var signer = provData == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provData, 0, false, 0);
            if (signer == IntPtr.Zero)
                return "no signer information.";

            // CRYPT_PROVIDER_SGNR: DWORD cbStruct; FILETIME sftVerifyAsOf; DWORD csCertChain; ...
            var chainLength = Marshal.ReadInt32(signer, 12);
            if (chainLength < 2)
                return "the signing certificate chain is incomplete.";

            using var leaf = ProviderCert(signer, 0);
            using var root = ProviderCert(signer, (uint)chainLength - 1);
            if (leaf is null || root is null)
                return "could not read the signing certificate chain.";

            if (!HasOrganization(leaf, "Microsoft Corporation"))
                return $"signed by '{leaf.Subject}', not Microsoft Corporation.";
            if (!root.GetNameInfo(X509NameType.SimpleName, forIssuer: false)
                    .StartsWith("Microsoft Root Certificate Authority", StringComparison.Ordinal))
                return $"the signature chains to '{root.Subject}', not a Microsoft root.";
            return null;
        }
        finally
        {
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            fileInfoHandle.Free();
            Marshal.FreeHGlobal(pathPtr);
        }
    }

    static X509Certificate2? ProviderCert(IntPtr signer, uint index)
    {
        // CRYPT_PROVIDER_CERT: DWORD cbStruct; PCCERT_CONTEXT pCert; ...
        var providerCert = WTHelperGetProvCertFromChain(signer, index);
        if (providerCert == IntPtr.Zero)
            return null;
        var certContext = Marshal.ReadIntPtr(providerCert, IntPtr.Size);
        return certContext == IntPtr.Zero ? null : new X509Certificate2(certContext);
    }

    static bool HasOrganization(X509Certificate2 cert, string organization)
    {
        foreach (var rdn in cert.SubjectName.EnumerateRelativeDistinguishedNames())
            if (rdn.GetSingleElementType().Value == "2.5.4.10"
                && string.Equals(rdn.GetSingleElementValue(), organization, StringComparison.Ordinal))
                return true;
        return false;
    }

    static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    const uint WTD_UI_NONE = 2;
    const uint WTD_REVOKE_NONE = 0;
    const uint WTD_CHOICE_FILE = 1;
    const uint WTD_STATEACTION_VERIFY = 1;
    const uint WTD_STATEACTION_CLOSE = 2;
    const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, ref WINTRUST_DATA data);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    static extern IntPtr WTHelperGetProvSignerFromChain(
        IntPtr provData, uint signerIndex, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certIndex);
}
