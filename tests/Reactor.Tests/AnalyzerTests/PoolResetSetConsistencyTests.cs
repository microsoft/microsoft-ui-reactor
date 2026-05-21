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

        var missing = resetProps
            .Where(prop =>
                !IntentionallyExcluded.ContainsKey(prop) &&
                modifierNames.Contains(prop) &&
                !tracked.Contains(prop))
            .ToList();

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
        // Path.Join (vs Path.Combine) avoids the "rooted segment silently
        // discards the base path" behavior flagged by CodeQL cs/path-combine.
        // All segments here are hardcoded literals, so the warning is a
        // false positive — but the equivalent Path.Join keeps the analyzer
        // quiet and is otherwise identical for non-rooted segments.
        var path = Path.Join(root!, "src", "Reactor", "Core", "ElementPool.cs");
        Assert.True(File.Exists(path), $"ElementPool.cs not found at {path}");
        var source = File.ReadAllText(path);

        // Locate `(internal|private|...) static void CleanElement(FrameworkElement <param>)`,
        // capturing the parameter name. Matching by signature shape — not by the
        // exact `(FrameworkElement fe)` string — keeps the test robust to harmless
        // renames or spacing changes.
        var sigMatch = Regex.Match(source,
            @"static\s+void\s+CleanElement\s*\(\s*FrameworkElement\s+(\w+)\s*\)");
        Assert.True(sigMatch.Success,
            "Could not locate CleanElement(FrameworkElement) signature in ElementPool.cs — has it been removed or had its type changed?");
        var paramName = sigMatch.Groups[1].Value;

        var braceStart = source.IndexOf('{', sigMatch.Index + sigMatch.Length);
        Assert.True(braceStart > sigMatch.Index, "CleanElement opening brace not found");

        // The FE-common block runs from the opening brace up to the first
        // `switch (<param>)` that starts the type-specific cleanup.
        var switchRegex = new Regex($@"\bswitch\s*\(\s*{Regex.Escape(paramName)}\s*\)");
        var switchMatch = switchRegex.Match(source, braceStart);
        Assert.True(switchMatch.Success,
            $"CleanElement layout changed — could not find 'switch ({paramName})' boundary after the opening brace.");

        var commonBlock = source.Substring(braceStart, switchMatch.Index - braceStart);

        // ClearValue() is a method call caught separately by the second regex;
        // filter it out of the direct-assignment match set. Both regexes use
        // the captured parameter name so renaming `fe` → `element` keeps working.
        var escapedParam = Regex.Escape(paramName);
        var directAssignments = Regex.Matches(commonBlock, $@"\b{escapedParam}\.(\w+)\s*=")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Where(name => name != "ClearValue");

        var clearValueProps = Regex.Matches(commonBlock,
                $@"\b{escapedParam}\.ClearValue\(\s*FrameworkElement\.(\w+)Property\s*\)")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value);

        return new HashSet<string>(directAssignments.Concat(clearValueProps), StringComparer.Ordinal);
    }

    /// <summary>
    /// Extract the set of modifier method names defined in
    /// <c>ElementExtensions.cs</c> — any <c>public static T Name&lt;T&gt;(this T el, ...)</c>.
    /// </summary>
    private static HashSet<string> ReadModifierNames()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        // Path.Join — see ReadResetProperties for the cs/path-combine rationale.
        var path = Path.Join(root!, "src", "Reactor", "Elements", "ElementExtensions.cs");
        Assert.True(File.Exists(path), $"ElementExtensions.cs not found at {path}");
        var source = File.ReadAllText(path);

        var names = Regex.Matches(source, @"public\s+static\s+T\s+(\w+)\s*<T>\s*\(\s*this\s+T\s+\w+")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value);

        return new HashSet<string>(names, StringComparer.Ordinal);
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
