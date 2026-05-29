using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Controls.Validation;
using Validation = Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
namespace Microsoft.UI.Reactor.Core;

// AI-HINT: Reconciler.Mount.cs — creates real WinUI controls from Element descriptions.
// Mount() is a big switch over all Element subtypes → MountXxx() methods.
// Each MountXxx allocates (or rents from pool) a WinUI control, sets properties,
// wires event handlers that look up the current Element via the ReactorAttached
// DP (see Reconciler.SetElementTag), so handlers are wired once and survive
// element recycling — the trampoline re-reads the current Element on each fire.
// Context values are pushed/popped around child processing.

public sealed partial class Reconciler
{
    /// <summary>
    /// Creates a WinUI control tree from an Element tree. Returns null for EmptyElement.
    /// </summary>
    // <snippet:mount-phase>
    public UIElement? Mount(Element element, Action requestRerender)
    {
        // Unwrap legacy ModifiedElement (backward compat)
        ElementModifiers? modifiers = element.Modifiers;
        if (element is ModifiedElement mod)
        {
            modifiers = mod.WrappedModifiers;
            if (mod.Inner.Modifiers is not null)
                modifiers = modifiers.Merge(mod.Inner.Modifiers);
            element = mod.Inner;
        }
        // </snippet:mount-phase>

        // Push context values onto scope before processing children
        var ctxValues = element.ContextValues;
        int ctxCount = 0;
        if (ctxValues is { Count: > 0 })
        {
            _contextScope.Push(ctxValues);
            ctxCount = ctxValues.Count;
        }

        UIElement? control;
        // Push stagger scope if this element has StaggerConfig — children mounted
        // inside MountXxx will consume stagger indices for their enter transitions.
        bool pushedStagger = element.StaggerConfig is not null;
        if (pushedStagger)
            PushStaggerScope(element.StaggerConfig!.Delay);
        try
        {

        // Spec 047 §14 Phase 4 (§4.5) — dispatch is V1 registry → external
        // `_typeRegistry` → composition-primitive switch. The 87 V1-reachable
        // element types route through `_v1Handlers`; the legacy `MountXxx`
        // switch arms were deleted (V1 is the production path). Only the 8
        // composition primitives (above the protocol) remain on the switch.
        if (_v1Handlers.TryGet(element.GetType(), out var v1Entry))
        {
            control = v1Entry.Mount(element, requestRerender, this);
        }
        // Registered types checked first
        else if (_typeRegistry.TryGetValue(element.GetType(), out var reg))
        {
            control = reg.Mount(element, requestRerender, this);
        }
        else
        {
        control = element switch
        {
            CommandHostElement ch => MountCommandHost(ch, requestRerender),
            ErrorBoundaryElement eb => MountErrorBoundary(eb, requestRerender),
            Validation.FormFieldElement ff => MountFormField(ff, requestRerender),
            Validation.ValidationVisualizerElement vv => MountValidationVisualizer(vv, requestRerender),
            Validation.ValidationRuleElement rule => MountValidationRule(rule),
            ComponentElement comp => MountComponent(comp, requestRerender),
            FuncElement func => MountFuncComponent(func, requestRerender),
            MemoElement memo => MountMemoComponent(memo, requestRerender),
            _ => null,
        };
        }

        if (control is not null)
        {
            DebugUIElementsCreated++;
            // Highlight capture is gated by the flag, not just by list
            // existence: the list is allocated lazily on first flag-on and
            // never freed afterward, so a non-null check would keep
            // appending forever once the user toggles the flag off.
            if (ReactorFeatureFlags.HighlightReconcileChanges
                && _highlightMounted is not null)
                _highlightMounted.Add(control);
        }

        // Apply inline modifiers after mounting
        if (modifiers is not null && control is FrameworkElement fe)
            ApplyModifiers(fe, modifiers, requestRerender);

        // After modifiers + setters have had a chance to set an explicit
        // AutomationName, fall back to the control's visible caption so UIA
        // clients that read AutomationProperties.Name directly don't see an
        // empty string on a Button("Save", …). Author-supplied names win.
        if (control is FrameworkElement captionFe)
            ApplyDefaultAutomationName(captionFe, ResolveCaptionForElement(element));

        // Apply theme-resource bindings (ThemeRef → resolved Brush from WinUI resources)
        if (element.ThemeBindings is not null && control is FrameworkElement thFe)
            ApplyThemeBindings(thFe, element.ThemeBindings);

        // Apply per-control resource overrides (lightweight styling)
        if (element.ResourceOverrides is not null && control is FrameworkElement resFe)
            ApplyResourceOverrides(resFe, null, element.ResourceOverrides);

        // Apply transitions after mounting (runs after .Set() callbacks)
        if (control is not null && (element.ImplicitTransitions is not null || element.ThemeTransitions is not null))
            ApplyTransitions(control, element.ImplicitTransitions, element.ThemeTransitions);

        // Apply Composition-layer layout animation (implicit Offset/Size animation on Visual)
        if (control is not null && element.LayoutAnimation is not null)
            ApplyLayoutAnimation(control, element.LayoutAnimation);

        // Apply compositor property animation (.Animate() modifier)
        if (control is not null && element.AnimationConfig is not null)
            ApplyPropertyAnimation(control, element.AnimationConfig, element.LayoutAnimation);

        // Apply enter transition (.Transition() modifier)
        if (control is not null && element.ElementTransition is not null)
        {
            var (staggerIdx, staggerDly) = ConsumeStaggerIndex();
            ApplyEnterTransition(control, element.ElementTransition, staggerIdx, staggerDly);
        }

        // Apply interaction states (.InteractionStates() modifier)
        if (control is not null && element.InteractionStates is not null)
            ApplyInteractionStates(control, element.InteractionStates);

        // Apply keyframe animations (.Keyframes() modifier)
        if (control is not null && element.KeyframeAnimations is not null)
            ApplyKeyframeAnimations(control, element.KeyframeAnimations);

        // Apply scroll-linked expression animations (.ScrollLinked() modifier)
        if (control is not null && element.ScrollAnimation is not null)
            ApplyScrollAnimation(control, element.ScrollAnimation);

        // Apply stagger delays to children (.Stagger() modifier)
        if (control is not null && element.StaggerConfig is not null)
            ApplyStaggerDelays(control, element.StaggerConfig);

        // Queue connected animation start if a prepared animation exists with this key
        if (control is not null && element.ConnectedAnimationKey is not null)
            QueueConnectedAnimationStart(control, element.ConnectedAnimationKey);

        }
        finally
        {
            if (pushedStagger)
                PopStaggerScope();
            if (ctxCount > 0)
                _contextScope.Pop(ctxCount);
        }

        return control;
    }

    private TextBlock MountText(TextBlockElement text)
    {
        var tb = _pool.TryRent(typeof(TextBlock)) as TextBlock ?? new TextBlock();
        tb.Text = text.Content;
        if (text.FontSize.HasValue) tb.FontSize = text.FontSize.Value;
        if (text.Weight.HasValue) tb.FontWeight = text.Weight.Value;
        if (text.FontStyle.HasValue) tb.FontStyle = text.FontStyle.Value;
        if (text.HorizontalAlignment.HasValue) tb.HorizontalAlignment = text.HorizontalAlignment.Value;
        if (text.TextWrapping.HasValue) tb.TextWrapping = text.TextWrapping.Value;
        if (text.TextAlignment.HasValue) tb.TextAlignment = text.TextAlignment.Value;
        if (text.TextTrimming.HasValue) tb.TextTrimming = text.TextTrimming.Value;
        if (text.IsTextSelectionEnabled.HasValue) tb.IsTextSelectionEnabled = text.IsTextSelectionEnabled.Value;
        if (text.FontFamily is not null) tb.FontFamily = text.FontFamily;
        if (text.LineHeight.HasValue) tb.LineHeight = text.LineHeight.Value;
        if (text.MaxLines > 0) tb.MaxLines = text.MaxLines;
        if (text.CharacterSpacing != 0) tb.CharacterSpacing = text.CharacterSpacing;
        if (text.TextDecorations != global::Windows.UI.Text.TextDecorations.None) tb.TextDecorations = text.TextDecorations;
        ApplySetters(text.Setters, tb);
        return tb;
    }

    private WinUI.RichTextBlock MountRichTextBlock(RichTextBlockElement richText)
    {
        var rtb = _pool.TryRent(typeof(WinUI.RichTextBlock)) as WinUI.RichTextBlock ?? new WinUI.RichTextBlock();
        rtb.IsTextSelectionEnabled = richText.IsTextSelectionEnabled;
        if (richText.TextWrapping.HasValue) rtb.TextWrapping = richText.TextWrapping.Value;
        // Shared helper with the V1 descriptor — clears Blocks then rebuilds
        // from Paragraphs or falls back to a single Run with .Text. Extracted
        // (Phase 3-final Batch B) so RichTextBlockDescriptor's .OneWay set
        // lambda can call the same code path.
        RebuildRichTextBlocks(richText, rtb);
        if (richText.FontSize.HasValue) rtb.FontSize = richText.FontSize.Value;
        if (richText.MaxLines > 0) rtb.MaxLines = richText.MaxLines;
        if (richText.LineHeight.HasValue) rtb.LineHeight = richText.LineHeight.Value;
        if (richText.TextAlignment.HasValue) rtb.TextAlignment = richText.TextAlignment.Value;
        if (richText.TextTrimming.HasValue) rtb.TextTrimming = richText.TextTrimming.Value;
        if (richText.CharacterSpacing != 0) rtb.CharacterSpacing = richText.CharacterSpacing;
        ApplySetters(richText.Setters, rtb);
        return rtb;
    }

    internal WinUI.Button MountButton(ButtonElement btn, Action requestRerender)
    {
        var rented = _pool.TryRent(typeof(WinUI.Button));
        var button = rented as WinUI.Button ?? new WinUI.Button();
        ApplyButtonEnabledState(button, btn);
        if (btn.ContentElement is not null)
            button.Content = Mount(btn.ContentElement, requestRerender);
        else
            button.Content = btn.Label;
        SetElementTag(button, btn);
        EnsureButtonWiring(button, btn);
        ApplySetters(btn.Setters, button);
        return button;
    }

    /// <summary>
    /// Applies the (Disabled, DisabledFocusable) state to a WinUI Button.
    /// True disable: IsEnabled=false (removes from tab order). Disabled-
    /// focusable: IsEnabled=true plus visual dim; UIA still reports the
    /// button as enabled (full AT "unavailable" reporting is a follow-up
    /// requiring a custom AutomationPeer override). The Click trampoline
    /// (see EnsureButtonWiring) drops invokes when the element is in
    /// disabled-focusable mode.
    /// </summary>
    internal static void ApplyButtonEnabledState(WinUI.Button button, ButtonElement btn)
    {
        // Visual dim + Click trampoline drop. UIA still reports the button as
        // enabled — a future fix would attach a custom ButtonAutomationPeer
        // overriding IsEnabledCore() to fully mirror the WinUI Win32 / ARIA
        // aria-disabled pattern. Tracked as a TODO; not required for the
        // keyboard-reachability win this method delivers.
        if (btn.IsDisabledFocusable)
        {
            button.IsEnabled = true;
            button.Opacity = 0.4;
        }
        else
        {
            button.IsEnabled = btn.IsEnabled;
            // ClearValue (not Opacity=1.0) so any opacity coming from a XAML
            // style, template, or external code path survives. Forcing 1.0
            // here would silently override a Setters/Resources Opacity binding
            // every time the button rerenders out of disabled-focusable mode.
            button.ClearValue(UIElement.OpacityProperty);
        }
    }

    /// <summary>
    /// Wires Button.Click trampoline only when the element has a handler AND
    /// the control hasn't been wired yet. Dedupe key is the per-control
    /// ButtonEventPayload (ControlEventStateBox) trampoline reference, attached
    /// on ReactorAttached.StateProperty (native DO identity), so two managed
    /// RCWs over the same native Button share one trampoline. Survives pool
    /// round-trips for the same reason.
    /// </summary>
    internal static void EnsureButtonWiring(WinUI.Button button, ButtonElement btn)
    {
        if (btn.OnClick is null) return;
        var payload = GetOrCreateControlEventPayload<V1Protocol.ButtonEventPayload>(button);
        if (payload.ClickTrampoline is not null) return;
        payload.ClickTrampoline = (s, _) =>
        {
            if (GetElementTag((UIElement)s!) is ButtonElement live)
            {
                if (live.IsDisabledFocusable) return;
                live.OnClick?.Invoke();
            }
        };
        button.Click += payload.ClickTrampoline;
    }

    private WinUI.HyperlinkButton MountHyperlinkButton(HyperlinkButtonElement hlBtn)
    {
        var hb = new WinUI.HyperlinkButton { Content = hlBtn.Content };
        if (hlBtn.NavigateUri is not null) hb.NavigateUri = hlBtn.NavigateUri;
        SetElementTag(hb, hlBtn);
        if (hlBtn.OnClick is not null)
            hb.Click += (s, _) => (GetElementTag((UIElement)s!) as HyperlinkButtonElement)?.OnClick?.Invoke();
        ApplySetters(hlBtn.Setters, hb);
        return hb;
    }

    private WinPrim.RepeatButton MountRepeatButton(RepeatButtonElement repBtn)
    {
        var rb = new WinPrim.RepeatButton { Content = repBtn.Label, Delay = repBtn.Delay, Interval = repBtn.Interval };
        SetElementTag(rb, repBtn);
        if (repBtn.OnClick is not null)
            rb.Click += (s, _) => (GetElementTag((UIElement)s!) as RepeatButtonElement)?.OnClick?.Invoke();
        ApplySetters(repBtn.Setters, rb);
        return rb;
    }

    private WinPrim.ToggleButton MountToggleButton(ToggleButtonElement togBtn)
    {
        var tb = new WinPrim.ToggleButton { Content = togBtn.Label };
        if (togBtn.IsThreeState)
        {
            tb.IsThreeState = true;
            tb.IsChecked = togBtn.CheckedState;
        }
        else
        {
            tb.IsChecked = togBtn.IsChecked;
        }
        SetElementTag(tb, togBtn);
        // Bind to Click — fires only for real user toggles. Checked/Unchecked
        // would also fire when UpdateToggleButton rewrites IsChecked during a
        // state-driven rerender, which would re-enter the callback and loop.
        if (togBtn.OnIsCheckedChanged is not null || togBtn.OnCheckedStateChanged is not null)
            tb.Click += (s, _) =>
            {
                var t = (WinPrim.ToggleButton)s!;
                if (GetElementTag(t) is not ToggleButtonElement live) return;
                live.OnIsCheckedChanged?.Invoke(t.IsChecked ?? false);
                live.OnCheckedStateChanged?.Invoke(t.IsChecked);
            };
        ApplySetters(togBtn.Setters, tb);
        return tb;
    }

    private WinUI.DropDownButton MountDropDownButton(DropDownButtonElement ddBtn, Action requestRerender)
    {
        var ddb = new WinUI.DropDownButton { Content = ddBtn.Label };
        if (ddBtn.Flyout is not null)
            ddb.Flyout = CreateFlyoutFromElement(ddBtn.Flyout, requestRerender);
        ApplySetters(ddBtn.Setters, ddb);
        return ddb;
    }

    private WinUI.SplitButton MountSplitButton(SplitButtonElement spBtn, Action requestRerender)
    {
        var sb = new WinUI.SplitButton { Content = spBtn.Label };
        SetElementTag(sb, spBtn);
        if (spBtn.OnClick is not null)
            sb.Click += (s, _) => (GetElementTag((UIElement)s!) as SplitButtonElement)?.OnClick?.Invoke();
        if (spBtn.Flyout is not null)
            sb.Flyout = CreateFlyoutFromElement(spBtn.Flyout, requestRerender);
        ApplySetters(spBtn.Setters, sb);
        return sb;
    }

    // Spec 047 §14 Phase 3-final Batch B — widened to internal static so
    // NumberBoxDescriptor can register the same captured-free trampolines
    // via the .Immediate entry shape.
    internal static readonly Microsoft.UI.Xaml.DependencyPropertyChangedCallback NumberBoxImmediateTextChanged =
        (sender, _) =>
        {
            if (sender is not WinUI.NumberBox box) return;
            HandleNumberBoxImmediateTextChanged(box, box.Text);
        };

    internal static void NumberBoxLoadedEnsureImmediateTextBox(object sender, RoutedEventArgs _)
    {
        if (sender is not WinUI.NumberBox box) return;
        if (EnsureNumberBoxImmediateTextBoxWiring(box))
            box.Loaded -= NumberBoxLoadedEnsureImmediateTextBox;
    }

    internal static bool EnsureNumberBoxImmediateTextBoxWiring(WinUI.NumberBox box)
    {
        var payload = GetOrCreateControlEventPayload<V1Protocol.NumberBoxEventPayload>(box);
        if (payload.ImmediateInnerWired) return true;

        box.ApplyTemplate();
        var input = FindDescendant<TextBox>(box);
        if (input is null) return false;

        payload.ImmediateInnerWired = true;
        input.TextChanged += (_, _) => HandleNumberBoxImmediateTextChanged(box, input.Text);
        return true;
    }

    internal static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    internal static void HandleNumberBoxImmediateTextChanged(WinUI.NumberBox box, string text)
    {
        if (GetElementTag(box) is not NumberBoxElement el) return;
        if (el.OnValueChanged is null) return;
        if (el.GetAttached<Microsoft.UI.Reactor.Controls.Validation.ImmediateValueAttached>() is null) return;
        if (!double.TryParse(text,
            global::System.Globalization.NumberStyles.Float,
            global::System.Globalization.CultureInfo.CurrentCulture, out var parsed)) return;
        // Reject NaN/±Infinity — double.TryParse accepts the literal strings
        // "NaN"/"Infinity" by default, and NaN comparisons are never equal,
        // so the sync-guard below would let them through.
        if (!double.IsFinite(parsed)) return;
        if (parsed < el.Minimum || parsed > el.Maximum) return;
        if (AreNumberBoxValuesEquivalent(parsed, el.Value)) return; // already in sync; suppresses post-programmatic-write callback
        if (CanSynchronizeNumberBoxImmediateValueWithoutReformat(el, text, parsed)
            && !AreNumberBoxValuesEquivalent(box.Value, parsed))
        {
            ChangeEchoSuppressor.BeginSuppress(box);
            box.Value = parsed;
        }
        el.OnValueChanged.Invoke(parsed);
    }


    internal WinUI.CheckBox MountCheckBox(CheckBoxElement cb)
    {
        var checkBox = new WinUI.CheckBox { Content = cb.Label };
        if (cb.IsThreeState)
        {
            checkBox.IsThreeState = true;
            checkBox.IsChecked = cb.CheckedState;
        }
        else
        {
            checkBox.IsChecked = cb.IsChecked;
        }
        SetElementTag(checkBox, cb);
        if (cb.OnIsCheckedChanged is not null || cb.OnCheckedStateChanged is not null)
        {
            checkBox.Checked += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var el = GetElementTag(c) as CheckBoxElement;
                el?.OnIsCheckedChanged?.Invoke(true);
                el?.OnCheckedStateChanged?.Invoke(true);
            };
            checkBox.Unchecked += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var el = GetElementTag(c) as CheckBoxElement;
                el?.OnIsCheckedChanged?.Invoke(false);
                el?.OnCheckedStateChanged?.Invoke(false);
            };
            checkBox.Indeterminate += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                var el = GetElementTag(c) as CheckBoxElement;
                el?.OnCheckedStateChanged?.Invoke(null);
            };
        }
        ApplySetters(cb.Setters, checkBox);
        return checkBox;
    }






    // Dedupe via ModifierEventHandlerState (attached on ReactorAttached.StateProperty,
    // keyed by native DependencyObject identity). A plain CWT keyed by managed
    // RCW identity is unsafe — two RCWs over the same DO miss the dedupe and
    // double-subscribe, fanning one Toggled into multiple user-callback invocations.






    private WinUI.ProgressBar MountProgress(ProgressElement prog)
    {
        var bar = _pool.TryRent(typeof(WinUI.ProgressBar)) as WinUI.ProgressBar ?? new WinUI.ProgressBar();
        bar.IsIndeterminate = prog.IsIndeterminate;
        bar.Minimum = prog.Minimum;
        bar.Maximum = prog.Maximum;
        bar.ShowError = prog.ShowError;
        bar.ShowPaused = prog.ShowPaused;
        if (prog.Value.HasValue) bar.Value = prog.Value.Value;
        ApplySetters(prog.Setters, bar);
        return bar;
    }

    private WinUI.ProgressRing MountProgressRing(ProgressRingElement ring)
    {
        var pr = _pool.TryRent(typeof(WinUI.ProgressRing)) as WinUI.ProgressRing ?? new WinUI.ProgressRing();
        pr.IsIndeterminate = ring.IsIndeterminate;
        pr.IsActive = ring.IsActive;
        pr.Minimum = ring.Minimum;
        pr.Maximum = ring.Maximum;
        if (ring.Value.HasValue) pr.Value = ring.Value.Value;
        ApplySetters(ring.Setters, pr);
        return pr;
    }

    private WinUI.PersonPicture MountPersonPicture(PersonPictureElement pp)
    {
        var pic = new WinUI.PersonPicture { IsGroup = pp.IsGroup, BadgeNumber = pp.BadgeNumber };
        if (pp.DisplayName is not null) pic.DisplayName = pp.DisplayName;
        if (pp.Initials is not null) pic.Initials = pp.Initials;
        if (pp.ProfilePicture is not null)
            pic.ProfilePicture = new BitmapImage(new Uri(pp.ProfilePicture, UriKind.RelativeOrAbsolute));
        ApplySetters(pp.Setters, pic);
        return pic;
    }


    private WinUI.RichEditBox MountRichEditBox(RichEditBoxElement reb)
    {
        var box = new WinUI.RichEditBox
        {
            IsReadOnly = reb.IsReadOnly,
            TextWrapping = reb.TextWrapping,
            AcceptsReturn = reb.AcceptsReturn,
        };
        if (reb.Header is not null) box.Header = reb.Header;
        if (reb.PlaceholderText is not null) box.PlaceholderText = reb.PlaceholderText;
        if (reb.IsSpellCheckEnabled.HasValue) box.IsSpellCheckEnabled = reb.IsSpellCheckEnabled.Value;
        if (reb.MaxLength != 0) box.MaxLength = reb.MaxLength;
        if (reb.SelectionHighlightColor is not null) box.SelectionHighlightColor = reb.SelectionHighlightColor;
        if (!string.IsNullOrEmpty(reb.Text))
            box.Document.SetText(Microsoft.UI.Text.TextSetOptions.None, reb.Text);
        SetElementTag(box, reb);
        if (reb.OnTextChanged is not null)
            box.TextChanged += (s, _) =>
            {
                var r = (WinUI.RichEditBox)s!;
                r.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out var text);
                (GetElementTag(r) as RichEditBoxElement)?.OnTextChanged?.Invoke(text?.TrimEnd('\r') ?? "");
            };
        ApplySetters(reb.Setters, box);
        return box;
    }

    internal WinUI.VariableSizedWrapGrid MountWrapGrid(WrapGridElement wg, Action requestRerender)
    {
        var grid = new WinUI.VariableSizedWrapGrid { Orientation = wg.Orientation };
        if (wg.MaximumRowsOrColumns >= 0) grid.MaximumRowsOrColumns = wg.MaximumRowsOrColumns;
        if (!double.IsNaN(wg.ItemWidth)) grid.ItemWidth = wg.ItemWidth;
        if (!double.IsNaN(wg.ItemHeight)) grid.ItemHeight = wg.ItemHeight;
        foreach (var child in wg.Children)
        {
            if (child is null or EmptyElement) continue;
            var childControl = Mount(child, requestRerender);
            if (childControl is null) continue;
            var wga = child.GetAttached<WrapGridAttached>();
            if (wga is not null && childControl is FrameworkElement fe)
            {
                if (wga.RowSpan > 1) WinUI.VariableSizedWrapGrid.SetRowSpan(fe, wga.RowSpan);
                if (wga.ColumnSpan > 1) WinUI.VariableSizedWrapGrid.SetColumnSpan(fe, wga.ColumnSpan);
            }
            grid.Children.Add(childControl);
        }
        SetElementTag(grid, wg);
        ApplySetters(wg.Setters, grid);
        return grid;
    }

    internal WinUI.StackPanel MountStack(StackElement stack, Action requestRerender)
    {
        var panel = _pool.TryRent(typeof(WinUI.StackPanel)) as WinUI.StackPanel ?? new WinUI.StackPanel();
        panel.Orientation = stack.Orientation;
        panel.Spacing = stack.Spacing;
        if (stack.HorizontalAlignment.HasValue) panel.HorizontalAlignment = stack.HorizontalAlignment.Value;
        if (stack.VerticalAlignment.HasValue) panel.VerticalAlignment = stack.VerticalAlignment.Value;
        // Apply RequestedTheme before mounting children so that child ThemeRef
        // bindings resolve against the correct theme variant from the start.
        foreach (var child in stack.Children)
        {
            if (child is null or EmptyElement) continue;
            var childControl = Mount(child, requestRerender);
            if (childControl is not null) panel.Children.Add(childControl);
        }
        SetElementTag(panel, stack);
        ApplySetters(stack.Setters, panel);
        return panel;
    }

    internal WinUI.Grid MountGrid(GridElement grid, Action requestRerender)
    {
        var g = _pool.TryRent(typeof(WinUI.Grid)) as WinUI.Grid ?? new WinUI.Grid();
        g.RowSpacing = grid.RowSpacing;
        g.ColumnSpacing = grid.ColumnSpacing;
        g.ColumnDefinitions.Clear();
        g.RowDefinitions.Clear();
        foreach (var col in grid.Definition.Columns) g.ColumnDefinitions.Add(ParseColumnDef(col));
        foreach (var row in grid.Definition.Rows) g.RowDefinitions.Add(ParseRowDef(row));
        foreach (var child in grid.Children)
        {
            var ctrl = Mount(child, requestRerender);
            if (ctrl is null) continue;
            var ga = child.GetAttached<GridAttached>();
            if (ga is not null && ctrl is FrameworkElement fe)
            {
                WinUI.Grid.SetRow(fe, ga.Row);
                WinUI.Grid.SetColumn(fe, ga.Column);
                if (ga.RowSpan > 1) WinUI.Grid.SetRowSpan(fe, ga.RowSpan);
                if (ga.ColumnSpan > 1) WinUI.Grid.SetColumnSpan(fe, ga.ColumnSpan);
            }
            g.Children.Add(ctrl);
        }
        SetElementTag(g, grid);
        ApplySetters(grid.Setters, g);
        return g;
    }

    internal WinUI.Expander MountExpander(ExpanderElement exp, Action requestRerender)
    {
        var expander = new WinUI.Expander
        {
            IsExpanded = exp.IsExpanded,
            ExpandDirection = exp.ExpandDirection,
        };
        // Element header wins over the string slot (matches the spec
        // "HeaderTemplate" slot semantics — strings are still supported as
        // the default header content).
        if (exp.HeaderTemplate is not null)
            expander.Header = Mount(exp.HeaderTemplate, requestRerender);
        else
            expander.Header = exp.Header;
        if (exp.ContentTransitions is not null) expander.ContentTransitions = exp.ContentTransitions;
        expander.Content = Mount(exp.Content, requestRerender);
        SetElementTag(expander, exp);
        if (exp.OnIsExpandedChanged is not null)
        {
            expander.Expanding += (s, _) => (GetElementTag((UIElement)s!) as ExpanderElement)?.OnIsExpandedChanged?.Invoke(true);
            expander.Collapsed += (s, _) => (GetElementTag((UIElement)s!) as ExpanderElement)?.OnIsExpandedChanged?.Invoke(false);
        }
        ApplySetters(exp.Setters, expander);
        return expander;
    }

    private WinUI.SplitView MountSplitView(SplitViewElement svEl, Action requestRerender)
    {
        var splitView = new WinUI.SplitView
        {
            IsPaneOpen = svEl.IsPaneOpen, OpenPaneLength = svEl.OpenPaneLength,
            CompactPaneLength = svEl.CompactPaneLength, DisplayMode = svEl.DisplayMode,
            LightDismissOverlayMode = svEl.LightDismissOverlayMode,
        };
        if (svEl.PaneBackground is not null) splitView.PaneBackground = svEl.PaneBackground;
        if (svEl.Pane is not null) splitView.Pane = Mount(svEl.Pane, requestRerender);
        if (svEl.Content is not null) splitView.Content = Mount(svEl.Content, requestRerender);
        SetElementTag(splitView, svEl);
        if (svEl.OnPaneOpenChanged is not null)
        {
            splitView.PaneOpening += (s, _) => (GetElementTag((UIElement)s!) as SplitViewElement)?.OnPaneOpenChanged?.Invoke(true);
            splitView.PaneClosing += (s, _) => (GetElementTag((UIElement)s!) as SplitViewElement)?.OnPaneOpenChanged?.Invoke(false);
        }
        ApplySetters(svEl.Setters, splitView);
        return splitView;
    }

    private WinUI.Viewbox MountViewbox(ViewboxElement vb, Action requestRerender)
    {
        var viewbox = _pool.TryRent(typeof(WinUI.Viewbox)) as WinUI.Viewbox ?? new WinUI.Viewbox();
        viewbox.Child = Mount(vb.Child, requestRerender) as UIElement;
        if (vb.Stretch.HasValue) viewbox.Stretch = vb.Stretch.Value;
        if (vb.StretchDirection.HasValue) viewbox.StretchDirection = vb.StretchDirection.Value;
        ApplySetters(vb.Setters, viewbox);
        return viewbox;
    }

    internal WinUI.Canvas MountCanvas(CanvasElement cvs, Action requestRerender)
    {
        var canvas = _pool.TryRent(typeof(WinUI.Canvas)) as WinUI.Canvas ?? new WinUI.Canvas();
        if (cvs.Width.HasValue) canvas.Width = cvs.Width.Value;
        if (cvs.Height.HasValue) canvas.Height = cvs.Height.Value;
        if (cvs.Background is not null) canvas.Background = cvs.Background;
        foreach (var child in cvs.Children)
        {
            var ctrl = Mount(child, requestRerender);
            if (ctrl is null) continue;
            var ca = child.GetAttached<CanvasAttached>();
            if (ca is not null && ctrl is FrameworkElement fe)
                ApplyCanvasPosition(fe, ca);
            canvas.Children.Add(ctrl);
        }
        SetElementTag(canvas, cvs);
        ApplySetters(cvs.Setters, canvas);
        return canvas;
    }

    internal Layout.FlexPanel MountFlex(FlexElement flex, Action requestRerender)
    {
        var panel = _pool.TryRent(typeof(Layout.FlexPanel)) as Layout.FlexPanel ?? new Layout.FlexPanel();
        panel.Direction = flex.Direction;
        panel.JustifyContent = flex.JustifyContent;
        panel.AlignItems = flex.AlignItems;
        panel.AlignContent = flex.AlignContent;
        panel.Wrap = flex.Wrap;
        panel.ColumnGap = flex.ColumnGap;
        panel.RowGap = flex.RowGap;
        panel.FlexPadding = flex.FlexPadding;
        foreach (var child in flex.Children)
        {
            var ctrl = Mount(child, requestRerender);
            if (ctrl is null) continue;
            // Always apply flex properties — clears stale values on pool-rented controls.
            ApplyFlexAttached(child, ctrl);
            panel.Children.Add(ctrl);
        }
        SetElementTag(panel, flex);
        ApplySetters(flex.Setters, panel);
        return panel;
    }

    internal WinUI.Grid MountNavigationHost(NavigationHostElement element, Action requestRerender)
    {
        var grid = new WinUI.Grid();
        var handle = (Navigation.INavigationHandle)element.NavigationHandle;
        var routeMap = element.RouteMap;
        var currentRoute = handle.CurrentRoute;

        // Resolve and mount the initial route's element
        var childElement = routeMap(currentRoute);
        var childControl = Mount(childElement, requestRerender);
        if (childControl is not null)
            grid.Children.Add(childControl);

        // Track state for update/unmount
        var node = new NavigationHostNode
        {
            Handle = handle,
            LastRenderedRoute = currentRoute,
            CurrentChildElement = childElement,
            CurrentChildControl = childControl,
            RouteMap = routeMap,
            RequestRerender = requestRerender,
            HostTransition = element.Transition,
            CacheMode = element.CacheMode,
        };

        // Create page cache when caching is enabled
        if (element.CacheMode != Navigation.NavigationCacheMode.Disabled)
        {
            node.Cache = new Navigation.NavigationCache(
                element.CacheSize, evicted => Unmount(evicted));
        }

        // Subscribe to route changes so NavigationHost updates even if an intermediate
        // component's ShouldUpdate blocks the re-render propagation.
        void onRouteChanged() => requestRerender();
        handle.RouteChanged += onRouteChanged;
        node.RouteChangedHandler = onRouteChanged;

        // Wire lifecycle guard: invokes onNavigatingFrom callbacks from the current
        // page's component tree before the stack mutation. Records the navigation mode
        // and previous route for post-swap onNavigatedTo/onNavigatedFrom invocation.
        handle.LifecycleGuard = ctx =>
        {
            InvokeNavigatingFrom(node.CurrentChildControl, ctx);
            if (!ctx.IsCancelled)
            {
                node.PendingNavigationMode = ctx.Mode;
                node.PendingPreviousRoute = ctx.Route;
            }
        };

        _navigationHostNodes[grid] = node;
        return grid;
    }

    private static WinUI.NavigationViewItem CreateNavItem(NavigationViewItemData data)
    {
        var item = new WinUI.NavigationViewItem { Content = data.Content, Tag = data.Tag ?? data.Content };
        var icon = ResolveIcon(data.IconElement, data.Icon);
        if (icon is not null) item.Icon = icon;
        if (data.Children is not null)
            foreach (var child in data.Children) item.MenuItems.Add(CreateNavItem(child));
        return item;
    }



    // Spec 045 §2.2 — pin button for ToolWindow tabs. When IsPinnable is
    // true the header becomes a StackPanel { TextBlock(title) , pin Button };
    // otherwise the existing string header path is preserved verbatim so
    // tabs without pin affordance are visually identical to baseline.
    internal static object BuildTabHeader(TabViewItemData tabItem)
    {
        if (!tabItem.IsPinnable) return tabItem.Header;
        var sp = new WinUI.StackPanel
        {
            Orientation = WinUI.Orientation.Horizontal,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
        };
        var text = new WinUI.TextBlock
        {
            Text = tabItem.Header,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
        };
        sp.Children.Add(text);
        sp.Children.Add(BuildPinButton(tabItem));
        return sp;
    }

    /// <summary>
    /// In-place refresh of a pinnable tab header built by
    /// <see cref="BuildTabHeader"/>. Updates the embedded TextBlock + pin
    /// Button's Tag (so the captured Click handler resolves to the new
    /// OnPinRequested closure) + the FontIcon glyph for IsPinned state.
    /// Returns <c>false</c> when the existing StackPanel doesn't match
    /// the expected shape — the caller should fall back to a full
    /// rebuild. Spec 045 §2.2; called by <c>UpdateTabView</c>.
    /// </summary>
    internal static bool TryUpdatePinHeaderInPlace(
        WinUI.StackPanel existing,
        TabViewItemData oldTab,
        TabViewItemData newTab)
    {
        if (existing.Children.Count != 2) return false;
        if (existing.Children[0] is not WinUI.TextBlock label) return false;
        if (existing.Children[1] is not WinUI.Button pinBtn) return false;
        if (pinBtn.Content is not WinUI.FontIcon icon) return false;

        if (label.Text != newTab.Header) label.Text = newTab.Header;

        // Tag carries the live TabViewItemData; the Click handler reads
        // .OnPinRequested off the Tag. Swapping the Tag swaps the
        // closure without touching the visual tree.
        pinBtn.Tag = newTab;

        var newGlyph = newTab.IsPinned ? "" : "";
        if (icon.Glyph != newGlyph) icon.Glyph = newGlyph;

        if (oldTab.PinAutomationName != newTab.PinAutomationName)
        {
            if (!string.IsNullOrEmpty(newTab.PinAutomationName))
            {
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pinBtn, newTab.PinAutomationName);
                Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(pinBtn, newTab.PinAutomationName);
            }
            else
            {
                pinBtn.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty);
                Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(pinBtn, null);
            }
        }
        if (oldTab.PinAutomationId != newTab.PinAutomationId
            && !string.IsNullOrEmpty(newTab.PinAutomationId))
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(pinBtn, newTab.PinAutomationId);
        }
        return true;
    }

    private static WinUI.Button BuildPinButton(TabViewItemData tabItem)
    {
        var btn = new WinUI.Button
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
            Padding = new Microsoft.UI.Xaml.Thickness(4, 0, 4, 0),
            Margin = new Microsoft.UI.Xaml.Thickness(6, 0, 0, 0),
            MinWidth = 0,
            MinHeight = 0,
            Content = new WinUI.FontIcon
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                Glyph = tabItem.IsPinned ? "" : "",
                FontSize = 12,
            },
        };
        if (!string.IsNullOrEmpty(tabItem.PinAutomationName))
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, tabItem.PinAutomationName);
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(btn, tabItem.PinAutomationName);
        }
        if (!string.IsNullOrEmpty(tabItem.PinAutomationId))
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(btn, tabItem.PinAutomationId);
        // Tag with the TabViewItemData so updates can re-resolve the live
        // OnPinRequested closure (handler is captured at mount; if the
        // closure changes between renders the tag-based lookup picks up
        // the new one via the Header rebuild path).
        btn.Tag = tabItem;
        btn.Click += (s, _) =>
        {
            if (s is WinUI.Button b && b.Tag is TabViewItemData td)
                td.OnPinRequested?.Invoke();
        };
        return btn;
    }

    private WinUI.BreadcrumbBar MountBreadcrumbBar(BreadcrumbBarElement bcb)
    {
        var bar = new WinUI.BreadcrumbBar();
        bar.ItemsSource = bcb.Items.Select(i => i.Label).ToList();
        SetElementTag(bar, bcb);
        if (bcb.OnItemClicked is not null)
            bar.ItemClicked += (s, args) =>
            {
                var el = GetElementTag((UIElement)s!) as BreadcrumbBarElement;
                if (el is not null && args.Index >= 0 && args.Index < el.Items.Length) el.OnItemClicked?.Invoke(el.Items[args.Index]);
            };
        ApplySetters(bcb.Setters, bar);
        return bar;
    }

    private WinUI.Pivot MountPivot(PivotElement pvt, Action requestRerender)
    {
        var pivot = new WinUI.Pivot { SelectedIndex = pvt.SelectedIndex };
        if (pvt.Title is not null) pivot.Title = pvt.Title;
        foreach (var item in pvt.Items)
        {
            var pi = new WinUI.PivotItem { Header = item.Header, Content = Mount(item.Content, requestRerender) };
            pivot.Items.Add(pi);
        }
        SetElementTag(pivot, pvt);
        if (pvt.OnSelectedIndexChanged is not null)
            pivot.SelectionChanged += (s, _) =>
            {
                var p = (WinUI.Pivot)s!;
                (GetElementTag(p) as PivotElement)?.OnSelectedIndexChanged?.Invoke(p.SelectedIndex);
            };
        ApplySetters(pvt.Setters, pivot);
        return pivot;
    }

    // Internal visibility so V1Protocol.Handlers.ListViewHandler can delegate
    // to the shared mount body (Path B from spec 047 §14 Phase 1 task 1.15:
    // delegate + shape declaration rather than full reimplementation, so the
    // V1 ON and V1 OFF paths execute identical mount bodies).
    internal WinUI.ListView MountListView(ListViewElement lv, Action requestRerender)
    {
        var listView = new WinUI.ListView
        {
            SelectionMode = lv.SelectionMode,
            IsItemClickEnabled = lv.OnItemClick is not null,
            IncrementalLoadingTrigger = lv.IncrementalLoadingTrigger,
        };
        if (lv.Header is not null) listView.Header = lv.Header;
        if (lv.ItemContainerStyle is not null) listView.ItemContainerStyle = lv.ItemContainerStyle;

        SetElementTag(listView, lv);

        // DataTemplate with a ContentControl shell — we populate its Content on demand
        listView.ItemTemplate = SharedContentControlTemplate.Value;

        listView.ContainerContentChanging += (sender, args) =>
        {
            if (args.InRecycleQueue)
            {
                if (args.ItemContainer.ContentTemplateRoot is ContentControl oldCc)
                {
                    if (oldCc.Content is UIElement oldCtrl)
                        UnmountChild(oldCtrl);
                    oldCc.Content = null;
                }
                return;
            }

            args.Handled = true;
            var items = (GetElementTag((UIElement)sender!) as ListViewElement)?.Items;
            if (items is not null && args.ItemIndex >= 0 && args.ItemIndex < items.Length
                && args.ItemContainer.ContentTemplateRoot is ContentControl cc)
            {
                var ctrl = Mount(items[args.ItemIndex], requestRerender);
                cc.Content = ctrl;
            }
        };

        // Subscribe unconditionally so OnSelectionChanged (multi-select snapshot)
        // and OnSelectedIndexChanged (single focused index) both pick up
        // handlers attached on a later record-with without re-subscribing.
        listView.SelectionChanged += (s, _) =>
        {
            var l = (WinUI.ListView)s!;
            if (GetElementTag(l) is not ListViewElement el) return;
            el.OnSelectedIndexChanged?.Invoke(l.SelectedIndex);
            if (el.OnSelectionChanged is { } h)
            {
                // SelectedItems is List<object> of int — copy into a typed snapshot.
                var snapshot = new List<int>(l.SelectedItems.Count);
                foreach (var item in l.SelectedItems)
                    if (item is int i) snapshot.Add(i);
                h(snapshot);
            }
        };
        if (lv.OnItemClick is not null)
            listView.ItemClick += (s, args) =>
            {
                var l = (WinUI.ListView)s!;
                if (args.ClickedItem is int idx)
                    (GetElementTag(l) as ListViewElement)?.OnItemClick?.Invoke(idx);
            };

        // Set ItemsSource LAST — triggers container creation which needs the handler above
        listView.ItemsSource = Enumerable.Range(0, lv.Items.Length).ToList();

        if (lv.SelectedIndex >= 0) listView.SelectedIndex = lv.SelectedIndex;
        ApplySetters(lv.Setters, listView);
        return listView;
    }

    internal WinUI.GridView MountGridView(GridViewElement gv, Action requestRerender)
    {
        var gridView = new WinUI.GridView
        {
            SelectionMode = gv.SelectionMode,
            IsItemClickEnabled = gv.OnItemClick is not null,
            IncrementalLoadingTrigger = gv.IncrementalLoadingTrigger,
        };
        if (gv.Header is not null) gridView.Header = gv.Header;
        if (gv.ItemContainerStyle is not null) gridView.ItemContainerStyle = gv.ItemContainerStyle;

        SetElementTag(gridView, gv);

        gridView.ItemTemplate = SharedContentControlTemplate.Value;

        gridView.ContainerContentChanging += (sender, args) =>
        {
            if (args.InRecycleQueue)
            {
                if (args.ItemContainer.ContentTemplateRoot is ContentControl oldCc)
                {
                    if (oldCc.Content is UIElement oldCtrl)
                        UnmountChild(oldCtrl);
                    oldCc.Content = null;
                }
                return;
            }

            args.Handled = true;
            var items = (GetElementTag((UIElement)sender!) as GridViewElement)?.Items;
            if (items is not null && args.ItemIndex >= 0 && args.ItemIndex < items.Length
                && args.ItemContainer.ContentTemplateRoot is ContentControl cc)
            {
                var ctrl = Mount(items[args.ItemIndex], requestRerender);
                cc.Content = ctrl;
            }
        };

        gridView.SelectionChanged += (s, _) =>
        {
            var g = (WinUI.GridView)s!;
            if (GetElementTag(g) is not GridViewElement el) return;
            el.OnSelectedIndexChanged?.Invoke(g.SelectedIndex);
            if (el.OnSelectionChanged is { } h)
            {
                var snapshot = new List<int>(g.SelectedItems.Count);
                foreach (var item in g.SelectedItems)
                    if (item is int i) snapshot.Add(i);
                h(snapshot);
            }
        };
        if (gv.OnItemClick is not null)
            gridView.ItemClick += (s, args) =>
            {
                var g = (WinUI.GridView)s!;
                if (args.ClickedItem is int idx)
                    (GetElementTag(g) as GridViewElement)?.OnItemClick?.Invoke(idx);
            };

        gridView.ItemsSource = Enumerable.Range(0, gv.Items.Length).ToList();

        if (gv.SelectedIndex >= 0) gridView.SelectedIndex = gv.SelectedIndex;
        ApplySetters(gv.Setters, gridView);
        return gridView;
    }

    private WinUI.TreeView MountTreeView(TreeViewElement tv, Action requestRerender)
    {
        var treeView = new WinUI.TreeView
        {
            SelectionMode = tv.SelectionMode,
            CanDragItems = tv.CanDragItems,
            AllowDrop = tv.AllowDrop,
            CanReorderItems = tv.CanReorderItems,
        };

        // Check if any node uses ContentElement for custom rendering
        bool hasContentElements = HasAnyContentElement(tv.Nodes);

        if (hasContentElements)
        {
            // Use ContentControl template so pre-mounted Elements display in the tree
            treeView.ItemTemplate = SharedContentControlTemplate.Value;
        }
        else
        {
            // Default: text-binding template for efficiency
            // In node-mode, DataContext of the template = TreeViewNode,
            // so {Binding Content.Content} resolves TreeViewNode.Content (TreeViewNodeData) → .Content (string).
            treeView.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "<TextBlock Text='{Binding Content.Content}'/>" +
                "</DataTemplate>");
        }

        foreach (var node in tv.Nodes)
            treeView.RootNodes.Add(CreateTreeNode(node, hasContentElements, requestRerender));

        SetElementTag(treeView, tv);

        if (tv.OnItemInvoked is not null)
            treeView.ItemInvoked += (s, args) =>
            {
                var t = (WinUI.TreeView)s!;
                if (args.InvokedItem is WinUI.TreeViewNode tvn
                    && tvn.Content is TreeViewNodeData nodeData)
                {
                    (GetElementTag(t) as TreeViewElement)?.OnItemInvoked?.Invoke(nodeData);
                }
            };

        if (tv.OnExpanding is not null)
            treeView.Expanding += (s, args) =>
            {
                var t = (WinUI.TreeView)s!;
                if (args.Node.Content is TreeViewNodeData nodeData)
                    (GetElementTag(t) as TreeViewElement)?.OnExpanding?.Invoke(nodeData);
            };

        ApplySetters(tv.Setters, treeView);
        return treeView;
    }

    private static bool HasAnyContentElement(TreeViewNodeData[] nodes)
    {
        foreach (var node in nodes)
        {
            if (node.ContentElement is not null) return true;
            if (node.Children is not null && HasAnyContentElement(node.Children)) return true;
        }
        return false;
    }

    private WinUI.TreeViewNode CreateTreeNode(TreeViewNodeData data, bool mountElements, Action requestRerender)
    {
        var node = new WinUI.TreeViewNode { IsExpanded = data.IsExpanded };

        if (mountElements && data.ContentElement is not null)
        {
            // Mount the Element and store the UIElement as Content.
            // The ContentControl template will display it via ContentPresenter.
            var ctrl = Mount(data.ContentElement, requestRerender);
            node.Content = ctrl;
        }
        else
        {
            node.Content = data;
        }

        if (data.Children is not null)
            foreach (var child in data.Children)
                node.Children.Add(CreateTreeNode(child, mountElements, requestRerender));
        return node;
    }

    /// <summary>Backward-compatible overload for non-ContentElement code paths.</summary>
    private static WinUI.TreeViewNode CreateTreeNode(TreeViewNodeData data)
    {
        var node = new WinUI.TreeViewNode { Content = data, IsExpanded = data.IsExpanded };
        if (data.Children is not null)
            foreach (var child in data.Children) node.Children.Add(CreateTreeNode(child));
        return node;
    }

    private WinUI.FlipView MountFlipView(FlipViewElement fv, Action requestRerender)
    {
        var flipView = new WinUI.FlipView { SelectedIndex = fv.SelectedIndex };
        foreach (var item in fv.Items)
        {
            var ctrl = Mount(item, requestRerender);
            if (ctrl is not null) flipView.Items.Add(ctrl);
        }
        SetElementTag(flipView, fv);
        if (fv.OnSelectedIndexChanged is not null)
            flipView.SelectionChanged += (s, _) =>
            {
                var f = (WinUI.FlipView)s!;
                (GetElementTag(f) as FlipViewElement)?.OnSelectedIndexChanged?.Invoke(f.SelectedIndex);
            };
        ApplySetters(fv.Setters, flipView);
        return flipView;
    }

    internal UIElement MountTemplatedList(TemplatedListElementBase el, Action requestRerender)
    {
        return el.ControlKind switch
        {
            TemplatedControlKind.ListView => MountTemplatedListView(el, requestRerender),
            TemplatedControlKind.GridView => MountTemplatedGridView(el, requestRerender),
            TemplatedControlKind.FlipView => MountTemplatedFlipView(el, requestRerender),
            _ => throw new InvalidOperationException($"Unknown TemplatedControlKind: {el.ControlKind}")
        };
    }

    /// <summary>
    /// Shared ContainerContentChanging handler for all templated items controls.
    /// On materialize: calls viewBuilder, mounts element, stores in ContentControl.
    /// On recycle: unmounts child, clears content.
    /// </summary>
    private void HandleTemplatedContainerContentChanging(object sender, ContainerContentChangingEventArgs args, Action requestRerender)
    {
        if (args.InRecycleQueue)
        {
            if (args.ItemContainer.ContentTemplateRoot is ContentControl oldCc)
            {
                if (oldCc.Content is UIElement oldCtrl)
                    UnmountChild(oldCtrl);
                oldCc.Content = null;
                ClearElementTag(oldCc);
            }
            return;
        }

        args.Handled = true;
        // §14 Phase 3 close-out: prefer the descriptor-stashed view source
        // (TemplatedItems<> strategy path) over the legacy element-based
        // fallback. The two implementations are interchangeable through
        // IItemViewSource — only the resolution order matters here.
        Internal.IItemViewSource? viewSource = GetItemViewSource((UIElement)sender!)
            ?? GetElementTag((UIElement)sender!) as TemplatedListElementBase;
        if (viewSource is not null && args.ItemIndex >= 0 && args.ItemIndex < viewSource.ItemCount
            && args.ItemContainer.ContentTemplateRoot is ContentControl cc)
        {
            var itemElement = viewSource.BuildItemView(args.ItemIndex);
            var ctrl = Mount(itemElement, requestRerender);
            cc.Content = ctrl;
            SetElementTag(cc, itemElement); // Store for later reconciliation

            // Spec 042 §6 — if this container is materializing a row that
            // the keyed diff tagged as inserted under an active
            // Animations.Animate transaction, fire a one-shot enter
            // animation on the realized container and clear the tag so the
            // next recycle/materialize cycle doesn't replay it.
            if (args.Item is ReactorRow row && row.PendingEnterAnimation is { } kind)
            {
                row.PendingEnterAnimation = null;
                ApplyAmbientEnterAnimation(args.ItemContainer, kind);
            }
        }
    }

    /// <summary>
    /// Spec 042 §6 — apply a default fade-up enter animation to a
    /// container freshly realized under an <see cref="Animations.Animate"/>
    /// transaction. Uses the same per-container Composition path resolved
    /// by Q4 (not the shared <c>ListView.ItemContainerTransitions</c>
    /// collection) so concurrent transactions don't clobber each other. The element
    /// developer's <c>.Transition(...)</c> modifier still wins when set —
    /// this only fires when no per-element transition has been declared.
    /// </summary>
    internal static void ApplyAmbientEnterAnimation(UIElement container, AnimationKind kind)
    {
        var curve = AnimationKindMap.ToCurve(kind);
        if (curve is null) return;

        try
        {
            var visual = global::Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(container);
            var compositor = visual.Compositor;

            // Set initial state, then animate to final. Opacity carries the
            // fade-in; a small Y offset carries the slide-up so the row
            // visibly emerges rather than just appearing. Both targets use
            // the same curve so they stay phase-locked.
            visual.Opacity = 0f;
            var prevOffset = visual.Offset;
            visual.Offset = new global::System.Numerics.Vector3(prevOffset.X, prevOffset.Y + 12f, prevOffset.Z);

            var opacityAnim = AnimationHelper.CreateScalarTargetAnimation(compositor, 1.0f, curve);
            visual.StartAnimation("Opacity", opacityAnim);

            var offsetAnim = AnimationHelper.CreateVector3TargetAnimation(compositor, prevOffset, curve);
            visual.StartAnimation("Offset", offsetAnim);
        }
        catch
        {
            // Composition can throw in headless / disposing contexts.
            // Animation is non-critical — correctness is preserved.
        }
    }

    private WinUI.ListView MountTemplatedListView(TemplatedListElementBase el, Action requestRerender)
    {
        var listView = new WinUI.ListView
        {
            SelectionMode = el.GetSelectionMode(),
            IsItemClickEnabled = el.GetIsItemClickEnabled(),
        };
        var header = el.GetHeader();
        if (header is not null) listView.Header = header;

        SetElementTag(listView, el);
        listView.ItemTemplate = SharedContentControlTemplate.Value;

        listView.ContainerContentChanging += (sender, args) =>
            HandleTemplatedContainerContentChanging(sender, args, requestRerender);

        listView.SelectionChanged += (s, _) =>
        {
            var l = (WinUI.ListView)s!;
            if (GetElementTag(l) is not TemplatedListElementBase tel) return;
            tel.InvokeSelectionChanged(l.SelectedIndex);
            if (tel.HasMultiSelectionCallback)
            {
                var snapshot = new List<int>(l.SelectedItems.Count);
                foreach (var item in l.SelectedItems)
                {
                    // Spec 042 Phase 1: items in SelectedItems are
                    // ReactorRow when the OC delta path is active; preserve
                    // the legacy int path for the rare direct-int consumer.
                    if (item is ReactorRow row) snapshot.Add(row.Index);
                    else if (item is int i) snapshot.Add(i);
                }
                tel.InvokeMultiSelectionChanged(snapshot);
            }
        };
        listView.ItemClick += (s, args) =>
        {
            var l = (WinUI.ListView)s!;
            // ClickedItem is the OC element — ReactorRow under the delta
            // path; int under the legacy path. Translate to the data index
            // either way.
            int? idx = args.ClickedItem switch
            {
                ReactorRow row => row.Index,
                int i => i,
                _ => null,
            };
            if (idx is int v)
                (GetElementTag(l) as TemplatedListElementBase)?.InvokeItemClick(v);
        };

        // Spec 042 Phase 1: bind to an internally-owned
        // ObservableCollection<ReactorRow> so insert/remove/move surface
        // as INotifyCollectionChanged deltas — WinUI animates only the
        // affected containers rather than re-realizing the entire viewport.
        var listState = BuildListStateFor(el);
        SetListState(listView, listState);
        listView.ItemsSource = listState.Source;

        var selectedIndex = el.GetSelectedIndex();
        if (selectedIndex >= 0) listView.SelectedIndex = selectedIndex;
        el.ApplyControlSetters(listView);
        return listView;
    }

    /// <summary>
    /// Builds a fresh <see cref="ReactorListState"/> populated with the
    /// element's current keys. Used at mount time and at bulk-replace
    /// bailout time. Tolerates duplicate keys per <see cref="ReactorListState.Reset"/>;
    /// the bailout path is where duplicate-key diagnostics surface (see
    /// <see cref="KeyedListDiff"/>).
    /// </summary>
    private static ReactorListState BuildListStateFor(TemplatedListElementBase el)
    {
        var state = new ReactorListState();
        int n = el.ItemCount;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, el.GetKeyAt(i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }

    private static ReactorListState BuildListStateForLazy(LazyStackElementBase lazy)
    {
        var state = new ReactorListState();
        int n = lazy.ItemCount;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, lazy.GetKeyAt(i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }

    private WinUI.GridView MountTemplatedGridView(TemplatedListElementBase el, Action requestRerender)
    {
        var gridView = new WinUI.GridView
        {
            SelectionMode = el.GetSelectionMode(),
            IsItemClickEnabled = el.GetIsItemClickEnabled(),
        };
        var header = el.GetHeader();
        if (header is not null) gridView.Header = header;

        SetElementTag(gridView, el);
        gridView.ItemTemplate = SharedContentControlTemplate.Value;

        gridView.ContainerContentChanging += (sender, args) =>
            HandleTemplatedContainerContentChanging(sender, args, requestRerender);

        gridView.SelectionChanged += (s, _) =>
        {
            var g = (WinUI.GridView)s!;
            if (GetElementTag(g) is not TemplatedListElementBase tel) return;
            tel.InvokeSelectionChanged(g.SelectedIndex);
            if (tel.HasMultiSelectionCallback)
            {
                var snapshot = new List<int>(g.SelectedItems.Count);
                foreach (var item in g.SelectedItems)
                {
                    if (item is ReactorRow row) snapshot.Add(row.Index);
                    else if (item is int i) snapshot.Add(i);
                }
                tel.InvokeMultiSelectionChanged(snapshot);
            }
        };
        gridView.ItemClick += (s, args) =>
        {
            var g = (WinUI.GridView)s!;
            int? idx = args.ClickedItem switch
            {
                ReactorRow row => row.Index,
                int i => i,
                _ => null,
            };
            if (idx is int v)
                (GetElementTag(g) as TemplatedListElementBase)?.InvokeItemClick(v);
        };

        var gridState = BuildListStateFor(el);
        SetListState(gridView, gridState);
        gridView.ItemsSource = gridState.Source;

        var selectedIndex = el.GetSelectedIndex();
        if (selectedIndex >= 0) gridView.SelectedIndex = selectedIndex;
        el.ApplyControlSetters(gridView);
        return gridView;
    }

    private WinUI.FlipView MountTemplatedFlipView(TemplatedListElementBase el, Action requestRerender)
    {
        var flipView = new WinUI.FlipView();

        SetElementTag(flipView, el);

        // FlipView doesn't support ContainerContentChanging (not a ListViewBase).
        // Pre-mount all items — FlipView typically has few items so this is fine.
        for (int i = 0; i < el.ItemCount; i++)
        {
            var itemElement = el.BuildItemView(i);
            var ctrl = Mount(itemElement, requestRerender);
            if (ctrl is not null) flipView.Items.Add(ctrl);
        }

        flipView.SelectionChanged += (s, _) =>
        {
            var f = (WinUI.FlipView)s!;
            (GetElementTag(f) as TemplatedListElementBase)?.InvokeSelectionChanged(f.SelectedIndex);
        };

        var selectedIndex = el.GetSelectedIndex();
        if (selectedIndex >= 0) flipView.SelectedIndex = selectedIndex;
        el.ApplyControlSetters(flipView);
        return flipView;
    }

    private WinUI.InfoBar MountInfoBar(InfoBarElement ib, Action requestRerender)
    {
        var infoBar = new WinUI.InfoBar
        {
            Title = ib.Title ?? "", Message = ib.Message ?? "",
            Severity = ib.Severity, IsOpen = ib.IsOpen, IsClosable = ib.IsClosable,
        };
        if (ib.IconSource is not null) infoBar.IconSource = ResolveIconSource(ib.IconSource);
        if (ib.Content is not null) infoBar.Content = Mount(ib.Content, requestRerender);
        // Tag the parent InfoBar first so both the Closed handler (wired on
        // infoBar) and the ActionButton handler (captures infoBar in its
        // closure, reads Tag from there) dispatch through a Tag that the
        // Update/skip paths will keep refreshed.
        SetElementTag(infoBar, ib);
        if (ib.ActionButtonContent is not null)
        {
            infoBar.ActionButton = new WinUI.Button { Content = ib.ActionButtonContent };
            if (ib.OnActionButtonClick is not null)
                ((WinUI.Button)infoBar.ActionButton).Click += (_, _) =>
                    (GetElementTag(infoBar) as InfoBarElement)?.OnActionButtonClick?.Invoke();
        }
        if (ib.OnClosed is not null)
            infoBar.Closed += (s, _) => (GetElementTag((UIElement)s!) as InfoBarElement)?.OnClosed?.Invoke();
        ApplySetters(ib.Setters, infoBar);
        return infoBar;
    }

    private WinUI.InfoBadge MountInfoBadge(InfoBadgeElement badge)
    {
        var ib = _pool.TryRent(typeof(WinUI.InfoBadge)) as WinUI.InfoBadge ?? new WinUI.InfoBadge();
        if (badge.Value.HasValue) ib.Value = badge.Value.Value;
        ApplySetters(badge.Setters, ib);
        return ib;
    }



    private static void AddCommandHostAccelerators(WinUI.Grid host, Command[] commands)
    {
        // Suppress WinUI's auto-generated chord tooltip on the host Grid. Without
        // this, accelerators registered on the host (which wraps the entire app)
        // propagate as ambient keyboard hints — hovering ANY descendant (a step
        // prompt textbox, say) flashes the parent's chord ("Ctrl+O") as a tooltip
        // on the descendant. Setting Hidden on the host stops the auto-generation
        // at the source and is invisible to users (the chord is still announced
        // by command-bound buttons that opt back in via their own tooltip).
        if (commands.Length > 0)
            host.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;

        foreach (var cmd in commands)
        {
            if (cmd.Accelerator is null) continue;
            var ka = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
            {
                Key = cmd.Accelerator.Key,
                Modifiers = cmd.Accelerator.Modifiers,
            };
            var command = cmd;
            ka.Invoked += (s, e) =>
            {
                // Scope check: only fire if focus is within this CommandHost subtree
                var xamlRoot = host.XamlRoot;
                if (xamlRoot is null) return;
                var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
                if (focused is null || !IsDescendantOf(focused, host))
                {
                    // Don't mark handled — let other handlers process it
                    return;
                }

                e.Handled = true;
                if (command.IsEnabled)
                    command.Execute?.Invoke();
            };
            host.KeyboardAccelerators.Add(ka);
        }
    }

    private static bool IsDescendantOf(DependencyObject element, DependencyObject ancestor)
    {
        var current = element;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }
        return false;
    }



    /// <summary>Descriptor-accessible bridge to <see cref="ResolveIcon"/>
    /// for the icon-bearing controls ported in Phase 3 (e.g.
    /// AutoSuggestBox.QueryIcon). Static so it can be invoked from a
    /// descriptor lambda without a Reconciler instance.</summary>
    internal static WinUI.IconElement? ResolveIconForDescriptor(IconData? iconData)
        => ResolveIcon(iconData, null);

    private static WinUI.IconElement? ResolveIcon(IconData? iconData, string? iconSymbol)
    {
        if (iconData is not null)
        {
            return iconData switch
            {
                SymbolIconData sym => ResolveIconString(sym.Symbol) ?? new WinUI.SymbolIcon(Symbol.Placeholder),
                FontIconData fi => CreateFontIcon(fi),
                BitmapIconData bi => new WinUI.BitmapIcon { UriSource = bi.Source, ShowAsMonochrome = bi.ShowAsMonochrome },
                PathIconData pi => CreatePathIcon(pi),
                ImageIconData ii => new WinUI.ImageIcon { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(ii.Source) },
                _ => null,
            };
        }
        if (iconSymbol is not null) return ResolveIconString(iconSymbol);
        return null;
    }

    // Handles both Symbol enum names ("Home", "Edit") and raw Segoe Fluent
    // glyphs (""). A Symbol enum mismatch used to collapse to
    // Symbol.Placeholder, which rendered as a diamond — fall through to a
    // FontIcon with SymbolThemeFontFamily so glyph strings render correctly.
    private static WinUI.IconElement? ResolveIconString(string iconSymbol)
    {
        if (string.IsNullOrEmpty(iconSymbol)) return null;
        if (Enum.TryParse<Symbol>(iconSymbol, ignoreCase: true, out var symbol))
            return new WinUI.SymbolIcon(symbol);
        // Treat as a Segoe Fluent / MDL2 glyph codepoint.
        return new WinUI.FontIcon
        {
            Glyph = iconSymbol,
            FontFamily = Microsoft.UI.Xaml.Application.Current?.Resources["SymbolThemeFontFamily"] as Microsoft.UI.Xaml.Media.FontFamily
                         ?? new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
        };
    }

    // IconSource counterpart for controls (TabView, etc.) that take an
    // IconSource instead of IconElement. Same glyph-fallback semantics as
    // ResolveIconString.
    internal static WinUI.IconSource? ResolveIconSource(string? iconSymbol)
    {
        if (string.IsNullOrEmpty(iconSymbol)) return null;
        if (Enum.TryParse<Symbol>(iconSymbol, ignoreCase: true, out var symbol))
            return new WinUI.SymbolIconSource { Symbol = symbol };
        return new WinUI.FontIconSource
        {
            Glyph = iconSymbol,
            FontFamily = Microsoft.UI.Xaml.Application.Current?.Resources["SymbolThemeFontFamily"] as Microsoft.UI.Xaml.Media.FontFamily
                         ?? new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
        };
    }

    /// <summary>
    /// Strongly-typed <see cref="IconData"/> → <see cref="WinUI.IconSource"/>
    /// projection. Used by controls that expose an <c>IconSource</c> slot
    /// (TitleBar, TabView, etc.). Returns null on unknown subtypes so the
    /// caller can fall through to the string-glyph overload.
    /// </summary>
    internal static WinUI.IconSource? ResolveIconSource(IconData? iconData) => iconData switch
    {
        null => null,
        SymbolIconData sym => ResolveIconSource(sym.Symbol),
        FontIconData fi => new WinUI.FontIconSource
        {
            Glyph = fi.Glyph,
            FontFamily = fi.FontFamily is null ? null! : WinRTCache.GetFontFamily(fi.FontFamily),
            FontSize = fi.FontSize ?? double.NaN,
        },
        BitmapIconData bi => new WinUI.BitmapIconSource { UriSource = bi.Source, ShowAsMonochrome = bi.ShowAsMonochrome },
        PathIconData pi => CreatePathIconSource(pi),
        ImageIconData ii => new WinUI.ImageIconSource
        {
            ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(ii.Source),
        },
        _ => null,
    };

    private static WinUI.PathIconSource? CreatePathIconSource(PathIconData pi)
    {
        var src = new WinUI.PathIconSource();
        if (Microsoft.UI.Xaml.Markup.XamlReader.Load(
            $"<Geometry xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>{pi.Data}</Geometry>")
            is Microsoft.UI.Xaml.Media.Geometry geo)
        {
            src.Data = geo;
        }
        return src;
    }

    private static WinUI.FontIcon CreateFontIcon(FontIconData fi)
    {
        var icon = new WinUI.FontIcon { Glyph = fi.Glyph };
        if (fi.FontFamily is not null) icon.FontFamily = WinRTCache.GetFontFamily(fi.FontFamily);
        if (fi.FontSize.HasValue) icon.FontSize = fi.FontSize.Value;
        return icon;
    }

    private static WinUI.PathIcon CreatePathIcon(PathIconData pi)
    {
        var icon = new WinUI.PathIcon();
        if (Microsoft.UI.Xaml.Markup.XamlReader.Load(
            $"<Geometry xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>{pi.Data}</Geometry>")
            is Microsoft.UI.Xaml.Media.Geometry geo)
        {
            icon.Data = geo;
        }
        return icon;
    }

    // ════════════════════════════════════════════════════════════════
    //  Validation elements
    // ════════════════════════════════════════════════════════════════

    private WinUI.StackPanel MountFormField(FormFieldElement ff, Action requestRerender)
    {
        var panel = new WinUI.StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };

        // Resolve field name from explicit or auto-detected from Content's ValidationAttached
        var fieldName = FormFieldHelpers.ResolveFieldName(ff.FieldName, ff.Content);

        // Auto-validate: if Content has attached validators with a Value, run them now
        var attached = ff.Content.GetAttached<ValidationAttached>();
        var valCtx = _contextScope.Read(ValidationContexts.Current);
        if (valCtx is not null && attached is not null && attached.Validators.Length > 0)
        {
            ValidationReconciler.ValidateAttached(valCtx, attached, attached.Value);
        }

        // [0] Label — always present, collapsed when empty
        var displayLabel = FormFieldHelpers.GetDisplayLabel(ff.Label, ff.Required);
        var labelTb = new TextBlock
        {
            Text = displayLabel,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Visibility = displayLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        panel.Children.Add(labelTb);

        // [1] Content (the actual form control) — always present
        var contentControl = Mount(ff.Content, requestRerender);
        if (contentControl is not null)
        {
            ApplyFormFieldAutomation(contentControl, ff.Label);
            ApplyFormFieldErrorStyling(contentControl, valCtx, fieldName, ff.ShowWhen);
            panel.Children.Add(contentControl);
        }
        else
        {
            // Placeholder so indices stay fixed
            panel.Children.Add(new WinUI.StackPanel { Visibility = Visibility.Collapsed });
        }

        // [2] Description/error text — always present, collapsed when empty
        var descTb = new TextBlock { FontSize = 12 };
        ApplyFormFieldDescription(descTb, valCtx, fieldName, ff.Description, ff.ShowWhen);
        panel.Children.Add(descTb);

        SetElementTag(panel, ff);
        return panel;
    }

    private static void ApplyFormFieldAutomation(UIElement contentControl, string? label)
    {
        var automationName = FormFieldHelpers.GetAutomationName(label);
        if (automationName is not null && contentControl is FrameworkElement cfe)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cfe, automationName);
    }

    private static void ApplyFormFieldErrorStyling(
        UIElement contentControl, ValidationContext? valCtx, string? fieldName, ShowWhen showWhen)
    {
        if (contentControl is not WinUI.Control ctrl)
            return;

        if (valCtx is not null && fieldName is not null)
        {
            var severity = valCtx.HighestSeverity(fieldName);
            if (severity is not null && ErrorStyling.ShouldShowErrors(valCtx, fieldName, showWhen))
            {
                var brushKey = ErrorStyling.GetBrushKey(severity.Value);
                var brush = ThemeRef.Resolve(brushKey, ctrl);
                if (brush is not null)
                {
                    ctrl.BorderBrush = brush;
                    ctrl.BorderThickness = ErrorStyling.ErrorBorderThickness;
                }
                return;
            }
        }

        // Clear error styling — reset to default
        ctrl.ClearValue(WinUI.Control.BorderBrushProperty);
        ctrl.ClearValue(WinUI.Control.BorderThicknessProperty);
    }

    private static void ApplyFormFieldDescription(
        TextBlock descTb, ValidationContext? valCtx, string? fieldName,
        string? description, ShowWhen showWhen)
    {
        var (descText, isError) = FormFieldHelpers.GetDescriptionOrError(
            valCtx, fieldName, description, showWhen);

        if (descText is null)
        {
            descTb.Text = "";
            descTb.Visibility = Visibility.Collapsed;
            return;
        }

        descTb.Text = descText;
        descTb.Visibility = Visibility.Visible;
        descTb.Opacity = 1.0;

        if (isError)
        {
            var errorBrush = ThemeRef.Resolve(ErrorStyling.ErrorBrushKey, descTb);
            descTb.Foreground = errorBrush
                ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
        }
        else
        {
            descTb.ClearValue(TextBlock.ForegroundProperty);
            descTb.Opacity = 0.6;
        }
    }

    private WinUI.StackPanel MountValidationVisualizer(
        ValidationVisualizerElement vv, Action requestRerender)
    {
        var panel = new WinUI.StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        var valCtx = _contextScope.Read(ValidationContexts.Current);

        // Mount the content subtree first
        var contentControl = Mount(vv.Content, requestRerender);

        // Collect messages from the validation context
        var allMessages = valCtx?.GetAllMessages() ?? (IReadOnlyList<ValidationMessage>)[];
        var (caught, _) = ErrorBubbling.FilterMessages(allMessages, vv.SeverityFilter);
        var shouldDisplay = ErrorBubbling.ShouldDisplay(caught, vv.ShowWhen, valCtx);

        switch (vv.Style)
        {
            case VisualizerStyle.InfoBar when shouldDisplay && caught.Count > 0:
            {
                var severity = ErrorBubbling.HighestSeverity(caught);
                var infoBarSeverity = severity switch
                {
                    Severity.Error => InfoBarSeverity.Error,
                    Severity.Warning => InfoBarSeverity.Warning,
                    _ => InfoBarSeverity.Informational,
                };
                var infoBar = new WinUI.InfoBar
                {
                    Title = vv.Title ?? (severity == Severity.Error ? "Errors" : "Warnings"),
                    Message = string.Join("\n", caught.Select(m => m.Text)),
                    Severity = infoBarSeverity,
                    IsOpen = true,
                    IsClosable = false,
                };
                panel.Children.Add(infoBar);
                break;
            }
            case VisualizerStyle.Summary when shouldDisplay && caught.Count > 0:
            {
                if (vv.Title is not null)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = vv.Title,
                        FontSize = 13,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    });
                }
                foreach (var msg in caught)
                {
                    var bullet = new TextBlock
                    {
                        Text = $"• {msg.Text}",
                        FontSize = 12,
                    };
                    var brush = ThemeRef.Resolve(ErrorStyling.GetBrushKey(msg.Severity), bullet);
                    if (brush is not null) bullet.Foreground = brush;
                    panel.Children.Add(bullet);
                }
                break;
            }
            case VisualizerStyle.Custom when shouldDisplay && vv.CustomRender is not null:
            {
                var customElement = vv.CustomRender(caught);
                var customControl = Mount(customElement, requestRerender);
                if (customControl is not null)
                    panel.Children.Add(customControl);
                break;
            }
            case VisualizerStyle.Inline when shouldDisplay && caught.Count > 0:
            {
                // Inline errors rendered after the content below
                break;
            }
        }

        // Add the content control
        if (contentControl is not null)
            panel.Children.Add(contentControl);

        // Inline error text below the content
        if (vv.Style == VisualizerStyle.Inline && shouldDisplay && caught.Count > 0)
        {
            var errorText = string.Join(" • ", caught.Select(m => m.Text));
            var errorTb = new TextBlock { Text = errorText, FontSize = 12 };
            var brush = ThemeRef.Resolve(ErrorStyling.ErrorBrushKey, errorTb);
            if (brush is not null)
                errorTb.Foreground = brush;
            else
                errorTb.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Red);
            panel.Children.Add(errorTb);
        }

        SetElementTag(panel, vv);
        return panel;
    }

    private UIElement MountValidationRule(ValidationRuleElement rule)
    {
        // Evaluate the rule against the nearest ValidationContext
        var valCtx = _contextScope.Read(ValidationContexts.Current);
        if (valCtx is not null)
            rule.Evaluate(valCtx);

        // Return a collapsed placeholder — validation rules produce no UI
        var placeholder = new WinUI.StackPanel { Visibility = Visibility.Collapsed };
        SetElementTag(placeholder, rule);
        return placeholder;
    }

    private UIElement MountErrorBoundary(ErrorBoundaryElement eb, Action requestRerender)
    {
        var wrapper = new Border();
        Element renderedElement;
        Exception? caughtEx = null;

        _errorBoundaryDepth++;
        try
        {
            renderedElement = eb.Child;
            wrapper.Child = Mount(eb.Child, requestRerender);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ErrorBoundary caught render error");
            caughtEx = ex;
            renderedElement = eb.Fallback(ex);
            wrapper.Child = Mount(renderedElement, requestRerender);
        }
        finally
        {
            _errorBoundaryDepth--;
        }

        _errorBoundaryNodes[wrapper] = new ErrorBoundaryNode
        {
            ChildElement = eb.Child,
            RenderedElement = renderedElement,
            CaughtException = caughtEx,
            Fallback = eb.Fallback,
        };

        return wrapper;
    }

    private UIElement MountComponent(ComponentElement compElement, Action requestRerender)
    {
        var component = compElement.CreateInstance();

        if (compElement.Props is not null && component is IPropsReceiver receiver)
            receiver.SetProps(compElement.Props);

        // Each component gets its own Border wrapper as an identity anchor
        // in _componentNodes, preventing key collisions when components nest.
        var wrapper = new Border();
        var node = new ComponentNode
        {
            Component = component, RenderedElement = null, Element = compElement,
            PreviousProps = compElement.Props,
        };
        _componentNodes[wrapper] = node;

        // Layout-cost bookkeeping: gated by the flag so the GetType().Name
        // call, the event raise, and the depth-counter increments are all
        // skipped entirely when the overlay is off. Cache `trackLC` once so
        // the matching decrement in `finally` agrees even if the flag flips
        // mid-mount.
        bool trackLC = ReactorFeatureFlags.ShowLayoutCost;
        if (trackLC)
        {
            RaiseLayoutCostComponentMounted(wrapper, component.GetType().Name);
            _layoutCostComponentDepth++;
        }

        // Pass the component's own wrapped rerender to children so that child state
        // changes propagate SelfTriggered up through all component ancestors.
        var componentRerender = CreateComponentRerender(node, requestRerender);

        Element childElement;
        try
        {
            component.Context.BeginRender(componentRerender, _contextScope);
            childElement = component.Render();
            component.Context.FlushEffects();
        }
        catch (Exception ex) when (_errorBoundaryDepth == 0 && ex is not OutOfMemoryException and not StackOverflowException)
        {
            _logger?.LogError(ex, "Component Render() threw during mount: {ComponentName}", compElement.GetType().Name);
            childElement = ErrorFallback.BuildElement(ex);
        }
        UIElement? childControl;
        try
        {
            childControl = Mount(childElement, componentRerender);
        }
        finally { if (trackLC) _layoutCostComponentDepth--; }

        wrapper.Child = childControl;
        node.RenderedElement = childElement;
        return wrapper;
    }

    private UIElement MountFuncComponent(FuncElement funcElement, Action requestRerender)
    {
        var ctx = new RenderContext();
        var wrapper = new Border();
        var node = new ComponentNode
        {
            Context = ctx, RenderedElement = null, Element = funcElement,
        };
        _componentNodes[wrapper] = node;

        bool trackLC = ReactorFeatureFlags.ShowLayoutCost;
        if (trackLC)
        {
            RaiseLayoutCostComponentMounted(wrapper, funcElement.GetType().Name);
            _layoutCostComponentDepth++;
        }

        // Pass the component's own wrapped rerender to children so that child state
        // changes propagate SelfTriggered up through all component ancestors.
        var componentRerender = CreateComponentRerender(node, requestRerender);

        Element childElement;
        try
        {
            ctx.BeginRender(componentRerender, _contextScope);
            childElement = funcElement.RenderFunc(ctx);
            ctx.FlushEffects();
        }
        catch (Exception ex) when (_errorBoundaryDepth == 0 && ex is not OutOfMemoryException and not StackOverflowException)
        {
            _logger?.LogError(ex, "FuncComponent Render() threw during mount");
            childElement = ErrorFallback.BuildElement(ex);
        }
        UIElement? childControl;
        try
        {
            childControl = Mount(childElement, componentRerender);
        }
        finally { if (trackLC) _layoutCostComponentDepth--; }

        wrapper.Child = childControl;
        node.RenderedElement = childElement;
        return wrapper;
    }

    private UIElement MountMemoComponent(MemoElement memoElement, Action requestRerender)
    {
        var ctx = new RenderContext();
        var wrapper = new Border();
        var node = new ComponentNode
        {
            Context = ctx, RenderedElement = null, Element = memoElement,
            MemoDependencies = memoElement.Dependencies,
        };
        _componentNodes[wrapper] = node;

        bool trackLC = ReactorFeatureFlags.ShowLayoutCost;
        if (trackLC)
        {
            RaiseLayoutCostComponentMounted(wrapper, memoElement.GetType().Name);
            _layoutCostComponentDepth++;
        }

        // Pass the component's own wrapped rerender to children so that child state
        // changes propagate SelfTriggered up through all component ancestors.
        var componentRerender = CreateComponentRerender(node, requestRerender);

        Element childElement;
        try
        {
            ctx.BeginRender(componentRerender, _contextScope);
            childElement = memoElement.RenderFunc(ctx);
            ctx.FlushEffects();
        }
        catch (Exception ex) when (_errorBoundaryDepth == 0 && ex is not OutOfMemoryException and not StackOverflowException)
        {
            _logger?.LogError(ex, "MemoComponent Render() threw during mount");
            childElement = ErrorFallback.BuildElement(ex);
        }
        UIElement? childControl;
        try
        {
            childControl = Mount(childElement, componentRerender);
        }
        finally { if (trackLC) _layoutCostComponentDepth--; }

        wrapper.Child = childControl;
        node.RenderedElement = childElement;
        return wrapper;
    }

    /// <summary>
    /// Spec 047 §14 Phase 3 finish — Port (7). Legacy mount arm for
    /// <see cref="ItemsRepeaterElement{T}"/> / <see cref="ItemsRepeaterElementBase"/>.
    /// Mirrors <see cref="MountLazyStack"/> but without the
    /// <see cref="WinUI.ScrollViewer"/> wrap and without imposing a
    /// StackLayout — the element supplies its own <see cref="WinUI.Layout"/>
    /// (or null, meaning "leave on the WinUI default").
    /// </summary>
    private WinUI.ItemsRepeater MountItemsRepeater(ItemsRepeaterElementBase ir, Action requestRerender)
    {
        var repeater = new WinUI.ItemsRepeater();
        if (ir.Layout is not null) repeater.Layout = ir.Layout;

        var listState = BuildListStateForItemsRepeater(ir);
        SetListState(repeater, listState);
        repeater.ItemsSource = listState.Source;
        var factory = ir.CreateFactory(this, requestRerender, _pool);
        ir.AttachListStateToFactory(factory, listState);
        repeater.ItemTemplate = factory;
        SetElementTag(repeater, ir);
        ApplySetters(ir.RepeaterSetters, repeater);
        return repeater;
    }

    private static Internal.ReactorListState BuildListStateForItemsRepeater(ItemsRepeaterElementBase ir)
    {
        var state = new Internal.ReactorListState();
        int n = ir.ItemCount;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, ((Internal.IKeyedItemSource)ir).GetKeyAt(i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }

    internal UIElement MountLazyStack(LazyStackElementBase lazy, Action requestRerender)
    {
        var repeater = new WinUI.ItemsRepeater();

        repeater.Layout = new WinUI.StackLayout
        {
            Orientation = lazy.Orientation,
            Spacing = lazy.Spacing,
        };

        // Spec 042 Phase 1: bind the repeater to an internally-owned
        // ObservableCollection<ReactorRow>. Without this, every Items.Count
        // change replaced the int-range source wholesale and the
        // ItemsRepeater re-realized every visible child.
        var listState = BuildListStateForLazy(lazy);
        SetListState(repeater, listState);
        repeater.ItemsSource = listState.Source;
        var factory = lazy.CreateFactory(this, requestRerender, _pool);
        // Plumb the list state into the factory so its _mountedElements
        // dictionary is keyed by ReactorRow.Key (reorder-stable) instead
        // of by realized index.
        lazy.AttachListStateToFactory(factory, listState);
        repeater.ItemTemplate = factory;
        SetElementTag(repeater, lazy);
        ApplySetters(lazy.RepeaterSetters, repeater);

        var sv = _pool.TryRent(typeof(WinUI.ScrollViewer)) as WinUI.ScrollViewer ?? new WinUI.ScrollViewer();
        sv.Content = repeater;
        sv.HorizontalScrollBarVisibility = lazy.Orientation == Microsoft.UI.Xaml.Controls.Orientation.Horizontal
            ? WinUI.ScrollBarVisibility.Auto
            : WinUI.ScrollBarVisibility.Disabled;
        sv.VerticalScrollBarVisibility = lazy.Orientation == Microsoft.UI.Xaml.Controls.Orientation.Vertical
            ? WinUI.ScrollBarVisibility.Auto
            : WinUI.ScrollBarVisibility.Disabled;
        SetElementTag(sv, lazy);
        ApplySetters(lazy.ScrollViewerSetters, sv);

        return sv;
    }

    // ── ItemsView ───────────────────────────────────────────────────────

    /// <summary>
    /// Translate the user-facing layout-kind enum to a real WinUI
    /// <see cref="WinUI.Layout"/>. All three layouts live under
    /// <c>Microsoft.UI.Xaml.Controls</c>; <c>StackLayout</c> here is the
    /// virtualizing layout (not the panel of the same name).
    /// </summary>
    private static WinUI.Layout BuildItemsViewLayout(ItemsViewLayoutKind kind) => kind switch
    {
        ItemsViewLayoutKind.LinedFlowLayout => new WinUI.LinedFlowLayout
        {
            LineSpacing = 4,
            MinItemSpacing = 4,
        },
        // Leave MinItemWidth / MinItemHeight at the WinUI default of 0.
        // The layout then measures the first realized item and applies
        // that size uniformly to the rest — far less likely to clip
        // user content than picking arbitrary minimums. Users who want
        // explicit cell sizing can override via .Set(iv => iv.Layout =
        // new UniformGridLayout { MinItemWidth = ..., MinItemHeight = ... }).
        ItemsViewLayoutKind.UniformGridLayout => new WinUI.UniformGridLayout
        {
            MinRowSpacing = 4,
            MinColumnSpacing = 4,
        },
        _ => new WinUI.StackLayout { Spacing = 4 },
    };

    private UIElement MountItemsView(ItemsViewElementBase iv, Action requestRerender)
    {
        // Run the user's viewBuilder for index 0 up front so a missing
        // ItemContainer wrap surfaces here — on the caller's stack —
        // instead of inside WinUI's realize loop where it would hang the
        // measure pass.
        iv.PreflightFirstItem();

        var control = new WinUI.ItemsView
        {
            Layout = BuildItemsViewLayout(iv.LayoutKind),
            SelectionMode = iv.SelectionMode,
            IsItemInvokedEnabled = iv.IsItemInvokedEnabled,
        };

        // ItemsView is a ScrollView + ItemsRepeater under the hood. We feed
        // it the same OC<ReactorRow> source / ElementFactory<T> pair used
        // by LazyVStack — the framework drives realize/recycle through
        // GetElement/RecycleElement, the factory mounts/unmounts via the
        // reconciler. See ElementFactory.cs for the bridge contract.
        //
        // Caller contract: the user's viewBuilder MUST return an
        // <see cref="ItemContainerElement"/> at the root. MountItemContainer
        // is the only Mount path that produces a WinUI ItemContainer,
        // which ItemsView's selection / focus / animation infrastructure
        // requires (without it, the inner ItemsRepeater enters an infinite
        // measure cycle — see microsoft-ui-xaml-lift/controls/dev/
        // ItemsView/ItemsView.cpp:317).
        var listState = BuildListStateForItemsView(iv);
        SetListState(control, listState);
        control.ItemsSource = listState.Source;
        var factory = iv.CreateFactory(this, requestRerender, _pool);
        iv.AttachListStateToFactory(factory, listState);
        control.ItemTemplate = factory;

        SetElementTag(control, iv);

        // ItemsView raises ItemInvoked/SelectionChanged with ReactorRow
        // payloads (because ItemsSource is OC<ReactorRow>). Translate the
        // row → original-list index inside the trampoline so the user's
        // handler sees their own T.
        control.ItemInvoked += (s, e) =>
        {
            var c = (WinUI.ItemsView)s!;
            if (GetElementTag(c) is not ItemsViewElementBase current) return;
            if (e.InvokedItem is ReactorRow row)
                current.InvokeItemInvoked(row.Index);
        };
        control.SelectionChanged += (s, e) =>
        {
            var c = (WinUI.ItemsView)s!;
            if (GetElementTag(c) is not ItemsViewElementBase current) return;
            var selected = c.SelectedItems;
            if (selected is null || selected.Count == 0)
            {
                current.InvokeSelectionChanged(global::System.Array.Empty<int>());
                return;
            }
            var indices = new List<int>(selected.Count);
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] is ReactorRow row) indices.Add(row.Index);
            }
            current.InvokeSelectionChanged(indices);
        };

        ApplySetters(iv.Setters, control);
        return control;
    }

    private UIElement MountItemContainer(ItemContainerElement ic, Action requestRerender)
    {
        var container = new WinUI.ItemContainer { IsSelected = ic.IsSelected };
        if (ic.Child is not null)
        {
            var childCtrl = Mount(ic.Child, requestRerender);
            container.Child = childCtrl;
        }
        SetElementTag(container, ic);
        ApplySetters(ic.Setters, container);
        return container;
    }

    private static ReactorListState BuildListStateForItemsView(ItemsViewElementBase iv)
    {
        var state = new ReactorListState();
        int n = iv.ItemCount;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, iv.GetKeyAt(i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }

    // ── Shape elements ──────────────────────────────────────────────────

    private WinShapes.Rectangle MountRectangle(RectangleElement rect)
    {
        var r = new WinShapes.Rectangle();
        if (rect.Fill is not null) r.Fill = rect.Fill;
        if (rect.Stroke is not null) r.Stroke = rect.Stroke;
        if (rect.StrokeThickness > 0) r.StrokeThickness = rect.StrokeThickness;
        if (rect.RadiusX > 0) r.RadiusX = rect.RadiusX;
        if (rect.RadiusY > 0) r.RadiusY = rect.RadiusY;
        ApplySetters(rect.Setters, r);
        return r;
    }

    private WinShapes.Ellipse MountEllipse(EllipseElement ell)
    {
        var e = new WinShapes.Ellipse();
        if (ell.Fill is not null) e.Fill = ell.Fill;
        if (ell.Stroke is not null) e.Stroke = ell.Stroke;
        if (ell.StrokeThickness > 0) e.StrokeThickness = ell.StrokeThickness;
        ApplySetters(ell.Setters, e);
        return e;
    }

    private WinShapes.Line MountLine(LineElement ln)
    {
        var l = new WinShapes.Line { X1 = ln.X1, Y1 = ln.Y1, X2 = ln.X2, Y2 = ln.Y2 };
        if (ln.Stroke is not null) l.Stroke = ln.Stroke;
        if (ln.StrokeThickness > 0) l.StrokeThickness = ln.StrokeThickness;
        ApplySetters(ln.Setters, l);
        return l;
    }

    private WinShapes.Path MountPath(PathElement pa)
    {
        // Prefer constructing the Path via WinUI's native XAML parser when a path
        // data string is available. Hand-built PathGeometry trees can trip
        // Path.Data validation ("Value does not fall within the expected range")
        // even when geometrically valid, and a Geometry already attached to one
        // Path cannot be re-parented to another. Building the Path + Geometry
        // together via XamlReader avoids both pitfalls.
        WinShapes.Path? p = null;
        global::System.Exception? xamlReaderError = null;
        string? attemptedXaml = null;
        if (pa.PathDataString is { Length: > 0 } pds)
        {
            try
            {
                var safe = global::System.Net.WebUtility.HtmlEncode(pds);
                attemptedXaml =
                    "<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\""
                    + safe + "\" />";
                p = Microsoft.UI.Xaml.Markup.XamlReader.Load(attemptedXaml) as WinShapes.Path;
            }
            catch (global::System.Exception ex)
            {
                xamlReaderError = ex;
            }
        }

        if (p is null)
        {
            p = new WinShapes.Path();
            if (pa.Data is not null)
            {
                try { p.Data = pa.Data; }
                catch (global::System.Exception ex)
                {
                    var xamlNote = xamlReaderError is not null
                        ? $" XamlReader.Load also failed: {xamlReaderError.GetType().Name}: {xamlReaderError.Message}. Attempted XAML: {attemptedXaml}"
                        : " (XamlReader.Load returned non-Path or wasn't attempted)";
                    throw new global::System.ArgumentException(
                        $"Path.Data rejected by WinUI. PathDataString={pa.PathDataString ?? "(null)"}; "
                        + $"DataType={pa.Data.GetType().Name}; inner={ex.Message}.{xamlNote}", ex);
                }
            }
            else if (pa.PathDataString is { Length: > 0 } pdsFallback)
            {
                // XamlReader.Load failed (or returned non-Path) and no pre-built Geometry
                // was supplied. Fall back to our own parser so a non-empty PathDataString
                // never silently mounts as an empty Path. If parsing also fails, surface
                // both errors together so the next regression has actionable context.
                global::System.Exception? parserError = null;
                try { p.Data = global::Microsoft.UI.Reactor.Charting.PathDataParser.Parse(pdsFallback); }
                catch (global::System.Exception ex) { parserError = ex; }

                if (parserError is not null)
                {
                    var xamlNote = xamlReaderError is not null
                        ? $"XamlReader.Load failed: {xamlReaderError.GetType().Name}: {xamlReaderError.Message}. Attempted XAML: {attemptedXaml}. "
                        : "XamlReader.Load returned non-Path. ";
                    throw new global::System.ArgumentException(
                        $"Could not mount PathElement from PathDataString='{pdsFallback}'. "
                        + xamlNote
                        + $"PathDataParser.Parse also failed: {parserError.GetType().Name}: {parserError.Message}.",
                        parserError);
                }
            }
        }

        if (pa.Fill is not null) p.Fill = pa.Fill;
        if (pa.Stroke is not null) p.Stroke = pa.Stroke;
        if (pa.StrokeThickness > 0) p.StrokeThickness = pa.StrokeThickness;
        if (pa.StrokeDashArray is not null) p.StrokeDashArray = pa.StrokeDashArray;
        if (pa.RenderTransform is not null) p.RenderTransform = pa.RenderTransform;
        if (pa.StrokeStartLineCap != Microsoft.UI.Xaml.Media.PenLineCap.Flat) p.StrokeStartLineCap = pa.StrokeStartLineCap;
        if (pa.StrokeEndLineCap != Microsoft.UI.Xaml.Media.PenLineCap.Flat) p.StrokeEndLineCap = pa.StrokeEndLineCap;
        if (pa.StrokeLineJoin != Microsoft.UI.Xaml.Media.PenLineJoin.Miter) p.StrokeLineJoin = pa.StrokeLineJoin;
        if (pa.StrokeMiterLimit != 10) p.StrokeMiterLimit = pa.StrokeMiterLimit;
        if (pa.StrokeDashCap != Microsoft.UI.Xaml.Media.PenLineCap.Flat) p.StrokeDashCap = pa.StrokeDashCap;
        if (pa.StrokeDashOffset != 0) p.StrokeDashOffset = pa.StrokeDashOffset;
        // WinUI's Shapes.Path doesn't expose FillRule directly — it lives on
        // the PathGeometry. Cast and set when we own a writable PathGeometry.
        if (pa.FillRule != Microsoft.UI.Xaml.Media.FillRule.EvenOdd
            && p.Data is Microsoft.UI.Xaml.Media.PathGeometry pg)
            pg.FillRule = pa.FillRule;
        ApplySetters(pa.Setters, p);
        return p;
    }

    // ── RelativePanel ───────────────────────────────────────────────────

    internal WinUI.RelativePanel MountRelativePanel(RelativePanelElement rp, Action requestRerender)
    {
        var panel = new WinUI.RelativePanel();
        var nameMap = new Dictionary<string, UIElement>();

        // First pass: mount all children and register names
        foreach (var child in rp.Children)
        {
            var ctrl = Mount(child, requestRerender);
            if (ctrl is null) continue;
            var rpa = child.GetAttached<RelativePanelAttached>();
            if (rpa is not null && ctrl is FrameworkElement fe) fe.Name = rpa.Name;
            if (rpa is not null) nameMap[rpa.Name] = ctrl;
            panel.Children.Add(ctrl);
        }

        // Second pass: apply attached properties using name references
        foreach (var child in rp.Children)
        {
            var rpa = child.GetAttached<RelativePanelAttached>();
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

            if (rpa.AlignLeftWithPanel) WinUI.RelativePanel.SetAlignLeftWithPanel(ctrl, true);
            if (rpa.AlignRightWithPanel) WinUI.RelativePanel.SetAlignRightWithPanel(ctrl, true);
            if (rpa.AlignTopWithPanel) WinUI.RelativePanel.SetAlignTopWithPanel(ctrl, true);
            if (rpa.AlignBottomWithPanel) WinUI.RelativePanel.SetAlignBottomWithPanel(ctrl, true);
            if (rpa.AlignHorizontalCenterWithPanel) WinUI.RelativePanel.SetAlignHorizontalCenterWithPanel(ctrl, true);
            if (rpa.AlignVerticalCenterWithPanel) WinUI.RelativePanel.SetAlignVerticalCenterWithPanel(ctrl, true);
        }

        SetElementTag(panel, rp);
        ApplySetters(rp.Setters, panel);
        return panel;
    }

    // ── MediaPlayerElement ──────────────────────────────────────────────


    private static void DispatchToElement<TElement>(FrameworkElement fe, Action<TElement> body)
        where TElement : Element
    {
        var dispatcher = fe.DispatcherQueue;
        if (dispatcher is null) return;
        dispatcher.TryEnqueue(() =>
        {
            if (GetElementTag(fe) is TElement el) body(el);
        });
    }

    // ── AnimatedVisualPlayer ────────────────────────────────────────────


    // ── SemanticZoom ────────────────────────────────────────────────────


    // ── ListBox ─────────────────────────────────────────────────────────

    private WinUI.ListBox MountListBox(ListBoxElement lb)
    {
        var listBox = new WinUI.ListBox { SelectedIndex = lb.SelectedIndex };
        foreach (var item in lb.Items) listBox.Items.Add(item);
        SetElementTag(listBox, lb);
        // Subscribe unconditionally so handlers attached on a later record-with
        // are picked up via GetElementTag — see MountListView for the rationale.
        listBox.SelectionChanged += (s, _) =>
        {
            var l = (WinUI.ListBox)s!;
            if (GetElementTag(l) is not ListBoxElement el) return;
            el.OnSelectedIndexChanged?.Invoke(l.SelectedIndex);
            if (el.OnSelectionChanged is { } h)
            {
                var snapshot = new List<int>(l.SelectedItems.Count);
                for (int i = 0; i < l.SelectedItems.Count; i++)
                {
                    var idx = l.Items.IndexOf(l.SelectedItems[i]);
                    if (idx >= 0) snapshot.Add(idx);
                }
                h(snapshot);
            }
        };
        ApplySetters(lb.Setters, listBox);
        return listBox;
    }

    // ── SelectorBar ─────────────────────────────────────────────────────

    private WinUI.SelectorBar MountSelectorBar(SelectorBarElement sb)
    {
        var selectorBar = new WinUI.SelectorBar();
        foreach (var item in sb.Items)
        {
            var sbi = new WinUI.SelectorBarItem { Text = item.Text };
            if (item.Icon is not null) sbi.Icon = new WinUI.SymbolIcon(ParseSymbol(item.Icon));
            selectorBar.Items.Add(sbi);
        }
        if (sb.SelectedIndex >= 0 && sb.SelectedIndex < selectorBar.Items.Count)
            selectorBar.SelectedItem = selectorBar.Items[sb.SelectedIndex];
        SetElementTag(selectorBar, sb);
        if (sb.OnSelectedIndexChanged is not null)
            selectorBar.SelectionChanged += (s, _) =>
            {
                var bar = (WinUI.SelectorBar)s!;
                var idx = bar.Items.IndexOf(bar.SelectedItem);
                (GetElementTag(bar) as SelectorBarElement)?.OnSelectedIndexChanged?.Invoke(idx);
            };
        ApplySetters(sb.Setters, selectorBar);
        return selectorBar;
    }

    // ── PipsPager ───────────────────────────────────────────────────────

    private WinUI.PipsPager MountPipsPager(PipsPagerElement pp)
    {
        var pager = new WinUI.PipsPager
        {
            NumberOfPages = pp.NumberOfPages,
            SelectedPageIndex = pp.SelectedPageIndex,
            WrapMode = pp.WrapMode,
            MaxVisiblePips = pp.MaxVisiblePips,
            PreviousButtonVisibility = pp.PreviousButtonVisibility,
            NextButtonVisibility = pp.NextButtonVisibility,
        };
        SetElementTag(pager, pp);
        if (pp.OnSelectedPageIndexChanged is not null)
            pager.SelectedIndexChanged += (s, _) =>
            {
                var p = (WinUI.PipsPager)s!;
                (GetElementTag(p) as PipsPagerElement)?.OnSelectedPageIndexChanged?.Invoke(p.SelectedPageIndex);
            };
        ApplySetters(pp.Setters, pager);
        return pager;
    }

    // ── AnnotatedScrollBar ──────────────────────────────────────────────


    // ── Popup ───────────────────────────────────────────────────────────


    // ── SwipeControl ──────────────────────────────────────────────────


    private static SwipeItem CreateSwipeItem(SwipeItemData data)
    {
        var si = new SwipeItem
        {
            Text = data.Text,
            BehaviorOnInvoked = data.BehaviorOnInvoked,
        };
        if (data.IconSource is not null) si.IconSource = data.IconSource;
        if (data.Background is not null) si.Background = data.Background;
        if (data.Foreground is not null) si.Foreground = data.Foreground;
        if (data.OnInvoked is not null) si.Invoked += (s, e) => data.OnInvoked();
        return si;
    }

    // ── AnimatedIcon ──────────────────────────────────────────────────

    private WinUI.AnimatedIcon MountAnimatedIcon(AnimatedIconElement ai)
    {
        var icon = new WinUI.AnimatedIcon();
        if (ai.Source is Microsoft.UI.Xaml.Controls.IAnimatedVisualSource2 src)
            icon.Source = src;
        if (ai.FallbackIconSource is not null) icon.FallbackIconSource = ai.FallbackIconSource;
        ApplySetters(ai.Setters, icon);
        return icon;
    }

    // ── Icon ─────────────────────────────────────────────────────────


    // ── ParallaxView ──────────────────────────────────────────────────


    // ── MapControl ────────────────────────────────────────────────────


    // ── Frame ─────────────────────────────────────────────────────────

    private WinUI.Frame MountFrame(FrameElement frame)
    {
        var f = new WinUI.Frame();
        SetElementTag(f, frame);

        // Subscribe unconditionally — the trampoline reads the latest element
        // via GetElementTag so a later record-with that attaches a handler
        // picks up without re-wiring.
        f.Navigated += (s, e) =>
        {
            if (GetElementTag((WinUI.Frame)s!) is FrameElement el && el.OnNavigated is { } h)
                h(e.SourcePageType);
        };
        f.Navigating += (s, e) =>
        {
            if (GetElementTag((WinUI.Frame)s!) is FrameElement el && el.OnNavigating is { } h)
                h(e.SourcePageType);
        };
        f.NavigationFailed += (s, e) =>
        {
            if (GetElementTag((WinUI.Frame)s!) is FrameElement el && el.OnNavigationFailed is { } h)
                h(e.SourcePageType, e.Exception);
        };

        if (frame.SourcePageType is not null)
            f.Navigate(frame.SourcePageType, frame.NavigationParameter);
        ApplySetters(frame.Setters, f);
        return f;
    }

    // ════════════════════════════════════════════════════════════════════
    //  XamlHostElement / XamlPageElement — built-in XAML interop
    // ════════════════════════════════════════════════════════════════════

    private static UIElement MountXamlHost(XamlHostElement host)
    {
        var control = host.Factory();
        host.Updater?.Invoke(control);
        SetElementTag(control, host);
        return control;
    }

    private static UIElement MountXamlPage(XamlPageElement page)
    {
        var frame = new WinUI.Frame();
        frame.Navigate(page.PageType, page.Parameter);
        SetElementTag(frame, page);
        return frame;
    }

    /// <summary>
    /// Mounts a zero-size hidden TextBlock for screen reader live-region announcements.
    /// The TextBlock is connected to the <see cref="AnnounceHandle"/> so that
    /// <see cref="AnnounceHandle.Announce(string)"/> can raise UIA notifications through it.
    /// </summary>
    private static UIElement MountAnnounceRegion(AnnounceRegionElement ann)
    {
        var tb = new TextBlock
        {
            Width = 0,
            Height = 0,
            Opacity = 0,
            IsHitTestVisible = false,
            IsTabStop = false,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(
            tb, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(
            tb, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        ann.Handle.SetTextBlock(tb);
        SetElementTag(tb, ann);
        return tb;
    }

    /// <summary>
    /// Mounts a SemanticPanel that provides custom automation semantics
    /// for composite Reactor components that can't override OnCreateAutomationPeer().
    /// </summary>
    private UIElement MountSemantic(SemanticElement sem, Action requestRerender)
    {
        var panel = new Accessibility.SemanticPanel();

        // Apply semantic properties
        var s = sem.Semantics;
        if (s.Role is not null) panel.SemanticRole = s.Role;
        if (s.Value is not null) panel.SemanticValue = s.Value;
        if (s.RangeMin.HasValue) panel.RangeMinimum = s.RangeMin.Value;
        if (s.RangeMax.HasValue) panel.RangeMaximum = s.RangeMax.Value;
        if (s.RangeValue.HasValue) panel.RangeValue = s.RangeValue.Value;
        panel.IsReadOnly = s.IsReadOnly;

        // Mount child inside the panel
        var childControl = Mount(sem.Child, requestRerender);
        if (childControl is not null)
            panel.Children.Add(childControl);

        SetElementTag(panel, sem);
        return panel;
    }

    private WinUI.Grid MountCommandHost(CommandHostElement ch, Action requestRerender)
    {
        var host = new WinUI.Grid();
        var child = Mount(ch.Child, requestRerender);
        if (child is not null) host.Children.Add(child);

        AddCommandHostAccelerators(host, ch.Commands);

        SetElementTag(host, ch);
        return host;
    }

    internal static WinUI.ICommandBarElement CreateAppBarItem(AppBarItemBase item)
    {
        switch (item)
        {
            case AppBarButtonData cmd:
            {
                var abb = new WinUI.AppBarButton { Label = cmd.Label };
                abb.IsEnabled = cmd.IsEnabled;
                abb.Icon = ResolveIcon(cmd.IconElement, cmd.Icon);
                if (cmd.KeyboardAccelerators is not null)
                    foreach (var ka in cmd.KeyboardAccelerators)
                        abb.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = ka.Key, Modifiers = ka.Modifiers });
                if (cmd.AccessKey is not null) abb.AccessKey = cmd.AccessKey;
                if (cmd.Description is not null)
                {
                    WinUI.ToolTipService.SetToolTip(abb, cmd.Description);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(abb, cmd.Description);
                }
                abb.Tag = cmd;
                abb.Click += (s, _) => ((AppBarButtonData)((WinUI.AppBarButton)s!).Tag!).OnClick?.Invoke();
                return abb;
            }
            case AppBarToggleButtonData toggle:
            {
                var atb = new WinUI.AppBarToggleButton { Label = toggle.Label, IsChecked = toggle.IsChecked };
                atb.Icon = ResolveIcon(toggle.IconElement, toggle.Icon);
                atb.Tag = toggle;
                atb.Checked += (s, _) => ((AppBarToggleButtonData)((WinUI.AppBarToggleButton)s!).Tag!).OnIsCheckedChanged?.Invoke(true);
                atb.Unchecked += (s, _) => ((AppBarToggleButtonData)((WinUI.AppBarToggleButton)s!).Tag!).OnIsCheckedChanged?.Invoke(false);
                return atb;
            }
            case AppBarSeparatorData:
                return new WinUI.AppBarSeparator();
            default:
                return new WinUI.AppBarSeparator();
        }
    }

    internal static WinUI.MenuFlyoutItemBase CreateMenuFlyoutItem(MenuFlyoutItemBase item)
    {
        switch (item)
        {
            case MenuFlyoutItemData mfi:
            {
                var flyoutItem = new WinUI.MenuFlyoutItem { Text = mfi.Text };
                flyoutItem.IsEnabled = mfi.IsEnabled;
                flyoutItem.Icon = ResolveIcon(mfi.IconElement, mfi.Icon);
                if (mfi.KeyboardAccelerators is not null)
                    foreach (var ka in mfi.KeyboardAccelerators)
                        flyoutItem.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = ka.Key, Modifiers = ka.Modifiers });
                if (mfi.AccessKey is not null) flyoutItem.AccessKey = mfi.AccessKey;
                if (mfi.Description is not null)
                {
                    WinUI.ToolTipService.SetToolTip(flyoutItem, mfi.Description);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(flyoutItem, mfi.Description);
                }
                flyoutItem.Tag = mfi;
                flyoutItem.Click += (s, _) => ((MenuFlyoutItemData)((WinUI.MenuFlyoutItem)s!).Tag!).OnClick?.Invoke();
                return flyoutItem;
            }
            case ToggleMenuFlyoutItemData toggle:
            {
                var toggleItem = new WinUI.ToggleMenuFlyoutItem { Text = toggle.Text, IsChecked = toggle.IsChecked };
                toggleItem.Icon = ResolveIcon(toggle.IconElement, toggle.Icon);
                toggleItem.Tag = toggle;
                toggleItem.Click += (s, _) =>
                {
                    var ti = (WinUI.ToggleMenuFlyoutItem)s!;
                    ((ToggleMenuFlyoutItemData)ti.Tag!).OnIsCheckedChanged?.Invoke(ti.IsChecked);
                };
                return toggleItem;
            }
            case RadioMenuFlyoutItemData radio:
            {
                var radioItem = new WinUI.RadioMenuFlyoutItem { Text = radio.Text, GroupName = radio.GroupName, IsChecked = radio.IsChecked };
                radioItem.Tag = radio;
                radioItem.Click += (s, _) => ((RadioMenuFlyoutItemData)((WinUI.RadioMenuFlyoutItem)s!).Tag!).OnClick?.Invoke();
                return radioItem;
            }
            case MenuFlyoutSeparatorData:
                return new WinUI.MenuFlyoutSeparator();
            case MenuFlyoutSubItemData sub:
            {
                var subItem = new WinUI.MenuFlyoutSubItem { Text = sub.Text };
                subItem.Icon = ResolveIcon(sub.IconElement, sub.Icon);
                foreach (var child in sub.Items) subItem.Items.Add(CreateMenuFlyoutItem(child));
                return subItem;
            }
            default:
                return new WinUI.MenuFlyoutSeparator();
        }
    }
}
