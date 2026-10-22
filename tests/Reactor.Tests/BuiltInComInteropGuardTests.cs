using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Guards the shipped, <c>IsAotCompatible</c> Reactor assemblies against the runtime's
/// built-in COM interop (<c>[ComImport]</c> / <c>[CoClass]</c> / COM-typed P/Invoke
/// parameters). Built-in COM generates its marshalling IL stubs at run time, so it is
/// unsupported under Native AOT ("Windows: No built-in COM" —
/// https://learn.microsoft.com/dotnet/core/deploying/native-aot/#limitations-of-native-aot-deployment).
/// The library build stays green because the trim/AOT Roslyn analyzer does not flag
/// <c>[ComImport]</c>; the break only surfaces as ILC <c>IL3052</c>/<c>IL3053</c>/
/// <c>CsWinRT1033</c> or a "will always throw" stub when a consumer publishes with
/// Native AOT and reaches the code (issue #1402). This headless reflection check
/// catches it at PR time regardless of which path reaches it.
/// </summary>
public partial class BuiltInComInteropGuardTests
{
    private const string Remedy =
        "Use [GeneratedComInterface] + StrategyBasedComWrappers + explicit CoCreateInstance " +
        "(see src/Reactor/Hosting/Shell/TaskbarComInterop.cs and " +
        "https://learn.microsoft.com/dotnet/standard/native-interop/comwrappers-source-generation).";

    private static readonly Dictionary<string, Assembly> s_shipped = new[]
    {
        typeof(ReactorApp).Assembly,
        typeof(Microsoft.UI.Reactor.Docking.DockManager).Assembly,
    }.ToDictionary(a => a.GetName().Name!);

    public static TheoryData<string> ShippedAssemblies() => new(s_shipped.Keys);

    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void ShippedAssembly_HasNoBuiltInComInterop(string assemblyName)
    {
        var violations = FindViolations(s_shipped[assemblyName]);

        Assert.True(
            violations.Count == 0,
            $"{assemblyName} uses built-in COM interop, which is not Native AOT compatible:\n  " +
            string.Join("\n  ", violations) + "\n" + Remedy);
    }

    [Fact]
    public void Guard_DetectsEveryBuiltInComShape()
    {
        // Positive control: the same scanner must flag each shape it claims to guard,
        // otherwise a green ShippedAssembly run proves nothing.
        var violations = FindViolations(typeof(BuiltInComInteropGuardTests).Assembly)
            .Where(v => v.Contains(nameof(Samples), StringComparison.Ordinal))
            .ToArray();

        Assert.Contains(violations, v => v.Contains("[ComImport] class", StringComparison.Ordinal) && v.Contains(nameof(Samples.LegacyCoClass), StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("[ComImport] interface", StringComparison.Ordinal) && v.Contains(nameof(Samples.ILegacyCom), StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("[InterfaceType]", StringComparison.Ordinal) && v.Contains(nameof(Samples.IInterfaceTypeOnly), StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("[CoClass]", StringComparison.Ordinal) && v.Contains(nameof(Samples.ICoClassed), StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains(nameof(Samples.TakesInterface), StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains(nameof(Samples.TakesObject), StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains(nameof(Samples.ReturnsUnknown), StringComparison.Ordinal));
        Assert.DoesNotContain(violations, v => v.Contains(nameof(Samples.IGenerated), StringComparison.Ordinal));
        Assert.DoesNotContain(violations, v => v.Contains(nameof(Samples.TakesPointer), StringComparison.Ordinal));
    }

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only guard: reads P/Invoke metadata (Type.GetMethods) on types from the GetTypes scan. This host is never trimmed. Behaviour-neutral.")]
    private static List<string> FindViolations(Assembly assembly)
    {
        var violations = new List<string>();
        foreach (var type in LoadableTypes(assembly))
        {
            if (type.IsImport)
                violations.Add($"[ComImport] {(type.IsInterface ? "interface" : "class")} {type.FullName}");
            else if (type.IsInterface
                && type.IsDefined(typeof(InterfaceTypeAttribute), inherit: false)
                && !type.IsDefined(typeof(GeneratedComInterfaceAttribute), inherit: false))
                violations.Add($"[InterfaceType] without [GeneratedComInterface] on {type.FullName}");

            if (type.IsDefined(typeof(CoClassAttribute), inherit: false))
                violations.Add($"[CoClass] on {type.FullName}");

            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0)
                    continue;
                foreach (var p in method.GetParameters().Append(method.ReturnParameter))
                {
                    if (UsesBuiltInComMarshalling(p))
                        violations.Add($"P/Invoke {type.FullName}.{method.Name} marshals '{p.Name ?? "return"}' ({p.ParameterType.Name}) through built-in COM");
                }
            }
        }
        return violations;
    }

    private static bool UsesBuiltInComMarshalling(ParameterInfo p)
    {
        var marshalAs = p.GetCustomAttribute<MarshalAsAttribute>();
        if (marshalAs?.Value is UnmanagedType.Interface or UnmanagedType.IUnknown or UnmanagedType.IDispatch)
            return true;

        var t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
        // object defaults to VARIANT/IUnknown and an interface to its COM vtable — both
        // need the built-in COM marshaller. Delegates, strings, structs and pointers don't.
        return t == typeof(object) || t.IsInterface;
    }

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only guard: enumerates every type of the scanned assembly (Assembly.GetTypes) by design. This host is never trimmed. Behaviour-neutral.")]
    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    // Deliberately-bad shapes for the positive control above. Never instantiated or called.
    internal static partial class Samples
    {
        [ComImport, Guid("00000000-0000-0000-0000-0000000000a1")]
        internal class LegacyCoClass { }

        [ComImport, Guid("00000000-0000-0000-0000-0000000000a2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface ILegacyCom { }

        [Guid("00000000-0000-0000-0000-0000000000a3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IInterfaceTypeOnly { }

        [ComImport, Guid("00000000-0000-0000-0000-0000000000a5"), CoClass(typeof(LegacyCoClass))]
        internal interface ICoClassed { }

        [GeneratedComInterface, Guid("00000000-0000-0000-0000-0000000000a4")]
        internal partial interface IGenerated { }

#pragma warning disable SYSLIB1054 // the built-in DllImport shape is the point of this sample
        [DllImport("ole32.dll")]
        internal static extern int TakesInterface([MarshalAs(UnmanagedType.Interface)] ILegacyCom value);

        [DllImport("ole32.dll")]
        internal static extern int TakesObject(object value);

        [DllImport("ole32.dll")]
        internal static extern int ReturnsUnknown([MarshalAs(UnmanagedType.IUnknown)] out object value);

        [DllImport("ole32.dll")]
        internal static extern int TakesPointer(nint value);
#pragma warning restore SYSLIB1054
    }
}
