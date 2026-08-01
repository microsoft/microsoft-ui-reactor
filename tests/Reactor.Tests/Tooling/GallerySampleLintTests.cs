using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// Source lint for the ReactorGallery sample pages. The gallery is the reference app, so a
/// card that silently renders nothing teaches the wrong pattern — and the five mistakes
/// guarded here are all statically detectable and all shipped at least once:
///
/// <list type="bullet">
/// <item>an <c>ItemsView</c> view builder returning a non-<c>ItemContainer</c> root, which
/// makes the whole page throw (<c>ItemsViewElement.GuardedViewBuilder</c>);</item>
/// <item><c>.Background(...)</c> on a shape, which the reconciler only applies to
/// Panel / Control / Border and therefore drops, rendering an invisible shape;</item>
/// <item>an <c>ms-appx:///</c> asset that either does not exist, is never copied to the
/// output folder, or is composed at runtime so it cannot be checked — all of which render a
/// blank image with no error of any kind;</item>
/// <item><c>new Uri(x)</c> on a runtime value, which throws <c>UriFormatException</c> out of
/// <c>Render()</c> the moment the value is half-typed and replaces the whole page with the
/// error boundary;</item>
/// <item>one <c>UseState</c> slot read by two <c>SampleCard</c>s, which makes the cards a
/// single control mirrored twice — driving one silently retargets the other, and the reader
/// copying either card out gets a snippet that never worked on its own.</item>
/// </list>
///
/// Roslyn parses the page sources directly — no gallery build, no WinUI objects, so this
/// stays in the headless unit tier. Each fact also asserts the scan actually found the
/// construct it polices, so deleting the sample would fail the test rather than pass it
/// vacuously.
/// </summary>
public sealed class GallerySampleLintTests
{
    /// <summary>
    /// The class doc above opens with a count word — the one this test matches as
    /// <c>the (\w+) mistakes</c> — and then lists them. Both halves are hand-maintained, and that
    /// opening line is the one place two branches each adding a rule edit the *same* line while
    /// their rule bodies land far enough apart to auto-merge in silence. Resolving that conflict
    /// by taking either side ships a number contradicting its own list, and nothing else in this
    /// file would notice.
    ///
    /// So: union the list, and let this fail if the word does not follow. It fires in both
    /// directions — a word bumped without an item, or an item added without the word.
    /// </summary>
    [Fact]
    public void ClassDoc_CountWordMatchesTheRuleList()
    {
        var path = ThisFile();
        Assert.True(global::System.IO.File.Exists(path), $"Could not read this file's own source at {path} — if it moved, update ThisFile().");

        var source = global::System.IO.File.ReadAllText(path);
        var declaration = source.IndexOf("public sealed class GallerySampleLintTests", global::System.StringComparison.Ordinal);
        Assert.True(declaration >= 0, "Could not locate the class declaration — this check reads the doc comment above it.");

        var doc = source[..declaration];
        var items = Regex.Matches(doc, "<item>").Count;
        var counted = Regex.Match(doc, @"the (\w+) mistakes");

        // Both halves have to be found before they can be compared: a doc reworded past these
        // patterns would otherwise leave this silently comparing "" to "" and passing.
        Assert.True(counted.Success, "Class doc no longer contains a 'the <word> mistakes' count — update this check alongside it.");
        Assert.True(items > 0, "Class doc no longer contains an <item> list — update this check alongside it.");

        Assert.Equal(NumberWord(items), counted.Groups[1].Value);
    }

    /// <summary>
    /// Spells a count the way the class doc does. The ceiling is twenty, and it is not arbitrary:
    /// the check captures the count with <c>(\w+)</c>, a single word, and English needs a hyphen
    /// from twenty-one on — which <c>\w</c> does not match. So twenty is exactly where the
    /// single-word assumption breaks, and past it the *pattern* is what needs widening, not this
    /// map. Falling back to digits instead would quietly force the doc to read "the 21 mistakes"
    /// mid-sentence, so this fails with the real remedy named instead.
    ///
    /// The throw is a signpost, not a tested path — five rules today, so nothing reaches it.
    /// </summary>
    static string NumberWord(int value) => value switch
    {
        1 => "one",
        2 => "two",
        3 => "three",
        4 => "four",
        5 => "five",
        6 => "six",
        7 => "seven",
        8 => "eight",
        9 => "nine",
        10 => "ten",
        11 => "eleven",
        12 => "twelve",
        13 => "thirteen",
        14 => "fourteen",
        15 => "fifteen",
        16 => "sixteen",
        17 => "seventeen",
        18 => "eighteen",
        19 => "nineteen",
        20 => "twenty",
        _ => throw new global::System.NotSupportedException(
            $"No single-word spelling for {value}. English hyphenates from twenty-one on, which the " +
            @"(\w+) capture in ClassDoc_CountWordMatchesTheRuleList cannot match — widen that pattern " +
            "alongside this map."),
    };

    /// <summary>
    /// Every spelling the map produces has to survive the capture that reads it back. A hyphenated
    /// or spaced word would still <em>match</em> <c>(\w+)</c> — it would just capture the first
    /// piece, so the check would end up comparing "twenty" against "twenty-one". That coupling
    /// between the map and the pattern is the whole reason the ceiling is twenty, so it is
    /// asserted rather than left as a comment.
    /// </summary>
    [Fact]
    public void NumberWord_SpellingsSurviveTheCountPatternThatReadsThemBack()
    {
        for (var count = 1; count <= 20; count++)
        {
            var word = NumberWord(count);
            var match = Regex.Match($"the {word} mistakes", @"the (\w+) mistakes");

            Assert.True(match.Success, $"'{word}' is not reachable by the count pattern at all.");
            Assert.Equal(word, match.Groups[1].Value);
        }

        // And the ceiling is a decision, not an accident: restoring a digit fallback fails here.
        Assert.Throws<global::System.NotSupportedException>(() => NumberWord(21));
    }

    /// <summary>
    /// This file's own source, located the way every other reader here locates the tree — by
    /// walking up from <c>AppContext.BaseDirectory</c> for <c>Reactor.slnx</c>.
    ///
    /// Deliberately *not* <c>[CallerFilePath]</c>, which bakes in the path the assembly was
    /// **compiled** at. That is the machine it was built on, not the machine it runs on: it passes
    /// locally and fails only in CI, where the build happens under a different root and the test
    /// dies on <c>D:\_\tests\…</c>. Measured, not hypothesised — this check failed exactly that way
    /// on its first CI run.
    /// </summary>
    static string ThisFile() =>
        global::System.IO.Path.Join(GallerySources.RepoRoot(), "tests", "Reactor.Tests", "Tooling", "GallerySampleLintTests.cs");

    // Source loading lives in GallerySources so the snippet-agreement lint next door reads the
    // same pages through the same code path.
    static string GalleryDir() => GallerySources.GalleryDir();

    static IReadOnlyList<(string Path, SyntaxNode Root)> Pages() => GallerySources.Pages();

    static string Rel(string absolute) => GallerySources.Rel(absolute);

    static string Where(string path, SyntaxNode node) => GallerySources.Where(path, node);

    static string? InvokedName(InvocationExpressionSyntax invocation) => GallerySources.InvokedName(invocation);

    /// <summary>
    /// Name of the call that *starts* a fluent chain: for <c>ItemContainer(x).Margin(4)</c>
    /// and <c>Rectangle().Size(8).Background("#fff")</c> this yields "ItemContainer" and
    /// "Rectangle". Returns null when the head is not a plain invocation.
    /// </summary>
    static string? ChainHeadName(ExpressionSyntax expression)
    {
        var current = expression;
        while (true)
        {
            switch (current)
            {
                case ParenthesizedExpressionSyntax paren:
                    current = paren.Expression;
                    continue;
                case WithExpressionSyntax with:
                    current = with.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppress:
                    current = suppress.Operand;
                    continue;
                case InvocationExpressionSyntax invocation:
                    if (invocation.Expression is MemberAccessExpressionSyntax member)
                    {
                        current = member.Expression;
                        continue;
                    }
                    return InvokedName(invocation);
                default:
                    return null;
            }
        }
    }

    /// <summary>Parameter count of a lambda, for either the simple or parenthesized form.</summary>
    static int ParameterCount(AnonymousFunctionExpressionSyntax lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax => 1,
        ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters.Count,
        AnonymousMethodExpressionSyntax a => a.ParameterList?.Parameters.Count ?? 0,
        _ => -1,
    };

    /// <summary>
    /// Strip parentheses and casts from an argument so <c>(Func&lt;T,int,Element&gt;)((i, n) =&gt; ...)</c>
    /// is still recognised as the lambda it is. Without this the lint fails closed on legal code.
    /// </summary>
    static ExpressionSyntax UnwrapArgument(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax paren => UnwrapArgument(paren.Expression),
        CastExpressionSyntax cast => UnwrapArgument(cast.Expression),
        _ => expression,
    };

    /// <summary>
    /// The <c>(item, index) =&gt; Element</c> view-builder lambdas of an <c>ItemsView(...)</c> call.
    /// Selected by parameter count rather than position: <c>ItemsViewElement&lt;T&gt;</c> has exactly one
    /// two-parameter delegate (<c>ViewBuilder</c>) — <c>KeySelector</c>, <c>OnItemInvoked</c> and
    /// <c>OnSelectionChanged</c> all take one — so the count is unambiguous, while picking "the last
    /// lambda" would grab the key selector whenever the builder is passed as a method group.
    /// </summary>
    static IReadOnlyList<AnonymousFunctionExpressionSyntax> ViewBuilders(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments
            .Select(a => UnwrapArgument(a.Expression))
            .OfType<AnonymousFunctionExpressionSyntax>()
            .Where(l => ParameterCount(l) == 2)
            .ToList();

    /// <summary>
    /// The lambda or local function a statement actually belongs to. <c>DescendantNodes</c> walks the
    /// whole subtree, so without this a <c>return</c> inside a nested lambda or local helper declared
    /// in the builder body would be attributed to the builder itself and checked for an
    /// <c>ItemContainer</c> root it was never required to have.
    /// </summary>
    static SyntaxNode? OwningScope(SyntaxNode node) =>
        node.Ancestors().FirstOrDefault(a =>
            a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);

    /// <summary>Every expression a lambda can hand back, flattening conditional branches.</summary>
    static IEnumerable<ExpressionSyntax> ReturnedExpressions(AnonymousFunctionExpressionSyntax lambda)
    {
        var roots = new List<ExpressionSyntax>();
        if (lambda.ExpressionBody is { } body)
            roots.Add(body);
        if (lambda.Block is { } block)
            roots.AddRange(block.DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Where(r => ReferenceEquals(OwningScope(r), lambda))
                .Select(r => r.Expression)
                .OfType<ExpressionSyntax>());

        foreach (var root in roots)
            foreach (var leaf in Flatten(root))
                yield return leaf;

        static IEnumerable<ExpressionSyntax> Flatten(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax paren:
                    foreach (var e in Flatten(paren.Expression)) yield return e;
                    break;
                case ConditionalExpressionSyntax conditional:
                    foreach (var e in Flatten(conditional.WhenTrue)) yield return e;
                    foreach (var e in Flatten(conditional.WhenFalse)) yield return e;
                    break;
                case CastExpressionSyntax cast:
                    foreach (var e in Flatten(cast.Expression)) yield return e;
                    break;
                default:
                    yield return expression;
                    break;
            }
        }
    }

    // ── ItemsView view builders must return an ItemContainer root ────────────

    [Fact]
    public void ItemsView_ViewBuilders_ReturnItemContainerRoots()
    {
        var offenders = new List<string>();
        var checkedBuilders = 0;

        foreach (var (path, root) in Pages())
        {
            foreach (var invocation in root.DescendantNodes()
                         .OfType<InvocationExpressionSyntax>()
                         .Where(i => InvokedName(i) == "ItemsView"))
            {
                // The view builder is the (item, index) => Element lambda.
                var lambdas = ViewBuilders(invocation);

                if (lambdas.Count == 0)
                {
                    offenders.Add($"{Where(path, invocation)}: the ItemsView view builder is not a two-parameter " +
                                  "lambda, so this lint cannot verify its root. Pass it inline as " +
                                  "(item, index) => ItemContainer(...) rather than as a method group or a " +
                                  "parameterless delegate { } form.");
                    continue;
                }

                foreach (var builder in lambdas)
                foreach (var returned in ReturnedExpressions(builder))
                {
                    checkedBuilders++;
                    var head = ChainHeadName(returned);
                    if (head == "ItemContainer") continue;
                    offenders.Add(head is null
                        ? $"{Where(path, returned)}: could not statically verify the ItemsView view-builder root — " +
                          "return ItemContainer(...) directly from the builder so the requirement stays checkable."
                        : $"{Where(path, returned)}: ItemsView view builder returns {head}(...) — " +
                          "ItemsView requires an ItemContainer root, so the page throws at render.");
                }
            }
        }

        Assert.True(checkedBuilders > 0,
            "no ItemsView view-builder roots were inspected — the lint would pass vacuously.");
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    static InvocationExpressionSyntax ParseItemsViewCall(string call) =>
        CSharpSyntaxTree
            .ParseText($"class C {{ void M() {{ var x = {call}; }} }}",
                cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(i => InvokedName(i) == "ItemsView");

    /// <summary>
    /// The builder selection above decides whether the lint checks a call or fails it closed, so its
    /// own behaviour is pinned here against synthetic source. A cast- or parenthesis-wrapped lambda is
    /// legal C# that converts to <c>Func&lt;T,int,Element&gt;</c>; before <c>UnwrapArgument</c> existed
    /// those were seen as non-lambdas and the lint reported a bogus offender against correct code.
    /// </summary>
    [Theory]
    [InlineData("ItemsView(items, i => i.Key, (i, n) => ItemContainer(Text(i.Name)))", 1)]
    [InlineData("ItemsView(items, i => i.Key, ((i, n) => ItemContainer(Text(i.Name))))", 1)]
    [InlineData("ItemsView(items, i => i.Key, (Func<Item, int, Element>)((i, n) => ItemContainer(Text(i.Name))))", 1)]
    [InlineData("ItemsView(items, i => i.Key, BuildRow)", 0)]
    [InlineData("ItemsView(items, i => i.Key, i => ItemContainer(Text(i.Name)))", 0)]
    public void ViewBuilderSelection_FindsWrappedLambdas_AndIgnoresOneParameterArguments(string call, int expected) =>
        Assert.Equal(expected, ViewBuilders(ParseItemsViewCall(call)).Count);

    /// <summary>
    /// Returns are collected with <c>DescendantNodes</c>, which walks the entire subtree — so a
    /// <c>return</c> belonging to a nested lambda or a local helper declared inside the builder would
    /// otherwise be read as one of the builder's own return paths and demanded to be an
    /// <c>ItemContainer</c>. That is a false failure against legal code, so the scoping is pinned here.
    /// </summary>
    [Theory]
    [InlineData("ItemsView(items, i => i.Key, (i, n) => ItemContainer(Text(i.Name)))", "ItemContainer")]
    [InlineData("ItemsView(items, i => i.Key, (i, n) => { return ItemContainer(Text(i.Name)); })", "ItemContainer")]
    [InlineData("ItemsView(items, i => i.Key, (i, n) => { var f = () => { return Text(i.Name); }; return ItemContainer(f()); })", "ItemContainer")]
    [InlineData("ItemsView(items, i => i.Key, (i, n) => { Element H() { return Text(i.Name); } return ItemContainer(H()); })", "ItemContainer")]
    [InlineData("ItemsView(items, i => i.Key, (i, n) => n == 0 ? ItemContainer(Text(i.Name)) : ItemContainer(Icon()))", "ItemContainer,ItemContainer")]
    public void ReturnedExpressions_AreScopedToTheBuilder_NotNestedLambdasOrLocalFunctions(string call, string expectedHeads)
    {
        var builder = Assert.Single(ViewBuilders(ParseItemsViewCall(call)));
        var heads = ReturnedExpressions(builder).Select(ChainHeadName).ToArray();

        Assert.Equal(expectedHeads, string.Join(",", heads));
    }

    // ── Shapes are painted with Fill, not Background ─────────────────────────

    static readonly string[] ShapeFactories = ["Rectangle", "Ellipse", "Line", "Path2D"];

    [Fact]
    public void Shapes_DoNotUseBackground()
    {
        var offenders = new List<string>();
        var inspectedShapeChains = 0;

        foreach (var (path, root) in Pages())
        {
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax member) continue;

                var head = ChainHeadName(member.Expression);
                if (head is null || !ShapeFactories.Contains(head)) continue;
                inspectedShapeChains++;

                var modifier = member.Name.Identifier.Text;
                if (modifier is "Background" or "Foreground")
                {
                    offenders.Add($"{Where(path, invocation)}: .{modifier}(...) on {head}(...) has no effect — " +
                                  "the reconciler only applies it to Panel / Control / Border. Shapes paint with .Fill(...) / .Stroke(...).");
                }
            }
        }

        Assert.True(inspectedShapeChains > 0,
            "no shape fluent chains were inspected — the lint would pass vacuously.");
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    // ── ms-appx assets must exist AND be copied to the output folder ─────────

    /// <summary>
    /// An <c>ms-appx:///</c> reference and whatever literal text follows it. The alternation's
    /// second arm matches an <em>empty</em> path when the scheme is immediately followed by a
    /// quote — i.e. <c>"ms-appx:///" + relative</c>, where nothing after the scheme is literal.
    /// Without it that form matches nothing at all and escapes the lint entirely. Prose mentions
    /// in comments are followed by a space, so they still match neither arm.
    /// </summary>
    static readonly Regex MsAppxLiteral = new(@"ms-appx:///(?<path>[^""'\s\\]+|(?=[""']))", RegexOptions.Compiled);

    /// <summary>
    /// True when the captured text cannot be a whole asset path, so the lint must say so rather
    /// than go looking for a file. Each arm is a real composition form: an interpolation hole,
    /// a capture that ran past the closing quote, a trailing <c>/</c> left by
    /// <c>"ms-appx:///Assets/" + name</c>, and an empty capture from <c>"ms-appx:///" + path</c>.
    /// </summary>
    static bool IsComposedAssetPath(string assetPath) =>
        assetPath.Length == 0
        || assetPath.Contains('{')
        || assetPath.Contains(')')
        || assetPath.EndsWith('/');

    /// <summary>
    /// Directory prefixes / exact files the gallery csproj copies next to the executable.
    /// <c>ms-appx:///</c> resolves against that folder for an unpackaged app, so an asset
    /// that is not a Content item is invisible at runtime no matter where it sits in the repo.
    /// </summary>
    static IReadOnlyList<string> CopiedContentEntries()
    {
        var csproj = Path.Join(GalleryDir(), "ReactorGallery.csproj");
        var text = File.ReadAllText(csproj);
        // Match Include= anywhere in the tag, not just as the first attribute — otherwise a
        // perfectly ordinary `<Content Update="…" Include="…">` silently drops out and the
        // asset lint reports "not copied to output" for a file that is.
        var entries = Regex.Matches(text, @"<Content\b[^>]*?\bInclude\s*=\s*""(?<inc>[^""]+)""")
            .Select(m => m.Groups["inc"].Value.Replace('\\', '/'))
            .Select(inc => inc.EndsWith("/**", global::System.StringComparison.Ordinal)
                ? inc[..^3] + "/"
                : inc)
            .ToList();

        Assert.True(entries.Count > 0, $"no <Content Include=...> items found in {Rel(csproj)}");
        return entries;
    }

    [Fact]
    public void MsAppxAssets_ExistAndAreCopiedToOutput()
    {
        var content = CopiedContentEntries();
        var offenders = new List<string>();
        var inspectedAssets = 0;

        foreach (var (path, root) in Pages())
        {
            foreach (var (assetPath, offset) in MsAppxLiteral.Matches(root.ToFullString())
                         .Select(m => (m.Groups["path"].Value, m.Index)))
            {
                inspectedAssets++;

                // A regex match has no syntax node, so map its offset back through the tree
                // to report file:line like the other two facts in this file do.
                var at = $"{Rel(path)}:{root.SyntaxTree.GetLineSpan(new TextSpan(offset, 1), TestContext.Current.CancellationToken).StartLinePosition.Line + 1}";

                // A composed / interpolated URI cannot be resolved statically, so it would
                // silently escape this lint — fail instead of quietly skipping it.
                if (IsComposedAssetPath(assetPath))
                {
                    offenders.Add($"{at}: ms-appx:///{assetPath} is composed at runtime, so this lint cannot " +
                                  "verify the asset ships. Use a literal ms-appx:/// path in gallery pages.");
                    continue;
                }

                if (!File.Exists(Path.Join(GalleryDir(), assetPath.Replace('/', Path.DirectorySeparatorChar))))
                {
                    offenders.Add($"{at}: ms-appx:///{assetPath} does not exist under samples/ReactorGallery — " +
                                  "the Image renders blank with no error.");
                    continue;
                }

                var covered = content.Any(entry => entry.EndsWith("/", global::System.StringComparison.Ordinal)
                    ? assetPath.StartsWith(entry, global::System.StringComparison.OrdinalIgnoreCase)
                    : string.Equals(assetPath, entry, global::System.StringComparison.OrdinalIgnoreCase));

                if (!covered)
                {
                    offenders.Add($"{at}: ms-appx:///{assetPath} exists in the repo but no " +
                                  "<Content Include=...> item copies it to the output folder, so it is missing at runtime.");
                }
            }
        }

        Assert.True(inspectedAssets > 0,
            "no ms-appx:/// asset literals were inspected — the lint would pass vacuously.");
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    /// <summary>
    /// The regex and the composed-path classifier together decide whether a reference is checked,
    /// reported as unverifiable, or skipped entirely — so each composition form is pinned against
    /// a source line. <c>expected</c> is <c>null</c> when nothing should match at all, which is the
    /// correct answer only for a prose mention of the scheme in a comment.
    /// </summary>
    [Theory]
    [InlineData(@"Image(""ms-appx:///Assets/SampleImages/Landscape.png"")", "Assets/SampleImages/Landscape.png", false)]
    [InlineData(@"Image(""""ms-appx:///Assets/SampleImages/Landscape.png"""")", "Assets/SampleImages/Landscape.png", false)]
    [InlineData(@"Image($""ms-appx:///Assets/{name}.png"")", "Assets/{name}.png", true)]
    [InlineData(@"Image(""ms-appx:///Assets/"" + fileName)", "Assets/", true)]
    [InlineData(@"Image(""ms-appx:///"" + relative)", "", true)]
    [InlineData("// what ms-appx:/// resolves against, next to the executable", null, false)]
    public void MsAppxDetection_ClassifiesEveryCompositionForm(string source, string? expected, bool composed)
    {
        var match = MsAppxLiteral.Match(source);

        if (expected is null)
        {
            Assert.False(match.Success, $"prose mention should not match: {source}");
            return;
        }

        Assert.True(match.Success, $"no ms-appx match in: {source}");
        Assert.Equal(expected, match.Groups["path"].Value);
        Assert.Equal(composed, IsComposedAssetPath(match.Groups["path"].Value));
    }

    // ── sibling SampleCards do not share one UseState slot ──────────────────

    /// <summary>
    /// The names bound by one <c>UseState</c> call. Deconstruction — the form every gallery page
    /// uses — parses as an assignment whose left side is a declaration expression, not as a local
    /// declaration, so both shapes have to be matched separately.
    /// </summary>
    static IEnumerable<List<string>> StateSlots(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                // var (x, setX) = UseState(...)
                case AssignmentExpressionSyntax
                {
                    Right: InvocationExpressionSyntax deconstructed,
                    Left: DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax parenthesized }
                } when InvokedName(deconstructed) is "UseState" or "UseReducer":
                {
                    var names = parenthesized.Variables
                        .OfType<SingleVariableDesignationSyntax>()
                        .Select(v => v.Identifier.Text)
                        .Where(n => n != "_")
                        .ToList();

                    if (names.Count > 0) yield return names;
                    break;
                }

                // var state = UseState(...) — the tuple kept whole, then read as state.Item1.
                case VariableDeclaratorSyntax { Initializer.Value: InvocationExpressionSyntax whole } declarator
                    when InvokedName(whole) is "UseState" or "UseReducer":
                {
                    yield return [declarator.Identifier.Text];
                    break;
                }
            }
        }
    }

    /// <summary>The outermost <c>SampleCard(...)</c> calls — one per card the reader sees.</summary>
    static List<InvocationExpressionSyntax> SampleCards(SyntaxNode root) =>
        root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(i => InvokedName(i) == "SampleCard")
            .Where(i => i.Ancestors().OfType<InvocationExpressionSyntax>().All(a => InvokedName(a) != "SampleCard"))
            .ToList();

    /// <summary>
    /// The names a node introduces <em>directly</em> into the scope it opens — lambda and
    /// anonymous-method parameters, a <c>foreach</c> variable, the locals declared straight inside a
    /// block or switch section, and a statement's pattern designations.
    /// <para>
    /// Deliberately not the whole subtree. A card that declares its own <c>url</c> is not touching a
    /// page-level slot of the same name — but that only holds <em>within that declaration's scope</em>.
    /// A card can read the page slot at its top level and separately bind the same name in a nested
    /// lambda, and collecting bindings subtree-wide drops the genuine read along with the shadowed
    /// one, reporting a coupled page as clean. Callers walk the ancestor chain of each identifier
    /// instead, so shadowing is decided per occurrence.
    /// </para>
    /// <para>
    /// The block and statement arms are deliberately <em>position-insensitive</em>: they return every
    /// local a block declares, not only those declared before the identifier being tested. That looks
    /// like a false-negative hole — an occurrence resolving outward past a same-named local declared
    /// later would be reported as shadowed — but the shape does not exist in source that compiles.
    /// C# reserves a simple name for one meaning per block (CS0136) and rejects a use that precedes
    /// its declaration (CS0841, or CS0844 when the later local hides a field), so the ambiguity the
    /// position check would resolve is a compile error before this lint ever parses it. Verified by
    /// compiling the four candidate spellings — outer local read from an inner block, the same inside
    /// a lambda body, a field read from a block that later declares the name, and a deconstruction
    /// slot read before a same-named local — plus a local function capturing a later local, which is
    /// CS0841 as well rather than the legal capture it looks like. Adding a <c>SpanStart</c> filter
    /// would therefore change no answer on any input this lint can receive, while reading as though
    /// it distinguishes something. If you are here to add one, produce a <em>compiling</em>
    /// counterexample first; if you have one, this paragraph is wrong and the filter belongs.
    /// </para>
    /// </summary>
    static IEnumerable<string> NamesIntroducedBy(SyntaxNode node) => node switch
    {
        SimpleLambdaExpressionSyntax lambda => [lambda.Parameter.Identifier.Text],
        ParenthesizedLambdaExpressionSyntax lambda =>
            lambda.ParameterList.Parameters.Select(parameter => parameter.Identifier.Text),
        AnonymousMethodExpressionSyntax anonymous =>
            anonymous.ParameterList is { } list
                ? list.Parameters.Select(parameter => parameter.Identifier.Text)
                : [],

        // A parameter binds its name for the whole body, so a field spelled the same way is not what
        // a site inside that body resolves to. Local functions are matched ahead of the general
        // statement arm below because `LocalFunctionStatementSyntax` *is* a `StatementSyntax` — the
        // same derived-before-base ordering `BlockSyntax` needs.
        BaseMethodDeclarationSyntax method =>
            method.ParameterList.Parameters.Select(parameter => parameter.Identifier.Text),
        LocalFunctionStatementSyntax localFunction =>
            localFunction.ParameterList.Parameters.Select(parameter => parameter.Identifier.Text),
        ForEachStatementSyntax loop => [loop.Identifier.Text],
        BlockSyntax block => DeclaredLocals(block.Statements),
        SwitchSectionSyntax section => DeclaredLocals(section.Statements),

        // Two binder kinds that introduce a name without declaring a local, a field or a
        // designation, so a walk built from those three alone never sees them and an outer constant
        // of the same name wins by default — the fail-open direction, in the rule written to catch
        // exactly that class of mistake.
        //
        // `catch (UriFormatException url)` is legal beside a type-level `const string url`, and an
        // exception variable concatenates to a string, so it does reach a `new Uri(...)` argument as
        // a bare identifier. Checked against the compiler rather than assumed: the first repro used
        // `url.Message`, which the rule rejects as a member access on its own, so it passed whether
        // or not the binder was understood and would have pinned nothing.
        CatchClauseSyntax catchClause =>
            catchClause.Declaration?.Identifier.Text is { Length: > 0 } caught ? [caught] : [],

        // Range variables (`from url in`, `let url =`, `join … into url`, `into url`) bind for the
        // rest of the query and are none of the three either.
        QueryExpressionSyntax query => RangeVariablesOf(query),

        // Designations (`is { } v`, `out var v`, deconstruction) scope to the enclosing statement.
        // Nested lambdas are skipped: a designation inside one belongs to the scope that lambda
        // opens, and the walk visits that scope in its own right on the way up.
        StatementSyntax statement => statement
            .DescendantNodes(descend => descend is not AnonymousFunctionExpressionSyntax)
            .OfType<SingleVariableDesignationSyntax>()
            .Select(designation => designation.Identifier.Text),

        _ => [],
    };

    /// <summary>
    /// The nearest node that opens a declaration scope around <paramref name="node"/> — a block, a
    /// switch section, or the member itself when nothing narrower encloses it.
    /// </summary>
    static SyntaxNode? ScopeOf(SyntaxNode node) =>
        node.Ancestors().FirstOrDefault(ancestor =>
            ancestor is BlockSyntax or SwitchSectionSyntax or MemberDeclarationSyntax);

    /// <summary>
    /// Every range variable a query introduces — <c>from</c>, <c>let</c>, <c>join</c>, a join's
    /// <c>into</c>, and a continuation's <c>into</c>. Nested lambdas and nested queries are skipped:
    /// each opens its own scope and the ancestor walk visits it in its own right.
    /// </summary>
    static IEnumerable<string> RangeVariablesOf(QueryExpressionSyntax query) =>
        new[] { query.FromClause.Identifier.Text }.Concat(query.Body
            .DescendantNodesAndSelf(descend =>
                descend is not (AnonymousFunctionExpressionSyntax or QueryExpressionSyntax))
            .SelectMany<SyntaxNode, string>(node => node switch
            {
                FromClauseSyntax from => [from.Identifier.Text],
                LetClauseSyntax let => [let.Identifier.Text],
                JoinClauseSyntax join => [join.Identifier.Text],
                JoinIntoClauseSyntax joinInto => [joinInto.Identifier.Text],
                QueryContinuationSyntax continuation => [continuation.Identifier.Text],
                _ => [],
            }));

    static IEnumerable<string> DeclaredLocals(SyntaxList<StatementSyntax> statements) =>
        statements.SelectMany(statement => statement switch
        {
            LocalDeclarationStatementSyntax local =>
                local.Declaration.Variables.Select(variable => variable.Identifier.Text),

            // `var (url, setUrl) = UseState(...)` — the canonical Reactor slot — is not a
            // `LocalDeclarationStatementSyntax` at all. Roslyn models a deconstruction as an
            // assignment whose left side is a `DeclarationExpressionSyntax`, so a scan for
            // declarators misses every slot the gallery actually declares. Nested lambdas are
            // skipped: a designation inside one belongs to the scope that lambda opens, and the
            // ancestor walk visits that scope in its own right.
            ExpressionStatementSyntax expression =>
                expression.DescendantNodes(descend => descend is not AnonymousFunctionExpressionSyntax)
                    .OfType<DeclarationExpressionSyntax>()
                    .SelectMany(declaration => declaration.Designation.DescendantNodesAndSelf()
                        .OfType<SingleVariableDesignationSyntax>())
                    .Select(designation => designation.Identifier.Text),

            _ => [],
        });

    /// <summary>
    /// Whether this particular occurrence of an identifier resolves to a binding introduced somewhere
    /// between it and <paramref name="root"/>, rather than to the page-level name it spells.
    /// </summary>
    static bool IsShadowedWithin(IdentifierNameSyntax identifier, SyntaxNode root)
    {
        var name = identifier.Identifier.Text;

        for (var node = identifier.Parent; node is not null; node = node.Parent)
        {
            if (NamesIntroducedBy(node).Contains(name, global::System.StringComparer.Ordinal))
                return true;

            if (ReferenceEquals(node, root)) break;
        }

        return false;
    }

    /// <summary>
    /// Every name a card reaches, following locals declared beside it to a fixed point. A page that
    /// lifts a card's body out — <c>var first = WebView2(url);</c> then <c>SampleCard("one", first, …)</c>
    /// — mentions only <c>first</c> inside the card, so a detector reading the card subtree alone
    /// would report the page clean while it still shares <c>url</c> with its neighbour.
    /// </summary>
    static HashSet<string> ReachableNames(InvocationExpressionSyntax card)
    {
        var reachable = new HashSet<string>(
            card.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(identifier => !IsShadowedWithin(identifier, card))
                .Select(identifier => identifier.Identifier.Text),
            global::System.StringComparer.Ordinal);

        var member = card.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (member is null) return reachable;

        // Locals of the enclosing member that are declared outside every card — the ones a card can
        // name without containing them.
        //
        // Filtered to declarations whose scope encloses *this* card, because a name does not map to
        // one declarator: two disjoint sibling blocks may each declare it (legal C#, no CS0136,
        // since the scopes never overlap). Choosing between them by document order chooses a
        // declarator the card may not be able to see, and then the widening pass reads the wrong
        // initializer — the card stops reaching the page slot and a coupled page reports clean.
        // Fail-open, in the guard for issue #982, one level out from the card scan. `Last()` is not
        // the fix either: it just moves which of the two orderings breaks, which is why the scope
        // filter is pinned below by a *pair* of rows, decoy-first and decoy-second.
        //
        // The tie-break after it is defensive, and measured to be unreachable rather than assumed
        // so: `member` above is the enclosing *method*, so a field is never a candidate, and two
        // locals that both enclose this card are necessarily in nested scopes — where CS0136
        // forbids them sharing a name. Replacing the tie-break with a throw-on-ambiguity ran clean
        // over every row here and over the real gallery tree, so each group holds exactly one
        // declarator today. Kept deterministic (innermost = the scope that starts last) so that if
        // some future shape does reach it, it picks the one C# would rather than an arbitrary one.
        // Note this means no row here pins the *ordering* — flipping it changes nothing, by design.
        var lifted = member.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(v => v.Initializer is not null)
            .Where(v => v.Ancestors().OfType<InvocationExpressionSyntax>().All(a => InvokedName(a) != "SampleCard"))
            .Where(v => ScopeOf(v) is { } scope && scope.Span.Contains(card.Span))
            .GroupBy(v => v.Identifier.Text, global::System.StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                // All candidates enclose the card, so they are nested in one another and the
                // innermost is the one that starts last.
                g => g.OrderByDescending(v => ScopeOf(v)!.SpanStart).First(),
                global::System.StringComparer.Ordinal);

        // A lifted local can be built from another, so widen until nothing new appears.
        bool grew;
        do
        {
            grew = false;

            // Snapshotted and materialised before the adds below: `reachable` is what we are
            // widening, so the pass has to read the set as it stood when the pass began.
            // One `TryGetValue` rather than `ContainsKey` + indexer, and the initializer is bound
            // by pattern: `lifted` is built from a `.Where(v => v.Initializer is not null)` ~15
            // lines up, but that filter cannot narrow the dictionary's *value type*, so reading
            // `.Initializer!.Value` here would be an unchecked assertion resting on a guard in a
            // different statement — the kind that survives an edit to the guard.
            var initializers = reachable.ToList()
                .SelectMany(name => lifted.TryGetValue(name, out var declarator)
                                    && declarator.Initializer?.Value is { } value
                    ? new[] { value }
                    : [])
                .ToList();

            foreach (var initializer in initializers)
            {
                // Same per-occurrence shadowing the card subtree gets above: a lambda parameter or
                // local bound *inside* the initializer is not the page-level slot that happens to
                // share its name. `Repeat(items, value => Text(value))` must not make a slot named
                // `value` reachable, or a page with no coupling at all reports as coupled — while an
                // initializer that both reads the slot and rebinds the name deeper still must.
                foreach (var id in initializer.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
                             .Where(id => !IsShadowedWithin(id, initializer)))
                    grew |= reachable.Add(id.Identifier.Text);
            }
        } while (grew);

        return reachable;
    }

    /// <summary>
    /// State slots read or written by more than one card. Each card is an independently
    /// copy-pasteable demo, so a slot spanning two of them means driving one silently retargets the
    /// other — the reader sees a control move that they did not touch (issue #982, and #980 for the
    /// pages that still do it).
    /// </summary>
    /// <remarks>
    /// Only the first clause is measured. This walks syntax and never renders, so the retargeting
    /// is an inference from the shared slot plus the independently-copy-pasteable premise, not an
    /// observation — #982 and #980 are where it was actually observed. The premise is what makes
    /// the inference sound and what makes "split the slot" the right remedy rather than one of
    /// several, so callers reporting this to a human should carry it rather than assert the
    /// consequence bare.
    /// </remarks>
    static IReadOnlyList<(string Names, List<InvocationExpressionSyntax> Cards)> CrossCardState(SyntaxNode root)
    {
        var cards = SampleCards(root);
        if (cards.Count < 2) return [];

        var reachable = cards.ToDictionary(c => c, ReachableNames);
        var shared = new List<(string, List<InvocationExpressionSyntax>)>();

        foreach (var slot in StateSlots(root))
        {
            var touched = cards.Where(card => slot.Any(reachable[card].Contains)).ToList();

            if (touched.Count > 1) shared.Add((string.Join("/", slot), touched));
        }

        return shared;
    }

    [Fact]
    public void WebView2Page_CardsDoNotShareState()
    {
        var (path, root) = Pages().Single(p => p.Path.EndsWith("WebView2Page.cs", global::System.StringComparison.Ordinal));

        // Issue #982 was two cards driven by one slot, so a page that stopped presenting two cards
        // — or whose slots stopped being found — would satisfy the real assertion for the wrong
        // reason. Both counts are what makes the sharing question askable at all.
        Assert.True(SampleCards(root).Count >= 2, $"{Rel(path)} no longer has two SampleCards to compare.");
        Assert.True(StateSlots(root).Count() >= 2, $"{Rel(path)} no longer has two UseState slots to compare.");

        var shared = CrossCardState(root)
            .Select(s => $"{Where(path, s.Cards[0])}: `{s.Names}` is read by {s.Cards.Count} cards " +
                         $"(lines {string.Join(", ", s.Cards.Select(c => c.GetLocation().GetLineSpan().StartLinePosition.Line + 1))})")
            .ToList();

        Assert.True(shared.Count == 0,
            "Issue #982: the preset buttons in the lower card silently retargeted the WebView2 in the " +
            "upper one, because both read the same UseState slot. Each card is copy-pasteable on its " +
            "own, so each needs its own state.\n" + string.Join("\n", shared));
    }

    /// <summary>
    /// Pages that predate the rule, tracked by issue #980. An explicit set rather than a count: a
    /// count stays green when one page is fixed and another regresses in the same change, which is
    /// exactly the drift the rule exists to stop. A floor and a ceiling each miss one of those two
    /// directions too.
    /// <para>
    /// Pinned in <em>both</em> directions: an offender missing from the list fails, and a list
    /// entry with no matching offender fails too. An upper bound alone was the earlier design,
    /// on the reasoning that #980 drains this list in a separate change and an equality pin would
    /// redden the moment that lands — on a branch whose own checks nothing re-runs, so the break
    /// would surface on main rather than on either PR. That trade is the wrong way round. Every
    /// page listed here is one #980 fixes, so a stale entry is this list's expected end state
    /// rather than an edge case, and an entry that outlives its offender is indistinguishable
    /// from a permanent suppression of the exact page-and-slot pair the rule exists to protect.
    /// The red is the draining mechanism: it names the lines to delete, is discharged by deleting
    /// them, and cannot recur once they are gone — whereas a masked re-regression is silent and
    /// survives forever. Both directions the rule is for are kept either way: a page that is not
    /// on the list fails, and so does a compensating edit that fixes one page while breaking
    /// another — not because of anything on this list, which such an edit leaves consistent, but
    /// because the new offender is absent from <see cref="SharedStateDebtAtIntroduction"/>.
    /// </para>
    /// <para>
    /// This does not make the test vacuous when the list drains to nothing: non-vacuity here comes
    /// from <see cref="CrossCardRule_ReportsOnlySlotsSpanningCards"/> driving the detector over
    /// synthetic source, plus the multi-card floor below — neither of which depends on the tree
    /// still containing offenders.
    /// </para>
    /// <para>
    /// At zero, emptying this array is on its own enough to keep the tree green — measured against
    /// the real combined tree rather than reasoned about: merging the fix branch with this array
    /// intact fails exactly one test, <see cref="SampleCards_SharedStateDoesNotSpread"/> via the
    /// staleness arm, naming the entries; emptying it and nothing else takes that to
    /// <c>Failed: 0</c>, with <c>Skipped: 0</c> in both legs. Absolute pass counts are deliberately
    /// omitted: the pair was measured twice, hours apart, on the same two branches, and the totals
    /// differed by five as the other branch added tests while every load-bearing term — which test,
    /// which arm, the 1→0, the <c>Skipped: 0</c> — was identical. Only the delta is evidence; a
    /// total is a scalar summary of a set and can be satisfied by the wrong members.
    /// </para>
    /// <para>
    /// The rest of the apparatus is inert once <c>offenders</c> is empty <em>and this array has been
    /// emptied with it</em> — both conditions, not just the first. The staleness arm runs the other
    /// way round, <c>KnownSharedStatePages.Except(offenders)</c>, so an empty <c>offenders</c>
    /// measured against a populated ledger hands back all fourteen entries: that is the single red
    /// measured above, not a trivially satisfied arm. Stated with only the first condition the
    /// sentence is false, and false in the direction that reads as "nothing here is required" — which
    /// is how it was in fact read by the first person to check it against the code.
    /// </para>
    /// <para>
    /// Sharper, and not dependent on <c>offenders</c> reaching exactly zero: <c>stale</c> is empty
    /// only while every entry still names a live offender, i.e. while <c>KnownSharedStatePages</c> is
    /// a subset of <c>offenders</c> — which is to say, only while nothing has been fixed.
    /// <em>Any</em> partial fix reddens it. So there is no state except "no page was ever fixed" in
    /// which emptying this array is optional, and that, rather than the measured red alone, is why
    /// the emptying belongs in the same commit as the fix.
    /// </para>
    /// <para>
    /// <em>Emptying</em> this array is required, per the paragraphs above. <em>Deleting</em> the
    /// emptied declaration and the arms around it is cleanup, not a correctness requirement. Two
    /// different edits; collapsing them into one reading is what makes this section look
    /// self-contradictory, and the list below is the second edit only. The distinction is
    /// recorded because stating it the other way round is what makes a merge instruction rot: a
    /// required-looking list whose items stop existing (this one already outlived a size ceiling and
    /// a <c>StaysClosed</c> fact) still parses as true, and the reader cannot tell which items were
    /// load-bearing. The cleanup is: this array's emptied declaration,
    /// <see cref="SharedStateDebtAtIntroduction"/>, all
    /// three <c>Except</c> arms, and the ambiguity arm — which exists only because <c>Except</c>
    /// dedups, so with no ledger to dedup through it guards nothing that <c>offenders.Count == 0</c>
    /// does not already catch. What remains is that single assertion plus the vacuity floors.
    /// </para>
    /// <para>
    /// Do it in the same commit as the fix that empties it. The intermediate state — pages fixed,
    /// entries still listed — is the red measured above, so splitting it across two commits puts
    /// that red on <c>main</c> rather than on either branch; and an emptied list left standing is a
    /// suppression mechanism with nothing in it, which is an invitation to refill rather than a
    /// fixed rule.
    /// </para>
    /// </summary>
    static readonly string[] KnownSharedStatePages =
    [
        "samples/ReactorGallery/ControlPages/BasicInput/NumberBoxPage.cs: value/setValue",
        "samples/ReactorGallery/ControlPages/BasicInput/RatingControlPage.cs: rating/setRating",
        "samples/ReactorGallery/ControlPages/DateAndTime/CalendarDatePickerPage.cs: date/setDate",
        "samples/ReactorGallery/ControlPages/DateAndTime/DatePickerPage.cs: date/setDate",
        "samples/ReactorGallery/ControlPages/DateAndTime/TimePickerPage.cs: time/setTime",
        "samples/ReactorGallery/ControlPages/DialogsAndFlyouts/CommandBarFlyoutPage.cs: lastAction/setLastAction",
        "samples/ReactorGallery/ControlPages/DialogsAndFlyouts/MenuFlyoutPage.cs: lastAction/setLastAction",
        "samples/ReactorGallery/ControlPages/Layout/StackPanelPage.cs: spacing/setSpacing",
        "samples/ReactorGallery/ControlPages/MenusAndToolbars/CommandBarPage.cs: lastAction/setLastAction",
        "samples/ReactorGallery/ControlPages/MenusAndToolbars/MenuBarPage.cs: lastAction/setLastAction",
        "samples/ReactorGallery/ControlPages/Navigation/NavigationViewPage.cs: selectedTag/setSelectedTag",
        "samples/ReactorGallery/ControlPages/Text/AutoSuggestBoxPage.cs: query/setQuery",
        "samples/ReactorGallery/ControlPages/Text/RichEditBoxPage.cs: charCount/setCharCount",
        "samples/ReactorGallery/ControlPages/Text/RichEditBoxPage.cs: text/setText",
    ];

    /// <summary>
    /// The same 14 pairs, frozen as the debt that existed when this rule was written (#982).
    /// <see cref="KnownSharedStatePages"/> is the live ledger; this does not move, and is compared
    /// against the <em>tree</em> rather than against the ledger.
    /// <para>
    /// The ledger does not drain an entry at a time. That is worth stating because "shrinks as pages
    /// get fixed" is the natural reading of a mutable allowlist and it is wrong here: #980's fix
    /// branch rewrites all thirteen distinct files this array names, so the entire ledger goes stale
    /// on a single merge. Measured rather than predicted — swapping that branch's copies of those
    /// thirteen files into this tree fails <see cref="SampleCards_SharedStateDoesNotSpread"/> with
    /// the staleness arm naming all fourteen entries, and restoring the files takes it back to
    /// green. The merge instruction above is therefore all-or-nothing; an entry-at-a-time reading of
    /// it leaves a red tree.
    /// </para>
    /// <para>
    /// Two identical arrays look redundant and are not: the point is that no single edit reaches
    /// both. Every arm that consults only the ledger can be satisfied by editing the ledger — add
    /// the page you just broke and the tree-vs-ledger arms agree again. Checking offenders against
    /// a record the ledger cannot influence is the only shape where suppressing a new defect
    /// requires appending to a list that says, in its name, that it is history.
    /// </para>
    /// </summary>
    static readonly string[] SharedStateDebtAtIntroduction =
    [
        "samples/ReactorGallery/ControlPages/BasicInput/NumberBoxPage.cs: value/setValue",
        "samples/ReactorGallery/ControlPages/BasicInput/RatingControlPage.cs: rating/setRating",
        "samples/ReactorGallery/ControlPages/DateAndTime/CalendarDatePickerPage.cs: date/setDate",
        "samples/ReactorGallery/ControlPages/DateAndTime/DatePickerPage.cs: date/setDate",
        "samples/ReactorGallery/ControlPages/DateAndTime/TimePickerPage.cs: time/setTime",
        "samples/ReactorGallery/ControlPages/DialogsAndFlyouts/CommandBarFlyoutPage.cs: lastAction/setLastAction",
        "samples/ReactorGallery/ControlPages/DialogsAndFlyouts/MenuFlyoutPage.cs: lastAction/setLastAction",
        "samples/ReactorGallery/ControlPages/Layout/StackPanelPage.cs: spacing/setSpacing",
        "samples/ReactorGallery/ControlPages/MenusAndToolbars/CommandBarPage.cs: lastAction/setLastAction",
        "samples/ReactorGallery/ControlPages/MenusAndToolbars/MenuBarPage.cs: lastAction/setLastAction",
        "samples/ReactorGallery/ControlPages/Navigation/NavigationViewPage.cs: selectedTag/setSelectedTag",
        "samples/ReactorGallery/ControlPages/Text/AutoSuggestBoxPage.cs: query/setQuery",
        "samples/ReactorGallery/ControlPages/Text/RichEditBoxPage.cs: charCount/setCharCount",
        "samples/ReactorGallery/ControlPages/Text/RichEditBoxPage.cs: text/setText",
    ];

    [Fact]
    public void SampleCards_SharedStateDoesNotSpread()
    {
        var offenders = new List<string>();
        var pagesWithMultipleCards = 0;

        foreach (var (path, root) in Pages())
        {
            if (SampleCards(root).Count > 1) pagesWithMultipleCards++;

            offenders.AddRange(CrossCardState(root).Select(s => $"{Rel(path)}: {s.Names}"));
        }

        // The rule can only fire on a page with two or more cards, so a loader or a card matcher
        // that quietly stopped finding them would report on a tree it never examined.
        Assert.True(pagesWithMultipleCards >= 20,
            $"only {pagesWithMultipleCards} gallery pages were seen to have multiple SampleCards — the rule would pass near-vacuously.");

        // Offenders are keyed by page and slot names only, so two same-named slots in disjoint
        // scopes on one page collapse to one key — and `Except` dedups, so an entry covering the
        // first would silently cover the second. Every arm below assumes the key identifies one
        // slot; if it does not, say so rather than reporting on subjects that cannot be told apart.
        var ambiguous = offenders.GroupBy(entry => entry, global::System.StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}  (x{g.Count()})")
            .OrderBy(entry => entry, global::System.StringComparer.Ordinal)
            .ToList();

        Assert.True(ambiguous.Count == 0,
            "these pages declare more than one state slot with the same names, so one allowlist " +
            "entry would mask all of them. Rename the slots, or key the entry by declaration " +
            "site:\n  " + string.Join("\n  ", ambiguous));

        var added = offenders.Except(KnownSharedStatePages)
            .OrderBy(entry => entry, global::System.StringComparer.Ordinal)
            .ToList();

        Assert.True(added.Count == 0,
            "these SampleCards share a UseState slot, so driving one silently retargets its " +
            "neighbour (#982). Give each card its own slot:\n  " + string.Join("\n  ", added));

        // The other direction. Once a listed page is fixed its entry stops describing the tree and
        // starts suppressing it, silently, for the one page-and-slot pair most likely to regress.
        var stale = KnownSharedStatePages.Except(offenders)
            .OrderBy(entry => entry, global::System.StringComparer.Ordinal)
            .ToList();

        Assert.True(stale.Count == 0,
            "these KnownSharedStatePages entries no longer match a live offender — the page was " +
            "fixed (#980), so the entry now only masks a re-regression of that exact page and " +
            "slot. Delete them from KnownSharedStatePages:\n  " + string.Join("\n  ", stale));

        // Both arms above compare the tree to the ledger, so both are satisfied by editing the
        // ledger: acknowledge the page you just broke and they agree again. Neither can see that,
        // because "already listed" and "never should have been listed" are the same input to them.
        // This one asks the tree against a record the ledger cannot influence.
        var unrecorded = offenders.Except(SharedStateDebtAtIntroduction)
            .OrderBy(entry => entry, global::System.StringComparer.Ordinal)
            .ToList();

        Assert.True(unrecorded.Count == 0,
            "these SampleCards share a UseState slot on a page that was not already doing so when " +
            "the rule was written (#982), so this is new coupling rather than the debt #980 is " +
            "draining. Give each card its own slot — adding it to KnownSharedStatePages will not " +
            "silence this. That leaves no escape on purpose, and the premise is falsifiable rather " +
            "than assumed: a card is an independently copy-pasteable demo, so a slot spanning two " +
            "of them is the anti-pattern by construction. A demo whose subject genuinely is shared " +
            "state belongs inside a single card, where this rule does not look:\n  "
            + string.Join("\n  ", unrecorded));
    }

    [Theory]
    // Two cards, one slot each — the shape #982 was fixed into.
    [InlineData(0, @"
        var (a, setA) = UseState(""x"");
        var (b, setB) = UseState(""y"");
        return VStack(
            SampleCard(""one"", TextBox(a, setA), ""snippet""),
            SampleCard(""two"", TextBox(b, setB), ""snippet""));")]
    // One card, one slot: nothing to share with.
    [InlineData(0, @"
        var (a, setA) = UseState(""x"");
        return SampleCard(""one"", VStack(TextBox(a, setA), Button(""go"", () => setA(""z""))), ""snippet"");")]
    // Read in one card, written outside any card (e.g. a page-level command bar): one card only.
    [InlineData(0, @"
        var (a, setA) = UseState(""x"");
        return VStack(
            Button(""reset"", () => setA("""")),
            SampleCard(""one"", TextBox(a, setA), ""snippet""),
            SampleCard(""two"", TextBlock(""static""), ""snippet""));")]
    // A name only mentioned inside a snippet string is not a reference — strings are not identifiers.
    [InlineData(0, @"
        var (a, setA) = UseState(""x"");
        return VStack(
            SampleCard(""one"", TextBox(a, setA), ""snippet""),
            SampleCard(""two"", TextBlock(""static""), ""var (a, setA) = UseState(1);""));")]
    // The bug: one slot read by both cards.
    [InlineData(1, @"
        var (a, setA) = UseState(""x"");
        return VStack(
            SampleCard(""one"", TextBox(a, setA), ""snippet""),
            SampleCard(""two"", TextBlock(a), ""snippet""));")]
    // Only the setter crosses over — still coupling, and the more confusing direction: the card
    // holding the button shows no effect at all.
    [InlineData(1, @"
        var (a, setA) = UseState(""x"");
        return VStack(
            SampleCard(""one"", TextBox(a, setA), ""snippet""),
            SampleCard(""two"", Button(""bing"", () => setA(""bing"")), ""snippet""));")]
    // Two slots shared across the same pair of cards are two findings, not one.
    [InlineData(2, @"
        var (a, setA) = UseState(""x"");
        var (b, setB) = UseState(""y"");
        return VStack(
            SampleCard(""one"", VStack(TextBox(a, setA), TextBox(b, setB)), ""snippet""),
            SampleCard(""two"", VStack(TextBlock(a), TextBlock(b)), ""snippet""));")]
    // A slot used twice inside one card is not cross-card; attribution is to the outermost card.
    [InlineData(0, @"
        var (a, setA) = UseState(""x"");
        return SampleCard(""one"", VStack(TextBox(a, setA), TextBlock(a)), ""snippet"");")]
    // The card bodies lifted into locals: neither card mentions the slot, but both still close over
    // it. Reading the card subtree alone reports this clean — which is #982, undetected.
    [InlineData(1, @"
        var (a, setA) = UseState(""x"");
        var first = TextBox(a, setA);
        var second = Button(""bing"", () => setA(""bing""));
        return VStack(
            SampleCard(""one"", first, ""snippet""),
            SampleCard(""two"", second, ""snippet""));")]
    // Lifted through a second local, so widening has to reach a fixed point rather than one hop.
    [InlineData(1, @"
        var (a, setA) = UseState(""x"");
        var inner = TextBlock(a);
        var wrapped = VStack(inner);
        return VStack(
            SampleCard(""one"", TextBox(a, setA), ""snippet""),
            SampleCard(""two"", wrapped, ""snippet""));")]
    // A lambda parameter that shadows the slot name is not the slot — reporting it would redden a
    // page that has no coupling at all.
    [InlineData(0, @"
        var (a, setA) = UseState(""x"");
        return VStack(
            SampleCard(""one"", TextBox(a, setA), ""snippet""),
            SampleCard(""two"", ItemsRepeater(items, a => TextBlock(a)), ""snippet""));")]
    // Same for a local declared inside the other card.
    [InlineData(0, @"
        var (a, setA) = UseState(""x"");
        return VStack(
            SampleCard(""one"", TextBox(a, setA), ""snippet""),
            SampleCard(""two"", Wrap(() => { var a = ""local""; return TextBlock(a); }), ""snippet""));")]
    // The same shadowing question one level out, where the widening pass rather than the card scan
    // is what walks the identifier: the lambda parameter is bound inside a *lifted* local's
    // initializer. Widening without excluding it makes `a` reachable from card two and reports a
    // page that shares nothing.
    [InlineData(0, @"
        var (a, setA) = UseState(""x"");
        var listing = ItemsRepeater(items, a => TextBlock(a));
        return VStack(
            SampleCard(""one"", TextBox(a, setA), ""snippet""),
            SampleCard(""two"", listing, ""snippet""));")]
    // Shadowing is per-occurrence, not per-name: card two both reads the page slot `a` at its top
    // level *and* binds an unrelated lambda parameter called `a` further in. Only the read inside
    // the lambda is the parameter; the `TextBlock(a)` is the page slot, so this is real coupling.
    // Excluding every mention of a name bound anywhere in the card drops that read and reports the
    // page clean — the false negative, and the direction that matters: the three rows above keep a
    // clean page from reddening, this one keeps a coupled page from passing.
    [InlineData(1, @"
        var (a, setA) = UseState(""x"");
        return VStack(
            SampleCard(""one"", TextBox(a, setA), ""snippet""),
            SampleCard(""two"", VStack(TextBlock(a), ItemsRepeater(items, a => TextBlock(a))), ""snippet""));")]
    // The same hole one level out, in the widening pass rather than the card scan: the lifted local
    // both reads the slot and binds the name in a nested lambda.
    [InlineData(1, @"
        var (a, setA) = UseState(""x"");
        var listing = VStack(TextBlock(a), ItemsRepeater(items, a => TextBlock(a)));
        return VStack(
            SampleCard(""one"", TextBox(a, setA), ""snippet""),
            SampleCard(""two"", listing, ""snippet""));")]
    // The tuple kept whole rather than deconstructed.
    [InlineData(1, @"
        var slot = UseState(""x"");
        return VStack(
            SampleCard(""one"", TextBox(slot.Item1, slot.Item2), ""snippet""),
            SampleCard(""two"", TextBlock(slot.Item1), ""snippet""));")]
    // Two disjoint sibling blocks may each declare the same name — legal C#, no CS0136, because the
    // scopes never overlap. So a name maps to more than one lifted declarator, and choosing between
    // them by document order can choose the one this card cannot see: here the decoy comes first, so
    // the widening pass reads `TextBlock(""literal"")`, card two never reaches `a`, and a genuinely
    // coupled page reports clean. Fail-open, one level out from the card scan.
    [InlineData(1, @"
        var (a, setA) = UseState(""x"");
        { var listing = TextBlock(""literal""); }
        {
            var listing = TextBlock(a);
            return VStack(
                SampleCard(""one"", TextBox(a, setA), ""snippet""),
                SampleCard(""two"", listing, ""snippet""));
        }")]
    // …and the mirror, so the fix has to be scope resolution rather than a flip to `Last()`: the
    // decoy is second here, which document order already gets right. A rule that simply reversed the
    // tie-break would pass the row above and redden this one.
    [InlineData(1, @"
        var (a, setA) = UseState(""x"");
        {
            var listing = TextBlock(a);
            var result = VStack(
                SampleCard(""one"", TextBox(a, setA), ""snippet""),
                SampleCard(""two"", listing, ""snippet""));
            return result;
        }
        { var listing = TextBlock(""literal""); }")]
    public void CrossCardRule_ReportsOnlySlotsSpanningCards(int expected, string body)
    {
        var root = CSharpSyntaxTree.ParseText($@"
            class P : Component {{ public override Element Render() {{ {body} }} }}",
            cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);

        Assert.Equal(expected, CrossCardState(root).Count);
    }

    /// <summary>
    /// The offender key is page + slot names, so two same-named slots in disjoint scopes collapse
    /// to a single key — which is what the ambiguity arm of
    /// <see cref="SampleCards_SharedStateDoesNotSpread"/> exists to refuse. Without a live example
    /// that arm would be a guard nobody has watched reject anything, and the tree has none today.
    /// If the key ever gains a declaration site this fails, and the arm should be deleted with it.
    /// </summary>
    [Fact]
    public void CrossCardRule_SameNamedSlotsInDisjointScopesShareOneKey()
    {
        var root = CSharpSyntaxTree.ParseText(@"
            class P : Component { public override Element Render() {
                {
                    var (text, setText) = UseState("""");
                    var a = VStack(SampleCard(""one"", TextBox(text, setText), ""s""),
                                   SampleCard(""two"", TextBlock(text), ""s""));
                }
                {
                    var (text, setText) = UseState("""");
                    var b = VStack(SampleCard(""three"", TextBox(text, setText), ""s""),
                                   SampleCard(""four"", TextBlock(text), ""s""));
                }
            } }",
            cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);

        var slots = CrossCardState(root);

        // Two distinct declarations, both reported — and one key between them, so an allowlist
        // entry naming that key cannot mean one of them rather than the other.
        Assert.Equal(2, slots.Count);
        Assert.Single(slots.Select(s => s.Names).Distinct(global::System.StringComparer.Ordinal));
    }


    // ---------------------------------------------------------------------------------------
    // `new Uri(...)` takes only values that cannot vary at runtime.
    //
    // `new Uri(text)` throws `UriFormatException` on anything malformed, and a gallery page
    // evaluates its element tree inside `Render()` — so the throw lands in `ErrorFallback` and
    // the reader's whole page becomes a `⚠ Render error: UriFormatException…` overlay.
    // Half-typed text in a bound `TextBox` is enough to trigger it (issue #982). A string that
    // cannot vary at runtime cannot do that, hence the helpers below.
    //
    // Unnumbered on purpose: the class doc lists the guarded mistakes in a different order than
    // the file declares them, so any number here is right against one of the two and wrong
    // against the other. It was "Rule 4" and had already drifted.
    //
    // Plain `//` rather than `///` deliberately: two adjacent doc-comment blocks merge onto the
    // next declaration, which would give `RightmostName` a second, unrelated <summary>.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The last identifier of any name form — <c>Uri</c>, <c>System.Uri</c>,
    /// <c>global::System.Uri</c>, <c>global::Uri</c>, <c>Uri?</c>, <c>System.UriKind.Absolute</c>.
    /// Every qualification question below reduces to this, so there is one place to be wrong.
    /// </summary>
    static string? RightmostName(SyntaxNode? node) => node switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        // `Uri? u = new(text)` — every arm of TargetTypeOf can hand back a nullable type, and a
        // TryCreate-style helper returning `Uri?` is the shape a #982 fix is most likely written
        // in. Unwrapping keeps the target-typed `new` visible instead of silently unrecognised.
        NullableTypeSyntax nullable => RightmostName(nullable.ElementType),
        _ => null,
    };

    /// <summary>
    /// Names that mean <c>System.Uri</c> in this file: <c>Uri</c> plus any
    /// <c>using WebUri = System.Uri;</c> alias, which would otherwise construct a Uri under a
    /// name the rule never looks for.
    /// </summary>
    /// <remarks>
    /// The alias is matched on its <em>whole</em> name rather than its last identifier.
    /// <c>Windows.Foundation.Uri</c> also ends in <c>Uri</c> and is a different type, so a rightmost
    /// match adopts it and reports a construction the rule knows nothing about — a false positive
    /// whose stated remedy (<c>Uri.TryCreate</c>) may not even apply to the aliased type. A bare
    /// <c>Uri</c> on the right-hand side is still accepted: it can only resolve to
    /// <c>System.Uri</c> here, and rejecting it would fail open, which is the direction that costs a
    /// missed defect rather than a blocked tree.
    /// </remarks>
    /// <remarks>
    /// <para><b>Known limit — a file that rebinds the name <c>Uri</c> itself.</b> The seed below is
    /// unconditional, so under <c>using Uri = Some.Other.Type;</c> a bare <c>new Uri(x)</c> is still
    /// adopted and reported. Pinned by the <c>Windows.Foundation.Uri</c>-as-<c>Uri</c> row, which
    /// records the <em>current</em> answer rather than the correct one.</para>
    /// <para>Left unfixed deliberately. The set is keyed on the <em>rightmost</em> identifier, so
    /// dropping <c>"Uri"</c> when it is shadowed would also stop matching an explicitly qualified
    /// <c>new System.Uri(x)</c> in the same file — that spelling is unambiguous and must keep
    /// reporting. Separating them means deciding per site on the whole type form instead of by name
    /// lookup, which is a different matcher, not a smaller seed.</para>
    /// <para>The trade is bought by direction, not by rarity. This one fails <em>closed</em>: a false
    /// positive naming a file and a line, which the next contributor reads and dismisses in seconds.
    /// The aliasing case that fails <em>open</em> — <c>using WebUri = System.Uri;</c>, a real Uri
    /// built under a name the rule never looks for — is the one that costs a missed defect, and it is
    /// handled above and pinned by two rows. Fixing a loud limit by rebuilding the matcher that
    /// closes the silent one is the wrong order.</para>
    /// </remarks>
    static HashSet<string> UriTypeNames(SyntaxNode root)
    {
        var names = new HashSet<string>(global::System.StringComparer.Ordinal) { "Uri" };

        var aliases = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .SelectMany(directive => directive.Alias is { } alias && IsSystemUriName(directive.Name)
                ? new[] { alias.Name.Identifier.Text }
                : []);

        names.UnionWith(aliases);

        return names;
    }

    /// <summary>
    /// Whether a name form denotes <c>System.Uri</c> — <c>Uri</c>, <c>global::Uri</c>,
    /// <c>System.Uri</c>, <c>global::System.Uri</c> — rather than merely ending in <c>Uri</c>.
    /// </summary>
    static bool IsSystemUriName(SyntaxNode? name) => name switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.Text == "Uri",
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text == "Uri",
        QualifiedNameSyntax qualified =>
            qualified.Right.Identifier.Text == "Uri" && IsSystemQualifier(qualified.Left),
        _ => false,
    };

    /// <summary>
    /// Whether a qualifier is the <c>System</c> namespace, spelled plainly or through
    /// <c>global::</c>. Anything else — <c>Windows.Foundation</c>, <c>Foo</c> — is a different
    /// namespace, so the <c>Uri</c> it qualifies is a different type.
    /// </summary>
    static bool IsSystemQualifier(SyntaxNode? name) => name switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.Text == "System",
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text == "System",
        _ => false,
    };

    /// <summary>
    /// Every DSL surface with a <c>Uri</c> parameter, mapped to <em>which</em> parameter that is.
    /// Target-typed <c>new(...)</c> carries no type at all in the syntax, so in argument position
    /// this table is the only thing that can recover it — and <c>WebView2(new(text))</c> is issue
    /// #982 spelled with three fewer characters.
    /// </summary>
    /// <remarks>
    /// The whole set, not just the one this bug used: <c>HyperlinkButton</c>, <c>Hyperlink</c>,
    /// <c>BitmapIcon</c>, <c>ImageIcon</c> and the <c>.NavigateUri(...)</c> modifier all take a
    /// <c>Uri</c> too, so listing only <c>WebView2</c> left four ways to write the same defect.
    /// The parameter is recorded rather than just the name because widening the set also widens
    /// the false-positive surface — a <c>new(...)</c> in <c>BitmapIcon</c>'s <c>showAsMonochrome</c>
    /// slot is not a Uri, and matching on the factory name alone would say it was.
    /// </remarks>
    static readonly Dictionary<string, (int Index, string Name)> UriTakingFactories =
        new(global::System.StringComparer.Ordinal)
        {
            ["WebView2"] = (0, "source"),
            ["HyperlinkButton"] = (1, "navigateUri"),
            ["Hyperlink"] = (1, "navigateUri"),
            ["BitmapIcon"] = (0, "source"),
            ["ImageIcon"] = (0, "source"),
            ["NavigateUri"] = (0, "uri"),
        };

    /// <summary>
    /// Ascends through expression parents that preserve the target type, so a target-typed
    /// <c>new(...)</c> is still recognised when it is not the immediate child of the node carrying
    /// the type.
    /// </summary>
    /// <remarks>
    /// This exists to stop the target-type question being a hand-enumeration of parent kinds. Each
    /// context that separates a construction from its type — a parenthesis, a conditional branch, a
    /// <c>??</c> operand, a switch arm — is not a new *kind* of target position, it is the same
    /// position one node further out, and enumerating them one review cycle at a time is what made
    /// this helper wrong five times. Every miss here is a false negative: the site is silently "not a
    /// Uri construction at all", so the rule reports clean on the exact defect it exists to catch.
    /// <para>
    /// Only genuinely type-preserving nodes belong here. A conditional's <em>condition</em> is a
    /// bool, not the conditional's type, so it is excluded — including it would type a
    /// <c>new(...)</c> in the condition slot as the branch type and report a construction that
    /// cannot exist.
    /// </para>
    /// </remarks>
    static ExpressionSyntax AscendTypePreserving(ExpressionSyntax expression)
    {
        var current = expression;

        while (true)
        {
            switch (current.Parent)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized;
                    break;

                // Both branches are converted to the conditional's type; the condition is a bool.
                case ConditionalExpressionSyntax conditional when !ReferenceEquals(conditional.Condition, current):
                    current = conditional;
                    break;

                // Both operands of `??` are converted to the result type.
                case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                    current = coalesce;
                    break;

                case SwitchExpressionArmSyntax { Parent: SwitchExpressionSyntax switchExpression } arm
                    when ReferenceEquals(arm.Expression, current):
                    current = switchExpression;
                    break;

                default:
                    return current;
            }
        }
    }

    /// <summary>
    /// The declared type of the nearest binding of <paramref name="name"/> visible from
    /// <paramref name="site"/>, where the syntax carries one.
    /// </summary>
    /// <remarks>
    /// Shares the outward walk with <see cref="ResolvesTo"/> rather than scanning the member for a
    /// declarator of that name, so a shadowing binding wins exactly as the compiler would resolve it.
    /// </remarks>
    static TypeSyntax? DeclaredTypeOfName(string name, SyntaxNode site)
    {
        for (var node = site; node is not null; node = node.Parent)
        {
            if (NamesIntroducedBy(node).Contains(name, global::System.StringComparer.Ordinal))
                return DeclaredTypeIn(node, name);

            if (node is TypeDeclarationSyntax) break;
        }

        return null;
    }

    /// <summary>The type a single binding scope declares <paramref name="name"/> with, if any.</summary>
    /// <remarks>
    /// Nested blocks are deliberately not descended into. A declaration inside one is not visible to
    /// a site outside it, and a site <em>inside</em> it never reaches this scope —
    /// <see cref="DeclaredTypeOfName"/> walks outward, so the inner block is offered its own
    /// declarations first and wins there.
    /// </remarks>
    static TypeSyntax? DeclaredTypeIn(SyntaxNode scope, string name) =>
        scope.DescendantNodes(descendIntoChildren: child => ReferenceEquals(child, scope) || child is not (BlockSyntax or MemberDeclarationSyntax))
            .OfType<VariableDeclarationSyntax>()
            .FirstOrDefault(declaration => declaration.Variables.Any(v => v.Identifier.Text == name))?.Type
        ?? scope.ChildNodes()
            .OfType<ParameterListSyntax>()
            .SelectMany(list => list.Parameters)
            .FirstOrDefault(parameter => parameter.Identifier.Text == name)?.Type;

    /// <summary>
    /// The declared type a target-typed <c>new(...)</c> is initializing, where the syntax carries
    /// one. The gallery uses <c>new(...)</c> heavily for record collection initializers, so this
    /// must key off the declared type rather than flagging every implicit construction.
    /// </summary>
    /// <remarks>
    /// A lambda body — <c>Func&lt;string, Uri&gt; f = s =&gt; new(s);</c> — is the known limit. The
    /// target type there is the delegate's <em>return</em> type, which is not present in the syntax
    /// at all: recovering it means knowing that <c>Func</c>'s last type argument is the return, which
    /// is a guess about one BCL delegate rather than a rule, and it is wrong for any custom delegate.
    /// Measured as a real fail-open gap; it needs a <c>SemanticModel</c>, not another arm.
    /// </remarks>
    static TypeSyntax? TargetTypeOf(ImplicitObjectCreationExpressionSyntax creation) => AscendTypePreserving(creation) switch
    {
        var target => target.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } } => declaration.Type,
            EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } => property.Type,
            ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax property } => property.Type,
            ArrowExpressionClauseSyntax { Parent: MethodDeclarationSyntax method } => method.ReturnType,
            ArrowExpressionClauseSyntax { Parent: LocalFunctionStatementSyntax local } => local.ReturnType,

            // `u = new(t)` carries its type on `u`'s declaration, not at the construction. Resolved
            // through the same outward walk the shadowing rules use, so an inner `u` wins.
            AssignmentExpressionSyntax assignment
                when ReferenceEquals(assignment.Right, target) && assignment.Left is IdentifierNameSyntax left =>
                    DeclaredTypeOfName(left.Identifier.Text, assignment),

            // Nearest enclosing *function*, not nearest enclosing method. A `return` inside a local
            // function whose ancestor chain continues into `Element Render()` would otherwise be typed
            // by `Element`, so the site is silently not a Uri construction at all — a false negative
            // that the row guard reports as "no `new Uri(...)` in the source" rather than as a miscount.
            ReturnStatementSyntax statement => statement
                .Ancestors()
                .FirstOrDefault(node => node is MethodDeclarationSyntax or LocalFunctionStatementSyntax) switch
            {
                MethodDeclarationSyntax method => method.ReturnType,
                LocalFunctionStatementSyntax local => local.ReturnType,
                _ => null,
            },
            _ => null,
        },
    };

    static bool IsArgumentToUriTakingFactory(ExpressionSyntax expression) =>
        AscendTypePreserving(expression) is { Parent: ArgumentSyntax argument } 
        && argument.Parent is ArgumentListSyntax arguments
        && arguments.Parent is InvocationExpressionSyntax invocation
        && RightmostName(invocation.Expression) is { } factory
        && UriTakingFactories.TryGetValue(factory, out var parameter)
        && (argument.NameColon is { } named
            ? named.Name.Identifier.Text == parameter.Name
            : arguments.Arguments.IndexOf(argument) == parameter.Index);

    static bool IsUriCreation(BaseObjectCreationExpressionSyntax creation, HashSet<string> uriTypeNames) => creation switch
    {
        ObjectCreationExpressionSyntax explicitCreation =>
            RightmostName(explicitCreation.Type) is { } name && uriTypeNames.Contains(name),
        ImplicitObjectCreationExpressionSyntax implicitCreation =>
            (RightmostName(TargetTypeOf(implicitCreation)) is { } name && uriTypeNames.Contains(name))
            || IsArgumentToUriTakingFactory(implicitCreation),
        _ => false,
    };

    static IEnumerable<BaseObjectCreationExpressionSyntax> UriCreations(SyntaxNode root, HashSet<string> uriTypeNames) =>
        root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().Where(c => IsUriCreation(c, uriTypeNames));

    /// <summary>
    /// A mode argument — <c>UriKind.Absolute</c> or the obsolete <c>dontEscape</c> bool — carries no
    /// URL, and is always the trailing parameter of whichever overload takes it.
    /// </summary>
    static bool IsModeArgument(ExpressionSyntax expression) =>
        expression.IsKind(SyntaxKind.TrueLiteralExpression)
        || expression.IsKind(SyntaxKind.FalseLiteralExpression)
        || (expression is MemberAccessExpressionSyntax member && RightmostName(member.Expression) == "UriKind");

    /// <summary>The arguments that actually carry a URL, i.e. everything but a trailing mode.</summary>
    static List<ExpressionSyntax> UrlArguments(BaseObjectCreationExpressionSyntax creation)
    {
        var arguments = (creation.ArgumentList?.Arguments ?? default).Select(a => a.Expression).ToList();

        if (arguments.Count > 0 && IsModeArgument(arguments[^1]))
            arguments.RemoveAt(arguments.Count - 1);

        return arguments;
    }

    /// <summary>
    /// Whether an expression is <em>fixed</em>: assigned once from a literal and unable to differ
    /// between two renders. Deliberately not "compile-time constant" — only <c>const</c> is that,
    /// and the rule also accepts a <c>static readonly</c> initialized from a literal, which is
    /// created at runtime by the static constructor but still cannot carry half-typed input into
    /// <c>new Uri(...)</c>. Inability to vary is the property that makes a throw impossible; being
    /// known to the compiler is merely one way to have it.
    /// </summary>
    static bool IsFixedValue(ExpressionSyntax expression, HashSet<string> names) => expression switch
    {
        LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression),
        IdentifierNameSyntax identifier => names.Contains(identifier.Identifier.Text),
        ParenthesizedExpressionSyntax parenthesized => IsFixedValue(parenthesized.Expression, names),
        // `Scheme + Host` is fixed exactly when both halves are.
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) =>
            IsFixedValue(binary.Left, names) && IsFixedValue(binary.Right, names),
        _ => false,
    };

    /// <summary>
    /// Whether a name is bound by something between <paramref name="site"/> and its enclosing type —
    /// a local, a parameter, a lambda parameter, a <c>foreach</c> variable, a pattern designation.
    /// <para>
    /// The counterpart of <see cref="IsShadowedWithin"/> for names reached by <em>declaration</em>
    /// rather than by occurrence. A type's <c>const</c> and <c>static readonly</c> fields are visible
    /// throughout the type, so a name set built from its members needs no scope walk to be reached —
    /// which is precisely why it needs one to be trusted. C# lets a local or a parameter hide a field
    /// of the same name, and then the constant the set is vouching for is not what the site resolves
    /// to. Unfiltered, that is issue #982's own shape reported as clean: a page-level
    /// <c>const string url</c> beside a <c>UseState</c> slot spelled the same way.
    /// </para>
    /// </summary>
    static bool IsShadowedAt(string name, SyntaxNode site)
    {
        for (var node = site; node is not null; node = node.Parent)
        {
            if (NamesIntroducedBy(node).Contains(name, global::System.StringComparer.Ordinal))
                return true;

            if (node is TypeDeclarationSyntax) break;
        }

        return false;
    }

    /// <summary>
    /// Whether the nearest binding of <paramref name="name"/> above <paramref name="site"/> is the one
    /// <paramref name="declaringScope"/> introduces — that is, whether this declaration is what the
    /// site actually sees.
    /// <para>
    /// The local-tier counterpart of <see cref="IsShadowedAt"/>, which cannot be reused here: a const
    /// local is itself a binding, so it would always report as shadowed by its own declaration.
    /// "Declared in a scope that encloses the site" is a weaker question than "what the site resolves
    /// to", and only the second is sound — a lambda parameter may legally shadow an enclosing const
    /// local (this is not CS0136), which lets a constant vouch for a name the site reads as a
    /// runtime value.
    /// </para>
    /// </summary>
    static bool ResolvesTo(string name, SyntaxNode site, SyntaxNode declaringScope)
    {
        for (var node = site; node is not null; node = node.Parent)
        {
            if (NamesIntroducedBy(node).Contains(name, global::System.StringComparer.Ordinal))
                return ReferenceEquals(node, declaringScope);

            if (node is TypeDeclarationSyntax) break;
        }

        return false;
    }

    /// <summary>Names a <c>new Uri(...)</c> at <paramref name="site"/> may safely reference.</summary>
    /// <remarks>
    /// Two tiers, and the scoping is the point:
    /// <list type="bullet">
    /// <item><c>const</c> fields of the enclosing type(s) <em>that the site is not shadowed from</em>,
    /// and the <c>const</c> locals the site <em>actually resolves to</em>. A file-wide set would let
    /// an unrelated <c>const string url</c>
    /// in one method whitelist a runtime <c>url</c> in another — which is issue #982 itself,
    /// undetected — and a member-wide set does the same thing across two sibling blocks. A field is
    /// visible throughout its type without any scope walk, which is exactly why one is needed to
    /// trust it: a local or a parameter may legally hide it, and then the constant being vouched for
    /// is not what the site resolves to;</item>
    /// <item><c>static readonly</c> fields — under the same shadowing filter — whose own initializer
    /// is a fixed value or a
    /// <c>new Uri(...)</c> over accepted arguments. That covers <c>new Uri(BaseUri, "page.html")</c>
    /// without hand-waving: the referenced field's construction is checked by this same rule, so
    /// accepting the reference adds no unchecked value. A <c>static readonly</c> initialized from a
    /// runtime call is <em>not</em> accepted.</item>
    /// </list>
    /// Both tiers iterate to a fixed point so a chain — or a field referenced before it is declared —
    /// resolves regardless of declaration order.
    /// </remarks>
    static HashSet<string> AcceptedNames(SyntaxNode site, HashSet<string> uriTypeNames)
    {
        var names = new HashSet<string>(global::System.StringComparer.Ordinal);

        var constants = site.Ancestors().OfType<TypeDeclarationSyntax>()
            .SelectMany(type => type.Members.OfType<FieldDeclarationSyntax>())
            .Where(field => field.Modifiers.Any(SyntaxKind.ConstKeyword))
            .SelectMany(field => field.Declaration.Variables)
            .Where(variable => !IsShadowedAt(variable.Identifier.Text, site))
            .ToList();

        // Only the blocks that actually enclose the site. `member.DescendantNodes()` would also
        // reach a *sibling* block, and this is legal C#:
        //
        //     { const string url = "https://example.com"; }
        //     { var (url, setUrl) = UseState(""); var u = new Uri(url); }
        //
        // — two non-overlapping scopes, no CS0136, and a member-wide set would accept the second
        // `url` on the strength of the first. That is the same scoping bug as the file-wide set
        // this replaced, one level in.
        foreach (var scope in site.Ancestors().TakeWhile(node => node is not MemberDeclarationSyntax))
        {
            constants.AddRange(scope.ChildNodes()
                .OfType<LocalDeclarationStatementSyntax>()
                .Where(local => local.Modifiers.Any(SyntaxKind.ConstKeyword))
                .SelectMany(local => local.Declaration.Variables)
                .Where(variable => ResolvesTo(variable.Identifier.Text, site, scope)));
        }

        var staticReadonly = site.Ancestors().OfType<TypeDeclarationSyntax>()
            .SelectMany(type => type.Members.OfType<FieldDeclarationSyntax>())
            .Where(field => field.Modifiers.Any(SyntaxKind.StaticKeyword) && field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
            .SelectMany(field => field.Declaration.Variables)
            .Where(variable => !IsShadowedAt(variable.Identifier.Text, site))
            .ToList();

        // Materialised once, outside the fixed-point loop: the filter is a property of the syntax
        // and cannot change, while `names` below grows on every pass and must stay inside.
        //
        // Filter and projection are the same step deliberately. A bare
        // `.Where(d => d.Initializer?.Value is not null)` does not narrow the *element type* —
        // C# flow analysis does not carry a predicate's narrowing out of the lambda — so the loop
        // body would have to re-extract behind a null-forgiving `d.Initializer!.Value`, an
        // unchecked assertion sitting ~15 lines from the guard that justifies it. Binding `value`
        // in the pattern keeps the hoist and the non-null guarantee in one expression, so editing
        // the filter cannot leave a latent dereference behind.
        var initialized = constants.Concat(staticReadonly)
            .SelectMany(declarator => declarator.Initializer?.Value is { } value
                ? new[] { (Name: declarator.Identifier.Text, Value: value) }
                : [])
            .ToList();

        // `Add` drives the fixed point: it returns false for a name already accepted, so the pass
        // ends without a `Contains` entry guard. That guard was load-bearing for termination
        // rather than an optimisation — paired with an unconditional `grew = true` it was the
        // only thing stopping the loop spinning forever — so hoisting it into a lazy
        // `.Where(d => !names.Contains(...))` would have made termination depend on the pipeline
        // interleaving pull-by-pull with the `Add` below, and a later `.ToList()` on that pipeline
        // would hang the test with no visible cause. Acceptance is monotone in `names`, so
        // re-testing an accepted declarator is wasted work, never a different answer.
        bool grew;
        do
        {
            grew = false;

            foreach (var (name, value) in initialized)
            {
                var accepted = IsFixedValue(value, names)
                               || (value is BaseObjectCreationExpressionSyntax creation
                                   && IsUriCreation(creation, uriTypeNames)
                                   && UrlArguments(creation).All(a => IsFixedValue(a, names)));

                if (accepted)
                    grew |= names.Add(name);
            }
        }
        while (grew);

        return names;
    }

    /// <summary>
    /// Every <c>new Uri(...)</c> argument in <paramref name="root"/> that can vary at runtime,
    /// as <c>"line: expression"</c>. Separate from the tree-wide fact below so the rule
    /// can be driven over synthetic source — a scan of a clean tree cannot tell a working detector
    /// from one that reports nothing.
    /// </summary>
    static IReadOnlyList<(BaseObjectCreationExpressionSyntax Site, ExpressionSyntax Argument)> RuntimeUriArguments(SyntaxNode root)
    {
        var uriTypeNames = UriTypeNames(root);
        var offenders = new List<(BaseObjectCreationExpressionSyntax, ExpressionSyntax)>();

        foreach (var creation in UriCreations(root, uriTypeNames))
        {
            var names = AcceptedNames(creation, uriTypeNames);

            // First offending argument only — one finding per site reads better than one per
            // argument, and `new Uri(base, relative)` would otherwise report twice.
            var runtime = UrlArguments(creation)
                .Where(argument => !IsFixedValue(argument, names))
                .Take(1);

            foreach (var argument in runtime)
                offenders.Add((creation, argument));
        }

        return offenders;
    }

    static int UriCreationCount(SyntaxNode root) => UriCreations(root, UriTypeNames(root)).Count();

    [Fact]
    public void NewUri_TakesOnlyValuesThatCannotVaryAtRuntime()
    {
        var offenders = new List<string>();
        var recognized = new Dictionary<string, int>(global::System.StringComparer.OrdinalIgnoreCase);

        foreach (var (path, root) in Pages())
        {
            recognized[Rel(path)] = UriCreationCount(root);

            foreach (var (site, argument) in RuntimeUriArguments(root))
            {
                offenders.Add($"{Where(path, site)}: new Uri({argument}) is built from a runtime value — " +
                              "it throws UriFormatException on malformed input, and a throw out of Render() " +
                              "replaces the whole page with the error boundary. Parse with Uri.TryCreate and " +
                              "render the last value that parsed.");
            }
        }

        // Pinned per file rather than as a tree-wide floor: a tree-wide floor stays green when one
        // page loses a site and another gains one, so it fails the repo's deletion test. Naming the
        // files means a loader that quietly stopped reading a page — or a type check that stopped
        // recognising a construction — reddens here instead of reporting a clean tree it never
        // looked at.
        //
        // A minimum per file rather than an exact set. What this guard is for is keeping the scan
        // below honest: `offenders.Count == 0` is also what a detector that stopped recognising
        // anything produces. Requiring the set to match exactly would additionally forbid any
        // future page from adding a *safe* `new Uri("https://…")` — a correct change the rule has
        // no business rejecting, since its whole contract is that the argument cannot vary at
        // runtime, which `offenders` enforces directly and everywhere. The floor still fails in the
        // direction that actually threatens this test: a pinned file dropping below its count means
        // constructions that used to be seen no longer are.
        var minimums = new Dictionary<string, int>(global::System.StringComparer.Ordinal)
        {
            ["HyperlinkButtonPage.cs"] = 1,
            ["WebView2Page.cs"] = 3,
        };

        var seen = recognized
            .Where(entry => entry.Value > 0)
            .ToDictionary(
                entry => global::System.IO.Path.GetFileName(entry.Key),
                entry => entry.Value,
                global::System.StringComparer.Ordinal);

        var regressed = minimums
            .Where(pin => !seen.TryGetValue(pin.Key, out var count) || count < pin.Value)
            .Select(pin => $"{pin.Key}: expected at least {pin.Value} recognised `new Uri(...)` " +
                           $"site(s), saw {(seen.TryGetValue(pin.Key, out var c) ? c : 0)}")
            .OrderBy(entry => entry, global::System.StringComparer.Ordinal)
            .ToArray();

        Assert.True(regressed.Length == 0,
            "fewer recognised `new Uri(...)` sites than pinned. There are two very different causes " +
            "and the edit that resolves one silences the other: if the site was deliberately removed, " +
            "lower the pin; if it was not, the scan stopped recognising a construction it used to " +
            "see, and the clean `offenders` result below is then reporting on a tree it no longer " +
            "reads. Confirm which before changing the number:\n  " + string.Join("\n  ", regressed));

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    /// <summary>
    /// The accept-list decides everything, so each of its arms is pinned against synthetic source:
    /// the tree-wide fact above is green for a detector that works and for one that returns nothing,
    /// and only these rows tell them apart. The reported rows are the shapes issue #982 shipped —
    /// state read straight out of <c>UseState</c>, a method call on it, an interpolation — plus the
    /// spellings that would let the same bug back in under a name the rule was not looking for.
    /// </summary>
    [Theory]
    // Accepted: nothing here can vary at runtime.
    [InlineData(@"class P { void M() { var u = new Uri(""https://example.com""); } }", 0)]
    [InlineData(@"class P { const string U = ""https://example.com""; void M() { var u = new Uri(U); } }", 0)]
    [InlineData(@"class P { void M() { const string U = ""https://example.com""; var u = new Uri(U); } }", 0)]
    // A const in an *enclosing* block is visible at the site and stays accepted.
    [InlineData(@"class P { void M() { const string U = ""https://example.com""; { var u = new Uri(U); } } }", 0)]
    [InlineData(@"class P { static readonly Uri B = new Uri(""https://example.com""); void M() { var u = new Uri(B, ""page.html""); } }", 0)]
    [InlineData(@"class P { void M() { var u = new Uri(""https://example.com"", UriKind.Absolute); } }", 0)]
    // A const chain folds at compile time. A `static readonly string` does not — it is
    // initialised at runtime — but it is assigned once from a literal and cannot vary per
    // render, which is the property the rule actually needs.
    [InlineData(@"class P { const string H = ""https://example.com""; const string U = H; void M() { var u = new Uri(U); } }", 0)]
    [InlineData(@"class P { static readonly string U = ""https://example.com""; void M() { var u = new Uri(U); } }", 0)]
    // Concatenation is accepted exactly when both halves are.
    [InlineData(@"class P { const string H = ""https://example.com""; void M() { var u = new Uri(H + ""/docs""); } }", 0)]
    // Declaration order must not matter: Sub references Base before Base is declared.
    [InlineData(@"class P { static readonly Uri Sub = new Uri(Base, ""p""); static readonly Uri Base = new Uri(""https://example.com""); void M() { var u = new Uri(Sub, ""q""); } }", 0)]
    // Qualified UriKind, and the obsolete dontEscape bool, are modes rather than URLs.
    [InlineData(@"class P { void M() { var u = new Uri(""https://example.com"", System.UriKind.Absolute); } }", 0)]
    [InlineData(@"class P { void M() { var u = new Uri(""https://example.com"", true); } }", 0)]
    [InlineData(@"class P { void M() { var u = new Uri(""https://example.com"", false); } }", 0)]
    // A trusted base built with an explicit UriKind stays trusted.
    [InlineData(@"class P { static readonly Uri B = new Uri(""https://example.com"", UriKind.Absolute); void M() { var u = new Uri(B, ""p""); } }", 0)]
    // Target-typed `new(...)` for a record is not a Uri construction and must stay silent.
    [InlineData(@"class P { static readonly Item[] Items = { new(""a"", 1), new(""b"", 2) }; void M() { var u = new Uri(""https://example.com""); } }", 0)]
    // Reported: every one of these is a value the reader can make malformed.
    [InlineData(@"class P { Element R() { var (t, s) = UseState(""""); return WebView2(new Uri(t)); } }", 1)]
    [InlineData(@"class P { Element R(string t) { return WebView2(new Uri(t.Trim())); } }", 1)]
    [InlineData(@"class P { Element R(string t) { return WebView2(new Uri($""https://{t}"")); } }", 1)]
    [InlineData(@"class P { Element R(string t) { return WebView2(new System.Uri(t)); } }", 1)]
    // A `static readonly` is only trusted when its own initializer is, and a property is not a field.
    [InlineData(@"class P { static readonly string U = Fetch(); void M() { var u = new Uri(U); } }", 1)]
    [InlineData(@"class P { static string U => Fetch(); void M() { var u = new Uri(U); } }", 1)]
    // A `const` in one member must not whitelist a runtime value of the same name in another —
    // this is issue #982 exactly, and a file-wide name set reports it as clean.
    [InlineData(@"class P { void Helper() { const string url = ""https://example.com""; } Element R() { var (url, s) = UseState(""""); return WebView2(new Uri(url)); } }", 1)]
    // Same bug one level in: two *sibling* blocks in one member. This is legal C# — the scopes do
    // not overlap, so there is no CS0136 — and a member-wide name set accepts the runtime `url` on
    // the strength of a constant that is not visible from the site.
    [InlineData(@"class P { Element R() { { const string url = ""https://example.com""; } { var (url, s) = UseState(""""); return WebView2(new Uri(url)); } } }", 1)]
    // Same bug at the *field* tier. A const field is visible everywhere in the type, so a name set
    // built from the type's members needs no scope walk to be reached — and that is exactly why it
    // has to be filtered by one. A local or a parameter may legally hide a field of the same name,
    // and then the constant the set is vouching for is not what the site resolves to. This is #982's
    // own shape: a page-level `const string url` beside a `UseState` slot spelled the same way.
    [InlineData(@"class P { const string url = ""https://example.com""; Element R() { var (url, s) = UseState(""""); return WebView2(new Uri(url)); } }", 1)]
    [InlineData(@"class P { const string url = ""https://example.com""; Element R(string url) { return WebView2(new Uri(url)); } }", 1)]
    [InlineData(@"class P { const string url = ""https://example.com""; Element R() => Items.Select(url => WebView2(new Uri(url))); }", 1)]
    // …and the `static readonly` tier is reachable by name the same way, so it needs the same filter.
    [InlineData(@"class P { static readonly string url = ""https://example.com""; Element R() { var (url, s) = UseState(""""); return WebView2(new Uri(url)); } }", 1)]
    // The local tier needs the same treatment, and this one is legal C# rather than merely
    // representable: a lambda parameter *may* shadow an enclosing const local (verified against the
    // compiler, not assumed — it is not CS0136). So "declared in a scope that encloses the site" is
    // not the same question as "what the site resolves to", and only the second one is sound.
    [InlineData(@"class P { Element R() { const string url = ""https://example.com""; return Items.Select(url => WebView2(new Uri(url))); } }", 1)]
    // Binder kinds that introduce a name without declaring a local, a field or a designation, so a
    // walk built from those three alone never sees them and the outer constant wins by default.
    // All three are fail-open: the site reads a runtime value and the rule calls it fixed.
    [InlineData(@"class P { const string url = ""https://example.com""; Element R(string[] items) { return VStack(from url in items select WebView2(new Uri(url))); } }", 1)]
    [InlineData(@"class P { const string url = ""https://example.com""; Element R(string[] items) { return VStack(from s in items let url = s.Trim() select WebView2(new Uri(url))); } }", 1)]
    // The catch arm needs concatenation rather than `url.Message`: a member access is rejected on its
    // own, so that spelling passes whether or not the binder is understood and pins nothing. Verified
    // against the compiler that this one is legal — an exception variable concatenates to a string.
    [InlineData(@"class P { const string url = ""https://example.com""; Uri R() { try { return new Uri(url); } catch (UriFormatException url) { return new Uri(url + """"); } } }", 1)]
    // Spellings that hide the type: a using-alias, global::Uri, and target-typed new.
    [InlineData(@"using WebUri = System.Uri; class P { Element R(string t) { return WebView2(new WebUri(t)); } }", 1)]
    // …but the alias has to be to *System*.Uri. `Windows.Foundation.Uri` also ends in `Uri`, so a
    // rightmost-name match adopts it and reports a construction of a type this rule knows nothing
    // about — a false positive whose stated remedy (`Uri.TryCreate`) may not even apply to it.
    // The accepted `new Uri(t)` alongside is not decoration: once the alias is correctly ignored the
    // row guard needs *something* it recognises, so `1` separates "the alias was rejected" from
    // "nothing in this row parsed as a Uri construction at all".
    [InlineData(@"using WebUri = Windows.Foundation.Uri; class P { Element R(string t) { WebUri a = new WebUri(t); return WebView2(new Uri(t)); } }", 1)]
    // KNOWN LIMIT, pinned deliberately: when the alias rebinds `Uri` *itself*, the unconditional seed
    // in UriTypeNames still adopts the bare name, so the aliased `new Uri(t)` is reported alongside
    // the genuine `new System.Uri(t)`. The correct answer is 1; this row records the 2 the rule
    // actually gives, so the limit is auditable instead of folklore. See the UriTypeNames remarks for
    // why it stays: the set is keyed on the rightmost identifier, so suppressing the shadowed name
    // would also silence the explicitly-qualified spelling next to it. If a future change makes this
    // row fail, the limit was fixed — update the count to 1 and delete the remark, don't relax this.
    [InlineData(@"using Uri = Windows.Foundation.Uri; class P { Element R(string t) { Uri a = new Uri(t); return WebView2(new System.Uri(t)); } }", 2)]
    // …and `global::` in front of the aliased name must not defeat that System check. This is the
    // over-tightening direction: dropping the alias-qualified arm silently stops recognising a real
    // `System.Uri` alias, which is a false negative rather than a blocked tree.
    [InlineData(@"using WebUri = global::System.Uri; class P { Element R(string t) { return WebView2(new WebUri(t)); } }", 1)]
    [InlineData(@"class P { Element R(string t) { return WebView2(new global::Uri(t)); } }", 1)]
    [InlineData(@"class P { Element R(string t) { Uri u = new(t); return WebView2(u); } }", 1)]
    [InlineData(@"class P { Element R(string t) { return WebView2(new(t)); } }", 1)]
    // `Uri?` is what a TryCreate-style helper returns, so the nullable spellings of target-typed
    // `new` are the ones a fix for #982 is most likely to be written in — including inside the
    // guard helper itself. Every arm of TargetTypeOf can hand back a nullable type.
    [InlineData(@"class P { Element R(string t) { Uri? u = new(t); return WebView2(u); } }", 1)]
    [InlineData(@"class P { Uri? Parse(string t) => new(t); }", 1)]
    [InlineData(@"class P { Uri? Parse(string t) { return new(t); } }", 1)]
    [InlineData(@"class P { System.Uri? Parse(string t) => new(t); }", 1)]
    // The same two spellings as *local* functions. Worth pinning separately: an arrow clause on a
    // local function has a different parent type, and a `return` inside one walks past it to the
    // enclosing method — whose return type is `Element`, so the site is silently not a Uri at all.
    [InlineData(@"class P { Element R(string t) { Uri? Parse(string s) => new(s); return WebView2(Parse(t)); } }", 1)]
    [InlineData(@"class P { Element R(string t) { Uri Parse(string s) { return new(s); } return WebView2(Parse(t)); } }", 1)]
    // …and a local function still accepts a constant, so recognising it did not widen the rule.
    [InlineData(@"class P { Element R() { Uri? Parse() => new(""https://example.com""); return WebView2(Parse()); } }", 0)]
    [InlineData(@"class P { Uri? Home { get; } = new(Fetch()); }", 1)]
    // …and the nullable target type still accepts a constant, so unwrapping it did not simply
    // turn the arm into a blanket report.
    [InlineData(@"class P { const string U = ""https://example.com""; Uri? Parse() => new(U); }", 0)]
    // The rest of the DSL's Uri-taking surface. Listing only WebView2 left four other ways to
    // write the same defect, including the `.NavigateUri(...)` modifier, whose invocation is a
    // member access rather than a bare factory name.
    [InlineData(@"class P { Element R(string t) { return HyperlinkButton(""Go"", new(t)); } }", 1)]
    [InlineData(@"class P { Element R(string t) { return HyperlinkButton(""Go"", navigateUri: new(t)); } }", 1)]
    [InlineData(@"class P { Element R(string t) { return HyperlinkButton(""Go"").NavigateUri(new(t)); } }", 1)]
    [InlineData(@"class P { Element R(string t) { return ImageIcon(new(t)); } }", 1)]
    // …but it has to be the Uri *parameter*. `showAsMonochrome` is a bool, so a target-typed
    // `new(...)` there is not a Uri — keying off the factory name alone would say it was, and
    // widening the set from one entry to six is exactly what makes that reachable.
    [InlineData(@"class P { Element R(string t) { return BitmapIcon(new Uri(""https://example.com""), new(t)); } }", 0)]
    // A type-preserving node between the construction and its type. These are not distinct kinds of
    // target position — they are the same position one node further out — so they are pinned
    // together to keep the fix a rule rather than an enumeration. Each was a measured false
    // negative: the rule reported clean on an unvalidated construction.
    [InlineData(@"class P { Element R(string t, bool f) { return WebView2(f ? new(t) : Fallback); } }", 1)]
    [InlineData(@"class P { Element R(string t) { return WebView2((new(t))); } }", 1)]
    [InlineData(@"class P { Element R(string t, Uri? o) { return WebView2(o ?? new(t)); } }", 1)]
    [InlineData(@"class P { Element R(string t, bool f) { Uri u = f ? new(t) : Fallback; return WebView2(u); } }", 1)]
    // …and the condition slot is a bool, so ascending must not type a construction there as the
    // branch type. This is the false *positive* the ascent would introduce if it were careless.
    // The accepted `new Uri("literal")` in the true branch is not decoration: the row guard below
    // requires every row to contain a construction the rule recognises, and a condition-slot
    // `new(t)` deliberately is not one. Without it the row would satisfy `expected: 0` for the
    // wrong reason — the same vacuity the guard exists to catch.
    [InlineData(@"class P { Element R(string t) { return WebView2(new(t) ? new Uri(""https://example.com"") : Fallback); } }", 0)]
    // A plain literal argument stays accepted — the ascent above must not disturb the base case.
    [InlineData(@"class P { Element R(string t) { return WebView2(new Uri(""https://example.com"")); } }", 0)]
    // Assignment carries its type on the target's declaration, not at the construction.
    [InlineData(@"class P { Element R(string t) { Uri u; u = new(t); return WebView2(u); } }", 1)]
    // …and the *nearest* declaration wins, so an inner binding is not typed by an outer one.
    [InlineData(@"class P { string u; Element R(string t) { Uri u; u = new(t); return WebView2(u); } }", 1)]
    // An assignment inside a *nested* block still resolves. DeclaredTypeIn deliberately does not
    // descend into nested blocks, so this only works because the outward walk offers the innermost
    // block first — the two halves are one mechanism, and this row fails if either is dropped.
    [InlineData(@"class P { Element R(string t, bool f) { if (f) { Uri u; u = new(t); return WebView2(u); } return null; } }", 1)]
    // …and that same non-descent is what stops a *sibling* block answering for this one. The decoy
    // `string u` is earlier in document order and would win under a whole-member scan, typing the
    // construction as not-a-Uri and dropping the report. Fail-open, and invisible without the decoy.
    [InlineData(@"class P { Element R(string t, bool f) { if (f) { string u; u = """"; } { Uri u; u = new(t); return WebView2(u); } } }", 1)]
    public void UriConstantRule_AcceptsFixedValues_AndReportsRuntimeOnes(string source, int expected)
    {
        var root = CSharpSyntaxTree
            .ParseText(source, cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken);

        // Guards the rows themselves: a typo that stopped parsing as a Uri construction would
        // otherwise satisfy every `expected: 0` row for the wrong reason.
        Assert.True(UriCreationCount(root) > 0, $"no `new Uri(...)` in the row source: {source}");
        Assert.Equal(expected, RuntimeUriArguments(root).Count);
    }
}
