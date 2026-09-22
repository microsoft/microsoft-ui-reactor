// Resolving a path to the one the filesystem actually means.
//
// Why this exists
// ---------------
// Two independent coordination schemes in this repo key off a path: the packaged tier derives
// an MSIX identity from a layout directory, and the E2E tier keys a lease and a startup gate
// off the host executable. Both hash the path and both compare paths for equality, so both
// have the same failure mode — if one physical location has two spellings, the two runs that
// reach it by different spellings never see each other. They take different lock files, each
// concludes it is alone, and then they concurrently rewrite the same registration or kill each
// other's host. That is precisely the cross-checkout interference this suite exists to prevent.
//
// `Path.GetFullPath` cannot close that gap because it is pure string handling. Measured on
// Windows it does expand 8.3 short components of an existing path, but it leaves `subst` and
// mapped drive letters pointing at the mapping and preserves the extended-length `\\?\`
// spelling verbatim. Asking the filesystem is the only way to collapse those.
//
// Shared rather than duplicated because the two callers must not disagree about what "the same
// path" means, and because the interop below is easy to get subtly wrong twice.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Reactor.Tests.Shared;

/// <summary>
/// Asks Windows what a path really points at, collapsing the alternate spellings the
/// filesystem admits for one location.
/// </summary>
internal static partial class FinalPath
{
    /// <summary>
    /// The final path of an existing file or directory, or <see langword="null"/> when Windows
    /// cannot answer.
    /// </summary>
    /// <remarks>
    /// <para>Resolves symlinks and junctions in any component, mapped and <c>subst</c>'d drive
    /// letters, and the extended-length <c>\\?\</c> spelling, all in one call — so two runs
    /// that reach one physical location by different spellings agree on what they reached.</para>
    /// <para>The handle is opened with no access rights at all:
    /// <c>GetFinalPathNameByHandle</c> needs only a handle, not readable content, so requesting
    /// nothing lets this succeed on a path the caller could not otherwise open.
    /// <c>FILE_FLAG_BACKUP_SEMANTICS</c> is what makes <c>CreateFile</c> open a directory
    /// rather than fail; it is harmless for a file, which is why one helper serves both
    /// callers.</para>
    /// <para>Every failure returns null rather than throwing. This runs while deciding an
    /// identity, where an unreadable path is not an error: each caller has a weaker but working
    /// fallback, and turning a query failure into an exception would take down a run over a
    /// question that has an answer.</para>
    /// </remarks>
    internal static string? TryResolve(string path)
    {
        try
        {
            using var handle = CreateFileW(
                path,
                dwDesiredAccess: 0,
                dwShareMode: FileShareAll,
                lpSecurityAttributes: IntPtr.Zero,
                dwCreationDisposition: OpenExisting,
                dwFlagsAndAttributes: FileFlagBackupSemantics,
                hTemplateFile: IntPtr.Zero);

            if (handle.IsInvalid) return null;

            var buffer = new char[1024];
            var length = FinalPathInto(handle, buffer);

            // A return of 0 is failure; a return >= the buffer size is "needed this much room".
            if (length == 0) return null;
            if (length >= buffer.Length)
            {
                buffer = new char[length + 1];
                length = FinalPathInto(handle, buffer);
                if (length == 0 || length >= buffer.Length) return null;
            }

            return StripExtendedLengthPrefix(new string(buffer, 0, (int)length));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Pins <paramref name="buffer"/> and asks for the final path in DOS form.</summary>
    private static unsafe uint FinalPathInto(SafeFileHandle handle, char[] buffer)
    {
        fixed (char* p = buffer)
            return GetFinalPathNameByHandleW(handle, p, (uint)buffer.Length, VolumeNameDos);
    }

    /// <summary>
    /// Converts the extended-length form <c>GetFinalPathNameByHandle</c> returns back into an
    /// ordinary path.
    /// </summary>
    /// <remarks>
    /// <para>The API always answers with a <c>\\?\</c> prefix, and for a network location with
    /// <c>\\?\UNC\server\share</c>, whose ordinary spelling is <c>\\server\share</c>. Leaving
    /// either form in place would make the answer disagree with every path its callers produce
    /// via <see cref="Path.GetFullPath(string)"/>, so one location would hash one way when it
    /// exists and another way when a caller's fallback ran.
    /// </para>
    /// <para>Exposed rather than private because a caller deriving a path's <em>unresolved</em>
    /// form has to strip the prefix the same way this does, or the two spellings of one
    /// location would still disagree on the one code path that never gets a handle.</para>
    /// </remarks>
    internal static string StripExtendedLengthPrefix(string path)
    {
        const string UncPrefix = @"\\?\UNC\";
        const string DevicePrefix = @"\\?\";

        if (path.StartsWith(UncPrefix, StringComparison.Ordinal))
            return @"\\" + path[UncPrefix.Length..];

        return path.StartsWith(DevicePrefix, StringComparison.Ordinal)
            ? path[DevicePrefix.Length..]
            : path;
    }

    private const uint FileShareAll = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint VolumeNameDos = 0x0;

    // Source-generated interop rather than [DllImport]: the marshalling is emitted at compile
    // time, which keeps this trim- and AOT-clean. Entry points are spelled with their explicit
    // -W suffix because LibraryImport uses ExactSpelling and does not append one.
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    // Declared with a char* rather than a char[] buffer: LibraryImport rejects the array form
    // for this signature (SYSLIB1051, "runtime marshalling must be disabled"), so the buffer is
    // pinned by the caller instead.
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        char* lpszFilePath,
        uint cchFilePath,
        uint dwFlags);
}
