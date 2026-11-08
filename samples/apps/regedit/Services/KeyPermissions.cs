using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ReactorRegedit.Core;

namespace ReactorRegedit.Services;

/// <summary>
/// Edit > Permissions: shows the native Windows security editor (aclui!EditSecurity) for a
/// registry key, the same property sheet classic regedit uses. The key's DACL/owner are read
/// with RegGetKeySecurity and written back with RegSetKeySecurity. When the user may not
/// change the DACL (no WRITE_DAC) the editor opens read-only; likewise for the owner.
/// </summary>
internal static unsafe partial class KeyPermissions
{
    /// <summary>Opens the modal security editor. Returns an error message, or null on success/cancel.</summary>
    public static string? Edit(nint owner, string path)
    {
        if (path.Length == 0) return "Select a key first.";
        if (RegPath.IsRemote(path)) return "Permissions can't be edited on a remote registry from this app.";

        // Probe what we may do; we need at least READ_CONTROL to show anything.
        if (!CanOpen(path, READ_CONTROL)) return $"Cannot open {RegPath.Leaf(path)}: Access is denied.";
        var canWriteDac = CanOpen(path, WRITE_DAC);
        var canWriteOwner = CanOpen(path, WRITE_OWNER);

        var info = new SecurityInformation(path, canWriteDac, canWriteOwner);
        var wrappers = new StrategyBasedComWrappers();
        nint unknown = wrappers.GetOrCreateComInterfaceForObject(info, CreateComInterfaceFlags.None);
        try
        {
            var iid = typeof(ISecurityInformation).GUID;
            var hr = Marshal.QueryInterface(unknown, in iid, out var psi);
            if (hr < 0) return $"Cannot show permissions (0x{hr:X8}).";
            try
            {
                EditSecurity(owner, psi);
            }
            finally { Marshal.Release(psi); }
            return info.LastError;
        }
        finally
        {
            Marshal.Release(unknown);
            info.Dispose();
        }
    }

    private static bool CanOpen(string path, int access)
    {
        try
        {
            using var key = RegistryService.OpenKeyWithRights(path, access);
            return true;
        }
        catch (RegistryOperationException) { return false; }
    }

    // ── Native ───────────────────────────────────────────────────────

    internal const int READ_CONTROL = 0x00020000;
    internal const int WRITE_DAC = 0x00040000;
    internal const int WRITE_OWNER = 0x00080000;

    private const uint OWNER_SECURITY_INFORMATION = 0x1;
    private const uint GROUP_SECURITY_INFORMATION = 0x2;
    private const uint DACL_SECURITY_INFORMATION = 0x4;

    private const uint SI_EDIT_PERMS = 0x0;
    private const uint SI_EDIT_OWNER = 0x1;
    private const uint SI_CONTAINER = 0x4;
    private const uint SI_READONLY = 0x8;
    private const uint SI_ADVANCED = 0x10;
    private const uint SI_OWNER_READONLY = 0x40;
    private const uint SI_EDIT_EFFECTIVE = 0x20000;
    private const uint SI_NO_ACL_PROTECT = 0x200;

    private const uint SI_ACCESS_SPECIFIC = 0x00010000;
    private const uint SI_ACCESS_GENERAL = 0x00020000;
    private const uint SI_ACCESS_CONTAINER = 0x00040000;

    private const byte CONTAINER_INHERIT_ACE = 0x2;
    private const byte INHERIT_ONLY_ACE = 0x8;

    private const int S_OK = 0;
    private const int E_FAIL = unchecked((int)0x80004005);
    private const int E_ACCESSDENIED = unchecked((int)0x80070005);

    [StructLayout(LayoutKind.Sequential)]
    private struct SI_OBJECT_INFO
    {
        public uint dwFlags;
        public nint hInstance;
        public nint pszServerName;
        public nint pszObjectName;
        public nint pszPageTitle;
        public Guid guidObjectType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SI_ACCESS
    {
        public nint pguid;
        public uint mask;
        public nint pszName;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SI_INHERIT_TYPE
    {
        public nint pguid;
        public uint dwFlags;
        public nint pszName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GENERIC_MAPPING
    {
        public uint GenericRead, GenericWrite, GenericExecute, GenericAll;
    }

    [LibraryImport("aclui.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EditSecurity(nint hwndOwner, nint psi);

    [LibraryImport("advapi32.dll")]
    private static partial void MapGenericMask(uint* accessMask, GENERIC_MAPPING* mapping);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalAlloc(uint flags, nuint bytes);

    [GeneratedComInterface]
    [Guid("965FC360-16FF-11d0-91CB-00AA00BBB723")]
    internal partial interface ISecurityInformation
    {
        [PreserveSig] int GetObjectInformation(nint pObjectInfo);
        [PreserveSig] int GetSecurity(uint requestedInformation, nint ppSecurityDescriptor, int fDefault);
        [PreserveSig] int SetSecurity(uint securityInformation, nint pSecurityDescriptor);
        [PreserveSig] int GetAccessRights(nint pguidObjectType, uint dwFlags, nint ppAccess, nint pcAccesses, nint piDefaultAccess);
        [PreserveSig] int MapGeneric(nint pguidObjectType, nint pAceFlags, nint pMask);
        [PreserveSig] int GetInheritTypes(nint ppInheritTypes, nint pcInheritTypes);
        [PreserveSig] int PropertySheetPageCallback(nint hwnd, uint uMsg, int uPage);
    }

    [GeneratedComClass]
    internal sealed partial class SecurityInformation : ISecurityInformation, IDisposable
    {
        private readonly string _path;
        private readonly bool _canWriteDac;
        private readonly bool _canWriteOwner;
        private readonly List<nint> _strings = [];
        private readonly nint _objectName;
        private readonly nint _access;
        private readonly int _accessCount;
        private readonly nint _inherit;
        private readonly int _inheritCount;

        public string? LastError { get; private set; }

        public SecurityInformation(string path, bool canWriteDac, bool canWriteOwner)
        {
            _path = path;
            _canWriteDac = canWriteDac;
            _canWriteOwner = canWriteOwner;
            _objectName = Str(RegPath.Leaf(path));

            // The same rights classic regedit lists (KEY_* masks).
            (uint Mask, string Name, uint Flags)[] rights =
            [
                (0xF003F, "Full Control", SI_ACCESS_GENERAL | SI_ACCESS_SPECIFIC),
                (0x20019, "Read", SI_ACCESS_GENERAL),
                (0x0001, "Query Value", SI_ACCESS_SPECIFIC),
                (0x0002, "Set Value", SI_ACCESS_SPECIFIC),
                (0x0004, "Create Subkey", SI_ACCESS_SPECIFIC),
                (0x0008, "Enumerate Subkeys", SI_ACCESS_SPECIFIC),
                (0x0010, "Notify", SI_ACCESS_SPECIFIC),
                (0x0020, "Create Link", SI_ACCESS_SPECIFIC),
                (0x10000, "Delete", SI_ACCESS_SPECIFIC),
                (0x40000, "Write DAC", SI_ACCESS_SPECIFIC),
                (0x80000, "Write Owner", SI_ACCESS_SPECIFIC),
                (0x20000, "Read Control", SI_ACCESS_SPECIFIC),
            ];
            _accessCount = rights.Length;
            _access = Marshal.AllocHGlobal(sizeof(SI_ACCESS) * rights.Length);
            var a = (SI_ACCESS*)_access;
            for (int i = 0; i < rights.Length; i++)
                a[i] = new SI_ACCESS { pguid = 0, mask = rights[i].Mask, pszName = Str(rights[i].Name), dwFlags = rights[i].Flags };

            (uint Flags, string Name)[] inherit =
            [
                (0, "This key only"),
                (CONTAINER_INHERIT_ACE, "This key and subkeys"),
                (CONTAINER_INHERIT_ACE | INHERIT_ONLY_ACE, "Subkeys only"),
            ];
            _inheritCount = inherit.Length;
            _inherit = Marshal.AllocHGlobal(sizeof(SI_INHERIT_TYPE) * inherit.Length);
            var t = (SI_INHERIT_TYPE*)_inherit;
            for (int i = 0; i < inherit.Length; i++)
                t[i] = new SI_INHERIT_TYPE { pguid = 0, dwFlags = inherit[i].Flags, pszName = Str(inherit[i].Name) };
        }

        private nint Str(string s)
        {
            var p = Marshal.StringToHGlobalUni(s);
            _strings.Add(p);
            return p;
        }

        public int GetObjectInformation(nint pObjectInfo)
        {
            var flags = SI_EDIT_PERMS | SI_EDIT_OWNER | SI_CONTAINER | SI_ADVANCED | SI_EDIT_EFFECTIVE;
            if (!_canWriteDac) flags |= SI_READONLY | SI_NO_ACL_PROTECT;
            if (!_canWriteOwner) flags |= SI_OWNER_READONLY;
            *(SI_OBJECT_INFO*)pObjectInfo = new SI_OBJECT_INFO
            {
                dwFlags = flags,
                hInstance = 0,
                pszServerName = 0,
                pszObjectName = _objectName,
                pszPageTitle = 0,
            };
            return S_OK;
        }

        public int GetSecurity(uint requestedInformation, nint ppSecurityDescriptor, int fDefault)
        {
            *(nint*)ppSecurityDescriptor = 0;
            // We never ask for SACL rights (it needs SeSecurityPrivilege), so drop that bit.
            requestedInformation &= OWNER_SECURITY_INFORMATION | GROUP_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION;
            try
            {
                var sd = RegistryService.GetKeySecurity(_path, requestedInformation);
                // aclui frees the descriptor with LocalFree.
                var mem = LocalAlloc(0x0040 /* LPTR */, (nuint)sd.Length);
                if (mem == 0) return E_FAIL;
                Marshal.Copy(sd, 0, mem, sd.Length);
                *(nint*)ppSecurityDescriptor = mem;
                return S_OK;
            }
            catch (RegistryOperationException ex)
            {
                LastError = ex.Message;
                return ex.IsAccessDenied ? E_ACCESSDENIED : E_FAIL;
            }
        }

        public int SetSecurity(uint securityInformation, nint pSecurityDescriptor)
        {
            try
            {
                RegistryService.SetKeySecurity(_path, securityInformation, pSecurityDescriptor);
                LastError = null;
                return S_OK;
            }
            catch (RegistryOperationException ex)
            {
                LastError = ex.Message;
                return ex.IsAccessDenied ? E_ACCESSDENIED : E_FAIL;
            }
        }

        public int GetAccessRights(nint pguidObjectType, uint dwFlags, nint ppAccess, nint pcAccesses, nint piDefaultAccess)
        {
            *(nint*)ppAccess = _access;
            *(uint*)pcAccesses = (uint)_accessCount;
            *(uint*)piDefaultAccess = 1; // Read
            return S_OK;
        }

        public int MapGeneric(nint pguidObjectType, nint pAceFlags, nint pMask)
        {
            var mapping = new GENERIC_MAPPING
            {
                GenericRead = 0x20019,      // KEY_READ
                GenericWrite = 0x20006,     // KEY_WRITE
                GenericExecute = 0x20019,   // KEY_EXECUTE
                GenericAll = 0xF003F,       // KEY_ALL_ACCESS
            };
            MapGenericMask((uint*)pMask, &mapping);
            return S_OK;
        }

        public int GetInheritTypes(nint ppInheritTypes, nint pcInheritTypes)
        {
            *(nint*)ppInheritTypes = _inherit;
            *(uint*)pcInheritTypes = (uint)_inheritCount;
            return S_OK;
        }

        public int PropertySheetPageCallback(nint hwnd, uint uMsg, int uPage) => S_OK;

        public void Dispose()
        {
            foreach (var s in _strings) Marshal.FreeHGlobal(s);
            _strings.Clear();
            Marshal.FreeHGlobal(_access);
            Marshal.FreeHGlobal(_inherit);
        }
    }
}
