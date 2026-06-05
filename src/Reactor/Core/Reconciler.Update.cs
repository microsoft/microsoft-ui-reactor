using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Reactor.Hosting;
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

        // Issue #522 — when the element transitions away from ThemeBindings,
        // drop the synthesized themed Style we wrote on the previous render.
        // Without this, e.g. the red Foreground from .Foreground(ThemeRef(
        // "SystemFillColorCriticalBrush")) on an AsyncValue Error arm bleeds
        // into the next Loading / Data arm on the recycled control.
        //
        // Ownership is tracked via an attached DP storing the Style instance
        // we wrote (see ReactorAppliedThemeStyleProperty); we deliberately
        // avoid ConditionalWeakTable per-FrameworkElement tracking (fragile
        // under RCW churn) and we avoid forcing a fresh Mount (would
        // needlessly lose subtree state for container elements like
        // .Background(ThemeRef(...)) on a VStack).
        if (oldEl.ThemeBindings is not null && newEl.ThemeBindings is null
            && control is FrameworkElement clearThFe)
        {
            ClearThemeBindings(clearThFe);
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

        // Spec 048 §8 — four-arm dispatch precedence (matches Mount):
        //   (1) per-host `_v1Handlers`, (2) per-host `_typeRegistry`,
        //   (3) global `ControlRegistry`, (4) composition-primitive switch.
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
        else if (TryResolveFromControlRegistry(newEl.GetType(), out v1Entry))
        {
            var v1Result = v1Entry.Update(oldEl, newEl, control, requestRerender, this);
            result = ReferenceEquals(v1Result, control) ? null : v1Result;
        }
        else
        {
        result = (oldEl, newEl, control) switch
        {
            (ErrorBoundaryElement oldEb, ErrorBoundaryElement newEb, Border)
                => UpdateErrorBoundary(oldEb, newEb, control, requestRerender),
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

    private Microsoft.UI.Xaml.Documents.Inline MountInline(RichTextInline inline, Action requestRerender)
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
                var hl = new Microsoft.UI.Xaml.Documents.Hyperlink();
                if (link.OnClick is not null)
                {
                    // Issue #479 click mode: fire delegate, suppress platform
                    // navigation by leaving NavigateUri unset.
                    s_hyperlinkClickActions.AddOrUpdate(hl, link.OnClick);
                    hl.Click += OnHyperlinkClick;
                }
                else
                {
                    var l = link.NavigateUri ?? new Uri("about:blank");
                    if (l.ToString().Length < 1) l = new Uri("about:blank");
                    try { hl.NavigateUri = l; } catch { hl.NavigateUri = new Uri("about:blank"); }
                }
                hl.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = link.Text ?? ""});
                return hl;
            case RichTextLineBreak:
                return new Microsoft.UI.Xaml.Documents.LineBreak();
            case RichTextInlineUIContainer iuc:
                return MountInlineUIContainer(iuc, requestRerender);
            default:
                return new Microsoft.UI.Xaml.Documents.Run { Text = "" };
        }
    }

    /// <summary>
    /// Issue #480 — mount a <see cref="RichTextInlineUIContainer"/> as a real
    /// WinUI <see cref="Microsoft.UI.Xaml.Documents.InlineUIContainer"/>.
    /// Route A: <c>Child</c> is mounted through the reconciler so descendant
    /// hooks (UseState/UseEffect) and event wiring run normally. Route B:
    /// <c>Factory</c> produces a raw native <see cref="FrameworkElement"/>
    /// scoped to this rebuild. The mounted <c>UIElement</c> reference is
    /// stashed on the container so the surrounding RichTextBlock's unmount /
    /// rebuild paths can walk and tear it down before <c>Blocks.Clear()</c>.
    /// </summary>
    private Microsoft.UI.Xaml.Documents.InlineUIContainer MountInlineUIContainer(
        RichTextInlineUIContainer iuc, Action requestRerender)
    {
        var container = new Microsoft.UI.Xaml.Documents.InlineUIContainer();
        UIElement? mounted = null;
        if (iuc.Child is not null)
        {
            mounted = Mount(iuc.Child, requestRerender);
        }
        else if (iuc.Factory is not null)
        {
            try { mounted = iuc.Factory(); }
            catch { mounted = null; }
        }
        if (mounted is not null)
        {
            container.Child = mounted;
            // Mark the mounted control so RichTextBlock teardown knows whether
            // to dispatch the reactor unmount path (Route A) or just drop the
            // reference (Route B = factory). RichTextBlock has no Reactor
            // element of its own per inline UI, so we encode the route here.
            if (iuc.Child is not null && mounted is FrameworkElement childFe)
                childFe.SetValue(s_inlineUIRouteAProperty, true);
        }
        return container;
    }

    // Attached DP used by RichTextBlock teardown / rebuild to distinguish
    // Reactor-mounted inline UI children (Route A — needs UnmountChild) from
    // raw native factory results (Route B — just drop the reference; GC
    // reclaims). Booleans on a DP avoid an extra CWT just for this.
    private static readonly DependencyProperty s_inlineUIRouteAProperty =
        DependencyProperty.RegisterAttached(
            "ReactorInlineUIRouteA",
            typeof(bool),
            typeof(Reconciler),
            new PropertyMetadata(false));

    /// <summary>
    /// Walks an existing <see cref="WinUI.RichTextBlock"/>'s blocks and
    /// unmounts any Reactor-managed (Route A) inline UI children before the
    /// caller clears <c>Blocks</c>. No-op for blocks that contain only
    /// regular runs / hyperlinks / line breaks, or for Route B (native
    /// factory) inline UI children.
    /// </summary>
    internal void UnmountInlineUIChildren(WinUI.RichTextBlock rtb)
    {
        foreach (var block in rtb.Blocks)
        {
            if (block is Microsoft.UI.Xaml.Documents.Paragraph para)
                UnmountInlineUIChildrenInInlines(para.Inlines);
        }
    }

    private void UnmountInlineUIChildrenInInlines(
        Microsoft.UI.Xaml.Documents.InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Microsoft.UI.Xaml.Documents.InlineUIContainer iuc:
                    if (iuc.Child is FrameworkElement childFe
                        && (bool)childFe.GetValue(s_inlineUIRouteAProperty))
                    {
                        UnmountChild(iuc.Child);
                    }
                    iuc.Child = null;
                    break;
                case Microsoft.UI.Xaml.Documents.Span span:
                    UnmountInlineUIChildrenInInlines(span.Inlines);
                    break;
            }
        }
    }

    private Microsoft.UI.Xaml.Documents.Paragraph MountParagraph(RichTextParagraph para, Action requestRerender)
    {
        var p = new Microsoft.UI.Xaml.Documents.Paragraph();
        foreach (var inline in para.Inlines)
            p.Inlines.Add(MountInline(inline, requestRerender));
        return p;
    }

    // Spec 047 §14 Phase 3-final Batch B — widened to internal so the
    // legacy MountRichTextBlock arm AND RichTextBlockDescriptor's bridged
    // set lambda call the same rebuild path. Issue #480 widened the
    // signature to take a rerender callback so InlineUIContainer Route A
    // children can be mounted through the reconciler.
    internal void RebuildRichTextBlocks(RichTextBlockElement n, WinUI.RichTextBlock rtb, Action requestRerender)
    {
        // Tear down any Route A inline UI children before clearing the
        // block collection — otherwise their Reactor state (hooks, event
        // trampolines, pooled descendants) leaks when WinUI silently
        // drops the InlineUIContainer references.
        UnmountInlineUIChildren(rtb);
        rtb.Blocks.Clear();
        if (n.Paragraphs is not null)
        {
            foreach (var para in n.Paragraphs)
                rtb.Blocks.Add(MountParagraph(para, requestRerender));
        }
        else
        {
            var p = new Microsoft.UI.Xaml.Documents.Paragraph();
            p.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = n.Text });
            rtb.Blocks.Add(p);
        }
    }

    /// <summary>
    /// Issue #480 follow-up — incremental update path for RichTextBlock that
    /// preserves WinUI document identity AND embedded Reactor child state
    /// (e.g. Slider drag position, Button click identity) across re-renders
    /// where the parent paragraph array reference changes but the document
    /// shape is the same. Falls back to <see cref="RebuildRichTextBlocks"/>
    /// when the shape changed.
    ///
    /// <para><b>Why this exists:</b> the previous always-rebuild path tore
    /// down + remounted every <see cref="RichTextInlineUIContainer"/> child
    /// on every state change of the owning component. That made inline
    /// interactive controls effectively unusable (drag canceled, focus
    /// lost) and defeated the reconcile-highlight overlay (the whole RTB
    /// flashed instead of just the changed run).</para>
    /// </summary>
    internal void UpdateRichTextBlocks(
        WinUI.RichTextBlock rtb,
        RichTextBlockElement prev,
        RichTextBlockElement next,
        Action requestRerender)
    {
        if (TryIncrementalUpdateRichTextBlocks(rtb, prev, next, requestRerender))
            return;
        RebuildRichTextBlocks(next, rtb, requestRerender);
    }

    private bool TryIncrementalUpdateRichTextBlocks(
        WinUI.RichTextBlock rtb,
        RichTextBlockElement prev,
        RichTextBlockElement next,
        Action requestRerender)
    {
        // Case A — both .Paragraphs null: text-only fallback path.
        if (prev.Paragraphs is null && next.Paragraphs is null)
        {
            if (rtb.Blocks.Count != 1) return false;
            if (rtb.Blocks[0] is not Microsoft.UI.Xaml.Documents.Paragraph p1) return false;
            if (p1.Inlines.Count != 1) return false;
            if (p1.Inlines[0] is not Microsoft.UI.Xaml.Documents.Run r1) return false;
            if (string.Equals(prev.Text, next.Text, global::System.StringComparison.Ordinal))
                return true;
            r1.Text = next.Text ?? string.Empty;
            MarkRichTextBlockModified(rtb);
            return true;
        }

        // Mode mismatch (text → paragraphs or vice-versa) → full rebuild.
        if (prev.Paragraphs is null || next.Paragraphs is null) return false;

        var prevPs = prev.Paragraphs;
        var nextPs = next.Paragraphs;
        if (prevPs.Length != nextPs.Length) return false;
        if (rtb.Blocks.Count != prevPs.Length) return false;

        // Preflight pass — validate the entire tree is incrementally
        // updatable BEFORE mutating anything. Avoids partial Reactor child
        // update churn (running component updates, scheduling effects,
        // etc.) only to discover later we have to tear everything down.
        for (int pi = 0; pi < nextPs.Length; pi++)
        {
            if (rtb.Blocks[pi] is not Microsoft.UI.Xaml.Documents.Paragraph winPara)
                return false;
            var prevInlines = prevPs[pi].Inlines;
            var nextInlines = nextPs[pi].Inlines;
            if (prevInlines.Length != nextInlines.Length) return false;
            if (winPara.Inlines.Count != prevInlines.Length) return false;
            for (int ii = 0; ii < nextInlines.Length; ii++)
            {
                if (!CanUpdateInlineInPlace(prevInlines[ii], nextInlines[ii], winPara.Inlines[ii]))
                    return false;
            }
        }

        // Mutation pass.
        bool anyMutation = false;
        for (int pi = 0; pi < nextPs.Length; pi++)
        {
            var prevPara = prevPs[pi];
            var nextPara = nextPs[pi];
            var winPara = (Microsoft.UI.Xaml.Documents.Paragraph)rtb.Blocks[pi];
            if (ReferenceEquals(prevPara, nextPara))
            {
                // Paragraph object is unchanged at the structural level, but
                // a Route A InlineUIContainer inside it may still own an
                // inner Reactor Component whose state has updated since
                // the last reconcile. Walk just those — every other inline
                // (Run / Hyperlink / LineBreak) is a pure value type and has
                // no out-of-band update path.
                for (int ii = 0; ii < nextPara.Inlines.Length; ii++)
                {
                    if (nextPara.Inlines[ii] is RichTextInlineUIContainer rinl
                        && rinl.Child is not null
                        && winPara.Inlines[ii] is Microsoft.UI.Xaml.Documents.InlineUIContainer wc)
                    {
                        if (UpdateInlineUIContainerInPlace(rinl, rinl, wc, requestRerender))
                            anyMutation = true;
                    }
                }
                continue;
            }
            for (int ii = 0; ii < nextPara.Inlines.Length; ii++)
            {
                var prevInline = prevPara.Inlines[ii];
                var nextInline = nextPara.Inlines[ii];
                // Always recurse for Route A InlineUIContainer — the embedded
                // Reactor child may have its own state that wants to render
                // even when the parent inline record is structurally equal.
                if (prevInline is not RichTextInlineUIContainer && prevInline.Equals(nextInline))
                    continue;
                if (UpdateInlineInPlace(prevInline, nextInline, winPara.Inlines[ii], requestRerender))
                    anyMutation = true;
            }
        }

        if (anyMutation)
            MarkRichTextBlockModified(rtb);
        return true;
    }

    private static bool CanUpdateInlineInPlace(
        RichTextInline prev,
        RichTextInline next,
        Microsoft.UI.Xaml.Documents.Inline existing)
    {
        return (prev, next, existing) switch
        {
            (RichTextRun, RichTextRun, Microsoft.UI.Xaml.Documents.Run) => true,
            (RichTextHyperlink, RichTextHyperlink, Microsoft.UI.Xaml.Documents.Hyperlink hl)
                => hl.Inlines.Count == 1 && hl.Inlines[0] is Microsoft.UI.Xaml.Documents.Run,
            (RichTextLineBreak, RichTextLineBreak, Microsoft.UI.Xaml.Documents.LineBreak) => true,
            (RichTextInlineUIContainer p, RichTextInlineUIContainer n, Microsoft.UI.Xaml.Documents.InlineUIContainer)
                => CanUpdateInlineUIContainerInPlace(p, n),
            _ => false,
        };
    }

    private static bool CanUpdateInlineUIContainerInPlace(
        RichTextInlineUIContainer prev,
        RichTextInlineUIContainer next)
    {
        bool prevRouteA = prev.Child is not null;
        bool nextRouteA = next.Child is not null;
        // Route swap (A↔B) or null swap → full rebuild for clarity.
        if (prevRouteA != nextRouteA) return false;
        if (!prevRouteA)
        {
            // Both Route B: factory swap or null → handled in mutation pass
            // (just re-invoke). Both null is a no-op.
            return true;
        }
        // Both Route A. The reconciler's ReconcileV1Child handles either
        // structural Update (CanUpdate true) or unmount-and-mount (CanUpdate
        // false), so we can always claim incremental success here.
        return true;
    }

    private bool UpdateInlineInPlace(
        RichTextInline prev,
        RichTextInline next,
        Microsoft.UI.Xaml.Documents.Inline existing,
        Action requestRerender)
    {
        switch (prev, next, existing)
        {
            case (RichTextRun p, RichTextRun n, Microsoft.UI.Xaml.Documents.Run wr):
                return UpdateRunInPlace(p, n, wr);
            case (RichTextHyperlink p, RichTextHyperlink n, Microsoft.UI.Xaml.Documents.Hyperlink wh):
                return UpdateHyperlinkInPlace(p, n, wh);
            case (RichTextLineBreak, RichTextLineBreak, _):
                return false;
            case (RichTextInlineUIContainer p, RichTextInlineUIContainer n,
                  Microsoft.UI.Xaml.Documents.InlineUIContainer wc):
                return UpdateInlineUIContainerInPlace(p, n, wc, requestRerender);
            default:
                return false; // unreachable per preflight
        }
    }

    private static bool UpdateRunInPlace(RichTextRun prev, RichTextRun next, Microsoft.UI.Xaml.Documents.Run wr)
    {
        bool any = false;
        if (!string.Equals(prev.Text, next.Text, global::System.StringComparison.Ordinal))
        {
            wr.Text = next.Text ?? string.Empty;
            any = true;
        }
        if (prev.IsBold != next.IsBold)
        {
            wr.FontWeight = next.IsBold
                ? Microsoft.UI.Text.FontWeights.Bold
                : Microsoft.UI.Text.FontWeights.Normal;
            any = true;
        }
        if (prev.IsItalic != next.IsItalic)
        {
            wr.FontStyle = next.IsItalic
                ? global::Windows.UI.Text.FontStyle.Italic
                : global::Windows.UI.Text.FontStyle.Normal;
            any = true;
        }
        if (prev.IsStrikethrough != next.IsStrikethrough)
        {
            wr.TextDecorations = next.IsStrikethrough
                ? global::Windows.UI.Text.TextDecorations.Strikethrough
                : global::Windows.UI.Text.TextDecorations.None;
            any = true;
        }
        if (prev.FontSize != next.FontSize)
        {
            if (next.FontSize.HasValue) wr.FontSize = next.FontSize.Value;
            else wr.ClearValue(Microsoft.UI.Xaml.Documents.TextElement.FontSizeProperty);
            any = true;
        }
        if (!string.Equals(prev.FontFamily, next.FontFamily, global::System.StringComparison.Ordinal))
        {
            if (next.FontFamily is not null)
                wr.FontFamily = WinRTCache.GetFontFamily(next.FontFamily);
            else
                wr.ClearValue(Microsoft.UI.Xaml.Documents.TextElement.FontFamilyProperty);
            any = true;
        }
        if (!ReferenceEquals(prev.Foreground, next.Foreground))
        {
            if (next.Foreground is not null) wr.Foreground = next.Foreground;
            else wr.ClearValue(Microsoft.UI.Xaml.Documents.TextElement.ForegroundProperty);
            any = true;
        }
        return any;
    }

    private static bool UpdateHyperlinkInPlace(
        RichTextHyperlink prev,
        RichTextHyperlink next,
        Microsoft.UI.Xaml.Documents.Hyperlink wh)
    {
        bool any = false;
        // Issue #479 — handle OnClick attach/detach + NavigateUri transitions.
        bool prevControlled = prev.OnClick is not null;
        bool nextControlled = next.OnClick is not null;
        if (nextControlled)
        {
            // AddOrUpdate keeps the existing static event subscription pointing
            // at the latest delegate without detach/attach churn — important
            // because authors typically pass a fresh closure per render.
            s_hyperlinkClickActions.AddOrUpdate(wh, next.OnClick!);
            if (!prevControlled)
            {
                wh.Click += OnHyperlinkClick;
                // Clear any URI from navigate mode so the platform does not
                // also fire its own navigation on the same click.
                wh.ClearValue(Microsoft.UI.Xaml.Documents.Hyperlink.NavigateUriProperty);
                any = true;
            }
        }
        else
        {
            if (prevControlled)
            {
                wh.Click -= OnHyperlinkClick;
                s_hyperlinkClickActions.Remove(wh);
                any = true;
            }
            if (prevControlled || prev.NavigateUri != next.NavigateUri)
            {
                // Mirror MountInline's normalization: fall back to about:blank
                // on null/empty/invalid URIs. The fallback assignment is not
                // wrapped — `about:blank` always parses and a `NavigateUri =`
                // assignment for it should never throw; if it ever did, that
                // is a real bug we want to surface, not silently swallow.
                var uri = next.NavigateUri ?? new Uri("about:blank");
                if (uri.ToString().Length < 1) uri = new Uri("about:blank");
                try { wh.NavigateUri = uri; }
                catch { wh.NavigateUri = new Uri("about:blank"); }
                any = true;
            }
        }
        if (!string.Equals(prev.Text, next.Text, global::System.StringComparison.Ordinal))
        {
            if (wh.Inlines.Count > 0
                && wh.Inlines[0] is Microsoft.UI.Xaml.Documents.Run innerRun)
            {
                innerRun.Text = next.Text ?? string.Empty;
                any = true;
            }
        }
        return any;
    }

    // Issue #479 — backing storage for the static Click handler. CWT keys are
    // weak references so entries die when the WinUI Hyperlink is collected
    // (e.g. after RichTextBlock.Blocks.Clear() during a full rebuild). Using
    // one static handler + a per-Hyperlink action mapping means OnClick swaps
    // across renders are a cheap dictionary update, never an event detach +
    // re-attach.
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<
        Microsoft.UI.Xaml.Documents.Hyperlink, Action> s_hyperlinkClickActions = new();

    private static void OnHyperlinkClick(
        Microsoft.UI.Xaml.Documents.Hyperlink sender,
        Microsoft.UI.Xaml.Documents.HyperlinkClickEventArgs args)
    {
        if (s_hyperlinkClickActions.TryGetValue(sender, out var action))
            action?.Invoke();
    }

    private bool UpdateInlineUIContainerInPlace(
        RichTextInlineUIContainer prev,
        RichTextInlineUIContainer next,
        Microsoft.UI.Xaml.Documents.InlineUIContainer container,
        Action requestRerender)
    {
        // Route A both (preflight already confirmed Child ↔ Child).
        if (prev.Child is not null && next.Child is not null)
        {
            var existing = container.Child;
            var replacement = ReconcileV1Child(prev.Child, next.Child, existing, requestRerender);
            if (!ReferenceEquals(replacement, existing))
            {
                container.Child = replacement;
                if (replacement is FrameworkElement newFe)
                    newFe.SetValue(s_inlineUIRouteAProperty, true);
                return true;
            }
            return false; // child reconciled in place; no container-level mutation
        }
        // Route B / empty branches. Preflight (CanUpdateInlineUIContainerInPlace)
        // accepts any combination of {Factory, null} for both sides as "same
        // shape" so we must handle every transition here — otherwise a
        // factory→empty leaves the old child attached and an empty→factory
        // never mounts the new child.
        if (prev.Factory is null && next.Factory is null)
            return false; // both empty — nothing to do
        if (prev.Factory is not null && next.Factory is null)
        {
            container.Child = null;
            return true;
        }
        if (prev.Factory is null && next.Factory is not null)
        {
            UIElement? mounted = null;
            try { mounted = next.Factory(); }
            catch { mounted = null; }
            container.Child = mounted;
            return true;
        }
        // Both factories non-null — re-invoke only when the delegate identity
        // changes (factories are opaque; reference equality is the closest
        // signal authors have to "the inline UI source actually changed").
        if (ReferenceEquals(prev.Factory, next.Factory)) return false;
        UIElement? rebuilt = null;
        try { rebuilt = next.Factory!(); }
        catch { rebuilt = null; }
        container.Child = rebuilt;
        return true;
    }

    private void MarkRichTextBlockModified(WinUI.RichTextBlock rtb)
    {
        // WinUI Run / Hyperlink / LineBreak are TextElement, not UIElement,
        // so the highlight overlay can't paint them directly. Mark the
        // containing RichTextBlock so authors still see *something* flash
        // when inline mutations occur — matches the existing reconciler
        // convention of recording per-control modifications.
        if (ReactorFeatureFlags.HighlightReconcileChanges && _highlightModified is not null)
            _highlightModified.Add(rtb);
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
    // Internal so the V1-owned TemplatedListLifecycle can drive per-row content
    // reconciliation; also reused 1:1 by Reconciler.KeyedItemsBinding.cs.
    internal void RefreshRealizedContainers(WinUI.ListViewBase listViewBase, Internal.IItemViewSource viewSource, Action requestRerender)
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


    /// <summary>
    /// Spec 042 §6 — per-container offset animation for ListView/GridView
    /// survivors that moved index inside an active <see cref="Animations.Animate"/>
    /// transaction. WinUI's <c>ListViewBase.ContainerFromItem</c>
    /// returns the live container for a realized row (null for virtualized
    /// ones, which is fine — the realize path attaches the animation when
    /// they come back into view).
    /// </summary>
    // Internal so the V1-owned TemplatedListLifecycle can drive the keyed-move
    // offset animation; kept here next to the shared StartMoveOffsetAnimation primitive.
    internal void ApplyMoveAnimations(WinUI.ListViewBase lvb, IReadOnlyList<ReactorRow> moved, AnimationKind kind)
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
    // Internal so V1-owned lifecycle classes (LazyStackLifecycle and the templated
    // families) can reuse the shared repeater move-animation helper.
    internal void ApplyMoveAnimationsRepeater(WinUI.ItemsRepeater repeater, IReadOnlyList<ReactorRow> moved, AnimationKind kind)
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
#pragma warning disable CS0618 // legacy TreeViewNodeData.ContentElement path (see issue #447)
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
#pragma warning restore CS0618

    // ── Typed, data-driven TreeView<T> ───────────────────────────────────
    // Mount/Update bodies relocated to Reconciler.TemplatedTree.cs (spec 047 §14).

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


}
