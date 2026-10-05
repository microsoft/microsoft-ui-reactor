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
    // Reconciler's members mirror Reactor's nullable signatures, and the handler context mirrors
    // Reactor's: a readonly ref struct in V1Protocol that holds one Reconciler.
    private const string ReactorStub = @"#nullable enable
namespace Microsoft.UI.Xaml
{
    public class UIElement
    {
        public object? Tag { get; set; }
    }
}

namespace Microsoft.UI.Reactor.Core
{
    using Microsoft.UI.Xaml;

    public abstract class Element { }

    public sealed class Reconciler
    {
        public UIElement? UpdateChild(Element oldEl, Element newEl, UIElement control, System.Action requestRerender) => null;
        public UIElement? Reconcile(Element? oldElement, Element? newElement, UIElement? existingControl, System.Action requestRerender) => existingControl;
        public void UnmountChild(UIElement control) { }
    }

    // Decoy: the namespace and method name of Reconciler.UpdateChild on another type.
    public sealed class ElementTree
    {
        public UIElement UpdateChild(Element oldEl, Element newEl, UIElement control, System.Action requestRerender) => control;
    }
}

namespace Microsoft.UI.Reactor.Core.V1Protocol
{
    using Microsoft.UI.Reactor.Core;

    public readonly ref struct UpdateContext
    {
        private readonly Reconciler _reconciler;

        public UpdateContext(Reconciler reconciler) { _reconciler = reconciler; }

        public Reconciler Reconciler => _reconciler;
        public System.Action RequestRerender => () => { };
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
}
#nullable restore
";

    private const string Usings = @"
using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
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
        // Reconcile returns null when the child is gone, and the identity check then assigns it,
        // which empties the slot.
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
        if (!ReferenceEquals(replacement, existing))
            slot.Content = replacement;
    }
}");
    }

    [Fact]
    public async Task CodeFix_Removes_A_Manual_Unmount_Through_The_Handler_Context()
    {
        // Reconcile unmounts the control it replaces through the same reconciler, so the body's
        // own UnmountChild goes.
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
        if (!ReferenceEquals(replacement, existing))
        {
            slot.Content = replacement;
        }
    }
}");
    }

    [Fact]
    public async Task CodeFix_Removes_A_Manual_Unmount_Before_The_Existing_Control_Is_Replaced()
    {
        // The result goes into the existing control's own variable, after the unmount.
        await VerifyFixAsync(@"
class Host
{
    UIElement Update(Reconciler r, Element oldEl, Element newEl, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement != null)
        {
            r.UnmountChild(existing);
            existing = replacement;
        }
        return existing;
    }
}", @"
class Host
{
    UIElement Update(Reconciler r, Element oldEl, Element newEl, UIElement existing, Action rerender)
    {
        var replacement = r.Reconcile(oldEl, newEl, existing, rerender);
        if (!ReferenceEquals(replacement, existing))
        {
            existing = replacement;
        }
        return existing;
    }
}");
    }

    [Fact]
    public async Task CodeFix_Rewrites_A_Nullable_Slot()
    {
        await VerifyFixAsync(@"
#nullable enable
class NullableSlot { public UIElement? Content; }

class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, NullableSlot slot, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
            slot.Content = replacement;
    }
}", @"
#nullable enable
class NullableSlot { public UIElement? Content; }

class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, NullableSlot slot, UIElement existing, Action rerender)
    {
        var replacement = r.Reconcile(oldEl, newEl, existing, rerender);
        if (!ReferenceEquals(replacement, existing))
            slot.Content = replacement;
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
        if (!object.ReferenceEquals(replacement, existing))
            slot.Content = replacement;
    }
}");
    }

    [Fact]
    public async Task CodeFix_Not_Offered_For_A_Conditional_Access()
    {
        // Without the null check, a null receiver would empty the slot, which UpdateChild never did.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender)
    {
        var replacement = r?.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
            slot.Content = replacement;
    }
}";
        await VerifyFixAsync(code, code);
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
    public async Task CodeFix_Not_Offered_When_The_Call_Writes_The_Existing_Control()
    {
        // The rewritten condition reads the existing control after the call.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, () => existing = null);
        if (replacement is not null)
            slot.Content = replacement;
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
    public async Task CodeFix_Not_Offered_With_An_Else()
    {
        // The else ran whenever UpdateChild returned null, which includes a child that became
        // empty; that case now takes the if branch.
        const string code = @"
class Host
{
    int _patched;

    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
            slot.Content = replacement;
        else
            _patched++;
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Body_Does_More_Than_Install()
    {
        // Without the null check, the rest of the body would also run when the child is gone.
        const string code = @"
class Host
{
    int _swaps;

    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
        {
            slot.Content = replacement;
            _swaps++;
        }
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Body_Assigns_Something_Else()
    {
        // Only installing the result itself empties the slot when the result is null.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, UIElement placeholder, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
            slot.Content = placeholder;
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Install_Calls_A_Method()
    {
        // Passing null to a method doesn't empty a slot.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, System.Collections.Generic.List<UIElement> children, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
            children.Add(replacement);
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Install_Writes_An_Indexer()
    {
        // An indexer usually writes into a collection such as Panel.Children, where null isn't an
        // empty slot.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, System.Collections.Generic.List<UIElement> children, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
            children[0] = replacement;
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Install_Writes_Through_The_Result()
    {
        // With the result null, the assignment would throw.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
            replacement.Tag = replacement;
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_For_A_Non_Nullable_Slot()
    {
        // Assigning a result that can now be null would add a nullable warning.
        const string code = @"
#nullable enable
class StrictSlot { public UIElement Content = null!; }

class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, StrictSlot slot, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
            slot.Content = replacement;
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_An_Unmount_Sits_Outside_The_Body()
    {
        // Only an UnmountChild that is a statement of its own in the body can be removed safely.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
            slot.Content = replacement;
        r.UnmountChild(existing);
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Body_Only_Unmounts()
    {
        // Removing the unmount would leave nothing that installs the result.
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
    public async Task CodeFix_Not_Offered_When_The_Body_Unmounts_Another_Control()
    {
        // Only Reconciler.UnmountChild of the existing control duplicates what Reconcile does.
        const string code = @"
class Host
{
    void Update(Reconciler r, Element oldEl, Element newEl, Slot slot, UIElement existing, UIElement sibling, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
        {
            r.UnmountChild(sibling);
            slot.Content = replacement;
        }
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Unmount_Goes_Through_Another_Reconciler()
    {
        // Each reconciler keeps its own component and handler state, and Reconcile unmounts
        // through r.
        const string code = @"
class Host
{
    void Update(Reconciler r, Reconciler other, Element oldEl, Element newEl, Slot slot, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
        {
            other.UnmountChild(existing);
            slot.Content = replacement;
        }
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Call_Writes_The_Reconciler()
    {
        // The unmount is no longer known to go through the reconciler the call used.
        const string code = @"
class Host
{
    void Update(Reconciler r, Reconciler other, Element oldEl, Element newEl, Slot slot, UIElement existing)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, () => r = other);
        if (replacement is not null)
        {
            r.UnmountChild(existing);
            slot.Content = replacement;
        }
    }
}";
        await VerifyFixAsync(code, code);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_The_Existing_Control_Is_Replaced_Before_The_Unmount()
    {
        // That unmount received the result, not the control it replaced.
        const string code = @"
class Host
{
    UIElement Update(Reconciler r, Element oldEl, Element newEl, UIElement existing, Action rerender)
    {
        var replacement = r.{|REACTOR_LIFECYCLE_003:UpdateChild|}(oldEl, newEl, existing, rerender);
        if (replacement is not null)
        {
            existing = replacement;
            r.UnmountChild(existing);
        }
        return existing;
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
            slot.Content = replacement;
#endif
            r.UnmountChild(existing);
        }
    }
}";
        await VerifyFixAsync(code, code);
    }
}
