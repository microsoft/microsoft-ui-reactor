using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="ComponentInpcAnalyzer"/> (<c>REACTOR_STATE_001</c>). Stubs a
/// minimal <c>Microsoft.UI.Reactor.Core.Component</c> shape so the analyzer's two-condition
/// symbol match (derives from <c>Component</c> and implements <c>INotifyPropertyChanged</c>)
/// resolves without pulling the framework in. <c>INotifyPropertyChanged</c> and the near-miss
/// <c>System.ComponentModel.Component</c> come from the default reference assemblies and are
/// fully qualified so they never collide with the stubbed Reactor <c>Component</c>.
/// </summary>
public class ComponentInpcAnalyzerTests
{
    // Mirrors the real base shape: an abstract Component plus the generic Component<TProps>
    // (which derives from the non-generic Component, exactly as in src/Reactor/Core/Component.cs).
    // The `using` sits at the top of the compilation unit so the global-namespace test types
    // below can name `Component` while the namespace block declares it.
    private const string Stubs = @"
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Core
{
    public abstract class Component { }
    public abstract class Component<TProps> : Component { }
}
";

    [Fact]
    public async Task Fires_For_Component_Implementing_Inpc()
    {
        // The XAML habit: a Component subclass raising PropertyChanged for local state.
        var source = Stubs + @"
class {|REACTOR_STATE_001:MyComponent|} : Component, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
}";

        await new CSharpAnalyzerTest<ComponentInpcAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Generic_Component_Subclass()
    {
        // Component<TProps> derives from Component, so the generic base must also trip the rule.
        var source = Stubs + @"
class MyProps { }

class {|REACTOR_STATE_001:MyComponent|} : Component<MyProps>, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
}";

        await new CSharpAnalyzerTest<ComponentInpcAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Component_Without_Inpc()
    {
        // Negative: a plain Component with no INotifyPropertyChanged is the idiomatic shape.
        var source = Stubs + @"
class MyComponent : Component
{
}";

        await new CSharpAnalyzerTest<ComponentInpcAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Plain_Inpc_ViewModel()
    {
        // Negative: a real MVVM view-model implementing INPC but not deriving Component
        // is exactly what UseObservable is meant to consume — never flag it.
        var source = Stubs + @"
class MyViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
}";

        await new CSharpAnalyzerTest<ComponentInpcAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_NonReactor_Component_Lookalike()
    {
        // Near-miss: derives from a class literally named 'Component' AND implements INPC,
        // but it is System.ComponentModel.Component — not Reactor's. The namespace-qualified
        // symbol match (not a name match) must keep this quiet.
        var source = Stubs + @"
class MyThing : System.ComponentModel.Component, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
}";

        await new CSharpAnalyzerTest<ComponentInpcAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reports_Once_On_Base_Not_Cascaded_To_Derived()
    {
        // When a base Component declared IN SOURCE introduces INPC, only the base (the mistake
        // site, flagged on its own) is reported; a derived type that merely inherits INPC in the
        // same compilation must not produce a duplicate diagnostic.
        var source = Stubs + @"
class {|REACTOR_STATE_001:MyBase|} : Component, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
}

class MyDerived : MyBase
{
}";

        await new CSharpAnalyzerTest<ComponentInpcAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_When_Inpc_Comes_Via_A_Derived_Interface()
    {
        // The 'implements INPC' check walks AllInterfaces, so an interface that
        // itself extends INotifyPropertyChanged must still trip the rule.
        var source = Stubs + @"
interface IObservableComponent : System.ComponentModel.INotifyPropertyChanged { }

class {|REACTOR_STATE_001:MyComponent|} : Component, IObservableComponent
{
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
}";

        await new CSharpAnalyzerTest<ComponentInpcAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Reactor_Component_Not_Referenced()
    {
        // The compilation-start early-out registers no symbol callback when
        // Microsoft.UI.Reactor.Core.Component cannot be resolved. Without the stubs
        // the Reactor base is absent, so even a plain INPC class must stay quiet.
        var source = @"
class MyViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
}";

        await new CSharpAnalyzerTest<ComponentInpcAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Derived_When_Inpc_Component_Base_Is_From_Metadata()
    {
        // The cascade suppression must only fire for a base declared IN SOURCE. Here the
        // Component + INPC base is compiled into a real referenced ASSEMBLY (PE metadata), so
        // its symbol's locations are not in source and it is not analyzed in this compilation.
        // The derived source type must therefore still warn — otherwise the anti-pattern would
        // produce no diagnostic at all (the false-negative this guard is designed to avoid).
        const string baseLib = @"
using System.ComponentModel;

namespace Microsoft.UI.Reactor.Core
{
    public abstract class Component { }
}

namespace Lib
{
    public abstract class ObservableComponentBase : Microsoft.UI.Reactor.Core.Component, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
    }
}";

        var references = await ReferenceAssemblies.Default.ResolveAsync(
            LanguageNames.CSharp, TestContext.Current.CancellationToken);
        var baseCompilation = CSharpCompilation.Create(
            "ReactorMetadataBaseLib",
            new[] { CSharpSyntaxTree.ParseText(baseLib, cancellationToken: TestContext.Current.CancellationToken) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        var emitResult = baseCompilation.Emit(peStream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics));
        peStream.Position = 0;
        var baseReference = MetadataReference.CreateFromStream(peStream);

        const string appCode = @"
class {|REACTOR_STATE_001:MyScreen|} : Lib.ObservableComponentBase
{
}";

        var test = new CSharpAnalyzerTest<ComponentInpcAnalyzer, DefaultVerifier>
        {
            TestCode = appCode,
        };
        test.TestState.AdditionalReferences.Add(baseReference);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
