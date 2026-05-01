using System.Runtime.InteropServices;

namespace ReactorFiles.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct FsEntry
{
    public nint NamePtr;     // *const u16
    public uint NameLen;
    public ulong Size;
    public ulong ModifiedTicks;
    public byte IsDirectory;
    public byte HasChildren;
}

/// <summary>
/// SAFETY (TASK-077): All pointer-bearing fields below (<see cref="Entries"/>
/// and <see cref="FsEntry.NamePtr"/>) are allocated by the Rust side using the
/// system allocator via <c>Box&lt;[T]&gt;::into_raw</c>. They MUST be freed
/// only by passing the entire <see cref="FsResult"/> to
/// <see cref="NativeFs.reactorfs_free_result"/>. Calling
/// <see cref="System.Runtime.InteropServices.Marshal.FreeHGlobal"/> or any
/// other CoTaskMem/Local/Global free here would mix allocators and corrupt
/// the heap on non-default allocator builds.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FsResult
{
    public nint Entries;     // *mut FsEntry — owned by the Rust allocator
    public uint Count;
}

internal static partial class NativeFs
{
    [LibraryImport("reactorfs")]
    internal static unsafe partial FsResult reactorfs_enumerate(char* pathPtr, uint pathLen);

    [LibraryImport("reactorfs")]
    internal static unsafe partial FsResult reactorfs_enumerate_subdirs(char* pathPtr, uint pathLen);

    /// <summary>
    /// Free a <see cref="FsResult"/> previously returned by enumerate. This is
    /// the ONLY supported deallocation path; see comment on <see cref="FsResult"/>.
    /// </summary>
    [LibraryImport("reactorfs")]
    internal static partial void reactorfs_free_result(FsResult result);
}
