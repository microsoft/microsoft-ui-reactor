using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using Windows.UI.WebUI;

namespace Microsoft.UI.Reactor.Core;

// AI-HINT: Reconciler.Update.cs — patches existing WinUI controls to match new Elements.
// Update() diffs old vs new Element and mutates the existing control in-place.
// Critical optimization: Element.ShallowEquals short-circuits when nothing changed.
// Returns null if existing control was patched; returns a new UIElement if the
// control type changed (caller must swap). Each UpdateXxx method mirrors its
// MountXxx counterpart but only touches properties that differ.

public sealed partial class Reconciler
{
    /// <summary>
    /// Diffs oldEl vs newEl and patches the existing control. Returns null if patched in-place,
    /// or a replacement UIElement if the control type changed at runtime.
    /// </summary>
    internal UIElement? Update(Element oldEl, Element newEl, UIElement control, Action requestRerender)
    {
        DebugElementsDiffed++;
        // Unwrap all layers of ModifiedElement, accumulating modifiers.
        // Inner modifiers override outer ones (via Merge: other wins where non-null).
        ElementModifiers? oldModifiers = oldEl.Modifiers;
        ElementModifiers? modifiers = newEl.Modifiers;
        while (oldEl is ModifiedElement oldMod && newEl is ModifiedElement newMod)
        {
            oldModifiers = oldModifiers is not null
                ? oldModifiers.Merge(oldMod.WrappedModifiers)
                : oldMod.WrappedModifiers;
            modifiers = modifiers is not null
                ? modifiers.Merge(newMod.WrappedModifiers)
                : newMod.WrappedModifiers;
            oldEl = oldMod.Inner;
            newEl = newMod.Inner;
        }
        // Merge any modifiers from the final inner element
        if (oldEl.Modifiers is not null)
            oldModifiers = oldModifiers is not null ? oldModifiers.Merge(oldEl.Modifiers) : oldEl.Modifiers;
        if (newEl.Modifiers is not null)
            modifiers = modifiers is not null ? modifiers.Merge(newEl.Modifiers) : newEl.Modifiers;

        // Short-circuit: if old and new elements are structurally identical,
        // skip all WinUI property access. This is the critical optimization for
        // large grids where only a fraction of elements change each frame.
        // Exception: elements with ThemeBindings must always re-apply because
        // the resolved brush value depends on the control's effective theme,
        // which can change independently of the element tree (e.g., parent
        // RequestedTheme toggle).
        // ReferenceEquals would fail constantly because fluent chains like
        // .Width(200).Margin(10) produce a fresh ElementModifiers each render —
        // identical values, new instance. Use structural equality so we skip
        // when nothing actually changed.
        //
        // Callback-presence (oldEl.HasCallbacks == newEl.HasCallbacks) must
        // also match: ShallowEquals ignores delegate identity, so a null→non-null
        // OnClick transition would otherwise be skipped and the lazy-wire path
        // in UpdateXxx never gets to attach the WinRT event. If presence
        // changes, force Update so EnsureXxxWiring (poolable) or the diff-based
        // null→non-null checks (non-poolable) can subscribe.
        if (Element.ShallowEquals(oldEl, newEl)
            && Element.ModifiersEqual(oldModifiers, modifiers)
            && oldEl.HasCallbacks == newEl.HasCallbacks
            && !ForceRenderThroughWrapper(newEl)
            && !IsOnDirtyAncestorPath(control))
        {
            DebugElementsSkipped++;
            // Refresh Tag so the event trampoline dispatches into the new element's
            // closure on next click/value-change. Gated on HasCallbacks so we skip
            // the DependencyProperty write entirely for leaves with no handlers
            // (TextBlock, Image, Border, etc.) — which is most of them.
            if (newEl.HasCallbacks && control is FrameworkElement tagFeSE)
                SetElementTag(tagFeSE, newEl);
            if (newEl.ThemeBindings is not null && control is FrameworkElement thFeSE)
                ApplyThemeBindings(thFeSE, newEl.ThemeBindings);
            // Re-resolve ThemeRef-based resource overrides on theme change
            if (newEl.ResourceOverrides is { ThemeRefs.Count: > 0 } && control is FrameworkElement resFeSE)
                ApplyResourceOverrides(resFeSE, newEl.ResourceOverrides, newEl.ResourceOverrides);
            return null; // null = keep existing control as-is
        }
        DebugUIElementsModified++;

        // Push context values onto scope before processing children
        var ctxValues = newEl.ContextValues;
        int ctxCount = 0;
        if (ctxValues is { Count: > 0 })
        {
            _contextScope.Push(ctxValues);
            ctxCount = ctxValues.Count;
        }

        UIElement? result;
        try
        {

        // Spec 047 §14 Phase 4 (§4.5) — dispatch is V1 registry → external
        // `_typeRegistry` → composition-primitive switch (legacy `UpdateXxx`
        // arms deleted; V1 is the production path).
        // V1 Update returns the UIElement to install in the parent's
        // slot. Standard handlers always return `control` unchanged
        // (§13 Q12 — no substitution on the public author surface);
        // decorator-style handlers (§14 Phase 3 completion) may return
        // a different instance (target-wrapping decorators whose
        // Target changed type). When the returned instance equals
        // `control`, set `result` to null so callers preserve identity.
        if (_v1Handlers.TryGet(newEl.GetType(), out var v1Entry))
        {
            var v1Result = v1Entry.Update(oldEl, newEl, control, requestRerender, this);
            result = ReferenceEquals(v1Result, control) ? null : v1Result;
        }
        // Registered types checked first
        else if (_typeRegistry.TryGetValue(newEl.GetType(), out var reg))
        {
            result = reg.Update(oldEl, newEl, control, requestRerender, this);
        }
        else
        {
        result = (oldEl, newEl, control) switch
        {
            (CommandHostElement o, CommandHostElement n, WinUI.Grid chGrid)
                => UpdateCommandHost(o, n, chGrid, requestRerender),
            (ErrorBoundaryElement oldEb, ErrorBoundaryElement newEb, Border)
                => UpdateErrorBoundary(oldEb, newEb, control, requestRerender),
            (FormFieldElement oldFf, FormFieldElement newFf, WinUI.StackPanel sp)
                => UpdateFormField(oldFf, newFf, sp, requestRerender),
            (ValidationVisualizerElement oldVv, ValidationVisualizerElement newVv, WinUI.StackPanel sp)
                => UpdateValidationVisualizer(oldVv, newVv, sp, requestRerender),
            (ValidationRuleElement, ValidationRuleElement n, WinUI.StackPanel)
                => UpdateValidationRule(n),
            (ComponentElement, ComponentElement, _)
                => UpdateComponent(oldEl, newEl, control, requestRerender),
            (FuncElement, FuncElement, _)
                => UpdateComponent(oldEl, newEl, control, requestRerender),
            (MemoElement, MemoElement, _)
                => UpdateComponent(oldEl, newEl, control, requestRerender),
            _ => Mount(newEl, requestRerender),
        };
        }

        // Apply inline modifiers after update. When old modifiers existed but new
        // modifiers are null, pass an empty instance so ApplyModifiers can clear
        // stale values (same principle as the flex attached-property fix).
        var target = result ?? control;

        // Record the control for highlight overlay only when the element's own
        // WinUI properties were actually updated (not just children recursed).
        // Containers whose only change is children references are excluded — the
        // individual children will be captured if they change.
        if (result is null && ReactorFeatureFlags.HighlightReconcileChanges
            && _highlightModified is not null
            && (!Element.OwnPropsEqual(oldEl, newEl) || !Element.ModifiersEqual(oldModifiers, modifiers)))
            _highlightModified.Add(control);
        if ((modifiers is not null || oldModifiers is not null) && target is FrameworkElement fe)
            ApplyModifiers(fe, oldModifiers, modifiers ?? new ElementModifiers(), requestRerender);

        // Re-apply the caption-derived default after modifiers have run so a
        // label change ("+ 1" → "+ 2") updates UIA Name when the author never
        // set an explicit name. No-ops when the author did.
        if (target is FrameworkElement captionFe)
            UpdateDefaultAutomationName(
                captionFe,
                ResolveCaptionForElement(oldEl),
                ResolveCaptionForElement(newEl));

        // Apply theme-resource bindings (ThemeRef → resolved Brush from WinUI resources)
        if (newEl.ThemeBindings is not null && target is FrameworkElement thFe)
            ApplyThemeBindings(thFe, newEl.ThemeBindings);

        // Apply per-control resource overrides (lightweight styling)
        if ((newEl.ResourceOverrides is not null || oldEl.ResourceOverrides is not null) && target is FrameworkElement resFe)
            ApplyResourceOverrides(resFe, oldEl.ResourceOverrides, newEl.ResourceOverrides);

        // Apply transitions after update (re-applies when transition config changes)
        if (newEl.ImplicitTransitions is not null || newEl.ThemeTransitions is not null)
            ApplyTransitions(target, newEl.ImplicitTransitions, newEl.ThemeTransitions);

        // Apply or clear Composition-layer layout animation
        if (newEl.LayoutAnimation is not null)
            ApplyLayoutAnimation(target, newEl.LayoutAnimation);
        else if (oldEl.LayoutAnimation is not null)
            ClearLayoutAnimation(target);

        // Apply or clear compositor property animation (.Animate() modifier)
        if (newEl.AnimationConfig is not null)
            ApplyPropertyAnimation(target, newEl.AnimationConfig, newEl.LayoutAnimation);
        else if (oldEl.AnimationConfig is not null)
            ClearPropertyAnimation(target, newEl.LayoutAnimation);

        // Apply or clear interaction states (.InteractionStates() modifier)
        if (newEl.InteractionStates is not null)
            ApplyInteractionStates(target, newEl.InteractionStates);
        else if (oldEl.InteractionStates is not null)
            ClearInteractionStates(target);

        // Apply keyframe animations (.Keyframes() modifier)
        if (newEl.KeyframeAnimations is not null)
            ApplyKeyframeAnimations(target, newEl.KeyframeAnimations);
        else if (oldEl.KeyframeAnimations is not null)
            ClearKeyframeAnimations(target, oldEl.KeyframeAnimations);

        // Apply or clear scroll-linked expression animations (.ScrollLinked() modifier)
        if (newEl.ScrollAnimation is not null)
            ApplyScrollAnimation(target, newEl.ScrollAnimation);
        else if (oldEl.ScrollAnimation is not null)
            ClearScrollAnimation(target, oldEl.ScrollAnimation);

        // Apply stagger delays to children (.Stagger() modifier)
        if (newEl.StaggerConfig is not null)
            ApplyStaggerDelays(target, newEl.StaggerConfig);

        }
        finally
        {
            if (ctxCount > 0)
                _contextScope.Pop(ctxCount);
        }

        return result;
    }

    private UIElement? UpdateText(TextBlockElement n, TextBlock tb)
    {
        if (tb.Text != n.Content) tb.Text = n.Content;
        if (n.FontSize.HasValue && tb.FontSize != n.FontSize.Value) tb.FontSize = n.FontSize.Value;
        if (n.Weight.HasValue && tb.FontWeight.Weight != n.Weight.Value.Weight) tb.FontWeight = n.Weight.Value;
        if (n.FontStyle.HasValue && tb.FontStyle != n.FontStyle.Value) tb.FontStyle = n.FontStyle.Value;
        if (n.HorizontalAlignment.HasValue && tb.HorizontalAlignment != n.HorizontalAlignment.Value) tb.HorizontalAlignment = n.HorizontalAlignment.Value;
        if (n.TextWrapping.HasValue && tb.TextWrapping != n.TextWrapping.Value) tb.TextWrapping = n.TextWrapping.Value;
        if (n.TextAlignment.HasValue && tb.TextAlignment != n.TextAlignment.Value) tb.TextAlignment = n.TextAlignment.Value;
        if (n.TextTrimming.HasValue && tb.TextTrimming != n.TextTrimming.Value) tb.TextTrimming = n.TextTrimming.Value;
        if (n.IsTextSelectionEnabled.HasValue && tb.IsTextSelectionEnabled != n.IsTextSelectionEnabled.Value) tb.IsTextSelectionEnabled = n.IsTextSelectionEnabled.Value;
        if (n.FontFamily is not null && tb.FontFamily != n.FontFamily) tb.FontFamily = n.FontFamily;
        if (n.LineHeight.HasValue && tb.LineHeight != n.LineHeight.Value) tb.LineHeight = n.LineHeight.Value;
        if (tb.MaxLines != n.MaxLines) tb.MaxLines = n.MaxLines;
        if (tb.CharacterSpacing != n.CharacterSpacing) tb.CharacterSpacing = n.CharacterSpacing;
        if (tb.TextDecorations != n.TextDecorations) tb.TextDecorations = n.TextDecorations;
        ApplySetters(n.Setters, tb);
        return null;
    }

    /// <summary>
    /// EXP-2: Bitmask-based UpdateText — compares old vs new TextBlockElement (pure C#)
    /// to determine which properties changed, then only touches those WinUI properties.
    /// Avoids COM interop reads for unchanged properties.
    /// </summary>
    private UIElement? UpdateTextBitmask(TextBlockElement old, TextBlockElement n, TextBlock tb)
    {
        var diff = TextBlockElement.DiffProps(old, n);
        if (diff == TextPropChanged.None) return null;

        if ((diff & TextPropChanged.Content) != 0) tb.Text = n.Content;
        if ((diff & TextPropChanged.FontSize) != 0 && n.FontSize.HasValue) tb.FontSize = n.FontSize.Value;
        if ((diff & TextPropChanged.Weight) != 0 && n.Weight.HasValue) tb.FontWeight = n.Weight.Value;
        if ((diff & TextPropChanged.FontStyle) != 0 && n.FontStyle.HasValue) tb.FontStyle = n.FontStyle.Value;
        if ((diff & TextPropChanged.HorizontalAlignment) != 0 && n.HorizontalAlignment.HasValue) tb.HorizontalAlignment = n.HorizontalAlignment.Value;
        if ((diff & TextPropChanged.TextWrapping) != 0 && n.TextWrapping.HasValue) tb.TextWrapping = n.TextWrapping.Value;
        if ((diff & TextPropChanged.TextAlignment) != 0 && n.TextAlignment.HasValue) tb.TextAlignment = n.TextAlignment.Value;
        if ((diff & TextPropChanged.TextTrimming) != 0 && n.TextTrimming.HasValue) tb.TextTrimming = n.TextTrimming.Value;
        if ((diff & TextPropChanged.IsTextSelectionEnabled) != 0 && n.IsTextSelectionEnabled.HasValue) tb.IsTextSelectionEnabled = n.IsTextSelectionEnabled.Value;
        if ((diff & TextPropChanged.FontFamily) != 0 && n.FontFamily is not null) tb.FontFamily = n.FontFamily;
        if ((diff & TextPropChanged.LineHeight) != 0 && n.LineHeight.HasValue) tb.LineHeight = n.LineHeight.Value;
        if ((diff & TextPropChanged.MaxLines) != 0) tb.MaxLines = n.MaxLines;
        if ((diff & TextPropChanged.CharacterSpacing) != 0) tb.CharacterSpacing = n.CharacterSpacing;
        if ((diff & TextPropChanged.TextDecorations) != 0) tb.TextDecorations = n.TextDecorations;
        if ((diff & TextPropChanged.Setters) != 0) ApplySetters(n.Setters, tb);
        return null;
    }

    private UIElement? UpdateRichTextBlock(RichTextBlockElement o, RichTextBlockElement n, WinUI.RichTextBlock rtb)
    {
        rtb.IsTextSelectionEnabled = n.IsTextSelectionEnabled;
        if (n.FontSize.HasValue) rtb.FontSize = n.FontSize.Value;
        if (n.TextWrapping.HasValue && rtb.TextWrapping != n.TextWrapping.Value) rtb.TextWrapping = n.TextWrapping.Value;
        if (rtb.MaxLines != n.MaxLines) rtb.MaxLines = n.MaxLines;
        if (n.LineHeight.HasValue && rtb.LineHeight != n.LineHeight.Value) rtb.LineHeight = n.LineHeight.Value;
        if (n.TextAlignment.HasValue && rtb.TextAlignment != n.TextAlignment.Value) rtb.TextAlignment = n.TextAlignment.Value;
        if (n.TextTrimming.HasValue && rtb.TextTrimming != n.TextTrimming.Value) rtb.TextTrimming = n.TextTrimming.Value;
        if (rtb.CharacterSpacing != n.CharacterSpacing) rtb.CharacterSpacing = n.CharacterSpacing;

        var oldParas = o.Paragraphs;
        var newParas = n.Paragraphs;

        // Both use simple text (no Paragraphs) — fast path.
        if (oldParas is null && newParas is null)
        {
            if (o.Text != n.Text)
            {
                // Cache the WinRT collection reference to avoid repeated interop calls.
                var blocks = rtb.Blocks;
                if (blocks.Count > 0 &&
                    blocks[0] is Microsoft.UI.Xaml.Documents.Paragraph p0)
                {
                    var inlines = p0.Inlines;
                    if (inlines.Count > 0 && inlines[0] is Microsoft.UI.Xaml.Documents.Run r0)
                        r0.Text = n.Text;
                }
            }
            ApplySetters(n.Setters, rtb);
            return null;
        }

        // Structural mismatch (one has Paragraphs, other doesn't) — full rebuild.
        if (oldParas is null || newParas is null)
        {
            RebuildRichTextBlocks(n, rtb);
            ApplySetters(n.Setters, rtb);
            return null;
        }

        // Both have Paragraphs — diff incrementally.
        int oldCount = oldParas.Length;
        int newCount = newParas.Length;
        int commonCount = Math.Min(oldCount, newCount);

        // Cache the WinRT Blocks collection to avoid repeated interop calls.
        var rtbBlocks = rtb.Blocks;

        // Update existing paragraphs in place.
        for (int pi = 0; pi < commonCount; pi++)
        {
            var oldPara = oldParas[pi];
            var newPara = newParas[pi];

            // Skip paragraphs whose content is structurally identical.
            if (Element.ParagraphEqual(oldPara, newPara)) continue;

            if (rtbBlocks.Count <= pi) break;
            var winPara = (Microsoft.UI.Xaml.Documents.Paragraph)rtbBlocks[pi];

            DiffParagraphInlines(oldPara, newPara, winPara);
        }

        // Remove excess paragraphs.
        while (rtbBlocks.Count > newCount)
            rtbBlocks.RemoveAt(rtbBlocks.Count - 1);

        // Add new paragraphs.
        for (int pi = oldCount; pi < newCount; pi++)
            rtbBlocks.Add(MountParagraph(newParas[pi]));

        ApplySetters(n.Setters, rtb);
        return null;
    }

    private static void DiffParagraphInlines(RichTextParagraph oldPara, RichTextParagraph newPara,
        Microsoft.UI.Xaml.Documents.Paragraph winPara)
    {
        var oldInlines = oldPara.Inlines;
        var newInlines = newPara.Inlines;
        int oldCount = oldInlines.Length;
        int newCount = newInlines.Length;
        int commonCount = Math.Min(oldCount, newCount);

        // Cache the WinRT InlineCollection once — each .Inlines access is a managed→WinRT
        // interop call, and each indexed get (winInlines[i]) is another. For documents with
        // hundreds of inlines this was the dominant cost in the profile (~14% self CPU).
        var winInlines = winPara.Inlines;

        // Update existing inlines in place where types match.
        for (int i = 0; i < commonCount; i++)
        {
            var oldInl = oldInlines[i];
            var newInl = newInlines[i];

            // Skip inlines that are record-equal (no changes).
            if (oldInl == newInl) continue;

            if (oldInl.GetType() != newInl.GetType())
            {
                // Type changed — replace this inline.
                winInlines.RemoveAt(i);
                winInlines.Insert(i, MountInline(newInl));
                continue;
            }

            var winInline = winInlines[i];
            switch (newInl)
            {
                case RichTextRun newRun:
                    if (winInline is Microsoft.UI.Xaml.Documents.Run winRun)
                        UpdateRun((RichTextRun)oldInl, newRun, winRun);
                    break;
                case RichTextHyperlink newLink:
                    if (winInline is Microsoft.UI.Xaml.Documents.Hyperlink winHl)
                        UpdateHyperlink((RichTextHyperlink)oldInl, newLink, winHl);
                    break;
                case RichTextLineBreak:
                    break;
            }
        }

        // Remove excess inlines.
        while (winInlines.Count > newCount)
            winInlines.RemoveAt(winInlines.Count - 1);

        // Add new inlines.
        for (int i = oldCount; i < newCount; i++)
            winInlines.Add(MountInline(newInlines[i]));
    }

    private static void UpdateRun(RichTextRun oldRun, RichTextRun newRun,
        Microsoft.UI.Xaml.Documents.Run winRun)
    {
        if (oldRun.Text != newRun.Text)
            winRun.Text = newRun.Text;
        if (oldRun.IsBold != newRun.IsBold)
            winRun.FontWeight = newRun.IsBold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
        if (oldRun.IsItalic != newRun.IsItalic)
            winRun.FontStyle = newRun.IsItalic ? global::Windows.UI.Text.FontStyle.Italic : global::Windows.UI.Text.FontStyle.Normal;
        if (oldRun.IsStrikethrough != newRun.IsStrikethrough)
            winRun.TextDecorations = newRun.IsStrikethrough ? global::Windows.UI.Text.TextDecorations.Strikethrough : global::Windows.UI.Text.TextDecorations.None;
        if (oldRun.FontSize != newRun.FontSize)
            winRun.FontSize = newRun.FontSize ?? (double)Microsoft.UI.Xaml.DependencyProperty.UnsetValue;
        if (oldRun.FontFamily != newRun.FontFamily)
        {
            if (newRun.FontFamily is not null)
                winRun.FontFamily = WinRTCache.GetFontFamily(newRun.FontFamily);
            else
                winRun.ClearValue(Microsoft.UI.Xaml.Documents.TextElement.FontFamilyProperty);
        }
        if (!ReferenceEquals(oldRun.Foreground, newRun.Foreground))
            winRun.Foreground = newRun.Foreground;
    }

    private static void UpdateHyperlink(RichTextHyperlink oldLink, RichTextHyperlink newLink,
        Microsoft.UI.Xaml.Documents.Hyperlink winHl)
    {
        if (oldLink.NavigateUri != newLink.NavigateUri)
        {
            try { winHl.NavigateUri = newLink.NavigateUri; }
            catch (Exception) { winHl.NavigateUri = new Uri("about:error"); }
            
        }
        if (oldLink.Text != newLink.Text && winHl.Inlines.Count > 0 &&
            winHl.Inlines[0] is Microsoft.UI.Xaml.Documents.Run hlRun)
            hlRun.Text = newLink.Text;
    }

    private static Microsoft.UI.Xaml.Documents.Inline MountInline(RichTextInline inline)
    {
        switch (inline)
        {
            case RichTextRun run:
                var r = new Microsoft.UI.Xaml.Documents.Run { Text = run.Text };
                if (run.IsBold) r.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                if (run.IsItalic) r.FontStyle = global::Windows.UI.Text.FontStyle.Italic;
                if (run.IsStrikethrough) r.TextDecorations = global::Windows.UI.Text.TextDecorations.Strikethrough;
                if (run.FontSize.HasValue) r.FontSize = run.FontSize.Value;
                if (run.FontFamily is not null) r.FontFamily = WinRTCache.GetFontFamily(run.FontFamily);
                if (run.Foreground is not null) r.Foreground = run.Foreground;
                return r;
            case RichTextHyperlink link:
                var l = link?.NavigateUri ?? new Uri("about:blank");
                l = l.ToString().Length < 1 ? l = new Uri("about:blank") : l;
                var hl = new Microsoft.UI.Xaml.Documents.Hyperlink();
                try { hl.NavigateUri = l; } catch { hl.NavigateUri = new Uri("about:blank"); }
                hl.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = link?.Text ?? ""});
                return hl;
            case RichTextLineBreak:
                return new Microsoft.UI.Xaml.Documents.LineBreak();
            default:
                return new Microsoft.UI.Xaml.Documents.Run { Text = "" };
        }
    }

    private static Microsoft.UI.Xaml.Documents.Paragraph MountParagraph(RichTextParagraph para)
    {
        var p = new Microsoft.UI.Xaml.Documents.Paragraph();
        foreach (var inline in para.Inlines)
            p.Inlines.Add(MountInline(inline));
        return p;
    }

    // Spec 047 §14 Phase 3-final Batch B — widened to internal static so the
    // legacy MountRichTextBlock arm AND RichTextBlockDescriptor's .OneWay set
    // lambda call the same rebuild path.
    internal static void RebuildRichTextBlocks(RichTextBlockElement n, WinUI.RichTextBlock rtb)
    {
        rtb.Blocks.Clear();
        if (n.Paragraphs is not null)
        {
            foreach (var para in n.Paragraphs)
                rtb.Blocks.Add(MountParagraph(para));
        }
        else
        {
            var p = new Microsoft.UI.Xaml.Documents.Paragraph();
            p.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = n.Text });
            rtb.Blocks.Add(p);
        }
    }

    internal UIElement? UpdateButton(ButtonElement o, ButtonElement n, WinUI.Button b, Action requestRerender)
    {
        ApplyButtonEnabledState(b, n);
        if (n.ContentElement is not null && o.ContentElement is not null && b.Content is UIElement existingContent)
        {
            var replacement = UpdateChild(o.ContentElement, n.ContentElement, existingContent, requestRerender);
            if (replacement is not null && !ReferenceEquals(b.Content, replacement))
            {
                UnmountChild(existingContent);
                b.Content = replacement;
            }
        }
        else if (n.ContentElement is not null)
        {
            if (b.Content is UIElement oldContent) UnmountChild(oldContent);
            b.Content = Mount(n.ContentElement, requestRerender);
        }
        else
        {
            b.Content = n.Label;
        }
        SetElementTag(b, n);
        EnsureButtonWiring(b, n);
        ApplySetters(n.Setters, b);
        return null;
    }

    private UIElement? UpdateHyperlinkButton(HyperlinkButtonElement o, HyperlinkButtonElement n, WinUI.HyperlinkButton hb)
    {
        hb.Content = n.Content;
        // Unconditional: a transition to null must clear the stale navigation target.
        if (o.NavigateUri != n.NavigateUri) hb.NavigateUri = n.NavigateUri;
        SetElementTag(hb, n);
        if (o.OnClick is null && n.OnClick is not null)
            hb.Click += (s, _) => (GetElementTag((UIElement)s!) as HyperlinkButtonElement)?.OnClick?.Invoke();
        ApplySetters(n.Setters, hb);
        return null;
    }

    private UIElement? UpdateRepeatButton(RepeatButtonElement o, RepeatButtonElement n, WinPrim.RepeatButton rb)
    {
        rb.Content = n.Label; rb.Delay = n.Delay; rb.Interval = n.Interval; SetElementTag(rb, n);
        if (o.OnClick is null && n.OnClick is not null)
            rb.Click += (s, _) => (GetElementTag((UIElement)s!) as RepeatButtonElement)?.OnClick?.Invoke();
        ApplySetters(n.Setters, rb);
        return null;
    }

    private UIElement? UpdateToggleButton(ToggleButtonElement o, ToggleButtonElement n, WinPrim.ToggleButton tb)
    {
        tb.Content = n.Label;
        if (n.IsThreeState)
        {
            if (!tb.IsThreeState) tb.IsThreeState = true;
            if (tb.IsChecked != n.CheckedState) tb.IsChecked = n.CheckedState;
        }
        else
        {
            if (tb.IsThreeState) tb.IsThreeState = false;
            if ((tb.IsChecked ?? false) != n.IsChecked) tb.IsChecked = n.IsChecked;
        }
        SetElementTag(tb, n);
        bool oldWired = o.OnIsCheckedChanged is not null || o.OnCheckedStateChanged is not null;
        bool newWired = n.OnIsCheckedChanged is not null || n.OnCheckedStateChanged is not null;
        if (!oldWired && newWired)
            tb.Click += (s, _) =>
            {
                var t = (WinPrim.ToggleButton)s!;
                if (GetElementTag(t) is not ToggleButtonElement live) return;
                live.OnIsCheckedChanged?.Invoke(t.IsChecked ?? false);
                live.OnCheckedStateChanged?.Invoke(t.IsChecked);
            };
        ApplySetters(n.Setters, tb);
        return null;
    }

    private UIElement? UpdateDropDownButton(DropDownButtonElement n, WinUI.DropDownButton ddb)
    {
        if (ddb.Content as string != n.Label) ddb.Content = n.Label;
        SetElementTag(ddb, n);
        ApplySetters(n.Setters, ddb);
        return null;
    }

    private UIElement? UpdateSplitButton(SplitButtonElement o, SplitButtonElement n, WinUI.SplitButton sb)
    {
        sb.Content = n.Label; SetElementTag(sb, n);
        if (o.OnClick is null && n.OnClick is not null)
            sb.Click += (s, _) => (GetElementTag((UIElement)s!) as SplitButtonElement)?.OnClick?.Invoke();
        ApplySetters(n.Setters, sb);
        return null;
    }





    private static bool CanSynchronizeNumberBoxImmediateValueWithoutReformat(NumberBoxElement el, string text, double value)
    {
        if (el.NumberFormatter is not null) return false;
        var canonical = value.ToString("G", global::System.Globalization.CultureInfo.CurrentCulture);
        return string.Equals(text, canonical, StringComparison.Ordinal);
    }

    private static bool AreNumberBoxValuesEquivalent(double left, double right)
    {
        var tolerance = 1e-12 * global::System.Math.Max(
            1.0,
            global::System.Math.Max(global::System.Math.Abs(left), global::System.Math.Abs(right)));
        return global::System.Math.Abs(left - right) <= tolerance;
    }


    internal UIElement? UpdateCheckBox(CheckBoxElement o, CheckBoxElement n, WinUI.CheckBox cb)
    {
        SetElementTag(cb, n);
        bool oldWired = o.OnIsCheckedChanged is not null || o.OnCheckedStateChanged is not null;
        bool newWired = n.OnIsCheckedChanged is not null || n.OnCheckedStateChanged is not null;
        if (!oldWired && newWired)
        {
            cb.Checked += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var el = GetElementTag(c) as CheckBoxElement;
                el?.OnIsCheckedChanged?.Invoke(true);
                el?.OnCheckedStateChanged?.Invoke(true);
            };
            cb.Unchecked += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var el = GetElementTag(c) as CheckBoxElement;
                el?.OnIsCheckedChanged?.Invoke(false);
                el?.OnCheckedStateChanged?.Invoke(false);
            };
            cb.Indeterminate += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var el = GetElementTag(c) as CheckBoxElement;
                el?.OnCheckedStateChanged?.Invoke(null);
            };
        }
        cb.Content = n.Label;
        cb.IsThreeState = n.IsThreeState;
        var target = n.IsThreeState ? n.CheckedState : n.IsChecked;
        if (cb.IsChecked != target)
        {
            ChangeEchoSuppressor.BeginSuppress(cb);
            cb.IsChecked = target;
        }
        ApplySetters(n.Setters, cb);
        return null;
    }









    private UIElement? UpdateProgress(ProgressElement n, WinUI.ProgressBar pb)
    {
        pb.IsIndeterminate = n.IsIndeterminate; pb.Minimum = n.Minimum; pb.Maximum = n.Maximum;
        pb.ShowError = n.ShowError; pb.ShowPaused = n.ShowPaused;
        if (n.Value.HasValue) pb.Value = n.Value.Value;
        ApplySetters(n.Setters, pb);
        return null;
    }

    private UIElement? UpdateProgressRing(ProgressRingElement n, WinUI.ProgressRing pr)
    {
        pr.IsIndeterminate = n.IsIndeterminate; pr.IsActive = n.IsActive;
        if (n.Value.HasValue) pr.Value = n.Value.Value;
        ApplySetters(n.Setters, pr);
        return null;
    }

    private UIElement? UpdatePersonPicture(PersonPictureElement n, WinUI.PersonPicture pp)
    {
        if (n.DisplayName is not null) pp.DisplayName = n.DisplayName;
        if (n.Initials is not null) pp.Initials = n.Initials;
        pp.IsGroup = n.IsGroup; pp.BadgeNumber = n.BadgeNumber;
        ApplySetters(n.Setters, pp);
        return null;
    }


    private UIElement? UpdateRichEditBox(RichEditBoxElement o, RichEditBoxElement n, WinUI.RichEditBox reb)
    {
        reb.IsReadOnly = n.IsReadOnly;
        if (n.Header is not null) reb.Header = n.Header;
        if (n.PlaceholderText is not null) reb.PlaceholderText = n.PlaceholderText;
        if (n.IsSpellCheckEnabled.HasValue && reb.IsSpellCheckEnabled != n.IsSpellCheckEnabled.Value)
            reb.IsSpellCheckEnabled = n.IsSpellCheckEnabled.Value;
        if (reb.MaxLength != n.MaxLength) reb.MaxLength = n.MaxLength;
        if (reb.TextWrapping != n.TextWrapping) reb.TextWrapping = n.TextWrapping;
        if (reb.AcceptsReturn != n.AcceptsReturn) reb.AcceptsReturn = n.AcceptsReturn;
        if (n.SelectionHighlightColor is not null && !ReferenceEquals(reb.SelectionHighlightColor, n.SelectionHighlightColor))
            reb.SelectionHighlightColor = n.SelectionHighlightColor;
        SetElementTag(reb, n);
        if (o.OnTextChanged is null && n.OnTextChanged is not null)
            reb.TextChanged += (s, _) =>
            {
                var r = (WinUI.RichEditBox)s!;
                r.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out var text);
                (GetElementTag(r) as RichEditBoxElement)?.OnTextChanged?.Invoke(text?.TrimEnd('\r') ?? "");
            };
        ApplySetters(n.Setters, reb);
        return null;
    }

    internal UIElement? UpdateWrapGrid(WrapGridElement o, WrapGridElement n, WinUI.VariableSizedWrapGrid wg, Action requestRerender)
    {
        wg.Orientation = n.Orientation;
        if (n.MaximumRowsOrColumns >= 0) wg.MaximumRowsOrColumns = n.MaximumRowsOrColumns;
        if (!double.IsNaN(n.ItemWidth)) wg.ItemWidth = n.ItemWidth;
        if (!double.IsNaN(n.ItemHeight)) wg.ItemHeight = n.ItemHeight;
        ReconcileChildren(o.Children, n.Children, wg, requestRerender);

        // Re-apply VariableSizedWrapGrid attached properties — children may have
        // changed their .WrapGridColumnSpan(...) / .WrapGridRowSpan(...) between
        // renders. ReconcileChildren filters nulls/EmptyElements like Canvas so
        // walk the panel children in parallel with the filtered new-children list.
        int panelIdx = 0;
        for (int i = 0; i < n.Children.Length && panelIdx < wg.Children.Count; i++)
        {
            if (n.Children[i] is null or EmptyElement) continue;
            var wga = n.Children[i].GetAttached<WrapGridAttached>();
            if (wga is not null && wg.Children[panelIdx] is FrameworkElement fe)
            {
                WinUI.VariableSizedWrapGrid.SetRowSpan(fe, wga.RowSpan);
                WinUI.VariableSizedWrapGrid.SetColumnSpan(fe, wga.ColumnSpan);
            }
            panelIdx++;
        }

        SetElementTag(wg, n);
        ApplySetters(n.Setters, wg);
        return null;
    }

    internal UIElement? UpdateCanvas(CanvasElement o, CanvasElement n, WinUI.Canvas canvas, Action requestRerender)
    {
        if (n.Width.HasValue && n.Width != o.Width) canvas.Width = n.Width.Value;
        if (n.Height.HasValue && n.Height != o.Height) canvas.Height = n.Height.Value;
        if (n.Background is not null) canvas.Background = n.Background;

        ReconcileChildren(o.Children, n.Children, canvas, requestRerender);

        // Re-apply Canvas attached properties (Left/Top) — skip nulls/EmptyElements
        // to stay aligned with canvas.Children (ChildReconciler filters those out).
        int panelIdx = 0;
        for (int i = 0; i < n.Children.Length && panelIdx < canvas.Children.Count; i++)
        {
            if (n.Children[i] is null or EmptyElement) continue;
            var ca = n.Children[i].GetAttached<CanvasAttached>();
            if (ca is not null && canvas.Children[panelIdx] is FrameworkElement fe)
                ApplyCanvasPosition(fe, ca);
            panelIdx++;
        }

        ApplySetters(n.Setters, canvas);
        return null;
    }

    internal UIElement? UpdateStack(StackElement o, StackElement n, WinUI.StackPanel sp, Action requestRerender)
    {
        if (o.Orientation != n.Orientation) sp.Orientation = n.Orientation;
        if (o.Spacing != n.Spacing) sp.Spacing = n.Spacing;
        if (n.HorizontalAlignment.HasValue && n.HorizontalAlignment != o.HorizontalAlignment) sp.HorizontalAlignment = n.HorizontalAlignment.Value;
        if (n.VerticalAlignment.HasValue && n.VerticalAlignment != o.VerticalAlignment) sp.VerticalAlignment = n.VerticalAlignment.Value;
        ReconcileChildren(o.Children, n.Children, sp, requestRerender);
        // No Tag set — StackPanel has no event handlers. Avoids expensive COM call.
        ApplySetters(n.Setters, sp);
        return null;
    }

    private UIElement? UpdateBorder(BorderElement o, BorderElement n, WinUI.Border b, Element newEl, Action requestRerender)
    {
        if (o.Child is null && n.Child is null)
        {
            // Both null — nothing to reconcile for child
        }
        else if (n.Child is null)
        {
            // Child removed
            if (b.Child is not null) UnmountRecursive(b.Child);
            b.Child = null;
        }
        else if (o.Child is null)
        {
            // Child added
            b.Child = Mount(n.Child, requestRerender);
        }
        else if (CanUpdate(o.Child, n.Child))
        {
            if (b.Child is not null)
            {
                var childRepl = Update(o.Child, n.Child, b.Child, requestRerender);
                if (childRepl is not null) return Mount(newEl, requestRerender);
            }
        }
        else return Mount(newEl, requestRerender);

        if (n.CornerRadius.HasValue) b.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(n.CornerRadius.Value);
        if (n.Background is not null) b.Background = n.Background;
        if (n.BorderBrush is not null) b.BorderBrush = n.BorderBrush;
        if (n.BorderThickness.HasValue) b.BorderThickness = new Microsoft.UI.Xaml.Thickness(n.BorderThickness.Value);
        // No Tag set — Border has no event handlers.
        ApplySetters(n.Setters, b);
        return null;
    }

    internal UIElement? UpdateExpander(ExpanderElement o, ExpanderElement n, WinUI.Expander exp, Action requestRerender)
    {
        exp.IsExpanded = n.IsExpanded;
        exp.ExpandDirection = n.ExpandDirection;

        // Element header wins over the string slot. Reconcile via ReconcileChild
        // when both old and new use HeaderTemplate; otherwise swap modes.
        if (n.HeaderTemplate is not null)
        {
            ReconcileChild(o.HeaderTemplate, n.HeaderTemplate,
                () => exp.Header as UIElement,
                c => exp.Header = c,
                () => exp.Header = n.Header,
                requestRerender);
        }
        else
        {
            if (exp.Header is UIElement headerCtrl) Unmount(headerCtrl);
            exp.Header = n.Header;
        }

        if (!ReferenceEquals(o.ContentTransitions, n.ContentTransitions))
            exp.ContentTransitions = n.ContentTransitions;

        // Reconcile content child
        if (exp.Content is UIElement existingContent && CanUpdate(o.Content, n.Content))
        {
            var replacement = Update(o.Content, n.Content, existingContent, requestRerender);
            if (replacement is not null && !ReferenceEquals(exp.Content, replacement))
                exp.Content = replacement;
        }
        else
        {
            if (exp.Content is UIElement oldContent)
                Unmount(oldContent);
            exp.Content = Mount(n.Content, requestRerender);
        }

        SetElementTag(exp, n);
        if (o.OnIsExpandedChanged is null && n.OnIsExpandedChanged is not null)
        {
            exp.Expanding += (s, _) => (GetElementTag((UIElement)s!) as ExpanderElement)?.OnIsExpandedChanged?.Invoke(true);
            exp.Collapsed += (s, _) => (GetElementTag((UIElement)s!) as ExpanderElement)?.OnIsExpandedChanged?.Invoke(false);
        }
        ApplySetters(n.Setters, exp);
        return null;
    }

    internal UIElement? UpdateNavigationHost(
        NavigationHostElement oldEl, NavigationHostElement newEl,
        WinUI.Grid grid, Action requestRerender)
    {
        if (!_navigationHostNodes.TryGetValue(grid, out var node))
        {
            // Lost tracking — remount from scratch
            return Mount(newEl, requestRerender);
        }

        var handle = (Navigation.INavigationHandle)newEl.NavigationHandle;
        var currentRoute = handle.CurrentRoute;

        // Update the RouteMap if the delegate reference changed (rare but possible if
        // the parent component recreates the lambda every render).
        node.RouteMap = newEl.RouteMap;

        // If the handle changed (different navigation stack wired up), re-subscribe
        if (!ReferenceEquals(node.Handle, handle))
        {
            if (node.RouteChangedHandler is not null)
                node.Handle.RouteChanged -= node.RouteChangedHandler;
            node.Handle.Detach();

            node.Handle = handle;
            void onRouteChanged() => requestRerender();
            handle.RouteChanged += onRouteChanged;
            node.RouteChangedHandler = onRouteChanged;

            // Re-wire lifecycle guard for the new handle
            handle.LifecycleGuard = ctx =>
            {
                InvokeNavigatingFrom(node.CurrentChildControl, ctx);
                if (!ctx.IsCancelled)
                {
                    node.PendingNavigationMode = ctx.Mode;
                    node.PendingPreviousRoute = ctx.Route;
                }
            };
        }

        node.RequestRerender = requestRerender;

        if (Equals(currentRoute, node.LastRenderedRoute) && node.CurrentChildElement is not null)
        {
            // Route unchanged — reconcile the existing child element in place
            var newChildElement = node.RouteMap(currentRoute);
            var replacement = node.CurrentChildControl is not null
                ? Update(node.CurrentChildElement, newChildElement, node.CurrentChildControl, requestRerender)
                : Mount(newChildElement, requestRerender);

            if (replacement is not null && node.CurrentChildControl is not null)
            {
                // Child control type changed — swap in grid
                var idx = grid.Children.IndexOf(node.CurrentChildControl);
                if (idx >= 0)
                    grid.Children[idx] = replacement;
                else
                    grid.Children.Add(replacement);
                Unmount(node.CurrentChildControl);
                node.CurrentChildControl = replacement;
            }
            else if (replacement is not null)
            {
                grid.Children.Add(replacement);
                node.CurrentChildControl = replacement;
            }

            node.CurrentChildElement = newChildElement;
        }
        else
        {
            // Route changed — transition from old page to new page.
            // Lifecycle sequence per spec:
            //   1. onNavigatingFrom (already done by LifecycleGuard before stack mutation)
            //   2-3. Stack mutation (already done)
            //   4-5. Resolve + mount new element (or restore from cache)
            //   6. Run transition animation
            //   7. onNavigatedTo (new page)
            //   8. onNavigatedFrom (old page)
            //   9. Unmount or cache old element

            var oldChildControl = node.CurrentChildControl;
            var oldChildElement = node.CurrentChildElement;
            var previousRoute = node.LastRenderedRoute;
            var pendingMode = node.PendingNavigationMode;
            var pendingPreviousRoute = node.PendingPreviousRoute;
            node.PendingNavigationMode = null;
            node.PendingPreviousRoute = null;

            // Collect lifecycle hooks from old page BEFORE detach/unmount
            var oldHooks = pendingMode is not null
                ? CollectLifecycleHooks(oldChildControl)
                : null;

            // Resolve transition: per-navigation override > host default
            var transitionOverride = handle.PendingTransitionOverride;
            handle.PendingTransitionOverride = null;
            var transition = transitionOverride ?? node.HostTransition;
            var mode = pendingMode ?? Navigation.NavigationMode.Push;

            // Resolve new child: check cache first, then mount fresh
            UIElement? newChildControl;
            Element? newChildElement;

            bool wasCacheHit = false;
            if (node.Cache is not null && node.Cache.TryGet(currentRoute, out var cached))
            {
                // Cache hit — restore the mounted control
                newChildControl = cached.MountedControl;
                newChildElement = cached.LastElement;
                node.Cache.Remove(currentRoute);
                wasCacheHit = true;
            }
            else
            {
                // Cache miss — mount fresh
                newChildElement = node.RouteMap(currentRoute);
                newChildControl = Mount(newChildElement, requestRerender);
            }

            // Destination-side guard: invoke onNavigatingTo on the new page.
            // If cancelled, revert to old page.
            if (!InvokeNavigatingTo(newChildControl, currentRoute, pendingPreviousRoute, mode))
            {
                if (!wasCacheHit && newChildControl is not null)
                    Unmount(newChildControl);
                return null;
            }

            // Update node state immediately
            node.CurrentChildElement = newChildElement;
            node.CurrentChildControl = newChildControl;
            node.LastRenderedRoute = currentRoute;

            // Action to finalize the old page (cache or unmount)
            void FinalizeOldPage(UIElement? oldCtrl, Element? oldElem, object? oldRoute)
            {
                if (oldCtrl is null) return;
                grid.Children.Remove(oldCtrl);

                if (node.Cache is not null && node.CacheMode != Navigation.NavigationCacheMode.Disabled
                    && oldRoute is not null)
                {
                    // Store in cache instead of unmounting
                    node.Cache.Add(oldRoute, new Navigation.CachedPage
                    {
                        MountedControl = oldCtrl,
                        LastElement = oldElem,
                        LastAccessed = DateTime.UtcNow,
                        CacheMode = node.CacheMode,
                    });
                }
                else
                {
                    Unmount(oldCtrl);
                }
            }

            // Determine whether to run an animated transition
            bool useAnimation = transition is not Navigation.SuppressTransition
                && oldChildControl is not null
                && newChildControl is not null;

            if (useAnimation)
            {
                // Mount new content at Opacity 0 alongside old content
                var inVisual = ElementCompositionPreview.GetElementVisual(newChildControl!);
                inVisual.Opacity = 0;
                grid.Children.Add(newChildControl!);
                node.TransitionInProgress = true;

                // Capture references for the completion callback
                var capturedOldControl = oldChildControl;
                var capturedOldElement = oldChildElement;
                var capturedOldRoute = previousRoute;
                var capturedNewControl = newChildControl!;
                var capturedMode = mode;
                var capturedCurrentRoute = currentRoute;
                var capturedPreviousRoute = pendingPreviousRoute;
                var capturedOldHooks = oldHooks;

                Navigation.TransitionEngine.RunTransition(
                    capturedOldControl!, capturedNewControl, transition, capturedMode,
                    onComplete: () =>
                    {
                        node.TransitionInProgress = false;
                        FinalizeOldPage(capturedOldControl, capturedOldElement, capturedOldRoute);

                        InvokePostNavigationLifecycle(
                            capturedNewControl, capturedOldHooks,
                            capturedCurrentRoute, capturedPreviousRoute, capturedMode);
                    });
            }
            else
            {
                // Instant swap (SuppressTransition or missing controls)
                FinalizeOldPage(oldChildControl, oldChildElement, previousRoute);

                if (newChildControl is not null)
                    grid.Children.Add(newChildControl);

                InvokePostNavigationLifecycle(
                    newChildControl, oldHooks,
                    currentRoute, pendingPreviousRoute, mode);
            }
        }

        // Update host properties if changed
        node.HostTransition = newEl.Transition;
        if (node.CacheMode != newEl.CacheMode)
        {
            node.CacheMode = newEl.CacheMode;
            if (newEl.CacheMode == Navigation.NavigationCacheMode.Disabled && node.Cache is not null)
            {
                node.Cache.Clear();
                node.Cache = null;
            }
            else if (newEl.CacheMode != Navigation.NavigationCacheMode.Disabled && node.Cache is null)
            {
                node.Cache = new Navigation.NavigationCache(newEl.CacheSize, evicted => Unmount(evicted));
            }
        }
        if (node.Cache is not null)
            node.Cache.MaxSize = newEl.CacheSize;

        return null; // Patched in place
    }

    private static object? FindNavItemByTag(global::System.Collections.IEnumerable items, string? selectedTag)
    {
        if (selectedTag is null) return null;
        foreach (var item in items)
        {
            if (item is WinUI.NavigationViewItem nvi)
            {
                if ((nvi.Tag as string) == selectedTag) return nvi;
                var child = FindNavItemByTag(nvi.MenuItems, selectedTag);
                if (child is not null) return child;
            }
        }
        return null;
    }


    internal void ReconcileChild(Element? oldChild, Element? newChild,
        Func<UIElement?> getControl, Action<UIElement> setControl, Action clearControl,
        Action requestRerender)
    {
        if (newChild is not null && oldChild is not null
            && getControl() is UIElement existing && CanUpdate(oldChild, newChild))
        {
            var replacement = Update(oldChild, newChild, existing, requestRerender);
            if (replacement is not null) setControl(replacement);
        }
        else if (newChild is not null)
        {
            if (getControl() is UIElement old) Unmount(old);
            var mounted = Mount(newChild, requestRerender);
            if (mounted is not null) setControl(mounted);
        }
        else if (newChild is null && getControl() is UIElement stale)
        {
            Unmount(stale);
            clearControl();
        }
    }


    private UIElement? UpdatePivot(PivotElement o, PivotElement n, WinUI.Pivot pivot, Action requestRerender)
    {
        SetElementTag(pivot, n);

        if (o.OnSelectedIndexChanged is null && n.OnSelectedIndexChanged is not null)
            pivot.SelectionChanged += (s, _) =>
            {
                var p = (WinUI.Pivot)s!;
                (GetElementTag(p) as PivotElement)?.OnSelectedIndexChanged?.Invoke(p.SelectedIndex);
            };

        var items = pivot.Items;
        int common = Math.Min(o.Items.Length, n.Items.Length);

        for (int i = 0; i < common; i++)
        {
            if (items[i] is not WinUI.PivotItem pi) continue;
            var oldItem = o.Items[i];
            var newItem = n.Items[i];

            if (pi.Header as string != newItem.Header) pi.Header = newItem.Header;

            if (pi.Content is UIElement existing && CanUpdate(oldItem.Content, newItem.Content))
            {
                var replacement = Update(oldItem.Content, newItem.Content, existing, requestRerender);
                if (replacement is not null && !ReferenceEquals(pi.Content, replacement))
                    pi.Content = replacement;
            }
            else
            {
                if (pi.Content is UIElement stale) Unmount(stale);
                pi.Content = Mount(newItem.Content, requestRerender);
            }
        }

        for (int i = items.Count - 1; i >= n.Items.Length; i--)
        {
            if (items[i] is WinUI.PivotItem stale && stale.Content is UIElement sc) Unmount(sc);
            items.RemoveAt(i);
        }

        for (int i = o.Items.Length; i < n.Items.Length; i++)
        {
            var newItem = n.Items[i];
            items.Add(new WinUI.PivotItem { Header = newItem.Header, Content = Mount(newItem.Content, requestRerender) });
        }

        if (n.Title is not null && pivot.Title as string != n.Title) pivot.Title = n.Title;

        // Only sync SelectedIndex when the element changed it — see UpdateTabView.
        if (o.SelectedIndex != n.SelectedIndex
            && n.SelectedIndex >= 0 && n.SelectedIndex < items.Count
            && pivot.SelectedIndex != n.SelectedIndex)
            pivot.SelectedIndex = n.SelectedIndex;

        ApplySetters(n.Setters, pivot);
        return null;
    }



    private UIElement? UpdateListBox(ListBoxElement o, ListBoxElement n, WinUI.ListBox lb)
    {
        SetElementTag(lb, n);
        // SelectionChanged wired unconditionally in MountListBox; tag refresh
        // suffices to pick up newly-attached OnSelectedIndexChanged /
        // OnSelectionChanged handlers.
        if (!StringArrayEquals(o.Items, n.Items))
        {
            lb.Items.Clear();
            foreach (var item in n.Items) lb.Items.Add(item);
        }
        if (o.SelectedIndex != n.SelectedIndex && lb.SelectedIndex != n.SelectedIndex)
            lb.SelectedIndex = n.SelectedIndex;
        ApplySetters(n.Setters, lb);
        return null;
    }

    private UIElement? UpdateSelectorBar(SelectorBarElement o, SelectorBarElement n, WinUI.SelectorBar bar)
    {
        SetElementTag(bar, n);

        if (o.OnSelectedIndexChanged is null && n.OnSelectedIndexChanged is not null)
            bar.SelectionChanged += (s, _) =>
            {
                var b = (WinUI.SelectorBar)s!;
                var idx = b.Items.IndexOf(b.SelectedItem);
                (GetElementTag(b) as SelectorBarElement)?.OnSelectedIndexChanged?.Invoke(idx);
            };

        var items = bar.Items;
        int common = Math.Min(o.Items.Length, n.Items.Length);

        for (int i = 0; i < common; i++)
        {
            if (items[i] is not WinUI.SelectorBarItem sbi) continue;
            var oldItem = o.Items[i];
            var newItem = n.Items[i];

            if (sbi.Text != newItem.Text) sbi.Text = newItem.Text;
            if (oldItem.Icon != newItem.Icon)
                sbi.Icon = ResolveIconString(newItem.Icon ?? "");
        }

        for (int i = items.Count - 1; i >= n.Items.Length; i--)
            items.RemoveAt(i);

        for (int i = o.Items.Length; i < n.Items.Length; i++)
        {
            var newItem = n.Items[i];
            var sbi = new WinUI.SelectorBarItem { Text = newItem.Text };
            if (newItem.Icon is not null) sbi.Icon = ResolveIconString(newItem.Icon);
            items.Add(sbi);
        }

        // Only sync selection when the element moved it.
        if (o.SelectedIndex != n.SelectedIndex
            && n.SelectedIndex >= 0 && n.SelectedIndex < items.Count)
        {
            var desired = items[n.SelectedIndex];
            if (!ReferenceEquals(bar.SelectedItem, desired)) bar.SelectedItem = desired;
        }
        ApplySetters(n.Setters, bar);
        return null;
    }

    private UIElement? UpdateSplitView(SplitViewElement o, SplitViewElement n, WinUI.SplitView sv, Action requestRerender)
    {
        if (sv.IsPaneOpen != n.IsPaneOpen) sv.IsPaneOpen = n.IsPaneOpen;
        if (sv.OpenPaneLength != n.OpenPaneLength) sv.OpenPaneLength = n.OpenPaneLength;
        if (sv.CompactPaneLength != n.CompactPaneLength) sv.CompactPaneLength = n.CompactPaneLength;
        if (sv.DisplayMode != n.DisplayMode) sv.DisplayMode = n.DisplayMode;
        if (sv.LightDismissOverlayMode != n.LightDismissOverlayMode) sv.LightDismissOverlayMode = n.LightDismissOverlayMode;
        if (!ReferenceEquals(o.PaneBackground, n.PaneBackground) && n.PaneBackground is not null)
            sv.PaneBackground = n.PaneBackground;

        ReconcileChild(o.Pane, n.Pane,
            () => sv.Pane as UIElement,
            c => sv.Pane = c,
            () => sv.Pane = null,
            requestRerender);

        ReconcileChild(o.Content, n.Content,
            () => sv.Content as UIElement,
            c => sv.Content = c,
            () => sv.Content = null,
            requestRerender);

        SetElementTag(sv, n);
        if (o.OnPaneOpenChanged is null && n.OnPaneOpenChanged is not null)
        {
            sv.PaneOpening += (s, _) => (GetElementTag((UIElement)s!) as SplitViewElement)?.OnPaneOpenChanged?.Invoke(true);
            sv.PaneClosing += (s, _) => (GetElementTag((UIElement)s!) as SplitViewElement)?.OnPaneOpenChanged?.Invoke(false);
        }
        ApplySetters(n.Setters, sv);
        return null;
    }


    internal UIElement? UpdateRelativePanel(RelativePanelElement o, RelativePanelElement n, WinUI.RelativePanel rp, Action requestRerender)
    {
        // RelativePanel's children reference each other by name for layout.
        // Reconcile in place when the children line up positionally; if the
        // count differs, fall back to a rebuild since attached-property
        // references depend on the name map.
        if (o.Children.Length != n.Children.Length)
        {
            foreach (var existing in rp.Children)
                if (existing is UIElement ue) Unmount(ue);
            rp.Children.Clear();
            // Re-run the mount logic so the two-pass attached-property wiring
            // stays consistent.
            var remount = Mount(n, requestRerender);
            return remount;
        }

        var nameMap = new Dictionary<string, UIElement>();
        for (int i = 0; i < n.Children.Length; i++)
        {
            if (rp.Children[i] is not UIElement existingCtrl) continue;
            UIElement? updated = existingCtrl;
            if (CanUpdate(o.Children[i], n.Children[i]))
            {
                var replacement = Update(o.Children[i], n.Children[i], existingCtrl, requestRerender);
                if (replacement is not null && !ReferenceEquals(rp.Children[i], replacement))
                {
                    rp.Children[i] = replacement;
                    updated = replacement;
                }
            }
            else
            {
                Unmount(existingCtrl);
                var mounted = Mount(n.Children[i], requestRerender);
                rp.Children[i] = mounted;
                updated = mounted;
            }
            var rpa = n.Children[i].GetAttached<RelativePanelAttached>();
            if (rpa is not null && updated is FrameworkElement fe)
            {
                fe.Name = rpa.Name;
                nameMap[rpa.Name] = updated;
            }
        }

        // Reapply relative attached properties using the refreshed name map.
        for (int i = 0; i < n.Children.Length; i++)
        {
            var rpa = n.Children[i].GetAttached<RelativePanelAttached>();
            if (rpa is null) continue;
            if (!nameMap.TryGetValue(rpa.Name, out var ctrl)) continue;
            if (rpa.RightOf is not null && nameMap.TryGetValue(rpa.RightOf, out var rightOf))
                WinUI.RelativePanel.SetRightOf(ctrl, rightOf);
            if (rpa.Below is not null && nameMap.TryGetValue(rpa.Below, out var below))
                WinUI.RelativePanel.SetBelow(ctrl, below);
            if (rpa.LeftOf is not null && nameMap.TryGetValue(rpa.LeftOf, out var leftOf))
                WinUI.RelativePanel.SetLeftOf(ctrl, leftOf);
            if (rpa.Above is not null && nameMap.TryGetValue(rpa.Above, out var above))
                WinUI.RelativePanel.SetAbove(ctrl, above);
            if (rpa.AlignLeftWith is not null && nameMap.TryGetValue(rpa.AlignLeftWith, out var alw))
                WinUI.RelativePanel.SetAlignLeftWith(ctrl, alw);
            if (rpa.AlignRightWith is not null && nameMap.TryGetValue(rpa.AlignRightWith, out var arw))
                WinUI.RelativePanel.SetAlignRightWith(ctrl, arw);
            if (rpa.AlignTopWith is not null && nameMap.TryGetValue(rpa.AlignTopWith, out var atw))
                WinUI.RelativePanel.SetAlignTopWith(ctrl, atw);
            if (rpa.AlignBottomWith is not null && nameMap.TryGetValue(rpa.AlignBottomWith, out var abw))
                WinUI.RelativePanel.SetAlignBottomWith(ctrl, abw);
            if (rpa.AlignHorizontalCenterWith is not null && nameMap.TryGetValue(rpa.AlignHorizontalCenterWith, out var ahcw))
                WinUI.RelativePanel.SetAlignHorizontalCenterWith(ctrl, ahcw);
            if (rpa.AlignVerticalCenterWith is not null && nameMap.TryGetValue(rpa.AlignVerticalCenterWith, out var avcw))
                WinUI.RelativePanel.SetAlignVerticalCenterWith(ctrl, avcw);
            WinUI.RelativePanel.SetAlignLeftWithPanel(ctrl, rpa.AlignLeftWithPanel);
            WinUI.RelativePanel.SetAlignRightWithPanel(ctrl, rpa.AlignRightWithPanel);
            WinUI.RelativePanel.SetAlignTopWithPanel(ctrl, rpa.AlignTopWithPanel);
            WinUI.RelativePanel.SetAlignBottomWithPanel(ctrl, rpa.AlignBottomWithPanel);
            WinUI.RelativePanel.SetAlignHorizontalCenterWithPanel(ctrl, rpa.AlignHorizontalCenterWithPanel);
            WinUI.RelativePanel.SetAlignVerticalCenterWithPanel(ctrl, rpa.AlignVerticalCenterWithPanel);
        }

        SetElementTag(rp, n);
        ApplySetters(n.Setters, rp);
        return null;
    }




    private static bool StringArrayEquals(string[] a, string[] b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private UIElement? UpdateBreadcrumbBar(BreadcrumbBarElement o, BreadcrumbBarElement n, WinUI.BreadcrumbBar bcb)
    {
        bcb.ItemsSource = n.Items.Select(i => i.Label).ToList();
        SetElementTag(bcb, n);
        if (o.OnItemClicked is null && n.OnItemClicked is not null)
            bcb.ItemClicked += (s, args) =>
            {
                var el = GetElementTag((UIElement)s!) as BreadcrumbBarElement;
                if (el is not null && args.Index >= 0 && args.Index < el.Items.Length) el.OnItemClicked?.Invoke(el.Items[args.Index]);
            };
        ApplySetters(n.Setters, bcb);
        return null;
    }

    private UIElement? UpdateInfoBar(InfoBarElement o, InfoBarElement n, WinUI.InfoBar ib, Action requestRerender)
    {
        ib.Title = n.Title ?? ""; ib.Message = n.Message ?? "";
        ib.Severity = n.Severity; ib.IsOpen = n.IsOpen; ib.IsClosable = n.IsClosable;
        if (!ReferenceEquals(o.IconSource, n.IconSource) && n.IconSource is not null)
            ib.IconSource = ResolveIconSource(n.IconSource);
        ReconcileChild(o.Content, n.Content,
            () => ib.Content as UIElement,
            c => ib.Content = c,
            () => ib.Content = null,
            requestRerender);
        SetElementTag(ib, n);
        if (o.OnClosed is null && n.OnClosed is not null)
            ib.Closed += (s, _) => (GetElementTag((UIElement)s!) as InfoBarElement)?.OnClosed?.Invoke();
        // Lazy-wire ActionButton.Click on null→non-null. The ActionButton
        // control itself only exists when ActionButtonContent was authored,
        // and Mount handles the case where both content and handler are
        // present. If the handler appears later without the button, WinUI
        // still won't render one — that's an author-side constraint.
        // Dispatch captures infoBar (not the button) so it reads the parent
        // InfoBar's Tag, which Update keeps fresh via SetElementTag above.
        if (o.OnActionButtonClick is null && n.OnActionButtonClick is not null
            && ib.ActionButton is WinUI.Button actionButton)
            actionButton.Click += (_, _) => (GetElementTag(ib) as InfoBarElement)?.OnActionButtonClick?.Invoke();
        ApplySetters(n.Setters, ib);
        return null;
    }

    private UIElement? UpdateInfoBadge(InfoBadgeElement n, WinUI.InfoBadge badge)
    {
        if (n.Value.HasValue) badge.Value = n.Value.Value;
        ApplySetters(n.Setters, badge);
        return null;
    }


    // Internal visibility — see comment on MountListView (1.15 Path B).
    internal UIElement? UpdateListView(ListViewElement o, ListViewElement n, WinUI.ListView lv, Action requestRerender)
    {
        lv.SelectionMode = n.SelectionMode;
        lv.IsItemClickEnabled = n.OnItemClick is not null;
        if (n.Header is not null) lv.Header = n.Header;
        if (lv.IncrementalLoadingTrigger != n.IncrementalLoadingTrigger)
            lv.IncrementalLoadingTrigger = n.IncrementalLoadingTrigger;
        if (!ReferenceEquals(o.ItemContainerStyle, n.ItemContainerStyle) && n.ItemContainerStyle is not null)
            lv.ItemContainerStyle = n.ItemContainerStyle;

        // Update ItemsSource — ContainerContentChanging re-mounts visible items via Tag.
        // Always set a new list when items differ (even same count) so WinUI re-realizes containers.
        if (!ReferenceEquals(o.Items, n.Items))
            lv.ItemsSource = Enumerable.Range(0, n.Items.Length).ToList();

        SetElementTag(lv, n);

        // Mount subscribes SelectionChanged unconditionally and reads handlers
        // via GetElementTag, so no lazy wire here — the tag refresh above
        // makes a newly-attached OnSelectedIndexChanged / OnSelectionChanged
        // pick up on the very next selection.
        if (o.OnItemClick is null && n.OnItemClick is not null)
            lv.ItemClick += (s, args) =>
            {
                var l = (WinUI.ListView)s!;
                if (args.ClickedItem is int idx)
                    (GetElementTag(l) as ListViewElement)?.OnItemClick?.Invoke(idx);
            };

        if (n.SelectedIndex >= 0) lv.SelectedIndex = n.SelectedIndex;
        ApplySetters(n.Setters, lv);
        return null;
    }

    internal UIElement? UpdateGridView(GridViewElement o, GridViewElement n, WinUI.GridView gv, Action requestRerender)
    {
        gv.SelectionMode = n.SelectionMode;
        gv.IsItemClickEnabled = n.OnItemClick is not null;
        if (n.Header is not null) gv.Header = n.Header;
        if (gv.IncrementalLoadingTrigger != n.IncrementalLoadingTrigger)
            gv.IncrementalLoadingTrigger = n.IncrementalLoadingTrigger;
        if (!ReferenceEquals(o.ItemContainerStyle, n.ItemContainerStyle) && n.ItemContainerStyle is not null)
            gv.ItemContainerStyle = n.ItemContainerStyle;

        if (!ReferenceEquals(o.Items, n.Items))
            gv.ItemsSource = Enumerable.Range(0, n.Items.Length).ToList();

        SetElementTag(gv, n);

        // SelectionChanged is wired unconditionally in Mount (see comment in
        // UpdateListView). Tag refresh suffices to pick up a later-attached
        // OnSelectedIndexChanged / OnSelectionChanged.
        if (o.OnItemClick is null && n.OnItemClick is not null)
            gv.ItemClick += (s, args) =>
            {
                var g = (WinUI.GridView)s!;
                if (args.ClickedItem is int idx)
                    (GetElementTag(g) as GridViewElement)?.OnItemClick?.Invoke(idx);
            };

        if (n.SelectedIndex >= 0) gv.SelectedIndex = n.SelectedIndex;
        ApplySetters(n.Setters, gv);
        return null;
    }

    private UIElement? UpdateFlipView(FlipViewElement o, FlipViewElement n, WinUI.FlipView fv, Action requestRerender)
    {
        ReconcileItemsChildren(o.Items, n.Items, fv, requestRerender);
        fv.SelectedIndex = n.SelectedIndex;
        SetElementTag(fv, n);
        if (o.OnSelectedIndexChanged is null && n.OnSelectedIndexChanged is not null)
            fv.SelectionChanged += (s, _) =>
            {
                var f = (WinUI.FlipView)s!;
                (GetElementTag(f) as FlipViewElement)?.OnSelectedIndexChanged?.Invoke(f.SelectedIndex);
            };
        ApplySetters(n.Setters, fv);
        return null;
    }

    /// <summary>
    /// Walks visible (realized) containers and reconciles each item's Element
    /// using the stored reactor element (attached DP) as the old element.
    /// Iterates the realized panel children directly rather than calling
    /// ContainerFromIndex(i) for every i in 0..ItemCount — on a virtualized
    /// list with thousands of items but a small viewport, that loop would do
    /// thousands of cross-WinRT lookups per parent re-render and discard
    /// most as null. Children of the realized panel IS the realized set, so
    /// iterating it is O(realized) instead of O(total).
    /// </summary>
    private void RefreshRealizedContainers(WinUI.ListViewBase listViewBase, Internal.IItemViewSource viewSource, Action requestRerender)
    {
        var panel = listViewBase.ItemsPanelRoot;
        if (panel is null) return;

        // Snapshot first — Update may indirectly mount new controls and modifying
        // Children during enumeration throws (WinUI's UIElementCollection enforces
        // this). Counts are small (one viewport's worth) so the copy is cheap.
        var realized = new List<UIElement>(panel.Children.Count);
        foreach (var child in panel.Children) realized.Add(child);

        foreach (var child in realized)
        {
            // Cast to SelectorItem so both ListView (ListViewItem) and GridView
            // (GridViewItem) containers are handled — both derive from SelectorItem
            // and share the same ContentTemplateRoot pattern.
            if (child is not Microsoft.UI.Xaml.Controls.Primitives.SelectorItem container) continue;
            if (container.ContentTemplateRoot is not ContentControl cc) continue;

            var index = listViewBase.IndexFromContainer(container);
            if (index < 0 || index >= viewSource.ItemCount) continue;

            var oldItemElement = GetElementTag(cc);
            var newItemElement = viewSource.BuildItemView(index);

            if (oldItemElement is not null && cc.Content is UIElement existingCtrl && CanUpdate(oldItemElement, newItemElement))
            {
                var replacement = Update(oldItemElement, newItemElement, existingCtrl, requestRerender);
                if (replacement is not null && !ReferenceEquals(cc.Content, replacement))
                    cc.Content = replacement;
            }
            else
            {
                if (cc.Content is UIElement oldCtrl)
                    Unmount(oldCtrl);
                cc.Content = Mount(newItemElement, requestRerender);
            }
            SetElementTag(cc, newItemElement);
        }
    }

    internal UIElement? UpdateTemplatedListView(TemplatedListElementBase o, TemplatedListElementBase n, WinUI.ListView lv, Action requestRerender)
    {
        lv.SelectionMode = n.GetSelectionMode();
        lv.IsItemClickEnabled = n.GetIsItemClickEnabled();
        var header = n.GetHeader();
        if (header is not null) lv.Header = header;

        // SetElementTag *before* the diff so HandleTemplatedContainerContentChanging
        // — which fires synchronously inside the OC insert/move ops — reads the
        // new element when materializing freshly-inserted containers.
        SetElementTag(lv, n);

        // Spec 042 Phase 1: feed structural changes through the
        // keyed-diff pipeline so WinUI animates only the affected
        // containers. RefreshRealizedContainers still runs in every case
        // because the viewBuilder is a closure that may produce
        // different content even when the key set is unchanged.
        ApplyKeyedDiffOrFallback(lv, n);
        RefreshRealizedContainers(lv, n, requestRerender);

        var selectedIndex = n.GetSelectedIndex();
        if (selectedIndex >= 0) lv.SelectedIndex = selectedIndex;
        n.ApplyControlSetters(lv);
        return null;
    }

    internal UIElement? UpdateTemplatedGridView(TemplatedListElementBase o, TemplatedListElementBase n, WinUI.GridView gv, Action requestRerender)
    {
        gv.SelectionMode = n.GetSelectionMode();
        gv.IsItemClickEnabled = n.GetIsItemClickEnabled();
        var header = n.GetHeader();
        if (header is not null) gv.Header = header;

        SetElementTag(gv, n);
        ApplyKeyedDiffOrFallback(gv, n);
        RefreshRealizedContainers(gv, n, requestRerender);

        var selectedIndex = n.GetSelectedIndex();
        if (selectedIndex >= 0) gv.SelectedIndex = selectedIndex;
        n.ApplyControlSetters(gv);
        return null;
    }

    /// <summary>
    /// Spec 042 §4 — apply the keyed diff to the control's attached
    /// <see cref="ReactorListState"/>. If the state is missing (control
    /// mounted before Phase 1, or the diff bailed out and the legacy
    /// ItemsSource was swapped in) we transparently rebuild a fresh
    /// state and re-bind ItemsSource. The result is correctness either
    /// way; bailout only degrades the animation.
    /// </summary>
    private void ApplyKeyedDiffOrFallback(WinUI.ListViewBase lvb, TemplatedListElementBase n)
    {
        var state = GetListState(lvb);
        if (state is null || !ReferenceEquals(lvb.ItemsSource, state.Source))
        {
            // No state, or another path replaced ItemsSource (bailout from a
            // prior render). Rebuild and rebind in one step.
            var fresh = BuildListStateFromElement(n);
            SetListState(lvb, fresh);
            lvb.ItemsSource = fresh.Source;
            return;
        }

        // Project new keys through the typed peer. Pass the active ambient
        // so newly-inserted ReactorRows are tagged with the kind and the
        // ContainerContentChanging handler can attach a per-container
        // enter animation as those containers realize. (spec 042 §6.)
        var ambient = AnimationAmbient.Current;
        var stats = KeyedListDiff.Apply(
            state,
            new TemplatedKeyAdapter(n),
            static (item, _) => item.Key,
            _logger,
            lvb.GetType().Name,
            ambient,
            controlInstance: lvb);

        // Drive per-container offset animations for survivors that moved.
        // Insert / Remove animations attach through the realize/recycle
        // path so they survive virtualization; Move requires the live
        // container handle here because the row instance was already
        // realized before the move op.
        if (ambient is { HasEffect: true } && stats.MovedRows is { Count: > 0 } movedRows)
            ApplyMoveAnimations(lvb, movedRows, ambient.Kind);

        if (stats.Bailout)
        {
            // Reset replaced state.Source's contents in bulk; ItemsSource
            // still references the same OC object so WinUI sees a single
            // Reset action and re-realizes. Acceptable per spec §4.3.
            // No additional binding refresh needed.
        }
    }

    /// <summary>
    /// Adapter so the generic <see cref="KeyedListDiff.Apply{T}"/> can run
    /// against the abstract non-generic <see cref="TemplatedListElementBase"/>
    /// without an extra allocation per item.
    /// </summary>
    private readonly struct TemplatedKeyAdapter : IReadOnlyList<TemplatedKeyAdapter.KeyOnly>
    {
        private readonly TemplatedListElementBase _el;
        public TemplatedKeyAdapter(TemplatedListElementBase el) => _el = el;
        public KeyOnly this[int index] => new(_el.GetKeyAt(index) ?? $"__null_{index}");
        public int Count => _el.ItemCount;
        public IEnumerator<KeyOnly> GetEnumerator()
        {
            for (int i = 0; i < _el.ItemCount; i++) yield return this[i];
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public readonly record struct KeyOnly(string Key);
    }

    private static ReactorListState BuildListStateFromElement(TemplatedListElementBase el)
    {
        var state = new ReactorListState();
        int n = el.ItemCount;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, el.GetKeyAt(i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }

    internal UIElement? UpdateTemplatedFlipView(TemplatedListElementBase o, TemplatedListElementBase n, WinUI.FlipView fv, Action requestRerender)
    {
        // FlipView items are pre-mounted directly (no ContainerContentChanging).
        // Build old element array from o, then reconcile like regular items.
        int oldCount = o.ItemCount;
        int newCount = n.ItemCount;
        int shared = Math.Min(oldCount, newCount);

        for (int i = 0; i < shared; i++)
        {
            var oldItemElement = o.BuildItemView(i);
            var newItemElement = n.BuildItemView(i);
            if (fv.Items[i] is UIElement existingCtrl && CanUpdate(oldItemElement, newItemElement))
            {
                var replacement = Update(oldItemElement, newItemElement, existingCtrl, requestRerender);
                if (replacement is not null && replacement != existingCtrl)
                    fv.Items[i] = replacement;
            }
            else
            {
                if (fv.Items[i] is UIElement oldCtrl) Unmount(oldCtrl);
                fv.Items[i] = Mount(newItemElement, requestRerender)!;
            }
        }

        // Remove excess
        for (int i = oldCount - 1; i >= shared; i--)
        {
            if (fv.Items[i] is UIElement oldCtrl) Unmount(oldCtrl);
            fv.Items.RemoveAt(i);
        }

        // Add new
        for (int i = shared; i < newCount; i++)
        {
            var ctrl = Mount(n.BuildItemView(i), requestRerender);
            if (ctrl is not null) fv.Items.Add(ctrl);
        }

        fv.SelectedIndex = n.GetSelectedIndex();
        SetElementTag(fv, n);
        n.ApplyControlSetters(fv);
        return null;
    }

    /// <summary>
    /// Spec 047 §14 Phase 3 finish — Port (7). Legacy update arm for
    /// <see cref="ItemsRepeaterElementBase"/>. Same TryUpdateFactory + keyed
    /// diff + RefreshRealizedItems pipeline as
    /// <see cref="UpdateLazyStack"/>, but operates directly on the
    /// bare <see cref="WinUI.ItemsRepeater"/> (no ScrollViewer host).
    /// </summary>
    private UIElement? UpdateItemsRepeater(ItemsRepeaterElementBase n, WinUI.ItemsRepeater repeater, Action requestRerender)
    {
        if (n.Layout is not null && !ReferenceEquals(repeater.Layout, n.Layout))
            repeater.Layout = n.Layout;

        if (repeater.ItemTemplate is IElementFactory existingFactory && n.TryUpdateFactory(existingFactory))
        {
            ApplyItemsRepeaterKeyedDiffOrFallback(repeater, n, existingFactory);
            n.RefreshRealizedItems(existingFactory, repeater);
        }
        else
        {
            var fresh = BuildListStateFromItemsRepeater(n);
            SetListState(repeater, fresh);
            repeater.ItemsSource = fresh.Source;
            var factory = n.CreateFactory(this, requestRerender, _pool);
            n.AttachListStateToFactory(factory, fresh);
            repeater.ItemTemplate = factory;
        }
        SetElementTag(repeater, n);
        ApplySetters(n.RepeaterSetters, repeater);
        return null;
    }

    private void ApplyItemsRepeaterKeyedDiffOrFallback(WinUI.ItemsRepeater repeater, ItemsRepeaterElementBase n, IElementFactory factory)
    {
        var state = GetListState(repeater);
        if (state is null || !ReferenceEquals(repeater.ItemsSource, state.Source))
        {
            var fresh = BuildListStateFromItemsRepeater(n);
            SetListState(repeater, fresh);
            repeater.ItemsSource = fresh.Source;
            n.AttachListStateToFactory(factory, fresh);
            return;
        }

        var ambient = AnimationAmbient.Current;
        var keyAdapter = new ItemsRepeaterKeyAdapter(n);
        var stats = KeyedListDiff.Apply(
            state,
            keyAdapter,
            static (k, _) => k.Key,
            _logger,
            repeater.GetType().Name,
            ambient,
            controlInstance: repeater);

        if (ambient is { HasEffect: true } && stats.MovedRows is { Count: > 0 } movedRows)
            ApplyMoveAnimationsRepeater(repeater, movedRows, ambient.Kind);
    }

    private readonly struct ItemsRepeaterKeyAdapter : IReadOnlyList<ItemsRepeaterKeyAdapter.KeyOnly>
    {
        private readonly ItemsRepeaterElementBase _el;
        public ItemsRepeaterKeyAdapter(ItemsRepeaterElementBase el) => _el = el;
        public KeyOnly this[int index] => new(((Internal.IKeyedItemSource)_el).GetKeyAt(index) ?? $"__null_{index}");
        public int Count => _el.ItemCount;
        public IEnumerator<KeyOnly> GetEnumerator()
        {
            for (int i = 0; i < _el.ItemCount; i++) yield return this[i];
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public readonly record struct KeyOnly(string Key);
    }

    private static Internal.ReactorListState BuildListStateFromItemsRepeater(ItemsRepeaterElementBase ir)
    {
        var state = new Internal.ReactorListState();
        int n = ir.ItemCount;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, ((Internal.IKeyedItemSource)ir).GetKeyAt(i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }

    internal UIElement? UpdateLazyStack(LazyStackElementBase n, WinUI.ScrollViewer sv, Action requestRerender)
    {
        if (sv.Content is WinUI.ItemsRepeater repeater)
        {
            // Try to update the existing factory in place. This avoids
            // replacing ItemTemplate, which would cause ItemsRepeater to
            // re-realize all items (modifying Children during layout →
            // "Cannot run layout in the middle of a collection change").
            // The factory keeps its identity; existing realized items
            // stay mounted. On next scroll or layout, IElementFactory.GetElement
            // uses the updated viewBuilder to produce new content.
            if (repeater.ItemTemplate is IElementFactory existingFactory && n.TryUpdateFactory(existingFactory))
            {
                // Spec 042 Phase 1: route Items changes through the keyed
                // diff into the internally-owned OC<ReactorRow>. WinUI
                // sees incremental Insert/Move/RemoveAt events and only
                // animates affected containers; the steady-state
                // RefreshRealizedItems below still runs for per-row
                // content updates.
                ApplyLazyKeyedDiffOrFallback(repeater, n, existingFactory);
                n.RefreshRealizedItems(existingFactory, repeater);
            }
            else
            {
                // First mount or type mismatch — full replacement using the
                // Phase 1 OC<ReactorRow> binding shape.
                var fresh = BuildListStateFromLazy(n);
                SetListState(repeater, fresh);
                repeater.ItemsSource = fresh.Source;
                var factory = n.CreateFactory(this, requestRerender, _pool);
                n.AttachListStateToFactory(factory, fresh);
                repeater.ItemTemplate = factory;
            }
            if (repeater.Layout is WinUI.StackLayout layout)
                layout.Spacing = n.Spacing;
            SetElementTag(repeater, n);
            ApplySetters(n.RepeaterSetters, repeater);
        }
        SetElementTag(sv, n);
        ApplySetters(n.ScrollViewerSetters, sv);
        return null;
    }

    // ── ItemContainer ───────────────────────────────────────────────────


    // ── ItemsView ───────────────────────────────────────────────────────



    private readonly struct ItemsViewKeyAdapter : IReadOnlyList<ItemsViewKeyAdapter.KeyOnly>
    {
        private readonly ItemsViewElementBase _el;
        public ItemsViewKeyAdapter(ItemsViewElementBase el) => _el = el;
        public KeyOnly this[int index] => new(_el.GetKeyAt(index) ?? $"__null_{index}");
        public int Count => _el.ItemCount;
        public IEnumerator<KeyOnly> GetEnumerator()
        {
            for (int i = 0; i < _el.ItemCount; i++) yield return this[i];
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public readonly record struct KeyOnly(string Key);
    }


    private void ApplyLazyKeyedDiffOrFallback(WinUI.ItemsRepeater repeater, LazyStackElementBase n, IElementFactory factory)
    {
        var state = GetListState(repeater);
        if (state is null || !ReferenceEquals(repeater.ItemsSource, state.Source))
        {
            var fresh = BuildListStateFromLazy(n);
            SetListState(repeater, fresh);
            repeater.ItemsSource = fresh.Source;
            n.AttachListStateToFactory(factory, fresh);
            return;
        }

        var ambient = AnimationAmbient.Current;
        var stats = KeyedListDiff.Apply(
            state,
            new LazyKeyAdapter(n),
            static (item, _) => item.Key,
            _logger,
            repeater.GetType().Name,
            ambient,
            controlInstance: repeater);

        // ItemsRepeater realizes containers through ElementFactory, so the
        // enter animation runs from there. Moves on already-realized
        // elements need the same handle-based offset animation as the
        // templated list path. (spec 042 §6.)
        if (ambient is { HasEffect: true } && stats.MovedRows is { Count: > 0 } movedRows)
            ApplyMoveAnimationsRepeater(repeater, movedRows, ambient.Kind);
        // Bailout reset still mutates state.Source in place, so the
        // existing ItemsSource binding remains valid.
    }

    private readonly struct LazyKeyAdapter : IReadOnlyList<LazyKeyAdapter.KeyOnly>
    {
        private readonly LazyStackElementBase _el;
        public LazyKeyAdapter(LazyStackElementBase el) => _el = el;
        public KeyOnly this[int index] => new(_el.GetKeyAt(index) ?? $"__null_{index}");
        public int Count => _el.ItemCount;
        public IEnumerator<KeyOnly> GetEnumerator()
        {
            for (int i = 0; i < _el.ItemCount; i++) yield return this[i];
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public readonly record struct KeyOnly(string Key);
    }

    private static ReactorListState BuildListStateFromLazy(LazyStackElementBase lazy)
    {
        var state = new ReactorListState();
        int n = lazy.ItemCount;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, lazy.GetKeyAt(i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }

    /// <summary>
    /// Spec 042 §6 — per-container offset animation for ListView/GridView
    /// survivors that moved index inside an active <see cref="Animations.Animate"/>
    /// transaction. WinUI's <c>ListViewBase.ContainerFromItem</c>
    /// returns the live container for a realized row (null for virtualized
    /// ones, which is fine — the realize path attaches the animation when
    /// they come back into view).
    /// </summary>
    private void ApplyMoveAnimations(WinUI.ListViewBase lvb, IReadOnlyList<ReactorRow> moved, AnimationKind kind)
    {
        var curve = AnimationKindMap.ToCurve(kind);
        if (curve is null) return;

        // WinUI's container realignment for OC.Move events runs on the
        // next layout pass — calling ContainerFromIndex synchronously
        // here returns null even for items whose containers are realized,
        // because the lookup is keyed on the pre-move position the
        // ListView hasn't reconciled yet. Defer to the next dispatcher
        // turn so the lookup runs after layout. Implicit-Offset attached
        // *after* the position change still animates subsequent layout
        // shifts on the same container, which is the right shape for
        // continued reordering inside one Animate block.
        var dq = global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Action attach = () =>
        {
            for (int i = 0; i < moved.Count; i++)
            {
                var row = moved[i];
                try
                {
                    var container = lvb.ContainerFromIndex(row.Index) as UIElement
                                  ?? lvb.ContainerFromItem(row) as UIElement;
                    if (container is not null)
                        StartMoveOffsetAnimation(container, curve);
                }
                catch { /* best-effort */ }
            }
        };
        if (dq is not null) dq.TryEnqueue(global::Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => attach());
        else attach();
    }

    /// <summary>
    /// Spec 042 §6 — same as <see cref="ApplyMoveAnimations"/> but routed
    /// through <see cref="WinUI.ItemsRepeater.TryGetElement"/> because
    /// ItemsRepeater doesn't expose <c>ContainerFromItem</c>. Row.Index is
    /// the post-move target position, which is what TryGetElement keys on.
    /// </summary>
    private void ApplyMoveAnimationsRepeater(WinUI.ItemsRepeater repeater, IReadOnlyList<ReactorRow> moved, AnimationKind kind)
    {
        var curve = AnimationKindMap.ToCurve(kind);
        if (curve is null) return;
        for (int i = 0; i < moved.Count; i++)
        {
            try
            {
                var container = repeater.TryGetElement(moved[i].Index);
                if (container is not null)
                    StartMoveOffsetAnimation(container, curve);
            }
            catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// One-shot Composition offset animation: snap the visual to its
    /// previous offset and animate to zero so the row visibly slides into
    /// its new layout slot. The expression keyframe form is required so
    /// the spring/ease curve picks the *current* visual.Offset as the
    /// starting value — WinUI has already moved the layout slot under us
    /// by the time we attach. (spec 042 §6, Q4 — per-container.)
    /// </summary>
    private static void StartMoveOffsetAnimation(UIElement container, Curve curve)
    {
        var visual = global::Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(container);
        var compositor = visual.Compositor;
        var anim = AnimationHelper.CreateVector3ImplicitAnimation(compositor, "Offset", curve);
        // Implicit animations fire automatically when WinUI assigns the
        // new Offset on layout; attaching here means the next layout pass
        // animates instead of snapping. We deliberately don't pre-set
        // Offset — letting the implicit animation observe WinUI's own
        // assignment is what makes the move read correctly without us
        // racing the layout pass.
        var coll = compositor.CreateImplicitAnimationCollection();
        coll["Offset"] = anim;
        visual.ImplicitAnimations = coll;
    }


    private UIElement? UpdateCommandHost(CommandHostElement o, CommandHostElement n, WinUI.Grid host, Action requestRerender)
    {
        // Update child element
        if (host.Children.Count > 0 && host.Children[0] is UIElement existingChild)
        {
            var replacement = UpdateChild(o.Child, n.Child, existingChild, requestRerender);
            if (replacement is not null)
            {
                UnmountChild(existingChild);
                host.Children[0] = replacement;
            }
        }
        else
        {
            var child = Mount(n.Child, requestRerender);
            if (child is not null) host.Children.Add(child);
        }

        // Rebuild accelerators — clear and re-add (commands may have changed enabled state or handlers)
        host.KeyboardAccelerators.Clear();
        AddCommandHostAccelerators(host, n.Commands);

        SetElementTag(host, n);
        return null;
    }


    internal UIElement? UpdateGrid(Core.GridElement o, Core.GridElement n, WinUI.Grid g, Action requestRerender)
    {
        if (o.RowSpacing != n.RowSpacing) g.RowSpacing = n.RowSpacing;
        if (o.ColumnSpacing != n.ColumnSpacing) g.ColumnSpacing = n.ColumnSpacing;

        // Update column/row definitions when the GridDefinition changes.
        if (!ReferenceEquals(o.Definition, n.Definition))
        {
            var newCols = n.Definition.Columns;
            if (newCols.Length != g.ColumnDefinitions.Count)
            {
                g.ColumnDefinitions.Clear();
                foreach (var col in newCols) g.ColumnDefinitions.Add(ParseColumnDef(col));
            }
            else
            {
                for (int i = 0; i < newCols.Length; i++)
                {
                    var parsed = ParseColumnDef(newCols[i]);
                    if (g.ColumnDefinitions[i].Width != parsed.Width)
                        g.ColumnDefinitions[i].Width = parsed.Width;
                }
            }

            var newRows = n.Definition.Rows;
            if (newRows.Length != g.RowDefinitions.Count)
            {
                g.RowDefinitions.Clear();
                foreach (var row in newRows) g.RowDefinitions.Add(ParseRowDef(row));
            }
            else
            {
                for (int i = 0; i < newRows.Length; i++)
                {
                    var parsed = ParseRowDef(newRows[i]);
                    if (g.RowDefinitions[i].Height != parsed.Height)
                        g.RowDefinitions[i].Height = parsed.Height;
                }
            }
        }

        // When old and new child counts differ, the tree structure changed (e.g., a split
        // was added or removed). Delegate to ChildReconciler which handles this safely,
        // including the keyed path.
        if (o.Children.Length != n.Children.Length || o.Children.Length != g.Children.Count)
        {
            ReconcileChildren(o.Children, n.Children, g, requestRerender);
            // Re-apply grid placement for all children after reconciliation
            for (int i = 0; i < n.Children.Length && i < g.Children.Count; i++)
            {
                var ga = n.Children[i].GetAttached<GridAttached>();
                if (ga is not null && g.Children[i] is FrameworkElement fe)
                {
                    WinUI.Grid.SetRow(fe, ga.Row);
                    WinUI.Grid.SetColumn(fe, ga.Column);
                    if (ga.RowSpan > 1) WinUI.Grid.SetRowSpan(fe, ga.RowSpan);
                    if (ga.ColumnSpan > 1) WinUI.Grid.SetColumnSpan(fe, ga.ColumnSpan);
                }
            }
            SetElementTag(g, n);
            ApplySetters(n.Setters, g);
            return null;
        }

        // Fast path: same child count — reconcile positionally with bounds checks.
        int count = o.Children.Length;

        for (int i = 0; i < count; i++)
        {
            var oldChild = o.Children[i];
            var newChild = n.Children[i];

            // Early skip: element + modifiers + attached all identical — avoid COM
            // g.Children[i] read entirely. ShallowEquals already checks modifiers
            // and attached (GridAttached), so grid placement is also covered.
            if (Element.CanSkipUpdate(oldChild, newChild))
            {
                // Must still refresh Tag when the element exposes callbacks,
                // otherwise the event trampoline dispatches through the previous
                // render's stale closure. Pays one children.Get COM call only
                // for callback-bearing elements; handler-free leaves stay free.
                if (newChild.HasCallbacks && i < g.Children.Count
                    && g.Children[i] is FrameworkElement fe)
                    SetElementTag(fe, newChild);
                continue;
            }

            // Guard: recursive Reconcile may have modified g.Children (e.g., via
            // component re-renders that remove children from this grid).
            if (i >= g.Children.Count) break;

            var existingCtrl = g.Children[i];
            var replacement = Reconcile(oldChild, newChild, existingCtrl, requestRerender);
            var wasReplaced = replacement is not null && replacement != existingCtrl;
            if (wasReplaced && i < g.Children.Count)
            {
                g.Children[i] = replacement!;
            }
            // Update grid placement — re-apply when control was replaced (new control
            // defaults to Row=0/Column=0) or when the attached data changed.
            if (i < g.Children.Count)
            {
                var oldGa = oldChild.GetAttached<GridAttached>();
                var ga = newChild.GetAttached<GridAttached>();
                if (ga is not null && (wasReplaced || ga != oldGa) && g.Children[i] is FrameworkElement ctrl)
                {
                    WinUI.Grid.SetRow(ctrl, ga.Row);
                    WinUI.Grid.SetColumn(ctrl, ga.Column);
                    if (ga.RowSpan > 1) WinUI.Grid.SetRowSpan(ctrl, ga.RowSpan);
                    if (ga.ColumnSpan > 1) WinUI.Grid.SetColumnSpan(ctrl, ga.ColumnSpan);
                }
            }
        }

        SetElementTag(g, n);
        ApplySetters(n.Setters, g);
        return null;
    }

    internal UIElement? UpdateFlex(FlexElement o, FlexElement n, Layout.FlexPanel panel, Action requestRerender)
    {
        panel.Direction = n.Direction;
        panel.JustifyContent = n.JustifyContent;
        panel.AlignItems = n.AlignItems;
        panel.AlignContent = n.AlignContent;
        panel.Wrap = n.Wrap;
        panel.ColumnGap = n.ColumnGap;
        panel.RowGap = n.RowGap;
        panel.FlexPadding = n.FlexPadding;

        ReconcileChildren(o.Children, n.Children, panel, requestRerender);

        // Re-apply flex attached properties — skip nulls/EmptyElements to stay
        // aligned with panel.Children (ChildReconciler filters those out).
        int panelIdx = 0;
        for (int i = 0; i < n.Children.Length && panelIdx < panel.Children.Count; i++)
        {
            if (n.Children[i] is null or EmptyElement) continue;
            ApplyFlexAttached(n.Children[i], panel.Children[panelIdx]);
            panelIdx++;
        }

        SetElementTag(panel, n);
        ApplySetters(n.Setters, panel);
        return null;
    }

    internal static void ApplyFlexAttached(Element child, Microsoft.UI.Xaml.UIElement ctrl)
    {
        var fa = child.GetAttached<FlexAttached>();
        // Always apply — reset to defaults when no FlexAttached, so stale values
        // from pool-rented or reconciler-reused controls are cleared.
        Layout.FlexPanel.SetGrow(ctrl, fa?.Grow ?? 0);
        Layout.FlexPanel.SetShrink(ctrl, fa?.Shrink ?? 1);
        if (fa is { Basis: { } basis }) Layout.FlexPanel.SetBasis(ctrl, basis);
        else ctrl.ClearValue(Layout.FlexPanel.BasisProperty);
        if (fa is { MinWidth: { } minWidth }) Layout.FlexPanel.SetMinWidth(ctrl, minWidth);
        else ctrl.ClearValue(Layout.FlexPanel.FlexMinWidthProperty);
        if (fa is { MinHeight: { } minHeight }) Layout.FlexPanel.SetMinHeight(ctrl, minHeight);
        else ctrl.ClearValue(Layout.FlexPanel.FlexMinHeightProperty);
        if (fa is { AlignSelf: { } alignSelf }) Layout.FlexPanel.SetAlignSelf(ctrl, alignSelf);
        else ctrl.ClearValue(Layout.FlexPanel.AlignSelfProperty);
        Layout.FlexPanel.SetPosition(ctrl, fa?.Position ?? Layout.FlexPositionType.Relative);
        if (fa is { Left: { } left }) Layout.FlexPanel.SetLeft(ctrl, left);
        else ctrl.ClearValue(Layout.FlexPanel.LeftProperty);
        if (fa is { Top: { } top }) Layout.FlexPanel.SetTop(ctrl, top);
        else ctrl.ClearValue(Layout.FlexPanel.TopProperty);
        if (fa is { Right: { } right }) Layout.FlexPanel.SetRight(ctrl, right);
        else ctrl.ClearValue(Layout.FlexPanel.RightProperty);
        if (fa is { Bottom: { } bottom }) Layout.FlexPanel.SetBottom(ctrl, bottom);
        else ctrl.ClearValue(Layout.FlexPanel.BottomProperty);
    }

    private UIElement? UpdateTreeView(TreeViewElement o, TreeViewElement n, WinUI.TreeView tv, Action requestRerender)
    {
        // If the node data is reference-equal (same arrays), skip entirely
        if (ReferenceEquals(o.Nodes, n.Nodes))
        {
            SetElementTag(tv, n);
            if (o.OnItemInvoked is null && n.OnItemInvoked is not null)
                tv.ItemInvoked += (s, args) =>
                {
                    var t = (WinUI.TreeView)s!;
                    if (args.InvokedItem is WinUI.TreeViewNode tvn
                        && tvn.Content is TreeViewNodeData nodeData)
                    {
                        (GetElementTag(t) as TreeViewElement)?.OnItemInvoked?.Invoke(nodeData);
                    }
                };
            if (o.OnExpanding is null && n.OnExpanding is not null)
                tv.Expanding += (s, args) =>
                {
                    var t = (WinUI.TreeView)s!;
                    if (args.Node.Content is TreeViewNodeData nodeData)
                        (GetElementTag(t) as TreeViewElement)?.OnExpanding?.Invoke(nodeData);
                };
            return null;
        }

        // Diff the node tree to minimize WinUI interop calls
        DiffTreeViewNodes(tv.RootNodes, o.Nodes, n.Nodes, requestRerender);

        tv.SelectionMode = n.SelectionMode;
        SetElementTag(tv, n);
        if (o.OnItemInvoked is null && n.OnItemInvoked is not null)
            tv.ItemInvoked += (s, args) =>
            {
                var t = (WinUI.TreeView)s!;
                if (args.InvokedItem is WinUI.TreeViewNode tvn
                    && tvn.Content is TreeViewNodeData nodeData)
                {
                    (GetElementTag(t) as TreeViewElement)?.OnItemInvoked?.Invoke(nodeData);
                }
            };
        if (o.OnExpanding is null && n.OnExpanding is not null)
            tv.Expanding += (s, args) =>
            {
                var t = (WinUI.TreeView)s!;
                if (args.Node.Content is TreeViewNodeData nodeData)
                    (GetElementTag(t) as TreeViewElement)?.OnExpanding?.Invoke(nodeData);
            };
        ApplySetters(n.Setters, tv);
        return null;
    }

    /// <summary>
    /// Recursively diff TreeViewNode lists, reusing existing nodes where Content matches.
    /// Only adds/removes/updates nodes that actually changed, minimizing COM interop calls.
    /// Also reconciles ContentElement changes on existing nodes.
    ///
    /// Algorithm: Snapshot existing live nodes into a Content→node map. Clear the live list,
    /// then rebuild it in new order — reusing matched nodes and creating fresh ones.
    /// </summary>
    private void DiffTreeViewNodes(
        IList<WinUI.TreeViewNode> liveNodes,
        TreeViewNodeData[] oldData,
        TreeViewNodeData[] newData,
        Action requestRerender)
    {
        // Snapshot: map old Content → (live node, old data index).
        // Use the old data array for indexing since liveNodes mirrors it 1:1.
        var liveByContent = new Dictionary<string, (WinUI.TreeViewNode Node, int OldIdx)>(oldData.Length);
        for (int i = 0; i < oldData.Length && i < liveNodes.Count; i++)
            liveByContent.TryAdd(oldData[i].Content, (liveNodes[i], i));

        // Detach all live nodes so we can re-insert in new order
        liveNodes.Clear();

        for (int i = 0; i < newData.Length; i++)
        {
            var nd = newData[i];

            if (liveByContent.Remove(nd.Content, out var match))
            {
                var liveNode = match.Node;
                var oldNodeData = oldData[match.OldIdx];

                if (liveNode.IsExpanded != nd.IsExpanded)
                    liveNode.IsExpanded = nd.IsExpanded;

                ReconcileTreeNodeContent(liveNode, oldNodeData, nd, requestRerender);

                // Diff children
                var oldChildren = oldNodeData.Children;
                var newChildren = nd.Children;

                if (!ReferenceEquals(oldChildren, newChildren))
                {
                    if (newChildren is null)
                        liveNode.Children.Clear();
                    else if (oldChildren is null)
                    {
                        liveNode.Children.Clear();
                        foreach (var child in newChildren)
                            liveNode.Children.Add(CreateTreeNode(child));
                    }
                    else
                        DiffTreeViewNodes(liveNode.Children, oldChildren, newChildren, requestRerender);
                }

                liveNodes.Add(liveNode);
            }
            else
            {
                // New node
                liveNodes.Add(CreateTreeNode(nd));
            }
        }
        // Unmatched old nodes are simply not re-added — they're dropped.
    }

    /// <summary>
    /// Reconciles ContentElement changes on a TreeViewNode.
    /// When ContentElement is used, node.Content holds a mounted UIElement.
    /// </summary>
    private void ReconcileTreeNodeContent(
        WinUI.TreeViewNode liveNode,
        TreeViewNodeData? oldData,
        TreeViewNodeData newData,
        Action requestRerender)
    {
        var oldContentEl = oldData?.ContentElement;
        var newContentEl = newData.ContentElement;

        if (newContentEl is null && oldContentEl is null) return; // Both text-only, no change needed

        if (newContentEl is not null && oldContentEl is not null
            && liveNode.Content is UIElement existingCtrl
            && CanUpdate(oldContentEl, newContentEl))
        {
            // Reconcile in place
            var replacement = Update(oldContentEl, newContentEl, existingCtrl, requestRerender);
            if (replacement is not null && !ReferenceEquals(liveNode.Content, replacement))
                liveNode.Content = replacement;
        }
        else if (newContentEl is not null)
        {
            // Mount new content element
            if (liveNode.Content is UIElement oldCtrl)
                Unmount(oldCtrl);
            liveNode.Content = Mount(newContentEl, requestRerender);
        }
        else
        {
            // ContentElement removed, revert to data
            if (liveNode.Content is UIElement oldCtrl2)
                Unmount(oldCtrl2);
            liveNode.Content = newData;
        }
    }

    private UIElement? UpdateRectangle(RectangleElement n, WinShapes.Rectangle r)
    {
        if (n.Fill is not null) r.Fill = n.Fill;
        if (n.Stroke is not null) r.Stroke = n.Stroke;
        r.StrokeThickness = n.StrokeThickness;
        r.RadiusX = n.RadiusX;
        r.RadiusY = n.RadiusY;
        ApplySetters(n.Setters, r);
        return null;
    }

    private UIElement? UpdateEllipse(EllipseElement n, WinShapes.Ellipse e)
    {
        if (n.Fill is not null) e.Fill = n.Fill;
        if (n.Stroke is not null) e.Stroke = n.Stroke;
        e.StrokeThickness = n.StrokeThickness;
        ApplySetters(n.Setters, e);
        return null;
    }

    private UIElement? UpdateLine(LineElement n, WinShapes.Line l)
    {
        l.X1 = n.X1; l.Y1 = n.Y1;
        l.X2 = n.X2; l.Y2 = n.Y2;
        if (n.Stroke is not null) l.Stroke = n.Stroke;
        l.StrokeThickness = n.StrokeThickness;
        ApplySetters(n.Setters, l);
        return null;
    }

    private UIElement? UpdatePath(PathElement o, PathElement n, WinShapes.Path p)
    {
        // Skip expensive COM Geometry property set when the path data string hasn't changed.
        // PathDataParser.Parse creates a new PathGeometry COM object every call, so
        // reference equality is never true — compare the source string instead.
        bool pathChanged = n.PathDataString is null
            ? n.Data is not null
            : !string.Equals(n.PathDataString, o.PathDataString, StringComparison.Ordinal);
        if (pathChanged && n.Data is not null) p.Data = n.Data;

        if (n.Fill is not null) p.Fill = n.Fill;
        if (n.Stroke is not null) p.Stroke = n.Stroke;
        p.StrokeThickness = n.StrokeThickness;
        if (n.StrokeDashArray is not null) p.StrokeDashArray = n.StrokeDashArray;
        if (n.RenderTransform is not null) p.RenderTransform = n.RenderTransform;
        if (p.StrokeStartLineCap != n.StrokeStartLineCap) p.StrokeStartLineCap = n.StrokeStartLineCap;
        if (p.StrokeEndLineCap != n.StrokeEndLineCap) p.StrokeEndLineCap = n.StrokeEndLineCap;
        if (p.StrokeLineJoin != n.StrokeLineJoin) p.StrokeLineJoin = n.StrokeLineJoin;
        if (p.StrokeMiterLimit != n.StrokeMiterLimit) p.StrokeMiterLimit = n.StrokeMiterLimit;
        if (p.StrokeDashCap != n.StrokeDashCap) p.StrokeDashCap = n.StrokeDashCap;
        if (p.StrokeDashOffset != n.StrokeDashOffset) p.StrokeDashOffset = n.StrokeDashOffset;
        // FillRule lives on the PathGeometry, not Shapes.Path — propagate when we own one.
        if (p.Data is Microsoft.UI.Xaml.Media.PathGeometry npg && npg.FillRule != n.FillRule)
            npg.FillRule = n.FillRule;
        ApplySetters(n.Setters, p);
        return null;
    }



    private UIElement? UpdatePipsPager(PipsPagerElement o, PipsPagerElement n, WinUI.PipsPager pp)
    {
        pp.NumberOfPages = n.NumberOfPages;
        pp.SelectedPageIndex = n.SelectedPageIndex;
        if (pp.WrapMode != n.WrapMode) pp.WrapMode = n.WrapMode;
        if (pp.MaxVisiblePips != n.MaxVisiblePips) pp.MaxVisiblePips = n.MaxVisiblePips;
        if (pp.PreviousButtonVisibility != n.PreviousButtonVisibility) pp.PreviousButtonVisibility = n.PreviousButtonVisibility;
        if (pp.NextButtonVisibility != n.NextButtonVisibility) pp.NextButtonVisibility = n.NextButtonVisibility;
        SetElementTag(pp, n);
        if (o.OnSelectedPageIndexChanged is null && n.OnSelectedPageIndexChanged is not null)
            pp.SelectedIndexChanged += (s, _) =>
            {
                var p = (WinUI.PipsPager)s!;
                (GetElementTag(p) as PipsPagerElement)?.OnSelectedPageIndexChanged?.Invoke(p.SelectedPageIndex);
            };
        ApplySetters(n.Setters, pp);
        return null;
    }



    private static void SyncSelectedDates(WinUI.CalendarView cv, IReadOnlyList<DateTimeOffset> desired)
    {
        // Diff current vs desired and apply the delta. Each Add/Remove on the
        // SelectedDates collection raises SelectedDatesChanged, so suppress one
        // token per mutation to keep OnSelectedDatesChanged free of echoes.
        var current = cv.SelectedDates;
        var desiredSet = new HashSet<DateTimeOffset>(desired);

        for (int i = current.Count - 1; i >= 0; i--)
        {
            if (!desiredSet.Contains(current[i]))
            {
                ChangeEchoSuppressor.BeginSuppress(cv);
                current.RemoveAt(i);
            }
        }

        var currentSet = new HashSet<DateTimeOffset>(current);
        foreach (var d in desired)
        {
            if (currentSet.Add(d))
            {
                ChangeEchoSuppressor.BeginSuppress(cv);
                current.Add(d);
            }
        }
    }

    private UIElement? UpdateAnimatedIcon(AnimatedIconElement n, WinUI.AnimatedIcon ai)
    {
        if (n.Source is Microsoft.UI.Xaml.Controls.IAnimatedVisualSource2 src)
            ai.Source = src;
        if (n.FallbackIconSource is not null) ai.FallbackIconSource = n.FallbackIconSource;
        ApplySetters(n.Setters, ai);
        return null;
    }



    private UIElement? UpdateFrame(FrameElement n, WinUI.Frame f)
    {
        // Frame navigation is inherently imperative — only refresh the
        // element tag so event trampolines see the latest handlers, then
        // apply setters.
        SetElementTag(f, n);
        ApplySetters(n.Setters, f);
        return null;
    }

    private UIElement? UpdateFormField(
        FormFieldElement oldFf, FormFieldElement newFf,
        WinUI.StackPanel panel, Action requestRerender)
    {
        // Fixed 3-child layout: [0] label, [1] content, [2] description/error
        if (panel.Children.Count != 3)
            return Mount(newFf, requestRerender);

        var fieldName = FormFieldHelpers.ResolveFieldName(newFf.FieldName, newFf.Content);

        // Auto-validate
        var attached = newFf.Content.GetAttached<ValidationAttached>();
        var valCtx = _contextScope.Read(ValidationContexts.Current);
        if (valCtx is not null && attached is not null && attached.Validators.Length > 0)
        {
            ValidationReconciler.ValidateAttached(valCtx, attached, attached.Value);
        }

        // [0] Update label
        if (panel.Children[0] is TextBlock labelTb)
        {
            var displayLabel = FormFieldHelpers.GetDisplayLabel(newFf.Label, newFf.Required);
            labelTb.Text = displayLabel;
            labelTb.Visibility = displayLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // [1] Patch content in-place (preserves caret position and focus)
        var existingContent = panel.Children[1];
        if (CanUpdate(oldFf.Content, newFf.Content))
        {
            var replacement = Update(oldFf.Content, newFf.Content, existingContent, requestRerender);
            if (replacement is not null)
            {
                // WinUI indexer assignment doesn't fully disconnect the old element's
                // parent state — use RemoveAt+Insert (see ChildCollection.Replace).
                Unmount(existingContent);
                panel.Children.RemoveAt(1);
                panel.Children.Insert(1, replacement);
                existingContent = replacement;
            }
        }
        else
        {
            // Content element type changed — must remount
            Unmount(existingContent);
            panel.Children.RemoveAt(1);
            var newContent = Mount(newFf.Content, requestRerender)
                ?? new WinUI.StackPanel { Visibility = Visibility.Collapsed };
            panel.Children.Insert(1, newContent);
            existingContent = newContent;
        }

        ApplyFormFieldAutomation(existingContent, newFf.Label);
        ApplyFormFieldErrorStyling(existingContent, valCtx, fieldName, newFf.ShowWhen);

        // [2] Update description/error text
        if (panel.Children[2] is TextBlock descTb)
        {
            ApplyFormFieldDescription(descTb, valCtx, fieldName, newFf.Description, newFf.ShowWhen);
        }

        SetElementTag(panel, newFf);
        return null; // patched in-place
    }

    private UIElement? UpdateValidationVisualizer(
        ValidationVisualizerElement oldVv, ValidationVisualizerElement newVv,
        WinUI.StackPanel panel, Action requestRerender)
    {
        // The visualizer layout varies by style and message state, so a full in-place
        // patch is complex. However, we can at least reconcile the content child when
        // styles match and the content element is updatable.
        if (oldVv.Style != newVv.Style)
            return Mount(newVv, requestRerender);

        // Find the content child — it's the form control, not the error display chrome.
        // In MountValidationVisualizer, content is added after style-specific elements,
        // except for Inline where error text comes after content.
        // For simplicity and correctness, remount the visualizer but reconcile the
        // content subtree to preserve control state.
        return Mount(newVv, requestRerender);
    }

    private UIElement? UpdateValidationRule(ValidationRuleElement rule)
    {
        var valCtx = _contextScope.Read(ValidationContexts.Current);
        if (valCtx is not null)
            rule.Evaluate(valCtx);
        return null; // keep existing collapsed placeholder
    }

    private UIElement? UpdateErrorBoundary(
        ErrorBoundaryElement oldEb, ErrorBoundaryElement newEb,
        UIElement control, Action requestRerender)
    {
        if (!_errorBoundaryNodes.TryGetValue(control, out var node))
            return Mount(newEb, requestRerender);

        var wrapper = (Border)control;
        var existingChild = wrapper.Child;

        // Always retry the child on re-render (error recovery).
        Element newRendered;
        Exception? caughtEx = null;

        _errorBoundaryDepth++;
        try
        {
            newRendered = newEb.Child;
            var newControl = Reconcile(node.RenderedElement, newEb.Child, existingChild, requestRerender);
            if (newControl != existingChild)
                wrapper.Child = newControl;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ErrorBoundary caught render error during update");
            caughtEx = ex;
            if (existingChild is not null)
                Unmount(existingChild);
            newRendered = newEb.Fallback(ex);
            wrapper.Child = Mount(newRendered, requestRerender);
        }
        finally
        {
            _errorBoundaryDepth--;
        }

        node.ChildElement = newEb.Child;
        node.RenderedElement = newRendered;
        node.CaughtException = caughtEx;
        node.Fallback = newEb.Fallback;

        return null;
    }

    private UIElement? UpdateComponent(Element oldEl, Element newEl, UIElement control, Action requestRerender)
    {
        ReconcileComponent(oldEl, newEl, control, requestRerender);
        return null;
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "…";

    // ════════════════════════════════════════════════════════════════════
    //  XamlHostElement / XamlPageElement — built-in XAML interop
    // ════════════════════════════════════════════════════════════════════



    // ════════════════════════════════════════════════════════════════════
    //  SemanticElement — composite accessibility wrapper
    // ════════════════════════════════════════════════════════════════════

    private UIElement? UpdateSemantic(
        SemanticElement oldSem, SemanticElement newSem,
        Accessibility.SemanticPanel panel, Action requestRerender)
    {
        // Update semantic properties if changed
        var s = newSem.Semantics;
        if (oldSem.Semantics.Role != s.Role)
            panel.SemanticRole = s.Role;
        if (oldSem.Semantics.Value != s.Value)
            panel.SemanticValue = s.Value;
        if (oldSem.Semantics.RangeMin != s.RangeMin)
            panel.RangeMinimum = s.RangeMin ?? 0.0;
        if (oldSem.Semantics.RangeMax != s.RangeMax)
            panel.RangeMaximum = s.RangeMax ?? 0.0;
        if (oldSem.Semantics.RangeValue != s.RangeValue)
            panel.RangeValue = s.RangeValue ?? 0.0;
        if (oldSem.Semantics.IsReadOnly != s.IsReadOnly)
            panel.IsReadOnly = s.IsReadOnly;

        // Reconcile the child element
        var existingChild = panel.Children.Count > 0 ? panel.Children[0] : null;
        var newChild = Reconcile(oldSem.Child, newSem.Child, existingChild, requestRerender);
        if (newChild != existingChild)
        {
            panel.Children.Clear();
            if (newChild is not null)
                panel.Children.Add(newChild);
        }

        SetElementTag(panel, newSem);
        return null; // updated in place
    }

    internal void UpdateMenuFlyoutItems(
        global::System.Collections.Generic.IList<WinUI.MenuFlyoutItemBase> target,
        MenuFlyoutItemBase[] oldSource,
        MenuFlyoutItemBase[] newSource)
    {
        int oldCount = oldSource.Length;
        int newCount = newSource.Length;
        int shared = Math.Min(oldCount, newCount);

        for (int i = 0; i < shared; i++)
        {
            switch (newSource[i])
            {
                case MenuFlyoutItemData mfi when target[i] is WinUI.MenuFlyoutItem existing:
                    existing.Text = mfi.Text;
                    existing.IsEnabled = mfi.IsEnabled;
                    existing.Icon = ResolveIcon(mfi.IconElement, mfi.Icon);
                    if (mfi.AccessKey is not null) existing.AccessKey = mfi.AccessKey;
                    if (mfi.Description is not null)
                    {
                        WinUI.ToolTipService.SetToolTip(existing, mfi.Description);
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(existing, mfi.Description);
                    }
                    existing.Tag = mfi;
                    break;

                case ToggleMenuFlyoutItemData toggle when target[i] is WinUI.ToggleMenuFlyoutItem toggleItem:
                    toggleItem.Text = toggle.Text;
                    toggleItem.IsChecked = toggle.IsChecked;
                    toggleItem.Icon = ResolveIcon(toggle.IconElement, toggle.Icon);
                    toggleItem.Tag = toggle;
                    break;

                case RadioMenuFlyoutItemData radio when target[i] is WinUI.RadioMenuFlyoutItem radioItem:
                    radioItem.Text = radio.Text;
                    radioItem.IsChecked = radio.IsChecked;
                    radioItem.Tag = radio;
                    break;

                case MenuFlyoutSeparatorData when target[i] is WinUI.MenuFlyoutSeparator:
                    break; // nothing to update

                case MenuFlyoutSubItemData sub when target[i] is WinUI.MenuFlyoutSubItem subItem:
                    subItem.Text = sub.Text;
                    subItem.Icon = ResolveIcon(sub.IconElement, sub.Icon);
                    // Recursively patch sub-items
                    var oldSub = oldSource[i] is MenuFlyoutSubItemData oldSubData ? oldSubData.Items : [];
                    UpdateMenuFlyoutItems(subItem.Items, oldSub, sub.Items);
                    break;

                default:
                    // Type mismatch — replace the item
                    target[i] = CreateMenuFlyoutItem(newSource[i]);
                    break;
            }
        }

        // Remove excess
        for (int i = oldCount - 1; i >= shared; i--)
            target.RemoveAt(i);

        // Add new
        for (int i = shared; i < newCount; i++)
            target.Add(CreateMenuFlyoutItem(newSource[i]));
    }

    internal static void UpdateAppBarItems(
        global::System.Collections.Generic.IList<WinUI.ICommandBarElement> target,
        AppBarItemBase[]? source)
    {
        int newCount = source?.Length ?? 0;
        int oldCount = target.Count;

        // Update shared range (only update if types match, otherwise replace)
        int shared = Math.Min(oldCount, newCount);
        for (int i = 0; i < shared; i++)
        {
            if (source is null) continue;
            switch (source[i])
            {
                case AppBarButtonData cmd when target[i] is WinUI.AppBarButton abb:
                    abb.Label = cmd.Label;
                    abb.IsEnabled = cmd.IsEnabled;
                    abb.Icon = ResolveIcon(cmd.IconElement, cmd.Icon);
                    if (cmd.AccessKey is not null) abb.AccessKey = cmd.AccessKey;
                    if (cmd.Description is not null)
                    {
                        WinUI.ToolTipService.SetToolTip(abb, cmd.Description);
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(abb, cmd.Description);
                    }
                    abb.Tag = cmd;
                    break;
                case AppBarToggleButtonData toggle when target[i] is WinUI.AppBarToggleButton atb:
                    atb.Label = toggle.Label;
                    atb.IsChecked = toggle.IsChecked;
                    if (toggle.Icon is not null) atb.Icon = new WinUI.SymbolIcon(ParseSymbol(toggle.Icon));
                    atb.Tag = toggle;
                    break;
                case AppBarSeparatorData when target[i] is WinUI.AppBarSeparator:
                    break; // nothing to update
                default:
                    // Type mismatch — replace
                    target[i] = CreateAppBarItem(source[i]);
                    break;
            }
        }

        // Remove excess
        for (int i = oldCount - 1; i >= shared; i--)
            target.RemoveAt(i);

        // Add new
        if (source is not null)
            for (int i = shared; i < newCount; i++)
                target.Add(CreateAppBarItem(source[i]));
    }
}
