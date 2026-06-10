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

    [Fact]
    public async Task Detects_Current_Passed_To_Attached_SetLabeledBy()
    {
        // CR-006: AutomationProperties.SetLabeledBy(control, ref.Current) is just as
        // non-reactive as a direct property assignment.
        var test = @"
namespace Microsoft.UI.Reactor.Input
{
    class ElementRef { public Microsoft.UI.Xaml.FrameworkElement Current => null; }
}
namespace Microsoft.UI.Xaml
{
    class FrameworkElement { }
}
namespace Microsoft.UI.Xaml.Automation
{
    static class AutomationProperties
    {
        public static void SetLabeledBy(Microsoft.UI.Xaml.FrameworkElement e, Microsoft.UI.Xaml.FrameworkElement v) { }
    }
}
class MyControlHandler
{
    void Update(Microsoft.UI.Xaml.FrameworkElement control, Microsoft.UI.Reactor.Input.ElementRef targetRef)
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLabeledBy(control, {|REACTOR_REF_001:targetRef.Current|});
    }
}";

        await new CSharpAnalyzerTest<ReferenceCurrentReadAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync();
    }

    [Fact]
    public async Task Detects_Current_Added_To_Attached_Relationship_List()
    {
        // CR-006: pushing ref.Current into an AutomationProperties relationship list
        // (GetDescribedBy(control).Add(ref.Current)) is the list-valued variant.
        var test = @"
namespace Microsoft.UI.Reactor.Input
{
    class ElementRef { public Microsoft.UI.Xaml.FrameworkElement Current => null; }
}
namespace Microsoft.UI.Xaml
{
    class FrameworkElement { }
}
namespace Microsoft.UI.Xaml.Automation
{
    static class AutomationProperties
    {
        public static System.Collections.Generic.IList<Microsoft.UI.Xaml.FrameworkElement> GetDescribedBy(Microsoft.UI.Xaml.FrameworkElement e) => null;
    }
}
class MyControlDescriptor
{
    void Mount(Microsoft.UI.Xaml.FrameworkElement control, Microsoft.UI.Reactor.Input.ElementRef targetRef)
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.GetDescribedBy(control).Add({|REACTOR_REF_001:targetRef.Current|});
    }
}";

        await new CSharpAnalyzerTest<ReferenceCurrentReadAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync();
    }

    [Fact]
    public async Task Detects_Current_Assigned_To_PlacementTarget()
    {
        var test = @"
namespace Microsoft.UI.Reactor.Input
{
    class ElementRef { public Microsoft.UI.Xaml.FrameworkElement Current => null; }
}
namespace Microsoft.UI.Xaml
{
    class FrameworkElement { }
}
namespace Microsoft.UI.Xaml.Controls.Primitives
{
    class Popup : Microsoft.UI.Xaml.FrameworkElement
    {
        public Microsoft.UI.Xaml.FrameworkElement PlacementTarget { get; set; }
    }
}
class PopupDescriptor
{
    void Mount(Microsoft.UI.Xaml.Controls.Primitives.Popup popup, Microsoft.UI.Reactor.Input.ElementRef targetRef)
    {
        popup.PlacementTarget = {|REACTOR_REF_001:targetRef.Current|};
    }
}";

        await new CSharpAnalyzerTest<ReferenceCurrentReadAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync();
    }

    [Fact]
    public async Task Detects_Current_Assignment_In_Binding_Context()
    {
        var test = @"
namespace Microsoft.UI.Reactor.Input
{
    class ElementRef { public Microsoft.UI.Xaml.FrameworkElement Current => null; }
}
namespace Microsoft.UI.Xaml
{
    class FrameworkElement { public FrameworkElement XYFocusDown { get; set; } }
}
class FocusBinding
{
    void Wire(Microsoft.UI.Xaml.FrameworkElement control, Microsoft.UI.Reactor.Input.ElementRef targetRef)
    {
        control.XYFocusDown = {|REACTOR_REF_001:targetRef.Current|};
    }
}";

        await new CSharpAnalyzerTest<ReferenceCurrentReadAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync();
    }

    [Fact]
    public async Task No_Diagnostic_For_Unrelated_Property_Named_Target()
    {
        // CR-007: a property merely named 'Target' on a non-WinUI type must not warn,
        // even inside a handler-like class.
        var test = @"
namespace Microsoft.UI.Reactor.Input
{
    class ElementRef { public object Current => null; }
}
class Unrelated { public object Target { get; set; } }
class MyControlHandler
{
    void Update(Unrelated thing, Microsoft.UI.Reactor.Input.ElementRef r)
    {
        thing.Target = r.Current;
    }
}";

        await new CSharpAnalyzerTest<ReferenceCurrentReadAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync();
    }

    [Fact]
    public async Task No_Diagnostic_For_Unrelated_Property_Named_LabeledBy()
    {
        var test = @"
namespace Microsoft.UI.Reactor.Input
{
    class ElementRef { public object Current => null; }
}
class Unrelated { public object LabeledBy { get; set; } }
class MyControlDescriptor
{
    void Mount(Unrelated thing, Microsoft.UI.Reactor.Input.ElementRef r)
    {
        thing.LabeledBy = r.Current;
    }
}";

        await new CSharpAnalyzerTest<ReferenceCurrentReadAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync();
    }
}
