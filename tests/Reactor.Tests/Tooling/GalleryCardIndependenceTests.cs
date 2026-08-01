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
/// <para>One more escapes for a different reason: two cards driven by a page <em>field</em> or
/// property rather than a state slot are coupled in exactly the way this rule is about, and are
/// invisible to it — a field is not minted by a state hook, so it is never a slot. That is the
/// deliberate scope above (shared data is not per-card state) meeting a shape where it happens
/// to be wrong, not an oversight; narrowing it would need to tell mutable per-card state held in
/// a field from the ordinary shared data the same declaration form expresses. Measured rather
/// than assumed: no page under <c>ControlPages/</c> reaches one field or property from two cards
/// today, so this is a documented limit rather than a live miss.</para>
///
/// <para>Each needs call-graph or data-flow reasoning this tier does not have, and silence is
/// the cheaper error: this lint has no allowlist, so a false positive is a blocked tree. The
/// one shape that errs the other way is two <em>mutually exclusive</em> cards — a
/// <c>flag ? SampleCard(a) : SampleCard(b)</c> both naming one slot would be reported although
/// only one ever renders. No gallery page builds a card conditionally; if one ever does, the
/// fix is to give each branch its own slot, which is what the reader would expect anyway.</para>
///
/// <para>Lambda-parameter shadowing <em>was</em> on this list, on an argument that turned out to
/// be false. Inside the member that declares the slot, a lambda parameter was taken to be unable
/// to reuse the name (CS0136), so no shadowed name could exist to be misattributed. Compiling the
/// shape says otherwise: <c>value =&gt; setSpinValue(value)</c> beside a <c>value</c> slot builds
/// clean, as do a parenthesized lambda's parameter, a local-function parameter, and an ordinary
/// local declared in a nested lambda body. CS0136 governs nested <em>blocks</em>; it does not
/// reach across a lambda or local-function boundary. Handled rather than argued away, by
/// <see cref="DeclaredNames"/> — a reference inside a narrower scope binding the same name reads
/// that declaration, not the slot.</para>
///
/// <para>The member bound is still load-bearing and is a separate rule: CS0136 says nothing about
/// a lambda parameter in a <em>sibling</em> method, which compiles happily and — since this scan
/// walks the file, not the <c>Render</c> node — would be read as a reference to the slot.
/// Attribution therefore requires a reference to sit in the same member that declares the slot
/// (see <see cref="DeclaringMember"/>). Pinned by
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

    internal readonly record struct Finding(
        string Slot,
        IReadOnlyList<string> Cards,
        int Line)
    {
        /// <summary>Compact form the detector's own tests compare against.</summary>
        public string Signature() => $"{Slot}: {string.Join(" | ", Cards)}";

        /// <remarks>
        /// Only the first sentence is measured. This lint reads source and never renders, so it
        /// observes the shared slot and <i>infers</i> the coupling; it also cannot tell a mistake
        /// from a deliberate share. Both remedies are offered rather than the likelier one alone,
        /// because a message that names a single cause pushes the reader toward a single fix — and
        /// in the one case the lint cannot distinguish, that fix is the wrong one.
        /// </remarks>
        public string Describe() =>
            $"the {Slot} state slot is wired into {Cards.Count} sample cards — " +
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
    internal static IReadOnlyList<(IReadOnlyList<string> Names, int Line, SyntaxNode? Scope, SyntaxNode Region)> StateSlots(SyntaxNode pageRoot)
    {
        var slots = new List<(IReadOnlyList<string> Names, int Line, SyntaxNode? Scope, SyntaxNode Region)>();

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

            slots.Add((names, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, DeclaringMember(node), DeclaringRegion(node)));
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
    /// <para>No local function is a boundary, <c>static</c> or not. An ordinary one captures the
    /// enclosing method's locals, so a card extracted into one still reads the very same slot, and
    /// treating it as its own scope made that reference fail to resolve and hid real coupling. A
    /// <c>static</c> one severs capture and so <em>was</em> a boundary here, until
    /// <see cref="DeclaredNames"/> arrived: a declaration inside it now shadows the enclosing slot
    /// over exactly its own body, which is the same verdict by a rule that also covers lambdas and
    /// plain nested blocks. Keeping the carve-out as well left a branch no input could distinguish —
    /// deleting it changed nothing in the suite or on the gallery, which is the measurement that
    /// retired it.</para>
    /// </summary>
    static SyntaxNode? DeclaringMember(SyntaxNode node) =>
        node.Ancestors().FirstOrDefault(a => a is MemberDeclarationSyntax);

    /// <summary>
    /// A value-typed identity for a declaring member, so a scope can be part of a dictionary key
    /// without relying on syntax-node reference identity. Two members cannot begin at the same
    /// offset in one file, so the start of a member's span names it exactly; <c>-1</c> stands for
    /// the file scope a node outside any member sits in.
    /// </summary>
    static int ScopeKey(SyntaxNode? scope) => scope?.SpanStart ?? -1;

    /// <summary>
    /// The innermost scope a slot's declaration sits in — the region a reference must sit inside to
    /// be reading <em>that</em> declaration rather than a same-named one beside or around it.
    ///
    /// <para>This is one granularity finer than <see cref="DeclaringMember"/> and exists for two
    /// shapes that key alike but mean different slots. The first is two slots binding one name in
    /// <em>disjoint</em> blocks of a member: CS0136 forbids shadowing, not reuse across scopes that
    /// do not nest, so this compiles and the two must be told apart rather than merged. The second
    /// is a slot redeclared in a <em>nested</em> lambda or non-static local function body, which
    /// also compiles — measured, not assumed: the same redeclaration in a plain nested block is
    /// CS0136, but across a lambda or local-function boundary it is legal, so nesting is real and
    /// the innermost region wins.</para>
    ///
    /// <para>A switch section counts as a region even without braces. Its statements have their own
    /// scope, and a declaration directly under a <c>case</c> label sits in no block of its own; if
    /// the walk skipped past it to the enclosing method block, the region would be wider than the
    /// scope and references after the switch — which bind to a field, not to the local — would be
    /// attributed to that slot. This gate has no allowlist, so that is a blocked tree on correct
    /// code. Unbraced <c>if</c>/<c>while</c> bodies need no such arm: a declaration cannot be an
    /// embedded statement (CS1023).</para>
    ///
    /// <para>Falls back to the declaring member when the declaration sits in no block, and to the
    /// file root when it sits in no member either — the region a file-scope local is readable from.
    /// Widening the fallback keeps those references resolving exactly as they did before regions
    /// existed; narrowing it would drop them silently.</para>
    /// </summary>
    static SyntaxNode DeclaringRegion(SyntaxNode declaration)
    {
        var member = DeclaringMember(declaration);

        foreach (var ancestor in declaration.Ancestors())
        {
            if (ancestor is BlockSyntax or SwitchSectionSyntax) return ancestor;
            if (ancestor == member) break;
        }

        return member ?? declaration.SyntaxTree.GetRoot();
    }

    /// <summary>
    /// The scope a parameter is readable from: the lambda or local function it belongs to, or the
    /// member for an ordinary parameter list. Unlike a local, a parameter is not introduced by a
    /// statement inside a block, so <see cref="DeclaringRegion"/> would walk past the construct
    /// that owns it and hand back a scope wider than the parameter's.
    /// </summary>
    static SyntaxNode ParameterScope(SyntaxNode parameter) =>
        parameter.Ancestors().FirstOrDefault(a =>
            a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax)
        ?? parameter.SyntaxTree.GetRoot();

    /// <summary>
    /// Every name a declaration introduces, paired with the scope it is readable from — slots and
    /// non-slots alike. A reference inside one of these scopes reads <em>that</em> declaration, so
    /// a slot whose region encloses it does not own it.
    ///
    /// <para>This exists because the shadowing rule is narrower than it reads. CS0136 stops a
    /// nested <em>block</em> from reusing an enclosing local's name, and that was taken here as
    /// meaning no shadowed name can exist inside the declaring member at all. It does not reach
    /// across a lambda or local-function boundary: <c>value =&gt; setSpinValue(value)</c> beside a
    /// <c>value</c> slot compiles, as does an ordinary local or <c>catch</c> variable declared in a
    /// nested lambda body. Measured on the repo's language version rather than reasoned about —
    /// simple and parenthesized lambda parameters and local-function parameters all compile, while
    /// the same name on a <c>foreach</c> in the declaring block is CS0136.</para>
    ///
    /// <para>Without this, such a parameter is read as a reference to the slot, and two cards that
    /// each spell one lambda parameter after the page's slot are reported as sharing it. That is a
    /// false positive on compiling code, and this gate has no allowlist to waive it with. No
    /// gallery page does it today; the shape is one rename away, which is exactly the reachability
    /// the switch-section arm has.</para>
    /// </summary>
    static IEnumerable<(string Name, SyntaxNode Scope)> DeclaredNames(SyntaxNode pageRoot)
    {
        foreach (var node in pageRoot.DescendantNodes())
        {
            switch (node)
            {
                case ParameterSyntax parameter:
                    yield return (parameter.Identifier.ValueText, ParameterScope(parameter));
                    break;

                // Covers `var s = UseState(...)` slots as well as ordinary locals and fields. A
                // slot's own declaration yields its own region, never a narrower one, so it can
                // never shadow itself — the containment test below is strict for that reason.
                case VariableDeclaratorSyntax declarator:
                    yield return (declarator.Identifier.ValueText, DeclaringRegion(declarator));
                    break;

                case SingleVariableDesignationSyntax designation:
                    yield return (designation.Identifier.ValueText, DeclaringRegion(designation));
                    break;

                // A loop variable's scope is the loop, not the block around it.
                case ForEachStatementSyntax loop:
                    yield return (loop.Identifier.ValueText, loop);
                    break;

                case CatchDeclarationSyntax caught when caught.Parent is { } clause:
                    yield return (caught.Identifier.ValueText, clause);
                    break;
            }
        }
    }

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
        // keyed by (member, name). CS0136 does not reach across member bodies, so two members each
        // holding a `value` slot is ordinary compiling code, and a file-wide key conflated them —
        // the false negative `64b05561` fixed. The key still carries weight that block resolution
        // below cannot: a `static` local function may shadow the name with an ordinary local that
        // is not a slot at all, and with nothing to resolve against, only the member boundary stops
        // that reference being read as the enclosing slot.
        //
        // Ambiguity stays real *within* one member: two slots in non-overlapping blocks may both
        // bind `value`, and a nested lambda or local-function body may redeclare an enclosing
        // slot's name. Both compile, and each card in such a page is genuinely independent, so the
        // name is resolved at the use site by the innermost region containing it rather than
        // reported. Merging them would invent a coupling; reporting them would block a correct page
        // in a gate that has no allowlist to waive it with.
        var candidates = new Dictionary<(int Scope, string Name), List<int>>();

        for (var index = 0; index < slots.Count; index++)
        {
            // Loop-invariant across the slot's names, so it is read once rather than per name.
            var scope = ScopeKey(slots[index].Scope);

            foreach (var key in slots[index].Names.Select(name => (scope, name)))
            {
                if (!candidates.TryGetValue(key, out var declarations))
                    candidates[key] = declarations = [];

                // One slot can bind a name only once; a repeat is the same declaration seen again.
                if (!declarations.Contains(index)) declarations.Add(index);
            }
        }

        // Cards reached per slot, in source order, de-duplicated by the card's own span.
        var reached = slots.Select(_ => new List<InvocationExpressionSyntax>()).ToList();

        // Same key shape as `candidates`, so a reference looks both up with the one key it already
        // built. These are every declaration of the name, slots included; a slot yields exactly its
        // own region, which the strict containment test in `IsShadowed` discards.
        var shadowers = new Dictionary<(int Scope, string Name), List<SyntaxNode>>();

        foreach (var (name, scope) in DeclaredNames(pageRoot))
        {
            if (IsDiscard(name)) continue;

            var key = (ScopeKey(DeclaringMember(scope)), name);

            if (!shadowers.TryGetValue(key, out var scopes)) shadowers[key] = scopes = [];

            scopes.Add(scope);
        }

        // A `sourceCode:` snippet is a string literal, so it contributes no IdentifierNameSyntax
        // and needs no special-casing: only the live half of a card is ever inspected here.
        foreach (var reference in GallerySnippetAgreementTests.ReferenceNames(pageRoot))
        {
            // The declaring member is part of the key, so a reference from another member simply
            // fails to resolve and no separate containment check is needed after the lookup.
            var key = (ScopeKey(DeclaringMember(reference)), reference.Identifier.Text);

            if (IsNameOfOperand(reference)) continue;

            if (!candidates.TryGetValue(key, out var declarations)) continue;

            var index = Resolve(declarations, reference);

            // Outside every candidate's region, and that is a determination rather than a shrug: a
            // local is visible only inside the scope that declares it, so a reference beyond all of
            // them is reading something else — a field, most likely — and is skipped exactly as a
            // reference from another member is. How many declarations bind the name cannot change
            // what "inside none of them" means, so nothing here is left unadjudicated.
            if (index < 0) continue;

            // Inside the slot's region, but inside something narrower that binds the same name —
            // a lambda parameter, or a local in a nested lambda body. C# gives the reference to the
            // inner declaration, and only a slot can be shared between cards.
            if (shadowers.TryGetValue(key, out var scopes)
                && IsShadowed(reference, slots[index].Region, scopes)) continue;

            RecordCard(reached[index], reference);
        }

        // Which declaration a reference means: the innermost candidate whose region contains it.
        // Candidates in disjoint scopes cannot both contain a point, but nested ones can — a lambda
        // or non-static local function body may redeclare an enclosing slot's name and still
        // compile, where a plain nested block is CS0136 — so the smallest-span tie-break is the arm
        // that picks the inner slot, not defensive padding. Returns -1 when no candidate's region
        // contains the reference, which the caller reads as "this name is not these slots".
        int Resolve(List<int> declarations, SyntaxNode reference)
        {
            var best = -1;

            foreach (var candidate in declarations)
            {
                var region = slots[candidate].Region;

                if (!region.Span.Contains(reference.Span)) continue;

                if (best < 0 || region.Span.Length < slots[best].Region.Span.Length) best = candidate;
            }

            return best;
        }

        // Does something narrower than the slot's region bind the same name over this reference?
        // Strict on both halves: a scope as wide as the region is the slot's own declaration (or a
        // sibling that cannot enclose the reference anyway), and a scope that merely overlaps the
        // region without nesting inside it belongs to a different branch of the tree.
        static bool IsShadowed(SyntaxNode reference, SyntaxNode region, List<SyntaxNode> scopes)
        {
            foreach (var scope in scopes)
            {
                if (scope.Span.Length >= region.Span.Length) continue;

                if (region.Span.Contains(scope.Span) && scope.Span.Contains(reference.Span)) return true;
            }

            return false;
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

        // The header counts findings, not shared slots. Most findings *are* a shared slot, but an
        // unadjudicable one asserts the opposite — that the scan could not tell — so a header
        // spelling every finding as a confirmed share would state the one thing that finding
        // declines to. Each line below says which it is; this line only says how many there are.
        Assert.True(offenders.Count == 0,
            $"{offenders.Count} sample-card state independence finding(s) " +
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
    /// <para>The remaining collision is resolved rather than reported. C# already decides it: a
    /// local is visible only in the scope that declares it, so the innermost candidate region
    /// containing a reference is the declaration that reference means. Merging the two would invent
    /// a coupling across independent cards; dropping the name would answer "these cards are
    /// independent" on the one input where the scan had no idea. Resolving gives the <em>right</em>
    /// answer to both halves of this page at once.</para>
    ///
    /// <para>Which is what this asserts: the first block really does wire one slot into two cards
    /// and is convicted by name, while the second block's identically-named slot drives its own
    /// card and is left alone. A scan that collapsed the two would name all three cards; one that
    /// dropped the name would report nothing at all.</para>
    ///
    /// <para>Measured before it was written: zero gallery pages bind a slot name twice in one
    /// member, so this fires on nothing today. That is what makes the synthetic drive necessary —
    /// an arm the real corpus never exercises is one no run has ever watched reject anything.</para>
    /// </summary>
    [Fact]
    public void SameNamedSlotsInDisjointBlocks_ResolveToTheBlockThatDeclaresThem()
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

        // The first block's slot is wired into two cards and is named as the shared slot it is.
        // "Other" reads the second block's slot and appears nowhere — resolution kept the two
        // apart, so neither an invented three-card coupling nor a silent drop.
        Assert.Equal(["(value, setValue): Basic | Spin"], scan.Findings.Select(f => f.Signature()));

        // Both declarations were seen, so the verdict above is the scan telling two known slots
        // apart rather than having missed one of them.
        Assert.Equal(2, scan.Slots);
    }

    /// <summary>
    /// Two same-named slots in disjoint blocks, each driving <em>its own</em> card. This is correct
    /// independent C# and the exact page an over-eager ambiguity arm blocks: both names collide on
    /// one key, both reach a card, and reporting the collision would convict a page that has
    /// nothing wrong with it. This gate has no allowlist, so a false positive here is a blocked
    /// tree with no escape hatch — the expensive direction.
    ///
    /// <para>Written as a conviction rather than as <c>Assert.Empty</c>. A page asserting only
    /// silence is satisfied by a scan that collapsed everything, which is the opposite failure;
    /// pairing the legitimate duplicate with a real coupling on a third slot means a collapse takes
    /// the conviction with it and the test reddens either way.</para>
    /// </summary>
    [Fact]
    public void SameNamedSlotsEachDrivingTheirOwnCard_AreNotReported()
    {
        var scan = ScanSource("""
            namespace Gallery;

            class __Page : Component
            {
                public override Element Render()
                {
                    var (shared, setShared) = UseState(0.0);
                    Element alpha, beta;

                    { var (value, setValue) = UseState(1.0);
                      alpha = SampleCard("Alpha", NumberBox(value, v => setValue(v)), sourceCode: @"x"); }

                    { var (value, setValue) = UseState(2.0);
                      beta = SampleCard("Beta", NumberBox(value, v => setValue(v)), sourceCode: @"x"); }

                    return VStack(alpha, beta,
                        SampleCard("Basic", NumberBox(shared, v => setShared(v)), sourceCode: @"x"),
                        SampleCard("Spin", NumberBox(shared, v => setShared(v)), sourceCode: @"x"));
                }
            }
            """);

        // "Alpha" and "Beta" are independent despite sharing a slot *name*, so neither the pair nor
        // an unadjudicable notice appears — and the real coupling on `shared` is still convicted
        // despite sitting in the same member as them.
        Assert.Equal(["(shared, setShared): Basic | Spin"], scan.Findings.Select(f => f.Signature()));

        // All three declarations were seen, so the silence about `value` is resolution telling the
        // two blocks apart rather than the slot scan having skipped them.
        Assert.Equal(3, scan.Slots);
    }

    /// <summary>
    /// A lambda parameter spelled after the page's slot. C# binds the references inside that lambda
    /// to the parameter, so the two cards holding them are independent — and the shape compiles,
    /// which is the whole problem: CS0136 governs nested blocks and stops at the lambda boundary.
    /// Measured, not reasoned about. A simple lambda parameter, a parenthesized one, and a
    /// local-function parameter all build clean beside a slot of the same name.
    ///
    /// <para>Before <see cref="DeclaredNames"/> existed the scan read all four references as the
    /// slot and reported <c>(value, setValue): Basic | Spin</c> — a blocked tree on correct code,
    /// in a gate with no allowlist to waive it with. No gallery page spells a parameter this way
    /// today, so this arm is driven from synthetic source rather than from a live page; a guard
    /// nobody has ever watched reject anything is not a guard.</para>
    ///
    /// <para><c>shared</c> is the conviction, so a scan that had stopped attributing anything at
    /// all fails here rather than passing as silence.</para>
    /// </summary>
    [Fact]
    public void ALambdaParameterShadowingASlot_IsNotAReferenceToIt()
    {
        var scan = ScanSource("""
            namespace Gallery;

            class __Page : Component
            {
                public override Element Render()
                {
                    var (value, setValue) = UseState(0.0);
                    var (shared, setShared) = UseState(1.0);

                    return VStack(
                        SampleCard("Basic", NumberBox(0.0, value => Log(value)), sourceCode: @"x"),
                        SampleCard("Spin", NumberBox(0.0, (double value) => Log(value)), sourceCode: @"x"),
                        SampleCard("Third", NumberBox(shared, v => setShared(v)), sourceCode: @"x"),
                        SampleCard("Fourth", NumberBox(shared, v => setShared(v)), sourceCode: @"x"));
                }
            }
            """);

        Assert.Equal(
            ["(shared, setShared): Third | Fourth"],
            scan.Findings.Select(f => f.Signature()));

        // The `value` slot was found — so its absence from the findings is the parameter binding
        // those references, not the slot detector having missed the declaration.
        Assert.Equal(2, scan.Slots);
        Assert.Equal(4, scan.Cards);
    }

    /// <summary>
    /// The same rule one construct over: an ordinary local declared inside a nested lambda body,
    /// which is legal for the same reason a parameter is and is <em>not</em> a slot, so the
    /// smallest-span tie-break between slots cannot reach it. Only a scope that binds the name
    /// without being a state hook can.
    /// </summary>
    [Fact]
    public void ANonSlotLocalInANestedLambda_IsNotAReferenceToTheEnclosingSlot()
    {
        var scan = ScanSource("""
            namespace Gallery;

            class __Page : Component
            {
                public override Element Render()
                {
                    var (value, setValue) = UseState(0.0);
                    var (shared, setShared) = UseState(1.0);

                    Func<Element> first = () =>
                    {
                        var value = 2.0;
                        return SampleCard("Basic", NumberBox(value, _ => { }), sourceCode: @"x");
                    };

                    Func<Element> second = () =>
                    {
                        var value = 3.0;
                        return SampleCard("Spin", NumberBox(value, _ => { }), sourceCode: @"x");
                    };

                    return VStack(
                        first(),
                        second(),
                        SampleCard("Third", NumberBox(shared, v => setShared(v)), sourceCode: @"x"),
                        SampleCard("Fourth", NumberBox(shared, v => setShared(v)), sourceCode: @"x"));
                }
            }
            """);

        Assert.Equal(
            ["(shared, setShared): Third | Fourth"],
            scan.Findings.Select(f => f.Signature()));

        Assert.Equal(2, scan.Slots);
    }

    /// <summary>
    /// A reference standing outside <em>every</em> candidate region reads something else — here a
    /// field the slots shadow — and is skipped rather than attributed. Both arities are on one page
    /// because they are one code path: <c>value</c> is bound twice and <c>lone</c> once, and
    /// "inside none of them" means the same thing either way. An earlier revision reported the
    /// two-declaration case as unadjudicable, which was a false positive on compiling code in a
    /// gate with no allowlist to waive it.
    ///
    /// <para>The shape compiles: a local may shadow a field, so each block's <c>value</c> is legal
    /// and the references past those blocks bind to the field. That is what makes it reachable
    /// rather than merely parseable.</para>
    ///
    /// <para><c>shared</c> is the conviction. Without it every assertion here would be satisfied by
    /// a detector that had stopped reporting anything at all, which is the one explanation for
    /// silence that a silence-shaped test cannot exclude.</para>
    /// </summary>
    [Fact]
    public void ReferencesOutsideEveryCandidateRegion_ReadSomethingElseAndAreNotReported()
    {
        var scan = ScanSource("""
            namespace Gallery;

            class __Page : Component
            {
                double value;
                Action<double> setValue = _ => { };
                double lone;
                Action<double> setLone = _ => { };

                public override Element Render()
                {
                    { var (value, setValue) = UseState(1.0); setValue(value); }
                    { var (value, setValue) = UseState(2.0); setValue(value); }
                    { var (lone, setLone) = UseState(3.0); setLone(lone); }

                    var (shared, setShared) = UseState(4.0);

                    return VStack(
                        SampleCard("TwiceA", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                        SampleCard("TwiceB", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                        SampleCard("OnceA", NumberBox(lone, v => setLone(v)), sourceCode: @"x"),
                        SampleCard("OnceB", NumberBox(lone, v => setLone(v)), sourceCode: @"x"),
                        SampleCard("Third", NumberBox(shared, v => setShared(v)), sourceCode: @"x"),
                        SampleCard("Fourth", NumberBox(shared, v => setShared(v)), sourceCode: @"x"));
                }
            }
            """);

        Assert.Equal(
            ["(shared, setShared): Third | Fourth"],
            scan.Findings.Select(f => f.Signature()));

        // All four slots were seen, so the silence about `value` and `lone` is resolution declining
        // to attribute their references rather than the scan never having found the declarations.
        Assert.Equal(4, scan.Slots);
        Assert.Equal(6, scan.Cards);
    }

    /// <summary>
    /// A slot redeclared inside a nested lambda. Both bindings key to the same member, so both are
    /// candidates, and the inner reference sits inside <em>both</em> regions — the only shape where
    /// more than one candidate contains a point, and therefore the only thing the smallest-span
    /// tie-break in <c>Resolve</c> decides.
    ///
    /// <para>That nesting is legal was measured rather than reasoned about, and the obvious
    /// reasoning is wrong: the same redeclaration in a plain nested block is CS0136, but across a
    /// lambda or non-static local-function boundary it compiles. An earlier comment on that
    /// tie-break called it unreachable defence for a construct the language does not have.</para>
    ///
    /// <para>The <em>inner</em> slot carries the conviction, and that is the whole design of this
    /// test. Asserting only that the outer slot stays out of the inner card is vacuous, because
    /// resolving outward hands the reference to a region the inner declaration shadows, so
    /// <see cref="DeclaredNames"/> drops it and the outer slot ends up just as unreported as if the
    /// tie-break had worked. Both mechanisms keep the outer slot clean; only one of them also lets
    /// the inner slot be <em>seen</em>. Sharing it across two cards is what separates them — the
    /// mutant reports nothing where the tie-break reports a coupling.</para>
    /// </summary>
    [Fact]
    public void ASlotRedeclaredInANestedLambda_ResolvesToTheInnermostDeclaration()
    {
        var scan = ScanSource("""
            namespace Gallery;

            class __Page : Component
            {
                public override Element Render()
                {
                    var (value, setValue) = UseState(1.0);

                    Func<Element> inner = () =>
                    {
                        var (value, setValue) = UseState(2.0);
                        return VStack(
                            SampleCard("Inner A", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                            SampleCard("Inner B", NumberBox(value, v => setValue(v)), sourceCode: @"x"));
                    };

                    return VStack(
                        SampleCard("Outer", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                        inner());
                }
            }
            """);

        // The inner slot is genuinely shared and must be convicted on its own two cards. Resolving
        // the inner references outward instead loses them to the shadowing check and reports
        // nothing; resolving them outward *and* keeping them would name "Outer" alongside. Both
        // failures are visible here, and neither is visible without the inner coupling.
        Assert.Equal(
            ["(value, setValue): Inner A | Inner B"],
            scan.Findings.Select(f => f.Signature()));

        Assert.Equal(2, scan.Slots);
        Assert.Equal(3, scan.Cards);
    }

    /// <summary>
    /// A slot declared directly under a <c>case</c> label, with no block of its own. Its scope is
    /// the switch section; if <see cref="DeclaringRegion"/> walked past that to the enclosing method
    /// block, the region would be wider than the scope and the two cards below — which read the
    /// <em>field</em>, since the local is out of scope there — would be attributed to it and
    /// reported as sharing a slot. That is a false positive on compiling code, and this gate has no
    /// allowlist to waive it with.
    /// </summary>
    [Fact]
    public void ASlotDeclaredInAnUnbracedSwitchSection_DoesNotClaimReferencesBeyondIt()
    {
        var scan = ScanSource("""
            namespace Gallery;

            class __Page : Component
            {
                double value;
                Action<double> setValue = _ => { };

                public override Element Render()
                {
                    switch (Mode)
                    {
                        case 1:
                            var (value, setValue) = UseState(1.0);
                            setValue(value);
                            break;
                    }

                    var (shared, setShared) = UseState(2.0);

                    return VStack(
                        SampleCard("Basic", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                        SampleCard("Spin", NumberBox(value, v => setValue(v)), sourceCode: @"x"),
                        SampleCard("Third", NumberBox(shared, v => setShared(v)), sourceCode: @"x"),
                        SampleCard("Fourth", NumberBox(shared, v => setShared(v)), sourceCode: @"x"));
                }
            }
            """);

        Assert.Equal(
            ["(shared, setShared): Third | Fourth"],
            scan.Findings.Select(f => f.Signature()));

        Assert.Equal(2, scan.Slots);
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
    /// The complement of the case above. A <c>static</c> local function cannot capture, so the
    /// enclosing method's locals are not in scope inside it — and C# therefore permits it to declare
    /// a local of the same name. Verified by compiling the shape, not by reading the rule:
    /// <c>static void Local() { int value = 2; }</c> nested in a method holding its own
    /// <c>value</c> builds clean, 0 warnings. It joins a discard, a lambda parameter in a sibling
    /// member, and a lambda or local-function body nested in the declaring member on the list of
    /// places a slot's name legally reappears.
    ///
    /// <para>The obvious form of this test — a page with no coupling, asserting the scan stays
    /// silent — is <em>vacuous</em>, and was written that way first. So this is the
    /// <see cref="ASameNamedSlotInASiblingMember_DoesNotHideARealSharedSlot"/> shape instead: give
    /// <c>Render</c> a <em>real</em> coupling, then require it to be convicted and named <em>and no
    /// wider</em> despite the static local functions beside it.</para>
    ///
    /// <para><c>Aside</c> holds its own slot, and <c>Bare</c> an ordinary local that is not a slot
    /// at all. Both are resolved the same way — the declaration nearest the reference wins — and
    /// neither needs a local-function carve-out to get there. An earlier version of this test
    /// claimed <c>Bare</c> pinned exactly such a carve-out, on the reasoning that with no candidate
    /// declaration to resolve against, only a scope boundary could stop the reference reaching
    /// <c>Render</c>'s slot. <see cref="DeclaredNames"/> is that second way, so the carve-out became
    /// unreachable and the claim false. The mutation ladder is what said so; nothing in the file
    /// re-checks a paragraph.</para>
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
                        Aside(),
                        Bare());

                    static Element Aside()
                    {
                        var (value, setValue) = UseState(0.0);
                        return SampleCard("Aside", NumberBox(value, v => setValue(v)), sourceCode: @"x");
                    }

                    static Element Bare()
                    {
                        double value = 2;
                        return SampleCard("Bare", NumberBox(value, v => { }), sourceCode: @"x");
                    }
                }
            }
            """);

        // Render's coupling is convicted and named, and neither "Aside" nor "Bare" is in it — a
        // static local function's `value` is a different symbol whether or not it is a slot.
        Assert.Equal(["(value, setValue): Basic | Spin"], scan.Findings.Select(f => f.Signature()));

        // Both slots and all four cards were seen, so the finding above is the scan telling the
        // `value`s apart rather than having missed one declaration outright. `Bare`'s local is
        // deliberately not counted: it is an ordinary local, not a state slot.
        Assert.Equal(2, scan.Slots);
        Assert.Equal(4, scan.Cards);
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
    /// across two slots <em>in the same block</em>, where the use-site resolution below has no
    /// disjoint regions to tell them apart and would attribute every <c>_</c> to whichever slot
    /// came first, taking the real setter names down with it. Still a shared slot, still reported,
    /// named for the half that actually binds.
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
