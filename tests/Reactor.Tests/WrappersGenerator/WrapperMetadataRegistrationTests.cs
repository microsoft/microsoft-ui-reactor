using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.UI.Reactor.Wrappers.Generator;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.WrappersGenerator;

public class WrapperMetadataRegistrationTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);
    private static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .Select(p => MetadataReference.CreateFromFile(p))
        .ToImmutableArray<MetadataReference>();

    private const string ProviderBody = """
        : Microsoft.UI.Xaml.Markup.IXamlMetadataProvider
        {
            public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(Type type) => null!;
            public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(string name) => null!;
            public Microsoft.UI.Xaml.Markup.XmlnsDefinition[] GetXmlnsDefinitions() => [];
        }
        """;

    private static CSharpCompilation Compilation(string name, string source, params MetadataReference[] libraries) =>
        CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText("using System;\n" + source, ParseOptions)],
            References.AddRange(libraries),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    private static MetadataReference Library(string name, string providers)
    {
        var compilation = Compilation(name, $$"""
            namespace {{name}};
            public class TestControl : Microsoft.UI.Xaml.Controls.Control {}
            {{providers}}
            """);
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static GeneratorDriverRunResult Run(string source, params MetadataReference[] libraries)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new WrapperGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(Compilation("Consumer", source, libraries), out var output, out _);
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
        return driver.GetRunResult();
    }

    private static string Wrapper(GeneratorDriverRunResult result, string name = "TestElement") =>
        result.GeneratedTrees.Single(t => Path.GetFileName(t.FilePath) == $"{name}.Wrapper.g.cs")
            .GetText(TestContext.Current.CancellationToken).ToString();

    [Fact]
    public void ReferencedProviders_AreStaticallyConstructed_ForEachWrappersOwnLibrary()
    {
        var first = Library("First", "public class Provider " + ProviderBody);
        var second = Library("Second", "public class Container { public class Provider " + ProviderBody + " }");
        var result = Run("""
            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(First.TestControl))]
            public partial record FirstElement;
            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(First.TestControl))]
            public partial record AnotherFirstElement;
            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Second.TestControl))]
            public partial record SecondElement;
            """, first, second);

        foreach (var name in new[] { "FirstElement", "AnotherFirstElement", "SecondElement" })
        {
            var source = Wrapper(result, name);
            var provider = name == "SecondElement" ? "Second.Container.Provider" : "First.Provider";
            Assert.Contains($"static {name}()\n    {{\n        global::Microsoft.UI.Reactor.ReactorApp.RegisterControlAssembly(new global::{provider}());",
                source.Replace("\r\n", "\n"));
            Assert.DoesNotContain("TryRegisterControlAssembly", source);
            Assert.DoesNotContain("ModuleInitializer", source);
        }
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void CodeOnlyLibrary_KeepsTolerantRegistration()
    {
        var result = Run("""
            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(CodeOnly.TestControl))]
            public partial record TestElement;
            """, Library("CodeOnly", ""));

        Assert.Contains("TryRegisterControlAssembly(typeof(global::CodeOnly.TestControl).Assembly)", Wrapper(result));
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("internal class Provider")]
    [InlineData("public class Provider<T>")]
    [InlineData("public class Outer<T> { public class Provider", "}")]
    [InlineData("internal class Outer { public class Provider", "}")]
    [InlineData("public class Provider", "", "private Provider() {}")]
    [InlineData("public class Provider", "", "public required string Name { get; init; }")]
    [InlineData("public class Provider", "", "public Provider(int value) {}")]
    public void NonConstructibleProviders_WarnAndKeepJitFallback(string declaration, string suffix = "", string member = "")
    {
        var body = ProviderBody.Replace("public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(Type", member + "\npublic Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(Type");
        var result = Run("""
            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Unsupported.TestControl))]
            public partial record TestElement;
            """, Library("Unsupported", declaration + body + suffix));

        Assert.Contains("TryRegisterControlAssembly(typeof(global::Unsupported.TestControl).Assembly)", Wrapper(result));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("REACTORGEN016", diagnostic.Id);
        Assert.Contains("RegisterAssembly = false", diagnostic.GetMessage());
    }

    [Fact]
    public void AmbiguousProviders_AreNotChosenByEnumerationOrder()
    {
        var result = Run("""
            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Ambiguous.TestControl))]
            public partial record TestElement;
            """, Library("Ambiguous", "public class One " + ProviderBody + " public class Two " + ProviderBody));

        Assert.Contains("TryRegisterControlAssembly", Wrapper(result));
        Assert.Equal("REACTORGEN016", Assert.Single(result.Diagnostics).Id);
    }

    [Fact]
    public void FrameworkControl_RegistersTheCoreWinUIProvider()
    {
        // The real Microsoft.WinUI reference. On the experimental channel it also carries the
        // Charts and Tabular providers, which must not make a built-in control's wrapper ambiguous.
        Assert.NotNull(Compilation("Probe", "").GetTypeByMetadataName(
            "Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsChartsXamlMetaDataProvider"));

        var result = Run("""
            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Microsoft.UI.Xaml.Controls.StackPanel))]
            public partial record TestElement;
            """);

        var source = Wrapper(result);
        Assert.Contains("RegisterControlAssembly(new global::Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider())", source);
        Assert.DoesNotContain("TryRegisterControlAssembly", source);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ExplicitOptOut_AndDescriptorOnly_DoNotRegisterOrWarn()
    {
        var result = Run("""
            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Custom.TestControl), RegisterAssembly = false)]
            public partial record TestElement;
            [Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(Custom.TestControl))]
            public partial record DescriptorElement : Microsoft.UI.Reactor.Core.Element
            {
                public Action<Custom.TestControl>[] Setters { get; init; } = [];
            }
            """, Library("Custom", "public class One " + ProviderBody + " public class Two " + ProviderBody));

        Assert.All(result.GeneratedTrees, tree => Assert.DoesNotContain("RegisterControlAssembly",
            tree.GetText(TestContext.Current.CancellationToken).ToString()));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void SourceProvider_IsSelectedWithoutConstructingAnApplication()
    {
        var result = Run("""
            namespace Local;
            public class TestControl : Microsoft.UI.Xaml.Controls.Control {}
            public class App : Microsoft.UI.Xaml.Application, Microsoft.UI.Xaml.Markup.IXamlMetadataProvider
            {
                public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(Type type) => null!;
                public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(string name) => null!;
                public Microsoft.UI.Xaml.Markup.XmlnsDefinition[] GetXmlnsDefinitions() => [];
            }
            """ + "public class Provider " + ProviderBody + """
            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(TestControl))]
            public partial record TestElement;
            """);

        Assert.Contains("RegisterControlAssembly(new global::Local.Provider())", Wrapper(result));
        Assert.Empty(result.Diagnostics);
    }
}
