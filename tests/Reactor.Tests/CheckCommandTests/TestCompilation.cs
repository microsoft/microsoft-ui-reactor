// In-memory CSharpCompilation builder for Suggester / FactoryIndex tests.
// We deliberately avoid loading the real Reactor.dll: the suggesters are pure
// over Compilation/Diagnostic/SyntaxNode, so a minimal stub type tree is the
// fastest, most deterministic surface to test against. Spec 038 §5 conventions.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests;

internal static class TestCompilation
{
    static readonly Lazy<MetadataReference[]> _defaultReferences = new(() =>
    {
        // Reference standard runtime / framework assemblies but deliberately
        // EXCLUDE the loaded Reactor / WinUI assemblies so stubs are the
        // exclusive source of Reactor types in tests. This keeps the
        // FactoryIndex / Suggester tests reproducible regardless of which
        // project graph the test process happens to have loaded.
        var refs = new List<MetadataReference>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            string? loc;
            try { loc = asm.Location; } catch { continue; }
            if (string.IsNullOrEmpty(loc)) continue;

            var name = asm.GetName().Name ?? "";
            if (IsReactorOrWinUI(name)) continue;

            try { refs.Add(MetadataReference.CreateFromFile(loc)); }
            catch { /* skip unreadable */ }
        }
        return refs.ToArray();
    });

    static bool IsReactorOrWinUI(string asmName)
    {
        if (asmName.StartsWith("Reactor", StringComparison.OrdinalIgnoreCase)) return true;
        if (asmName.StartsWith("Microsoft.UI.Xaml", StringComparison.OrdinalIgnoreCase)) return true;
        if (asmName.StartsWith("Microsoft.WindowsAppRuntime", StringComparison.OrdinalIgnoreCase)) return true;
        if (asmName.StartsWith("WinRT", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static CSharpCompilation Create(string source, string assemblyName = "TestAssembly", string path = "Test.cs")
        => Create(new[] { (source, path) }, assemblyName);

    public static CSharpCompilation Create(IEnumerable<string> sources, string assemblyName = "TestAssembly")
        => Create(sources.Select(s => (s, "Test.cs")), assemblyName);

    public static CSharpCompilation Create(IEnumerable<(string source, string path)> sources, string assemblyName = "TestAssembly")
    {
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s.source, path: s.path));
        return CSharpCompilation.Create(
            assemblyName,
            trees,
            _defaultReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    public static IReadOnlyList<Diagnostic> ErrorsAndWarnings(this CSharpCompilation comp)
        => comp.GetDiagnostics()
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .ToList();
}
