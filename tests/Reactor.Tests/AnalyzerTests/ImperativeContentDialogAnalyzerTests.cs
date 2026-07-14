using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="ImperativeContentDialogAnalyzer"/> (<c>REACTOR_DIALOG_001</c>). Stubs a
/// minimal WinUI <c>Microsoft.UI.Xaml.Controls.ContentDialog</c> (with <c>ShowAsync</c>), the
/// Reactor <c>ContentDialog(...)</c> DSL factory + controlled <c>ContentDialogElement.IsOpen</c>,
/// and an unrelated <c>ShowAsync</c>-bearing type — so the positive / negative / near-miss cases
/// exercise the analyzer's syntactic gate and its semantic type confirmation without pulling the
/// framework in.
/// </summary>
/// <remarks>
/// The shared <see cref="Stubs"/> prefix carries every <c>using</c> at the top (before any
/// namespace) and each test class is appended after it, so tests never emit a <c>using</c> after a
/// namespace (CS1529). Positive tests fully-qualify the WinUI <c>ContentDialog</c> type to keep it
/// unambiguous against the <c>using static Factories</c> <c>ContentDialog(...)</c> method.
/// </remarks>
public class ImperativeContentDialogAnalyzerTests
{
    private const string Stubs = @"
using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace System.Runtime.CompilerServices
{
    // Required for `record` types + `init` accessors under older runtime metadata — supply a
    // stub so the test source compiles self-contained across reference-assembly sets.
    public static class IsExternalInit { }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum ContentDialogResult { None, Primary, Secondary }
    public enum ContentDialogPlacement { Popup, InPlace }

    // Minimal shape of the WinUI ContentDialog: the analyzer resolves .ShowAsync() by name and
    // confirms its ContainingType is Microsoft.UI.Xaml.Controls.ContentDialog.
    public class ContentDialog
    {
        public string Title { get; set; }
        public string PrimaryButtonText { get; set; }
        public Task<ContentDialogResult> ShowAsync() => Task.FromResult(ContentDialogResult.None);
        public Task<ContentDialogResult> ShowAsync(ContentDialogPlacement placement) => Task.FromResult(ContentDialogResult.None);
    }

    // A subclass of the WinUI ContentDialog: ShowAsync is inherited, so the resolved method's
    // ContainingType is still ContentDialog — a subclass shown imperatively must still fire.
    public class ConfirmDialog : ContentDialog { }

    // Same namespace as ContentDialog, different type name, own ShowAsync — locks the type-NAME
    // half of the predicate (namespace matches, name must not).
    public class PopupDialog
    {
        public Task<ContentDialogResult> ShowAsync() => Task.FromResult(ContentDialogResult.None);
    }

    // A subclass that DECLARES its own ShowAsync (hides the base). The resolved method's
    // ContainingType is CustomDialog, not ContentDialog, so only a base-type-chain walk catches it.
    public class CustomDialog : ContentDialog
    {
        public new Task<ContentDialogResult> ShowAsync() => Task.FromResult(ContentDialogResult.None);
    }
}

namespace Microsoft.UI.Reactor.Core
{
    public record Element;
    public record TextBlockElement(string Text) : Element;

    // Reactor's controlled dialog element — the CORRECT path. `with { IsOpen = ... }` drives it.
    public record ContentDialogElement(string Title, Element Content, string PrimaryButtonText = ""OK"") : Element
    {
        public bool IsOpen { get; init; }
        public Action<Microsoft.UI.Xaml.Controls.ContentDialogResult> OnClosed { get; init; }
    }
}

namespace Microsoft.UI.Reactor
{
    // The Reactor DSL factory: an IdentifierNameSyntax invocation `ContentDialog(...)` that
    // never calls .ShowAsync(), so it is the controlled path and must NEVER fire.
    public static class Factories
    {
        public static ContentDialogElement ContentDialog(string title, Element content, string primaryButtonText = ""OK"")
            => new(title, content, primaryButtonText);

        public static TextBlockElement TextBlock(string text) => new(text);
    }
}

namespace Unrelated
{
    // A different type that happens to expose a ShowAsync — trips the syntactic name gate but
    // must be rejected by the semantic ContainingType check.
    public class MessageDialog
    {
        public Task ShowAsync() => Task.CompletedTask;
    }
}

namespace OtherUi
{
    // Same simple type name as WinUI ContentDialog, different namespace, own ShowAsync — locks
    // the NAMESPACE half of the predicate (name matches, namespace must not).
    public class ContentDialog
    {
        public Task ShowAsync() => Task.CompletedTask;
    }
}
";

    // ── Positive: imperative WinUI ContentDialog.ShowAsync ──────────────────

    [Fact]
    public async Task Fires_For_Inline_New_ShowAsync()
    {
        // The canonical anti-pattern from docs/guide/dialogs-and-flyouts.md.
        var source = Stubs + @"
class C
{
    async Task M()
    {
        await {|REACTOR_DIALOG_001:new Microsoft.UI.Xaml.Controls.ContentDialog { Title = ""Confirm"" }.ShowAsync()|};
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_TwoStatement_Local_ShowAsync()
    {
        var source = Stubs + @"
class C
{
    async Task M()
    {
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog { Title = ""Confirm"" };
        var result = await {|REACTOR_DIALOG_001:dialog.ShowAsync()|};
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_In_Async_Event_Handler_Lambda()
    {
        // Mirrors the doc's `Button(""Save"", async () => await dialog.ShowAsync())` shape.
        var source = Stubs + @"
class C
{
    void M()
    {
        Func<Task> handler = async () =>
        {
            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog { Title = ""Confirm"" };
            await {|REACTOR_DIALOG_001:dialog.ShowAsync()|};
        };
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_FireAndForget_Placement_Overload()
    {
        // The ShowAsync(ContentDialogPlacement) overload, discarded (not awaited).
        var source = Stubs + @"
class C
{
    void M()
    {
        {|REACTOR_DIALOG_001:new Microsoft.UI.Xaml.Controls.ContentDialog { Title = ""x"" }.ShowAsync(Microsoft.UI.Xaml.Controls.ContentDialogPlacement.Popup)|};
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Conditional_Access()
    {
        // dialog?.ShowAsync() — the invocation node is the `.ShowAsync()` member-binding call.
        var source = Stubs + @"
class C
{
    void M(Microsoft.UI.Xaml.Controls.ContentDialog dialog)
    {
        dialog?{|REACTOR_DIALOG_001:.ShowAsync()|};
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Subclass_ShowAsync()
    {
        // A ContentDialog subclass shown imperatively — ShowAsync is inherited, so the resolved
        // method's ContainingType is still Microsoft.UI.Xaml.Controls.ContentDialog.
        var source = Stubs + @"
class C
{
    async Task M()
    {
        await {|REACTOR_DIALOG_001:new Microsoft.UI.Xaml.Controls.ConfirmDialog().ShowAsync()|};
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Escaped_Identifier_ShowAsync()
    {
        // Escaped identifier: ValueText (not Text) decodes `@ShowAsync` to `ShowAsync`, so the
        // gate still matches and the diagnostic fires.
        var source = Stubs + @"
class C
{
    async Task M()
    {
        await {|REACTOR_DIALOG_001:new Microsoft.UI.Xaml.Controls.ContentDialog { Title = ""x"" }.@ShowAsync()|};
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Subclass_Declaring_Own_ShowAsync()
    {
        // The subclass hides ShowAsync, so the resolved method's ContainingType is the subclass;
        // the base-type-chain walk still recognizes it as a WinUI ContentDialog.
        var source = Stubs + @"
class C
{
    async Task M()
    {
        await {|REACTOR_DIALOG_001:new Microsoft.UI.Xaml.Controls.CustomDialog().ShowAsync()|};
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Implicit_Receiver_In_Subclass()
    {
        // A bare `ShowAsync()` with an implicit receiver inside a ContentDialog subclass — the
        // IdentifierNameSyntax gate arm + the base-type walk catch it.
        var source = Stubs + @"
class MyDlg : Microsoft.UI.Xaml.Controls.ContentDialog
{
    async Task Open()
    {
        await {|REACTOR_DIALOG_001:ShowAsync()|};
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative: the controlled Reactor path ───────────────────────────────

    [Fact]
    public async Task No_Diagnostic_For_Controlled_Factory_With_IsOpen()
    {
        // The canonical CORRECT pattern: declarative ContentDialog(...) driven by IsOpen.
        var source = Stubs + @"
class C
{
    Element Render(bool open)
        => ContentDialog(""Welcome"", TextBlock(""Thanks"")) with { IsOpen = open };
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Plain_Factory_Call()
    {
        var source = Stubs + @"
class C
{
    Element Render() => ContentDialog(""Welcome"", TextBlock(""Thanks""));
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Near-miss: unrelated ShowAsync trips the syntactic gate only ─────────

    [Fact]
    public async Task No_Diagnostic_For_Unrelated_ShowAsync()
    {
        // Same method name, different type — the semantic ContainingType check rejects it.
        var source = Stubs + @"
class C
{
    async Task M()
    {
        var dlg = new Unrelated.MessageDialog();
        await dlg.ShowAsync();
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_NonWinUi_SameName_ContentDialog()
    {
        // Same simple type name (ContentDialog) but a different namespace — the namespace half of
        // the predicate rejects it, so a look-alike type is not flagged.
        var source = Stubs + @"
class C
{
    async Task M()
    {
        var d = new OtherUi.ContentDialog();
        await d.ShowAsync();
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_SameNamespace_NonContentDialog()
    {
        // Same namespace (Microsoft.UI.Xaml.Controls) but a different type name — the type-name
        // half of the predicate rejects it, so a sibling WinUI type with ShowAsync is not flagged.
        var source = Stubs + @"
class C
{
    async Task M()
    {
        var p = new Microsoft.UI.Xaml.Controls.PopupDialog();
        await p.ShowAsync();
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Unresolved_ShowAsync()
    {
        // Incomplete / mid-edit code: the receiver can't be resolved, so GetSymbolInfo yields no
        // IMethodSymbol and the analyzer stays silent rather than firing a false positive.
        // CompilerDiagnostics.None because the source intentionally has an unresolved identifier.
        var source = Stubs + @"
class C
{
    void M()
    {
        undefinedDialog.ShowAsync();
    }
}";

        await new CSharpAnalyzerTest<ImperativeContentDialogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            CompilerDiagnostics = CompilerDiagnostics.None,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
