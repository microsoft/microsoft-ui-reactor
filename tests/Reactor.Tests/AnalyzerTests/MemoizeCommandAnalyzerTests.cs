using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="MemoizeCommandAnalyzer"/> (<c>REACTOR_PERF_FUNCREF</c>) and its
/// <see cref="MemoizeCommandCodeFix"/>. Stubs a minimal Reactor surface — the
/// <c>Command</c>/<c>Command&lt;T&gt;</c> records and a <c>Component</c> base exposing
/// <c>Render</c>, <c>UseMemo</c>, <c>UseCommand</c>, <c>UseState</c> and a <c>Button</c> binding
/// factory — so the analyzer's semantic checks (Reactor command type, UseCommand suppression) and
/// the fix's UseMemo-in-scope guard resolve without pulling the framework in.
/// </summary>
public class MemoizeCommandAnalyzerTests
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
        public bool CanExecute { get; init; }
        public int DebounceMs { get; init; }
    }

    public sealed record Command<T>
    {
        public string Label { get; init; }
        public System.Action<T> Execute { get; init; }
        public int DebounceMs { get; init; }
    }

    // Mirrors Component's render override + protected hooks + a Command binding factory. UseMemo /
    // UseCommand living in Microsoft.UI.Reactor.Core is what lets the analyzer treat UseCommand as
    // the routing call and the fix find a Reactor UseMemo in scope.
    public abstract class Component
    {
        public abstract Element Render();
        protected T UseMemo<T>(System.Func<T> factory, params object[] deps) => factory();
        protected Command UseCommand(Command command) => command;
        protected Command<T> UseCommand<T>(Command<T> command) => command;
        protected (int, System.Action<int>) UseState(int initial) => (initial, _ => { });
        protected ButtonElement Button(Command command) => new ButtonElement();
        protected ButtonElement Button(string label) => new ButtonElement();
        protected ButtonElement Button(string label, System.Action onClick) => new ButtonElement();
    }
}
";

    private static CSharpAnalyzerTest<MemoizeCommandAnalyzer, DefaultVerifier> Analyzer(string source) =>
        new() { TestCode = source };

    // ── Positive: constructed directly in the render path ───────────────

    [Fact]
    public async Task Fires_On_Local_In_Render()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
        {
            var save = {|REACTOR_PERF_FUNCREF:new Command { Label = ""Save"", Execute = Save }|};
            return Button(save);
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Inline_Bind_In_Render()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
            => Button({|REACTOR_PERF_FUNCREF:new Command { Label = ""Save"", Execute = Save }|});
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_In_Custom_Use_Hook()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        Command UseSaveCommand()
            => {|REACTOR_PERF_FUNCREF:new Command { Label = ""Save"", Execute = Save }|};

        public override Element Render() => Button(UseSaveCommand());
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Generic_Command()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        public override Element Render()
        {
            var cmd = {|REACTOR_PERF_FUNCREF:new Command<int> { Label = ""Inc"" }|};
            return Button(""x"");
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative: already memoized / routed / deferred / off the render path ──

    [Fact]
    public async Task No_Diagnostic_When_Wrapped_In_UseMemo()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
        {
            var save = UseMemo(() => new Command { Label = ""Save"", Execute = Save });
            return Button(save);
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Routed_Through_UseCommand()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
        {
            var save = UseCommand(new Command { Label = ""Save"", Execute = Save });
            return Button(save);
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Deferred_In_Event_Handler()
    {
        // The command is built inside a click handler lambda — it runs on click, not each render,
        // so it is not a per-render allocation. CrossesDeferredBoundary suppresses it.
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }
        void Use(Command c) { }

        public override Element Render()
            => Button(""Open"", () => { var c = new Command { Label = ""Save"", Execute = Save }; Use(c); });
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Built_Outside_Render()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        // Not a Render()/Use* method — a plain helper, so no per-render concern.
        Command BuildSave() => new Command { Label = ""Save"", Execute = Save };

        public override Element Render() => Button(BuildSave());
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_DebounceMs_Set_Cedes_To_HOOKS_009()
    {
        // A non-zero DebounceMs is REACTOR_HOOKS_009's domain (must go through UseCommand, not
        // UseMemo); firing here too would give conflicting advice on the same construct.
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
        {
            var save = new Command { Label = ""Save"", Execute = Save, DebounceMs = 1500 };
            return Button(save);
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Inside_Loop()
    {
        // A command built inside a loop can't be wrapped in a top-level UseMemo without violating the
        // rules-of-hooks (REACTOR_HOOKS_001), so the rule stays silent rather than offer a broken fix.
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
        {
            for (int i = 0; i < 3; i++)
            {
                var save = new Command { Label = ""Save"", Execute = Save };
                Button(save);
            }
            return Button(""x"");
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Inside_If()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
        {
            Command save = null;
            if (System.DateTime.Now.Second > 0)
                save = new Command { Label = ""Save"", Execute = Save };
            return Button(save);
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_In_Conditional_Expression()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
        {
            Command other = null;
            var save = System.DateTime.Now.Second > 0 ? new Command { Label = ""Save"", Execute = Save } : other;
            return Button(save);
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_In_Coalesce_Right_Operand()
    {
        // `existing ?? new Command { … }` — the new command is evaluated conditionally, so a UseMemo
        // there would break the rules-of-hooks.
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
        {
            Command existing = null;
            var save = existing ?? new Command { Label = ""Save"", Execute = Save };
            return Button(save);
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_In_Switch_Expression_Arm()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
        {
            Command other = null;
            var save = System.DateTime.Now.Second switch
            {
                0 => new Command { Label = ""Save"", Execute = Save },
                _ => other,
            };
            return Button(save);
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Not_In_Reactor_Component()
    {
        // A plain class (not a Component / RenderContext) with a method named Render that builds a
        // Reactor Command — the render-context anchor excludes it, so no diagnostic.
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class NotAComponent
    {
        void Save() { }

        public Element Render()
        {
            var save = new Command { Label = ""Save"", Execute = Save };
            return save is null ? null : null;
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Near-miss: syntactic ""Command"" name matches, but not the Reactor type ──

    [Fact]
    public async Task No_Diagnostic_On_NonReactor_Command_Type()
    {
        var source = Stubs + @"
namespace Other
{
    // Same simple name, different type — trips the syntactic fast path, fails the semantic gate.
    public sealed class Command { public string Label; }
}

namespace TestApp
{
    using Other;

    // Base class fully-qualified so only Other.Command is imported (no CS0104 ambiguity); the
    // unqualified `Command` below binds to Other.Command, which is not the Reactor type.
    public sealed class Comp : Microsoft.UI.Reactor.Core.Component
    {
        public override Microsoft.UI.Reactor.Core.Element Render()
        {
            var save = new Command { Label = ""Save"" };
            return Button(save.Label);
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code fix ────────────────────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Wraps_Local_With_Captured_Deps()
    {
        var before = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            var save = {|REACTOR_PERF_FUNCREF:new Command { Label = ""Save"", Execute = () => setCount(count + 1) }|};
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
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            var save = UseMemo(() => new Command { Label = ""Save"", Execute = () => setCount(count + 1) }, count, setCount);
            return Button(save);
        }
    }
}";

        await new CSharpCodeFixTest<MemoizeCommandAnalyzer, MemoizeCommandCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = MemoizeCommandAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_No_Captures_Emits_No_Deps()
    {
        var before = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
            => Button({|REACTOR_PERF_FUNCREF:new Command { Label = ""Save"", Execute = Save }|});
    }
}";

        var after = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
            => Button(UseMemo(() => new Command { Label = ""Save"", Execute = Save }));
    }
}";

        await new CSharpCodeFixTest<MemoizeCommandAnalyzer, MemoizeCommandCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = MemoizeCommandAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_Implicit_New()
    {
        var before = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Save() { }

        public override Element Render()
        {
            Command save = {|REACTOR_PERF_FUNCREF:new() { Label = ""Save"", Execute = Save }|};
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

        public override Element Render()
        {
            Command save = UseMemo(() => new Command { Label = ""Save"", Execute = Save });
            return Button(save);
        }
    }
}";

        await new CSharpCodeFixTest<MemoizeCommandAnalyzer, MemoizeCommandCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = MemoizeCommandAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_Command_Reads_Instance_Field()
    {
        // The command reads a mutable instance field at render time (snapshotted into CanExecute).
        // The fix can't turn that into a UseMemo dependency, so it declines — but the diagnostic
        // still fires. FixedCode == TestCode (no code action applied).
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        bool _isValid;
        void Save() { }

        public override Element Render()
        {
            var save = {|REACTOR_PERF_FUNCREF:new Command { Label = ""Save"", Execute = Save, CanExecute = _isValid }|};
            return Button(save);
        }
    }
}";

        await new CSharpCodeFixTest<MemoizeCommandAnalyzer, MemoizeCommandCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
            CodeActionEquivalenceKey = MemoizeCommandAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Escapes_Keyword_Dependency()
    {
        // A captured local whose name is a reserved keyword (`@event`) must be re-emitted with `@`
        // in the deps list, or the generated UseMemo call would not compile.
        var before = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        void Use(int x) { }

        public override Element Render()
        {
            var @event = 0;
            var save = {|REACTOR_PERF_FUNCREF:new Command { Label = ""Save"", Execute = () => Use(@event) }|};
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
        void Use(int x) { }

        public override Element Render()
        {
            var @event = 0;
            var save = UseMemo(() => new Command { Label = ""Save"", Execute = () => Use(@event) }, @event);
            return Button(save);
        }
    }
}";

        await new CSharpCodeFixTest<MemoizeCommandAnalyzer, MemoizeCommandCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = MemoizeCommandAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_When_Command_Reads_Base_Member()
    {
        // `base.Member` is a render-time instance read too — the fix must decline (no code action),
        // just like an implicit-`this` member, so it never memoizes a stale value.
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;

    public abstract class BaseComp : Component
    {
        protected string BaseLabel => ""Save"";
    }

    public sealed class Comp : BaseComp
    {
        void Save() { }

        public override Element Render()
        {
            var save = {|REACTOR_PERF_FUNCREF:new Command { Label = base.BaseLabel, Execute = Save }|};
            return Button(save);
        }
    }
}";

        await new CSharpCodeFixTest<MemoizeCommandAnalyzer, MemoizeCommandCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
            CodeActionEquivalenceKey = MemoizeCommandAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
