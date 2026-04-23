using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Data;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    /// <summary>
    /// Define a column from a property accessor expression.
    /// </summary>
    public static ColumnBuilder<T> Column<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        string name,
        Func<T, object?> accessor,
        bool editable = false,
        string? displayName = null,
        string? format = null,
        double? width = null,
        PinPosition pin = PinPosition.None)
    {
        var fieldType = ColumnHelpers.InferFieldType<T>(name);

        Func<object, object?, object>? setter = null;
        if (editable)
        {
            setter = ColumnHelpers.BuildSetValue<T>(name);
        }

        var descriptor = new FieldDescriptor
        {
            Name = name,
            DisplayName = displayName ?? name,
            FieldType = fieldType,
            GetValue = obj => accessor((T)obj),
            SetValue = setter,
            IsReadOnly = !editable || setter is null,
            Width = width,
            Pin = pin,
            FormatValue = format is not null ? val => ColumnHelpers.FormatWithSpec(val, format) : null,
        };

        return new ColumnBuilder<T>(descriptor);
    }

    /// <summary>
    /// Auto-generate columns from a type using reflection and TypeRegistry.
    /// </summary>
    public static IReadOnlyList<FieldDescriptor> AutoColumns<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        TypeRegistry? registry = null,
        Func<FieldDescriptor, FieldDescriptor>? overrides = null)
    {
        var meta = registry?.Resolve(typeof(T))
            ?? ReflectionTypeMetadataProvider.CreateMetadata(typeof(T));

        if (meta.Decompose is null)
            return Array.Empty<FieldDescriptor>();

        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .Where(p => p.GetCustomAttribute<PropertyHiddenAttribute>() is null)
            .ToArray();

        var descriptors = new List<FieldDescriptor>();
        for (int i = 0; i < properties.Length; i++)
        {
            var desc = ReflectionTypeMetadataProvider.CreateDescriptor(properties[i], i);

            if (registry is not null)
            {
                var cellRenderer = registry.GetCellRenderer(properties[i].PropertyType);
                var formatter = registry.GetFormatter(properties[i].PropertyType);
                if (cellRenderer is not null || formatter is not null)
                {
                    desc = desc with
                    {
                        CellRenderer = cellRenderer ?? desc.CellRenderer,
                        FormatValue = formatter ?? desc.FormatValue,
                    };
                }
            }

            if (overrides is not null)
                desc = overrides(desc);

            descriptors.Add(desc);
        }

        return descriptors;
    }
}

file static class ColumnHelpers
{
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "PropertyInfo.PropertyType does not carry DynamicallyAccessedMembers.")]
    [UnconditionalSuppressMessage("Trimming", "IL2073", Justification = "PropertyInfo.PropertyType does not carry DynamicallyAccessedMembers.")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
    public static Type InferFieldType<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string name)
    {
        var prop = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return prop?.PropertyType ?? typeof(object);
    }

    /// <summary>
    /// Build a SetValue delegate from reflection. For mutable properties, mutates in place.
    /// For init-only (record) properties, uses the copy constructor.
    /// </summary>
    public static Func<object, object?, object>? BuildSetValue<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string propertyName)
    {
        var prop = typeof(T).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop is null || !prop.CanWrite) return null;

        var setMethod = prop.SetMethod;
        if (setMethod is null) return null;

        var isInitOnly = setMethod.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

        if (!isInitOnly)
        {
            return (owner, val) =>
            {
                prop.SetValue(owner, val);
                return owner;
            };
        }

        return ReflectionTypeMetadataProvider.BuildInitOnlySetter(prop);
    }

    public static string FormatWithSpec(object? value, string format)
    {
        if (value is null) return "";
        if (value is IFormattable formattable)
            return formattable.ToString(format, null);
        return value.ToString() ?? "";
    }
}
