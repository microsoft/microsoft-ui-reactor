using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="ControlledInputAnalyzer"/> (<c>REACTOR_HOOKS_011</c>) and its
/// <see cref="ControlledInputCodeFix"/>. Stubs a minimal Reactor-shaped DSL — the
/// <c>Optional&lt;T&gt;</c> value marker, a <c>Factories</c> class with the real
/// (non-uniform) controlled-input factory signatures, and the <c>IsReadOnly</c>
/// fluent modifiers — so the analyzer resolves symbols without pulling in the framework.
/// </summary>
public class ControlledInputAnalyzerTests
{
    private const string Stubs = @"
using System;
using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor
{
    public readonly struct Optional<T>
    {
        public Optional(T value) { }
        public static implicit operator Optional<T>(T value) => new Optional<T>(value);
        public static Optional<T> Unset => default;
    }

    public class TextBoxElement { }
    public class PasswordBoxElement { }
    public class RatingControlElement { }
    public class SliderElement { }
    public class ComboBoxElement { }
    public class CheckBoxElement { }

    // Mirrors the real (non-uniform) DSL shapes in src/Reactor/Elements/Dsl.cs:
    // ComboBox's value is arg #2; Slider's callback is arg #4.
    public static partial class Factories
    {
        public static TextBoxElement TextBox(Optional<string> value = default, Action<string> onChanged = null, string placeholderText = null, string header = null) => new TextBoxElement();
        public static PasswordBoxElement PasswordBox(Optional<string> password = default, Action<string> onPasswordChanged = null, string placeholderText = null) => new PasswordBoxElement();
        public static RatingControlElement RatingControl(Optional<double> value = default, Action<double> onValueChanged = null) => new RatingControlElement();
        public static SliderElement Slider(Optional<double> value = default, double min = 0, double max = 100, Action<double> onValueChanged = null) => new SliderElement();
        public static ComboBoxElement ComboBox(string[] items, Optional<int> selectedIndex = default, Action<int> onSelectedIndexChanged = null) => new ComboBoxElement();
        public static CheckBoxElement CheckBox(Optional<bool?> isChecked = default, Action<bool> onIsCheckedChanged = null, string label = null) => new CheckBoxElement();
    }

    public static class ControlledInputStubExtensions
    {
        public static TextBoxElement IsReadOnly(this TextBoxElement el, bool readOnly = true) => el;
        public static RatingControlElement IsReadOnly(this RatingControlElement el, bool readOnly = true) => el;
        public static TextBoxElement Margin(this TextBoxElement el, double v) => el;
    }
}

// A non-DSL look-alike (same shape, different owning class) — the near-miss.
namespace Look
{
    using Microsoft.UI.Reactor;
    public class FakeBox { }
    public static class Widgets
    {
        public static FakeBox TextBox(Optional<string> value = default, Action<string> onChanged = null) => new FakeBox();
    }
}

public class Model
{
    public string Name = string.Empty;
    public double Score;
    public int Index;
    public string Unset = string.Empty;
}
";

    private static Task Analyze(string body) =>
        new CSharpAnalyzerTest<ControlledInputAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
        }.RunAsync(TestContext.Current.CancellationToken);

    private static Task Fix(string before, string after) =>
        new CSharpCodeFixTest<ControlledInputAnalyzer, ControlledInputCodeFix, DefaultVerifier>
        {
            TestCode = Stubs + before,
            FixedCode = Stubs + after,
        }.RunAsync(TestContext.Current.CancellationToken);

    // ── Positive cases ──────────────────────────────────────────────────

    [Fact]
    public Task Fires_For_TextBox_StateDerived_Value_And_Empty_Callback() => Analyze(@"
class C
{
    void M(string name)
    {
        {|REACTOR_HOOKS_011:TextBox(name, _ => { })|};
    }
}");

    [Fact]
    public Task Fires_For_Qualified_Factories_Call() => Analyze(@"
class C
{
    void M(string name)
    {
        {|REACTOR_HOOKS_011:Factories.TextBox(name, _ => { })|};
    }
}");

    [Fact]
    public Task Fires_For_MemberAccess_Value() => Analyze(@"
class C
{
    void M(Model m)
    {
        {|REACTOR_HOOKS_011:TextBox(m.Name, _ => { })|};
    }
}");

    [Fact]
    public Task Fires_For_State_Member_Named_Unset() => Analyze(@"
class C
{
    // Only Optional<T>.Unset is the sentinel; a state member happening to be named
    // 'Unset' is still a live value.
    void M(Model m)
    {
        {|REACTOR_HOOKS_011:TextBox(m.Unset, _ => { })|};
    }
}");

    [Fact]
    public Task Fires_When_Callback_Ignores_Its_Parameter() => Analyze(@"
class C
{
    void Noop() { }
    void M(string name)
    {
        {|REACTOR_HOOKS_011:TextBox(name, v => Noop())|};
    }
}");

    [Fact]
    public Task Fires_For_Parenthesized_Lambda_Ignoring_Param() => Analyze(@"
class C
{
    void Noop() { }
    void M(string name)
    {
        {|REACTOR_HOOKS_011:TextBox(name, (v) => Noop())|};
    }
}");

    [Fact]
    public Task Fires_For_Parenthesized_Empty_Block_Lambda() => Analyze(@"
class C
{
    void M(string name)
    {
        {|REACTOR_HOOKS_011:TextBox(name, (v) => { })|};
    }
}");

    [Fact]
    public Task Fires_For_ComboBox_Whose_Value_Is_The_Second_Argument() => Analyze(@"
class C
{
    void M(string[] items, int sel)
    {
        {|REACTOR_HOOKS_011:ComboBox(items, sel, _ => { })|};
    }
}");

    [Fact]
    public Task Fires_For_Slider_With_Named_Arguments() => Analyze(@"
class C
{
    void M(double score)
    {
        {|REACTOR_HOOKS_011:Slider(value: score, onValueChanged: _ => { })|};
    }
}");

    [Fact]
    public Task Fires_When_IsReadOnly_Is_Explicitly_False() => Analyze(@"
class C
{
    void M(string name)
    {
        {|REACTOR_HOOKS_011:TextBox(name, _ => { })|}.IsReadOnly(false);
    }
}");

    // ── Negative cases ──────────────────────────────────────────────────

    [Fact]
    public Task No_Diagnostic_For_Bare_Literal_Value() => Analyze(@"
class C
{
    void M()
    {
        TextBox(""static label"");
    }
}");

    [Fact]
    public Task No_Diagnostic_When_Callback_Is_Omitted() => Analyze(@"
class C
{
    void M(string name)
    {
        // Value wired, callback omitted: a valid read-only display — not the target.
        TextBox(name);
    }
}");

    [Fact]
    public Task No_Diagnostic_For_Properly_Wired_Callback() => Analyze(@"
class C
{
    void M(string name, System.Action<string> setName)
    {
        TextBox(name, v => setName(v));
    }
}");

    [Fact]
    public Task No_Diagnostic_For_Parenthesized_Wired_Callback() => Analyze(@"
class C
{
    void M(string name, System.Action<string> setName)
    {
        TextBox(name, (v) => setName(v));
    }
}");

    [Fact]
    public Task No_Diagnostic_When_Discard_Named_Param_Is_Read() => Analyze(@"
class C
{
    // A lone '_' single-parameter lambda is a usable parameter, not a discard.
    void M(string name, System.Action<string> setName)
    {
        TextBox(name, _ => setName(_));
    }
}");

    [Fact]
    public Task No_Diagnostic_For_Literal_Value_With_Empty_Callback() => Analyze(@"
class C
{
    void M()
    {
        TextBox(""x"", _ => { });
    }
}");

    [Fact]
    public Task No_Diagnostic_For_Unset_Sentinel_Value() => Analyze(@"
class C
{
    void M()
    {
        TextBox(Optional<string>.Unset, _ => { });
    }
}");

    [Fact]
    public Task No_Diagnostic_For_MethodGroup_Callback() => Analyze(@"
class C
{
    void Handle(string s) { }
    void M(string name)
    {
        TextBox(name, Handle);
    }
}");

    [Fact]
    public Task No_Diagnostic_When_Marked_IsReadOnly_True() => Analyze(@"
class C
{
    void M(string name)
    {
        TextBox(name, _ => { }).IsReadOnly(true);
    }
}");

    [Fact]
    public Task No_Diagnostic_When_Marked_IsReadOnly_NoArg() => Analyze(@"
class C
{
    void M(string name)
    {
        TextBox(name, _ => { }).IsReadOnly();
    }
}");

    [Fact]
    public Task No_Diagnostic_When_IsReadOnly_Follows_Another_Modifier() => Analyze(@"
class C
{
    void M(string name)
    {
        TextBox(name, _ => { }).Margin(8).IsReadOnly(true);
    }
}");

    [Fact]
    public Task No_Diagnostic_For_NonDsl_LookAlike() => Analyze(@"
class C
{
    void M(string name)
    {
        Look.Widgets.TextBox(name, _ => { });
    }
}");

    // ── Code fix ────────────────────────────────────────────────────────

    [Fact]
    public Task CodeFix_Inserts_IsReadOnly_For_TextBox() => Fix(
        before: @"
class C
{
    void M(string name)
    {
        {|REACTOR_HOOKS_011:TextBox(name, _ => { })|};
    }
}",
        after: @"
class C
{
    void M(string name)
    {
        TextBox(name, _ => { }).IsReadOnly(true);
    }
}");

    [Fact]
    public Task CodeFix_Inserts_IsReadOnly_For_RatingControl() => Fix(
        before: @"
class C
{
    void M(double score)
    {
        {|REACTOR_HOOKS_011:RatingControl(score, _ => { })|};
    }
}",
        after: @"
class C
{
    void M(double score)
    {
        RatingControl(score, _ => { }).IsReadOnly(true);
    }
}");

    [Fact]
    public Task CodeFix_Preserves_Trivia_Inside_The_Call() => Fix(
        // Trivia inside the argument list (e.g. a comment) must survive the rewrite.
        before: @"
class C
{
    void M(string name)
    {
        {|REACTOR_HOOKS_011:TextBox(name, /*keep*/ _ => { })|};
    }
}",
        after: @"
class C
{
    void M(string name)
    {
        TextBox(name, /*keep*/ _ => { }).IsReadOnly(true);
    }
}");

    [Fact]
    public Task CodeFix_Not_Offered_For_Control_Without_IsReadOnly() => Fix(
        // Slider has no IsReadOnly modifier — nudge only. The warning stands but no
        // rewrite occurs (TestCode == FixedCode).
        before: @"
class C
{
    void M(double score)
    {
        {|REACTOR_HOOKS_011:Slider(score, 0, 100, _ => { })|};
    }
}",
        after: @"
class C
{
    void M(double score)
    {
        {|REACTOR_HOOKS_011:Slider(score, 0, 100, _ => { })|};
    }
}");

    [Fact]
    public Task CodeFix_Not_Offered_When_IsReadOnly_Already_Present() => Fix(
        // Explicit .IsReadOnly(false) means the author wants it editable; wrapping with
        // .IsReadOnly(true) would produce contradictory modifiers, so no fix is offered
        // (the warning still stands as a nudge to wire the callback).
        before: @"
class C
{
    void M(string name)
    {
        {|REACTOR_HOOKS_011:TextBox(name, _ => { })|}.IsReadOnly(false);
    }
}",
        after: @"
class C
{
    void M(string name)
    {
        {|REACTOR_HOOKS_011:TextBox(name, _ => { })|}.IsReadOnly(false);
    }
}");

    [Fact]
    public async Task CodeFix_Not_Offered_When_ReadOnly_Extension_Not_In_Scope()
    {
        // Fully-qualified factory call WITHOUT `using Microsoft.UI.Reactor;`. The analyzer
        // still fires (symbol resolution ignores usings), but .IsReadOnly(...) is an
        // extension method that would not bind here — so no fix is offered and the code is
        // left unchanged (TestCode == FixedCode).
        var code = @"
using System;

namespace Microsoft.UI.Reactor
{
    public readonly struct Optional<T>
    {
        public static implicit operator Optional<T>(T value) => default;
    }
    public class TextBoxElement { }
    public static partial class Factories
    {
        public static TextBoxElement TextBox(Optional<string> value = default, Action<string> onChanged = null, string placeholderText = null, string header = null) => new TextBoxElement();
    }
    public static class ControlledInputStubExtensions
    {
        public static TextBoxElement IsReadOnly(this TextBoxElement el, bool readOnly = true) => el;
    }
}

class C
{
    void M(string name)
    {
        {|REACTOR_HOOKS_011:Microsoft.UI.Reactor.Factories.TextBox(name, _ => { })|};
    }
}";

        await new CSharpCodeFixTest<ControlledInputAnalyzer, ControlledInputCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
