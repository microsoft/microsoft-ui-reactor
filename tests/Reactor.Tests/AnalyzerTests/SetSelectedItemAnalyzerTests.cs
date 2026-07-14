using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="SetSelectedItemAnalyzer"/> (<c>REACTOR_CTRL_001</c>) and its
/// <see cref="SetSelectedItemCodeFix"/>. Stubs Reactor selector elements (with the
/// controlled <c>SelectedIndex</c> element property + a factory) and their native controls
/// (carrying <c>SelectedItem</c>/<c>SelectedValue</c>), exercising the "also sets
/// SelectedIndex" spine walk across factory-argument and object-initializer forms.
/// </summary>
public class SetSelectedItemAnalyzerTests
{
    private const string Stubs = @"
using System;
using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Xaml.Controls
{
    public class ComboBox { public object SelectedItem { get; set; } public object SelectedValue { get; set; } public string Header { get; set; } }
    public class ListView { public object SelectedItem { get; set; } public object SelectedValue { get; set; } }
    public class RadioButtons { public object SelectedItem { get; set; } }
    public class GridView { public object SelectedItem { get; set; } }
}

namespace Microsoft.UI.Reactor
{
    using System;
    using Microsoft.UI.Xaml.Controls;

    public readonly struct Optional<T>
    {
        public static Optional<T> Unset => default;
        public static implicit operator Optional<T>(T value) => default;
    }

    public record ComboBoxElement { public Optional<int> SelectedIndex { get; init; } }
    public record ListViewElement { public Optional<int> SelectedIndex { get; init; } }
    public record RadioButtonsElement { public Optional<int> SelectedIndex { get; init; } }
    public record GridViewElement { public Optional<int> SelectedIndex { get; init; } }

    public static class Factories
    {
        public static ComboBoxElement ComboBox(string[] items, Optional<int> selectedIndex) => new ComboBoxElement { SelectedIndex = selectedIndex };
        public static ComboBoxElement ComboBox(string[] items) => new ComboBoxElement();
    }

    public static class Ext
    {
        public static ComboBoxElement Set(this ComboBoxElement el, Action<ComboBox> configure) => el;
        public static ListViewElement Set(this ListViewElement el, Action<ListView> configure) => el;
        public static RadioButtonsElement Set(this RadioButtonsElement el, Action<RadioButtons> configure) => el;
        public static GridViewElement Set(this GridViewElement el, Action<GridView> configure) => el;
        public static ComboBoxElement Header(this ComboBoxElement el, string header) => el;
    }
}
";

    [Fact]
    public async Task Fires_For_ComboBox_SelectedItem_With_Factory_Index()
    {
        var source = Stubs + @"
class C
{
    ComboBoxElement M(object x) =>
        {|REACTOR_CTRL_001:ComboBox(new[]{""a""}, 1).Set(cb => cb.SelectedItem = x)|};
}";
        await new CSharpAnalyzerTest<SetSelectedItemAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_ListView_SelectedValue_With_Initializer_Index()
    {
        var source = Stubs + @"
class C
{
    ListViewElement M(object x) =>
        {|REACTOR_CTRL_001:new ListViewElement { SelectedIndex = 2 }.Set(lv => lv.SelectedValue = x)|};
}";
        await new CSharpAnalyzerTest<SetSelectedItemAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_No_Competing_SelectedIndex()
    {
        // SelectedItem set, but the element never sets a competing SelectedIndex.
        var source = Stubs + @"
class C
{
    ComboBoxElement M(object x) =>
        ComboBox(new[]{""a""}).Set(cb => cb.SelectedItem = x);
}";
        await new CSharpAnalyzerTest<SetSelectedItemAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_SelectedIndex_Is_Default()
    {
        // Near-miss: SelectedIndex present but explicitly left unset ('default') is not a
        // competing authority.
        var source = Stubs + @"
class C
{
    ComboBoxElement M(object x) =>
        new ComboBoxElement { SelectedIndex = default }.Set(cb => cb.SelectedItem = x);
}";
        await new CSharpAnalyzerTest<SetSelectedItemAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Different_Member()
    {
        // Near-miss: a competing SelectedIndex is present, but the .Set assigns Header,
        // not SelectedItem/SelectedValue.
        var source = Stubs + @"
class C
{
    ComboBoxElement M(string h) =>
        ComboBox(new[]{""a""}, 1).Set(cb => cb.Header = h);
}";
        await new CSharpAnalyzerTest<SetSelectedItemAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_With_Expression_SelectedIndex()
    {
        var source = Stubs + @"
class C
{
    ComboBoxElement M(ComboBoxElement combo, object x) =>
        {|REACTOR_CTRL_001:(combo with { SelectedIndex = 1 }).Set(cb => cb.SelectedItem = x)|};
}";
        await new CSharpAnalyzerTest<SetSelectedItemAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Named_SelectedIndex_Argument()
    {
        var source = Stubs + @"
class C
{
    ComboBoxElement M(object x) =>
        {|REACTOR_CTRL_001:ComboBox(new[]{""a""}, selectedIndex: 1).Set(cb => cb.SelectedItem = x)|};
}";
        await new CSharpAnalyzerTest<SetSelectedItemAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_SelectedIndex_Is_Optional_Unset()
    {
        // Near-miss: SelectedIndex present but explicitly Optional<int>.Unset — not a
        // competing authority.
        var source = Stubs + @"
class C
{
    ComboBoxElement M(object x) =>
        new ComboBoxElement { SelectedIndex = Optional<int>.Unset }.Set(cb => cb.SelectedItem = x);
}";
        await new CSharpAnalyzerTest<SetSelectedItemAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_NonReactor_Helper_With_SelectedIndex_Param()
    {
        // A user helper (not a Reactor factory) that merely has a 'selectedIndex' parameter
        // must NOT be read as wiring the element's controlled SelectedIndex — otherwise the
        // destructive delete-fix would corrupt valid code.
        var source = Stubs + @"
class C
{
    static ComboBoxElement MakeCombo(int selectedIndex) => new ComboBoxElement();
    ComboBoxElement M(object x) =>
        MakeCombo(selectedIndex: 3).Set(cb => cb.SelectedItem = x);
}";
        await new CSharpAnalyzerTest<SetSelectedItemAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Deletes_The_Set_Call()
    {
        var before = Stubs + @"
class C
{
    ComboBoxElement M(object x) =>
        {|REACTOR_CTRL_001:ComboBox(new[]{""a""}, 1).Set(cb => cb.SelectedItem = x)|};
}";
        var after = Stubs + @"
class C
{
    ComboBoxElement M(object x) =>
        ComboBox(new[]{""a""}, 1);
}";
        await new CSharpCodeFixTest<SetSelectedItemAnalyzer, SetSelectedItemCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Deletes_Set_Call_Mid_Chain()
    {
        // The .Set is not the outermost call — deleting it must keep the trailing modifier.
        var before = Stubs + @"
class C
{
    ComboBoxElement M(object x) =>
        {|REACTOR_CTRL_001:ComboBox(new[]{""a""}, 1).Set(cb => cb.SelectedItem = x)|}.Header(""h"");
}";
        var after = Stubs + @"
class C
{
    ComboBoxElement M(object x) =>
        ComboBox(new[]{""a""}, 1).Header(""h"");
}";
        await new CSharpCodeFixTest<SetSelectedItemAnalyzer, SetSelectedItemCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
