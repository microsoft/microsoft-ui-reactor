using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Guards <c>Reactor</c> and <c>Reactor.Advanced</c> against the runtime's built-in COM
/// interop: <c>[ComImport]</c> / <c>[CoClass]</c> types, <c>[InterfaceType]</c> without
/// <c>[GeneratedComInterface]</c>, and P/Invokes whose signatures need the built-in COM
/// marshaller. Built-in COM generates its marshalling IL stubs at run time, so it is
/// unsupported under Native AOT ("Windows: No built-in COM" —
/// https://learn.microsoft.com/dotnet/core/deploying/native-aot/#limitations-of-native-aot-deployment).
/// The library build stays green because the trim/AOT Roslyn analyzer does not flag
/// <c>[ComImport]</c>; the break only surfaces as ILC <c>IL3052</c>/<c>IL3053</c>/
/// <c>CsWinRT1033</c> or a "will always throw" stub when a consumer publishes with
/// Native AOT and reaches the code (issue #1402). This headless reflection check
/// catches it at PR time regardless of which path reaches it.
/// </summary>
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only guard: enumerates every type of the scanned assemblies (Assembly.GetTypes) and loads a Roslyn-compiled sample assembly. JIT-only host, never trimmed.")]
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Test-only guard: reads field/method metadata of types reached by the GetTypes scan. JIT-only host, never trimmed.")]
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only guard: reads field/method metadata of types reached by the GetTypes scan. JIT-only host, never trimmed.")]
public sealed class BuiltInComInteropGuardTests
{
    private const string Remedy =
        "For COM types use [GeneratedComInterface] + StrategyBasedComWrappers + explicit CoCreateInstance; " +
        "for P/Invokes use [LibraryImport] and pass COM pointers as nint, wrapping them with " +
        "StrategyBasedComWrappers.GetOrCreateObjectForComInstance " +
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
            string.Join("\n  ", violations.Select(v => v.Message)) + "\n" + Remedy);
    }

    // ── Positive / negative controls ────────────────────────────────────────
    // Each flagged sample is reachable by exactly one scanner branch, so deleting any
    // branch turns its row red. The samples are compiled in memory rather than declared
    // in this assembly, so the test assembly itself carries no built-in COM interop.

    [Theory]
    [InlineData("GuardSamples.LegacyCoClass", "[ComImport] class")]
    [InlineData("GuardSamples.ILegacyCom", "[ComImport] interface")]
    [InlineData("GuardSamples.IInterfaceTypeOnly", "[InterfaceType] without [GeneratedComInterface]")]
    [InlineData("GuardSamples.ICoClassed", "[CoClass]")]
    [InlineData("GuardSamples.Native.TakesInterface", "'value'")]
    [InlineData("GuardSamples.Native.TakesObject", "'value'")]
    [InlineData("GuardSamples.Native.TakesOutInterface", "'value'")]
    [InlineData("GuardSamples.Native.TakesCoClassAsIDispatch", "'value'")]
    [InlineData("GuardSamples.Native.TakesSafeArray", "'values'")]
    [InlineData("GuardSamples.Native.TakesUnknownSubTypeArray", "'values'")]
    [InlineData("GuardSamples.Native.ReturnsUnknown", "'return'")]
    [InlineData("GuardSamples.Native.TakesObjectArray", "'values'")]
    [InlineData("GuardSamples.Native.TakesNestedStructWithInterfaceField", "'value'")]
    [InlineData("GuardSamples.Native.TakesStructWithUnknownField", "'value'")]
    [InlineData("GuardSamples.Native.TakesFormattedClassWithObjectField", "'value'")]
    [InlineData("GuardSamples.Native.TakesFormattedDerivedWithPrivateBaseObject", "'value'")]
    [InlineData("GuardSamples.Native.TakesCallbackTakingObject", "'callback'")]
    public void Guard_FlagsBuiltInComShape(string member, string reason)
    {
        var hits = SampleViolations.Value.Where(v => v.Member == member).ToArray();
        Assert.True(
            hits.Any(v => v.Message.Contains(reason, StringComparison.Ordinal)),
            $"{member} was not flagged for {reason}. Got:\n  " + string.Join("\n  ", hits.Select(v => v.Message)));
    }

    [Theory]
    [InlineData("GuardSamples.IGenerated")]
    [InlineData("GuardSamples.IGeneratedWithInterfaceType")]
    [InlineData("GuardSamples.Native.TakesPointer")]
    [InlineData("GuardSamples.Native.TakesPointerArray")]
    [InlineData("GuardSamples.Native.TakesBlittableStruct")]
    [InlineData("GuardSamples.Native.TakesString")]
    [InlineData("GuardSamples.Native.TakesBlittableCallback")]
    [InlineData("GuardSamples.Native.TakesSelfReferencingStruct")]
    [InlineData("GuardSamples.Native.TakesUnknownFieldStructPointer")]
    [InlineData("GuardSamples.Native.TakesHandleRef")]
    public void Guard_AllowsAotSafeShape(string member)
    {
        var hits = SampleViolations.Value.Where(v => v.Member == member).Select(v => v.Message).ToArray();
        Assert.True(hits.Length == 0, $"{member} was wrongly flagged:\n  " + string.Join("\n  ", hits));
    }

    // ── Scanner ─────────────────────────────────────────────────────────────

    private readonly record struct Violation(string Member, string Message);

    private static List<Violation> FindViolations(Assembly assembly)
    {
        var violations = new List<Violation>();
        // No ReflectionTypeLoadException fallback: a type that can't load can't be
        // inspected, so an incomplete scan must fail the guard rather than pass it.
        foreach (var type in assembly.GetTypes())
        {
            var name = type.FullName!;
            if (type.IsImport)
                violations.Add(new(name, $"[ComImport] {(type.IsInterface ? "interface" : "class")} {name}"));
            else if (type.IsInterface
                && type.IsDefined(typeof(InterfaceTypeAttribute), inherit: false)
                && !type.IsDefined(typeof(GeneratedComInterfaceAttribute), inherit: false))
                violations.Add(new(name, $"[InterfaceType] without [GeneratedComInterface] on {name}"));

            if (type.IsDefined(typeof(CoClassAttribute), inherit: false))
                violations.Add(new(name, $"[CoClass] on {name}"));

            var pinvokes = type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => (m.Attributes & MethodAttributes.PinvokeImpl) != 0);
            foreach (var method in pinvokes)
            {
                var member = $"{name}.{method.Name}";
                foreach (var p in method.GetParameters().Append(method.ReturnParameter).Where(UsesBuiltInComMarshalling))
                    violations.Add(new(member, $"P/Invoke {member} marshals '{(p.Position < 0 ? "return" : p.Name)}' ({p.ParameterType.Name}) through built-in COM"));
            }
        }
        return violations;
    }

    private static bool UsesBuiltInComMarshalling(ParameterInfo p)
        => IsComMarshalAs(p.GetCustomAttribute<MarshalAsAttribute>())
            || NeedsComMarshaller(p.ParameterType, new HashSet<Type>());

    private static bool IsComMarshalAs(MarshalAsAttribute? marshalAs)
        => marshalAs is not null && (IsComUnmanagedType(marshalAs.Value) || IsComUnmanagedType(marshalAs.ArraySubType));

    private static bool IsComUnmanagedType(UnmanagedType type)
        => type is UnmanagedType.Interface or UnmanagedType.IUnknown or UnmanagedType.IDispatch or UnmanagedType.SafeArray;

    // object marshals as VARIANT/IUnknown and an interface as its COM vtable — both need
    // the built-in COM marshaller, also when nested in an array, a struct / formatted
    // class field, or a delegate's signature. Strings and blittable data don't, and an
    // unmanaged pointer is passed as a raw address, so its pointee is never marshalled.
    // HandleRef marshals as its IntPtr; its object wrapper only keeps the owner alive.
    private static bool NeedsComMarshaller(Type t, HashSet<Type> visited)
    {
        if (t.IsPointer || t == typeof(HandleRef))
            return false;
        if (t.IsByRef || t.IsArray)
            return NeedsComMarshaller(t.GetElementType()!, visited);
        if (t == typeof(object) || t.IsInterface)
            return true;
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || !visited.Add(t))
            return false;

        if (typeof(Delegate).IsAssignableFrom(t))
        {
            var invoke = t.GetMethod("Invoke");
            return invoke is not null
                && invoke.GetParameters().Append(invoke.ReturnParameter)
                    .Any(p => IsComMarshalAs(p.GetCustomAttribute<MarshalAsAttribute>()) || NeedsComMarshaller(p.ParameterType, visited));
        }

        // Structs and formatted (sequential/explicit layout) classes are marshalled field by
        // field, including private fields declared on base classes (which GetFields omits).
        if (t.IsValueType || (t.IsClass && !t.IsAutoLayout))
        {
            for (var declaring = t; declaring is not null && declaring != typeof(object) && declaring != typeof(ValueType); declaring = declaring.BaseType)
            {
                if (declaring.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Any(f => IsComMarshalAs(f.GetCustomAttribute<MarshalAsAttribute>()) || NeedsComMarshaller(f.FieldType, visited)))
                    return true;
            }
        }
        return false;
    }

    // ── In-memory sample assembly ───────────────────────────────────────────

    private static readonly Lazy<List<Violation>> SampleViolations = new(() => FindViolations(LoadSamples()));

    private static Assembly LoadSamples()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        var references = tpa.Split(global::System.IO.Path.PathSeparator)
            .Where(static p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && global::System.IO.File.Exists(p))
            .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToImmutableArray();
        var compilation = CSharpCompilation.Create(
            $"GuardSamples{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(SamplesSource)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var stream = new global::System.IO.MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        return new AssemblyLoadContext($"com-guard-{Guid.NewGuid():N}", isCollectible: true).LoadFromStream(stream);
    }

    // Deliberately-bad shapes. Metadata only: never instantiated or called.
    private const string SamplesSource = """
        using System.Runtime.InteropServices;
        using System.Runtime.InteropServices.Marshalling;

        namespace GuardSamples;

        [ComImport, Guid("00000000-0000-0000-0000-0000000000a1")]
        public class LegacyCoClass { }

        [ComImport, Guid("00000000-0000-0000-0000-0000000000a2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ILegacyCom { }

        [Guid("00000000-0000-0000-0000-0000000000a3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IInterfaceTypeOnly { }

        [ComImport, Guid("00000000-0000-0000-0000-0000000000a5"), CoClass(typeof(LegacyCoClass))]
        public interface ICoClassed { }

        [GeneratedComInterface, Guid("00000000-0000-0000-0000-0000000000a4")]
        public partial interface IGenerated { }

        [GeneratedComInterface, Guid("00000000-0000-0000-0000-0000000000a6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public partial interface IGeneratedWithInterfaceType { }

        public struct StructWithInterfaceField { public int A; public IGenerated Field; }
        public struct NestedStructWithInterfaceField { public StructWithInterfaceField Inner; }
        public struct StructWithUnknownField { [MarshalAs(UnmanagedType.IUnknown)] public nint Field; }
        [StructLayout(LayoutKind.Sequential)] public class FormattedClassWithObjectField { public object Field; }
        [StructLayout(LayoutKind.Sequential)] public class FormattedBaseWithPrivateObjectField { private object _hidden; }
        [StructLayout(LayoutKind.Sequential)] public class FormattedDerived : FormattedBaseWithPrivateObjectField { public int A; }
        public struct BlittableStruct { public int A; public nint B; }
        public unsafe struct SelfReferencingStruct { public SelfReferencingStruct* Next; public int Value; }
        public delegate int CallbackTakingObject(object value);
        public delegate int BlittableCallback(nint value);

        public static class Native
        {
            // Caught by the type check only.
            [DllImport("ole32.dll")] public static extern int TakesInterface(IGenerated value);
            [DllImport("ole32.dll")] public static extern int TakesObject(object value);
            [DllImport("ole32.dll")] public static extern int TakesOutInterface(out IGenerated value);
            [DllImport("ole32.dll")] public static extern int TakesObjectArray(object[] values);
            [DllImport("ole32.dll")] public static extern int TakesNestedStructWithInterfaceField(ref NestedStructWithInterfaceField value);
            [DllImport("ole32.dll")] public static extern int TakesFormattedClassWithObjectField(FormattedClassWithObjectField value);
            [DllImport("ole32.dll")] public static extern int TakesFormattedDerivedWithPrivateBaseObject(FormattedDerived value);
            [DllImport("ole32.dll")] public static extern int TakesCallbackTakingObject(CallbackTakingObject callback);

            // Caught by the [MarshalAs] check only.
            [DllImport("ole32.dll")] public static extern int TakesCoClassAsIDispatch([MarshalAs(UnmanagedType.IDispatch)] LegacyCoClass value);
            [DllImport("ole32.dll")] public static extern int TakesSafeArray([MarshalAs(UnmanagedType.SafeArray)] int[] values);
            [DllImport("ole32.dll")] public static extern int TakesUnknownSubTypeArray([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.IUnknown)] nint[] values);
            [DllImport("ole32.dll")] public static extern int TakesStructWithUnknownField(ref StructWithUnknownField value);
            [DllImport("ole32.dll")] [return: MarshalAs(UnmanagedType.IUnknown)] public static extern nint ReturnsUnknown();

            // AOT-safe shapes that must stay unflagged.
            [DllImport("ole32.dll")] public static extern int TakesPointer(nint value);
            [DllImport("ole32.dll")] public static extern int TakesPointerArray(nint[] values);
            [DllImport("ole32.dll")] public static extern int TakesBlittableStruct(ref BlittableStruct value);
            [DllImport("ole32.dll")] public static extern int TakesString([MarshalAs(UnmanagedType.LPWStr)] string value);
            [DllImport("ole32.dll")] public static extern int TakesBlittableCallback(BlittableCallback callback);
            [DllImport("ole32.dll")] public static extern unsafe int TakesSelfReferencingStruct(SelfReferencingStruct* value);
            [DllImport("ole32.dll")] public static extern unsafe int TakesUnknownFieldStructPointer(StructWithUnknownField* value);
            [DllImport("user32.dll")] public static extern int TakesHandleRef(HandleRef hwnd);
        }
        """;
}
