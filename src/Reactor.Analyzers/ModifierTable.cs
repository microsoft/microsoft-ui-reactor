using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// How a <c>.Set(x =&gt; x.PROP = v)</c> write maps onto a Reactor fluent modifier, and
/// under what conditions suggesting that modifier is actually sound.
/// </summary>
internal sealed class ModifierInfo
{
    internal ModifierInfo(
        string modifier,
        bool poolReset = false,
        string[]? controlGate = null,
        string[]? elementTypes = null,
        string[]? poolResetGate = null)
    {
        Modifier = modifier;
        PoolReset = poolReset;
        ControlGate = controlGate;
        ElementTypes = elementTypes;
        PoolResetGate = poolResetGate;
    }

    /// <summary>Name of the fluent modifier method to suggest.</summary>
    public string Modifier { get; }

    /// <summary>
    /// True when <c>ElementPool.CleanElement</c> resets this property, so an imperative
    /// <c>.Set</c> write is silently lost on pool reuse. Selects the higher-severity
    /// <c>REACTOR_POOL_001</c>; everything else reports <c>REACTOR_MOD_002</c>.
    /// </summary>
    /// <remarks>
    /// Property-level. Where the reset only applies to some of the gated receivers,
    /// <see cref="PoolResetGate"/> narrows it — see that member for why the distinction is
    /// load-bearing rather than cosmetic.
    /// </remarks>
    public bool PoolReset { get; }

    /// <summary>
    /// WinUI control types that <c>ApplyModifiers</c> actually writes this modifier to, or
    /// <c>null</c> when it is applied unconditionally to the <c>FrameworkElement</c>.
    /// <para>
    /// Only needed where WinUI declares the dependency property on <em>more</em> types than
    /// the reconciler handles. On anything outside this list the modifier compiles and
    /// silently does nothing, so the suggestion must be withheld.
    /// </para>
    /// </summary>
    public string[]? ControlGate { get; }

    /// <summary>
    /// Reactor element types that declare a type-specific overload of this modifier, or
    /// <c>null</c> when the modifier is a generic <c>T Foo&lt;T&gt;(this T el, …)</c>.
    /// <para>
    /// A name-keyed rewrite would emit a call that does not compile on any other receiver,
    /// so the element type is checked before the fix is offered.
    /// </para>
    /// <para>
    /// When <see cref="ControlGate"/> is also set the two are <b>OR'd</b>, not AND'd: they
    /// describe two independent routes to a sound rewrite — the generic modifier reaching this
    /// receiver at runtime, or a type-specific overload existing for this element type. Fonts
    /// need both, because <c>ApplyModifiers</c> only writes the generic path to
    /// <c>Control</c>/<c>TextBlock</c> while <c>RichTextBlockElement</c> carries its own
    /// overloads.
    /// </para>
    /// </summary>
    public string[]? ElementTypes { get; }

    /// <summary>
    /// The subset of <see cref="ControlGate"/> the pool actually resets, or <c>null</c> when
    /// every gated receiver is reset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="PoolReset"/> is per-property, but poolability is per-receiver, and the two
    /// do not coincide: <c>ApplyModifiers</c> writes <c>Padding</c> to <c>RelativePanel</c>,
    /// yet <c>RelativePanel</c> is absent from <c>ElementPool.PoolableTypes</c>, so nothing
    /// is ever recycled and no write is ever unwound. Reporting <c>REACTOR_POOL_001</c> there
    /// states something false — and at Warning severity, which becomes a build break for
    /// consumers using <c>TreatWarningsAsErrors</c>. Narrowing to this list reports
    /// <c>REACTOR_MOD_002</c> instead, which keeps the modifier suggestion and drops only the
    /// pool-return claim: the write lands and is <i>never</i> unwound, so what it costs is the
    /// element's structural skip (<c>Element.SettersEqual</c>), not the value.
    /// </para>
    /// <para>
    /// This is a name-level mirror of <c>ControlGate ∩ ElementPool.PoolableTypes</c>, kept
    /// here because the analyzer targets <c>netstandard2.0</c> and cannot reference
    /// <c>src/Reactor</c>. It is not maintained by hand:
    /// <c>ModifierUnsetClearValueTests.Every_Poolable_Gated_Receiver_Is_Released_By_CleanElement</c>
    /// derives the same intersection from both real sources, and
    /// <c>Every_Pool_Reset_Gate_Matches_The_Poolable_Intersection</c> fails if this list
    /// drifts from it in either direction.
    /// </para>
    /// </remarks>
    public string[]? PoolResetGate { get; }
}

/// <summary>
/// How a <c>.Set(x =&gt; Owner.SetPROP(x, v))</c> <em>attached</em>-property write maps onto
/// a Reactor fluent modifier.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="ModifierInfo"/> because an attached write is a different
/// syntactic shape — an <c>InvocationExpressionSyntax</c>, not an assignment — and needs
/// three things an instance property does not: the declaring owner (so
/// <c>AutomationProperties.Name</c> can be told apart from <c>FrameworkElement.Name</c>),
/// the owner's namespace (so a same-named user type stays silent), and the setter method
/// name, which does not always follow the property name —
/// <c>FlexPanel.SetMinWidth</c> writes <c>FlexMinWidthProperty</c>.
/// </para>
/// <para>
/// Every entry is pool-reset <em>as a property</em> by construction:
/// <see cref="ModifierTable.AttachedProperties"/> only lists properties
/// <c>ElementPool.CleanElement</c> clears, so there is no per-entry <c>poolReset</c> flag
/// and no attached analogue of <see cref="ModifierInfo.PoolResetGate"/>.
/// </para>
/// <para>
/// That is not the same as "every attached write reports <c>REACTOR_POOL_001</c>".
/// <c>CleanElement</c> only runs on a control the pool recycles, so
/// <c>PoolResetSetAnalyzer</c> also requires the <c>.Set</c> lambda parameter's exact type
/// to be in <see cref="ModifierTable.PoolableTypeNames"/>; on anything else the same write reports
/// <c>REACTOR_MOD_002</c>: the modifier still exists, but the pool-return claim does not — the
/// write lands and is never unwound, so what it costs is the element's structural skip
/// (<c>Element.SettersEqual</c>), not the value. The instance and attached
/// halves resolve that one fact the same way and once per <c>.Set</c> body, so a body
/// mixing both shapes on one receiver cannot report two different ids. See
/// <c>Attached_Setter_Reports_ModifierAvailable_On_An_Unpooled_Receiver</c>.
/// </para>
/// </remarks>
internal sealed class AttachedModifierInfo
{
    internal AttachedModifierInfo(
        string owner,
        string ownerNamespace,
        string property,
        string modifier,
        bool autoFix = true,
        string? setter = null,
        string? fixValueType = null,
        string? modifierUsage = null,
        string[]? receiverConflicts = null)
    {
        Owner = owner;
        OwnerNamespace = ownerNamespace;
        Property = property;
        Modifier = modifier;
        AutoFix = autoFix;
        Setter = setter ?? "Set" + property;
        FixValueType = fixValueType;
        ModifierUsage = modifierUsage ?? "." + modifier + "(...)";
        ReceiverConflicts = receiverConflicts;
    }

    /// <summary>Simple name of the type declaring the attached property.</summary>
    public string Owner { get; }

    /// <summary>
    /// Namespace of <see cref="Owner"/>, checked against the resolved method symbol so an
    /// unrelated user-defined type that merely shares the name cannot trigger the rule.
    /// </summary>
    public string OwnerNamespace { get; }

    /// <summary>
    /// Base name of the dependency property, i.e. <c>Name</c> for <c>NameProperty</c> — the
    /// form <c>ElementPool.CleanElement</c> clears and <c>PoolResetSetConsistencyTests</c>
    /// scans for. Combined with <see cref="Owner"/> this is the table key.
    /// </summary>
    public string Property { get; }

    /// <summary>
    /// Static setter method name. Defaults to <c>"Set" + Property</c>, overridden where WinUI
    /// or Reactor names them differently (<c>FlexPanel.SetMinWidth</c> /
    /// <c>FlexMinWidthProperty</c>).
    /// </summary>
    public string Setter { get; }

    /// <summary>Name of the fluent modifier method to suggest.</summary>
    public string Modifier { get; }

    /// <summary>
    /// True when the setter's single value argument can be handed to the modifier verbatim.
    /// False when the modifier's signature does not line up 1:1 — a different arity
    /// (<c>SetPositionInSet(fe, 2)</c> vs <c>.PositionInSet(position, size)</c>), a different
    /// parameter type (<c>SetPlacementTarget</c> takes a <c>UIElement</c>,
    /// <c>.ToolTipPlacementTarget</c> an <c>ElementRef</c>), or an N:1 mapping
    /// (every <c>FlexPanel.*</c> property funnels into one <c>.Flex(...)</c> call that
    /// replaces the whole <c>FlexAttached</c> record, so chaining per statement would clobber
    /// the earlier ones). Those entries stay diagnostic-only.
    /// </summary>
    public bool AutoFix { get; }

    /// <summary>
    /// Fully-qualified type the value argument must convert to before the fix is offered, or
    /// <c>null</c> when no extra check is needed. Exists for setters typed more loosely than
    /// their modifier: <c>ToolTipService.SetToolTip</c> takes <c>object</c> while
    /// <c>.ToolTip(...)</c> takes <c>string</c>, so rewriting a non-string tooltip would not
    /// compile (use <c>.WithToolTip(Element)</c> there instead).
    /// </summary>
    public string? FixValueType { get; }

    /// <summary>
    /// How the modifier should be written at a call site, e.g. <c>.AutomationName(...)</c> or
    /// <c>.Flex(grow: …)</c>. Defaults to <c>.Modifier(...)</c>.
    /// </summary>
    /// <remarks>
    /// Carried per entry because the message is the only guidance an author gets for the
    /// entries with no code fix, and <c>.Modifier(...)</c> is actively misleading for several
    /// of them: <c>.Required()</c> takes no argument, <c>.PositionInSet</c> takes two, and all
    /// eleven flex properties share one <c>.Flex(...)</c> where the parameter name is the
    /// whole answer.
    /// </remarks>
    public string ModifierUsage { get; }

    /// <summary>
    /// Other modifier names on the receiver chain that already write this same property, so
    /// appending our modifier after them would change the rendered value rather than refactor.
    /// Empty when the modifier is the only thing that writes it.
    /// </summary>
    /// <remarks>
    /// <see cref="PoolResetSetCodeFix"/> guards against a receiver that already calls the
    /// modifier we are about to append, but a name comparison alone misses the aliases:
    /// <c>.ToolTip(tip, placement)</c> writes <c>ToolTipService.Placement</c> too, and
    /// <c>.AccessibilityHidden()</c> is shorthand for <c>.AccessibilityView(Raw)</c>.
    /// </remarks>
    public string[]? ReceiverConflicts { get; }

    /// <summary>Table key / diagnostic message subject, e.g. <c>AutomationProperties.Name</c>.</summary>
    public string Key => Owner + "." + Property;
}

/// <summary>
/// The single source of truth for "this property has a fluent modifier, prefer it over
/// <c>.Set</c>" — consumed by <see cref="PoolResetSetAnalyzer"/> and
/// <see cref="PoolResetSetCodeFix"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately one table rather than one per diagnostic. Two parallel lists is how the
/// original pool-reset list went stale: a modifier was added, nobody thought to update the
/// analyzer, and the rule silently stopped covering the properties people were actually
/// writing through <c>.Set</c>. Entries carry their own metadata so a new property is one
/// row here rather than an edit in several places.
/// </para>
/// <para>
/// <c>ModifierTableIntegrityTests</c> reflects over this table and over
/// <c>Microsoft.UI.Reactor.ElementExtensions</c> to keep it honest — every entry must name
/// a modifier that exists, every element type must really declare it, and any new generic
/// modifier matching a settable WinUI dependency property must be either listed here or
/// explicitly excluded with a reason.
/// </para>
/// <para><b>Two reasons a property belongs here.</b> Both make <c>.Set</c> the wrong tool,
/// but they differ in severity:</para>
/// <list type="number">
/// <item><description><see cref="ModifierInfo.PoolReset"/> — <c>ElementPool.CleanElement</c>
/// clears the property on return, so the imperative write is <em>lost</em> on pool reuse.
/// A real bug with a visible symptom → <c>REACTOR_POOL_001</c>, Warning.</description></item>
/// <item><description>Everything else — the write works, but <c>Element.SettersEqual</c> is
/// <c>ReferenceEquals(a,b) || both-empty</c>, so any element carrying setters is forced onto
/// the reconciler's update path every render, and the value is never unwound when a later
/// render drops it → <c>REACTOR_MOD_002</c>, Info.</description></item>
/// </list>
/// <para>
/// <b>Attached properties live in their own table.</b> <see cref="Properties"/> is keyed by
/// bare property name, which attached properties collide with — see
/// <see cref="AttachedProperties"/> for why, and for the second syntactic shape
/// (<c>Owner.SetPROP(x, v)</c>) the same <c>REACTOR_POOL_001</c> id covers.
/// </para>
/// </remarks>
internal static class ModifierTable
{
    // Type groups, named once so the intent is legible at each use site. Each name is its receiver
    // list concatenated, which is deliberate — and it makes set inclusion equivalent to string
    // inclusion. A gate that is a superset of another therefore *necessarily* has a name that
    // contains the narrower one: ControlBorder is a prefix of ControlBorderGridStack, which is a
    // prefix of ControlBorderGridStackRelative, which is a prefix of the ...Text form (8 such strict
    // prefix pairs today), and PanelControlBorder contains ControlBorder without starting with it.
    //
    // So: compare what a gate CONTAINS, not what it is CALLED. Read ModifierInfo.ControlGate /
    // ModifierInfo.PoolResetGate and compare the type sets — that is what every check in
    // ModifierTableIntegrityTests does, and it is why none of them can be fooled here. It is also
    // what ModifierGateProseParityTests does for the REACTOR_MOD_003 gate list in the shipped
    // reactor-build-and-check skill: prose names receiver TYPES, so it compares sets too.
    //
    // Only when the artifact genuinely leaves no typed property reachable should you match this
    // file as text, and then anchor on the delimiters the C# syntax guarantees: `SLOT:\s*NAME\s*[,)]`.
    // A bare Contains(NAME) selects every wider gate as well, silently passing the very assertion
    // meant to catch a mis-widened gate. tests/Reactor.Tests/AnalyzerTests/ModifierGateSource.cs is
    // the test-only reference implementation of that matcher — it is internal to Reactor.Tests, so
    // CLI and analyzer code must copy the pattern and add a parity test rather than call it —
    // and ModifierGateIdentifierTests pins it to this table. Issue #1062.
    private static readonly string[] ControlBorderGridStackRelativeText = { "Control", "Border", "Grid", "StackPanel", "RelativePanel", "TextBlock" };
    private static readonly string[] ControlBorderGridStackRelative = { "Control", "Border", "Grid", "StackPanel", "RelativePanel" };
    private static readonly string[] ControlBorder = { "Control", "Border" };
    private static readonly string[] PanelControlBorder = { "Panel", "Control", "Border" };
    private static readonly string[] ControlOrTextBlock = { "Control", "TextBlock" };
    private static readonly string[] RichTextBlockOnly = { "RichTextBlockElement" };
    private static readonly string[] TextOrRichTextBlock = { "TextBlockElement", "RichTextBlockElement" };

    // The two groups above minus RelativePanel, which ApplyModifiers writes to but
    // ElementPool never recycles. Used as poolResetGate, never as controlGate.
    private static readonly string[] ControlBorderGridStackText = { "Control", "Border", "Grid", "StackPanel", "TextBlock" };
    private static readonly string[] ControlBorderGridStack = { "Control", "Border", "Grid", "StackPanel" };

    // Single-receiver poolResetGate. ControlOnly names the receiver whose arm in
    // ElementPool.CleanElement actually clears the property, for the two rows cleared under
    // `if (fe is Control …)` rather than on the FrameworkElement itself.
    //
    // This is never a controlGate — a control gate says where ApplyModifiers writes the modifier,
    // which is a different and usually wider question. Deriving one from the other is the
    // conflation that made REACTOR_POOL_001 claim IsTabStop was pool-reset on a TextBlock.
    private static readonly string[] ControlOnly = { "Control" };

    // Element-type lists for the type-specific modifiers whose property CleanElement resets in a
    // `case` arm of its type dispatch. Named here rather than inline because each is shared by
    // two rows or reads better beside its sibling.
    private static readonly string[] ViewboxElementOnly = { "ViewboxElement" };
    private static readonly string[] ProgressRingElementOnly = { "ProgressRingElement" };

    /// <summary>
    /// The exact WinUI control type names <c>ElementPool</c> recycles — a mirror of
    /// <c>ElementPool.PoolableTypes</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pool tests membership with <c>PoolableTypes.Contains(element.GetType())</c>: an
    /// exact-type lookup, not an assignability walk. This mirror is matched the same way, and
    /// that distinction is the whole reason it exists. A control gate names the base type an
    /// <c>ApplyModifiers</c> arm dispatches on, so <c>Control</c> admits every WinUI control and
    /// <c>Panel</c> admits every panel, while the pool recycles these fifteen types and nothing
    /// else. Selecting <c>REACTOR_POOL_001</c> from a gate alone therefore asserts "reset on pool
    /// return" for receivers such as <c>CheckBox</c> and <c>RelativePanel</c> that are never
    /// pooled — at Warning severity, which is a build break for a consumer using
    /// <c>TreatWarningsAsErrors</c>. Those receivers fall to <c>REACTOR_MOD_002</c>, which drops
    /// only the pool-return claim: the write lands and is <i>never</i> unwound, and the cost is
    /// the element's structural skip (<c>Element.SettersEqual</c>) rather than the value.
    /// </para>
    /// <para>
    /// Matching the receiver's own type rather than its base chain costs no signal here, because
    /// every <c>.Set</c> overload in the DSL is declared per element type over the concrete
    /// control (<c>ButtonElement</c> takes <c>Action&lt;Button&gt;</c>), so the receiver the
    /// analyzer sees is the type the pool would key on.
    /// </para>
    /// <para>
    /// Not hand-maintained: <c>ModifierUnsetClearValueTests</c> reads
    /// <c>ElementPool.PoolableTypes</c> by reflection and fails if the two sets drift.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> PoolableTypeNameSet =
        new HashSet<string>(System.StringComparer.Ordinal)
        {
            "TextBlock", "RichTextBlock", "StackPanel", "Grid", "Border", "ScrollViewer",
            "Canvas", "Viewbox", "ProgressBar", "ProgressRing", "Image", "InfoBadge",
            "Button", "TextBox", "ToggleSwitch",
        };

    /// <summary>The mirrored poolable type names, for the parity test that keeps them honest.</summary>
    public static IEnumerable<string> PoolableTypeNames => PoolableTypeNameSet;

    /// <summary>True when <c>ElementPool</c> recycles exactly this type.</summary>
    public static bool IsPoolableTypeName(string name) => PoolableTypeNameSet.Contains(name);

    /// <summary>
    /// Property name → modifier mapping. Keyed by the WinUI property name as written inside
    /// the <c>.Set</c> lambda.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ModifierInfo> Properties =
        new Dictionary<string, ModifierInfo>(System.StringComparer.Ordinal)
        {
            // ── Pool-reset (REACTOR_POOL_001, Warning) ───────────────────────────────
            // Reset in ElementPool.CleanElement; all applied unconditionally to `fe`.
            { "Margin",              new ModifierInfo("Margin",              poolReset: true) },
            { "Width",               new ModifierInfo("Width",               poolReset: true) },
            { "Height",              new ModifierInfo("Height",              poolReset: true) },
            { "MinWidth",            new ModifierInfo("MinWidth",            poolReset: true) },
            { "MinHeight",           new ModifierInfo("MinHeight",           poolReset: true) },
            { "MaxWidth",            new ModifierInfo("MaxWidth",            poolReset: true) },
            { "MaxHeight",           new ModifierInfo("MaxHeight",           poolReset: true) },
            { "HorizontalAlignment", new ModifierInfo("HorizontalAlignment", poolReset: true) },
            { "VerticalAlignment",   new ModifierInfo("VerticalAlignment",   poolReset: true) },
            { "Opacity",             new ModifierInfo("Opacity",             poolReset: true) },
            { "AccessKey",           new ModifierInfo("AccessKey",           poolReset: true) },

            // IsTabStop deliberately has NO gate. It is declared on UIElement and ApplyModifiers
            // writes it ungated (`fe.IsTabStop = …`), so it reaches every element — including the
            // poolable non-Controls TextBlock, RichTextBlock, Grid, StackPanel, Border, Canvas,
            // Viewbox and Image. Gating the diagnostic to Control would have described a real
            // pool leak (a pooled TextBlock keeping a stale tab stop) as expected behaviour;
            // CleanElement clears it on `fe` instead, which makes the unrestricted claim true.
            //
            // IsEnabled is the genuine Control-only case: WinUI declares it on Control, so the
            // gate restricts nothing a user could hit and exists to keep the derivation total —
            // every poolReset row names the receivers CleanElement clears it on, and a row that
            // opts out of saying so is a row the parity check cannot verify.
            { "IsTabStop",           new ModifierInfo("IsTabStop",           poolReset: true) },
            { "IsEnabled",           new ModifierInfo("IsEnabled",           poolReset: true, poolResetGate: ControlOnly) },

            // Pool-reset but only on some receivers (issue #985). CleanElement clears these
            // through a Control | Border | Panel/Grid/StackPanel | TextBlock chain that mirrors
            // the receivers ApplyModifiers writes them to, so the control gates below still
            // apply: the gate decides whether the modifier reaches the control at all,
            // poolReset decides which rule id reports it.
            //
            // poolReset is per-property but poolability is per-receiver, so POOL_001 needs two
            // independent facts about the receiver and neither implies the other:
            //
            //   1. the pool recycles it at all      -> PoolableTypeNames, matched on the exact
            //                                          type, exactly as the pool matches it
            //   2. CleanElement clears THIS property -> poolResetGate, matched on the base chain,
            //      once it is recycled                 exactly as CleanElement's `is` arms match
            //
            // RelativePanel fails (1) for every property: it is gated for Padding, CornerRadius
            // and Background but never recycled. CheckBox fails (1) too — Control is in three of
            // these gates and admits every WinUI control, while the pool holds seven. Both
            // reported POOL_001 before issue #1051, asserting "reset on pool return" of a
            // receiver that is never pooled, at Warning severity — a build break for a consumer
            // using TreatWarningsAsErrors. They fall to MOD_002 instead, which keeps the modifier
            // suggestion and drops only the pool-return claim: the write lands and is never
            // unwound, costing the element's structural skip rather than the value.
            //
            // Nothing here is hand-maintained. ModifierUnsetClearValueTests reads
            // ElementPool.PoolableTypes for (1) and derives (2) from the CleanElementScan of
            // CleanElement itself, and fails if either drifts. Closes issue #1051; the derivation
            // for (2) moved off ControlGate in #1193, because "ApplyModifiers writes it here" and
            // "CleanElement clears it here" are different facts that only coincide for this
            // border-box family.
            //
            // WinUI declares most of these on Panel subclasses too, and the allow-lists
            // genuinely differ: Panel itself declares only Background; Grid, StackPanel, and
            // RelativePanel each declare their own border-box properties, and TextBlock takes
            // Padding but not CornerRadius. The three rows without a poolResetGate need none:
            // CleanElement clears Background for every Panel and both Border* for the whole
            // Control and Border arms, so once (1) holds there is no gated receiver left for (2)
            // to exclude — their ControlGate already names exactly the cleared receivers.
            { "Padding",         new ModifierInfo("Padding",         poolReset: true, controlGate: ControlBorderGridStackRelativeText, poolResetGate: ControlBorderGridStackText) },
            { "CornerRadius",    new ModifierInfo("CornerRadius",    poolReset: true, controlGate: ControlBorderGridStackRelative,     poolResetGate: ControlBorderGridStack) },
            { "BorderThickness", new ModifierInfo("BorderThickness", poolReset: true, controlGate: ControlBorder) },
            { "BorderBrush",     new ModifierInfo("BorderBrush",     poolReset: true, controlGate: ControlBorder) },
            { "Background",      new ModifierInfo("Background",      poolReset: true, controlGate: PanelControlBorder) },

            // ── Generic modifier, no runtime gate (REACTOR_MOD_002, Info) ────────────
            // The content-alignment pair IS Control-gated in ApplyModifiers, but WinUI
            // declares those DPs only on Control — if the .Set lambda compiles the receiver
            // already qualifies, so no predicate is needed.
            { "HorizontalContentAlignment", new ModifierInfo("HorizontalContentAlignment") },
            { "VerticalContentAlignment",   new ModifierInfo("VerticalContentAlignment") },

            // Reset by CleanElement (issue #162, alongside IsTabStop) and written ungated by
            // ApplyModifiers, yet deliberately NOT poolReset — see the note on DeliberatelyExcluded
            // below for the row this replaces. Two separate facts got conflated here historically:
            //
            //   1. "no .IsHitTestVisible modifier exists" — simply false. The generic
            //      .IsHitTestVisible(bool) has been in ElementExtensions.cs all along, with a
            //      signature that matches the property exactly, so MOD_002's suggestion is sound
            //      and the rewrite compiles. That is what this row restores (issue #1193).
            //
            //   2. "the pool loses the .Set write" — the claim POOL_001 would additionally make.
            //      It is not established. CleanElement runs on pool RETURN, and the next mount
            //      re-applies setters: DescriptorHandler.Mount rents the control and ends in
            //      ApplySetters, and Element.SettersEqual keeps any element carrying setters on
            //      the Update path, which also ends in ApplySetters. The place a .Set write really
            //      is discarded is a modifier set -> unset transition, because ApplyModifiers runs
            //      AFTER ApplySetters — pinned by the Issue950 selftest
            //      ModifierResetOutranksASetterWrite, whose own comment names MOD_002 as the
            //      diagnostic that steers callers off it.
            //
            // So this lands at MOD_002, matching the evidence, rather than inheriting IsTabStop's
            // poolReset out of symmetry. Whether the 18 existing poolReset rows should keep that
            // stronger claim is a live question raised on #1193 and deliberately not answered here:
            // flipping this one bool is the whole change if they do.
            { "IsHitTestVisible", new ModifierInfo("IsHitTestVisible") },

            // Fonts have BOTH a generic modifier and type-specific overloads, and the two
            // cover different receivers — so the gates are OR'd (see ModifierInfo.ElementTypes).
            // The generic path only reaches Control|TextBlock in ApplyModifiers; RichTextBlock
            // is neither, yet exposes the same DPs, so `.FontSize(n)` there would bind the
            // generic modifier and write nothing. The RichTextBlockElement overloads are what
            // make the suggestion sound on that receiver — FontSize's was added alongside this
            // table for exactly that reason.
            { "FontFamily", new ModifierInfo("FontFamily", controlGate: ControlOrTextBlock, elementTypes: TextOrRichTextBlock) },
            { "FontSize",   new ModifierInfo("FontSize",   controlGate: ControlOrTextBlock, elementTypes: TextOrRichTextBlock) },
            { "FontWeight", new ModifierInfo("FontWeight", controlGate: ControlOrTextBlock, elementTypes: RichTextBlockOnly) },
            { "Foreground", new ModifierInfo("Foreground", controlGate: ControlOrTextBlock, elementTypes: RichTextBlockOnly) },

            // ── Type-specific modifiers (REACTOR_MOD_002, Info) ──────────────────────
            // No generic overload exists, so the rewrite only compiles on these element
            // types. Lists are verified against ElementExtensions*.cs by
            // ModifierTableIntegrityTests.
            { "TextWrapping", new ModifierInfo("TextWrapping",
                elementTypes: new[] { "TextBlockElement", "TextBoxElement", "RichEditBoxElement" }) },
            { "TextTrimming", new ModifierInfo("TextTrimming",
                elementTypes: new[] { "TextBlockElement", "RichTextBlockElement" }) },
            { "MaxLines", new ModifierInfo("MaxLines",
                elementTypes: new[] { "TextBlockElement", "RichTextBlockElement" }) },
            { "LineHeight", new ModifierInfo("LineHeight",
                elementTypes: new[] { "TextBlockElement", "RichTextBlockElement" }) },
            { "CharacterSpacing", new ModifierInfo("CharacterSpacing",
                elementTypes: new[] { "TextBlockElement", "RichTextBlockElement" }) },
            { "FontStyle", new ModifierInfo("FontStyle",
                elementTypes: new[] { "TextBlockElement", "RichTextBlockElement" }) },
            { "TextAlignment", new ModifierInfo("TextAlignment",
                elementTypes: new[] { "TextBlockElement", "RichTextBlockElement", "TextBoxElement" }) },
            { "IsTextSelectionEnabled", new ModifierInfo("IsTextSelectionEnabled",
                elementTypes: new[] { "TextBlockElement" }) },
            { "AcceptsReturn", new ModifierInfo("AcceptsReturn",
                elementTypes: new[] { "TextBoxElement", "RichEditBoxElement" }) },
            { "IsSpellCheckEnabled", new ModifierInfo("IsSpellCheckEnabled",
                elementTypes: new[] { "TextBoxElement", "RichEditBoxElement" }) },
            { "MaxLength", new ModifierInfo("MaxLength",
                elementTypes: new[] { "TextBoxElement", "RichEditBoxElement", "PasswordBoxElement" }) },
            { "IsReadOnly", new ModifierInfo("IsReadOnly",
                elementTypes: new[] { "TextBoxElement", "RatingControlElement" }) },
            { "CharacterCasing", new ModifierInfo("CharacterCasing",
                elementTypes: new[] { "TextBoxElement" }) },
            { "PasswordRevealMode", new ModifierInfo("PasswordRevealMode",
                elementTypes: new[] { "PasswordBoxElement" }) },
            { "PlaceholderText", new ModifierInfo("PlaceholderText",
                elementTypes: new[]
                {
                    "TextBoxElement", "PasswordBoxElement", "NumberBoxElement", "ComboBoxElement",
                    "AutoSuggestBoxElement", "CalendarDatePickerElement", "RichEditBoxElement",
                }) },

            // Viewbox and ProgressRing. None of the three had a row before issue #1193, and the
            // reason each was missing is worth keeping: Stretch sat in DeliberatelyExcluded as a
            // "Viewbox-only modifier", which describes what elementTypes is FOR rather than a
            // reason to skip the property; StretchDirection and IsActive were in neither table
            // because the staleness tests' WinUI probe does not reach Viewbox or ProgressRing, so
            // nothing ever forced the choice. Each has an exact-signature type-specific modifier,
            // so MOD_002's suggestion is sound and its rewrite compiles.
            { "Stretch", new ModifierInfo("Stretch", elementTypes: ViewboxElementOnly) },
            { "StretchDirection", new ModifierInfo("StretchDirection", elementTypes: ViewboxElementOnly) },
            { "IsActive", new ModifierInfo("IsActive", elementTypes: ProgressRingElementOnly) },
            { "SelectionMode", new ModifierInfo("SelectionMode",
                elementTypes: new[] { "ListViewElement", "GridViewElement" }) },

            // Rich-text typography. Surfaced by the type-specific staleness test — each has a
            // modifier whose parameter type is the property's own type, so the rewrite is a
            // straight pass-through.
            { "FontStretch", new ModifierInfo("FontStretch",
                elementTypes: new[] { "RichTextBlockElement", "RichTextParagraph", "RichTextRun", "RichTextHyperlink" }) },
            { "TextDecorations", new ModifierInfo("TextDecorations",
                elementTypes: new[] { "TextBlockElement", "RichTextBlockElement", "RichTextParagraph", "RichTextRun", "RichTextHyperlink" }) },
            { "Language", new ModifierInfo("Language",
                elementTypes: new[] { "RichTextParagraph", "RichTextRun", "RichTextHyperlink" }) },
            { "HorizontalTextAlignment", new ModifierInfo("HorizontalTextAlignment",
                elementTypes: new[] { "RichTextBlockElement", "RichTextParagraph" }) },
            { "LineStackingStrategy", new ModifierInfo("LineStackingStrategy",
                elementTypes: new[] { "RichTextBlockElement", "RichTextParagraph" }) },
            { "SelectionHighlightColor", new ModifierInfo("SelectionHighlightColor",
                elementTypes: new[] { "RichTextBlockElement", "RichEditBoxElement" }) },
            { "IsColorFontEnabled", new ModifierInfo("IsColorFontEnabled",
                elementTypes: new[] { "RichTextBlockElement" }) },
            { "OpticalMarginAlignment", new ModifierInfo("OpticalMarginAlignment",
                elementTypes: new[] { "RichTextBlockElement" }) },
            { "TextLineBounds", new ModifierInfo("TextLineBounds",
                elementTypes: new[] { "RichTextBlockElement" }) },
            { "TextReadingOrder", new ModifierInfo("TextReadingOrder",
                elementTypes: new[] { "RichTextBlockElement" }) },
            { "ContentTransitions", new ModifierInfo("ContentTransitions",
                elementTypes: new[] { "ExpanderElement" }) },
        };

    /// <summary>
    /// Modifiers that <c>Reconciler.ApplyModifiers</c> gates on a control type but that carry
    /// <see cref="ModifierInfo.ControlGate"/> <see langword="null"/> here (or no
    /// <see cref="Properties"/> entry at all), with the reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A null <see cref="ModifierInfo.ControlGate"/> is ambiguous between "the reconciler applies
    /// this unconditionally" and "the reconciler gates it, but this rule's direction cannot reach a
    /// non-qualifying receiver anyway". <see cref="PoolResetSetAnalyzer"/> reads
    /// <c>.Set(x =&gt; x.IsEnabled = v)</c> (as <c>REACTOR_POOL_001</c> since issue #985 made the
    /// pool clear it; <c>REACTOR_MOD_002</c> before that),
    /// where the lambda parameter is already a <c>Control</c> because WinUI declares the dependency
    /// property only there — so no predicate is needed. <see cref="NoOpModifierAnalyzer"/> reads
    /// <c>.IsEnabled(v)</c>, a generic modifier callable on <em>any</em> element, where the same
    /// null would mean "never report" and quietly lose real findings.
    /// </para>
    /// <para>
    /// <c>ModifierTableIntegrityTests</c> requires every control gate it reads out of
    /// <c>ApplyModifiers</c> to match a declared <see cref="ModifierInfo.ControlGate"/> or appear
    /// here, so a newly gated modifier forces a deliberate decision in both directions instead of
    /// being invisible to one of them. The converse also holds: every row here must name a gate the
    /// reader actually finds, so the list cannot accumulate stale entries that silently suppress
    /// that check.
    /// </para>
    /// <para>
    /// One gate is deliberately absent: the content-alignment pair is written under a bare
    /// <c>if (fe is Control …)</c> with no <c>m.&lt;Prop&gt;</c> in the condition, so the gate reader —
    /// which ties a type test to the modifier guarding it — cannot attribute it to a property name.
    /// Recording it here would claim a gate the reader found, which it did not; the null-gate
    /// rationale for that pair is documented at its <see cref="Properties"/> entry instead.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> GateOnlyInReconciler =
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["IsEnabled"] = "Control-gated in ApplyModifiers. Mapped with a null ControlGate because WinUI declares IsEnabled on Control only, so a .Set lambda that compiles already qualifies. REACTOR_MOD_003 therefore does not report .IsEnabled(...) — declaring the gate here would be the way to turn that on.",
            ["TabIndex"] = "Control-gated in ApplyModifiers but unmapped in Properties (see DeliberatelyExcluded) — WinUI also declares TabIndex on UIElement, so the gate needs verifying before either direction uses it.",
            ["ElementSoundMode"] = "Control-gated in ApplyModifiers and unmapped in Properties: there is no .ElementSoundMode modifier to suggest, and the generic .ElementSoundMode(...) path has not been audited for the reverse direction.",

            // Reactor-only BiDi logical modifiers. Both fold into a physical write
            // (PaddingInlineStart → Padding, BorderInlineStart → BorderThickness) and inherit that
            // write's control gate, so `.PaddingInlineStart(8)` on a Grid is dropped exactly like
            // `.Padding(8)` is. They are NOT WinUI property names, and Properties is keyed by the
            // name written inside a .Set lambda — so mapping them there would add rows REACTOR_MOD_002
            // can never match. Covering them in REACTOR_MOD_003 needs a modifier-keyed gate table;
            // recorded here so the omission is deliberate rather than invisible.
            ["PaddingInlineStart"] = "Reactor-only BiDi logical modifier; folds into the Padding write and inherits its Control/Border/Grid/StackPanel/RelativePanel/TextBlock gate. Not a WinUI property name, so it has no home in Properties (which is keyed on those). REACTOR_MOD_003 coverage needs a modifier-keyed table.",
            ["PaddingInlineEnd"] = "Reactor-only BiDi logical modifier, the mirror of PaddingInlineStart; same guard, same gate, same reasoning.",
            ["BorderInlineStart"] = "Reactor-only BiDi logical modifier; folds into the BorderThickness write and inherits its Control/Border gate. Not a WinUI property name — same reasoning as PaddingInlineStart. (There is no BorderInlineEnd modifier.)",
        };

    /// <summary>
    /// Properties intentionally absent from <see cref="Properties"/>, with the reason.
    /// <c>ModifierTableIntegrityTests</c> requires every candidate modifier to appear in one
    /// of the two, so adding a modifier forces a deliberate choice instead of silently
    /// widening the gap between the DSL and the analyzer.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DeliberatelyExcluded =
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["Visibility"] = "Owned by REACTOR_VIS_001 — the modifier is .IsVisible(bool), an enum→bool translation that needs its own code fix.",
            ["RequestedTheme"] = "Owned by REACTOR_THEME_003 (RequestedThemeSetAnalyzer), which ships its own fix.",
            ["ItemsSource"] = "Owned by REACTOR_ITEMS_001 — the guidance is to pass items through the factory, not to swap in a modifier.",
            ["SelectedItem"] = "Owned by REACTOR_CTRL_001 — the fix removes the .Set rather than replacing it.",
            ["SelectedValue"] = "Owned by REACTOR_CTRL_001, as above.",
            ["Style"] = "The .ApplyStyle(name)/.AccentButton() modifiers are OnMount-based, so they are not equivalent to a .Set that re-applies every update.",
            ["Name"] = "No modifier exists. 154 .Set sites, all in selftest/E2E fixtures — adding a .Name(string) modifier is tracked separately.",
            ["BackgroundTransition"] = "The modifier takes a TimeSpan? duration and builds the BrushTransition itself; the property's value is a BrushTransition, so the rewrite would not type-check.",
            ["Content"] = "The .Content(Element) modifiers take a Reactor Element, not the native content object a .Set assigns.",
            ["Header"] = "Header modifiers take a string; a .Set may assign an arbitrary object.",
            ["Orientation"] = "Modifiers exist only for Slider/DatePicker; StackElement (the common .Set receiver) has none.",
            ["Spacing"] = "StackElement-only modifier; the property is also on native panels Reactor does not map.",
            ["FlowDirection"] = "Modifier exists only for RichTextRun, not for elements generally.",
            ["DisplayMode"] = "CalendarView-only modifier; the common .Set receiver is a SplitView.",
            ["IsTextScaleFactorEnabled"] = "Modifier exists for RichText* types but not TextBlockElement, the usual .Set receiver.",

            // CleanElement clears Image.Source, and a `Source` modifier does exist — but only on
            // ParallaxViewElement, a different control that shares the property name. There is no
            // .Source for ImageElement (the image is a constructor argument), so the rewrite would
            // not compile on the receiver that is actually reset. Surfaced by the whole-method
            // CleanElement scan added in #1193; recorded rather than mapped because a name match
            // across unrelated receivers is precisely what this table exists to reject.
            ["Source"] = "The only .Source modifier is ParallaxViewElement's; CleanElement resets Image.Source, which has no modifier.",

            // Transition helpers, not property assignments. `.ScaleTransition()` enables an
            // implicit composition animation; assigning the matching WinUI property through
            // .Set is a different operation, so the rewrite would not be equivalent.
            ["OpacityTransition"] = "Enables an implicit animation rather than assigning the property.",
            ["ScaleTransition"] = "Enables an implicit animation rather than assigning the property.",
            ["RotationTransition"] = "Enables an implicit animation rather than assigning the property.",
            ["TranslationTransition"] = "Enables an implicit animation rather than assigning the property.",

            // Signature mismatch: the modifier takes three floats, the WinUI property is a
            // Vector3, so passing the .Set right-hand side through would not compile.
            ["Translation"] = "Modifier takes (float x, float y, float z); the property is a Vector3.",

            // The XYFocus* modifiers take an ElementRef, not the FrameworkElement a .Set
            // assigns — same reasoning as PoolResetSetConsistencyTests.
            ["XYFocusUp"] = "Modifier takes ElementRef, not FrameworkElement.",
            ["XYFocusDown"] = "Modifier takes ElementRef, not FrameworkElement.",
            ["XYFocusLeft"] = "Modifier takes ElementRef, not FrameworkElement.",
            ["XYFocusRight"] = "Modifier takes ElementRef, not FrameworkElement.",

            ["Resources"] = "Modifier takes an Action<ResourceBuilder>; the property is a ResourceDictionary.",

            // Candidates, deliberately unmapped pending verification of how VisualModifiers
            // reach the control. Mapping one wrongly ships a code fix that compiles and does
            // nothing, which is the failure this table exists to prevent — so they stay out
            // until the application path is confirmed the way ApplyModifiers was.
            ["Scale"] = "Candidate: routed through VisualModifiers; application path not yet verified against a control-type gate.",
            ["Rotation"] = "Candidate: routed through VisualModifiers; application path not yet verified.",
            ["CenterPoint"] = "Candidate: routed through VisualModifiers; application path not yet verified.",
            ["TabIndex"] = "Candidate: Control-gated in ApplyModifiers, but WinUI also declares TabIndex on UIElement; needs the same gate treatment as Padding before mapping.",
            ["TabNavigation"] = "Candidate: Control-only property; not yet verified against ApplyModifiers.",
            ["XYFocusKeyboardNavigation"] = "Candidate: UIElement property; not yet verified against ApplyModifiers.",
        };

    // ── Attached properties ──────────────────────────────────────────────────
    //
    // Namespaces, named once. Pinned against the resolved method symbol so an unrelated
    // user type sharing the simple name cannot trigger the rule.
    private const string AutomationNs = "Microsoft.UI.Xaml.Automation";
    private const string ControlsNs = "Microsoft.UI.Xaml.Controls";
    private const string LayoutNs = "Microsoft.UI.Reactor.Layout";

    /// <summary>
    /// <c>Owner.Property</c> → modifier mapping for the attached properties
    /// <c>ElementPool.CleanElement</c> clears, matched against the
    /// <c>Owner.SetPROP(x, v)</c> invocation shape inside a <c>.Set(...)</c> lambda.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate dictionary rather than extra rows in <see cref="Properties"/>, because
    /// that table is keyed by <em>bare</em> property name and attached properties collide
    /// there. <c>AutomationProperties.SetName</c> would key as <c>"Name"</c> — which is
    /// <c>FrameworkElement.Name</c>, a different, modifier-less property the framework itself
    /// writes (<c>PanelAttachedHooks</c>), and one already listed in
    /// <see cref="DeliberatelyExcluded"/>. Owner-qualified keys make the two unambiguous and
    /// leave the instance path untouched.
    /// </para>
    /// <para>
    /// Keyed by the dependency property's base name — the <c>PROP</c> in
    /// <c>OWNER.PROPProperty</c> as <c>CleanElement</c> clears it — not by the setter method,
    /// because <c>PoolResetSetConsistencyTests</c> scans the reset list to prove this table
    /// is complete. The two differ for flex (<c>SetMinWidth</c> /
    /// <c>FlexMinWidthProperty</c>), which is why <see cref="AttachedModifierInfo.Setter"/>
    /// is overridable.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, AttachedModifierInfo> AttachedProperties =
        BuildAttached(
            // ── AutomationProperties — 1:1 with their modifier ───────────────
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "Name", "AutomationName"),
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "AutomationId", "AutomationId"),
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "HelpText", "HelpText"),
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "FullDescription", "FullDescription"),
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "LandmarkType", "Landmark"),
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "AccessibilityView", "AccessibilityView",
                receiverConflicts: new[] { "AccessibilityHidden" }),
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "LiveSetting", "LiveRegion"),
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "Level", "HierarchyLevel"),
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "ItemStatus", "ItemStatus"),
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "HeadingLevel", "HeadingLevel"),

            // ── AutomationProperties — diagnostic only ───────────────────────
            // .PositionInSet(position, size) sets both DPs at once, so neither single-value
            // setter has a mechanical rewrite.
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "PositionInSet", "PositionInSet",
                autoFix: false, modifierUsage: ".PositionInSet(position, size)"),
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "SizeOfSet", "PositionInSet",
                autoFix: false, modifierUsage: ".PositionInSet(position, size)"),
            // .Required() takes no argument and hardcodes true; SetIsRequiredForForm(fe, false)
            // has no modifier form at all.
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "IsRequiredForForm", "Required",
                autoFix: false, modifierUsage: ".Required()"),
            // SetLabeledBy takes the target DependencyObject; the modifier takes an
            // AutomationId string or an ElementRef.
            new AttachedModifierInfo("AutomationProperties", AutomationNs, "LabeledBy", "LabeledBy",
                autoFix: false, modifierUsage: ".LabeledBy(automationId)"),

            // ── ToolTipService ───────────────────────────────────────────────
            // SetToolTip takes object (it also accepts a ToolTip/UIElement); .ToolTip takes a
            // string, so the fix is withheld unless the value really is one — rich content
            // belongs on .WithToolTip(Element), which is not a mechanical rewrite of this.
            new AttachedModifierInfo("ToolTipService", ControlsNs, "ToolTip", "ToolTip",
                fixValueType: "System.String", receiverConflicts: new[] { "WithToolTip" }),
            new AttachedModifierInfo("ToolTipService", ControlsNs, "Placement", "ToolTipPlacement",
                receiverConflicts: new[] { "ToolTip", "WithToolTip" }),
            new AttachedModifierInfo("ToolTipService", ControlsNs, "PlacementTarget", "ToolTipPlacementTarget",
                autoFix: false, modifierUsage: ".ToolTipPlacementTarget(elementRef)"),

            // ── TitleBar (spec 059) ──────────────────────────────────────────
            new AttachedModifierInfo("TitleBar", ControlsNs, "IsDragRegion", "IsDragRegion"),

            // ── FlexPanel — all eleven funnel into one .Flex(...) ────────────
            // Diagnostic only: .Flex(...) is a single SetAttached(FlexAttached) that replaces
            // the whole record, so a per-statement chain would clobber the earlier calls. The
            // usage strings name the parameter, which is the whole answer for these.
            new AttachedModifierInfo("FlexPanel", LayoutNs, "Grow", "Flex", autoFix: false, modifierUsage: ".Flex(grow: ...)"),
            new AttachedModifierInfo("FlexPanel", LayoutNs, "Shrink", "Flex", autoFix: false, modifierUsage: ".Flex(shrink: ...)"),
            new AttachedModifierInfo("FlexPanel", LayoutNs, "Basis", "Flex", autoFix: false, modifierUsage: ".Flex(basis: ...)"),
            new AttachedModifierInfo("FlexPanel", LayoutNs, "FlexMinWidth", "Flex", autoFix: false, setter: "SetMinWidth", modifierUsage: ".Flex(minWidth: ...)"),
            new AttachedModifierInfo("FlexPanel", LayoutNs, "FlexMinHeight", "Flex", autoFix: false, setter: "SetMinHeight", modifierUsage: ".Flex(minHeight: ...)"),
            new AttachedModifierInfo("FlexPanel", LayoutNs, "AlignSelf", "Flex", autoFix: false, modifierUsage: ".Flex(alignSelf: ...)"),
            new AttachedModifierInfo("FlexPanel", LayoutNs, "Position", "Flex", autoFix: false, modifierUsage: ".Flex(position: ...)"),
            new AttachedModifierInfo("FlexPanel", LayoutNs, "Left", "Flex", autoFix: false, modifierUsage: ".Flex(left: ...)"),
            new AttachedModifierInfo("FlexPanel", LayoutNs, "Top", "Flex", autoFix: false, modifierUsage: ".Flex(top: ...)"),
            new AttachedModifierInfo("FlexPanel", LayoutNs, "Right", "Flex", autoFix: false, modifierUsage: ".Flex(right: ...)"),
            new AttachedModifierInfo("FlexPanel", LayoutNs, "Bottom", "Flex", autoFix: false, modifierUsage: ".Flex(bottom: ...)"));

    /// <summary>
    /// Attached properties <c>CleanElement</c> resets that are deliberately <em>not</em> in
    /// <see cref="AttachedProperties"/>, with the reason. <c>PoolResetSetConsistencyTests</c>
    /// requires every attached reset with a same-named modifier to appear in one of the two.
    /// </summary>
    /// <remarks>
    /// Every entry must be a genuine attached property that the <c>Owner.SetPROP(x, v)</c> rule
    /// cannot match. It is not a place to silence a property the attached scan claimed by
    /// mistake: <c>Grid.Padding</c> and <c>Grid.CornerRadius</c> sat here after the #1003 union
    /// with reasons that said, in as many words, "instance dependency property on Grid, not an
    /// attached property" — which is a misclassification to fix at the source, not to record.
    /// The test's scan — not this analyzer — now classifies per <em>property</em> rather than per
    /// owner (#1067): <c>PoolResetSetConsistencyTests</c> asks whether the owner declares the
    /// static <c>Owner.SetPROP(DependencyObject, value)</c> this rule matches, so
    /// <c>Grid.Padding</c> is instance while <c>Grid.Row</c> is attached, and neither needs an
    /// entry here. Suppressing that kind of entry would be actively harmful, because a genuinely
    /// attached <c>Grid.*</c> reset added later would land in the same bucket and read as
    /// already-triaged. That is now mechanically enforced rather than merely described:
    /// <c>PoolResetSetConsistencyTests.Excluded_Attached_Rows_Never_Name_An_Instance_Owner</c>
    /// rejects any key here whose owner the classification list already covers, so re-adding one of
    /// those rows — by hand or by a merge that reconciles this list against an older revision's —
    /// fails the build instead of silently reverting the fix (#1066).
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> DeliberatelyExcludedAttached =
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            // WinUI exposes these three as GetXxx(DependencyObject) returning a mutable
            // IList<DependencyObject> — there is no static SetXxx, so the
            // `Owner.SetPROP(x, v)` shape this rule matches cannot occur. Reactor itself
            // populates them through the getter (Reconciler's ApplyReferenceListEdge).
            // The .DescribedBy/.FlowsTo/.FlowsFrom modifiers do exist, which is exactly why
            // the exclusion has to be recorded rather than inferred.
            ["AutomationProperties.DescribedBy"] = "No static setter — WinUI exposes GetDescribedBy(...) returning a mutable IList<DependencyObject>.",
            ["AutomationProperties.FlowsTo"] = "No static setter — WinUI exposes GetFlowsTo(...) returning a mutable IList<DependencyObject>.",
            ["AutomationProperties.FlowsFrom"] = "No static setter — WinUI exposes GetFlowsFrom(...) returning a mutable IList<DependencyObject>.",
        };

    private static IReadOnlyDictionary<string, AttachedModifierInfo> BuildAttached(
        params AttachedModifierInfo[] entries)
    {
        var map = new Dictionary<string, AttachedModifierInfo>(
            entries.Length, System.StringComparer.Ordinal);
        foreach (var entry in entries)
            map.Add(entry.Key, entry);
        return map;
    }

    /// <summary>
    /// <c>Owner.Setter</c> → entry, the lookup the analyzer needs: it sees the setter method
    /// name at the call site, not the dependency property name.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, AttachedModifierInfo> AttachedBySetter =
        BuildAttachedBySetter();

    private static IReadOnlyDictionary<string, AttachedModifierInfo> BuildAttachedBySetter()
    {
        var map = new Dictionary<string, AttachedModifierInfo>(
            AttachedProperties.Count, System.StringComparer.Ordinal);
        foreach (var entry in AttachedProperties.Values)
            map.Add(entry.Owner + "." + entry.Setter, entry);
        return map;
    }
}
