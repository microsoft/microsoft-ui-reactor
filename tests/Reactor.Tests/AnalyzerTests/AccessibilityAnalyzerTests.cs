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
    public async Task TextButton_With_Localized_String_Label_No_Diagnostic()
    {
        // A non-literal string label (for example a resource lookup) is still a text
        // button: its content provides the accessible name.
        var test = @"
class C
{
    static dynamic Button(string label, System.Action onClick) => null;
    static string S(string key) => key;

    void M(string label)
    {
        Button(S(""Refresh""), () => { });
        Button(label, () => { });
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<IconButtonAccessibilityAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IconButton_With_Element_Content_Still_Produces_Diagnostic()
    {
        var test = @"
class C
{
    static dynamic Button(object content, System.Action onClick) => null;
    static object Glyph(string glyph) => glyph;

    void M()
    {
        Button(Glyph(""x""), () => { });
    }
}";
        var expected = Diagnostic(IconButtonAccessibilityAnalyzer.DiagnosticId)
            .WithSpan(9, 9, 9, 38);

        var analyzerTest = new CSharpAnalyzerTest<IconButtonAccessibilityAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Button_With_Real_Overload_Set_Distinguishes_Text_From_Element_Content()
    {
        // Mirrors Dsl.cs: Button(string, ...) and Button(Element, ...), where Element has an
        // implicit conversion from string. A label type with an implicit conversion to
        // string binds to the text overload, so it is a text button too.
        var test = @"
abstract class Element
{
    public static implicit operator Element(string text) => null;
}
sealed class LocalizedString
{
    public static implicit operator string(LocalizedString s) => null;
}
class C
{
    static object Button(string label, System.Action onClick = null) => null;
    static object Button(Element content, System.Action onClick = null) => null;
    static string S(string key) => key;

    void M(string label, LocalizedString localized, Element icon)
    {
        Button(label, () => { });
        Button(S(""Usage_Refresh""), () => { });
        Button(localized, () => { });
        {|REACTOR_A11Y_001:Button(icon, () => { })|};
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

/// <summary>
/// REACTOR_A11Y_003 coverage of <c>ComboBox</c> / <c>ToggleSwitch</c> (issue #1007), and the label
/// sources that keep it from firing on fields that already have an accessible name. The stubs
/// mirror the real Dsl.cs signatures and bind normally, so positional arguments are mapped through
/// the semantic model exactly as in user code.
/// </summary>
public class FormFieldLabelCoverageAnalyzerTests
{
    private const string Stubs = @"
using System;
using System.Linq;
using static F;

namespace System.Runtime.CompilerServices { public static class IsExternalInit { } }
namespace CommunityToolkit.WinUI.Controls { public class SettingsCard { } }
namespace Microsoft.UI.Reactor.Controls.Validation
{
    public static class FormFieldDsl
    {
        public static Element FormField(Element content, string label = null, bool required = false) => content;
    }
}

public record Element
{
    public string Header { get; init; }
    public string OnContent { get; init; }
}

public sealed class GenerateReactorWrapperAttribute : Attribute
{
    public GenerateReactorWrapperAttribute(Type wrapped) { }
}

[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.SettingsCard))]
public partial record SettingsCardElement : Element
{
    public static SettingsCardElement SettingsCard(string header = null, string description = null, Element content = null) => null;
}

public static class F
{
    public static Element ComboBox(string[] items, int selectedIndex = 0, Action<int> onSelectedIndexChanged = null) => new();
    public static Element ToggleSwitch(bool isOn = false, Action<bool> onIsOnChanged = null, string onContent = null, string offContent = null, string header = null) => new();
    public static Element TextBox(string value = null, Action<string> onChanged = null, string placeholderText = null, string header = null) => new();
    public static Element NumberBox(double value = 0, Action<double> onValueChanged = null, string header = null) => new();
    public static Element TextBlock(string text) => new();
    public static Element VStack(params Element[] children) => new();
    public static Element HStack(params Element[] children) => new();
    public static Element Header(this Element e, string header) => e;
    public static Element PlaceholderText(this Element e, string text) => e;
    public static Element AutomationName(this Element e, string name) => e;
    public static Element Width(this Element e, double width) => e;
}
";

    private static Task VerifyAsync(string body)
    {
        var test = new CSharpAnalyzerTest<FormFieldLabelAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task Bare_ComboBox_And_ToggleSwitch_Produce_Diagnostics() => VerifyAsync(@"
class C
{
    Element M(string[] items) => VStack(
        {|REACTOR_A11Y_003:ComboBox(items, 0, _ => { })|},
        {|REACTOR_A11Y_003:ToggleSwitch(false, _ => { })|});
}");

    [Fact]
    public Task ComboBox_Header_Or_AutomationName_Satisfies_Rule() => VerifyAsync(@"
class C
{
    Element M(string[] items) => VStack(
        ComboBox(items, 0, _ => { }).Header(""Colors""),
        ComboBox(items).Width(120).AutomationName(""Colors""),
        (ComboBox(items) with { Header = ""Colors"" }));
}");

    /// <summary>
    /// WinUI's ComboBoxAutomationPeer reads PlaceholderText into HelpText, never Name, so a
    /// placeholder alone leaves the combo unnamed.
    /// </summary>
    [Fact]
    public Task ComboBox_With_Only_PlaceholderText_Produces_Diagnostic() => VerifyAsync(@"
class C
{
    Element M(string[] items) => {|REACTOR_A11Y_003:ComboBox(items)|}.PlaceholderText(""Pick a color"");
}");

    [Fact]
    public Task ToggleSwitch_Header_Or_OnOff_Content_Satisfies_Rule() => VerifyAsync(@"
class C
{
    Element M() => VStack(
        ToggleSwitch(false, _ => { }).Header(""Wi-Fi""),
        ToggleSwitch(false, _ => { }, header: ""Wi-Fi""),
        ToggleSwitch(true, _ => { }, ""Working"", ""Not working""),
        ToggleSwitch(false, _ => { }, offContent: ""Off only""),
        (ToggleSwitch() with { OnContent = ""On"" }));
}");

    [Fact]
    public Task ToggleSwitch_With_Null_Label_Arguments_Produces_Diagnostic() => VerifyAsync(@"
class C
{
    Element M() => VStack(
        {|REACTOR_A11Y_003:ToggleSwitch(false, null, null, null, null)|},
        {|REACTOR_A11Y_003:ToggleSwitch(header: null)|});
}");

    /// <summary>A positional header binds to the <c>header</c> parameter just like a named one.</summary>
    [Fact]
    public Task Positional_Header_Satisfies_Rule() => VerifyAsync(@"
class C
{
    Element M() => VStack(
        TextBox(""v"", _ => { }, ""you@example.com"", ""Email""),
        NumberBox(1, _ => { }, ""Quantity""),
        {|REACTOR_A11Y_003:TextBox(""v"", _ => { }, ""Placeholder only"")|});
}");

    /// <summary>
    /// A caption TextBlock beside a field is not associated with it; only a call that actually
    /// names the field counts.
    /// </summary>
    [Fact]
    public Task Caption_Sibling_In_Plain_Container_Still_Produces_Diagnostic() => VerifyAsync(@"
class C
{
    Element M(string[] items) => HStack(
        TextBlock(""Theme""),
        {|REACTOR_A11Y_003:ComboBox(items)|},
        TextBlock(""Notifications""),
        {|REACTOR_A11Y_003:ToggleSwitch()|});
}");

    [Fact]
    public Task FormField_With_Label_Satisfies_Rule_But_Not_Without() => VerifyAsync(@"
class C
{
    Element M(string[] items) => VStack(
        Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl.FormField(TextBox(""v"").Width(200), label: ""Email"", required: true),
        Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl.FormField(ComboBox(items), ""Role""),
        Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl.FormField({|REACTOR_A11Y_003:ToggleSwitch()|}),
        Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl.FormField({|REACTOR_A11Y_003:ComboBox(items)|}, label: null));
}");

    [Fact]
    public Task Toolkit_SettingsCard_Header_Names_Its_Content() => VerifyAsync(@"
class C
{
    Element M() => VStack(
        SettingsCardElement.SettingsCard(header: ""Wi-Fi"", content: ToggleSwitch()),
        SettingsCardElement.SettingsCard(description: ""No header"", content: {|REACTOR_A11Y_003:ToggleSwitch()|}));
}");

    /// <summary>
    /// A source helper that applies <c>.AutomationName</c> to the parameter the field is passed
    /// into — directly, or via another helper on the same type, as the gallery's
    /// <c>OptionPanel</c> does — labels it. A helper that only lays the field out does not.
    /// </summary>
    [Fact]
    public Task Label_Forwarding_Helpers_Satisfy_Rule_But_Layout_Helpers_Do_Not() => VerifyAsync(@"
class C
{
    static Element SettingsRow(string label, Element control) =>
        HStack(TextBlock(label), control.AutomationName(label));

    static Element OptionPanel(params Element[] options) => VStack(NameFromCaptions(options).ToArray());

    static System.Collections.Generic.IEnumerable<Element> NameFromCaptions(Element[] items)
    {
        for (var i = 0; i < items.Length; i++)
            yield return i > 0 ? items[i].AutomationName(""caption"") : items[i];
    }

    static Element Row(string label, Element control) => HStack(TextBlock(label), control);

    Element M(string[] items) => VStack(
        SettingsRow(""Theme"", ComboBox(items).Width(120)),
        OptionPanel(TextBlock(""Direction""), ComboBox(items), TextBlock(""Wrap""), ToggleSwitch()),
        Row(""Theme"", {|REACTOR_A11Y_003:ComboBox(items)|}));
}");
}
