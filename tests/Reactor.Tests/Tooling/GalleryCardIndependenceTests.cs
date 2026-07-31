using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// Source lint for the independence of ReactorGallery <c>SampleCard</c>s. Every card is a
/// self-contained demonstration a reader is invited to copy, so two cards driven by one
/// <c>UseState</c> slot are not two samples — they are one sample rendered twice, and the
/// gallery says otherwise on its face.
///
/// <para>The rule is one sentence: <b>a state slot may be reached from at most one
/// <c>SampleCard</c>.</b></para>
///
/// <para>This is not hypothetical. Issue #980 reported it against NumberBox — the "Basic
/// NumberBox" and "NumberBox with Spin Buttons" cards shared <c>(value, setValue)</c>, so
/// clicking Increase in one card moved the other's readout, confirmed live over UIA. A scan
/// prompted by that report found the same defect on thirteen further pages, which is why this
/// exists as a gate rather than a one-page fix.</para>
///
/// <para><b>What the rule keys on.</b> The slot must come from a state hook —
/// <c>UseState</c> or <c>UseReducer</c> — rather than from a name-shape convention. That is a
/// stronger contract than <c>(x, setX)</c> spelling: it admits a pair named
/// <c>(log, updateLog)</c>, and it refuses to convict an ordinary tuple whose two names happen
/// to be live in two cards. A <c>UseMemo</c> or a plain local is deliberately out of scope —
/// derived values and shared data are not per-card state, and reusing them across cards is
/// normal.</para>
///
/// <para><b>What it deliberately cannot catch.</b> Attribution is by syntactic containment, so
/// a slot reaches a card only when its name appears lexically inside that card's call. These
/// escape, and all of them are absent from the gallery today:</para>
/// <list type="bullet">
/// <item>a slot referenced only through a page-level helper that two cards call;</item>
/// <item>one <c>SampleCard</c> call executed more than once — by a <c>foreach</c>, a LINQ
/// projection, or a helper invoked twice;</item>
/// <item>a card element built into a local and then rendered in two places.</item>
/// </list>
/// <para>Each needs call-graph or data-flow reasoning this tier does not have, and silence is
/// the cheaper error: this lint has no allowlist, so a false positive is a blocked tree. The
/// one shape that errs the other way is two <em>mutually exclusive</em> cards — a
/// <c>flag ? SampleCard(a) : SampleCard(b)</c> both naming one slot would be reported although
/// only one ever renders. No gallery page builds a card conditionally; if one ever does, the
/// fix is to give each branch its own slot, which is what the reader would expect anyway.</para>
///
/// <para>Note that lambda-parameter shadowing is <em>not</em> on this list. A lambda parameter
/// cannot reuse the name of a local in an enclosing scope (CS0136), and every slot is a local
/// of <c>Render</c>, so in code that compiles a shadowed name cannot exist to be misattributed.
/// </para>
///
/// <para>Roslyn parses the page sources directly — no gallery build, no WinUI objects — so this
/// stays in the headless unit tier.</para>
/// </summary>
public sealed class GalleryCardIndependenceTests
{
    /// <summary>
    /// The hooks that mint a piece of per-card mutable UI state. Both return a tuple that pages
    /// deconstruct, and both carry the same "this belongs to one card" contract.
    /// </summary>
    static readonly string[] StateHooks = ["UseState", "UseReducer"];

    // ── findings ─────────────────────────────────────────────────────────────

    internal readonly record struct Finding(string Slot, IReadOnlyList<string> Cards, int Line)
    {
        /// <summary>Compact form the detector's own tests compare against.</summary>
        public string Signature() => $"{Slot}: {string.Join(" | ", Cards)}";

        public string Describe() =>
            $"the {Slot} state slot is wired into {Cards.Count} sample cards — " +
            string.Join(", ", Cards.Select(c => $"\"{c}\"")) + ". " +
            "Each card is an independent demonstration, so driving one silently moves the others. " +
            "Give every card its own UseState.";
    }

    internal sealed record PageScan(IReadOnlyList<Finding> Findings, int Cards, int Slots);

    // ── finding the state slots ──────────────────────────────────────────────

    /// <summary>
    /// Strip parentheses so <c>var (a, b) = (UseState(0))</c> is still recognised as the hook
    /// call it is.
    /// </summary>
    static ExpressionSyntax Unwrap(ExpressionSyntax expression) =>
        expression is ParenthesizedExpressionSyntax paren ? Unwrap(paren.Expression) : expression;

    static bool IsStateHook(ExpressionSyntax expression) =>
        Unwrap(expression) is InvocationExpressionSyntax invocation
        && GallerySources.InvokedName(invocation) is { } hook
        && StateHooks.Contains(hook, global::System.StringComparer.Ordinal);

    /// <summary>
    /// Every state hook call on the page, as the names it binds paired with the line it sits on.
    /// Two spellings bind a slot and both are recognised:
    /// <list type="bullet">
    /// <item><c>var (x, setX) = UseState(...)</c> — the deconstruction the gallery uses.</item>
    /// <item><c>var s = UseState(...)</c> — <see cref="Core.RenderContext.UseState"/> returns the
    /// <em>named</em> tuple <c>(T Value, Action&lt;T&gt; Set)</c>, so <c>s.Value</c> / <c>s.Set</c>
    /// is legal and drives a card exactly as the deconstructed pair does. Missing it would leave
    /// the rule avoidable by rewriting one line.</item>
    /// </list>
    /// Discards are skipped — they bind nothing and so can never be referenced from a card.
    /// Returned in source order, which is what <c>DescendantNodes</c> pre-order already gives.
    /// </summary>
    internal static IReadOnlyList<(IReadOnlyList<string> Names, int Line)> StateSlots(SyntaxNode pageRoot)
    {
        var slots = new List<(IReadOnlyList<string> Names, int Line)>();

        foreach (var node in pageRoot.DescendantNodes())
        {
            IReadOnlyList<string>? names = null;

            if (node is AssignmentExpressionSyntax assignment
                && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && assignment.Left is DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax designation }
                && IsStateHook(assignment.Right))
            {
                names = designation.Variables
                    .OfType<SingleVariableDesignationSyntax>()
                    .Select(v => v.Identifier.Text)
                    .Where(n => n.Length > 0)
                    .ToList();
            }
            else if (node is VariableDeclaratorSyntax { Initializer: { } initializer } declarator
                && IsStateHook(initializer.Value))
            {
                names = [declarator.Identifier.Text];
            }

            if (names is null || names.Count == 0) continue;

            slots.Add((names, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
        }

        return slots;
    }

    // ── attributing a reference to a card ────────────────────────────────────

    /// <summary>
    /// The card an identifier sits inside, or null when it sits in the page's shared context.
    /// The <em>innermost</em> containing invocation wins, so a card nested inside another card's
    /// sample is attributed to itself rather than to its host.
    /// </summary>
    static InvocationExpressionSyntax? OwningCard(SyntaxNode node, IReadOnlyList<InvocationExpressionSyntax> cards) =>
        cards.Where(card => card.Span.Contains(node.Span))
            .OrderBy(card => card.Span.Length)
            .FirstOrDefault();

    /// <summary>
    /// A card's title: the <c>title:</c> label wherever it sits, otherwise the first positional
    /// argument. Only used to name a card in a message.
    /// </summary>
    static string CardTitle(InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;

        var titleArgument = args
            .Where(a => a.NameColon?.Name.Identifier.Text == "title")
            .Select(a => a.Expression)
            .FirstOrDefault()
            ?? (args.Count > 0 && args[0].NameColon is null ? args[0].Expression : null);

        return titleArgument is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : "(untitled)";
    }

    /// <summary>
    /// <c>nameof(value)</c> mentions the slot without reading it — no render depends on the
    /// state, so it cannot couple two cards. Excluded so it can never convict on its own.
    /// </summary>
    static bool IsNameOfOperand(SyntaxNode reference) =>
        reference.Ancestors().OfType<InvocationExpressionSyntax>().Any(i =>
            i.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" }
            && i.ArgumentList.Arguments.Any(a => a.Span.Contains(reference.Span)));

    // ── the scan ─────────────────────────────────────────────────────────────

    internal static PageScan ScanSource(string pageSource) =>
        ScanPage(CSharpSyntaxTree.ParseText(pageSource).GetRoot());

    internal static PageScan ScanPage(SyntaxNode pageRoot)
    {
        var cards = pageRoot.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(i => GallerySources.InvokedName(i) == "SampleCard")
            .ToList();

        var slots = StateSlots(pageRoot);

        // One name can only speak for one slot. Two slots binding the same name would make an
        // attribution ambiguous, so such a name is dropped rather than guessed at.
        var slotOfName = new Dictionary<string, int>(global::System.StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(global::System.StringComparer.Ordinal);

        for (var index = 0; index < slots.Count; index++)
        {
            foreach (var name in slots[index].Names)
            {
                if (slotOfName.TryGetValue(name, out var existing) && existing != index)
                    ambiguous.Add(name);
                else
                    slotOfName[name] = index;
            }
        }

        // Cards reached per slot, in source order, de-duplicated by the card's own span.
        var reached = slots.Select(_ => new List<InvocationExpressionSyntax>()).ToList();

        // A `sourceCode:` snippet is a string literal, so it contributes no IdentifierNameSyntax
        // and needs no special-casing: only the live half of a card is ever inspected here.
        foreach (var reference in GallerySnippetAgreementTests.ReferenceNames(pageRoot))
        {
            var name = reference.Identifier.Text;
            if (ambiguous.Contains(name)) continue;
            if (!slotOfName.TryGetValue(name, out var index)) continue;
            if (IsNameOfOperand(reference)) continue;

            var card = OwningCard(reference, cards);
            if (card is null) continue;

            if (!reached[index].Any(seen => seen.Span == card.Span))
                reached[index].Add(card);
        }

        var findings = new List<Finding>();

        for (var index = 0; index < slots.Count; index++)
        {
            if (reached[index].Count < 2) continue;

            findings.Add(new Finding(
                "(" + string.Join(", ", slots[index].Names) + ")",
                reached[index].Select(CardTitle).ToList(),
                slots[index].Line));
        }

        return new PageScan(findings, cards.Count, slots.Count);
    }

    // ── the gate ─────────────────────────────────────────────────────────────

    [Fact]
    public void StateSlots_AreNotSharedAcrossSampleCards()
    {
        var offenders = new List<string>();
        var pages = 0;
        var cards = 0;
        var slots = 0;

        foreach (var (path, root) in GallerySources.Pages())
        {
            var scan = ScanPage(root);
            pages++;
            cards += scan.Cards;
            slots += scan.Slots;

            offenders.AddRange(scan.Findings.Select(f =>
                $"{GallerySources.Rel(path)}:{f.Line}: {f.Describe()}"));
        }

        // Floors rather than `> 0`: a loader that silently found one page, or a slot detector that
        // stopped recognising the deconstruction shape, would still clear `> 0` while inspecting
        // almost nothing. These sit well under the current tree (94 pages / 214 cards / 142 slots)
        // so ordinary gallery churn never trips them, but a collapse of any input fails here
        // instead of passing quietly.
        Assert.True(pages >= 80, $"only {pages} gallery pages were inspected — the lint is barely looking.");
        Assert.True(cards >= 150, $"only {cards} SampleCard invocations were inspected — the lint is barely looking.");
        Assert.True(slots >= 100, $"only {slots} state slots were found — the slot detector has stopped recognising them.");

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} state slot(s) are shared across sample cards " +
            $"({pages} pages, {cards} cards, {slots} slots inspected):" +
            global::System.Environment.NewLine +
            string.Join(global::System.Environment.NewLine, offenders));
    }

    // ── the rule, pinned against the shipped defect and against itself ───────

    /// <summary>Assembles a synthetic page so a fixture reads like a real one.</summary>
    internal static string Page(string body) =>
        $$"""
        namespace Gallery;

        class __Page : Component
        {
            public override Element Render()
            {
        {{body}}
            }
        }
        """;

    static string[] Signatures(string body) =>
        ScanSource(Page(body)).Findings.Select(f => f.Signature()).ToArray();

    /// <summary>
    /// A slot that is unambiguously shared by two cards, appended to a fixture so the fixture
    /// carries a finding the detector must produce. Declared after the fixture's own
    /// <c>return</c>: unreachable, but this tier only parses, and keeping it last means its
    /// finding is always the last one emitted.
    /// </summary>
    const string ProbeBody = """


                var (__probe, set__probe) = UseState(0);
                VStack(
                    SampleCard("__ProbeCardA", NumberBox(__probe, v => set__probe(v)), sourceCode: @"x"),
                    SampleCard("__ProbeCardB", TextBlock($"{__probe}"), sourceCode: @"x"));
        """;

    const string ProbeSignature = "(__probe, set__probe): __ProbeCardA | __ProbeCardB";

    /// <summary>
    /// Asserts a fixture's findings twice: once as written, and once with <see cref="ProbeBody"/>
    /// appended.
    ///
    /// <para>The second call is what makes a <em>silent</em> expectation worth anything. "No
    /// finding" is also what a detector that has stopped reporting produces, so a bare
    /// <c>Assert.Empty</c> passes just as happily against a dead rule as against a correct one —
    /// it cannot come out the other way. With the probe appended the case must report the probe
    /// and nothing else, which fails if the detector went silent, fails if it never parsed the
    /// fixture, and fails again if it over-reports on the shape the case is actually about.</para>
    /// </summary>
    static void AssertFindings(string label, string body, params string[] expected)
    {
        Assert.Equal(expected, Signatures(body));
        Assert.Equal([.. expected, ProbeSignature], Signatures(body + ProbeBody));
    }

    /// <summary>
    /// The known-positive: the exact shape NumberBoxPage shipped when #980 was filed. If a change
    /// to the detector stops reporting this, the detector no longer catches the defect it was
    /// written for and this fails rather than going quietly green.
    /// </summary>
    [Fact]
    public void TheShippedNumberBoxDefect_IsReported()
    {
        var finding = Assert.Single(ScanSource(Page("""
                    var (value, setValue) = UseState(0.0);
                    var (rangeValue, setRangeValue) = UseState(50.0);

                    return ScrollView(VStack(16,
                        SampleCard("Basic NumberBox",
                            VStack(8,
                                NumberBox(value, v => setValue(v), "Enter a number"),
                                TextBlock($"Value: {value}")),
                            sourceCode: @"NumberBox(value, v => setValue(v), ""Enter a number"")"),

                        SampleCard("NumberBox with Spin Buttons",
                            NumberBox(value, v => setValue(v), "Quantity").SpinButtons(),
                            sourceCode: @"NumberBox(value, v => setValue(v), ""Quantity"").SpinButtons()"),

                        SampleCard("NumberBox with Range",
                            NumberBox(rangeValue, v => setRangeValue(v), "Percentage").Range(0, 100),
                            sourceCode: @"NumberBox(rangeValue, v => setRangeValue(v), ""Percentage"")")));
        """)).Findings);

        Assert.Equal("(value, setValue): Basic NumberBox | NumberBox with Spin Buttons", finding.Signature());
    }

    /// <summary>
    /// The shapes that must stay silent are the ones that decide whether this lint can be a gate
    /// at all: it has no allowlist, so any of these firing is a blocked tree on correct code.
    /// </summary>
    [Theory]
    // One slot per card is the fix, and the shape most of the gallery already has.
    [InlineData("one slot per card", """
                var (value, setValue) = UseState(0.0);
                var (spinValue, setSpinValue) = UseState(0.0);

                return VStack(
                    SampleCard("Basic", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                    SampleCard("Spin", NumberBox(spinValue, v => setSpinValue(v)), sourceCode: @"x"));
        """)]
    // A page-level readout outside every card is shared context, not a second card.
    [InlineData("card plus page-level element", """
                var (value, setValue) = UseState(0.0);

                return VStack(
                    TextBlock($"Value: {value}"),
                    SampleCard("Basic", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                    SampleCard("Other", TextBlock("no state here"), sourceCode: @"x"));
        """)]
    // `options:` is a fourth argument of the same SampleCard call, so a slot driving a card's
    // option panel and its sample is reached from one card.
    [InlineData("sample and its own options panel", """
                var (value, setValue) = UseState(0.0);

                return VStack(
                    SampleCard("Basic", NumberBox(value, v => setValue(v)), sourceCode: @"x",
                        options: OptionPanel(Slider(value, 0, 100, v => setValue(v)))),
                    SampleCard("Other", TextBlock("no state here"), sourceCode: @"x"));
        """)]
    // An ordinary tuple carries no per-card contract — shared data is normal and not this defect.
    [InlineData("non-hook tuple shared by two cards", """
                var (items, count) = LoadSampleData();

                return VStack(
                    SampleCard("First", ListView(items), sourceCode: @"x"),
                    SampleCard("Second", TextBlock($"{count} items: {items}"), sourceCode: @"x"));
        """)]
    // Derived values are not per-card state; UseMemo is deliberately outside the hook set.
    [InlineData("UseMemo value shared by two cards", """
                var rows = UseMemo(() => BuildRows(), []);

                return VStack(
                    SampleCard("First", DataGrid(rows), sourceCode: @"x"),
                    SampleCard("Second", ListView(rows), sourceCode: @"x"));
        """)]
    // The name only appears in the snippet, which is a string literal and binds nothing live.
    [InlineData("name appears only in a sibling snippet", """
                var (value, setValue) = UseState(0.0);

                return VStack(
                    SampleCard("Basic", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                    SampleCard("Spin", NumberBox(0.0), sourceCode: @"NumberBox(value, v => setValue(v))"));
        """)]
    // `nameof` names the slot without reading it — no render depends on the state.
    [InlineData("nameof mentions the slot without reading it", """
                var (value, setValue) = UseState(0.0);

                return VStack(
                    SampleCard("Basic", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                    SampleCard("Other", TextBlock(nameof(value)), sourceCode: @"x"));
        """)]
    public void IndependentPages_AreNotReported(string label, string body) =>
        AssertFindings(label, body);

    /// <summary>
    /// Coupling across more than two cards is reported once, listing every card involved — the
    /// AutoSuggestBox shape, where one <c>query</c> drove three cards.
    /// </summary>
    [Fact]
    public void SlotSharedByThreeCards_IsReportedOnceWithEveryCard()
    {
        var signature = Assert.Single(Signatures("""
                    var (query, setQuery) = UseState("");

                    return VStack(
                        SampleCard("Basic", AutoSuggestBox(query, setQuery), sourceCode: @"x"),
                        SampleCard("Submitted", AutoSuggestBox(query, setQuery), sourceCode: @"x"),
                        SampleCard("Filtered", TextBlock($"{query}"), sourceCode: @"x"));
        """));

        Assert.Equal("(query, setQuery): Basic | Submitted | Filtered", signature);
    }

    /// <summary>
    /// <c>UseReducer</c> mints per-card state exactly as <c>UseState</c> does, and its pair is not
    /// spelled <c>(x, setX)</c> — which is why the rule keys on the hook rather than on the names.
    /// </summary>
    [Fact]
    public void UseReducerSlot_IsCoveredEvenThoughItsPairIsNotSetPrefixed()
    {
        var signature = Assert.Single(Signatures("""
                    var (log, updateLog) = UseReducer<IReadOnlyList<string>>([]);

                    return VStack(
                        SampleCard("Navigate", Button("Go", () => updateLog(l => l)), sourceCode: @"x"),
                        SampleCard("Log", TextBlock($"{log.Count}"), sourceCode: @"x"));
        """));

        Assert.Equal("(log, updateLog): Navigate | Log", signature);
    }

    /// <summary>
    /// Attribution is by innermost containing card, so a card built inside another card's sample
    /// counts as its own card rather than being absorbed into its host. Without that, a slot used
    /// by a host and by the card nested in it would read as one card and escape the rule.
    /// </summary>
    [Fact]
    public void NestedCard_CountsAsItsOwnCard()
    {
        var signature = Assert.Single(Signatures("""
                    var (value, setValue) = UseState(0.0);

                    return SampleCard("Outer",
                        VStack(
                            NumberBox(value, v => setValue(v)),
                            SampleCard("Inner", TextBlock($"{value}"), sourceCode: @"x")),
                        sourceCode: @"x");
        """));

        Assert.Equal("(value, setValue): Outer | Inner", signature);
    }

    /// <summary>
    /// Member names are not references to a slot: a live <c>p.Value</c> or a <c>value:</c> argument
    /// label must not make a second card look like it reads the slot. This is the classifier the
    /// snippet-agreement lint already uses, reused rather than re-derived so the two cannot drift.
    /// </summary>
    [Fact]
    public void MemberNamesAndArgumentLabels_DoNotCountAsReferences() =>
        AssertFindings("member names and argument labels", """
                    var (value, setValue) = UseState(0.0);

                    return VStack(
                        SampleCard("Basic", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                        SampleCard("Other", Gauge(reading.value, value: 3), sourceCode: @"x"));
        """);

    /// <summary>
    /// <c>UseState</c> returns the named tuple <c>(T Value, Action&lt;T&gt; Set)</c>, so a page can
    /// hold the whole tuple in one local and drive cards through <c>s.Value</c> / <c>s.Set</c>
    /// instead of deconstructing it. That is the same slot reaching the same two cards, and a rule
    /// that only understood the deconstruction could be evaded by rewriting a single line.
    /// </summary>
    [Fact]
    public void WholeTupleSlot_IsCoveredEvenThoughItIsNeverDeconstructed()
    {
        var signature = Assert.Single(Signatures("""
                    var counter = UseState(0.0);

                    return VStack(
                        SampleCard("Basic", NumberBox(counter.Value, v => counter.Set(v)), sourceCode: @"x"),
                        SampleCard("Readout", TextBlock($"{counter.Value}"), sourceCode: @"x"));
        """));

        Assert.Equal("(counter): Basic | Readout", signature);
    }

    /// <summary>
    /// The probe the silent cases lean on has to be worth leaning on: it must produce exactly one
    /// finding when it stands alone. If this fails, every <see cref="AssertFindings"/> caller is
    /// asserting against a probe that proves nothing.
    /// </summary>
    [Fact]
    public void TheProbe_ReportsExactlyItself() =>
        Assert.Equal([ProbeSignature], Signatures("return VStack();" + ProbeBody));
}
