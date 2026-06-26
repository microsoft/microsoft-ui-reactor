using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="CommandDebounceAnalyzer"/> (<c>REACTOR_HOOKS_009</c>) and its
/// <see cref="CommandDebounceCodeFix"/>. Stubs a minimal Reactor command surface — the
/// <c>Command</c>/<c>Command&lt;T&gt;</c> records, an <c>Element</c>/<c>ButtonElement</c>, and a
/// <c>Component</c> base exposing <c>UseCommand</c> + a <c>Button</c> binding factory — so the
/// analyzer's semantic checks (Reactor command type, UseCommand routing, Reactor binding sink)
/// resolve without pulling the framework in.
/// </summary>
public class CommandDebounceAnalyzerTests
{
    private const string Stubs = @"
namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit { }
}

namespace Microsoft.UI.Reactor.Core
{
    public abstract record Element { }
    public sealed record ButtonElement : Element { }

    public sealed record Command
    {
        public string Label { get; init; }
        public System.Action Execute { get; init; }
        public int DebounceMs { get; init; }
    }

    public sealed record Command<T>
    {
        public string Label { get; init; }
        public System.Action<T> Execute { get; init; }
        public int DebounceMs { get; init; }
    }

    // Mirrors Component's protected hook + the Command-accepting binding factories. Living in a
    // Microsoft.UI.Reactor.* namespace is what makes the analyzer treat Button/MenuItem as a
    // binding sink and UseCommand as the routing call.
    public abstract class Component
    {
        protected Command UseCommand(Command command) => command;
        protected Command<T> UseCommand<T>(Command<T> command) => command;
        protected ButtonElement Button(Command command) => new ButtonElement();
        protected ButtonElement Button(string label) => new ButtonElement();
        protected ButtonElement MenuItem<T>(Command<T> command, T parameter) => new ButtonElement();
    }

    // Mirrors the `.Command(...)` fluent modifier (ElementExtensions.cs). Living in a
    // Microsoft.UI.Reactor.* namespace is what makes the analyzer treat it as a binding sink.
    public static class ButtonModifiers
    {
        public static ButtonElement Command(this ButtonElement element, Command command) => element;
    }
}

namespace Microsoft.UI.Reactor
{
    using Microsoft.UI.Reactor.Core;

    // Mirrors the static Dsl factories (Dsl.cs). Being static, they bind anywhere — including a
    // helper outside a Component, where the UseCommand hook is NOT in scope. Used to exercise the
    // code fix's out-of-scope guard.
    public static class Factories
    {
        public static ButtonElement Button(Command command) => new ButtonElement();
    }
}
";

    // ── Positive: bound directly without UseCommand ─────────────────────

    [Fact]
    public async Task Fires_On_Direct_Button_Bind()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
            => Button({|REACTOR_HOOKS_009:new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 }|});
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Local_Bound_Directly()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
        {
            var save = {|REACTOR_HOOKS_009:new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 }|};
            return Button(save);
        }
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_With_Expression()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
        {
            var baseCmd = new Command { Label = ""Save"", Execute = Save };
            return Button({|REACTOR_HOOKS_009:baseCmd with { DebounceMs = 1500 }|});
        }
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Generic_Command_Bind()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Delete(int id) { }

        public Element Render()
            => MenuItem({|REACTOR_HOOKS_009:new Command<int> { Label = ""Delete"", Execute = Delete, DebounceMs = 800 }|}, 5);
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative: routed through UseCommand ─────────────────────────────

    [Fact]
    public async Task No_Diagnostic_When_Routed_Through_UseCommand_Inline()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
            => Button(UseCommand(new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 }));
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Routed_Through_UseCommand_Via_Local()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
        {
            var save = new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 };
            var wrapped = UseCommand(save);
            return Button(wrapped);
        }
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative: DebounceMs is zero / unset ────────────────────────────

    [Fact]
    public async Task No_Diagnostic_When_DebounceMs_Zero()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
            => Button(new Command { Label = ""Save"", Execute = Save, DebounceMs = 0 });
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_DebounceMs_Unset()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
            => Button(new Command { Label = ""Save"", Execute = Save });
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative: created but not bound (e.g. factory method) ───────────

    [Fact]
    public async Task No_Diagnostic_When_Command_Returned_Not_Bound()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        // Returns the raw command; a caller is expected to wrap it in UseCommand.
        public Command MakeSave()
            => new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 };
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code fix ────────────────────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Wraps_Inline_Bind_In_UseCommand()
    {
        var before = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
            => Button({|REACTOR_HOOKS_009:new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 }|});
    }
}";

        var after = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
            => Button(UseCommand(new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 }));
    }
}";

        await new CSharpCodeFixTest<CommandDebounceAnalyzer, CommandDebounceCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = CommandDebounceAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Wraps_Local_Initializer_In_UseCommand()
    {
        var before = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
        {
            var save = {|REACTOR_HOOKS_009:new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 }|};
            return Button(save);
        }
    }
}";

        var after = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
        {
            var save = UseCommand(new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 });
            return Button(save);
        }
    }
}";

        await new CSharpCodeFixTest<CommandDebounceAnalyzer, CommandDebounceCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = CommandDebounceAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Binding sinks: `.Command(...)` modifier + syntactic factory fallback ──

    [Fact]
    public async Task Fires_On_Command_Modifier_Bind()
    {
        // The `.Command(...)` fluent sink resolves (semantically) to a Reactor-namespace
        // extension method, so the member-access binding arm should fire.
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
            => Button(""Save"").Command({|REACTOR_HOOKS_009:new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 }|});
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Resolved_NonReactor_Factory()
    {
        // A method named ""Button"" that Roslyn resolves to a non-Reactor namespace is an unrelated
        // API, not a Reactor bind — it must NOT warn (the syntactic factory-name list is only a
        // fallback for when symbol resolution fails, not an override of a successful resolution).
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public static class CustomUi
    {
        public static object Button(Command command) => command;
    }

    public sealed class Comp : Component
    {
        void Save() { }

        public object Render()
            => CustomUi.Button(new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 });
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_Via_Syntactic_Fallback_When_Unresolved()
    {
        // When the binding callee can't be resolved (incomplete code mid-edit — here `Button` is
        // undefined), the conservative syntactic fallback on the known-factory name list still
        // surfaces the footgun. The CS0103 is expected and declared via markup.
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp
    {
        void Save() { }

        public void Render()
        {
            _ = {|CS0103:Button|}({|REACTOR_HOOKS_009:new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 }|});
        }
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Parenthesized_Direct_Bind()
    {
        // Parentheses around the argument (`Button((cmd))`) must not hide the direct bind.
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
            => Button(({|REACTOR_HOOKS_009:new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 }|}));
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative: a foreign, same-named Command type must never fire (FP guard) ──

    [Fact]
    public async Task No_Diagnostic_When_Foreign_Command_Type()
    {
        // A different library's `Command` (with its own DebounceMs) bound to its own factory must
        // NOT warn — the type is not Microsoft.UI.Reactor.Core.Command.
        var source = Stubs + @"
namespace Other
{
    public sealed record Command
    {
        public string Label { get; init; }
        public int DebounceMs { get; init; }
    }

    public static class Ui
    {
        public static object Button(Command command) => command;
    }
}

namespace TestApp
{
    public sealed class Comp : Microsoft.UI.Reactor.Core.Component
    {
        public object Render()
            => Other.Ui.Button(new Other.Command { Label = ""x"", DebounceMs = 1500 });
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Constant folding: const positive fires; const/literal <= 0 never does ──

    [Fact]
    public async Task Fires_On_Const_DebounceMs()
    {
        // A non-literal constant must still fold through GetConstantValue and fire when > 0.
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        const int Delay = 1500;

        public Element Render()
            => Button({|REACTOR_HOOKS_009:new Command { Label = ""Save"", Execute = Save, DebounceMs = Delay }|});
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_DebounceMs_Negative()
    {
        // Runtime only debounces > 0; a negative literal is a no-op and must never warn.
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
            => Button(new Command { Label = ""Save"", Execute = Save, DebounceMs = -5 });
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Const_DebounceMs_Zero()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        const int Off = 0;

        public Element Render()
            => Button(new Command { Label = ""Save"", Execute = Save, DebounceMs = Off });
    }
}";

        await new CSharpAnalyzerTest<CommandDebounceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code fix: `with` expression + implicit target-typed generic new ──

    [Fact]
    public async Task CodeFix_Wraps_With_Expression_In_UseCommand()
    {
        var before = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
        {
            var baseCmd = new Command { Label = ""Save"", Execute = Save };
            return Button({|REACTOR_HOOKS_009:baseCmd with { DebounceMs = 1500 }|});
        }
    }
}";

        var after = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public Element Render()
        {
            var baseCmd = new Command { Label = ""Save"", Execute = Save };
            return Button(UseCommand(baseCmd with { DebounceMs = 1500 }));
        }
    }
}";

        await new CSharpCodeFixTest<CommandDebounceAnalyzer, CommandDebounceCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = CommandDebounceAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_Implicit_Generic_New_To_Explicit()
    {
        // Regression guard (H1): wrapping a target-typed `new() { … }` verbatim re-targets it to
        // UseCommand's parameter and binds the non-generic overload → CS0123. The fix must first
        // materialize the resolved type as `new Command<int> { … }` so the result compiles. The
        // CodeFixTest harness compiles FixedCode, so a broken rewrite would fail here.
        var before = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Delete(int id) { }

        public Element Render()
            => MenuItem({|REACTOR_HOOKS_009:new() { Label = ""Delete"", Execute = Delete, DebounceMs = 800 }|}, 5);
    }
}";

        var after = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Delete(int id) { }

        public Element Render()
            => MenuItem(UseCommand(new Command<int> { Label = ""Delete"", Execute = Delete, DebounceMs = 800 }), 5);
    }
}";

        await new CSharpCodeFixTest<CommandDebounceAnalyzer, CommandDebounceCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = CommandDebounceAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_UseCommand_Out_Of_Scope()
    {
        // The bind resolves to the static Dsl factory (Reactor namespace), so the diagnostic fires
        // — but this is a static helper with no Component instance, so UseCommand is not in scope.
        // Wrapping in UseCommand(...) would not compile, so the fix must NOT be offered (FixedCode
        // is identical to the input — the warning stands, the author fixes it by hand).
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Factories;

    public static class Helpers
    {
        static void Save() { }

        public static Element Build()
            => Button({|REACTOR_HOOKS_009:new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 }|});
    }
}";

        await new CSharpCodeFixTest<CommandDebounceAnalyzer, CommandDebounceCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
