using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Unit tests for REACTOR_A11Y_001: icon-only button needs an accessible name.
/// Uses class-level static stubs so Button/Image/etc. compile in the Roslyn test context.
/// </summary>
public class IconButtonAccessibilityAnalyzerTests
{
    private static DiagnosticResult Diagnostic(string id) =>
        CSharpAnalyzerVerifier<IconButtonAccessibilityAnalyzer, DefaultVerifier>
            .Diagnostic(id);

    [Fact]
    public async Task IconButton_Without_AutomationName_Produces_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic Button(object content, System.Action onClick) => null;

    void M(object icon)
    {
        Button(icon, () => { });
    }
}";
        var expected = Diagnostic(IconButtonAccessibilityAnalyzer.DiagnosticId)
            .WithSpan(8, 9, 8, 32);

        var analyzerTest = new CSharpAnalyzerTest<IconButtonAccessibilityAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IconButton_With_AutomationName_No_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic Button(object content, System.Action onClick) => null;

    void M(object icon)
    {
        Button(icon, () => { }).AutomationName(""Delete"");
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<IconButtonAccessibilityAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TextButton_No_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic Button(string label, System.Action onClick) => null;

    void M()
    {
        Button(""Click me"", () => { });
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<IconButtonAccessibilityAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>
/// Unit tests for REACTOR_A11Y_002: image needs alt text or AccessibilityHidden().
/// </summary>
public class ImageAccessibilityAnalyzerTests
{
    private static DiagnosticResult Diagnostic(string id) =>
        CSharpAnalyzerVerifier<ImageAccessibilityAnalyzer, DefaultVerifier>
            .Diagnostic(id);

    [Fact]
    public async Task Image_Without_AltText_Produces_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic Image(dynamic source) => null;

    void M(dynamic uri)
    {
        Image(uri);
    }
}";
        var expected = Diagnostic(ImageAccessibilityAnalyzer.DiagnosticId)
            .WithSpan(8, 9, 8, 19);

        var analyzerTest = new CSharpAnalyzerTest<ImageAccessibilityAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Image_With_AutomationName_No_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic Image(dynamic source) => null;

    void M(dynamic uri)
    {
        Image(uri).AutomationName(""Company logo"");
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<ImageAccessibilityAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Image_With_AccessibilityHidden_No_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic Image(dynamic source) => null;

    void M(dynamic uri)
    {
        Image(uri).AccessibilityHidden();
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<ImageAccessibilityAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>
/// Unit tests for REACTOR_A11Y_003: form field needs a label.
/// </summary>
public class FormFieldLabelAnalyzerTests
{
    private static DiagnosticResult Diagnostic(string id) =>
        CSharpAnalyzerVerifier<FormFieldLabelAnalyzer, DefaultVerifier>
            .Diagnostic(id);

    [Fact]
    public async Task TextBox_Without_Label_Produces_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic TextBox(dynamic value, string header = null) => null;

    void M(dynamic value)
    {
        TextBox(value);
    }
}";
        var expected = Diagnostic(FormFieldLabelAnalyzer.DiagnosticId)
            .WithSpan(8, 9, 8, 23);

        var analyzerTest = new CSharpAnalyzerTest<FormFieldLabelAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TextBox_With_Header_No_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic TextBox(dynamic value, string header = null) => null;

    void M(dynamic value)
    {
        TextBox(value, header: ""Name"");
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<FormFieldLabelAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TextBox_With_AutomationName_No_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic TextBox(dynamic value, string header = null) => null;

    void M(dynamic value)
    {
        TextBox(value).AutomationName(""Search"");
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<FormFieldLabelAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TextBox_With_LabeledBy_No_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic TextBox(dynamic value, string header = null) => null;

    void M(dynamic value, dynamic label)
    {
        TextBox(value).LabeledBy(label);
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<FormFieldLabelAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A fluent <c>.Header(...)</c> renders a visible label and satisfies the rule. Before this,
    /// the analyzer only recognised a named <c>header:</c> argument, so a field written in the
    /// fluent style was told to add <c>.AutomationName(...)</c> on top of a label it already had —
    /// and the guides were changed to teach that redundancy.
    /// </summary>
    [Fact]
    public async Task PasswordBox_With_Fluent_Header_No_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic PasswordBox(dynamic value, string header = null) => null;

    void M(dynamic value)
    {
        PasswordBox(value).Header(""Password"");
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<FormFieldLabelAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The match is on the exact identifier, so a container's header property does not launder an
    /// unlabeled field sitting inside the same chain.
    /// </summary>
    [Fact]
    public async Task TextBox_With_Only_A_PaneHeader_Still_Produces_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic TextBox(dynamic value, string header = null) => null;

    void M(dynamic value)
    {
        {|REACTOR_A11Y_003:TextBox(value)|}.PaneHeader(""Not a field label"");
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<FormFieldLabelAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }
}
