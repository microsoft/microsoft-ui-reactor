using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// Provides add/remove/reorder operations for array/list properties.
/// Works with IList for runtime polymorphism over List&lt;T&gt; and T[].
/// </summary>
/// <remarks>
/// The <see cref="IList"/> branch (covers <c>List&lt;T&gt;</c>,
/// <c>ObservableCollection&lt;T&gt;</c>, etc.) is fully AOT-safe. The plain-array branch
/// of <see cref="Add"/> / <see cref="RemoveAt"/> calls <see cref="Array.CreateInstance(Type, int)"/>,
/// which requires dynamic code; under Native AOT we throw <see cref="NotSupportedException"/>
/// rather than risk a runtime crash inside the BCL.
/// </remarks>
internal static class ArrayOperations
{
    private static readonly Type[] GenericCollectionDefinitions =
    [
        typeof(List<>),
        typeof(Collection<>),
        typeof(ObservableCollection<>),
        typeof(ReadOnlyCollection<>),
        typeof(HashSet<>),
        typeof(SortedSet<>),
        typeof(Queue<>),
        typeof(Stack<>),
        typeof(LinkedList<>),
        typeof(ConcurrentBag<>),
        typeof(ConcurrentQueue<>),
        typeof(ConcurrentStack<>),
        typeof(IList<>),
        typeof(ICollection<>),
        typeof(ISet<>),
        typeof(IReadOnlyList<>),
        typeof(IReadOnlyCollection<>),
        typeof(IReadOnlySet<>),
    ];

    private static readonly Type[] NonGenericCollectionTypes =
    [
        typeof(IList),
        typeof(ICollection),
        typeof(ArrayList),
        typeof(Queue),
        typeof(Stack),
    ];

    /// <summary>
    /// Adds an item to the end of the list. For arrays, returns a new array.
    /// Throws <see cref="NotSupportedException"/> on Native AOT for the array branch.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Array.CreateInstance is only reached when RuntimeFeature.IsDynamicCodeSupported is true; otherwise we throw before calling it.")]
    public static object Add(object collection, object? item, Type elementType)
    {
        if (collection is IList list && !collection.GetType().IsArray)
        {
            list.Add(item);
            return collection;
        }

        if (collection is Array array)
        {
            if (!RuntimeFeature.IsDynamicCodeSupported)
                throw new NotSupportedException(
                    $"Adding to a plain {DisplayName(elementType)}[] property requires dynamic code (Array.CreateInstance), " +
                    "which is unavailable on Native AOT. Use List<T> or another IList implementation instead.");

            var newArray = Array.CreateInstance(elementType, array.Length + 1);
            Array.Copy(array, newArray, array.Length);
            newArray.SetValue(item, array.Length);
            return newArray;
        }

        throw new InvalidOperationException($"Cannot add to {collection.GetType().Name}");
    }

    /// <summary>
    /// Removes an item at the given index. For arrays, returns a new array.
    /// Throws <see cref="NotSupportedException"/> on Native AOT for the array branch.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Array.CreateInstance is only reached when RuntimeFeature.IsDynamicCodeSupported is true; otherwise we throw before calling it.")]
    public static object RemoveAt(object collection, int index, Type elementType)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be non-negative.");

        if (collection is IList list && !collection.GetType().IsArray)
        {
            if (index >= list.Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be less than the collection size ({list.Count}).");
            list.RemoveAt(index);
            return collection;
        }

        if (collection is Array array)
        {
            if (index >= array.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be less than the array length ({array.Length}).");

            if (!RuntimeFeature.IsDynamicCodeSupported)
                throw new NotSupportedException(
                    $"Removing from a plain {DisplayName(elementType)}[] property requires dynamic code (Array.CreateInstance), " +
                    "which is unavailable on Native AOT. Use List<T> or another IList implementation instead.");

            var newArray = Array.CreateInstance(elementType, array.Length - 1);
            if (index > 0)
                Array.Copy(array, 0, newArray, 0, index);
            if (index < array.Length - 1)
                Array.Copy(array, index + 1, newArray, index, array.Length - index - 1);
            return newArray;
        }

        throw new InvalidOperationException($"Cannot remove from {collection.GetType().Name}");
    }

    /// <summary>
    /// Moves an item up (swap with previous).
    /// </summary>
    public static object MoveUp(object collection, int index, Type elementType)
    {
        if (index <= 0) return collection;

        if (collection is IList list && !collection.GetType().IsArray)
        {
            var item = list[index];
            list.RemoveAt(index);
            list.Insert(index - 1, item);
            return collection;
        }

        if (collection is Array array)
        {
            var newArray = (Array)array.Clone();
            var temp = newArray.GetValue(index);
            newArray.SetValue(newArray.GetValue(index - 1), index);
            newArray.SetValue(temp, index - 1);
            return newArray;
        }

        return collection;
    }

    /// <summary>
    /// Moves an item down (swap with next).
    /// </summary>
    public static object MoveDown(object collection, int index, Type elementType)
    {
        if (collection is IList list && !collection.GetType().IsArray)
        {
            if (index >= list.Count - 1) return collection;
            var item = list[index];
            list.RemoveAt(index);
            list.Insert(index + 1, item);
            return collection;
        }

        if (collection is Array array)
        {
            if (index >= array.Length - 1) return collection;
            var newArray = (Array)array.Clone();
            var temp = newArray.GetValue(index);
            newArray.SetValue(newArray.GetValue(index + 1), index);
            newArray.SetValue(temp, index + 1);
            return newArray;
        }

        return collection;
    }

    public static int GetCount(object collection)
    {
        if (collection is string) return 0;
        if (collection is ICollection c) return c.Count;
        if (collection is IEnumerable e)
        {
            var count = 0;
            foreach (var _ in e) count++;
            return count;
        }
        return 0;
    }

    public static object? GetItem(object collection, int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be non-negative.");

        if (collection is string) return null;
        if (collection is Array array && IsZeroBasedOneDimensional(array)) return array.GetValue(index);
        if (collection is IList list) return list[index];
        if (collection is IEnumerable e)
        {
            var i = 0;
            foreach (var item in e)
            {
                if (i == index) return item;
                i++;
            }
        }
        return null;
    }

    public static IReadOnlyList<object?> Snapshot(object collection)
    {
        if (collection is string) return [];

        if (collection is Array array && IsZeroBasedOneDimensional(array))
        {
            var items = new object?[array.Length];
            for (var i = 0; i < array.Length; i++)
                items[i] = array.GetValue(i);
            return items;
        }

        if (collection is IList list)
        {
            var items = new object?[list.Count];
            for (var i = 0; i < list.Count; i++)
                items[i] = list[i];
            return items;
        }

        if (collection is IEnumerable enumerable)
        {
            var items = new List<object?>();
            foreach (var item in enumerable)
                items.Add(item);
            return items;
        }

        return [];
    }

    public static CollectionCapabilities GetCapabilities(
        object collection,
        Type declaredType,
        bool canWriteBack,
        bool isReadOnly)
    {
        if (isReadOnly || IsDeclaredReadOnlyCollection(declaredType))
            return default;

        if (collection is Array array && IsZeroBasedOneDimensional(array))
        {
            var canResize = canWriteBack && RuntimeFeature.IsDynamicCodeSupported;
            return canWriteBack
                ? new CollectionCapabilities(
                    CanAdd: canResize,
                    CanRemoveAt: canResize,
                    CanReplaceAt: true,
                    CanReorder: true)
                : default;
        }

        if (collection is IList list)
        {
            var canResize = !list.IsReadOnly && !list.IsFixedSize;
            var canReplace = !list.IsReadOnly;
            return new CollectionCapabilities(
                CanAdd: canResize,
                CanRemoveAt: canResize,
                CanReplaceAt: canReplace,
                CanReorder: canResize);
        }

        return default;
    }

    public static object ReplaceAt(object collection, int index, object? item, Type elementType)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be non-negative.");

        if (collection is IList list && !collection.GetType().IsArray)
        {
            if (index >= list.Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be less than the collection size ({list.Count}).");
            list[index] = item;
            return collection;
        }

        if (collection is Array array)
        {
            if (index >= array.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be less than the array length ({array.Length}).");

            var newArray = (Array)array.Clone();
            newArray.SetValue(item, index);
            return newArray;
        }

        throw new InvalidOperationException($"Cannot replace an item in {collection.GetType().Name}");
    }

    /// <summary>
    /// Produces a human-readable C#-style name for diagnostic messages — handles
    /// generics (<c>List&lt;int&gt;</c> not <c>List`1</c>), nullable shorthand
    /// (<c>int?</c> not <c>Nullable&lt;Int32&gt;</c>), and nested types
    /// (<c>Outer.Inner</c> not <c>Outer+Inner</c>).
    /// </summary>
    private static string DisplayName(Type t)
    {
        if (Nullable.GetUnderlyingType(t) is { } inner)
            return DisplayName(inner) + "?";

        if (!t.IsGenericType)
            return t.Name.Replace('+', '.');

        var name = t.Name;
        var tickIndex = name.IndexOf('`');
        if (tickIndex >= 0)
            name = name[..tickIndex];

        var args = string.Join(", ", t.GetGenericArguments().Select(DisplayName));
        return $"{name.Replace('+', '.')}<{args}>";
    }

    public static Type? GetElementType(Type collectionType)
    {
        if (collectionType == typeof(string))
            return null;

        if (collectionType.IsArray)
        {
            if (!collectionType.IsSZArray)
                return null;
            return collectionType.GetElementType();
        }

        if (collectionType.IsGenericType)
        {
            var genDef = collectionType.GetGenericTypeDefinition();
            if (GenericCollectionDefinitions.Contains(genDef))
                return collectionType.GetGenericArguments()[0];
        }

        if (IsDictionaryLike(collectionType))
            return null;

        if (NonGenericCollectionTypes.Contains(collectionType) || typeof(IList).IsAssignableFrom(collectionType))
            return typeof(object);

        return null;
    }

    private static bool IsDeclaredReadOnlyCollection(Type declaredType)
    {
        if (declaredType == typeof(ICollection))
            return true;

        if (!declaredType.IsGenericType)
            return false;

        var genDef = declaredType.GetGenericTypeDefinition();
        return genDef == typeof(IReadOnlyCollection<>)
            || genDef == typeof(IReadOnlyList<>)
            || genDef == typeof(IReadOnlySet<>)
            || genDef == typeof(ReadOnlyCollection<>);
    }

    private static bool IsZeroBasedOneDimensional(Array array)
        => array.Rank == 1 && array.GetLowerBound(0) == 0;

    private static bool IsDictionaryLike(Type type)
        => typeof(IDictionary).IsAssignableFrom(type)
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>));
}

internal readonly record struct CollectionCapabilities(
    bool CanAdd,
    bool CanRemoveAt,
    bool CanReplaceAt,
    bool CanReorder);
