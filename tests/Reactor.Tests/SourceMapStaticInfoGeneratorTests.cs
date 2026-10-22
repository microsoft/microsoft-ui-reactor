using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Snapshot tests for the source-map generator's static-info pass: the identifier an
/// element is assigned to (<c>b.Name(…)</c>) and, per component render, the variable each
/// hook is stored in (<c>b.ComponentHooks(…)</c> / <c>b.RenderFunctionHooks(…)</c>).
///
/// <para>Runs the generator over a synthetic compilation with the
/// <see cref="SourceMapTransparentGeneratorTests"/> driver, which also proves the emitted
/// module initializer compiles. Expected positions are computed from the snippet text, so
/// re-indenting a snippet cannot silently re-baseline a test.</para>
/// </summary>
public sealed class SourceMapStaticInfoGeneratorTests
{
    private static readonly Regex s_name = new(
        @"b\.Name\(@""[^""]*"", (?<line>\d+), (?<col>\d+), @""(?<name>[^""]*)""\);", RegexOptions.CultureInvariant);

    private static readonly Regex s_componentHooks = new(
        @"b\.ComponentHooks\(@""(?<type>[^""]*)"", @""(?<hooks>[^""]*)""\);", RegexOptions.CultureInvariant);

    private static readonly Regex s_functionHooks = new(
        @"b\.RenderFunctionHooks\(@""[^""]*"", (?<line>\d+), (?<col>\d+), @""(?<hooks>[^""]*)""\);", RegexOptions.CultureInvariant);

    private static (int Line, int Column) PositionOf(string source, string needle, int occurrence = 1)
    {
        int index = -1;
        for (int i = 0; i < occurrence; i++)
        {
            index = source.IndexOf(needle, index + 1, StringComparison.Ordinal);
            Assert.True(index >= 0, $"probe '{needle}' (#{occurrence}) not found in the snippet");
        }
        int line = 1 + source.Take(index).Count(static c => c == '\n');
        int lineStart = index == 0 ? 0 : source.LastIndexOf('\n', index - 1) + 1;
        return (line, index - lineStart + 1);
    }

    private static int LineOf(string source, string needle) => PositionOf(source, needle).Line;

    private static Dictionary<(int Line, int Column), string> Names(string generated)
        => s_name.Matches(generated).ToDictionary(
            static m => (int.Parse(m.Groups["line"].Value), int.Parse(m.Groups["col"].Value)),
            static m => m.Groups["name"].Value);

    // ── Declared names ────────────────────────────────────────────────────

    [Fact]
    public void Names_LocalsFieldsPropertiesAndExpressionBodies()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public class Page
            {
                private readonly Element _header = TextBlock("header");
                public Element Title { get; } = TextBlock("title");
                public Element Footer => TextBlock("footer");
                public Element Status { get => TextBlock("status"); }
                private Element Row() => TextBlock("row");

                public Element Build(bool flag)
                {
                    var greeting = TextBlock("hi");
                    Element assigned;
                    assigned = TextBlock("assigned");
                    this.Field = TextBlock("member");
                    var pick = flag ? TextBlock("a") : TextBlock("b");
                    Element Local() => TextBlock("local");
                    return VStack(greeting, assigned, pick, Local());
                }

                public Element? Field;
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);
        var names = Names(generated);

        void Expect(string probe, string name)
            => Assert.Equal(name, names[PositionOf(code, probe)]);

        Expect("TextBlock(\"header\")", "_header");
        Expect("TextBlock(\"title\")", "Title");
        Expect("TextBlock(\"footer\")", "Footer");
        Expect("TextBlock(\"status\")", "Status");
        Expect("TextBlock(\"row\")", "Row");
        Expect("TextBlock(\"hi\")", "greeting");
        Expect("TextBlock(\"assigned\")", "assigned");
        Expect("TextBlock(\"member\")", "Field");
        Expect("TextBlock(\"a\")", "pick");
        Expect("TextBlock(\"b\")", "pick");
        Expect("TextBlock(\"local\")", "Local");

        // The VStack is returned, not assigned: no name.
        Assert.False(names.ContainsKey(PositionOf(code, "VStack(")));
    }

    [Fact]
    public void Names_LookThroughFluentChainsButNotOutOfThem()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public static class Mods
            {
                public static T Bold<T>(this T element) where T : Element => element;
                public static string Describe(this Element element) => "x";
            }

            public static class Page
            {
                public static void Build()
                {
                    var title = (TextBlock("t").Bold()).Bold()!;
                    var text = TextBlock("not an element at the end").Describe();
                }
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);
        var names = Names(generated);

        Assert.Equal("title", names[PositionOf(code, "TextBlock(\"t\")")]);
        Assert.False(names.ContainsKey(PositionOf(code, "TextBlock(\"not")), "a chain ending in a non-element names nothing");
    }

    [Fact]
    public void Names_InlineElementsAndRenderAreUnnamed()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public class Card : Component
            {
                public override Element Render() => VStack(TextBlock("a"), TextBlock("b"));
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);

        // The arguments are inline; the VStack is Render's output, which names the
        // component, not an element. Positive control: the factory calls were intercepted.
        Assert.Contains("InterceptsLocationAttribute(", generated, StringComparison.Ordinal);
        Assert.Empty(Names(generated));
    }

    // ── Hook names ────────────────────────────────────────────────────────

    [Fact]
    public void Hooks_ClassComponent_DeconstructionDeclaratorAndUnstored()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            namespace App
            {
                public class Outer
                {
                    public sealed class Counter : Component
                    {
                        public override Element Render()
                        {
                            var (count, setCount) = UseState(0);
                            var density = UseRef(1.0);
                            UseEffect(() => setCount(count + 1), count);
                            var (_, setOther) = UseState("x");
                            return TextBlock($"{count} {density}");
                        }
                    }
                }
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);
        var match = Assert.Single(s_componentHooks.Matches(generated));

        Assert.Equal("App.Outer+Counter", match.Groups["type"].Value);
        Assert.Equal(
            $"0:count@{LineOf(code, "UseState(0)")};1:density@{LineOf(code, "UseRef(")};"
            + $"2:UseEffect@{LineOf(code, "UseEffect(")};3:setOther@{LineOf(code, "UseState(\"x\")")}",
            match.Groups["hooks"].Value);
    }

    [Fact]
    public void Hooks_NestedInAnArgument_TakeTheEarlierSlot()
    {
        // C# evaluates arguments first, so the inner hook runs (and takes its slot) before the
        // outer one even though it starts later in the source.
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public sealed class Tabs : Component
            {
                public override Element Render()
                {
                    var (tabs, setTabs) = UseState(UseRef(3));
                    var after = UseRef("after");
                    return TextBlock("tabs");
                }
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);
        var match = Assert.Single(s_componentHooks.Matches(generated));

        int line = LineOf(code, "UseState(UseRef(3))");
        Assert.Equal($"0:UseRef@{line};1:tabs@{line};2:after@{LineOf(code, "UseRef(\"after\")")}", match.Groups["hooks"].Value);
    }

    [Fact]
    public void Hooks_EveryRenderOverrideIsRecorded_InheritingClassesAreNot()
    {
        // The runtime walks a component's base types to the Render() it runs; a hook-free
        // override must be recorded (empty) so it does not borrow its base class's hooks.
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public class BaseCard : Component
            {
                public override Element Render()
                {
                    var (taps, _) = UseState(0);
                    return TextBlock("base");
                }
            }

            public sealed class Inherits : BaseCard { }

            public sealed class Plain : BaseCard
            {
                public override Element Render() => TextBlock("plain");
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);
        var entries = s_componentHooks.Matches(generated).ToDictionary(m => m.Groups["type"].Value, m => m.Groups["hooks"].Value);

        Assert.Equal($"0:taps@{LineOf(code, "UseState(0)")}", entries["BaseCard"]);
        Assert.Equal(string.Empty, entries["Plain"]);
        Assert.False(entries.ContainsKey("Inherits"));
        Assert.Contains("RegisterStaticInfo(typeof(ReactorSourceMapStaticInfo).Assembly, Fill)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Hooks_GenericComponent_IsKeyedByItsOpenDefinition()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public sealed class ItemList<T> : Component
            {
                public override Element Render()
                {
                    var (items, _) = UseState(0);
                    return TextBlock("list");
                }
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);

        Assert.Equal("ItemList`1", Assert.Single(s_componentHooks.Matches(generated)).Groups["type"].Value);
    }

    [Fact]
    public void Hooks_RenderFunction_IsKeyedByTheCallItIsPassedTo()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public static class Page
            {
                public static Element Build() => Memo(ctx =>
                {
                    var (n, setN) = ctx.UseState(0);
                    return TextBlock($"{n}");
                }, 1);
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);
        var match = Assert.Single(s_functionHooks.Matches(generated));

        // Same (line, column) the interceptor stamps on the Memo element's CallSite.
        Assert.Equal(PositionOf(code, "Memo("), (int.Parse(match.Groups["line"].Value), int.Parse(match.Groups["col"].Value)));
        Assert.Equal($"0:n@{LineOf(code, "ctx.UseState(0)")}", match.Groups["hooks"].Value);
    }

    [Fact]
    public void Hooks_CustomHookInSource_CountsItsSlots_UnknownHookMakesTheRestUnknown()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public static class Hooks
            {
                // Two slots: a state and an effect.
                public static (int, global::System.Action<int>) UseCounter(RenderContext ctx)
                {
                    var state = ctx.UseState(0);
                    ctx.UseEffect(() => { });
                    return state;
                }
            }

            public static class Page
            {
                public static Element Build() => Memo(ctx =>
                {
                    var (a, _) = Hooks.UseCounter(ctx);
                    var b = ctx.UseRef(0);
                    var active = ctx.UseIsActive();
                    var c = ctx.UseRef(1);
                    return TextBlock("x");
                });
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);
        var hooks = Assert.Single(s_functionHooks.Matches(generated)).Groups["hooks"].Value;

        // UseCounter takes 2 slots, so `b` is slot 2. UseIsActive is a built-in this
        // generator does not track, so its own index is known but the next one is not.
        Assert.Equal(
            $"0:a@{LineOf(code, "Hooks.UseCounter(ctx)")};2:b@{LineOf(code, "ctx.UseRef(0)")};"
            + $"3:active@{LineOf(code, "ctx.UseIsActive()")};?:c@{LineOf(code, "ctx.UseRef(1)")}",
            hooks);
    }

    [Fact]
    public void Hooks_ComponentExtensionHook_TakesItsSlot_AndMakesTheRestUnknown()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Hooks;
            using static Microsoft.UI.Reactor.Factories;

            public sealed class RefsPage : Component
            {
                public override Element Render()
                {
                    var (focusRef, focus) = this.UseElementFocus();
                    var (query, setQuery) = UseState("");
                    return TextBlock("x");
                }
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);

        // UseElementFocus takes the Component, not the RenderContext, yet consumes slots
        // (how many is Reactor's business): it is a hook, and what follows it is unknown.
        Assert.Equal(
            $"0:focusRef@{LineOf(code, "this.UseElementFocus()")};?:query@{LineOf(code, "UseState(\"\")")}",
            Assert.Single(s_componentHooks.Matches(generated)).Groups["hooks"].Value);
    }

    [Fact]
    public void Hooks_AbstractOrVirtualCustomHook_MakesTheRestUnknown()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public abstract class AbstractHookPage : Component
            {
                protected abstract object UseCustom();
                public override Element Render()
                {
                    var a = UseCustom();
                    var b = UseRef(1);
                    return TextBlock("x");
                }
            }

            public class VirtualHookPage : Component
            {
                protected virtual object UseOverridable() => UseRef(0);
                public override Element Render()
                {
                    var v = UseOverridable();
                    var w = UseRef(2);
                    return TextBlock("y");
                }
            }

            public sealed class StaticHookPage : Component
            {
                private object UseFixed() => UseRef(3);
                public override Element Render()
                {
                    var s = UseFixed();
                    var t = UseRef(4);
                    return TextBlock("z");
                }
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);
        var hooks = s_componentHooks.Matches(generated).ToDictionary(
            static m => m.Groups["type"].Value, static m => m.Groups["hooks"].Value);

        // The body that runs is picked at run time: an override may take any number of slots.
        Assert.Equal($"0:a@{LineOf(code, "var a = UseCustom()")};?:b@{LineOf(code, "UseRef(1)")}", hooks["AbstractHookPage"]);
        Assert.Equal($"0:v@{LineOf(code, "var v = UseOverridable()")};?:w@{LineOf(code, "UseRef(2)")}", hooks["VirtualHookPage"]);
        // Positive control: a non-virtual custom hook is still counted (one UseRef).
        Assert.Equal($"0:s@{LineOf(code, "var s = UseFixed()")};1:t@{LineOf(code, "UseRef(4)")}", hooks["StaticHookPage"]);
    }

    [Fact]
    public void Source_FingerprintCoversAbsentFacts()
    {
        // Each pair keeps every call at the same line and column and differs in ONE fact being
        // absent: a declared name, or a render function's hook.
        const string named = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public static class Page
            {
                public static Element Build()
                {
                    var title = TextBlock("t");
                    return Memo(ctx => { var n = ctx.UseRef(0); return title; });
                }
            }
            """;
        const string hookless = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public static class Page
            {
                public static Element Build()
                {
                    var title = TextBlock("t");
                    return Memo(ctx => {                        return title; });
                }
            }
            """;

        static string FingerprintOf(string code)
        {
            var match = Regex.Match(SourceMapTransparentGeneratorTests.Run(code).GeneratedSource,
                @"b\.Source\(@""User\.cs"", @""(?<fp>[0-9a-f]{16})""\);", RegexOptions.CultureInvariant);
            Assert.True(match.Success, "no b.Source(...) for User.cs");
            return match.Groups["fp"].Value;
        }

        Assert.Equal(PositionOf(named, "TextBlock("), PositionOf(hookless, "TextBlock("));
        Assert.Equal(PositionOf(named, "Memo("), PositionOf(hookless, "Memo("));

        var a = FingerprintOf(named);
        Assert.Equal(a, FingerprintOf(named)); // deterministic
        Assert.NotEqual(a, FingerprintOf(hookless));
        // A discard at the same column: the call is still there, its name is not.
        var discarded = named.Replace("var title = TextBlock(\"t\");", "_         = TextBlock(\"t\");", StringComparison.Ordinal)
            .Replace("return title; });", "return null!; });", StringComparison.Ordinal);
        Assert.Equal(PositionOf(named, "TextBlock("), PositionOf(discarded, "TextBlock("));
        Assert.DoesNotContain("b.Name(", SourceMapTransparentGeneratorTests.Run(discarded).GeneratedSource, StringComparison.Ordinal);
        Assert.NotEqual(a, FingerprintOf(discarded));
    }

    [Fact]
    public void Source_FingerprintCoversHookFreeRootMounts()
    {
        // A file whose only Reactor call is a root mount building its output with a
        // constructor: no element factory and no hook, so before root mounts took part it
        // made no b.Source claim at all, and another assembly's hook-using root at the same
        // path, line and column lent it its hooks (StaticInfoTests covers the runtime side:
        // disagreeing claims make the facts unknown).
        const string hooked = """
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Hosting;

            namespace Microsoft.UI.Reactor.Hosting
            {
                public sealed class ReactorHost
                {
                    public void Mount(global::System.Func<RenderContext, Element> root) { }
                }
            }

            public static class App
            {
                public static void Start(ReactorHost host)
                    => host.Mount(ctx => { var n = ctx.UseRef(0); return new TextBlockElement("x"); });
            }
            """;
        var hookFree = hooked.Replace("var n = ctx.UseRef(0);", "                     ", StringComparison.Ordinal);
        Assert.Equal(PositionOf(hooked, "Mount(ctx"), PositionOf(hookFree, "Mount(ctx"));

        static string? FingerprintOf(string code)
        {
            var match = Regex.Match(SourceMapTransparentGeneratorTests.Run(code).GeneratedSource,
                @"b\.Source\(@""User\.cs"", @""(?<fp>[0-9a-f]{16})""\);", RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["fp"].Value : null;
        }

        // Positive control: the hook-using root is recorded where the runtime looks it up.
        var hookedOutput = SourceMapTransparentGeneratorTests.Run(hooked).GeneratedSource;
        var mount = PositionOf(hooked, "Mount(ctx");
        Assert.Contains($"b.RenderFunctionHooks(@\"User.cs\", {mount.Line}, {mount.Column}, ", hookedOutput, StringComparison.Ordinal);

        var withHooks = FingerprintOf(hooked);
        var withoutHooks = FingerprintOf(hookFree);
        Assert.NotNull(withHooks);
        Assert.NotNull(withoutHooks); // the hook-free root still claims its file
        Assert.NotEqual(withHooks, withoutHooks);
        Assert.DoesNotContain("b.RenderFunctionHooks(", SourceMapTransparentGeneratorTests.Run(hookFree).GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Hooks_ConditionalHook_MakesTheFollowingSlotsUnknown()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public static class Page
            {
                public static Element Build(bool flag) => Memo(ctx =>
                {
                    var first = ctx.UseRef(0);
                    if (flag) { var maybe = ctx.UseRef(1); }
                    var last = ctx.UseRef(2);
                    return TextBlock("x");
                });
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);

        Assert.Equal(
            $"0:first@{LineOf(code, "ctx.UseRef(0)")};1:maybe@{LineOf(code, "ctx.UseRef(1)")};?:last@{LineOf(code, "ctx.UseRef(2)")}",
            Assert.Single(s_functionHooks.Matches(generated)).Groups["hooks"].Value);
    }

    [Fact]
    public void Hooks_InsideCallbacksAndCustomHookBodies_AreNotRenderHooks()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public static class Hooks
            {
                public static int UseThing(RenderContext ctx) => ctx.UseRef(5);
            }

            public sealed class Widget : Component
            {
                public override Element Render()
                {
                    var (count, _) = UseState(0);
                    UseEffect(() => { var notAHook = UseRef(9); });
                    return TextBlock("w");
                }

                private void Helper() { var ignored = UseRef(3); }
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);

        // One owner (Widget). The custom hook's own body and the helper method are not
        // renders; the UseRef inside the effect callback is not a render-order hook.
        var match = Assert.Single(s_componentHooks.Matches(generated));
        Assert.Equal(
            $"0:count@{LineOf(code, "UseState(0)")};1:UseEffect@{LineOf(code, "UseEffect(")}",
            match.Groups["hooks"].Value);
        Assert.Empty(s_functionHooks.Matches(generated));
    }

    [Fact]
    public void Roots_ProjectAndSolutionDirectoriesAreRecorded()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;
            public static class Page { public static Element Build() => VStack(TextBlock("t")); }
            """;

        var (_, withSolution) = SourceMapTransparentGeneratorTests.Run(code, buildProperties: new Dictionary<string, string>
        {
            ["MSBuildProjectDirectory"] = @"C:\src\App",
            ["SolutionDir"] = @"C:\src\",
        });
        Assert.Contains(@"b.Roots(@""C:\src\App"", @""C:\src\"");", withSolution, StringComparison.Ordinal);

        // Outside a solution build MSBuild reports SolutionDir as "*Undefined*".
        var (_, noSolution) = SourceMapTransparentGeneratorTests.Run(code, buildProperties: new Dictionary<string, string>
        {
            ["MSBuildProjectDirectory"] = @"C:\src\App",
            ["SolutionDir"] = "*Undefined*",
        });
        Assert.Contains(@"b.Roots(@""C:\src\App"", null);", noSolution, StringComparison.Ordinal);

        // No project directory known: nothing to record, and no table when there is nothing else.
        var (_, none) = SourceMapTransparentGeneratorTests.Run(code);
        Assert.DoesNotContain("b.Roots(", none, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_EmitsNoStaticInfo()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;
            public static class Page { public static void Build() { var title = TextBlock("t"); } }
            """;

        var (_, enabled) = SourceMapTransparentGeneratorTests.Run(code);
        Assert.Contains("ModuleInitializer", enabled, StringComparison.Ordinal); // positive control

        var (_, disabled) = SourceMapTransparentGeneratorTests.Run(code, enabled: false);
        Assert.DoesNotContain("ModuleInitializer", disabled, StringComparison.Ordinal);
    }
}
