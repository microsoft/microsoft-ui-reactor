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
/// <para>Lambda-parameter shadowing is not on this list, but the reason is narrower than it
/// first looks and only the narrow version holds. Inside the member that declares the slot, a
/// lambda parameter cannot reuse the name (CS0136), so in code that compiles no shadowed name
/// exists to be misattributed. That argument stops at the member boundary: CS0136 says nothing
/// about a lambda parameter in a <em>sibling</em> method, which compiles happily and — since
/// this scan walks the file, not the <c>Render</c> node — would be read as a reference to the
/// slot. What rules that out is not CS0136 but scope: attribution requires a reference to sit
/// in the same member that declares the slot (see <see cref="DeclaringMember"/>). Pinned by
/// <see cref="ALambdaParameterInAnotherMethod_DoesNotCountAsAReference"/>, which reported
/// <c>(value, setValue): Basic | Extras</c> against a correct page before that bound existed.
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

    /// <param name="Unadjudicable">
    /// Set when the scan could not resolve <paramref name="Slot"/> to a single declaration, so it
    /// reports that the rule <em>cannot be checked</em> here rather than that a slot is shared.
    /// The two carry different evidence and must not share a message: a coupling is observed, an
    /// ambiguity is precisely the absence of an observation.
    /// </param>
    internal readonly record struct Finding(
        string Slot,
        IReadOnlyList<string> Cards,
        int Line,
        bool Unadjudicable = false)
    {
        /// <summary>Compact form the detector's own tests compare against.</summary>
        public string Signature() => Unadjudicable
            ? $"{Slot}: AMBIGUOUS: {string.Join(" | ", Cards)}"
            : $"{Slot}: {string.Join(" | ", Cards)}";

        /// <remarks>
        /// Only the first sentence is measured. This lint reads source and never renders, so it
        /// observes the shared slot and <i>infers</i> the coupling; it also cannot tell a mistake
        /// from a deliberate share. Both remedies are offered rather than the likelier one alone,
        /// because a message that names a single cause pushes the reader toward a single fix — and
        /// in the one case the lint cannot distinguish, that fix is the wrong one.
        ///
        /// <para>The unadjudicable form claims strictly less: how many declarations bind the name
        /// and which cards its references reach, both measured, and no coupling either way.</para>
        /// </remarks>
        public string Describe() => Unadjudicable
            ? $"the name {Slot} is bound by more than one state slot in the same member, so the scan " +
              $"cannot tell which slot each reference means. Its references reach {Cards.Count} " +
              "sample cards — " + string.Join(", ", Cards.Select(c => $"\"{c}\"")) + ". Whether that " +
              "is one slot shared across cards or separate slots that happen to agree on a name is " +
              "exactly what the ambiguity hides, so the rule cannot be checked here either way. " +
              "Give the slots distinct names."
            : $"the {Slot} state slot is wired into {Cards.Count} sample cards — " +
              string.Join(", ", Cards.Select(c => $"\"{c}\"")) + ". " +
              "Cards are meant to be independent demonstrations, so this almost always means driving " +
              "one silently moves the others. Give every card its own state hook — or, if the sharing " +
              "is deliberate, fold them into a single card.";
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
    /// <item><c>var s = UseState(...)</c> — <see cref="Microsoft.UI.Reactor.Core.RenderContext.UseState{T}(T, bool)"/>
    /// returns the <em>named</em> tuple <c>(T Value, Action&lt;T&gt; Set)</c>, so <c>s.Value</c> /
    /// <c>s.Set</c> is legal and drives a card exactly as the deconstructed pair does. Missing it
    /// would leave the rule avoidable by rewriting one line.</item>
    /// </list>
    /// Discards are skipped — they bind nothing and so can never be referenced from a card.
    /// Returned in source order, which is what <c>DescendantNodes</c> pre-order already gives.
    /// </summary>
    internal static IReadOnlyList<(IReadOnlyList<string> Names, int Line, SyntaxNode? Scope)> StateSlots(SyntaxNode pageRoot)
    {
        var slots = new List<(IReadOnlyList<string> Names, int Line, SyntaxNode? Scope)>();

        foreach (var node in pageRoot.DescendantNodes())
        {
            IReadOnlyList<string>? names = null;

            if (node is AssignmentExpressionSyntax assignment
                && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && assignment.Left is DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax designation }
                && IsStateHook(assignment.Right))
            {
                // Discards carry no name a card can read, so they never bind a slot. The two
                // spellings need different exclusions and only one is structural: Roslyn parses
                // `_` in a deconstruction as DiscardDesignationSyntax, which this OfType drops —
                // but `var _ = UseState(...)` below parses as an ordinary declarator named `_`,
                // which nothing about its shape distinguishes from a real local.
                names = designation.Variables
                    .OfType<SingleVariableDesignationSyntax>()
                    .Select(v => v.Identifier.Text)
                    .Where(n => !IsDiscard(n))
                    .ToList();
            }
            else if (node is VariableDeclaratorSyntax { Initializer: { } initializer } declarator
                && IsStateHook(initializer.Value))
            {
                names = IsDiscard(declarator.Identifier.Text) ? [] : [declarator.Identifier.Text];
            }


            if (names is null || names.Count == 0) continue;

            slots.Add((names, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, DeclaringMember(node)));
        }

        return slots;
    }

    /// <summary>
    /// A name that binds nothing readable. Covers the empty identifier a malformed parse can
    /// produce as well as <c>_</c>, which is the one name C# exempts from the duplicate-name
    /// rule — so unlike every other slot name it can legally appear twice in one scope, and a
    /// name-keyed scan must not treat the two as the same slot.
    /// </summary>
    static bool IsDiscard(string name) => name.Length == 0 || name == "_";

    /// <summary>
    /// The member whose body a node sits in — the scope a local declared there can be read from.
    ///
    /// <para>This is what bounds a slot's reach. A slot is a local, and a local is invisible
    /// outside the member that declares it, so an identifier in a <em>different</em> member
    /// spelling the same name is a different symbol by the language rules rather than by
    /// guesswork. Without this bound the scan is file-wide and a lambda parameter, loop
    /// variable, or local in a sibling method silently reads as a reference to the slot.</para>
    ///
    /// <para>An ordinary local function is deliberately <em>not</em> a boundary. It captures the
    /// enclosing method's locals, so a card extracted into one still reads the very same slot —
    /// treating it as its own scope made that reference fail to resolve and hid real coupling.
    /// Nothing can shadow the slot inside it either, since CS0136 does reach into a nested local
    /// function body. A <c>static</c> local function is a boundary, because <c>static</c> is
    /// precisely the modifier that severs capture.</para>
    /// </summary>
    static SyntaxNode? DeclaringMember(SyntaxNode node) =>
        node.Ancestors().FirstOrDefault(a =>
            a is MemberDeclarationSyntax
            || (a is LocalFunctionStatementSyntax local && local.Modifiers.Any(SyntaxKind.StaticKeyword)));

    /// <summary>
    /// A value-typed identity for a declaring member, so a scope can be part of a dictionary key
    /// without relying on syntax-node reference identity. Two members cannot begin at the same
    /// offset in one file, so the start of a member's span names it exactly; <c>-1</c> stands for
    /// the file scope a node outside any member sits in.
    /// </summary>
    static int ScopeKey(SyntaxNode? scope) => scope?.SpanStart ?? -1;

    // ── attributing a reference to a card ────────────────────────────────────

    /// <summary>
    /// The card an identifier sits inside, or null when it sits in the page's shared context.
    /// The <em>innermost</em> containing invocation wins, so a card nested inside another card's
    /// sample is attributed to itself rather than to its host.
    /// </summary>
    static InvocationExpressionSyntax? OwningCard(SyntaxNode node, IReadOnlyList<InvocationExpressionSyntax> cards)
    {
        // A single pass rather than Where(...).OrderBy(...).FirstOrDefault(): this runs once per
        // identifier reference on every gallery page, and sorting to read one element allocated an
        // iterator and a sorted buffer each time. Strict `<` keeps the first of any equal-length
        // pair, which is what the stable OrderBy did.
        //
        // The condition is not a filter, which is why it stays a loop: the second conjunct reads
        // `innermost`, so it depends on mutable loop state and cannot be hoisted into a Where.
        // Only the containment half could move, and splitting the two would reintroduce the
        // per-call allocation this exists to avoid without removing the `if`.
        InvocationExpressionSyntax? innermost = null;

        foreach (var card in cards)
            if (card.Span.Contains(node.Span)
                && (innermost is null || card.Span.Length < innermost.Span.Length))
                innermost = card;

        return innermost;
    }

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

        // A name can only speak for a slot inside the member that declares it, so the lookup is
        // keyed by (member, name). Scoping the key is load-bearing in *both* directions, and the
        // second is the easy one to miss: a sibling member's identically-named local must not be
        // mistaken for this slot, and it must not collide with it either — a file-wide key marks
        // the name ambiguous and then drops a genuine finding in Render(), which is a silent miss
        // of the very defect this gate exists to catch. CS0136 does not reach across member
        // bodies, so two members each holding a `value` slot is ordinary compiling code.
        //
        // Ambiguity stays real *within* one member: two slots in non-overlapping blocks may both
        // bind `value`, and there the scan genuinely cannot say which one a reference means.
        var slotOfName = new Dictionary<(int Scope, string Name), int>();
        var ambiguous = new HashSet<(int Scope, string Name)>();

        for (var index = 0; index < slots.Count; index++)
        {
            // Loop-invariant across the slot's names, so it is read once rather than per name.
            var scope = ScopeKey(slots[index].Scope);

            foreach (var key in slots[index].Names.Select(name => (scope, name)))
            {
                if (slotOfName.TryGetValue(key, out var existing) && existing != index)
                    ambiguous.Add(key);
                else
                    slotOfName[key] = index;
            }
        }

        // Cards reached per slot, in source order, de-duplicated by the card's own span.
        var reached = slots.Select(_ => new List<InvocationExpressionSyntax>()).ToList();

        // Cards reached by a name the scan could *not* narrow to one slot. Tracked rather than
        // dropped. Skipping an unresolvable name looks conservative and is not: the rule cannot be
        // checked on that name, so staying silent reports "no coupling here" on the one input where
        // the scan has no idea — a false negative on exactly the defect this gate exists to catch,
        // and one no allowlist is needed to produce.
        var reachedByAmbiguous = new Dictionary<(int Scope, string Name), List<InvocationExpressionSyntax>>();

        // A `sourceCode:` snippet is a string literal, so it contributes no IdentifierNameSyntax
        // and needs no special-casing: only the live half of a card is ever inspected here.
        foreach (var reference in GallerySnippetAgreementTests.ReferenceNames(pageRoot))
        {
            // The declaring member is part of the key, so a reference from another member simply
            // fails to resolve and no separate containment check is needed after the lookup.
            var key = (ScopeKey(DeclaringMember(reference)), reference.Identifier.Text);

            if (IsNameOfOperand(reference)) continue;

            if (ambiguous.Contains(key))
            {
                if (!reachedByAmbiguous.TryGetValue(key, out var ambiguousCards))
                    reachedByAmbiguous[key] = ambiguousCards = [];

                RecordCard(ambiguousCards, reference);
                continue;
            }

            if (!slotOfName.TryGetValue(key, out var index)) continue;

            RecordCard(reached[index], reference);
        }

        void RecordCard(List<InvocationExpressionSyntax> into, IdentifierNameSyntax reference)
        {
            var card = OwningCard(reference, cards);
            if (card is null) return;

            if (!into.Any(seen => seen.Span == card.Span))
                into.Add(card);
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

        // An ambiguous name that reaches two or more cards is reported as unadjudicable. Reaching
        // one card or none stays silent: there is nothing for the ambiguity to be hiding, so a page
        // that merely reuses a name across two disjoint blocks is not blocked by it. That keeps the
        // arm scoped to the case where the missing information is load-bearing, which matters here
        // because this gate has no allowlist and a finding it raises cannot be waived.
        foreach (var entry in reachedByAmbiguous.OrderBy(e => e.Key.Name, StringComparer.Ordinal))
        {
            if (entry.Value.Count < 2) continue;

            findings.Add(new Finding(
                entry.Key.Name,
                entry.Value.Select(CardTitle).ToList(),
                slotOfName.TryGetValue(entry.Key, out var declaredAt) ? slots[declaredAt].Line : 0,
                Unadjudicable: true));
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
    /// A lambda parameter in a <em>different method</em> that reuses a slot's name. CS0136 does
    /// not reach across member bodies, so unlike the in-<c>Render</c> case this compiles — and
    /// the scan walks the whole file, so before <see cref="DeclaringMember"/> bounded
    /// attribution this reported <c>(value, setValue): Basic | Extras</c> against a page whose
    /// two cards share nothing. This lint has no allowlist, so that false positive is a blocked
    /// tree, which makes it the more expensive direction to be wrong in.
    ///
    /// <para>Written against a hand-built page rather than <see cref="Page"/>, which only ever
    /// emits one method. Asserted on the finding <em>list</em> rather than a count so a
    /// regression names the card it wrongly convicted.</para>
    /// </summary>
    [Fact]
    public void ALambdaParameterInAnotherMethod_DoesNotCountAsAReference()
    {
        var scan = ScanSource("""
            namespace Gallery;

            class __Page : Component
            {
                public override Element Render()
                {
                    var (value, setValue) = UseState(0.0);

                    return VStack(
                        SampleCard("Basic", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                        Extras());
                }

                static Element Extras() =>
                    SampleCard("Extras",
                        TextBlock(string.Join(",", Labels.Select(value => value.Trim()))),
                        sourceCode: @"x");
            }
            """);

        Assert.Equal([], scan.Findings.Select(f => f.Signature()));

        // …and the page really was scanned, so the silence above is a verdict rather than a
        // parse that found nothing to look at.
        Assert.Equal(2, scan.Cards);
        Assert.Equal(1, scan.Slots);
    }

    /// <summary>
    /// Two cards that each discard the value half of their own slot. <c>_</c> is the one name
    /// the CS0136 argument in the class comment does not cover — discards are exempt from the
    /// duplicate-name rule, so two <c>_</c> designations in one scope are legal and a name-keyed
    /// scan could see "the same slot" on both cards. These cards are independent.
    /// </summary>
    [Fact]
    public void TwoCardsEachDiscardingTheirOwnSlot_AreNotReported() =>
        AssertFindings("two independent discarded slots", """
                    var (_, setA) = UseState(0);
                    var (_, setB) = UseState(0);

                    return VStack(
                        SampleCard("A", Button("a", () => setA(1)), sourceCode: @"x"),
                        SampleCard("B", Button("b", () => setB(1)), sourceCode: @"x"));
            """);

    /// <summary>
    /// The whole-tuple spelling, <c>var _ = UseState(...)</c>. Roslyn parses this as an ordinary
    /// <see cref="VariableDeclaratorSyntax"/> named <c>_</c>, <em>not</em> as a
    /// <c>DiscardDesignationSyntax</c> — so the structural exclusion that covers the
    /// deconstruction branch does not reach this one. A discard cannot be read, so a slot bound
    /// only to <c>_</c> can never couple two cards and must not enter the slot list under either
    /// spelling.
    /// </summary>
    [Fact]
    public void AWholeTupleDiscard_IsNotASlot()
    {
        var scan = ScanSource(Page("""
                    var _ = UseState(0);
                    var (kept, setKept) = UseState(0);

                    return VStack(
                        SampleCard("A", Button("a", () => setKept(1)), sourceCode: @"x"),
                        SampleCard("B", TextBlock($"{kept}"), sourceCode: @"x"));
            """));

        // `kept` is genuinely shared, so the page has exactly one finding — and the discard
        // contributes neither a slot of its own nor a second signature.
        Assert.Equal(["(kept, setKept): A | B"], scan.Findings.Select(f => f.Signature()));
        Assert.Equal(1, scan.Slots);
    }

    /// <summary>
    /// The dual of <see cref="ALambdaParameterInAnotherMethod_DoesNotCountAsAReference"/>, and the
    /// more dangerous direction. CS0136 does not reach across member bodies, so a page may legally
    /// declare a <c>value</c> slot in <c>Render</c> <em>and</em> another in a sibling method. While
    /// the name→slot map was keyed on the identifier alone, that collision marked <c>value</c>
    /// ambiguous and dropped every reference to it — so a genuine two-card coupling in
    /// <c>Render</c> went unreported.
    ///
    /// <para>Bounding attribution by <see cref="DeclaringMember"/> did not fix this: that check ran
    /// <em>after</em> the lookup, by which point the name had already collapsed. The scope has to be
    /// part of the key. A false negative is the worse failure for a gate than a false positive —
    /// a blocked tree gets investigated, a silent one does not.</para>
    /// </summary>
    [Fact]
    public void ASameNamedSlotInASiblingMember_DoesNotHideARealSharedSlot()
    {
        var scan = ScanSource("""
            namespace Gallery;

            class __Page : Component
            {
                public override Element Render()
                {
                    var (value, setValue) = UseState(0.0);

                    return VStack(
                        SampleCard("Basic", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                        SampleCard("Spin", NumberBox(value, v => setValue(v)), sourceCode: @"x"));
                }

                static Element Aside()
                {
                    var (value, setValue) = UseState(0.0);
                    return NumberBox(value, v => setValue(v));
                }
            }
            """);

        // The coupling in Render is still convicted, and named, despite the sibling's `value`.
        Assert.Equal(["(value, setValue): Basic | Spin"], scan.Findings.Select(f => f.Signature()));

        // Both slots were seen — so the finding above is the scan distinguishing them, not the
        // scan having missed the sibling declaration altogether.
        Assert.Equal(2, scan.Slots);
    }

    /// <summary>
    /// Two slots binding the same name in <em>one</em> member — legal C# when the blocks do not
    /// overlap, and the residue of a false negative already fixed once here. Adding the declaring
    /// member to the key stopped a <em>sibling</em> member's slot from colliding
    /// (<see cref="ASameNamedSlotInASiblingMember_DoesNotHideARealSharedSlot"/>); it shrank the
    /// ambiguous set without emptying it.
    ///
    /// <para>The scan cannot say which declaration a reference means, and the arm that handles
    /// that used to <c>continue</c>. Skipping reads as conservative and is the opposite: silence
    /// from this gate means "these cards are independent", so an unresolvable name returned the
    /// clean answer on the one input where the scan knew nothing. Reporting it as
    /// <c>Unadjudicable</c> claims only what was measured — more than one binding, and the cards
    /// the references reach.</para>
    ///
    /// <para>Measured before it was written: zero gallery pages bind a slot name twice in one
    /// member, so this fires on nothing today. That is what makes the synthetic drive necessary —
    /// an arm the real corpus never exercises is one no run has ever watched reject anything.</para>
    /// </summary>
    [Fact]
    public void AnUnresolvableSlotName_IsReportedRatherThanSilentlyDropped()
    {
        var scan = ScanSource("""
            namespace Gallery;

            class __Page : Component
            {
                public override Element Render()
                {
                    Element first, second, third;

                    {
                        var (value, setValue) = UseState(0.0);
                        first = SampleCard("Basic", NumberBox(value, v => setValue(v)), sourceCode: @"x");
                        second = SampleCard("Spin", NumberBox(value, v => setValue(v)), sourceCode: @"x");
                    }

                    {
                        var (value, setValue) = UseState(1.0);
                        third = SampleCard("Other", NumberBox(value, v => setValue(v)), sourceCode: @"x");
                    }

                    return VStack(first, second, third);
                }
            }
            """);

        // Both halves of the pair are ambiguous, so both are named. The signature says AMBIGUOUS
        // rather than asserting a coupling — the scan cannot tell whether "Basic"/"Spin" share the
        // first slot or not, and that inability is the finding.
        Assert.Equal(
            ["setValue: AMBIGUOUS: Basic | Spin | Other", "value: AMBIGUOUS: Basic | Spin | Other"],
            scan.Findings.Select(f => f.Signature()));

        // Both declarations were seen, so the report above is the scan declining to adjudicate two
        // known slots rather than having missed one of them.
        Assert.Equal(2, scan.Slots);
    }

    /// <summary>
    /// The other half of the ambiguity arm: a name the scan cannot resolve, whose references reach
    /// fewer than two cards, is harmless — nothing is being hidden — and must not block the tree.
    /// This gate has no allowlist, so an over-eager arm here has no escape hatch.
    ///
    /// <para>Written as a conviction rather than as <c>Assert.Empty</c>. A page asserting only
    /// silence can be satisfied by the scan collapsing everything, which is the failure this arm
    /// exists to prevent; pairing the harmless duplicate with a real coupling means a collapse
    /// takes the conviction with it and the test reddens.</para>
    /// </summary>
    [Fact]
    public void AnUnresolvableNameReachingNoCard_StaysSilentAndHidesNothing()
    {
        var scan = ScanSource("""
            namespace Gallery;

            class __Page : Component
            {
                public override Element Render()
                {
                    var (shared, setShared) = UseState(0.0);

                    { var (tmp, setTmp) = UseState(1); setTmp(2); }
                    { var (tmp, setTmp) = UseState(3); setTmp(4); }

                    return VStack(
                        SampleCard("Basic", NumberBox(shared, v => setShared(v)), sourceCode: @"x"),
                        SampleCard("Spin", NumberBox(shared, v => setShared(v)), sourceCode: @"x"));
                }
            }
            """);

        // `tmp`/`setTmp` are ambiguous but reach no card, so they are not reported — and the real
        // coupling on `shared` is still convicted despite sharing the member with them.
        Assert.Equal(["(shared, setShared): Basic | Spin"], scan.Findings.Select(f => f.Signature()));

        // All three declarations were seen, so the silence about `tmp` is the arm declining to
        // report a harmless ambiguity rather than the slot scan having skipped the blocks.
        Assert.Equal(3, scan.Slots);
    }

    /// <summary>
    /// A card extracted into a local function. A local function <em>captures</em> the enclosing
    /// method's locals, so the slot it reads there is the same slot — but while
    /// <see cref="DeclaringMember"/> treated any local function as its own scope, that reference
    /// failed to resolve and this page came back clean despite two cards sharing one slot.
    ///
    /// <para>The likely way to hit it is a refactor rather than fresh code: pulling a card body
    /// out into a helper is a natural tidy-up, and it silently switched the gate off for that
    /// card. Nothing about a green run would have said so.</para>
    /// </summary>
    [Fact]
    public void ACardExtractedIntoALocalFunction_StillReachesTheEnclosingSlot()
    {
        var scan = ScanSource("""
            namespace Gallery;

            class __Page : Component
            {
                public override Element Render()
                {
                    var (value, setValue) = UseState(0.0);

                    return VStack(
                        SampleCard("Basic", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                        Extra());

                    Element Extra() =>
                        SampleCard("Extra", NumberBox(value, v => setValue(v)), sourceCode: @"x");
                }
            }
            """);

        Assert.Equal(["(value, setValue): Basic | Extra"], scan.Findings.Select(f => f.Signature()));
    }

    /// <summary>
    /// The complement of the case above, and the reason <see cref="DeclaringMember"/> carves out
    /// <c>static</c> rather than treating every local function alike. A <c>static</c> local
    /// function cannot capture, so the enclosing method's locals are not in scope inside it — and
    /// C# therefore permits it to declare a local of the same name. Verified by compiling the
    /// shape, not by reading the rule: <c>static void Local() { int value = 2; }</c> nested in a
    /// method holding its own <c>value</c> builds clean, 0 warnings. This is the third construct
    /// exempt from CS0136, after a discard and a lambda parameter in a sibling member.
    ///
    /// <para>The obvious form of this test — a page with no coupling, asserting the scan stays
    /// silent — is <em>vacuous</em>, and was written that way first. Silence has two producers:
    /// the scope rule telling the two <c>value</c>s apart, or the two collapsing onto one key,
    /// being marked ambiguous and dropped. Deleting the carve-out swaps the first for the second
    /// and the assertion never notices. So this is the <see cref="ASameNamedSlotInASiblingMember_DoesNotHideARealSharedSlot"/>
    /// shape instead: give <c>Render</c> a <em>real</em> coupling, then require it to be convicted
    /// and named <em>despite</em> the static local function's same-named slot. Now the collapse has
    /// somewhere to show — it drops the finding, and the assertion reddens.</para>
    ///
    /// <para>Each carve-out in the scope rule earns a test, because the rule is an approximation of
    /// name resolution and approximations fail silently; and each such test has to assert a
    /// conviction, because that is the only outcome the approximation cannot fake.</para>
    /// </summary>
    [Fact]
    public void AStaticLocalFunctionsOwnSlot_DoesNotHideARealSharedSlot()
    {
        var scan = ScanSource("""
            namespace Gallery;

            class __Page : Component
            {
                public override Element Render()
                {
                    var (value, setValue) = UseState(0.0);

                    return VStack(
                        SampleCard("Basic", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                        SampleCard("Spin", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                        Aside());

                    static Element Aside()
                    {
                        var (value, setValue) = UseState(0.0);
                        return SampleCard("Aside", NumberBox(value, v => setValue(v)), sourceCode: @"x");
                    }
                }
            }
            """);

        // Render's coupling is convicted and named, and `Aside` is absent from it — the static
        // local function's `value` is a different symbol, so neither slot reaches the other's card.
        Assert.Equal(["(value, setValue): Basic | Spin"], scan.Findings.Select(f => f.Signature()));

        // Both slots and all three cards were seen, so the finding above is the scan telling the
        // two `value`s apart rather than having missed one declaration outright.
        Assert.Equal(2, scan.Slots);
        Assert.Equal(3, scan.Cards);
    }

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
    /// A page that only ever writes a slot can discard the value half. The discard binds nothing,
    /// so it cannot be referenced from a card and has no business in the reported signature — and
    /// leaving it in would be worse than untidy: <c>_</c> is the one name that can plausibly recur
    /// across two slots, and a repeated name is treated as ambiguous and dropped, which would take
    /// the real setter names down with it. Still a shared slot, still reported, named for the half
    /// that actually binds.
    /// </summary>
    [Fact]
    public void DiscardHalfOfASlot_IsNotPartOfTheSignature() =>
        AssertFindings("discarded value half", """
                    var (_, setValue) = UseState(0.0);

                    return VStack(
                        SampleCard("One", Button("a", () => setValue(1)), sourceCode: @"x"),
                        SampleCard("Two", Button("b", () => setValue(2)), sourceCode: @"x"));
        """, "(setValue): One | Two");

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
