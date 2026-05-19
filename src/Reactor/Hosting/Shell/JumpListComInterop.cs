using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Hosting.Shell;

/// <summary>
/// Win32 <c>ICustomDestinationList</c> COM wrapper for unpackaged jump-list
/// updates. The packaged path goes through <c>Windows.UI.StartScreen.JumpList</c>
/// instead — this is the fallback used when <see cref="PackageRuntime.IsPackaged"/>
/// returns false. (spec 036 §11.3)
/// </summary>
/// <remarks>
/// <para>The COM surface is intentionally small: <c>BeginList</c> →
/// <c>AppendCategory</c> / <c>AddUserTasks</c> → <c>CommitList</c>. Each
/// task is an <c>IShellLinkW</c> whose <c>IPropertyStore</c> carries the
/// title (<c>System.Title</c> = <c>PKEY_Title</c>) and arguments
/// (<c>SetArguments</c>).</para>
/// <para>This implementation supports the conventional shapes Reactor needs:
/// Task entries grouped under "Tasks", Custom entries grouped by
/// <c>GroupCategory</c>, and Separator entries (rendered as horizontal
/// rules in the Tasks group). Recent / Frequent OS-managed groups are
/// configured via <c>EnableAutoRecycle</c> on the <c>BeginList</c> result.</para>
/// </remarks>
internal static class JumpListComInterop
{
    /// <summary>
    /// Apply the supplied items to the unpackaged jump list. UI-thread
    /// callable but the implementation does no XAML work — safe to run
    /// on a worker via <c>Task.Run</c>.
    /// </summary>
    public static void UpdateUnpackaged(
        string appUserModelId,
        IReadOnlyList<JumpListItem> items,
        bool showRecent,
        bool showFrequent)
    {
        ArgumentNullException.ThrowIfNull(appUserModelId);
        if (string.IsNullOrEmpty(appUserModelId))
            throw new ArgumentException("AppUserModelId required for unpackaged JumpList.", nameof(appUserModelId));
        ArgumentNullException.ThrowIfNull(items);

        ICustomDestinationList? cdl = null;
        IObjectArray? removed = null;
        try
        {
            cdl = (ICustomDestinationList)new DestinationList();
            try { cdl.SetAppID(appUserModelId); }
            catch (Exception ex) { Debug.WriteLine($"[Reactor] JumpList SetAppID failed: {ex.Message}"); }

            uint slotCount;
            var iidObjArray = typeof(IObjectArray).GUID;
            int hr = cdl.BeginList(out slotCount, ref iidObjArray, out removed);
            if (hr < 0)
            {
                DiagnosticLog.HResultFailed(LogCategory.Shell, "JumpList.BeginList", hr);
                return;
            }

            // Group by category. Separators stay in the Tasks group with
            // their kind preserved on the IShellLink via PKEY_AppUserModel_IsDestListSeparator.
            var taskItems = new List<JumpListItem>();
            var customGroups = new Dictionary<string, List<JumpListItem>>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Kind == JumpListItemKind.Custom && !string.IsNullOrEmpty(item.GroupCategory))
                {
                    if (!customGroups.TryGetValue(item.GroupCategory!, out var bucket))
                        customGroups[item.GroupCategory!] = bucket = new List<JumpListItem>();
                    bucket.Add(item);
                }
                else
                {
                    taskItems.Add(item);
                }
            }

            // User Tasks group.
            if (taskItems.Count > 0)
            {
                var coll = BuildShellLinkArray(taskItems);
                if (coll is not null)
                {
                    try
                    {
                        var asArray = (IObjectArray)coll;
                        int taskHr = cdl.AddUserTasks(asArray);
                        if (taskHr < 0) DiagnosticLog.HResultFailed(LogCategory.Shell, "JumpList.AddUserTasks", taskHr);
                    }
                    finally { Marshal.ReleaseComObject(coll); }
                }
            }

            // Custom groups.
            foreach (var (category, list) in customGroups)
            {
                var coll = BuildShellLinkArray(list);
                if (coll is null) continue;
                try
                {
                    var asArray = (IObjectArray)coll;
                    int catHr = cdl.AppendCategory(category, asArray);
                    if (catHr < 0) DiagnosticLog.HResultFailed(LogCategory.Shell, "JumpList.AppendCategory", catHr);
                }
                finally { Marshal.ReleaseComObject(coll); }
            }

            if (showRecent)
            {
                int recHr = cdl.AppendKnownCategory(KnownDestCategory.Recent);
                if (recHr < 0) DiagnosticLog.HResultFailed(LogCategory.Shell, "JumpList.AppendKnownCategory.Recent", recHr);
            }
            if (showFrequent)
            {
                int freqHr = cdl.AppendKnownCategory(KnownDestCategory.Frequent);
                if (freqHr < 0) DiagnosticLog.HResultFailed(LogCategory.Shell, "JumpList.AppendKnownCategory.Frequent", freqHr);
            }

            int commitHr = cdl.CommitList();
            if (commitHr < 0)
                DiagnosticLog.HResultFailed(LogCategory.Shell, "JumpList.CommitList", commitHr);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] JumpList unpackaged update threw: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (removed is not null)
            {
                try { Marshal.ReleaseComObject(removed); } catch { }
            }
            if (cdl is not null)
            {
                try { Marshal.ReleaseComObject(cdl); } catch { }
            }
        }
    }

    /// <summary>
    /// Test hook — clear the unpackaged jump list for this AumId.
    /// Best-effort.
    /// </summary>
    public static void DeleteListForTests(string appUserModelId)
    {
        try
        {
            var cdl = (ICustomDestinationList)new DestinationList();
            try
            {
                cdl.SetAppID(appUserModelId);
                _ = cdl.DeleteList(appUserModelId);
            }
            finally { Marshal.ReleaseComObject(cdl); }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] DeleteListForTests failed: {ex.Message}");
        }
    }

    private static IObjectCollection? BuildShellLinkArray(IReadOnlyList<JumpListItem> items)
    {
        IObjectCollection? coll;
        try { coll = (IObjectCollection)new EnumerableObjectCollection(); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] EnumerableObjectCollection alloc failed: {ex.Message}");
            return null;
        }

        // Path to the current executable. Jump-list shell links re-launch
        // the same exe with the supplied arguments.
        string exePath;
        try { exePath = Environment.ProcessPath ?? string.Empty; }
        catch { exePath = string.Empty; }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            try
            {
                var link = (IShellLinkW)new CShellLink();
                try
                {
                    if (item.Kind == JumpListItemKind.Separator)
                    {
                        ApplySeparator(link);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(exePath))
                            link.SetPath(exePath);
                        link.SetArguments(item.Arguments ?? string.Empty);
                        if (!string.IsNullOrEmpty(item.Description))
                            link.SetDescription(item.Description);
                        // Icon: WindowIcon.FromPath maps to filesystem; resource
                        // URIs aren't honoured by ICustomDestinationList without
                        // additional resource-extraction work that the unpackaged
                        // path doesn't carry today (silently skipped).
                        if (item.Icon is { IsResource: false, Source: var iconPath } && !string.IsNullOrEmpty(iconPath))
                        {
                            try { link.SetIconLocation(iconPath, 0); } catch { }
                        }

                        ApplyTitle(link, item.Title);
                    }

                    coll.AddObject(link);
                }
                finally { Marshal.ReleaseComObject(link); }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Reactor] BuildShellLinkArray item '{item.Title}' failed: {ex.Message}");
            }
        }
        return coll;
    }

    private static void ApplyTitle(IShellLinkW link, string title)
    {
        var store = (IPropertyStore)link;
        var titleKey = new PropertyKey(FmtIdSummaryInfo, 2);   // PKEY_Title
        var pv = new PropVariant();
        try
        {
            InitPropVariantFromString(title, out pv);
            store.SetValue(ref titleKey, ref pv);
            store.Commit();
        }
        finally
        {
            try { PropVariantClear(ref pv); } catch { }
        }
    }

    private static void ApplySeparator(IShellLinkW link)
    {
        var store = (IPropertyStore)link;
        var sepKey = new PropertyKey(FmtIdAppUserModel, 6);    // PKEY_AppUserModel_IsDestListSeparator
        var pv = new PropVariant();
        try
        {
            InitPropVariantFromBoolean(true, out pv);
            store.SetValue(ref sepKey, ref pv);
            store.Commit();
        }
        finally
        {
            try { PropVariantClear(ref pv); } catch { }
        }
    }

    // ── COM definitions ───────────────────────────────────────────────────

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6"), ClassInterface(ClassInterfaceType.None)]
    private class DestinationList { }

    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a"), ClassInterface(ClassInterfaceType.None)]
    private class EnumerableObjectCollection { }

    [ComImport, Guid("00021401-0000-0000-c000-000000000046"), ClassInterface(ClassInterfaceType.None)]
    private class CShellLink { }

    [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        [PreserveSig] void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        [PreserveSig] int BeginList(out uint pcMaxSlots, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IObjectArray ppv);
        [PreserveSig] int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory,
            [MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
        [PreserveSig] int AppendKnownCategory(KnownDestCategory category);
        [PreserveSig] int AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
        [PreserveSig] int CommitList();
        [PreserveSig] int GetRemovedDestinations(ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig] int DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        [PreserveSig] int AbortList();
    }

    private enum KnownDestCategory : uint { Frequent = 1, Recent = 2 }

    [ComImport, Guid("92ca9dcd-5622-4bba-a805-5e9f541bd8c9"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        [PreserveSig] int GetCount(out uint pcObjects);
        [PreserveSig] int GetAt(uint uiIndex, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        // IObjectArray
        [PreserveSig] int GetCount(out uint pcObjects);
        [PreserveSig] int GetAt(uint uiIndex, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
        // IObjectCollection
        [PreserveSig] int AddObject([MarshalAs(UnmanagedType.IUnknown)] object punk);
        [PreserveSig] int AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray source);
        [PreserveSig] int RemoveObjectAt(uint uiIndex);
        [PreserveSig] int Clear();
    }

    [ComImport, Guid("000214f9-0000-0000-c000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] global::System.Text.StringBuilder pszFile,
            int cch, IntPtr pfd, uint fFlags);
        [PreserveSig] int GetIDList(out IntPtr ppidl);
        [PreserveSig] int SetIDList(IntPtr pidl);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] global::System.Text.StringBuilder pszName, int cch);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] global::System.Text.StringBuilder pszDir, int cch);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] global::System.Text.StringBuilder pszArgs, int cch);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        [PreserveSig] int GetHotkey(out short pwHotkey);
        [PreserveSig] int SetHotkey(short wHotkey);
        [PreserveSig] int GetShowCmd(out int piShowCmd);
        [PreserveSig] int SetShowCmd(int iShowCmd);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] global::System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        [PreserveSig] int Resolve(IntPtr hwnd, uint fFlags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant pv);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public PropertyKey(Guid fmtid, uint pid) { this.fmtid = fmtid; this.pid = pid; }
    }

    // PROPVARIANT — only the variants we set (LPWSTR, BOOL).
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr p1;
        public IntPtr p2;
    }

    private static readonly Guid FmtIdSummaryInfo = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9");
    private static readonly Guid FmtIdAppUserModel = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    [DllImport("propsys.dll", PreserveSig = false)]
    private static extern void InitPropVariantFromString(
        [MarshalAs(UnmanagedType.LPWStr)] string psz, out PropVariant ppropvar);

    [DllImport("propsys.dll", PreserveSig = false)]
    private static extern void InitPropVariantFromBoolean(
        [MarshalAs(UnmanagedType.Bool)] bool fVal, out PropVariant ppropvar);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);
}
