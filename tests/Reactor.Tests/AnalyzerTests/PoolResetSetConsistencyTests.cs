using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Cross-source consistency tests for <see cref="PoolResetSetAnalyzer"/>
/// (<c>REACTOR_POOL_001</c>). Catches drift between three files:
///
///   1. <c>src/Reactor/Core/ElementPool.cs</c> — the FE-prop reset list in
///      <c>CleanElement(FrameworkElement)</c>.
///   2. <c>src/Reactor/Elements/ElementExtensions.cs</c> — the modifier methods
///      that survive pool reset.
///   3. <c>src/Reactor.Analyzers/PoolResetSetAnalyzer.cs</c> —
///      the <c>TrappedProperties</c> dictionary.
///
/// The bug we're guarding against: someone adds a new property reset to
/// <c>CleanElement</c> (because pooled controls were leaking that prop into
/// the next mount), there is already a modifier with the same name, but
/// nobody updates the analyzer — so <c>.Set(fe => fe.NewProp = ...)</c>
/// still silently loses values and there's no warning at edit time. The
/// invariant test below fails in that scenario and tells the developer
/// exactly what to add.
/// </summary>
public class PoolResetSetConsistencyTests
{
    /// <summary>
    /// FE properties that <c>CleanElement</c> resets but that we intentionally
    /// do NOT include in <see cref="PoolResetSetAnalyzer.TrappedProperties"/>.
    /// Add a new entry here (with a comment explaining why) only when the
    /// property genuinely has no clean modifier-based replacement.
    /// </summary>
    private static readonly Dictionary<string, string> IntentionallyExcluded =
        new(StringComparer.Ordinal)
        {
            // Modifier is .IsVisible(bool); .Set(...) writes Visibility (enum).
            // The codefix would need an enum→bool translation, so we exclude
            // it from the auto-fix set. A future analyzer with a custom
            // codefix could pick this up.
            { "Visibility", "different signature (enum vs bool via .IsVisible)" },

            // No exact-name modifier exists, and Reactor uses Tag internally
            // to attach its element record — user .Set writes here are wrong
            // for a different reason (TASK-060 / Reconciler.ClearElementTag).
            { "Tag", "framework-internal — Reactor stores its element record here" },

            // No matching modifier; transform pipeline goes through Animate /
            // Scale / Rotation / Translation modifiers instead.
            { "RenderTransform", "no modifier; use Scale/Rotation/Translation modifiers" },

            // No matching modifier; FlowDirection is set on the root via app
            // configuration, not via a per-element modifier.
            { "FlowDirection", "no modifier; root-level concern" },
        };

    [Fact]
    public void Every_TrappedProperty_Is_Reset_In_CleanElement()
    {
        var resetProps = ReadResetProperties();

        foreach (var prop in PoolResetSetAnalyzer.TrappedProperties.Keys)
        {
            Assert.True(
                resetProps.Contains(prop),
                $"'{prop}' is in PoolResetSetAnalyzer.TrappedProperties but is " +
                $"NOT reset in ElementPool.CleanElement. Either remove it from " +
                $"TrappedProperties or add a reset for it in CleanElement.");
        }
    }

    [Fact]
    public void Every_TrappedProperty_Has_A_Matching_Modifier()
    {
        var modifierNames = ReadModifierNames();

        foreach (var (prop, modifier) in PoolResetSetAnalyzer.TrappedProperties)
        {
            Assert.True(
                modifierNames.Contains(modifier),
                $"'{prop}' maps to modifier '.{modifier}(...)' in " +
                $"PoolResetSetAnalyzer.TrappedProperties, but no such " +
                $"extension method exists in ElementExtensions.cs. The " +
                $"codefix would produce code that doesn't compile.");
        }
    }

    [Fact]
    public void Every_Reset_Property_With_Matching_Modifier_Is_Tracked()
    {
        // This is the load-bearing invariant: if someone adds a new
        // property to CleanElement's reset list, and ElementExtensions already
        // has a same-named modifier, then PoolResetSetAnalyzer MUST flag
        // .Set writes to that property — otherwise the trap is silent.
        var resetProps = ReadResetProperties();
        var modifierNames = ReadModifierNames();
        var tracked = PoolResetSetAnalyzer.TrappedProperties.Keys;

        var missing = new List<string>();
        foreach (var prop in resetProps)
        {
            if (IntentionallyExcluded.ContainsKey(prop)) continue;
            if (!modifierNames.Contains(prop)) continue;
            if (tracked.Contains(prop)) continue;
            missing.Add(prop);
        }

        Assert.True(
            missing.Count == 0,
            "These properties are reset in ElementPool.CleanElement AND have " +
            "a matching '.PROP(...)' modifier in ElementExtensions.cs, but " +
            "are NOT in PoolResetSetAnalyzer.TrappedProperties: " +
            $"[{string.Join(", ", missing)}]. " +
            "Either add them to TrappedProperties (so REACTOR_POOL_001 fires " +
            "on .Set writes to them), or — if intentional — add them to " +
            "IntentionallyExcluded in this test with a documented reason.");
    }

    /// <summary>
    /// Table-driven exercise of every entry in <see cref="PoolResetSetAnalyzer.TrappedProperties"/>:
    /// for each, prove the analyzer fires on the corresponding <c>.Set</c>
    /// lambda. This keeps the regular-test count growing automatically as
    /// new entries land, instead of relying on hand-written per-prop tests.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTrappedProperties))]
    public async Task Analyzer_Fires_For_Every_TrappedProperty(string propName, string modifierName)
    {
        _ = modifierName; // not consumed here; pinned by Every_TrappedProperty_Has_A_Matching_Modifier
        var stubs = BuildStubs();
        var source = stubs + $@"
class C
{{
    void M()
    {{
        var el = new FakeElement();
        {{|REACTOR_POOL_001:el.Set(fe => fe.{propName} = default!)|}};
    }}
}}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    public static IEnumerable<object[]> AllTrappedProperties() =>
        PoolResetSetAnalyzer.TrappedProperties
            .Select(kvp => new object[] { kvp.Key, kvp.Value });

    // ── Source-scanning helpers ─────────────────────────────────────────

    /// <summary>
    /// Extract the set of property names reset in the FE-common block of
    /// <c>ElementPool.CleanElement</c> — from the method's opening brace up
    /// to (but not including) the <c>switch (fe)</c> that begins type-specific
    /// cleanup. Captures both <c>fe.PROP = ...</c> direct sets and
    /// <c>fe.ClearValue(FrameworkElement.PROPProperty)</c> calls.
    /// </summary>
    private static HashSet<string> ReadResetProperties()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "src", "Reactor", "Core", "ElementPool.cs");
        Assert.True(File.Exists(path), $"ElementPool.cs not found at {path}");
        var source = File.ReadAllText(path);

        var start = source.IndexOf("internal static void CleanElement(FrameworkElement fe)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not locate CleanElement signature in ElementPool.cs — has it been renamed?");
        var braceStart = source.IndexOf('{', start);
        Assert.True(braceStart > start, "CleanElement opening brace not found");
        var switchStart = source.IndexOf("switch (fe)", braceStart, StringComparison.Ordinal);
        Assert.True(switchStart > braceStart, "CleanElement layout changed — could not find 'switch (fe)' boundary");

        var commonBlock = source.Substring(braceStart, switchStart - braceStart);

        var props = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(commonBlock, @"\bfe\.(\w+)\s*="))
        {
            var name = m.Groups[1].Value;
            // ClearValue() is a method call, not a property reset — caught by the second regex below.
            if (name != "ClearValue") props.Add(name);
        }
        foreach (Match m in Regex.Matches(commonBlock,
            @"\bfe\.ClearValue\(\s*FrameworkElement\.(\w+)Property\s*\)"))
        {
            props.Add(m.Groups[1].Value);
        }
        return props;
    }

    /// <summary>
    /// Extract the set of modifier method names defined in
    /// <c>ElementExtensions.cs</c> — any <c>public static T Name&lt;T&gt;(this T el, ...)</c>.
    /// </summary>
    private static HashSet<string> ReadModifierNames()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "src", "Reactor", "Elements", "ElementExtensions.cs");
        Assert.True(File.Exists(path), $"ElementExtensions.cs not found at {path}");
        var source = File.ReadAllText(path);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(source,
            @"public\s+static\s+T\s+(\w+)\s*<T>\s*\(\s*this\s+T\s+\w+"))
        {
            names.Add(m.Groups[1].Value);
        }
        return names;
    }

    /// <summary>
    /// Build a stub C# preamble that declares <c>FakeElement</c> with a
    /// public field for every property in <c>TrappedProperties</c>, so the
    /// table-driven analyzer test can compile uniformly. Uses <c>object?</c>
    /// fields with <c>default!</c> assignment — analyzer matches on syntax,
    /// not types, so this is sufficient.
    /// </summary>
    private static string BuildStubs()
    {
        var fields = string.Join(
            "\n    ",
            PoolResetSetAnalyzer.TrappedProperties.Keys
                .Select(p => $"public object? {p};"));

        return $@"
using System;

#nullable enable

public class FakeElement
{{
    {fields}
    public FakeElement Set(Action<FakeElement> configure) {{ configure(this); return this; }}
}}
";
    }
}
