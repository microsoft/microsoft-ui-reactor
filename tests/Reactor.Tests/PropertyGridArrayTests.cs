using System.Collections;
using System.Collections.ObjectModel;
using Microsoft.UI.Reactor.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for array/list support in the PropertyGrid.
/// </summary>
public class PropertyGridArrayTests
{
    private class ItemModel
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public ItemModel() { }
        public ItemModel(string name, int value) { Name = name; Value = value; }
    }

    private class NoDefaultCtor
    {
        public string Label { get; }
        public NoDefaultCtor(string label) => Label = label;
    }

    private sealed class GenericOnlyList<T> : IList<T>
    {
        private readonly List<T> _inner = [];

        public GenericOnlyList(IEnumerable<T> items) => _inner.AddRange(items);

        public T this[int index] { get => _inner[index]; set => _inner[index] = value; }
        public int Count => _inner.Count;
        public bool IsReadOnly => false;
        public void Add(T item) => _inner.Add(item);
        public void Clear() => _inner.Clear();
        public bool Contains(T item) => _inner.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);
        public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();
        public int IndexOf(T item) => _inner.IndexOf(item);
        public void Insert(int index, T item) => _inner.Insert(index, item);
        public bool Remove(T item) => _inner.Remove(item);
        public void RemoveAt(int index) => _inner.RemoveAt(index);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class EnumerableOnly<T> : IEnumerable<T>
    {
        private readonly List<T> _items = [];
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // ── Array resolution ──────────────────────────────────────────

    [Fact]
    public void Array_Property_Renders_With_Correct_Item_Count()
    {
        var items = new[] { new ItemModel("A", 1), new ItemModel("B", 2), new ItemModel("C", 3) };
        Assert.Equal(3, ArrayOperations.GetCount(items));
    }

    [Fact]
    public void List_Property_Renders_With_Correct_Item_Count()
    {
        var items = new List<ItemModel>
        {
            new("A", 1), new("B", 2)
        };
        Assert.Equal(2, ArrayOperations.GetCount(items));
    }

    // ── Add ───────────────────────────────────────────────────────

    [Fact]
    public void Add_To_List_Appends()
    {
        var items = new List<ItemModel> { new("A", 1) };
        var result = ArrayOperations.Add(items, new ItemModel("B", 2), typeof(ItemModel));

        Assert.Same(items, result); // mutates in-place
        Assert.Equal(2, items.Count);
        Assert.Equal("B", items[1].Name);
    }

    [Fact]
    public void Add_To_Array_Returns_New_Array()
    {
        var items = new[] { new ItemModel("A", 1) };
        var result = (ItemModel[])ArrayOperations.Add(items, new ItemModel("B", 2), typeof(ItemModel));

        Assert.NotSame(items, result); // new array
        Assert.Equal(2, result.Length);
        Assert.Equal("A", result[0].Name);
        Assert.Equal("B", result[1].Name);
    }

    // ── Remove ────────────────────────────────────────────────────

    [Fact]
    public void Remove_From_List()
    {
        var items = new List<ItemModel>
        {
            new("A", 1), new("B", 2), new("C", 3)
        };
        ArrayOperations.RemoveAt(items, 1, typeof(ItemModel));

        Assert.Equal(2, items.Count);
        Assert.Equal("A", items[0].Name);
        Assert.Equal("C", items[1].Name);
    }

    [Fact]
    public void Remove_From_Array_Returns_New_Array()
    {
        var items = new[] { new ItemModel("A", 1), new ItemModel("B", 2), new ItemModel("C", 3) };
        var result = (ItemModel[])ArrayOperations.RemoveAt(items, 1, typeof(ItemModel));

        Assert.Equal(2, result.Length);
        Assert.Equal("A", result[0].Name);
        Assert.Equal("C", result[1].Name);
    }

    // ── Reorder ───────────────────────────────────────────────────

    [Fact]
    public void MoveUp_In_List()
    {
        var items = new List<ItemModel>
        {
            new("A", 1), new("B", 2), new("C", 3)
        };
        ArrayOperations.MoveUp(items, 2, typeof(ItemModel));

        Assert.Equal("A", items[0].Name);
        Assert.Equal("C", items[1].Name);
        Assert.Equal("B", items[2].Name);
    }

    [Fact]
    public void MoveDown_In_List()
    {
        var items = new List<ItemModel>
        {
            new("A", 1), new("B", 2), new("C", 3)
        };
        ArrayOperations.MoveDown(items, 0, typeof(ItemModel));

        Assert.Equal("B", items[0].Name);
        Assert.Equal("A", items[1].Name);
        Assert.Equal("C", items[2].Name);
    }

    [Fact]
    public void MoveUp_At_Index_0_Is_Noop()
    {
        var items = new List<ItemModel>
        {
            new("A", 1), new("B", 2)
        };
        ArrayOperations.MoveUp(items, 0, typeof(ItemModel));

        Assert.Equal("A", items[0].Name);
        Assert.Equal("B", items[1].Name);
    }

    [Fact]
    public void MoveDown_At_Last_Index_Is_Noop()
    {
        var items = new List<ItemModel>
        {
            new("A", 1), new("B", 2)
        };
        ArrayOperations.MoveDown(items, 1, typeof(ItemModel));

        Assert.Equal("A", items[0].Name);
        Assert.Equal("B", items[1].Name);
    }

    // ── Array replacement via setter ──────────────────────────────

    [Fact]
    public void Array_Replacement_Via_Setter_After_Mutation()
    {
        var registry = new TypeRegistry();
        var parent = new ArrayParent { Items = new[] { "A", "B" } };
        var meta = registry.Resolve(typeof(ArrayParent));
        var descriptors = meta.Decompose!(parent);
        var itemsProp = descriptors.First(d => d.Name == "Items");

        // Remove via array operation
        var newArray = ArrayOperations.RemoveAt(parent.Items, 0, typeof(string));
        var result = itemsProp.SetValue!(parent, newArray);

        Assert.Same(parent, result); // mutable parent
        Assert.Single(parent.Items);
        Assert.Equal("B", parent.Items[0]);
    }

    // ── CreateElement ─────────────────────────────────────────────

    [Fact]
    public void CreateElement_Null_For_No_Parameterless_Ctor()
    {
        var registry = new TypeRegistry();
        var meta = (ArrayTypeMetadata)registry.Resolve(typeof(NoDefaultCtor[]));
        Assert.Null(meta.CreateElement);
    }

    [Fact]
    public async Task CreateElement_Works_For_Parameterless_Ctor()
    {
        var registry = new TypeRegistry();
        var meta = (ArrayTypeMetadata)registry.Resolve(typeof(ItemModel[]));
        Assert.NotNull(meta.CreateElement);

        var item = await meta.CreateElement!();
        Assert.NotNull(item);
        Assert.IsType<ItemModel>(item);
    }

    [Fact]
    public async Task CreateElement_For_String_Returns_Empty_String()
    {
        var registry = new TypeRegistry();
        var meta = (ArrayTypeMetadata)registry.Resolve(typeof(List<string>));
        Assert.NotNull(meta.CreateElement);

        var item = await meta.CreateElement!();
        Assert.Equal("", item);
    }

    // ── Element type detection ────────────────────────────────────

    [Fact]
    public void GetElementType_For_Array()
    {
        Assert.Equal(typeof(int), ArrayOperations.GetElementType(typeof(int[])));
        Assert.Equal(typeof(string), ArrayOperations.GetElementType(typeof(string[])));
    }

    [Fact]
    public void GetElementType_For_List()
    {
        Assert.Equal(typeof(int), ArrayOperations.GetElementType(typeof(List<int>)));
        Assert.Equal(typeof(string), ArrayOperations.GetElementType(typeof(List<string>)));
    }

    [Fact]
    public void GetElementType_For_Common_Generic_Collection_Contracts()
    {
        Assert.Equal(typeof(int), ArrayOperations.GetElementType(typeof(IList<int>)));
        Assert.Equal(typeof(int), ArrayOperations.GetElementType(typeof(ICollection<int>)));
        Assert.Equal(typeof(int), ArrayOperations.GetElementType(typeof(ISet<int>)));
        Assert.Equal(typeof(int), ArrayOperations.GetElementType(typeof(IReadOnlyList<int>)));
        Assert.Equal(typeof(int), ArrayOperations.GetElementType(typeof(IReadOnlyCollection<int>)));
        Assert.Equal(typeof(int), ArrayOperations.GetElementType(typeof(IReadOnlySet<int>)));
        Assert.Null(ArrayOperations.GetElementType(typeof(IEnumerable<int>)));
    }

    [Fact]
    public void GetElementType_For_Common_Concrete_Collections()
    {
        Assert.Equal(typeof(string), ArrayOperations.GetElementType(typeof(ObservableCollection<string>)));
        Assert.Equal(typeof(string), ArrayOperations.GetElementType(typeof(ReadOnlyCollection<string>)));
        Assert.Equal(typeof(string), ArrayOperations.GetElementType(typeof(HashSet<string>)));
        Assert.Equal(typeof(string), ArrayOperations.GetElementType(typeof(Queue<string>)));
        Assert.Equal(typeof(string), ArrayOperations.GetElementType(typeof(Stack<string>)));
        Assert.Equal(typeof(string), ArrayOperations.GetElementType(typeof(LinkedList<string>)));
    }

    [Fact]
    public void GetElementType_For_NonGeneric_Collections_Uses_Object()
    {
        Assert.Equal(typeof(object), ArrayOperations.GetElementType(typeof(IList)));
        Assert.Equal(typeof(object), ArrayOperations.GetElementType(typeof(ICollection)));
        Assert.Null(ArrayOperations.GetElementType(typeof(IEnumerable)));
        Assert.Equal(typeof(object), ArrayOperations.GetElementType(typeof(ArrayList)));
    }

    [Fact]
    public void GetElementType_Does_Not_Treat_String_Or_Domain_Enumerable_As_Collection()
    {
        Assert.Null(ArrayOperations.GetElementType(typeof(string)));
        Assert.Null(ArrayOperations.GetElementType(typeof(EnumerableOnly<string>)));
        Assert.Null(ArrayOperations.GetElementType(typeof(int[,]))); // multi-dimensional arrays are not list-like
    }

    [Fact]
    public void TypeRegistry_Resolves_Common_Collection_Contracts_To_ArrayMetadata()
    {
        var registry = new TypeRegistry();

        Assert.IsType<ArrayTypeMetadata>(registry.Resolve(typeof(IList<string>)));
        Assert.IsType<ArrayTypeMetadata>(registry.Resolve(typeof(ICollection<string>)));
        Assert.IsType<ArrayTypeMetadata>(registry.Resolve(typeof(ISet<string>)));
        Assert.IsType<ArrayTypeMetadata>(registry.Resolve(typeof(IReadOnlyList<string>)));
        Assert.IsType<ArrayTypeMetadata>(registry.Resolve(typeof(IReadOnlyCollection<string>)));
        Assert.IsType<ArrayTypeMetadata>(registry.Resolve(typeof(IReadOnlySet<string>)));
        Assert.NotNull(registry.Resolve(typeof(IEnumerable<string>)).Decompose);
        Assert.IsType<ArrayTypeMetadata>(registry.Resolve(typeof(ArrayList)));
    }

    // ── Array path coverage (the IList path is well covered;
    //     these lock down the array-clone branches) ─────────────────

    [Fact]
    public void Add_To_Array_Returns_New_Larger_Array()
    {
        var items = new[] { "a", "b" };
        var result = (string[])ArrayOperations.Add(items, "c", typeof(string));
        Assert.Equal(3, result.Length);
        Assert.Equal("c", result[2]);
        Assert.Equal(2, items.Length); // original unchanged
    }

    [Fact]
    public void RemoveAt_From_Array_Returns_New_Smaller_Array()
    {
        var items = new[] { "a", "b", "c" };
        var result = (string[])ArrayOperations.RemoveAt(items, 1, typeof(string));
        Assert.Equal(new[] { "a", "c" }, result);
    }

    [Fact]
    public void RemoveAt_From_Array_FirstIndex_Works()
    {
        var items = new[] { "a", "b", "c" };
        var result = (string[])ArrayOperations.RemoveAt(items, 0, typeof(string));
        Assert.Equal(new[] { "b", "c" }, result);
    }

    [Fact]
    public void RemoveAt_From_Array_LastIndex_Works()
    {
        var items = new[] { "a", "b", "c" };
        var result = (string[])ArrayOperations.RemoveAt(items, 2, typeof(string));
        Assert.Equal(new[] { "a", "b" }, result);
    }

    [Fact]
    public void RemoveAt_Negative_Index_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArrayOperations.RemoveAt(new List<string> { "a" }, -1, typeof(string)));
    }

    [Fact]
    public void RemoveAt_OutOfRange_List_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArrayOperations.RemoveAt(new List<string> { "a" }, 5, typeof(string)));
    }

    [Fact]
    public void RemoveAt_OutOfRange_Array_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArrayOperations.RemoveAt(new[] { "a" }, 5, typeof(string)));
    }

    [Fact]
    public void Add_Unsupported_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ArrayOperations.Add("not a collection", "x", typeof(string)));
    }

    [Fact]
    public void RemoveAt_Unsupported_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ArrayOperations.RemoveAt("not a collection", 0, typeof(string)));
    }

    [Fact]
    public void MoveUp_On_Array_Returns_Reordered_Array()
    {
        var items = new[] { "a", "b", "c" };
        var result = (string[])ArrayOperations.MoveUp(items, 2, typeof(string));
        Assert.Equal(new[] { "a", "c", "b" }, result);
    }

    [Fact]
    public void MoveUp_At_Index_Zero_Returns_Same()
    {
        var items = new List<string> { "a", "b" };
        var result = ArrayOperations.MoveUp(items, 0, typeof(string));
        Assert.Same(items, result);
    }

    [Fact]
    public void MoveUp_On_List_Reorders_In_Place()
    {
        var items = new List<string> { "a", "b", "c" };
        var result = ArrayOperations.MoveUp(items, 2, typeof(string));
        Assert.Same(items, result);
        Assert.Equal(new[] { "a", "c", "b" }, items);
    }

    [Fact]
    public void MoveUp_On_Unsupported_Returns_Same()
    {
        var x = "scalar";
        Assert.Same(x, ArrayOperations.MoveUp(x, 1, typeof(string)));
    }

    [Fact]
    public void MoveDown_On_Array_Returns_Reordered_Array()
    {
        var items = new[] { "a", "b", "c" };
        var result = (string[])ArrayOperations.MoveDown(items, 0, typeof(string));
        Assert.Equal(new[] { "b", "a", "c" }, result);
    }

    [Fact]
    public void MoveDown_On_List_Reorders_In_Place()
    {
        var items = new List<string> { "a", "b", "c" };
        var result = ArrayOperations.MoveDown(items, 0, typeof(string));
        Assert.Same(items, result);
        Assert.Equal(new[] { "b", "a", "c" }, items);
    }

    [Fact]
    public void MoveDown_At_Last_Index_Returns_Same()
    {
        var items = new List<string> { "a", "b" };
        var result = ArrayOperations.MoveDown(items, 1, typeof(string));
        Assert.Same(items, result);
    }

    [Fact]
    public void MoveDown_At_Last_Index_Array_Returns_Same()
    {
        var items = new[] { "a", "b" };
        var result = ArrayOperations.MoveDown(items, 1, typeof(string));
        Assert.Same(items, result);
    }

    [Fact]
    public void MoveDown_On_Unsupported_Returns_Same()
    {
        var x = "scalar";
        Assert.Same(x, ArrayOperations.MoveDown(x, 0, typeof(string)));
    }

    [Fact]
    public void GetCount_For_Array()
    {
        Assert.Equal(3, ArrayOperations.GetCount(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void GetCount_For_Unknown_Returns_Zero()
    {
        Assert.Equal(0, ArrayOperations.GetCount("scalar"));
    }

    [Fact]
    public void GetItem_From_List_And_Array()
    {
        Assert.Equal("b", ArrayOperations.GetItem(new List<string> { "a", "b", "c" }, 1));
        Assert.Equal("y", ArrayOperations.GetItem(new[] { "x", "y" }, 1));
    }

    [Fact]
    public void GetCount_And_GetItem_Work_For_ReadOnly_Collections()
    {
        IReadOnlyList<string> readOnlyList = new List<string> { "a", "b", "c" };

        Assert.Equal(3, ArrayOperations.GetCount(readOnlyList));
        Assert.Equal("b", ArrayOperations.GetItem(readOnlyList, 1));
    }

    [Fact]
    public void Snapshot_Captures_Collection_State_For_Current_Render()
    {
        var items = new List<string> { "a", "b" };
        var snapshot = ArrayOperations.Snapshot(items);
        items.Add("c");

        Assert.Equal(new[] { "a", "b" }, snapshot);
    }

    [Fact]
    public void GetItem_For_Unknown_Returns_Null()
    {
        Assert.Null(ArrayOperations.GetItem("scalar", 0));
    }

    [Fact]
    public void ReplaceAt_In_List_Replaces_In_Place()
    {
        var items = new List<string> { "a", "b" };
        var result = ArrayOperations.ReplaceAt(items, 1, "c", typeof(string));

        Assert.Same(items, result);
        Assert.Equal(new[] { "a", "c" }, items);
    }

    [Fact]
    public void ReplaceAt_In_Array_Returns_New_Array()
    {
        var items = new[] { "a", "b" };
        var result = (string[])ArrayOperations.ReplaceAt(items, 0, "z", typeof(string));

        Assert.NotSame(items, result);
        Assert.Equal(new[] { "z", "b" }, result);
        Assert.Equal(new[] { "a", "b" }, items);
    }

    [Fact]
    public void Capabilities_Enable_Mutable_IList_Contracts()
    {
        var caps = ArrayOperations.GetCapabilities(
            new List<string> { "a", "b" },
            typeof(IList<string>),
            canWriteBack: true,
            isReadOnly: false);

        Assert.Equal(global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported, caps.CanAdd);
        Assert.Equal(global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported, caps.CanRemoveAt);
        Assert.True(caps.CanReplaceAt);
        Assert.True(caps.CanReorder);
    }

    [Fact]
    public void Capabilities_Disable_ReadOnly_Declared_Contracts_Even_With_List_Runtime()
    {
        var list = new List<string> { "a", "b" };

        Assert.Equal(default, ArrayOperations.GetCapabilities(
            list, typeof(IReadOnlyList<string>), canWriteBack: true, isReadOnly: false));
    }

    [Fact]
    public void Capabilities_Disable_GenericOnly_IList_Runtime_To_Avoid_Unsupported_Clicks()
    {
        var caps = ArrayOperations.GetCapabilities(
            new GenericOnlyList<string>(["a", "b"]),
            typeof(IList<string>),
            canWriteBack: true,
            isReadOnly: false);

        Assert.Equal(default, caps);
    }

    [Fact]
    public void Capabilities_Enable_Array_Only_When_Writeback_Is_Available()
    {
        var items = new[] { "a", "b" };

        Assert.Equal(default, ArrayOperations.GetCapabilities(
            items, typeof(string[]), canWriteBack: false, isReadOnly: false));

        var caps = ArrayOperations.GetCapabilities(
            items, typeof(string[]), canWriteBack: true, isReadOnly: false);

        Assert.True(caps.CanAdd);
        Assert.True(caps.CanRemoveAt);
        Assert.True(caps.CanReplaceAt);
        Assert.True(caps.CanReorder);
    }

    [Fact]
    public void GetElementType_For_NonCollection_Types_Returns_Null()
    {
        Assert.Null(ArrayOperations.GetElementType(typeof(string)));
        Assert.Null(ArrayOperations.GetElementType(typeof(Dictionary<string, int>)));
    }

    // ── Test model ────────────────────────────────────────────────

    private class ArrayParent
    {
        public string[] Items { get; set; } = Array.Empty<string>();
    }
}
