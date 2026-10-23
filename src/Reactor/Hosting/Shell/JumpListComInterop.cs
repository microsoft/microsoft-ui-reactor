using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Hosting.Shell;

/// <summary>
/// Source-generated COM interop for unpackaged jump lists. Packaged apps use
/// Windows.UI.StartScreen.JumpList instead. All interfaces preserve the native
/// HRESULT and vtable layout; activation and ownership require no built-in RCW.
/// </summary>
internal static partial class JumpListComInterop
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();
    private static readonly Guid DestinationListClass = new("77f10cf0-3db5-4966-b520-b7c54fd35ed6");
    private static readonly Guid ObjectCollectionClass = new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-c000-000000000046");
    private static readonly Guid DestinationListInterface = new("6332debf-87b5-4670-90c0-5e57b408a49e");
    private static readonly Guid ObjectArrayInterface = new("92ca9dcd-5622-4bba-a805-5e9f541bd8c9");
    private static readonly Guid ObjectCollectionInterface = new("5632b1a4-e38a-400a-928a-d4cd63230295");
    private static readonly Guid ShellLinkInterface = new("000214f9-0000-0000-c000-000000000046");
    private static readonly Guid FmtIdSummaryInfo = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9");
    private static readonly Guid FmtIdAppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private const int NoInterface = unchecked((int)0x80004002);
    private const int ChangedApartmentMode = unchecked((int)0x80010106);

    public static (bool Succeeded, IReadOnlyList<string> RemovedArguments) UpdateUnpackaged(
        string appUserModelId, IReadOnlyList<JumpListItem> items,
        bool showRecent, bool showFrequent)
    {
        ArgumentException.ThrowIfNullOrEmpty(appUserModelId);
        ArgumentNullException.ThrowIfNull(items);
        using var apartment = InitializeApartment();
        using var destination = Create<ICustomDestinationList>(DestinationListClass, DestinationListInterface);
        var list = destination.Value;
        Marshal.ThrowExceptionForHR(list.SetAppID(appUserModelId));
        return UpdateUnpackaged(list, items, showRecent, showFrequent);
    }

    // The transaction is shared by native activation and controlled shell tests.
    internal static (bool Succeeded, IReadOnlyList<string> RemovedArguments) UpdateUnpackaged(
        ICustomDestinationList list, IReadOnlyList<JumpListItem> items,
        bool showRecent, bool showFrequent)
    {
        bool begun = false;
        bool committed = false;
        try
        {
            int hr = list.BeginList(out _, in ObjectArrayInterface, out var removedPointer);
            if (hr < 0)
            {
                Release(removedPointer);
                DiagnosticLog.HResultFailed(LogCategory.Shell, "JumpList.BeginList", hr);
                return (false, Array.Empty<string>());
            }
            begun = true;
            using var removed = WrapOwned<IObjectArray>(removedPointer);
            var removedArguments = ReadRemovedArguments(removed.Value, Environment.ProcessPath ?? string.Empty);
            items = JumpList.ExcludeRemovedItems(items, removedArguments, customCategoriesOnly: true);

            var tasks = new List<JumpListItem>();
            bool accepted = true;
            var groups = new Dictionary<string, List<JumpListItem>>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item.Kind == JumpListItemKind.Custom && !string.IsNullOrEmpty(item.GroupCategory))
                {
                    if (!groups.TryGetValue(item.GroupCategory, out var group))
                        groups[item.GroupCategory] = group = new List<JumpListItem>();
                    group.Add(item);
                }
                else
                    tasks.Add(item);
            }

            if (tasks.Count > 0)
            {
                using var collection = BuildShellLinkArray(tasks);
                int taskHr = list.AddUserTasks(collection.Value);
                accepted &= taskHr >= 0;
                LogFailure("JumpList.AddUserTasks", taskHr);
            }
            foreach (var (category, entries) in groups)
            {
                using var collection = BuildShellLinkArray(entries);
                int categoryHr = list.AppendCategory(category, collection.Value);
                accepted &= categoryHr >= 0;
                LogFailure("JumpList.AppendCategory", categoryHr);
            }
            if (showRecent)
            {
                int recentHr = list.AppendKnownCategory(KnownDestCategory.Recent);
                accepted &= recentHr >= 0;
                LogFailure("JumpList.AppendKnownCategory.Recent", recentHr);
            }
            if (showFrequent)
            {
                int frequentHr = list.AppendKnownCategory(KnownDestCategory.Frequent);
                accepted &= frequentHr >= 0;
                LogFailure("JumpList.AppendKnownCategory.Frequent", frequentHr);
            }
            int commitHr = list.CommitList();
            committed = commitHr >= 0;
            LogFailure("JumpList.CommitList", commitHr);
            return (accepted && committed, removedArguments);
        }
        finally
        {
            if (begun && !committed)
                LogFailure("JumpList.AbortList", list.AbortList());
        }
    }

    /// <summary>Read removals without beginning or committing a list update.</summary>
    internal static IReadOnlyList<string> GetRemovedArguments(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrEmpty(appUserModelId);
        using var apartment = InitializeApartment();
        using var destination = Create<ICustomDestinationList>(DestinationListClass, DestinationListInterface);
        Marshal.ThrowExceptionForHR(destination.Value.SetAppID(appUserModelId));
        int hr = destination.Value.GetRemovedDestinations(in ObjectArrayInterface, out var pointer);
        using var removed = WrapResult<IObjectArray>(hr, pointer);
        return ReadRemovedArguments(removed.Value, Environment.ProcessPath ?? string.Empty);
    }

    internal static unsafe IReadOnlyList<string> ReadRemovedArguments(IObjectArray removed, string executablePath)
    {
        Marshal.ThrowExceptionForHR(removed.GetCount(out var count));
        var arguments = new HashSet<string>(StringComparer.Ordinal);
        var buffer = new char[32768];
        for (uint i = 0; i < count; i++)
        {
            // A destination may be an IShellItem, not a link. Ask for the link
            // interface directly instead of relying on runtime object marshalling.
            int hr = removed.GetAt(i, in ShellLinkInterface, out var pointer);
            if (hr == NoInterface)
            {
                Release(pointer);
                continue;
            }
            using var link = WrapResult<IShellLinkW>(hr, pointer);
            fixed (char* text = buffer)
            {
                Marshal.ThrowExceptionForHR(link.Value.GetPath(text, buffer.Length, 0, 0));
                if (string.IsNullOrEmpty(executablePath) ||
                    !string.Equals(new string(text), executablePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                Marshal.ThrowExceptionForHR(link.Value.GetArguments(text, buffer.Length));
                arguments.Add(new string(text));
            }
        }
        return arguments.ToArray();
    }

    /// <summary>Test hook: delete only the supplied test application's list.</summary>
    public static void DeleteListForTests(string appUserModelId)
    {
        try
        {
            DeleteList(appUserModelId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] DeleteListForTests failed: {ex.Message}");
        }
    }

    internal static void DeleteList(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrEmpty(appUserModelId);
        using var apartment = InitializeApartment();
        using var destination = Create<ICustomDestinationList>(DestinationListClass, DestinationListInterface);
        Marshal.ThrowExceptionForHR(destination.Value.SetAppID(appUserModelId));
        Marshal.ThrowExceptionForHR(destination.Value.DeleteList(appUserModelId));
    }

    /// <summary>Caller keeps its COM apartment alive until the collection is disposed.</summary>
    internal static OwnedCom<IObjectCollection> BuildShellLinkArray(IReadOnlyList<JumpListItem> items)
    {
        var collection = Create<IObjectCollection>(ObjectCollectionClass, ObjectCollectionInterface);
        try
        {
            string executable = Environment.ProcessPath ?? string.Empty;
            foreach (var item in items)
            {
                using var link = Create<IShellLinkW>(ShellLinkClass, ShellLinkInterface);
                if (item.Kind == JumpListItemKind.Separator)
                    ApplyProperty(link.Value, new PropertyKey(FmtIdAppUserModel, 6),
                        new PropVariant { vt = 11, p1 = new IntPtr(-1) });
                else
                {
                    if (!string.IsNullOrEmpty(executable))
                        Marshal.ThrowExceptionForHR(link.Value.SetPath(executable));
                    Marshal.ThrowExceptionForHR(link.Value.SetArguments(item.Arguments ?? string.Empty));
                    if (!string.IsNullOrEmpty(item.Description))
                        Marshal.ThrowExceptionForHR(link.Value.SetDescription(item.Description));
                    if (item.Icon is { IsResource: false, Source: var icon } && !string.IsNullOrEmpty(icon))
                        Marshal.ThrowExceptionForHR(link.Value.SetIconLocation(icon, 0));
                    ApplyProperty(link.Value, new PropertyKey(FmtIdSummaryInfo, 2),
                        new PropVariant { vt = 31, p1 = Marshal.StringToCoTaskMemUni(item.Title) });
                }
                Marshal.ThrowExceptionForHR(collection.Value.AddObject(link.Pointer));
            }
            return collection;
        }
        catch
        {
            collection.Dispose();
            throw;
        }
    }

    private static void ApplyProperty(IShellLinkW link, PropertyKey key, PropVariant value)
    {
        try
        {
            var store = (IPropertyStore)link;
            Marshal.ThrowExceptionForHR(store.SetValue(in key, in value));
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally
        {
            Marshal.ThrowExceptionForHR(PropVariantClear(ref value));
        }
    }

    private static void LogFailure(string operation, int hr)
    {
        if (hr < 0) DiagnosticLog.HResultFailed(LogCategory.Shell, operation, hr);
    }

    private static OwnedCom<T> Create<T>(in Guid classId, in Guid interfaceId) where T : class
    {
        int hr = CoCreateInstance(in classId, 0, 1, in interfaceId, out var pointer);
        return WrapResult<T>(hr, pointer);
    }

    private static OwnedCom<T> WrapResult<T>(int hr, nint pointer) where T : class
    {
        if (hr < 0)
        {
            Release(pointer);
            Marshal.ThrowExceptionForHR(hr);
        }
        return WrapOwned<T>(pointer);
    }

    private static OwnedCom<T> WrapOwned<T>(nint pointer) where T : class
    {
        if (pointer == 0) throw new InvalidOperationException("The shell returned a null COM interface.");
        ComObject? wrapper = null;
        try
        {
            // UniqueInstance allows deterministic release without invalidating
            // another caller's wrapper for the same native COM identity.
            wrapper = (ComObject)ComWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
            return new OwnedCom<T>((T)(object)wrapper, wrapper, pointer);
        }
        catch
        {
            wrapper?.FinalRelease();
            Release(pointer);
            throw;
        }
    }

    private static void Release(nint pointer)
    {
        if (pointer != 0) Marshal.Release(pointer);
    }

    internal sealed class OwnedCom<T>(T value, ComObject wrapper, nint pointer) : IDisposable where T : class
    {
        private ComObject? _wrapper = wrapper;
        private nint _pointer = pointer;
        internal T Value { get; } = value;
        internal nint Pointer => _pointer;

        public void Dispose()
        {
            var owned = Interlocked.Exchange(ref _wrapper, null);
            if (owned is null) return;
            try { owned.FinalRelease(); }
            finally { Release(Interlocked.Exchange(ref _pointer, 0)); }
        }
    }

    internal readonly struct ApartmentScope(bool uninitialize) : IDisposable
    {
        public void Dispose()
        {
            if (uninitialize) CoUninitialize();
        }
    }

    internal static ApartmentScope InitializeApartment()
    {
        int hr = CoInitializeEx(0, 0); // COINIT_MULTITHREADED on worker threads.
        if (hr == ChangedApartmentMode)
            return new ApartmentScope(false); // Preserve the caller's existing STA.
        Marshal.ThrowExceptionForHR(hr); // S_OK and S_FALSE both need a matching uninitialize.
        return new ApartmentScope(true);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("6332debf-87b5-4670-90c0-5e57b408a49e")]
    internal partial interface ICustomDestinationList
    {
        [PreserveSig] int SetAppID(string appId);
        [PreserveSig] int BeginList(out uint maxSlots, in Guid iid, out nint value);
        [PreserveSig] int AppendCategory(string category, IObjectArray array);
        [PreserveSig] int AppendKnownCategory(KnownDestCategory category);
        [PreserveSig] int AddUserTasks(IObjectArray array);
        [PreserveSig] int CommitList();
        [PreserveSig] int GetRemovedDestinations(in Guid iid, out nint value);
        [PreserveSig] int DeleteList(string appId);
        [PreserveSig] int AbortList();
    }

    internal enum KnownDestCategory : uint { Frequent = 1, Recent = 2 }

    [GeneratedComInterface]
    [Guid("92ca9dcd-5622-4bba-a805-5e9f541bd8c9")]
    internal partial interface IObjectArray
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, in Guid iid, out nint value);
    }

    [GeneratedComInterface]
    [Guid("5632b1a4-e38a-400a-928a-d4cd63230295")]
    internal partial interface IObjectCollection : IObjectArray
    {
        [PreserveSig] int AddObject(nint value);
        [PreserveSig] int AddFromArray(IObjectArray array);
        [PreserveSig] int RemoveObjectAt(uint index);
        [PreserveSig] int Clear();
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("000214f9-0000-0000-c000-000000000046")]
    internal unsafe partial interface IShellLinkW
    {
        [PreserveSig] int GetPath(char* path, int capacity, nint findData, uint flags);
        [PreserveSig] int GetIDList(out nint itemIdList);
        [PreserveSig] int SetIDList(nint itemIdList);
        [PreserveSig] int GetDescription(char* text, int capacity);
        [PreserveSig] int SetDescription(string description);
        [PreserveSig] int GetWorkingDirectory(char* text, int capacity);
        [PreserveSig] int SetWorkingDirectory(string directory);
        [PreserveSig] int GetArguments(char* text, int capacity);
        [PreserveSig] int SetArguments(string arguments);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int show);
        [PreserveSig] int SetShowCmd(int show);
        [PreserveSig] int GetIconLocation(char* path, int capacity, out int index);
        [PreserveSig] int SetIconLocation(string path, int index);
        [PreserveSig] int SetRelativePath(string path, uint reserved);
        [PreserveSig] int Resolve(nint window, uint flags);
        [PreserveSig] int SetPath(string path);
    }

    [GeneratedComInterface]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    internal partial interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(in PropertyKey key, in PropVariant value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PropertyKey(Guid format, uint id)
    {
        public Guid fmtid = format;
        public uint pid = id;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1, wReserved2, wReserved3;
        public IntPtr p1, p2;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid iid, out nint value);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint concurrency);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant value);
}
