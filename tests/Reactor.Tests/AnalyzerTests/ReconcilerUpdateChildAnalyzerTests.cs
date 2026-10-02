using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="ReconcilerUpdateChildAnalyzer"/> (<c>REACTOR_LIFECYCLE_003</c>) and its
/// <see cref="ReconcilerUpdateChildCodeFix"/>. A stub <c>Reconciler</c> is compiled into its own
/// assembly, the way consumer code sees the real one: the analyzer stays quiet inside the assembly
/// that declares <c>Reconciler</c>, and one test proves that by compiling the stub alongside the code.
/// </summary>
public class ReconcilerUpdateChildAnalyzerTests
{
    private const string ReactorStub = @"
namespace Microsoft.UI.Xaml
{
    public class UIElement { }
}

namespace Microsoft.UI.Reactor.Core
{
    using Microsoft.UI.Xaml;

    public abstract class Element { }

    public sealed class Reconciler
    {
        public UIElement UpdateChild(Element oldEl, Element newEl, UIElement control, System.Action requestRerender) => null;
        public UIElement Reconcile(Element oldElement, Element newElement, UIElement existingControl, System.Action requestRerender) => null;
        public void UnmountChild(UIElement control) { }
    }

    public sealed class UpdateContext
    {
        public Reconciler Reconciler { get; } = new Reconciler();
        public System.Action RequestRerender { get; } = () => { };
    }

    // Decoy: the namespace and method name of Reconciler.UpdateChild on another type.
    public sealed class ElementTree
    {
        public UIElement UpdateChild(Element oldEl, Element newEl, UIElement control, System.Action requestRerender) => control;
    }
}

namespace ThirdParty
{
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Xaml;

    // Decoy: Reconciler's type and method names in another namespace.
    public sealed class Reconciler
    {
        public UIElement UpdateChild(Element oldEl, Element newEl, UIElement control, System.Action requestRerender) => control;
        public void UnmountChild(UIElement control) { }
    }
}";

    private const string Usings = @"
using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

class Slot { public UIElement Content; }
";

    private static readonly Lazy<Task<MetadataReference>> s_stub = new(CompileStubAsync);

    private static Task<MetadataReference> StubAsync() => s_stub.Value;

    private static async Task<MetadataReference> CompileStubAsync()
    {
        var references = await ReferenceAssemblies.Default.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var compilation = CSharpCompilation.Create(
            "ReactorStub",
            new[] { CSharpSyntaxTree.ParseText(ReactorStub) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        return MetadataReference.CreateFromImage(pe.ToArray());
    }

    private static async Task VerifyAsync(string code)
    {
        var test = new CSharpAnalyzerTest<ReconcilerUpdateChildAnalyzer, DefaultVerifier> { TestCode = Usings + code };
        test.TestState.AdditionalReferences.Add(await StubAsync());
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    private static async Task VerifyFixAsync(string code, string fixedCode)
    {
        var test = new CSharpCodeFixTest<ReconcilerUpdateChildAnalyzer, ReconcilerUpdateChildCodeFix, DefaultVerifier>
        {
            TestCode = Usings + code,
            FixedCode = Usings + fixedCode,
        };
        test.TestState.AdditionalReferences.Add(await StubAsync());
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Diagnostic ──────────────────────────────────────────────────────

    [Fact]
    public async Task Fires_On_UpdateChild()
    {
        await VerifyAsync(@"
class Host
{
    UIElement Update(Reconciler r, Element oldEl, Element newEl, UIElement existing, Action rerender)
        => r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
}");
    }

    [Fact]
    public async Task Fires_On_UpdateChild_Through_A_Handler_Context()
    {
        await VerifyAsync(@"
class Handler
{
    void Update(UpdateContext ctx, Element oldEl, Element newEl, Slot slot)
    {
        var replacement = ctx.Reconciler.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, slot.Content, ctx.RequestRerender);
        if (replacement is not null) slot.Content = replacement;
    }
}");
    }

    [Fact]
    public async Task Fires_On_A_Conditional_Access()
    {
        await VerifyAsync(@"
class Host
{
    UIElement Update(Reconciler r, Element oldEl, Element newEl, UIElement existing, Action rerender)
        => r?.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
}");
    }

    [Fact]
    public async Task No_Diagnostic_For_Reconcile()
    {
        await VerifyAsync(@"
class Host
{
    UIElement Update(Reconciler r, Element oldEl, Element newEl, UIElement existing, Action rerender)
        => r.Reconcile(oldEl, newEl, existing, rerender);
}");
    }

    // The decoys live in the referenced stub assembly: one declared in the code under test would be
    // skipped as the declaring assembly before the type check ran.

    [Fact]
    public async Task No_Diagnostic_For_UpdateChild_On_Another_Type()
    {
        await VerifyAsync(@"
class Host
{
    UIElement Update(ElementTree t, Element oldEl, Element newEl, UIElement existing, Action rerender)
        => t.UpdateChild(oldEl, newEl, existing, rerender);
}");
    }

    [Fact]
    public async Task No_Diagnostic_For_A_Reconciler_In_Another_Namespace()
    {
        await VerifyAsync(@"
class Host
{
    UIElement Update(ThirdParty.Reconciler r, Element oldEl, Element newEl, UIElement existing, Action rerender)
        => r.UpdateChild(oldEl, newEl, existing, rerender);
}");
    }

    [Fact]
    public async Task No_Diagnostic_In_The_Assembly_That_Declares_Reconciler()
    {
        // The framework's own slot owners check CanUpdate before calling UpdateChild.
        var test = new CSharpAnalyzerTest<ReconcilerUpdateChildAnalyzer, DefaultVerifier>
        {
            TestCode = ReactorStub + @"

namespace Microsoft.UI.Reactor.Core
{
    using Microsoft.UI.Xaml;

    class SlotOwner
    {
        UIElement Update(Reconciler r, Element oldEl, Element newEl, UIElement existing)
            => r.UpdateChild(oldEl, newEl, existing, () => { });
    }
}",
        };
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code fix ────────────────────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Rewrites_The_Common_Shape()
    {
        await VerifyFixAsync(@"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, Action rerender)
    {
        var existing = slot.Content;
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
            slot.Content = replacement;
    }
}", @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, Action rerender)
    {
        var existing = slot.Content;
        var replacement = r.Reconcile(oldEl, newEl, existing, rerender);
        if (replacement is not null && !ReferenceEquals(replacement, existing))
            slot.Content = replacement;
    }
}");
    }

    [Fact]
    public async Task CodeFix_Keeps_A_NotEquals_Check_And_Removes_The_Manual_Unmount()
    {
        // Reconcile unmounts the control it replaces, so the body's own UnmountChild goes.
        await VerifyFixAsync(@"
class Handler
{
    void Update(UpdateContext ctx, Element oldEl, Element newEl, Slot slot, UIElement existing)
    {
        var replacement = ctx.Reconciler.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, ctx.RequestRerender);
        if (null != replacement)
        {
            ctx.Reconciler.UnmountChild(existing);
            slot.Content = replacement;
        }
    }
}", @"
class Handler
{
    void Update(UpdateContext ctx, Element oldEl, Element newEl, Slot slot, UIElement existing)
    {
        var replacement = ctx.Reconciler.Reconcile(oldEl, newEl, existing, ctx.RequestRerender);
        if (null != replacement && !ReferenceEquals(replacement, existing))
        {
            slot.Content = replacement;
        }
    }
}");
    }

    [Fact]
    public async Task CodeFix_Keeps_Unmounts_Of_Other_Controls()
    {
        // Only Reconciler.UnmountChild of the existing control duplicates what Reconcile does.
        await VerifyFixAsync(@"
class Host
{
    void Update(Reconciler r, ThirdParty.Reconciler other, Element oldEl, Element newEl, Slot slot,
        UIElement existing, UIElement sibling, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
        {
            r.UnmountChild(sibling);
            other.UnmountChild(existing);
            slot.Content = replacement;
        }
    }
}", @"
class Host
{
    void Update(Reconciler r, ThirdParty.Reconciler other, Element oldEl, Element newEl, Slot slot,
        UIElement existing, UIElement sibling, Action rerender)
    {
        var replacement = r.Reconcile(oldEl, newEl, existing, rerender);
        if (replacement is not null && !ReferenceEquals(replacement, existing))
        {
            r.UnmountChild(sibling);
            other.UnmountChild(existing);
            slot.Content = replacement;
        }
    }
}");
    }

    [Fact]
    public async Task CodeFix_Qualifies_ReferenceEquals_When_A_Member_Hides_It()
    {
        await VerifyFixAsync(@"
class Host
{
    static new bool ReferenceEquals(object a, object b) => false;

    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement != null)
            slot.Content = replacement;
    }
}", @"
class Host
{
    static new bool ReferenceEquals(object a, object b) => false;

    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender)
    {
        var replacement = r.Reconcile(oldEl, newEl, existing, rerender);
        if (replacement != null && !object.ReferenceEquals(replacement, existing))
            slot.Content = replacement;
    }
}");
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Result_Is_Read_After_The_If()
    {
        // Reconcile returns the existing control where UpdateChild returned null, so a later read
        // would change meaning.
        const string code = @"
class Host
{
    UIElement Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
            slot.Content = replacement;
        return replacement;
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_For_Named_Arguments()
    {
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl: oldEl, newEl: newEl, control: existing, requestRerender: rerender);
        if (replacement is not null)
            slot.Content = replacement;
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Existing_Control_Is_Not_A_Local()
    {
        // The rewritten condition compares the result with the existing control after the call,
        // and slot.Content would be read again.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, slot.Content, rerender);
        if (replacement is not null)
            slot.Content = replacement;
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Existing_Control_Is_A_Field()
    {
        const string code = @"
class Host
{
    UIElement _existing;

    void Update(Reconciler r, Element oldEl, Element newEl, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, _existing, rerender);
        if (replacement is not null)
            _existing = replacement;
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_Without_A_Following_Null_Check()
    {
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (slot.Content is not null)
            slot.Content = replacement;
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Manual_Unmount_Is_Nested()
    {
        // Only an UnmountChild that is a statement of its own in the body can be removed safely.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender, bool flag)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
        {
            if (flag) r.UnmountChild(existing);
            slot.Content = replacement;
        }
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Body_Only_Unmounts()
    {
        // Removing the unmount would leave an empty body around a result nothing installs.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
        {
            r.UnmountChild(existing);
        }
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Manual_Unmount_Carries_A_Comment()
    {
        // Removing the statement would take the comment with it.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
        {
            r.UnmountChild(existing); // UpdateChild leaves it mounted
            slot.Content = replacement;
        }
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_A_Directive_Precedes_The_Manual_Unmount()
    {
        // The #endif is leading trivia of the unmount statement: removing it with the statement
        // would leave the #if unbalanced.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
        {
#if true
            Console.WriteLine(""replaced"");
#endif
            r.UnmountChild(existing);
            slot.Content = replacement;
        }
    }
}";
        await VerifyFixAsync(code, code);
    }
}
