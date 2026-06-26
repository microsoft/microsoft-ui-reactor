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
        protected ButtonElement MenuItem<T>(Command<T> command, T parameter) => new ButtonElement();
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
}
