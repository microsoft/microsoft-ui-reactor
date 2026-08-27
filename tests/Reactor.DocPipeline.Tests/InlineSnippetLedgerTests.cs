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

    /// <summary>A first line that labels the block as a deliberate counterexample.</summary>
    private static readonly Regex CounterexampleLabel =
        new(@"(?im)^\s*//\s*(don'?t|wrong|bad|avoid|never|incorrect|anti-?pattern|❌)", RegexOptions.Compiled);

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

        // Every parameter must be a type, or a type followed by a name (with optional default).
        return parameters
            .Split(',')
            .Select(p => p.Trim())
            .All(p => p.Length > 0 && Regex.IsMatch(
                p,
                @"^(params\s+)?[A-Za-z_][\w.<>,\[\]\s]*\??(\s+[a-z_]\w*(\s*=\s*[^,]+)?)?$"));
    }

    /// <summary>
    /// Hand-typed examples that are allowed to stay prose, keyed by template, then by the block's
    /// first non-empty line. Each entry records why the block cannot be snippet-backed.
    /// </summary>
    /// <remarks>
    /// Keyed on content rather than line number so ordinary edits above a block do not silently
    /// invalidate — or silently re-authorise — an entry.
    /// </remarks>
    private static readonly Dictionary<string, Dictionary<string, string>> AllowedInlineExamples =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["dialogs-and-flyouts"] = new(StringComparer.Ordinal)
            {
                // A two-line syntax illustration of the `with { IsOpen = ... }` edge trigger.
                // Compiling it would mean inventing selection/commands/hasSelection scaffolding
                // that buries the one line the prose is pointing at.
                ["CommandBarFlyout(Border(selection), primaryCommands: commands)"] =
                    "Two-line `with { IsOpen }` syntax fragment; scaffolding it would obscure the point.",
            },

            ["theming-tokens"] = new(StringComparer.Ordinal)
            {
                // A counterexample that the prose, not a comment, marks as wrong: "This one looks
                // like it checks both themes and cannot." The whole lesson is that the assertion is
                // vacuous, so it must not be made to pass.
                ["[Theory]"] =
                    "Deliberately vacuous test shown as a trap; the prose (not a comment) labels it wrong.",
            },

            ["hooks"] = new(StringComparer.Ordinal)
            {
                ["public override Element Render()"] =
                    "Hook-order counterexample; the prose introduces it as the shape to avoid.",
            },

            ["hooks-internals"] = new(StringComparer.Ordinal)
            {
                ["public static (string Value, Action<string> Set) UseDebouncedText("] =
                    "Illustrative custom-hook sketch with no counterpart in src/ to point at.",
                ["var (count, setCount) = UseState(0);"] =
                    "Two user-code fragments explaining slot ordering; neither is a runnable program.",
            },

            ["reconciliation"] = new(StringComparer.Ordinal)
            {
                // Deliberate unstable-key counterexample paired with a compiled stable-key snippet.
                // Compiling it under the docs analyzer gate would either fail or require changing the
                // exact bug the prose is warning readers not to write.
                ["// Unstable: changing the title changes the key, remounts the card,"] =
                    "Deliberate unstable-key counterexample; the lesson depends on leaving the bad key visible.",
            },

            ["input-and-gestures"] = new(StringComparer.Ordinal)
            {
                // Labelled "// Before —" rather than "// Don't", so the heuristic does not catch it.
                ["// Before — escapes the declarative surface and bypasses trampoline dispatch."] =
                    "The 'before' half of a migration pair; compiling it would trip REACTOR_EVENT_001 by design.",
            },

            ["migration/050-optional-t"] = new(StringComparer.Ordinal)
            {
                // A migration guide's whole job is to show the shape that no longer compiles.
                ["// Before"] =
                    "Migration before/after pair: the 'before' half is the pre-Optional<T> API and cannot compile by design.",
            },

            ["analyzer-architecture"] = new(StringComparer.Ordinal)
            {
                // Intentional analyzer-diagnostic sample: both .Set shapes are shown because the rule
                // reports them. Moving them into a doc app would make the analyzer gate fail by design.
                ["// Both are lost when the pooled control is reused. Only the first one"] =
                    "Deliberate REACTOR_POOL_001 diagnostic sample; it should remain uncompiled prose.",
            },
        };

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
                if (AllowedInlineExamples.TryGetValue(topic, out var allowed)
                    && allowed.ContainsKey(block))
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

        var stale = AllowedInlineExamples
            .SelectMany(t => t.Value.Keys.Select(k => (Topic: t.Key, Block: k)))
            .Where(e => !live.Contains($"{e.Topic}\u0000{e.Block}"))
            .Select(e => $"{e.Topic}: {e.Block}")
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

        // ...and neither is a snippet-backed block.
        Assert.Empty(UnverifiedExamples("```csharp snippet=\"layout/demo\"\n```\n"));
    }

    /// <summary>
    /// The first non-empty line of every fenced C# block that is neither snippet-backed, a
    /// signature listing, nor a labelled counterexample.
    /// </summary>
    private static IEnumerable<string> UnverifiedExamples(string templateText)
        => CSharpFence.Matches(templateText)
            // Snippet-backed blocks are extracted from real code, so CI already compiles them.
            .Where(fence => !fence.Groups[1].Value.Contains("snippet="))
            .Select(fence => fence.Groups["body"].Value)
            // A signature/reference listing is not code to copy; see IsSignatureListing.
            .Where(body => !IsSignatureListing(body))
            .Where(body => !CounterexampleLabel.IsMatch(body))
            .Select(body => body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0))
            .Where(first => first is not null)!;

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
