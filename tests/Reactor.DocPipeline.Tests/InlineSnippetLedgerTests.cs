using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Keeps hand-typed C# out of the guides unless someone has written down why it belongs there.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect class.</b> A ` ```csharp snippet="topic/id" ` block is extracted from a real doc
/// app, so CI compiles it and the <c>docs-snippet-gate</c> job holds it to the same analyzer rules
/// a reader's own project uses. A plain ` ```csharp ` block is just text: nothing compiles it,
/// nothing analyses it, and it renders identically on the published page. Readers cannot tell the
/// two apart, so the unverified one is the more dangerous.
/// </para>
/// <para>
/// That gap shipped real defects — <c>testing.md</c> taught a <c>ProfileCard</c>/<c>Mount</c> API
/// that does not exist, <c>hooks-internals.md</c> read <c>Ref&lt;T&gt;.Value</c> when the property
/// is <c>.Current</c>, and <c>layout.md</c> referenced an undeclared <c>window</c>. All three
/// compiled for nobody and were found only by moving them into real code.
/// </para>
/// <para>
/// <b>What this test allows.</b> Two shapes are self-evidently not meant to compile and need no
/// ledger entry: a signature listing (no statement terminator or brace anywhere — a reference
/// table such as <c>Markdown(string markdown)</c>), and a block whose first line labels it a
/// counterexample (<c>// Don't</c>, <c>// Wrong</c>, <c>// Avoid</c>, …). Everything else is a
/// "copy this" example and must be snippet-backed, or listed in
/// <see cref="AllowedInlineExamples"/> with the reason it cannot be.
/// </para>
/// </remarks>
public class InlineSnippetLedgerTests
{
    private const string TemplatesRelative = "docs/_pipeline/templates";

    /// <summary>A fenced C# block, with the fence's info string.</summary>
    private static readonly Regex CSharpFence =
        new(@"(?m)^```csharp([^\r\n]*)\r?\n(?<body>.*?)^```", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// A first line that labels the block as a deliberate counterexample.
    /// </summary>
    /// <remarks>
    /// Anchored to the start of the body, not to every line. Under <c>RegexOptions.Multiline</c>
    /// a <c>// Don't</c> appearing anywhere — including a warning comment halfway down an
    /// otherwise copyable example — exempted the whole block from the gate.
    /// </remarks>
    private static readonly Regex CounterexampleLabel =
        new(@"(?i)^\s*//\s*(don'?t|wrong|bad|avoid|never|incorrect|anti-?pattern|❌)", RegexOptions.Compiled);

    /// <summary>
    /// Separates a reference table from a one-line example. Both can lack a statement terminator,
    /// so keying only on <c>;</c>/<c>{</c> silently exempted real "copy this" code such as
    /// <c>ForEach(items, item =&gt; Card(item).WithKey(item.Id))</c> — a hole a reviewer found in
    /// the first cut of this gate. A listing *declares* typed parameters; an example *passes*
    /// arguments, which is detectable without parsing C#.
    /// </summary>
    /// <summary>True when the block is a signature/reference listing rather than code to copy.</summary>
    private static bool IsSignatureListing(string body)
    {
        // Anything with a statement terminator or a block is real code.
        if (body.IndexOfAny([';', '{']) >= 0) return false;

        // Strip trailing `// ...` annotations, then join wrapped signatures back onto one line so a
        // multi-line declaration is judged whole rather than by its dangling `CommandBarFlyout(`.
        var lines = body.Split('\n')
            .Select(l => Regex.Replace(l, @"//.*$", string.Empty).TrimEnd())
            .Where(l => l.Trim().Length > 0)
            .ToList();

        if (lines.Count == 0) return true;   // comment-only block: no example to verify

        var joined = new List<string>();
        foreach (var line in lines)
        {
            // An indented line continues the declaration above it; a flush-left one starts a new.
            var continues = joined.Count > 0 && (line.StartsWith(' ') || line.StartsWith('\t'));
            if (continues) joined[^1] += " " + line.Trim();
            else joined.Add(line.Trim());
        }

        return joined.All(IsDeclaration);
    }

    /// <summary>
    /// True when a line reads as a declaration — <c>Name(...)</c> whose parameter list is types, or
    /// types followed by names, rather than passed values.
    /// </summary>
    private static bool IsDeclaration(string line)
    {
        var open = line.IndexOf('(');
        var close = line.LastIndexOf(')');
        if (open <= 0 || close <= open) return false;

        // A call passes values: string/number literals, lambdas, or member access on an argument.
        // Default values are stripped first, since `= "OK"` and `= 1` are declarations, not calls.
        var parameters = line[(open + 1)..close];
        var withoutDefaults = Regex.Replace(parameters, @"=\s*[^,]+", string.Empty);
        if (withoutDefaults.Contains("=>", StringComparison.Ordinal)) return false;
        if (Regex.IsMatch(withoutDefaults, @"""|\b\d")) return false;

        // An empty parameter list is a call, not a listing.
        if (parameters.Trim().Length == 0) return false;

        return parameters.Split(',').Select(p => p.Trim()).All(IsParameterDeclaration);
    }

    /// <summary>
    /// True when a single parameter reads as a *declaration* rather than a passed argument.
    /// </summary>
    /// <remarks>
    /// The previous rule accepted any identifier or dotted expression as a "type", so ordinary
    /// copyable calls such as <c>TextBlock(message)</c> and <c>Card(item.Title)</c> were classified
    /// as signature listings and skipped the ledger whenever they omitted a semicolon — a broad
    /// re-opening of the original <c>ForEach(items, item =&gt; ...)</c> hole. Two things separate a
    /// declaration from an argument without parsing C#: a declaration is a <c>Type name</c> pair,
    /// and a lone token is a type only if it looks like one. Member access is never a parameter
    /// declaration, and by C# convention a bare lowercase token is an argument, not a type.
    /// </remarks>
    private static bool IsParameterDeclaration(string parameter)
    {
        var p = Regex.Replace(parameter, @"^(params|ref|out|in)\s+", string.Empty).Trim();

        var equals = p.IndexOf('=');
        if (equals >= 0) p = p[..equals].TrimEnd();
        if (p.Length == 0) return false;

        // `Type name` — two tokens — is unambiguous, whatever the type looks like.
        if (Regex.IsMatch(p, @"^[A-Za-z_][\w.<>,\[\]\s]*[\w>\]?]\s+[a-z_]\w*$")) return true;

        // A lone token: member access is an argument, never a declaration.
        if (p.Contains('.', StringComparison.Ordinal)) return false;

        // Otherwise accept it only if it reads as a type: a built-in keyword, or a
        // PascalCase name (optionally generic / array / nullable). A bare lowercase
        // identifier is how a call passes a value.
        return BuiltInTypeNames.Contains(p)
            || Regex.IsMatch(p, @"^[A-Z]\w*(<[^>]*>)?(\[\])?\??$");
    }

    private static readonly HashSet<string> BuiltInTypeNames = new(StringComparer.Ordinal)
    {
        "bool", "byte", "sbyte", "char", "decimal", "double", "float", "int", "uint",
        "long", "ulong", "short", "ushort", "object", "string", "dynamic", "void",
    };

    /// <summary>
    /// Hand-typed examples that are allowed to stay prose, keyed by template, then by the block's
    /// full normalized text. Each entry records why the block cannot be snippet-backed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed on content rather than line number so ordinary edits above a block do not silently
    /// invalidate — or silently re-authorise — an entry.
    /// </para>
    /// <para>
    /// Keyed on the <em>whole</em> block rather than its first line, because openers are shared.
    /// <c>hooks-internals</c> has two distinct blocks that both begin
    /// <c>var (count, setCount) = UseState(0);</c>, and a single first-line entry authorised both —
    /// only one had ever been reviewed. Generic openers like <c>[Theory]</c>,
    /// <c>public override Element Render()</c> and <c>// Before</c> had the same latent reach.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, Dictionary<string, string>> AllowedInlineExamples =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["dialogs-and-flyouts"] = new(StringComparer.Ordinal)
            {
                // A two-line syntax illustration of the `with { IsOpen = ... }` edge trigger.
                // Compiling it would mean inventing selection/commands/hasSelection scaffolding
                // that buries the one line the prose is pointing at.
                ["""
                 CommandBarFlyout(Border(selection), primaryCommands: commands)
                     with { IsOpen = hasSelection }
                 """] =
                    "Two-line `with { IsOpen }` syntax fragment; scaffolding it would obscure the point.",
            },

            ["theming-tokens"] = new(StringComparer.Ordinal)
            {
                // A counterexample that the prose, not a comment, marks as wrong: "This one looks
                // like it checks both themes and cannot." The whole lesson is that the assertion is
                // vacuous, so it must not be made to pass.
                ["""
                 [Theory]
                 [InlineData(ElementTheme.Light)]
                 [InlineData(ElementTheme.Dark)]
                 public void StatusBanner_renders_in_both_themes(ElementTheme theme)
                 {
                     var tree = StatusBanner("Saved", InfoBarSeverity.Success)
                         .RequestedTheme(theme);

                     // Vacuous: the scanner inspects accessibility metadata — names,
                     // roles, labels — and never resolves a brush. Both cases return
                     // the same findings, so a banner hardcoded to a light-only colour
                     // passes just as happily as a correct one. The `theme` parameter
                     // does not reach anything this assertion reads.
                     Assert.Empty(AccessibilityScanner.Scan(tree));
                 }
                 """] =
                    "Deliberately vacuous test shown as a trap; the prose (not a comment) labels it wrong.",
            },

            ["hooks"] = new(StringComparer.Ordinal)
            {
                ["""
                 public override Element Render()
                 {
                     var (a, setA) = UseState(0);
                     if (a > 0)
                         UseEffect(() => { ... }, a);  // WRONG: conditional hook
                     return TextBlock($"{a}");
                 }
                 """] =
                    "Hook-order counterexample; the prose introduces it as the shape to avoid.",
            },

            ["hooks-internals"] = new(StringComparer.Ordinal)
            {
                ["""
                 public static (string Value, Action<string> Set) UseDebouncedText(
                     this RenderContext ctx, string initial, TimeSpan delay)
                 {
                     var (value, setValue) = ctx.UseState(initial);
                     var (debounced, setDebounced) = ctx.UseState(initial);
                     ctx.UseEffect(() =>
                     {
                         var cts = new CancellationTokenSource();
                         _ = Task.Delay(delay, cts.Token).ContinueWith(
                             _ => setDebounced(value),
                             TaskContinuationOptions.OnlyOnRanToCompletion);
                         return () => { cts.Cancel(); };
                     }, value);
                     return (debounced, setValue);
                 }
                 """] =
                    "Illustrative custom-hook sketch with no counterpart in src/ to point at.",

                // These two share a first line. Under the old first-line key a single entry
                // covered both, so only one was ever actually reviewed.
                ["""
                 var (count, setCount) = UseState(0);
                 var prevCount = UseRef(0);
                 var previous = prevCount.Current;
                 UseEffect(() => { prevCount.Current = count; }, count);
                 """] =
                    "Previous-value fragment explaining slot ordering; not a runnable program.",

                ["""
                 var (count, setCount) = UseState(0);
                 return showCounter ? Button($"{count}", () => setCount(count + 1)) : TextBlock("hidden");
                 """] =
                    "The unconditional-hook fix paired with the conditional counterexample above it; "
                    + "two lines of user code with no surrounding component to compile.",
            },

            ["reconciliation"] = new(StringComparer.Ordinal)
            {
                // Deliberate unstable-key counterexample paired with a compiled stable-key snippet.
                // Compiling it under the docs analyzer gate would either fail or require changing the
                // exact bug the prose is warning readers not to write.
                ["""
                 // Unstable: changing the title changes the key, remounts the card,
                 // loses any state attached via UseState inside Card
                 ForEach(rows, row => Card(row).WithKey(row.Title))
                 """] =
                    "Deliberate unstable-key counterexample; the lesson depends on leaving the bad key visible.",
            },

            ["input-and-gestures"] = new(StringComparer.Ordinal)
            {
                // Labelled "// Before —" rather than "// Don't", so the heuristic does not catch it.
                ["""
                 // Before — escapes the declarative surface and bypasses trampoline dispatch.
                 Rectangle().Set(r =>
                 {
                     r.PointerEntered += (_, _) => Hover();
                     r.PointerExited += (_, _) => Unhover();
                 });
                 """] =
                    "The 'before' half of a migration pair; compiling it would trip REACTOR_EVENT_001 by design.",
            },

            ["migration/050-optional-t"] = new(StringComparer.Ordinal)
            {
                // A migration guide's whole job is to show the shape that no longer compiles.
                ["""
                 // Before
                 int index = element.SelectedIndex;

                 // After: choose the intent
                 int tolerant = element.SelectedIndex.GetValueOrDefault(-1); // tolerate control-owned
                 int asserted = element.SelectedIndex.Value;                 // require HasValue
                 """] =
                    "Migration before/after pair: the 'before' half is the pre-Optional<T> API and cannot compile by design.",
            },

            ["analyzer-architecture"] = new(StringComparer.Ordinal)
            {
                // Intentional analyzer-diagnostic sample: both .Set shapes are shown because the rule
                // reports them. Moving them into a doc app would make the analyzer gate fail by design.
                ["""
                 // Both are lost when the pooled control is reused. Only the first one
                 // was diagnosed before the attached shape was added.
                 .Set(fe => fe.Margin = new Thickness(8))
                 .Set(fe => AutomationProperties.SetName(fe, "Save"))
                 """] =
                    "Deliberate REACTOR_POOL_001 diagnostic sample; it should remain uncompiled prose.",
            },
        };

    /// <summary>
    /// <see cref="AllowedInlineExamples"/> with every key run through <see cref="NormalizeBlock"/>.
    /// The ledger keys are raw string literals, so they carry this file's CRLF endings while
    /// template blocks are normalized to LF — comparing them raw makes every entry look stale.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> AllowedNormalized =
        AllowedInlineExamples.ToDictionary(
            topic => topic.Key,
            topic => new HashSet<string>(topic.Value.Keys.Select(NormalizeBlock), StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every hand-typed "copy this" example must be snippet-backed or explicitly excused.
    /// </summary>
    [Fact]
    public void Inline_Csharp_Examples_Are_Snippet_Backed_Or_Ledgered()
    {
        var offenders = new List<string>();

        foreach (var (topic, path) in Templates())
        {
            foreach (var block in UnverifiedExamples(File.ReadAllText(path)))
            {
                if (AllowedNormalized.TryGetValue(topic, out var allowed)
                    && allowed.Contains(block))
                {
                    continue;
                }

                offenders.Add($"{topic}.md.dt: {block}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These C# blocks are typed straight into a template, so nothing compiles them and the "
            + "docs-snippet-gate cannot see them -- yet they read as code to copy. Move each into a "
            + "doc app (```csharp snippet=\"<topic>/<id>\") or point at real repo source "
            + "(```csharp snippet=\"source:<path>#<region>\"). If it genuinely cannot be compiled, "
            + "add it to AllowedInlineExamples with the reason:\n  "
            + string.Join("\n  ", offenders.OrderBy(o => o, StringComparer.Ordinal)));
    }

    /// <summary>
    /// A ledger entry whose block is gone is stale permission that would silently excuse the next
    /// hand-typed example that happens to start with the same line.
    /// </summary>
    [Fact]
    public void Ledger_Entries_All_Still_Match_A_Block()
    {
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (topic, path) in Templates())
        {
            foreach (var block in UnverifiedExamples(File.ReadAllText(path)))
                live.Add($"{topic}\u0000{block}");
        }

        var stale = AllowedNormalized
            .SelectMany(t => t.Value.Select(k => (Topic: t.Key, Block: k)))
            .Where(e => !live.Contains($"{e.Topic}\u0000{e.Block}"))
            .Select(e => $"{e.Topic}: {e.Block.Split('\n')[0]}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These AllowedInlineExamples entries no longer match a block in their template -- the "
            + "example was converted or removed. Delete the entry so the ledger keeps matching "
            + "reality:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// Topic ids must be unique. <c>index.md.dt</c> exists at the templates root and again under
    /// <c>recipes/</c>, so a file-name-only key collapsed them — and a ledger entry written for one
    /// would then quietly excuse a block in the other.
    /// </summary>
    [Fact]
    public void Topic_Ids_Are_Unique_Across_Subdirectories()
    {
        var duplicates = Templates()
            .GroupBy(t => t.Topic, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            "These topic ids are ambiguous, so AllowedInlineExamples cannot target one template "
            + "without also excusing the other:\n  " + string.Join("\n  ", duplicates));

        // Positive control: the corpus really does contain a colliding *file name*, so this proves
        // the path-relative key solved something rather than passing on an empty set.
        var collidingFileNames = Templates()
            .GroupBy(t => t.Topic.Split('/')[^1], StringComparer.OrdinalIgnoreCase)
            .Count(g => g.Count() > 1);

        Assert.True(
            collidingFileNames > 0,
            "No template file name collides any more; if that is intentional this control can go, "
            + "but until then it is what makes the uniqueness assertion above meaningful.");
    }

    /// <summary>
    /// Floors the two facts above. Both pass by reporting nothing, which is indistinguishable from
    /// a scanner that reads no templates or a classifier that calls everything exempt.
    /// </summary>
    [Fact]
    public void Classifier_Actually_Reads_The_Templates_And_Can_Still_Fire()
    {
        var templates = Templates().ToList();
        Assert.True(
            templates.Count >= 60,
            $"Only {templates.Count} templates were scanned; this is no longer measuring the corpus.");

        // The corpus must still contain snippet-backed blocks, or the pipeline itself has changed
        // shape and this test is guarding nothing.
        var snippetBacked = templates.Sum(t =>
            CSharpFence.Matches(File.ReadAllText(t.Path)).Count(m => m.Groups[1].Value.Contains("snippet=")));
        Assert.True(snippetBacked >= 300, $"Expected the guides to still be snippet-heavy; found {snippetBacked}.");

        // Positive control: a plain "copy this" block IS reported...
        Assert.Single(UnverifiedExamples("```csharp\nvar x = Button(\"hi\", () => { });\n```\n"));

        // ...including a single expression with no statement terminator, which an earlier cut of
        // this classifier let through because it keyed only on ';' and '{'.
        Assert.Single(UnverifiedExamples(
            "```csharp\nForEach(items, item => Card(item).WithKey(item.Id))\n```\n"));

        // ...a signature listing is not (it declares typed parameters rather than passing values)...
        Assert.Empty(UnverifiedExamples("```csharp\nMarkdown(string markdown)\n```\n"));
        Assert.Empty(UnverifiedExamples(
            "```csharp\nRichEditBox(Optional<string> text = default, Action<string>? onTextChanged = null)\n```\n"));
        Assert.Empty(UnverifiedExamples("```csharp\nMenuFlyout(Element target, params MenuFlyoutItemBase[] items)\n```\n"));

        // ...a labelled counterexample is not...
        Assert.Empty(UnverifiedExamples("```csharp\n// Don't: this leaks.\nvar x = new Timer();\n```\n"));

        // ...and neither is a snippet-backed block with an empty body, which the assembler expands.
        Assert.Empty(UnverifiedExamples("```csharp snippet=\"layout/demo\"\n```\n"));

        // ...but a snippet= fence that still carries a hand-typed body IS reported: the assembler
        // only expands empty directives, so that text ships unverified.
        Assert.Single(UnverifiedExamples(
            "```csharp snippet=\"layout/demo\"\nvar x = Button(\"hi\", () => { });\n```\n"));

        // A counterexample label only exempts when it opens the block; a warning comment further
        // down must not launder the copyable code above it.
        Assert.Single(UnverifiedExamples(
            "```csharp\nvar x = Button(\"hi\", () => { });\n// Don't do the other thing.\n```\n"));
    }

    /// <summary>
    /// Every fenced C# block that is neither snippet-backed, a signature listing, nor a labelled
    /// counterexample, normalized so it can be used as a ledger key.
    /// </summary>
    /// <remarks>
    /// This used to return only the block's *first non-empty line*. That made a ledger entry far
    /// broader than the block it was written to excuse: keys like <c>[Theory]</c>,
    /// <c>public override Element Render()</c>, <c>// Before</c> and
    /// <c>var (count, setCount) = UseState(0);</c> are openers many unrelated blocks share, so one
    /// entry silently authorised every future block in that topic starting the same way — and
    /// <see cref="Ledger_Entries_All_Still_Match_A_Block"/> still passed, because *a* block matched.
    /// Keying on the whole block makes each entry excuse exactly one example.
    /// </remarks>
    private static IEnumerable<string> UnverifiedExamples(string templateText)
        => CSharpFence.Matches(templateText)
            // A snippet= fence is only expanded by DocAssembler when its body is EMPTY. A fence
            // carrying both the attribute and hand-typed text is never expanded, so the page keeps
            // the hand-typed copy and the app marker it names is dead — exactly the drift this gate
            // exists to catch. Treating it as verified because the attribute is present was a hole
            // that three real fences (charting/imports, data-system/imports,
            // dialogs-and-flyouts/right-click-list-row) were sitting in.
            .Where(fence => !(fence.Groups[1].Value.Contains("snippet=")
                              && fence.Groups["body"].Value.Trim().Length == 0))
            .Select(fence => fence.Groups["body"].Value)
            // A signature/reference listing is not code to copy; see IsSignatureListing.
            .Where(body => !IsSignatureListing(body))
            .Where(body => !CounterexampleLabel.IsMatch(body))
            .Select(NormalizeBlock)
            .Where(block => block.Length > 0);

    /// <summary>
    /// Canonical form of a fenced block: LF endings, no trailing whitespace per line, no leading or
    /// trailing blank lines. Interior indentation is preserved because it is part of the example.
    /// </summary>
    internal static string NormalizeBlock(string body)
    {
        var lines = body.ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines);
    }

    private static IEnumerable<(string Topic, string Path)> Templates()
    {
        var repoRoot = FindRepoRoot();
        var dir = Path.Join(repoRoot, TemplatesRelative.Replace('/', Path.DirectorySeparatorChar));

        foreach (var file in Directory.EnumerateFiles(dir, "*.md.dt", SearchOption.AllDirectories))
        {
            // Path-relative, not file name: `index.md.dt` exists at the root *and* under
            // recipes/, so keying by name alone collapsed two distinct templates into one topic —
            // which would let a ledger entry written for one silently excuse a block in the other,
            // and made offender messages ambiguous about which file to open.
            var relative = Path.GetRelativePath(dir, file).Replace(Path.DirectorySeparatorChar, '/');
            yield return (relative[..^".md.dt".Length], file);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir, "Reactor.slnx")) || Directory.Exists(Path.Join(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Reactor repo root not found from test cwd.");
    }
}
