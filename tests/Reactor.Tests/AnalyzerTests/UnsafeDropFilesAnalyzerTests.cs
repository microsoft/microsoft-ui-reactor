using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="UnsafeDropFilesAnalyzer"/> (<c>REACTOR_INPUT_002</c>) and its
/// <see cref="UnsafeDropFilesCodeFix"/>. Stubs a minimal Reactor-shaped <c>DragData</c>
/// (with both <c>TryGetFiles</c> and <c>TryGetSafeLocalFiles</c>), a <c>DragTargetArgs</c>
/// carrying it, and an <c>.OnDrop</c> fluent modifier so the syntactic + semantic match
/// fires without pulling the framework in.
/// </summary>
public class UnsafeDropFilesAnalyzerTests
{
    // Mirrors src/Reactor/Input/DragData.cs: DragData lives in Microsoft.UI.Reactor.Input
    // and exposes the unsafe TryGetFiles alongside the filtered TryGetSafeLocalFiles, both
    // bool(out IReadOnlyList<...>). OtherData is a decoy with the same method name in a
    // different type, to exercise the semantic DragData gate.
    private const string Stubs = @"
using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Input;

namespace Microsoft.UI.Reactor.Input
{
    public sealed class DragData
    {
        public bool TryGetFiles(out IReadOnlyList<object> value) { value = Array.Empty<object>(); return false; }
        public bool TryGetSafeLocalFiles(out IReadOnlyList<object> value) { value = Array.Empty<object>(); return false; }
    }

    public sealed class DragTargetArgs
    {
        public DragData Data { get; } = new DragData();
    }

    // Raw target-side config (mirrors DragConfigs.cs): OnDrop is a public handler member.
    public sealed class DropTargetConfig
    {
        public Action<DragTargetArgs> OnDrop;
    }
}

// Decoy: same method name, different (non-DragData) type.
public sealed class OtherData
{
    public bool TryGetFiles(out System.Collections.Generic.IReadOnlyList<object> value) { value = System.Array.Empty<object>(); return false; }
}

public class FakeElement { }

public static class FakeElementExtensions
{
    public static T OnDrop<T>(this T el, Action<Microsoft.UI.Reactor.Input.DragTargetArgs> handler) => el;
    // A non-drop modifier taking the same lambda shape — proves the .OnDrop gate.
    public static T OnHover<T>(this T el, Action<Microsoft.UI.Reactor.Input.DragTargetArgs> handler) => el;
}
";

    // ── Positive ────────────────────────────────────────────────────────

    [Fact]
    public async Task Fires_For_TryGetFiles_On_ArgsData_In_OnDrop()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args => {|REACTOR_INPUT_002:args.Data.TryGetFiles(out var f)|});
    }
}";

        await new CSharpAnalyzerTest<UnsafeDropFilesAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_TryGetFiles_On_Local_DragData_In_OnDrop()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args =>
        {
            var d = new Microsoft.UI.Reactor.Input.DragData();
            {|REACTOR_INPUT_002:d.TryGetFiles(out var f)|};
        });
    }
}";

        await new CSharpAnalyzerTest<UnsafeDropFilesAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_TryGetFiles_In_Nested_Closure_Inside_OnDrop()
    {
        // The call sits in an inner lambda nested inside the OnDrop lambda — it still runs
        // during the drop, so the rule walks every enclosing lambda and fires.
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args =>
        {
            System.Action run = () => { {|REACTOR_INPUT_002:args.Data.TryGetFiles(out var f)|}; };
            run();
        });
    }
}";

        await new CSharpAnalyzerTest<UnsafeDropFilesAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Generic_OnDrop_Call()
    {
        // Explicit type argument makes the OnDrop name a GenericNameSyntax at the gate.
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop<FakeElement>(args => {|REACTOR_INPUT_002:args.Data.TryGetFiles(out var f)|});
    }
}";

        await new CSharpAnalyzerTest<UnsafeDropFilesAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_TryGetFiles_In_DropTargetConfig_Initializer()
    {
        // The raw DropTargetConfig { OnDrop = ... } form is also a drop handler — the source
        // still picks the files, so the rule fires on the assignment form too.
        var source = Stubs + @"
class C
{
    void M()
    {
        var cfg = new Microsoft.UI.Reactor.Input.DropTargetConfig
        {
            OnDrop = args => {|REACTOR_INPUT_002:args.Data.TryGetFiles(out var f)|}
        };
    }
}";

        await new CSharpAnalyzerTest<UnsafeDropFilesAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Conditional_Access_TryGetFiles_In_OnDrop()
    {
        // Null-conditional call: `d?.TryGetFiles(...)` — invocation.Expression is a
        // MemberBindingExpressionSyntax (not MemberAccess). The rule still fires.
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args =>
        {
            Microsoft.UI.Reactor.Input.DragData d = args.Data;
            d?{|REACTOR_INPUT_002:.TryGetFiles(out _)|};
        });
    }
}";

        await new CSharpAnalyzerTest<UnsafeDropFilesAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_TryGetFiles_In_Conditional_OnDrop_Call()
    {
        // Null-conditional on the OnDrop call itself: `el?.OnDrop(...)`. The outer invocation's
        // expression is a MemberBindingExpressionSyntax, still recognized as a drop handler.
        var source = Stubs + @"
class C
{
    void M()
    {
        FakeElement el = new FakeElement();
        el?.OnDrop(args => {|REACTOR_INPUT_002:args.Data.TryGetFiles(out var f)|});
    }
}";

        await new CSharpAnalyzerTest<UnsafeDropFilesAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_TryGetFiles_In_Anonymous_Method_OnDrop_Handler()
    {
        // Old-style anonymous method: delegate(DragTargetArgs args) { ... } is an
        // AnonymousFunctionExpression (not a lambda), still a drop handler.
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(delegate(Microsoft.UI.Reactor.Input.DragTargetArgs args)
        {
            {|REACTOR_INPUT_002:args.Data.TryGetFiles(out var f)|};
        });
    }
}";

        await new CSharpAnalyzerTest<UnsafeDropFilesAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative ────────────────────────────────────────────────────────

    [Fact]
    public async Task No_Diagnostic_For_Bare_Assignment_To_Local_Named_OnDrop()
    {
        // `OnDrop` here is an unrelated local delegate assigned outside any object/with
        // initializer, so the assignment form must NOT be treated as a drop handler.
        var source = Stubs + @"
class C
{
    void M()
    {
        System.Action<Microsoft.UI.Reactor.Input.DragData> OnDrop = null;
        OnDrop = d => d.TryGetFiles(out var f);
    }
}";

        await new CSharpAnalyzerTest<UnsafeDropFilesAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_TryGetSafeLocalFiles_Already_Used()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args => args.Data.TryGetSafeLocalFiles(out var f));
    }
}";

        await new CSharpAnalyzerTest<UnsafeDropFilesAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_TryGetFiles_Outside_A_Drop_Context()
    {
        // Same DragData receiver + method, but not inside any .OnDrop — the drop-context
        // gate rejects it. (Also covers the non-OnDrop .OnHover modifier.)
        var source = Stubs + @"
class C
{
    void M()
    {
        var d = new Microsoft.UI.Reactor.Input.DragData();
        d.TryGetFiles(out var f);

        var el = new FakeElement();
        el.OnHover(args => args.Data.TryGetFiles(out var g));
    }
}";

        await new CSharpAnalyzerTest<UnsafeDropFilesAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_TryGetFiles_On_NonDragData_Receiver_In_OnDrop()
    {
        // Near-miss: trips the syntactic name gate AND the .OnDrop gate, but the receiver is
        // OtherData, not Microsoft.UI.Reactor.Input.DragData, so the semantic gate rejects it.
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        var other = new OtherData();
        el.OnDrop(args => other.TryGetFiles(out var f));
    }
}";

        await new CSharpAnalyzerTest<UnsafeDropFilesAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code fix (swap round-trip) ──────────────────────────────────────

    [Fact]
    public async Task CodeFix_Swaps_TryGetFiles_To_TryGetSafeLocalFiles()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args => {|REACTOR_INPUT_002:args.Data.TryGetFiles(out var f)|});
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args => args.Data.TryGetSafeLocalFiles(out var f));
    }
}";

        await new CSharpCodeFixTest<UnsafeDropFilesAnalyzer, UnsafeDropFilesCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Swaps_Inside_Block_Bodied_OnDrop_Lambda()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args =>
        {
            {|REACTOR_INPUT_002:args.Data.TryGetFiles(out var f)|};
        });
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args =>
        {
            args.Data.TryGetSafeLocalFiles(out var f);
        });
    }
}";

        await new CSharpCodeFixTest<UnsafeDropFilesAnalyzer, UnsafeDropFilesCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Swaps_All_Occurrences_In_One_File()
    {
        // Two unsafe calls in the same file — the BatchFixer FixAll provider rewrites both.
        var before = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args => {|REACTOR_INPUT_002:args.Data.TryGetFiles(out var f)|});

        var cfg = new Microsoft.UI.Reactor.Input.DropTargetConfig
        {
            OnDrop = args => {|REACTOR_INPUT_002:args.Data.TryGetFiles(out var g)|}
        };
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args => args.Data.TryGetSafeLocalFiles(out var f));

        var cfg = new Microsoft.UI.Reactor.Input.DropTargetConfig
        {
            OnDrop = args => args.Data.TryGetSafeLocalFiles(out var g)
        };
    }
}";

        await new CSharpCodeFixTest<UnsafeDropFilesAnalyzer, UnsafeDropFilesCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Swaps_Conditional_Access_Call()
    {
        // The name identifier is swapped; the `?.` operator and args are preserved.
        var before = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args =>
        {
            Microsoft.UI.Reactor.Input.DragData d = args.Data;
            d?{|REACTOR_INPUT_002:.TryGetFiles(out _)|};
        });
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.OnDrop(args =>
        {
            Microsoft.UI.Reactor.Input.DragData d = args.Data;
            d?.TryGetSafeLocalFiles(out _);
        });
    }
}";

        await new CSharpCodeFixTest<UnsafeDropFilesAnalyzer, UnsafeDropFilesCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
