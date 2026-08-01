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
    /// <c>REACTOR_MOD_002</c> instead, which is the hazard such a receiver does have: the
    /// write is dropped by the next render.
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
/// Every entry is pool-reset by construction: <see cref="ModifierTable.AttachedProperties"/>
/// only lists properties <c>ElementPool.CleanElement</c> clears, so they all report
/// <c>REACTOR_POOL_001</c>. There is deliberately no attached equivalent of the Info tier
/// (<c>REACTOR_MOD_002</c>) — the value is concentrated in "your write is silently
/// discarded".
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
    // Type groups, named once so the intent is legible at each use site.
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
            { "IsTabStop",           new ModifierInfo("IsTabStop",           poolReset: true) },

            // Pool-reset but only on some receivers (issue #985). CleanElement clears these
            // through a Control | Border | Panel/Grid/StackPanel | TextBlock chain that mirrors
            // the receivers ApplyModifiers writes them to, so the control gates below still
            // apply: the gate decides whether the modifier reaches the control at all,
            // poolReset decides which rule id reports it.
            //
            // poolReset is per-property but poolability is per-receiver, and Padding and
            // CornerRadius are gated for RelativePanel, which ElementPool never recycles. On
            // that receiver POOL_001's leading clause is simply false, and it is a Warning, so
            // a consumer building with TreatWarningsAsErrors would fail on a hazard they do not
            // have. poolResetGate narrows rule selection to the receivers the pool really
            // resets; everything outside it falls to MOD_002, which is the hazard those
            // receivers do have (the write is dropped by the next render). The lists are not
            // hand-maintained — ModifierUnsetClearValueTests derives the same intersection from
            // ElementPool.PoolableTypes and fails if either drifts. Closes issue #1051.
            //
            // WinUI declares most of these on Panel subclasses too, and the allow-lists
            // genuinely differ: Panel itself declares only Background; Grid, StackPanel, and
            // RelativePanel each declare their own border-box properties, and TextBlock takes
            // Padding but not CornerRadius. IsEnabled needs no gate — WinUI declares it on
            // Control, so if the .Set lambda compiles the receiver already qualifies.
            { "IsEnabled",       new ModifierInfo("IsEnabled",       poolReset: true) },
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
            ["Stretch"] = "Viewbox-only modifier.",
            ["FlowDirection"] = "Modifier exists only for RichTextRun, not for elements generally.",
            ["DisplayMode"] = "CalendarView-only modifier; the common .Set receiver is a SplitView.",
            ["IsTextScaleFactorEnabled"] = "Modifier exists for RichText* types but not TextBlockElement, the usual .Set receiver.",

            // No modifier exists at all. IsHitTestVisible is reset by ElementPool alongside
            // IsTabStop but is framework-internal (chart label/tick hiding, #162) with no
            // user-facing modifier — PoolResetSetConsistencyTests excludes it for the same
            // reason. Recorded here because a sweep report claimed a modifier existed; the
            // integrity test above is what caught that it does not.
            ["IsHitTestVisible"] = "No modifier exists; framework-internal, reset for chart-label hiding (#162).",

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
    /// They are now declared instance properties via <c>InstancePropertyOwners</c>, so the scan
    /// never claims them and nothing needs excluding. Suppressing that kind of entry here is
    /// actively harmful, because a genuinely attached <c>Grid.*</c> reset added later would land
    /// in the same bucket and read as already-triaged.
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
