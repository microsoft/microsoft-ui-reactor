using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="SetOwnedItemsSourceAnalyzer"/> (<c>REACTOR_ITEMS_001</c>). Stubs a
/// minimal Reactor-shaped collection element + its native control so the analyzer's
/// syntactic <c>.Set(x =&gt; x.ItemsSource = ...)</c> match and its curated element-type
/// gate fire without pulling the framework in.
/// </summary>
public class SetOwnedItemsSourceAnalyzerTests
{
    // Top-level usings must precede the namespace declarations, so they live at the head
    // of the stub block; appended test snippets never add their own file-scoped usings.
    private const string Stubs = @"
using System;
using Microsoft.UI.Reactor;

namespace Microsoft.UI.Xaml.Controls
{
    public class ItemsControl { public object ItemsSource { get; set; } public object Header { get; set; } }
    public class ListView : ItemsControl { }
    public class GridView : ItemsControl { }
    public class TreeView : ItemsControl { }
    public class AutoSuggestBox : ItemsControl { }
}

namespace Microsoft.UI.Reactor
{
    using Microsoft.UI.Xaml.Controls;

    public record ListViewElement;
    public record GridViewElement;
    public record TreeViewElement;
    public record AutoSuggestBoxElement;

    public static class Ext
    {
        public static ListViewElement Set(this ListViewElement el, Action<ListView> configure) => el;
        public static GridViewElement Set(this GridViewElement el, Action<GridView> configure) => el;
        public static TreeViewElement Set(this TreeViewElement el, Action<TreeView> configure) => el;
        public static AutoSuggestBoxElement Set(this AutoSuggestBoxElement el, Action<AutoSuggestBox> configure) => el;
        public static ListViewElement Apply(this ListViewElement el, Action<ListView> configure) => el;
    }
}
";

    [Fact]
    public async Task Fires_For_ListView_ItemsSource()
    {
        var source = Stubs + @"
class C
{
    void M(ListViewElement lv, object data)
    {
        {|REACTOR_ITEMS_001:lv.Set(x => x.ItemsSource = data)|};
    }
}";
        await new CSharpAnalyzerTest<SetOwnedItemsSourceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_GridView_ItemsSource_Block_Body()
    {
        var source = Stubs + @"
class C
{
    void M(GridViewElement gv, object data)
    {
        {|REACTOR_ITEMS_001:gv.Set(x => { x.ItemsSource = data; })|};
    }
}";
        await new CSharpAnalyzerTest<SetOwnedItemsSourceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_AutoSuggestBox_Escape_Hatch()
    {
        // AutoSuggestBox is the documented ItemsSource escape hatch — excluded.
        var source = Stubs + @"
class C
{
    void M(AutoSuggestBoxElement asb, object data)
    {
        asb.Set(x => x.ItemsSource = data);
    }
}";
        await new CSharpAnalyzerTest<SetOwnedItemsSourceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Different_Member_On_Curated_Element()
    {
        // Near-miss: a .Set that assigns a different member on a curated element.
        var source = Stubs + @"
class C
{
    void M(ListViewElement lv, object data)
    {
        lv.Set(x => x.Header = data);
    }
}";
        await new CSharpAnalyzerTest<SetOwnedItemsSourceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Non_Set_Method()
    {
        // 'Apply' is not '.Set' — the syntactic fast path must reject it.
        var source = Stubs + @"
class C
{
    void M(ListViewElement lv, object data)
    {
        lv.Apply(x => x.ItemsSource = data);
    }
}";
        await new CSharpAnalyzerTest<SetOwnedItemsSourceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
