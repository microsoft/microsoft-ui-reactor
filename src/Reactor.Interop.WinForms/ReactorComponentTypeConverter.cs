using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ReactorComponent = Microsoft.UI.Reactor.Core.Component;

namespace Microsoft.UI.Reactor.Interop.WinForms;

// Design-time-only reflection: this TypeConverter powers the WinForms designer
// Properties-grid dropdown by enumerating every loaded assembly for concrete
// Reactor Component subclasses. It runs in the Visual Studio designer process
// (full framework, never trimmed/AOT) — the designer serializes the chosen type
// as `ComponentType = typeof(X)`, so the runtime/published path never invokes this
// whole-assembly enumeration. The trim/AOT warnings are therefore not reachable in
// a published app; suppressed per-method with this justification (issue #70).

/// <summary>
/// TypeConverter for the <see cref="XamlIslandControl.ComponentType"/> property.
/// Enables the WinForms designer Properties grid to:
///   - Display the component type name as a readable string
///   - Show a dropdown of all concrete Reactor Component subclasses in the project
///   - Accept typed type names and resolve them to Type objects
/// </summary>
internal class ReactorComponentTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Design-time-only assembly enumeration for the WinForms designer dropdown; never runs in a trimmed/AOT-published app (see file header).")]
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "(none)")
                return null;

            // Try exact match first (full name), then short name
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                try
                {
                    var match = asm.GetType(name);
                    if (match is not null && IsValidComponentType(match))
                        return match;
                }
                catch { }
            }

            // Short name search — check all types
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == name && IsValidComponentType(t))
                            return t;
                    }
                }
                catch { }
            }
        }
        return base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string))
            return value is Type t ? t.FullName : "(none)";
        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Design-time-only assembly enumeration for the WinForms designer dropdown; never runs in a trimmed/AOT-published app (see file header).")]
    public override StandardValuesCollection? GetStandardValues(ITypeDescriptorContext? context)
    {
        var types = new List<Type>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            try
            {
                foreach (var t in asm.GetTypes())
                {
                    if (IsValidComponentType(t))
                        types.Add(t);
                }
            }
            catch { }
        }
        types.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
        return new StandardValuesCollection(types);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Design-time-only: t comes from designer assembly enumeration (see file header); GetConstructor is not reached in a trimmed/AOT-published app.")]
    private static bool IsValidComponentType(Type t)
        => t.IsClass
        && !t.IsAbstract
        && typeof(ReactorComponent).IsAssignableFrom(t)
        && t.GetConstructor(Type.EmptyTypes) is not null;
}
