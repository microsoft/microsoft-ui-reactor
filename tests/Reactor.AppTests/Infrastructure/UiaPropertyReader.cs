using System.Globalization;
using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// In-process UI Automation (UIA) property reader used <b>only</b> as a fallback for the
/// handful of UIA properties that winapp 0.3.2 <c>get-property</c> cannot surface.
///
/// Empirically verified against winapp 0.3.2 (live Accessibility_Showcase fixture) — it
/// returns <c>null</c> for exactly these 10: LocalizedControlType, IsRequiredForForm,
/// FullDescription, HeadingLevel, LandmarkType, Level, PositionInSet, SizeOfSet, ItemStatus,
/// LiveSetting. (It DOES return Name, HelpText and AccessKey, which the GetAttribute path
/// takes from winapp directly — those are mapped here only as a safety net.) WinAppDriver
/// could read the missing 10 via the native UIA client, so we reproduce just that read path
/// with a minimal CUIAutomation COM interop — no Appium, read-only, and the primary driver
/// remains <see cref="WinAppUi"/>.
///
/// TODO (winappCli gap): once <c>winapp ui get-property</c> exposes the 10 properties above,
/// delete this COM interop and route those reads back through <see cref="WinAppUi"/>.
/// At that point the only implementation of <see cref="IUiaPropertyReader"/> can be a thin
/// winapp shim, or <see cref="Handles"/> can return <c>false</c> so the fallback is skipped.
/// The interface exists precisely so callers don't need to change when that happens.
/// </summary>
public sealed class UiaPropertyReader : IUiaPropertyReader
{
    private readonly long _hostHwnd;
    private readonly IUIAutomation _uia;

    public UiaPropertyReader(long hostHwnd)
    {
        _hostHwnd = hostHwnd;
        _uia = (IUIAutomation)new CUIAutomation();
    }

    // ─── UIA PROPERTYIDs (UIAutomationClient.h) ──────────────────────────────
    private const int UIA_NamePropertyId = 30005;
    private const int UIA_AutomationIdPropertyId = 30011;
    private const int UIA_LocalizedControlTypePropertyId = 30004;
    private const int UIA_HelpTextPropertyId = 30013;
    private const int UIA_HasKeyboardFocusPropertyId = 30008;
    private const int UIA_ItemStatusPropertyId = 30026;
    private const int UIA_IsRequiredForFormPropertyId = 30025;
    private const int UIA_FullDescriptionPropertyId = 30159;
    private const int UIA_LevelPropertyId = 30154;
    private const int UIA_PositionInSetPropertyId = 30152;
    private const int UIA_SizeOfSetPropertyId = 30153;
    private const int UIA_LandmarkTypePropertyId = 30157;
    private const int UIA_LiveSettingPropertyId = 30135;
    private const int UIA_HeadingLevelPropertyId = 30173;
    private const int UIA_AccessKeyPropertyId = 30007;
    private const int UIA_SelectionItemIsSelectedPropertyId = 30079;

    private const int TreeScope_Descendants = 4;

    // HeadingLevel enum (UIA): HeadingLevel_None=80050, HeadingLevel1..9 = 80051..80059.
    private const int UIA_HeadingLevel_None = 80050;

    /// <summary>The set of GetAttribute names this reader knows how to map.</summary>
    public bool Handles(string property) => MapName(property) != 0;

    private static int MapName(string property) => property switch
    {
        "Name" => UIA_NamePropertyId,
        "LocalizedControlType" => UIA_LocalizedControlTypePropertyId,
        "HelpText" => UIA_HelpTextPropertyId,
        "HasKeyboardFocus" => UIA_HasKeyboardFocusPropertyId,
        "ItemStatus" => UIA_ItemStatusPropertyId,
        "IsRequiredForForm" => UIA_IsRequiredForFormPropertyId,
        "FullDescription" => UIA_FullDescriptionPropertyId,
        "Level" => UIA_LevelPropertyId,
        "PositionInSet" => UIA_PositionInSetPropertyId,
        "SizeOfSet" => UIA_SizeOfSetPropertyId,
        "LandmarkType" => UIA_LandmarkTypePropertyId,
        "LiveSetting" => UIA_LiveSettingPropertyId,
        "HeadingLevel" => UIA_HeadingLevelPropertyId,
        "AccessKey" => UIA_AccessKeyPropertyId,
        "IsSelected" or "SelectionItemIsSelected" => UIA_SelectionItemIsSelectedPropertyId,
        _ => 0,
    };

    /// <summary>
    /// Read a UIA property of the element with the given AutomationId from the Host window
    /// tree. Returns a string formatted to match the values the test suite asserts against,
    /// or null when the property is unset / element not found.
    /// </summary>
    public string? ReadByAutomationId(string automationId, string property)
    {
        var propId = MapName(property);
        if (propId == 0) return null;

        var element = FindByAutomationId(automationId);
        if (element is null) return null;

        object? raw;
        try { raw = element.GetCurrentPropertyValue(propId); }
        catch { return null; }

        return Format(property, raw);
    }

    /// <summary>
    /// Read a UIA property of the first element whose Name matches <paramref name="name"/>.
    /// Used for elements that carry no AutomationId (e.g. TreeView text nodes), where the
    /// only stable handle is the caption.
    /// </summary>
    public string? ReadByName(string name, string property)
    {
        var propId = MapName(property);
        if (propId == 0) return null;

        var element = FindByProperty(UIA_NamePropertyId, name);
        if (element is null) return null;

        object? raw;
        try { raw = element.GetCurrentPropertyValue(propId); }
        catch { return null; }

        return Format(property, raw);
    }

    private IUIAutomationElement? FindByAutomationId(string automationId) =>
        FindByProperty(UIA_AutomationIdPropertyId, automationId);

    private IUIAutomationElement? FindByProperty(int propertyId, string value)
    {
        IUIAutomationElement root;
        try { root = _uia.ElementFromHandle((IntPtr)_hostHwnd); }
        catch { return null; }
        if (root is null) return null;

        IUIAutomationCondition cond = _uia.CreatePropertyCondition(propertyId, value);
        try
        {
            return root.FindFirst(TreeScope_Descendants, cond);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// AutomationId of the system-wide focused element, or empty string if none / on error.
    /// Used for WinForms focus assertions (winapp's get-focused is unreliable when the window
    /// isn't foreground; this reads the live UIA focus instead).
    /// </summary>
    public string GetFocusedAutomationId()
    {
        try
        {
            var focused = _uia.GetFocusedElement();
            if (focused is null) return "";
            var raw = focused.GetCurrentPropertyValue(UIA_AutomationIdPropertyId);
            return raw?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string? Format(string property, object? raw)
    {
        if (raw is null) return null;

        switch (property)
        {
            case "IsRequiredForForm":
            case "HasKeyboardFocus":
            case "IsSelected":
            case "SelectionItemIsSelected":
                return ToBool(raw) ? "True" : "False";

            case "LiveSetting": // Off=0, Polite=1, Assertive=2
                return ToInt(raw).ToString(CultureInfo.InvariantCulture);

            case "LandmarkType":
            {
                var v = ToInt(raw);
                return v == 0 ? null : v.ToString(CultureInfo.InvariantCulture);
            }

            case "Level":
            case "PositionInSet":
            case "SizeOfSet":
            {
                var v = ToInt(raw);
                return v <= 0 ? null : v.ToString(CultureInfo.InvariantCulture);
            }

            case "HeadingLevel":
            {
                var v = ToInt(raw);
                if (v <= UIA_HeadingLevel_None) return null;
                return (v - UIA_HeadingLevel_None).ToString(CultureInfo.InvariantCulture);
            }

            default:
            {
                if (raw is not string s)
                    return null;
                return string.IsNullOrEmpty(s) ? null : s;
            }
        }
    }

    private static bool ToBool(object raw) => raw switch
    {
        bool b => b,
        int i => i != 0,
        string s => bool.TryParse(s, out var b) && b,
        _ => false,
    };

    private static int ToInt(object raw) => raw switch
    {
        int i => i,
        double d => (int)d,
        bool b => b ? 1 : 0,
        string s => int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var i) ? i : 0,
        _ => 0,
    };

    // ─── Minimal CUIAutomation COM interop ───────────────────────────────────
    // Only the vtable slots up to the methods we call are exercised; the rest are
    // declared (IntPtr-typed) purely to keep the vtable offsets correct.

    [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
    private class CUIAutomation { }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements(IntPtr a, IntPtr b, out int areSame);                    // 1
        void CompareRuntimeIds(IntPtr a, IntPtr b, out int areSame);                  // 2
        void GetRootElement(out IntPtr root);                                         // 3
        IUIAutomationElement ElementFromHandle(IntPtr hwnd);                          // 4
        IUIAutomationElement ElementFromPoint(tagPOINT pt);                           // 5
        IUIAutomationElement GetFocusedElement();                                     // 6
        void GetRootElementBuildCache(IntPtr cacheRequest, out IntPtr element);       // 7
        void ElementFromHandleBuildCache(IntPtr hwnd, IntPtr cr, out IntPtr element); // 8
        void ElementFromPointBuildCache(tagPOINT pt, IntPtr cr, out IntPtr element);  // 9
        void GetFocusedElementBuildCache(IntPtr cr, out IntPtr element);              // 10
        void CreateTreeWalker(IntPtr condition, out IntPtr walker);                   // 11
        void get_ControlViewWalker(out IntPtr walker);                               // 12
        void get_ContentViewWalker(out IntPtr walker);                               // 13
        void get_RawViewWalker(out IntPtr walker);                                   // 14
        void get_RawViewCondition(out IntPtr condition);                             // 15
        void get_ControlViewCondition(out IntPtr condition);                         // 16
        void get_ContentViewCondition(out IntPtr condition);                         // 17
        void CreateCacheRequest(out IntPtr cacheRequest);                            // 18
        void CreateTrueCondition(out IntPtr condition);                              // 19
        void CreateFalseCondition(out IntPtr condition);                            // 20
        IUIAutomationCondition CreatePropertyCondition(                              // 21
            int propertyId, [MarshalAs(UnmanagedType.Struct)] object value);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus();                                                             // 1
        void GetRuntimeId(out IntPtr runtimeId);                                     // 2
        IUIAutomationElement FindFirst(int scope, IUIAutomationCondition condition); // 3
        void FindAll(int scope, IUIAutomationCondition condition, out IntPtr found); // 4
        void FindFirstBuildCache(int scope, IntPtr cond, IntPtr cr, out IntPtr el);  // 5
        void FindAllBuildCache(int scope, IntPtr cond, IntPtr cr, out IntPtr els);   // 6
        void BuildUpdatedCache(IntPtr cacheRequest, out IntPtr updated);             // 7
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCurrentPropertyValue(int propertyId);                              // 8
    }

    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCondition { }

    [StructLayout(LayoutKind.Sequential)]
    private struct tagPOINT
    {
        public int x;
        public int y;
    }
}
