using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Hosting.Shell;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public partial class JumpListRemovalTests
{
    [Fact]
    public void Packaged_Replacement_Reads_Removals_Before_Clearing_And_Publishes_Retained_Items()
    {
        var old = global::Windows.UI.StartScreen.JumpListItem.CreateWithArguments("app://removed", "Old");
        var nativeItems = new List<global::Windows.UI.StartScreen.JumpListItem> { old };
        var keep = JumpListItem.ForUri("Keep", "app://keep", description: "Retained description",
            groupCategory: "Recent");
        var separator = new JumpListItem("", "", JumpListItemKind.Separator);
        // Intentionally lazy, as in the real WinRT load path. Clearing first
        // would erase the removal observations and resurrect the removed item.
        var removedArguments = nativeItems.Select(item => item.Arguments);

        var observed = JumpList.ReplacePackagedItems(nativeItems,
            new[] { JumpListItem.ForUri("Removed", old.Arguments), separator, keep }, removedArguments);

        Assert.Equal(new[] { "app://removed" }, observed);

        Assert.Equal(2, nativeItems.Count);
        Assert.Equal(global::Windows.UI.StartScreen.JumpListItemKind.Separator, nativeItems[0].Kind);
        Assert.Equal(keep.Title, nativeItems[1].DisplayName);
        Assert.Equal(keep.Arguments, nativeItems[1].Arguments);
        Assert.Equal(keep.Description, nativeItems[1].Description);
        Assert.Equal(keep.GroupCategory, nativeItems[1].GroupName);
        Assert.DoesNotContain(nativeItems, item => item.Arguments == old.Arguments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publishing_Removal_Omits_Only_Matching_Destinations(bool customCategoriesOnly)
    {
        var removed = JumpListItem.ForUri("Removed", "app://removed", groupCategory: "Recent");
        var retained = JumpListItem.ForUri("Keep", "app://keep", groupCategory: "Recent");
        var otherGroup = removed with { Title = "Also removed", GroupCategory = "Favorites" };
        var items = new[] { removed, retained, otherGroup };

        var published = JumpList.ExcludeRemovedItems(items, new[] { removed.Arguments }, customCategoriesOnly);

        // The positive control prevents an implementation that drops the entire
        // category (or every entry) from satisfying the removal assertion.
        Assert.Equal(new[] { retained }, published);
        Assert.Equal(new[] { removed, retained, otherGroup }, items);
    }

    [Fact]
    public void Unpackaged_Removals_Do_Not_Remove_User_Tasks_Or_Separators()
    {
        var task = JumpListItem.ForUri("Task", "app://same");
        var ungrouped = task with { Kind = JumpListItemKind.Custom };
        var separator = new JumpListItem("", "app://same", JumpListItemKind.Separator);
        var destination = task with { Kind = JumpListItemKind.Custom, GroupCategory = "Recent" };

        var published = JumpList.ExcludeRemovedItems(
            new[] { task, ungrouped, separator, destination }, new[] { task.Arguments }, true);

        Assert.Equal(new[] { task, ungrouped, separator }, published);
    }

    [Fact]
    public void Packaged_Removals_Apply_To_Ungrouped_Entries_But_Not_Separators()
    {
        var task = JumpListItem.ForUri("Removed", "app://same");
        var keep = JumpListItem.ForUri("Keep", "app://keep");
        var separator = new JumpListItem("", task.Arguments, JumpListItemKind.Separator);

        var published = JumpList.ExcludeRemovedItems(
            new[] { task, separator, keep }, new[] { task.Arguments }, false);

        Assert.Equal(new[] { separator, keep }, published);
    }

    [Fact]
    public void Argument_Identity_Is_Ordinal_And_Does_Not_Normalize_Command_Lines()
    {
        var removed = JumpListItem.ForUri("Removed", "app://file/ABC", groupCategory: "Files");
        var differentCase = removed with { Arguments = "app://file/abc" };
        var quoted = removed with { Arguments = "\"app://file/ABC\"" };
        var published = JumpList.ExcludeRemovedItems(
            new[] { removed, differentCase, quoted }, new[] { removed.Arguments }, true);

        Assert.Equal(new[] { differentCase, quoted }, published);
    }

    [Fact]
    public void Removal_Is_A_Snapshot_Not_A_Permanent_Blacklist()
    {
        var item = JumpListItem.ForUri("Document", "app://file", groupCategory: "Files");
        Assert.Empty(JumpList.ExcludeRemovedItems(new[] { item }, new[] { item.Arguments }, true));

        // Once Windows has acknowledged the removal and the app supplies the
        // destination following new user activity, the wrapper must allow it.
        Assert.Equal(new[] { item }, JumpList.ExcludeRemovedItems(
            new[] { item }, Array.Empty<string>(), true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_Arguments_Can_Identify_A_Removed_Destination(bool customCategoriesOnly)
    {
        var removed = JumpListItem.ForUri("Default", "", groupCategory: "Files");
        var keep = removed with { Arguments = "open" };
        Assert.Equal(new[] { keep }, JumpList.ExcludeRemovedItems(
            new[] { removed, keep }, new[] { "" }, customCategoriesOnly));
    }

    [Fact]
    public void Packaged_Replacement_Returns_Removals_Not_In_The_New_Batch()
    {
        var nativeItems = new List<global::Windows.UI.StartScreen.JumpListItem>
        {
            global::Windows.UI.StartScreen.JumpListItem.CreateWithArguments("app://absent", "Removed"),
        };
        var observed = JumpList.ReplacePackagedItems(nativeItems, Array.Empty<JumpListItem>(),
            nativeItems.Select(item => item.Arguments));
        Assert.Equal(new[] { "app://absent" }, observed);
        Assert.Empty(nativeItems);
    }

    [Fact]
    public void Native_Transaction_Reports_Consumed_Removals_And_Keeps_User_Tasks()
    {
        using var apartment = JumpListComInterop.InitializeApartment();
        using var removed = JumpListComInterop.BuildShellLinkArray(new[]
        {
            JumpListItem.ForUri("Removed custom", "app://custom"),
            JumpListItem.ForUri("Removed task argument", "app://task"),
            JumpListItem.ForUri("Absent from batch", "app://absent"),
        });
        var shell = new RecordingDestinationList(removed.Pointer);
        var result = JumpListComInterop.UpdateUnpackaged(shell, new[]
        {
            JumpListItem.ForUri("Drop", "app://custom", groupCategory: "Files"),
            JumpListItem.ForUri("Keep", "app://keep", groupCategory: "Files"),
            JumpListItem.ForUri("Task", "app://task"),
        }, showRecent: true, showFrequent: true);
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "app://absent", "app://custom", "app://task" }, result.RemovedArguments.Order());
        Assert.Equal(new[] { "app://keep" }, shell.CategoryArguments);
        Assert.Equal(new[] { "app://task" }, shell.TaskArguments);
        Assert.Equal(new[] { JumpListComInterop.KnownDestCategory.Recent,
            JumpListComInterop.KnownDestCategory.Frequent }, shell.KnownCategories);
        Assert.True(shell.Committed);
        Assert.False(shell.Aborted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(unchecked((int)0x80004005))]
    public void Native_Clear_Returns_Observed_Removals_Even_When_Commit_Fails(int commitResult)
    {
        using var apartment = JumpListComInterop.InitializeApartment();
        using var removed = JumpListComInterop.BuildShellLinkArray(new[]
        {
            JumpListItem.ForUri("Absent from empty batch", "app://absent"),
        });
        var shell = new RecordingDestinationList(removed.Pointer) { CommitResult = commitResult };

        var result = JumpListComInterop.UpdateUnpackaged(shell, Array.Empty<JumpListItem>(),
            showRecent: false, showFrequent: false);

        Assert.Equal(commitResult >= 0, result.Succeeded);
        Assert.Equal(new[] { "app://absent" }, result.RemovedArguments);
        Assert.Empty(shell.CategoryArguments);
        Assert.Empty(shell.TaskArguments);
        Assert.True(shell.Committed);
        Assert.Equal(commitResult < 0, shell.Aborted);
    }

    private sealed class RecordingDestinationList(nint removed) : JumpListComInterop.ICustomDestinationList
    {
        public IReadOnlyList<string> CategoryArguments = Array.Empty<string>();
        public IReadOnlyList<string> TaskArguments = Array.Empty<string>();
        public List<JumpListComInterop.KnownDestCategory> KnownCategories = new();
        public bool Committed;
        public bool Aborted;
        public int CommitResult;
        public int SetAppID(string appId) => 0;
        public int BeginList(out uint maxSlots, in Guid iid, out nint value)
        {
            maxSlots = 10;
            var interfaceId = iid;
            return Marshal.QueryInterface(removed, in interfaceId, out value);
        }
        public int AppendCategory(string category, JumpListComInterop.IObjectArray array)
        {
            CategoryArguments = JumpListComInterop.ReadRemovedArguments(array, Environment.ProcessPath!);
            return 0;
        }
        public int AddUserTasks(JumpListComInterop.IObjectArray array)
        {
            TaskArguments = JumpListComInterop.ReadRemovedArguments(array, Environment.ProcessPath!);
            return 0;
        }
        public int AppendKnownCategory(JumpListComInterop.KnownDestCategory category)
        {
            KnownCategories.Add(category);
            return 0;
        }
        public int CommitList() { Committed = true; return CommitResult; }
        public int AbortList() { Aborted = true; return 0; }
        public int GetRemovedDestinations(in Guid iid, out nint value) => BeginList(out _, in iid, out value);
        public int DeleteList(string appId) => 0;
    }

    [Fact]
    public void Native_Shell_Links_RoundTrip_Arguments_And_Filter_By_Executable()
    {
        // Real shell links / IObjectArray, but not the user's live Jump List.
        // This exercises COM decoding without inventing a writable RemovedByUser
        // property or changing any shell-owned destination tracking data.
        var unicode = "app://device/设备?name=\"日本語\"";
        var longArguments = "open " + new string('x', 2048);
        using var apartment = JumpListComInterop.InitializeApartment();
        var collection = JumpListComInterop.BuildShellLinkArray(new[]
        {
            JumpListItem.ForUri("Unicode", unicode),
            JumpListItem.ForUri("Long", longArguments),
            JumpListItem.ForUri("Duplicate", unicode),
            JumpListItem.ForUri("Default", ""),
            new JumpListItem("", "", JumpListItemKind.Separator),
        });
        try
        {
            var array = (JumpListComInterop.IObjectArray)collection.Value;
            var path = Assert.IsType<string>(Environment.ProcessPath);
            var actual = JumpListComInterop.ReadRemovedArguments(array, path.ToUpperInvariant());
            Assert.Equal(3, actual.Count);
            Assert.Contains(unicode, actual);
            Assert.Contains(longArguments, actual);
            Assert.Contains("", actual);
            Assert.Empty(JumpListComInterop.ReadRemovedArguments(array, path + ".other.exe"));
            Assert.Empty(JumpListComInterop.ReadRemovedArguments(array, ""));
        }
        finally { collection.Dispose(); }
    }

    [Fact]
    public void Native_Shell_Link_Properties_Contain_The_Title_And_Separator_Marker()
    {
        using var apartment = JumpListComInterop.InitializeApartment();
        var collection = JumpListComInterop.BuildShellLinkArray(new[]
        {
            JumpListItem.ForUri("日本語 document", "app://file"),
            new JumpListItem("", "", JumpListItemKind.Separator),
        });
        try
        {
            var array = (JumpListComInterop.IObjectArray)collection.Value;
            var titleKey = new PropertyKey(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
            var separatorKey = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 6);
            AssertProperty(array, 0, titleKey, value =>
            {
                Assert.Equal(31, value.Type); // VT_LPWSTR
                Assert.Equal("日本語 document", Marshal.PtrToStringUni(value.Data));
            });
            AssertProperty(array, 1, separatorKey, value =>
            {
                Assert.Equal(11, value.Type); // VT_BOOL
                Assert.Equal(0xffff, value.Data.ToInt64() & 0xffff); // VARIANT_TRUE, not BOOL TRUE.
            });
        }
        finally { collection.Dispose(); }
    }

    private static unsafe void AssertProperty(JumpListComInterop.IObjectArray array, uint index,
        PropertyKey key, Action<PropVariant> assert)
    {
        var iid = new Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
        Marshal.ThrowExceptionForHR(array.GetAt(index, in iid, out var entry));
        try
        {
            // Independent native IPropertyStore::GetValue ABI (IUnknown slots
            // 0-2, GetCount 3, GetAt 4, GetValue 5), not the product's projection.
            var getValue = (delegate* unmanaged[Stdcall]<nint, PropertyKey*, PropVariant*, int>)(*(nint**)entry)[5];
            PropVariant value = default;
            Marshal.ThrowExceptionForHR(getValue(entry, &key, &value));
            try { assert(value); }
            finally { Marshal.ThrowExceptionForHR(PropVariantClear(ref value)); }
        }
        finally { Marshal.Release(entry); }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey(Guid format, uint id)
    {
        public Guid Format = format;
        public uint Id = id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public IntPtr Data, Data2;
    }

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant value);
}

[Collection("JumpListGlobals")]
public class JumpListRemovalValidationTests : IDisposable
{
    public JumpListRemovalValidationTests() => JumpList.ResetForTests();
    public void Dispose() => JumpList.ResetForTests();

    [Fact]
    public async Task Reading_Removals_Unpackaged_Requires_An_AppUserModelId()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => JumpList.GetRemovedArgumentsAsync());
        Assert.Contains("AppUserModelId", error.Message);
    }
}
