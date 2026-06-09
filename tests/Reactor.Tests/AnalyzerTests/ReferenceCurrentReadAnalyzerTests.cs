using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

using AnalyzerVerifier = CSharpAnalyzerVerifier<ReferenceCurrentReadAnalyzer, DefaultVerifier>;

public class ReferenceCurrentReadAnalyzerTests
{
    [Fact]
    public async Task Detects_Current_Assigned_To_Reference_Property_In_Update()
    {
        var test = @"
namespace Microsoft.UI.Reactor.Input
{
    class ElementRef<T> { public T Current => default(T); }
}
namespace Microsoft.UI.Xaml
{
    class FrameworkElement { public FrameworkElement Target { get; set; } }
}
class MyControlHandler
{
    void Update(Microsoft.UI.Xaml.FrameworkElement control, Microsoft.UI.Reactor.Input.ElementRef<Microsoft.UI.Xaml.FrameworkElement> targetRef)
    {
        control.Target = targetRef.Current;
    }
}";

        var expected = AnalyzerVerifier.Diagnostic(ReferenceCurrentReadAnalyzer.DiagnosticId)
            .WithSpan(14, 26, 14, 43);

        var analyzerTest = new CSharpAnalyzerTest<ReferenceCurrentReadAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };

        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Detects_Current_As_Target_Assigned_To_XYFocus_Property()
    {
        var test = @"
namespace Microsoft.UI.Reactor.Input
{
    class ElementRef { public object Current => null; }
}
namespace Microsoft.UI.Xaml
{
    class FrameworkElement { public FrameworkElement XYFocusRight { get; set; } }
}
class MyControlDescriptor
{
    void Mount(Microsoft.UI.Xaml.FrameworkElement control, Microsoft.UI.Reactor.Input.ElementRef targetRef)
    {
        control.XYFocusRight = targetRef.Current as Microsoft.UI.Xaml.FrameworkElement;
    }
}";

        var expected = AnalyzerVerifier.Diagnostic(ReferenceCurrentReadAnalyzer.DiagnosticId)
            .WithSpan(14, 32, 14, 49);

        var analyzerTest = new CSharpAnalyzerTest<ReferenceCurrentReadAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };

        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task No_Diagnostic_For_Non_Reference_Property()
    {
        var test = @"
namespace Microsoft.UI.Reactor.Input
{
    class ElementRef<T> { public T Current => default(T); }
}
namespace Microsoft.UI.Xaml
{
    class FrameworkElement { public FrameworkElement Tag { get; set; } }
}
class MyControlHandler
{
    void Update(Microsoft.UI.Xaml.FrameworkElement control, Microsoft.UI.Reactor.Input.ElementRef<Microsoft.UI.Xaml.FrameworkElement> targetRef)
    {
        control.Tag = targetRef.Current;
    }
}";

        var analyzerTest = new CSharpAnalyzerTest<ReferenceCurrentReadAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };

        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task No_Diagnostic_For_Reactive_Descriptor_Reference()
    {
        var test = @"
namespace Microsoft.UI.Reactor.Input
{
    class ElementRef<T> { public T Current => default(T); }
}
namespace Microsoft.UI.Xaml
{
    class FrameworkElement { public FrameworkElement Target { get; set; } }
}
class DescriptorBuilder
{
    void Build(dynamic descriptor)
    {
        descriptor.Reference<Microsoft.UI.Xaml.FrameworkElement>(get: null, set: null);
    }
}";

        var analyzerTest = new CSharpAnalyzerTest<ReferenceCurrentReadAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };

        await analyzerTest.RunAsync();
    }
}
