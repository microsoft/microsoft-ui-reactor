using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Hot-reload state migration (spec 049 §6, step 2). Copies field values, by
/// name, from a pre-edit instance onto a freshly-constructed post-edit instance
/// so that adding/removing a field on a record or class used in
/// <c>UseState</c> / <c>UseReducer</c> / <c>UseRef</c> / <c>UseMemo</c> (or as a
/// component's <c>Props</c>) preserves the untouched fields' values across an
/// edit. New fields read as their default; removed fields are silently dropped.
///
/// <para>This is reflection-heavy by nature (it walks runtime field metadata of
/// arbitrary user types). It is only ever reached through
/// <see cref="HotReloadService.IsHotReloadLive"/>-gated branches, so under
/// NativeAOT — where <see cref="global::System.Reflection.Metadata.MetadataUpdater.IsSupported"/>
/// is <c>false</c> — the whole call graph is statically dead and trims away
/// (spec 049 §8). The trim/AOT suppressions below document that invariant.</para>
/// </summary>
internal static class ReactorHotReloadCopier
{
    /// <summary>
    /// Copies every name-matching field from <paramref name="source"/> onto
    /// <paramref name="dest"/>. Returns <c>false</c> only when a guard rejects
    /// the inputs (null source/dest); a successful walk — even one that copies
    /// zero fields — returns <c>true</c>.
    /// </summary>
    /// <param name="visited">Cycle guard keyed on source reference identity.
    /// Pass <c>new HashSet&lt;object&gt;(ReferenceEqualityComparer.Instance)</c>.</param>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    public static bool TryMigrate(object? source, object? dest, HashSet<object> visited)
    {
        if (source is null || dest is null) return false;

        // Cycle guard: a self-referential object graph would otherwise recurse
        // forever. Once we have started migrating a given source instance we do
        // not re-enter it.
        if (!visited.Add(source)) return true;

        Type destType = dest.GetType();
        Type srcType = source.GetType();

        foreach (FieldInfo destField in destType.GetFields(InstanceFields))
        {
            if (IsBlockListed(destField.FieldType)) continue;

            FieldInfo? srcField = FindField(srcType, destField.Name);
            if (srcField is null) continue; // new field — leave the default.

            object? srcValue = srcField.GetValue(source);
            if (srcValue is null)
            {
                destField.SetValue(dest, null);
                continue;
            }

            Type destFieldType = destField.FieldType;
            Type srcValueType = srcValue.GetType();

            if (destFieldType.IsAssignableFrom(srcValueType))
            {
                // Same (or compatible) type — copy the reference/value directly.
                destField.SetValue(dest, srcValue);
            }
            else if (SameFullNameDifferentType(destFieldType, srcValueType))
            {
                // The field's own type was itself reshaped by the edit (the HR
                // runtime minted a new Type that shares a FullName). Recurse
                // into a fresh instance of the destination's field type.
                object? nested = CreateInstance(destFieldType);
                if (nested is not null && TryMigrate(srcValue, nested, visited))
                    destField.SetValue(dest, nested);
                // else: cannot rebuild the nested shape — leave default.
            }
            else
            {
                // Field type changed incompatibly: drop the value (default
                // stays) and trace at Debug. Never throw — a blank slate for
                // one field must not abort the whole reload.
                ReactorEventSource.Log.HotReloadFieldDropped(
                    destType.FullName ?? destType.Name,
                    destField.Name,
                    srcValueType.FullName ?? srcValueType.Name,
                    destFieldType.FullName ?? destFieldType.Name);
            }
        }

        return true;
    }

    private const BindingFlags InstanceFields =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// Constructs a new instance of <paramref name="type"/> for migration. Tries
    /// (in order): the parameterless constructor; then the widest public
    /// constructor invoked with each parameter's default (covers positional
    /// records, whose primary ctor has no parameterless form — every field is
    /// overwritten by name afterwards anyway, so the placeholder args are
    /// transient). Returns <c>null</c> when no usable constructor exists.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    internal static object? CreateInstance(Type type)
    {
        try
        {
            ConstructorInfo? parameterless = type.GetConstructor(Type.EmptyTypes);
            if (parameterless is not null) return parameterless.Invoke(null);

            ConstructorInfo? widest = null;
            int widestCount = -1;
            foreach (ConstructorInfo ctor in type.GetConstructors())
            {
                int count = ctor.GetParameters().Length;
                if (count > widestCount) { widest = ctor; widestCount = count; }
            }
            if (widest is null) return null;

            ParameterInfo[] ps = widest.GetParameters();
            var args = new object?[ps.Length];
            for (int i = 0; i < ps.Length; i++)
                args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : DefaultOf(ps[i].ParameterType);
            return widest.Invoke(args);
        }
        catch
        {
            return null;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    private static object? DefaultOf(Type t) =>
        t.IsValueType ? RuntimeHelpers.GetUninitializedObject(t) : null;

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    private static FieldInfo? FindField(Type type, string name)
    {
        // Walk the type hierarchy so private fields declared on a base class are
        // matched too (GetField with NonPublic only returns the most-derived).
        for (Type? t = type; t is not null; t = t.BaseType)
        {
            FieldInfo? f = t.GetField(name, InstanceFields | BindingFlags.DeclaredOnly);
            if (f is not null) return f;
        }
        return null;
    }

    private static bool SameFullNameDifferentType(Type a, Type b) =>
        !ReferenceEquals(a, b) && a.FullName is not null && a.FullName == b.FullName;

    /// <summary>
    /// Heuristic block-list of field types we must never reflect-copy: native
    /// handles and live WinUI / composition objects. Copying an
    /// <see cref="nint"/> handle or a <c>Visual</c>/<c>UIElement</c> reference
    /// across a reload would either smuggle a stale native pointer or re-parent
    /// a live control. These are left at their freshly-constructed default.
    /// </summary>
    private static bool IsBlockListed(Type fieldType)
    {
        if (fieldType == typeof(nint) || fieldType == typeof(nuint) ||
            fieldType == typeof(IntPtr) || fieldType == typeof(UIntPtr))
            return true;

        for (Type? t = fieldType; t is not null; t = t.BaseType)
        {
            string? name = t.FullName;
            if (name is null) continue;
            if (name == "Microsoft.UI.Composition.Compositor" ||
                name == "Microsoft.UI.Composition.Visual" ||
                name == "Microsoft.UI.Composition.CompositionObject" ||
                name == "Microsoft.UI.Xaml.UIElement" ||
                name == "Microsoft.UI.Xaml.DependencyObject")
                return true;
        }
        return false;
    }
}
