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

        // Registered types checked first
        if (_typeRegistry.TryGetValue(element.GetType(), out var reg))
        {
            control = reg.Mount(element, requestRerender, this);
        }
        else
        {
        control = element switch
        {
            TextBlockElement text => MountText(text),
            RichTextBlockElement richText => MountRichTextBlock(richText),
            ButtonElement btn => MountButton(btn, requestRerender),
            HyperlinkButtonElement hlBtn => MountHyperlinkButton(hlBtn),
            RepeatButtonElement repBtn => MountRepeatButton(repBtn),
            ToggleButtonElement togBtn => MountToggleButton(togBtn),
            DropDownButtonElement ddBtn => MountDropDownButton(ddBtn, requestRerender),
            SplitButtonElement spBtn => MountSplitButton(spBtn, requestRerender),
            ToggleSplitButtonElement tspBtn => MountToggleSplitButton(tspBtn, requestRerender),
            RichEditBoxElement reb => MountRichEditBox(reb),
            TextFieldElement tf => MountTextField(tf, requestRerender),
            PasswordBoxElement pw => MountPasswordBox(pw),
            NumberBoxElement nb => MountNumberBox(nb),
            AutoSuggestBoxElement asb => MountAutoSuggestBox(asb),
            CheckBoxElement cb => MountCheckBox(cb),
            RadioButtonElement rb => MountRadioButton(rb),
            RadioButtonsElement rbs => MountRadioButtons(rbs),
            ComboBoxElement combo => MountComboBox(combo, requestRerender),
            SliderElement sl => MountSlider(sl),
            ToggleSwitchElement ts => MountToggleSwitch(ts),
            RatingControlElement rc => MountRatingControl(rc),
            ColorPickerElement cp => MountColorPicker(cp),
            CalendarDatePickerElement cdp => MountCalendarDatePicker(cdp),
            DatePickerElement dp => MountDatePicker(dp),
            TimePickerElement tp => MountTimePicker(tp),
            ProgressElement prog => MountProgress(prog),
            ProgressRingElement ring => MountProgressRing(ring),
            ImageElement img => MountImage(img),
            PersonPictureElement pp => MountPersonPicture(pp),
            WebView2Element wv => MountWebView2(wv),
            WrapGridElement wg => MountWrapGrid(wg, requestRerender),
            StackElement stack => MountStack(stack, requestRerender),
            GridElement grid => MountGrid(grid, requestRerender),
            ScrollViewElement scroll => MountScrollView(scroll, requestRerender),
            BorderElement border => MountBorder(border, requestRerender),
            ExpanderElement exp => MountExpander(exp, requestRerender),
            SplitViewElement sv => MountSplitView(sv, requestRerender),
            ViewboxElement vb => MountViewbox(vb, requestRerender),
            CanvasElement cvs => MountCanvas(cvs, requestRerender),
            FlexElement flex => MountFlex(flex, requestRerender),
            NavigationHostElement navHost => MountNavigationHost(navHost, requestRerender),
            NavigationViewElement nav => MountNavigationView(nav, requestRerender),
            TitleBarElement tb => MountTitleBar(tb, requestRerender),
            TabViewElement tab => MountTabView(tab, requestRerender),
            BreadcrumbBarElement bcb => MountBreadcrumbBar(bcb),
            PivotElement pvt => MountPivot(pvt, requestRerender),
            ListViewElement lv => MountListView(lv, requestRerender),
            GridViewElement gv => MountGridView(gv, requestRerender),
            TreeViewElement tv => MountTreeView(tv, requestRerender),
            FlipViewElement fv => MountFlipView(fv, requestRerender),
            InfoBarElement ib => MountInfoBar(ib, requestRerender),
            InfoBadgeElement badge => MountInfoBadge(badge),
            ContentDialogElement cdEl => MountContentDialog(cdEl, requestRerender),
            FlyoutElement flyEl => MountFlyout(flyEl, requestRerender),
            TeachingTipElement ttEl => MountTeachingTip(ttEl, requestRerender),
            MenuBarElement mbEl => MountMenuBar(mbEl),
            CommandBarElement cmdEl => MountCommandBar(cmdEl, requestRerender),
            MenuFlyoutElement mfEl => MountMenuFlyout(mfEl, requestRerender),
            TemplatedListElementBase tl => MountTemplatedList(tl, requestRerender),
            LazyStackElementBase lazy => MountLazyStack(lazy, requestRerender),
            RectangleElement rect => MountRectangle(rect),
            EllipseElement ell => MountEllipse(ell),
            LineElement ln => MountLine(ln),
            PathElement pa => MountPath(pa),
            RelativePanelElement rp => MountRelativePanel(rp, requestRerender),
            MediaPlayerElementElement mpe => MountMediaPlayerElement(mpe),
            AnimatedVisualPlayerElement avp => MountAnimatedVisualPlayer(avp),
            SemanticZoomElement sz => MountSemanticZoom(sz, requestRerender),
            ListBoxElement lb => MountListBox(lb),
            SelectorBarElement sb => MountSelectorBar(sb),
            PipsPagerElement pp => MountPipsPager(pp),
            AnnotatedScrollBarElement asb => MountAnnotatedScrollBar(asb),
            PopupElement popup => MountPopup(popup, requestRerender),
            RefreshContainerElement rc => MountRefreshContainer(rc, requestRerender),
            CommandBarFlyoutElement cbf => MountCommandBarFlyout(cbf, requestRerender),
            CalendarViewElement cv => MountCalendarView(cv),
            SwipeControlElement swipe => MountSwipeControl(swipe, requestRerender),
            AnimatedIconElement ai => MountAnimatedIcon(ai),
            ParallaxViewElement pv => MountParallaxView(pv, requestRerender),
            MapControlElement mc => MountMapControl(mc),
            FrameElement frame => MountFrame(frame),
            CommandHostElement ch => MountCommandHost(ch, requestRerender),
            ErrorBoundaryElement eb => MountErrorBoundary(eb, requestRerender),
            Validation.FormFieldElement ff => MountFormField(ff, requestRerender),
            Validation.ValidationVisualizerElement vv => MountValidationVisualizer(vv, requestRerender),
            Validation.ValidationRuleElement rule => MountValidationRule(rule),
            SemanticElement sem => MountSemantic(sem, requestRerender),
            AnnounceRegionElement ann => MountAnnounceRegion(ann),
            XamlHostElement host => MountXamlHost(host),
            XamlPageElement page => MountXamlPage(page),
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
        if (richText.Paragraphs is not null)
        {
            foreach (var para in richText.Paragraphs)
            {
                var p = new Microsoft.UI.Xaml.Documents.Paragraph();
                foreach (var inline in para.Inlines)
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
                            p.Inlines.Add(r);
                            break;
                        case RichTextHyperlink link:
                            var hl = new Microsoft.UI.Xaml.Documents.Hyperlink();
                            try { hl.NavigateUri = link.NavigateUri; }
                            catch { hl.NavigateUri = new Uri("about:error"); }
                            hl.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = link.Text });
                            p.Inlines.Add(hl);
                            break;
                        case RichTextLineBreak:
                            p.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
                            break;
                    }
                }
                rtb.Blocks.Add(p);
            }
        }
        else
        {
            var paragraph = new Microsoft.UI.Xaml.Documents.Paragraph();
            paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = richText.Text });
            rtb.Blocks.Add(paragraph);
        }
        if (richText.FontSize.HasValue) rtb.FontSize = richText.FontSize.Value;
        if (richText.MaxLines > 0) rtb.MaxLines = richText.MaxLines;
        if (richText.LineHeight.HasValue) rtb.LineHeight = richText.LineHeight.Value;
        if (richText.TextAlignment.HasValue) rtb.TextAlignment = richText.TextAlignment.Value;
        if (richText.TextTrimming.HasValue) rtb.TextTrimming = richText.TextTrimming.Value;
        if (richText.CharacterSpacing != 0) rtb.CharacterSpacing = richText.CharacterSpacing;
        ApplySetters(richText.Setters, rtb);
        return rtb;
    }

    private WinUI.Button MountButton(ButtonElement btn, Action requestRerender)
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
    /// the control hasn't been wired yet. The CWT flag survives pool round-
    /// trips (Button is a poolable type, and ElementPool intentionally retains
    /// event subscriptions across rent/return cycles).
    /// </summary>
    internal static void EnsureButtonWiring(WinUI.Button button, ButtonElement btn)
    {
        if (btn.OnClick is null) return;
        var flags = GetPoolableWireFlags(button);
        if (flags.ButtonClick) return;
        flags.ButtonClick = true;
        button.Click += (s, _) =>
        {
            if (GetElementTag((UIElement)s!) is ButtonElement live)
            {
                if (live.IsDisabledFocusable) return;
                live.OnClick?.Invoke();
            }
        };
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

    private WinUI.ToggleSplitButton MountToggleSplitButton(ToggleSplitButtonElement tspBtn, Action requestRerender)
    {
        var tsb = new WinUI.ToggleSplitButton { Content = tspBtn.Label, IsChecked = tspBtn.IsChecked };
        SetElementTag(tsb, tspBtn);
        if (tspBtn.OnIsCheckedChanged is not null)
            tsb.IsCheckedChanged += (s, _) =>
            {
                var t = (WinUI.ToggleSplitButton)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(t)) return;
                (GetElementTag(t) as ToggleSplitButtonElement)?.OnIsCheckedChanged?.Invoke(t.IsChecked);
            };
        if (tspBtn.Flyout is not null)
            tsb.Flyout = CreateFlyoutFromElement(tspBtn.Flyout, requestRerender);
        ApplySetters(tspBtn.Setters, tsb);
        return tsb;
    }

    private TextBox MountTextField(TextFieldElement tf, Action requestRerender)
    {
        var rented = _pool.TryRent(typeof(TextBox));
        var textBox = rented as TextBox ?? new TextBox();
        // SetElementTag BEFORE writing Text: pooled controls retain their
        // previous tag, and programmatic Text= fires TextChanged on the pooled
        // event handler. Setting the new tag first ensures the handler reads
        // this mount's element, not the pool's last owner. The BeginSuppress
        // guard below is additional belt-and-suspenders against echo.
        SetElementTag(textBox, tf);
        // AcceptsReturn and TextWrapping must be set BEFORE Text. WinUI TextBox
        // defaults to single-line mode (AcceptsReturn=false); assigning Text
        // with embedded \r\n while in single-line mode silently strips the
        // newlines, keeping only the first paragraph. Setting these first
        // ensures multi-line content round-trips correctly on mount.
        if (tf.AcceptsReturn == true) textBox.AcceptsReturn = true;
        if (tf.TextWrapping.HasValue) textBox.TextWrapping = tf.TextWrapping.Value;
        if (rented is not null && textBox.Text != tf.Value)
            ChangeEchoSuppressor.BeginSuppress(textBox);
        textBox.Text = tf.Value;
        textBox.PlaceholderText = tf.Placeholder ?? "";
        if (tf.Header is not null) textBox.Header = tf.Header;
        if (tf.IsReadOnly == true) textBox.IsReadOnly = true;
        if (tf.SelectionStart.HasValue) textBox.SelectionStart = tf.SelectionStart.Value;
        if (tf.SelectionLength.HasValue) textBox.SelectionLength = tf.SelectionLength.Value;
        if (tf.MaxLength != 0) textBox.MaxLength = tf.MaxLength;
        if (tf.IsSpellCheckEnabled.HasValue) textBox.IsSpellCheckEnabled = tf.IsSpellCheckEnabled.Value;
        if (tf.CharacterCasing != CharacterCasing.Normal) textBox.CharacterCasing = tf.CharacterCasing;
        if (tf.TextAlignment != TextAlignment.Left) textBox.TextAlignment = tf.TextAlignment;
        if (tf.Description is not null) textBox.Description = tf.Description;
        EnsureTextFieldWiring(textBox, tf, requestRerender);
        ApplySetters(tf.Setters, textBox);
        return textBox;
    }

    /// <summary>
    /// Wires TextField's TextChanged/SelectionChanged trampolines only when the
    /// element has a corresponding handler AND the control hasn't been wired
    /// yet. Called from both Mount (fresh or pooled) and Update (null→non-null
    /// transition). The CWT flags survive pool round-trips.
    /// </summary>
    internal static void EnsureTextFieldWiring(TextBox textBox, TextFieldElement tf, Action requestRerender)
    {
        if (tf.OnChanged is null && tf.OnSelectionChanged is null) return;
        var flags = GetPoolableWireFlags(textBox);
        if (tf.OnChanged is not null && !flags.TextBoxTextChanged)
        {
            flags.TextBoxTextChanged = true;
            textBox.TextChanged += (_, _) =>
            {
                if (ChangeEchoSuppressor.ShouldSuppress(textBox)) return;
                var tag = GetElementTag(textBox) as TextFieldElement;
                tag?.OnChanged?.Invoke(textBox.Text);
                // Controlled input: when onChange is wired, always request a
                // re-render so UpdateTextField can enforce the controlled value.
                // Coalesces with any setState re-render (CAS gate).
                // Without onChange the field is uncontrolled — no snap-back.
                if (tag?.OnChanged is not null)
                    requestRerender();
            };
        }
        if (tf.OnSelectionChanged is not null && !flags.TextBoxSelectionChanged)
        {
            flags.TextBoxSelectionChanged = true;
            textBox.SelectionChanged += (_, _) => (GetElementTag(textBox) as TextFieldElement)?.OnSelectionChanged?.Invoke(textBox.SelectedText, textBox.SelectionStart, textBox.SelectionLength);
        }
    }

    private WinUI.PasswordBox MountPasswordBox(PasswordBoxElement pw)
    {
        var pb = new WinUI.PasswordBox
        {
            Password = pw.Password,
            PlaceholderText = pw.PlaceholderText ?? "",
            PasswordRevealMode = pw.PasswordRevealMode,
        };
        if (pw.Header is not null) pb.Header = pw.Header;
        if (pw.MaxLength != 0) pb.MaxLength = pw.MaxLength;
        if (pw.PasswordChar is not null) pb.PasswordChar = pw.PasswordChar;
        SetElementTag(pb, pw);
        if (pw.OnPasswordChanged is not null)
            pb.PasswordChanged += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                (GetElementTag(c) as PasswordBoxElement)?.OnPasswordChanged?.Invoke(((WinUI.PasswordBox)c).Password);
            };
        ApplySetters(pw.Setters, pb);
        return pb;
    }

    private WinUI.NumberBox MountNumberBox(NumberBoxElement nb)
    {
        var numBox = new WinUI.NumberBox
        {
            Value = nb.Value, Minimum = nb.Minimum, Maximum = nb.Maximum,
            SmallChange = nb.SmallChange, LargeChange = nb.LargeChange,
            PlaceholderText = nb.PlaceholderText ?? "",
            SpinButtonPlacementMode = nb.SpinButtonPlacement,
            AcceptsExpression = nb.AcceptsExpression,
            ValidationMode = nb.ValidationMode,
        };
        if (nb.NumberFormatter is not null) numBox.NumberFormatter = nb.NumberFormatter;
        if (nb.Description is not null) numBox.Description = nb.Description;
        if (nb.Header is not null) numBox.Header = nb.Header;
        SetElementTag(numBox, nb);
        if (nb.OnValueChanged is not null)
            numBox.ValueChanged += (s, _) =>
            {
                var box = (WinUI.NumberBox)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(box)) return;
                (GetElementTag(box) as NumberBoxElement)?.OnValueChanged?.Invoke(box.Value);
            };
        // Per-keystroke value fire for Immediate-mode controls. Registered
        // unconditionally so that toggling .Immediate() between renders works
        // without re-mounting — the handler re-checks the marker each fire.
        numBox.RegisterPropertyChangedCallback(WinUI.NumberBox.TextProperty,
            NumberBoxImmediateTextChanged);
        ApplySetters(nb.Setters, numBox);
        return numBox;
    }

    private static readonly Microsoft.UI.Xaml.DependencyPropertyChangedCallback NumberBoxImmediateTextChanged =
        (sender, _) =>
        {
            if (sender is not WinUI.NumberBox box) return;
            if (GetElementTag(box) is not NumberBoxElement el) return;
            if (el.OnValueChanged is null) return;
            if (el.GetAttached<Microsoft.UI.Reactor.Controls.Validation.ImmediateValueAttached>() is null) return;
            if (!double.TryParse(box.Text,
                global::System.Globalization.NumberStyles.Float,
                global::System.Globalization.CultureInfo.CurrentCulture, out var parsed)) return;
            // Reject NaN/±Infinity — double.TryParse accepts the literal strings
            // "NaN"/"Infinity" by default, and NaN comparisons are never equal,
            // so the sync-guard below would let them through.
            if (!double.IsFinite(parsed)) return;
            if (parsed < el.Minimum || parsed > el.Maximum) return;
            if (parsed == el.Value) return; // already in sync; suppresses post-programmatic-write callback
            el.OnValueChanged.Invoke(parsed);
        };

    private WinUI.AutoSuggestBox MountAutoSuggestBox(AutoSuggestBoxElement asb)
    {
        var box = new WinUI.AutoSuggestBox { Text = asb.Text, PlaceholderText = asb.PlaceholderText ?? "" };
        if (asb.Suggestions.Length > 0) box.ItemsSource = asb.Suggestions;
        if (asb.Header is not null) box.Header = asb.Header;
        if (asb.QueryIcon is not null) box.QueryIcon = ResolveIcon(asb.QueryIcon, null);
        if (asb.IsSuggestionListOpen) box.IsSuggestionListOpen = true;
        SetElementTag(box, asb);
        if (asb.OnTextChanged is not null)
            box.TextChanged += (s, args) =>
            {
                if (args.Reason == WinUI.AutoSuggestionBoxTextChangeReason.UserInput)
                    (GetElementTag((UIElement)s!) as AutoSuggestBoxElement)?.OnTextChanged?.Invoke(((WinUI.AutoSuggestBox)s!).Text);
            };
        if (asb.OnQuerySubmitted is not null)
            box.QuerySubmitted += (s, args) =>
                (GetElementTag((UIElement)s!) as AutoSuggestBoxElement)?.OnQuerySubmitted?.Invoke(args.QueryText);
        if (asb.OnSuggestionChosen is not null)
            box.SuggestionChosen += (s, args) =>
                (GetElementTag((UIElement)s!) as AutoSuggestBoxElement)?.OnSuggestionChosen?.Invoke(args.SelectedItem?.ToString() ?? "");
        ApplySetters(asb.Setters, box);
        return box;
    }

    private WinUI.CheckBox MountCheckBox(CheckBoxElement cb)
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

    private WinUI.RadioButton MountRadioButton(RadioButtonElement rb)
    {
        var radio = new WinUI.RadioButton { Content = rb.Label, IsChecked = rb.IsChecked };
        if (rb.GroupName is not null) radio.GroupName = rb.GroupName;
        SetElementTag(radio, rb);
        if (rb.OnIsCheckedChanged is not null)
        {
            radio.Checked += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                (GetElementTag(c) as RadioButtonElement)?.OnIsCheckedChanged?.Invoke(true);
            };
            radio.Unchecked += (s, _) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                (GetElementTag(c) as RadioButtonElement)?.OnIsCheckedChanged?.Invoke(false);
            };
        }
        ApplySetters(rb.Setters, radio);
        return radio;
    }

    private WinUI.RadioButtons MountRadioButtons(RadioButtonsElement rbs)
    {
        var rbGroup = new WinUI.RadioButtons { SelectedIndex = rbs.SelectedIndex };
        if (rbs.Header is not null) rbGroup.Header = rbs.Header;
        foreach (var item in rbs.Items) rbGroup.Items.Add(item);
        SetElementTag(rbGroup, rbs);
        if (rbs.OnSelectedIndexChanged is not null)
            rbGroup.SelectionChanged += (s, _) =>
            {
                var g = (WinUI.RadioButtons)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(g)) return;
                (GetElementTag(g) as RadioButtonsElement)?.OnSelectedIndexChanged?.Invoke(g.SelectedIndex);
            };
        ApplySetters(rbs.Setters, rbGroup);
        return rbGroup;
    }

    private WinUI.ComboBox MountComboBox(ComboBoxElement combo, Action requestRerender)
    {
        var cb = new WinUI.ComboBox
        {
            SelectedIndex = combo.SelectedIndex,
            PlaceholderText = combo.PlaceholderText ?? "",
            IsEditable = combo.IsEditable,
        };
        if (combo.Header is not null) cb.Header = combo.Header;
        if (!double.IsNaN(combo.MaxDropDownHeight)) cb.MaxDropDownHeight = combo.MaxDropDownHeight;
        if (combo.Description is not null) cb.Description = combo.Description;
        if (combo.ItemElements is { } elements)
            foreach (var el in elements) cb.Items.Add(Mount(el, requestRerender));
        else
            foreach (var item in combo.Items) cb.Items.Add(item);
        SetElementTag(cb, combo);
        if (combo.OnSelectedIndexChanged is not null)
            cb.SelectionChanged += (s, _) =>
            {
                var c = (WinUI.ComboBox)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                (GetElementTag(c) as ComboBoxElement)?.OnSelectedIndexChanged?.Invoke(c.SelectedIndex);
            };
        if (combo.OnDropDownOpened is not null)
            cb.DropDownOpened += (s, _) => (GetElementTag((UIElement)s!) as ComboBoxElement)?.OnDropDownOpened?.Invoke();
        if (combo.OnDropDownClosed is not null)
            cb.DropDownClosed += (s, _) => (GetElementTag((UIElement)s!) as ComboBoxElement)?.OnDropDownClosed?.Invoke();
        ApplySetters(combo.Setters, cb);
        return cb;
    }

    private WinUI.Slider MountSlider(SliderElement sl)
    {
        var slider = new WinUI.Slider
        {
            Value = sl.Value, Minimum = sl.Min, Maximum = sl.Max, StepFrequency = sl.StepFrequency,
            Orientation = sl.Orientation,
            TickFrequency = sl.TickFrequency,
            TickPlacement = sl.TickPlacement,
            SnapsTo = sl.SnapsTo,
            IsThumbToolTipEnabled = sl.IsThumbToolTipEnabled,
        };
        if (sl.Header is not null) slider.Header = sl.Header;
        SetElementTag(slider, sl);
        if (sl.OnValueChanged is not null)
            slider.ValueChanged += (_, args) =>
            {
                if (ChangeEchoSuppressor.ShouldSuppress(slider)) return;
                (GetElementTag(slider) as SliderElement)?.OnValueChanged?.Invoke(args.NewValue);
            };
        ApplySetters(sl.Setters, slider);
        return slider;
    }

    private WinUI.ToggleSwitch MountToggleSwitch(ToggleSwitchElement ts)
    {
        var rented = _pool.TryRent(typeof(WinUI.ToggleSwitch));
        var toggle = rented as WinUI.ToggleSwitch ?? new WinUI.ToggleSwitch();
        // SetElementTag BEFORE IsOn= so a pooled control's retained handler
        // sees this mount's element, not the pool's last owner. Suppress the
        // echo fired by the programmatic IsOn write when it actually changes.
        SetElementTag(toggle, ts);
        if (rented is not null && toggle.IsOn != ts.IsOn)
            ChangeEchoSuppressor.BeginSuppress(toggle);
        toggle.IsOn = ts.IsOn;
        toggle.OnContent = ts.OnContent;
        toggle.OffContent = ts.OffContent;
        if (ts.Header is not null) toggle.Header = ts.Header;
        EnsureToggleSwitchWiring(toggle, ts);
        ApplySetters(ts.Setters, toggle);
        return toggle;
    }

    // Dedupe via EventHandlerState (attached on ReactorAttached.StateProperty,
    // keyed by native DependencyObject identity). A plain CWT keyed by managed
    // RCW identity is unsafe — two RCWs over the same DO miss the dedupe and
    // double-subscribe, fanning one Toggled into multiple user-callback invocations.
    internal static void EnsureToggleSwitchWiring(WinUI.ToggleSwitch toggle, ToggleSwitchElement ts)
    {
        if (ts.OnIsOnChanged is null) return;
        var state = GetOrCreateEventState(toggle);
        if (state.ToggleSwitchToggledTrampoline is not null) return;
        state.ToggleSwitchToggledTrampoline = (s, _) =>
        {
            var t = (WinUI.ToggleSwitch)s!;
            if (ChangeEchoSuppressor.ShouldSuppress(t)) return;
            (GetElementTag(t) as ToggleSwitchElement)?.OnIsOnChanged?.Invoke(t.IsOn);
        };
        toggle.Toggled += state.ToggleSwitchToggledTrampoline;
    }

    private WinUI.RatingControl MountRatingControl(RatingControlElement rc)
    {
        var rating = new WinUI.RatingControl
        {
            Value = rc.Value,
            MaxRating = rc.MaxRating,
            IsReadOnly = rc.IsReadOnly,
            Caption = rc.Caption ?? "",
            PlaceholderValue = rc.PlaceholderValue,
            InitialSetValue = rc.InitialSetValue,
        };
        SetElementTag(rating, rc);
        if (rc.OnValueChanged is not null)
            rating.ValueChanged += (s, _) =>
            {
                var r = (WinUI.RatingControl)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(r)) return;
                (GetElementTag(r) as RatingControlElement)?.OnValueChanged?.Invoke(r.Value);
            };
        ApplySetters(rc.Setters, rating);
        return rating;
    }

    private WinUI.ColorPicker MountColorPicker(ColorPickerElement cp)
    {
        var picker = new WinUI.ColorPicker
        {
            Color = cp.Color, IsAlphaEnabled = cp.IsAlphaEnabled, IsMoreButtonVisible = cp.IsMoreButtonVisible,
            IsColorSpectrumVisible = cp.IsColorSpectrumVisible, IsColorSliderVisible = cp.IsColorSliderVisible,
            IsColorChannelTextInputVisible = cp.IsColorChannelTextInputVisible, IsHexInputVisible = cp.IsHexInputVisible,
            ColorSpectrumShape = cp.ColorSpectrumShape,
            MinHue = cp.MinHue, MaxHue = cp.MaxHue,
            MinSaturation = cp.MinSaturation, MaxSaturation = cp.MaxSaturation,
            MinValue = cp.MinValue, MaxValue = cp.MaxValue,
        };
        SetElementTag(picker, cp);
        if (cp.OnColorChanged is not null)
            picker.ColorChanged += (s, args) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                (GetElementTag(c) as ColorPickerElement)?.OnColorChanged?.Invoke(args.NewColor);
            };
        ApplySetters(cp.Setters, picker);
        return picker;
    }

    private WinUI.CalendarDatePicker MountCalendarDatePicker(CalendarDatePickerElement cdp)
    {
        var cal = new WinUI.CalendarDatePicker { Date = cdp.Date, PlaceholderText = cdp.PlaceholderText ?? "" };
        if (cdp.Header is not null) cal.Header = cdp.Header;
        if (cdp.MinDate.HasValue) cal.MinDate = cdp.MinDate.Value;
        if (cdp.MaxDate.HasValue) cal.MaxDate = cdp.MaxDate.Value;
        if (cdp.DateFormat is not null) cal.DateFormat = cdp.DateFormat;
        cal.IsTodayHighlighted = cdp.IsTodayHighlighted;
        cal.IsGroupLabelVisible = cdp.IsGroupLabelVisible;
        if (cdp.IsCalendarOpen) cal.IsCalendarOpen = true;
        SetElementTag(cal, cdp);
        if (cdp.OnDateChanged is not null)
            cal.DateChanged += (s, _) =>
            {
                var c = (WinUI.CalendarDatePicker)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                (GetElementTag(c) as CalendarDatePickerElement)?.OnDateChanged?.Invoke(c.Date);
            };
        ApplySetters(cdp.Setters, cal);
        return cal;
    }

    private WinUI.DatePicker MountDatePicker(DatePickerElement dp)
    {
        var picker = new WinUI.DatePicker
        {
            Date = dp.Date,
            DayVisible = dp.DayVisible,
            MonthVisible = dp.MonthVisible,
            YearVisible = dp.YearVisible,
            Orientation = dp.Orientation,
        };
        if (dp.Header is not null) picker.Header = dp.Header;
        if (dp.MinYear.HasValue) picker.MinYear = dp.MinYear.Value;
        if (dp.MaxYear.HasValue) picker.MaxYear = dp.MaxYear.Value;
        if (dp.DayFormat is not null) picker.DayFormat = dp.DayFormat;
        if (dp.MonthFormat is not null) picker.MonthFormat = dp.MonthFormat;
        if (dp.YearFormat is not null) picker.YearFormat = dp.YearFormat;
        SetElementTag(picker, dp);
        if (dp.OnDateChanged is not null)
            picker.DateChanged += (s, args) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                (GetElementTag(c) as DatePickerElement)?.OnDateChanged?.Invoke(args.NewDate);
            };
        ApplySetters(dp.Setters, picker);
        return picker;
    }

    private WinUI.TimePicker MountTimePicker(TimePickerElement tp)
    {
        var picker = new WinUI.TimePicker { Time = tp.Time, MinuteIncrement = tp.MinuteIncrement };
        if (tp.Header is not null) picker.Header = tp.Header;
        SetElementTag(picker, tp);
        if (tp.OnTimeChanged is not null)
            picker.TimeChanged += (s, args) =>
            {
                var c = (UIElement)s!;
                if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
                (GetElementTag(c) as TimePickerElement)?.OnTimeChanged?.Invoke(args.NewTime);
            };
        ApplySetters(tp.Setters, picker);
        return picker;
    }

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

    private WinUI.Image MountImage(ImageElement img)
    {
        var image = _pool.TryRent(typeof(WinUI.Image)) as WinUI.Image ?? new WinUI.Image();
        // Tag + wire BEFORE assigning Source — small/cached images can fire
        // ImageOpened/ImageFailed synchronously during Source assignment.
        SetElementTag(image, img);
        EnsureImageWiring(image);
        try
        {
            var uri = new Uri(img.Source, UriKind.RelativeOrAbsolute);
            image.Source = img.Source.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? new SvgImageSource(uri)
                : new BitmapImage(uri);
        }
        catch (UriFormatException)
        {
            // Malformed URI — leave image source empty rather than crashing
        }
        if (img.Width.HasValue) image.Width = img.Width.Value;
        if (img.Height.HasValue) image.Height = img.Height.Value;
        if (img.NineGrid.HasValue) image.NineGrid = img.NineGrid.Value;
        ApplySetters(img.Setters, image);
        return image;
    }

    /// <summary>
    /// Wires ImageOpened/ImageFailed trampolines once per pooled Image. The
    /// handler resolves the live element via GetElementTag so a later
    /// record-with that attaches a handler picks up without re-subscribing.
    /// </summary>
    internal static void EnsureImageWiring(WinUI.Image image)
    {
        var flags = GetPoolableWireFlags(image);
        if (!flags.ImageOpened)
        {
            flags.ImageOpened = true;
            image.ImageOpened += (s, _) =>
                (GetElementTag((UIElement)s!) as ImageElement)?.OnImageOpened?.Invoke();
        }
        if (!flags.ImageFailed)
        {
            flags.ImageFailed = true;
            image.ImageFailed += (s, args) =>
                (GetElementTag((UIElement)s!) as ImageElement)?.OnImageFailed?.Invoke(args.ErrorMessage);
        }
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

    private WinUI.WebView2 MountWebView2(WebView2Element wv)
    {
        var webView = new WinUI.WebView2();
        // Tag + subscribe BEFORE assigning Source — setting Source kicks off
        // CoreWebView2 initialization and navigation, and a fast init can fire
        // before subscriptions land otherwise.
        SetElementTag(webView, wv);

        // Subscribe unconditionally; the trampoline reads the live element via
        // GetElementTag so a later record-with that attaches a handler picks up
        // without re-wiring. NavigationStarting/Completed retain the existing
        // null-checked subscribe-only-on-handler pattern for backwards compat.
        if (wv.OnNavigationStarting is not null)
            webView.NavigationStarting += (s, args) =>
                (GetElementTag((UIElement)s!) as WebView2Element)?.OnNavigationStarting?.Invoke(new Uri(args.Uri));
        if (wv.OnNavigationCompleted is not null)
            webView.NavigationCompleted += (s, _) =>
                (GetElementTag((UIElement)s!) as WebView2Element)?.OnNavigationCompleted?.Invoke(((WinUI.WebView2)s!).Source);

        webView.WebMessageReceived += (s, args) =>
        {
            if (GetElementTag((UIElement)s!) is WebView2Element el && el.OnWebMessageReceived is { } h)
            {
                // TryGetWebMessageAsString throws if the underlying message
                // isn't a string (e.g. structured-clone JSON). Fall back to
                // WebMessageAsJson so handlers always see a string payload.
                string payload;
                try { payload = args.TryGetWebMessageAsString(); }
                catch { payload = args.WebMessageAsJson; }
                h(payload);
            }
        };

        webView.CoreWebView2Initialized += (s, _) =>
            (GetElementTag((UIElement)s!) as WebView2Element)?.OnCoreWebView2Initialized?.Invoke();

        if (wv.Source is not null) webView.Source = wv.Source;

        ApplySetters(wv.Setters, webView);
        return webView;
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

    private WinUI.VariableSizedWrapGrid MountWrapGrid(WrapGridElement wg, Action requestRerender)
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

    private WinUI.StackPanel MountStack(StackElement stack, Action requestRerender)
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

    private WinUI.Grid MountGrid(GridElement grid, Action requestRerender)
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

    private WinUI.ScrollViewer MountScrollView(ScrollViewElement scroll, Action requestRerender)
    {
        var sv = _pool.TryRent(typeof(WinUI.ScrollViewer)) as WinUI.ScrollViewer ?? new WinUI.ScrollViewer();
        sv.HorizontalScrollBarVisibility = scroll.HorizontalScrollBarVisibility;
        sv.VerticalScrollBarVisibility = scroll.VerticalScrollBarVisibility;
        sv.HorizontalScrollMode = (WinUI.ScrollMode)scroll.HorizontalScrollMode;
        sv.VerticalScrollMode = (WinUI.ScrollMode)scroll.VerticalScrollMode;
        sv.ZoomMode = (WinUI.ZoomMode)scroll.ZoomMode;
        sv.Content = Mount(scroll.Child, requestRerender);
        SetElementTag(sv, scroll);
        EnsureScrollViewerViewChangedWired(sv);
        ApplySetters(scroll.Setters, sv);
        return sv;
    }

    private static void EnsureScrollViewerViewChangedWired(WinUI.ScrollViewer sv)
    {
        // Pooled control: wire the trampoline exactly once. The handler reads
        // the live element via GetElementTag so a later record-with that
        // attaches OnViewChanged picks up without re-subscribing.
        var flags = GetPoolableWireFlags(sv);
        if (flags.ScrollViewerViewChanged) return;
        flags.ScrollViewerViewChanged = true;
        sv.ViewChanged += (s, e) =>
        {
            if (GetElementTag((WinUI.ScrollViewer)s!) is ScrollViewElement el && el.OnViewChanged is { } h)
                h(e);
        };
    }

    private WinUI.Border MountBorder(BorderElement border, Action requestRerender)
    {
        var bdr = _pool.TryRent(typeof(WinUI.Border)) as WinUI.Border ?? new WinUI.Border();
        if (border.CornerRadius.HasValue) bdr.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(border.CornerRadius.Value);
        if (border.Background is not null) bdr.Background = border.Background;
        if (border.BorderBrush is not null) bdr.BorderBrush = border.BorderBrush;
        if (border.BorderThickness.HasValue) bdr.BorderThickness = new Microsoft.UI.Xaml.Thickness(border.BorderThickness.Value);
        // Apply RequestedTheme before mounting children so that child ThemeRef
        // bindings resolve against the correct theme variant from the start.
        bdr.Child = border.Child is not null ? Mount(border.Child, requestRerender) : null;
        SetElementTag(bdr, border);
        ApplySetters(border.Setters, bdr);
        return bdr;
    }

    private WinUI.Expander MountExpander(ExpanderElement exp, Action requestRerender)
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

    private WinUI.Canvas MountCanvas(CanvasElement cvs, Action requestRerender)
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

    private Layout.FlexPanel MountFlex(FlexElement flex, Action requestRerender)
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

    private WinUI.Grid MountNavigationHost(NavigationHostElement element, Action requestRerender)
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

    private WinUI.NavigationView MountNavigationView(NavigationViewElement nav, Action requestRerender)
    {
        var nv = new WinUI.NavigationView
        {
            IsPaneOpen = nav.IsPaneOpen, PaneDisplayMode = nav.PaneDisplayMode,
            IsBackEnabled = nav.IsBackEnabled, IsSettingsVisible = nav.IsSettingsVisible,
        };
        if (nav.PaneTitle is not null) nv.PaneTitle = nav.PaneTitle;
        if (nav.Header is not null) nv.Header = Mount(nav.Header, requestRerender);
        if (nav.AutoSuggestBox is not null && Mount(nav.AutoSuggestBox, requestRerender) is WinUI.AutoSuggestBox asb)
            nv.AutoSuggestBox = asb;
        if (nav.PaneFooter is not null) nv.PaneFooter = Mount(nav.PaneFooter, requestRerender);
        if (nav.PaneCustomContent is not null) nv.PaneCustomContent = Mount(nav.PaneCustomContent, requestRerender);
        if (!double.IsNaN(nav.OpenPaneLength)) nv.OpenPaneLength = nav.OpenPaneLength;
        if (!double.IsNaN(nav.CompactModeThresholdWidth)) nv.CompactModeThresholdWidth = nav.CompactModeThresholdWidth;
        if (!double.IsNaN(nav.ExpandedModeThresholdWidth)) nv.ExpandedModeThresholdWidth = nav.ExpandedModeThresholdWidth;
        foreach (var item in nav.MenuItems)
        {
            if (item.IsHeader)
                nv.MenuItems.Add(new WinUI.NavigationViewItemHeader { Content = item.Content });
            else
                nv.MenuItems.Add(CreateNavItem(item));
        }
        if (nav.Content is not null) nv.Content = Mount(nav.Content, requestRerender);
        if (nav.SelectedTag is not null)
        {
            foreach (var mi in nv.MenuItems.OfType<WinUI.NavigationViewItem>())
                if (mi.Tag as string == nav.SelectedTag) { nv.SelectedItem = mi; break; }
        }
        SetElementTag(nv, nav);
        if (nav.OnSelectedTagChanged is not null)
            nv.SelectionChanged += (s, args) =>
            {
                var selected = args.SelectedItem as WinUI.NavigationViewItem;
                (GetElementTag((UIElement)s!) as NavigationViewElement)?.OnSelectedTagChanged?.Invoke(selected?.Tag as string);
            };
        if (nav.OnBackRequested is not null)
            nv.BackRequested += (s, _) => (GetElementTag((UIElement)s!) as NavigationViewElement)?.OnBackRequested?.Invoke();
        ApplySetters(nav.Setters, nv);
        return nv;
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

    private WinUI.TitleBar MountTitleBar(TitleBarElement tb, Action requestRerender)
    {
        var titleBar = new WinUI.TitleBar
        {
            Title = tb.Title,
            IsBackButtonVisible = tb.IsBackButtonVisible,
            IsBackButtonEnabled = tb.IsBackButtonEnabled,
            IsPaneToggleButtonVisible = tb.IsPaneToggleButtonVisible,
        };
        if (tb.Subtitle is not null) titleBar.Subtitle = tb.Subtitle;
        if (tb.Icon is not null) titleBar.IconSource = ResolveIconSource(tb.Icon);
        if (tb.Content is not null) titleBar.Content = Mount(tb.Content, requestRerender);
        if (tb.RightHeader is not null) titleBar.RightHeader = Mount(tb.RightHeader, requestRerender);
        SetElementTag(titleBar, tb);
        if (tb.OnBackRequested is not null)
            titleBar.BackRequested += (s, _) => (GetElementTag((UIElement)s!) as TitleBarElement)?.OnBackRequested?.Invoke();
        if (tb.OnPaneToggleRequested is not null)
            titleBar.PaneToggleRequested += (s, _) => (GetElementTag((UIElement)s!) as TitleBarElement)?.OnPaneToggleRequested?.Invoke();
        ApplySetters(tb.Setters, titleBar);

        // Register with the window for drag regions and caption buttons
        if (Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal is { } host)
        {
            host.Window.ExtendsContentIntoTitleBar = true;
            host.Window.SetTitleBar(titleBar);
        }

        return titleBar;
    }

    private WinUI.TabView MountTabView(TabViewElement tab, Action requestRerender)
    {
        var tv = new WinUI.TabView
        {
            SelectedIndex = tab.SelectedIndex,
            IsAddTabButtonVisible = tab.IsAddTabButtonVisible,
            TabWidthMode = tab.TabWidthMode,
            CloseButtonOverlayMode = tab.CloseButtonOverlayMode,
            CanDragTabs = tab.CanDragTabs,
            CanReorderTabs = tab.CanReorderTabs,
            AllowDropTabs = tab.AllowDropTabs,
        };
        if (tab.TabStripHeader is not null) tv.TabStripHeader = Mount(tab.TabStripHeader, requestRerender);
        if (tab.TabStripFooter is not null) tv.TabStripFooter = Mount(tab.TabStripFooter, requestRerender);
        foreach (var tabItem in tab.Tabs)
        {
            var tvi = new WinUI.TabViewItem
            {
                Header = tabItem.Header, IsClosable = tabItem.IsClosable,
                Content = Mount(tabItem.Content, requestRerender),
            };
            if (tabItem.Icon is not null) tvi.IconSource = ResolveIconSource(tabItem.Icon);
            tv.TabItems.Add(tvi);
        }
        SetElementTag(tv, tab);
        if (tab.OnSelectedIndexChanged is not null)
            tv.SelectionChanged += (s, _) =>
            {
                var t = (WinUI.TabView)s!;
                (GetElementTag(t) as TabViewElement)?.OnSelectedIndexChanged?.Invoke(t.SelectedIndex);
            };
        if (tab.OnTabCloseRequested is not null)
            tv.TabCloseRequested += (s, args) =>
            {
                var t = (WinUI.TabView)s!;
                var idx = t.TabItems.IndexOf(args.Tab);
                (GetElementTag(t) as TabViewElement)?.OnTabCloseRequested?.Invoke(idx);
            };
        if (tab.OnAddTabButtonClick is not null)
            tv.AddTabButtonClick += (s, _) => (GetElementTag((UIElement)s!) as TabViewElement)?.OnAddTabButtonClick?.Invoke();
        ApplySetters(tab.Setters, tv);
        return tv;
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

    private WinUI.ListView MountListView(ListViewElement lv, Action requestRerender)
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

    private WinUI.GridView MountGridView(GridViewElement gv, Action requestRerender)
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

    private UIElement MountTemplatedList(TemplatedListElementBase el, Action requestRerender)
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
        var currentEl = GetElementTag((UIElement)sender!) as TemplatedListElementBase;
        if (currentEl is not null && args.ItemIndex >= 0 && args.ItemIndex < currentEl.ItemCount
            && args.ItemContainer.ContentTemplateRoot is ContentControl cc)
        {
            var itemElement = currentEl.BuildItemView(args.ItemIndex);
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

    private UIElement MountContentDialog(ContentDialogElement cdEl, Action requestRerender)
    {
        var placeholder = new WinUI.StackPanel { Visibility = Visibility.Collapsed };
        SetElementTag(placeholder, cdEl);
        if (cdEl.IsOpen) ShowContentDialog(cdEl, placeholder, requestRerender);
        return placeholder;
    }

    private void ShowContentDialog(ContentDialogElement cdEl, FrameworkElement anchor, Action requestRerender)
    {
        // Source XamlRoot from the placeholder so the dialog routes to the
        // window that owns the anchor. If the anchor isn't attached yet
        // (mount-time IsOpen=true) defer via Loaded — falling back to
        // PrimaryWindow here would misroute the dialog when the anchor lives
        // in a secondary window.
        if (anchor.XamlRoot is null)
        {
            void OnLoaded(object sender, RoutedEventArgs _)
            {
                anchor.Loaded -= OnLoaded;
                // Re-read the current element from the anchor's Tag in case
                // IsOpen was toggled back to false (or the element was
                // replaced) before Loaded fired.
                if (GetElementTag(anchor) is not ContentDialogElement current || !current.IsOpen)
                    return;
                var deferredRoot = anchor.XamlRoot
                    ?? ReactorApp.PrimaryWindow?.NativeWindow.Content?.XamlRoot;
                ShowContentDialogCore(current, deferredRoot, requestRerender);
            }
            anchor.Loaded += OnLoaded;
            return;
        }
        ShowContentDialogCore(cdEl, anchor.XamlRoot, requestRerender);
    }

    private async void ShowContentDialogCore(ContentDialogElement cdEl, XamlRoot? xamlRoot, Action requestRerender)
    {
        var dialog = new WinUI.ContentDialog
        {
            Title = cdEl.Title, PrimaryButtonText = cdEl.PrimaryButtonText,
            DefaultButton = cdEl.DefaultButton,
            IsPrimaryButtonEnabled = cdEl.IsPrimaryButtonEnabled,
            IsSecondaryButtonEnabled = cdEl.IsSecondaryButtonEnabled,
        };
        if (cdEl.SecondaryButtonText is not null) dialog.SecondaryButtonText = cdEl.SecondaryButtonText;
        if (cdEl.CloseButtonText is not null) dialog.CloseButtonText = cdEl.CloseButtonText;
        dialog.Content = Mount(cdEl.Content, requestRerender);
        if (xamlRoot is not null) dialog.XamlRoot = xamlRoot;
        if (cdEl.OnOpened is not null) dialog.Opened += (_, _) => cdEl.OnOpened?.Invoke();
        // ApplySetters last so caller .Set(...) wins (including overriding XamlRoot).
        ApplySetters(cdEl.Setters, dialog);
        try
        {
            var winUiResult = await dialog.ShowAsync();
            cdEl.OnClosed?.Invoke(winUiResult);
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine(
                $"[Reactor.ContentDialog] ShowAsync failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private UIElement? MountFlyout(FlyoutElement flyEl, Action requestRerender)
    {
        var target = Mount(flyEl.Target, requestRerender);
        if (target is FrameworkElement targetFe)
        {
            var flyoutContent = Mount(flyEl.FlyoutContent, requestRerender);
            var flyout = new WinUI.Flyout
            {
                Content = flyoutContent,
                Placement = flyEl.Placement,
                ShowMode = flyEl.ShowMode,
                AreOpenCloseAnimationsEnabled = flyEl.AreOpenCloseAnimationsEnabled,
            };
            if (flyEl.OverlayInputPassThroughElement is not null
                && Mount(flyEl.OverlayInputPassThroughElement, requestRerender) is DependencyObject pt)
                flyout.OverlayInputPassThroughElement = pt;
            SetElementTag(targetFe, flyEl);
            // Route handlers through the target's Tag so Update() refreshing the tag to the
            // new FlyoutElement causes subsequent Opened/Closed to fire the current delegates —
            // capturing flyEl directly would freeze handlers to the mount-time element.
            if (flyEl.OnOpened is not null)
                flyout.Opened += (_, _) => (GetElementTag(targetFe) as FlyoutElement)?.OnOpened?.Invoke();
            if (flyEl.OnClosed is not null)
                flyout.Closed += (_, _) => (GetElementTag(targetFe) as FlyoutElement)?.OnClosed?.Invoke();
            // SetFlyoutOnControl wires .Flyout on Button/SplitButton targets so
            // clicking opens the flyout natively; non-button targets fall back
            // to SetAttachedFlyout metadata (opened only via ShowAttachedFlyout).
            SetFlyoutOnControl(targetFe, flyout);
            ApplySetters(flyEl.Setters, flyout);
            if (flyEl.IsOpen) WinPrim.FlyoutBase.ShowAttachedFlyout(targetFe);
        }
        return target;
    }

    private WinUI.TeachingTip MountTeachingTip(TeachingTipElement ttEl, Action requestRerender)
    {
        var tip = new WinUI.TeachingTip
        {
            Title = ttEl.Title,
            Subtitle = ttEl.Subtitle ?? "",
            IsOpen = ttEl.IsOpen,
            PlacementMargin = ttEl.PlacementMargin,
            PreferredPlacement = ttEl.PreferredPlacement,
        };
        if (ttEl.Content is not null) tip.Content = Mount(ttEl.Content, requestRerender);
        if (ttEl.ActionButtonContent is not null) tip.ActionButtonContent = ttEl.ActionButtonContent;
        if (ttEl.CloseButtonContent is not null) tip.CloseButtonContent = ttEl.CloseButtonContent;
        if (ttEl.IconSource is not null) tip.IconSource = ResolveIconSource(ttEl.IconSource);
        if (ttEl.HeroContent is not null) tip.HeroContent = Mount(ttEl.HeroContent, requestRerender);
        // Tag BEFORE wires so trampolines see the current element from the first tick.
        SetElementTag(tip, ttEl);
        // Route through the Tag trampoline (not a captured local) so skip-path
        // Tag refresh / Update can swap the dispatch target without re-wiring.
        if (ttEl.OnActionButtonClick is not null)
            tip.ActionButtonClick += (s, _) => (GetElementTag((UIElement)s!) as TeachingTipElement)?.OnActionButtonClick?.Invoke();
        if (ttEl.OnClosed is not null)
            tip.Closed += (s, _) => (GetElementTag((UIElement)s!) as TeachingTipElement)?.OnClosed?.Invoke();
        ApplySetters(ttEl.Setters, tip);
        return tip;
    }

    private WinUI.MenuBar MountMenuBar(MenuBarElement mbEl)
    {
        var menuBar = new WinUI.MenuBar();
        foreach (var menuItem in mbEl.Items)
        {
            var mbi = new WinUI.MenuBarItem { Title = menuItem.Title };
            foreach (var flyoutItem in menuItem.Items) mbi.Items.Add(CreateMenuFlyoutItem(flyoutItem));
            menuBar.Items.Add(mbi);
        }
        ApplySetters(mbEl.Setters, menuBar);
        return menuBar;
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

    private WinUI.CommandBar MountCommandBar(CommandBarElement cmdEl, Action requestRerender)
    {
        var commandBar = new WinUI.CommandBar
        {
            DefaultLabelPosition = cmdEl.DefaultLabelPosition,
            IsOpen = cmdEl.IsOpen,
        };
        if (cmdEl.Content is not null) commandBar.Content = Mount(cmdEl.Content, requestRerender);
        if (cmdEl.PrimaryCommands is not null)
            foreach (var cmd in cmdEl.PrimaryCommands) commandBar.PrimaryCommands.Add(CreateAppBarItem(cmd));
        if (cmdEl.SecondaryCommands is not null)
            foreach (var cmd in cmdEl.SecondaryCommands) commandBar.SecondaryCommands.Add(CreateAppBarItem(cmd));
        SetElementTag(commandBar, cmdEl);
        ApplySetters(cmdEl.Setters, commandBar);
        return commandBar;
    }

    private static WinUI.ICommandBarElement CreateAppBarItem(AppBarItemBase item)
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

    private UIElement? MountMenuFlyout(MenuFlyoutElement mfEl, Action requestRerender)
    {
        var target = Mount(mfEl.Target, requestRerender);
        if (target is FrameworkElement targetFe)
        {
            var menuFlyout = new WinUI.MenuFlyout();
            foreach (var item in mfEl.Items) menuFlyout.Items.Add(CreateMenuFlyoutItem(item));
            SetElementTag(targetFe, mfEl);
            // Use SetFlyoutOnControl so clicking a Button/SplitButton target opens
            // the flyout via .Flyout; non-button targets fall back to attached-flyout
            // metadata (still requires explicit ShowAttachedFlyout to open).
            SetFlyoutOnControl(targetFe, menuFlyout);
            ApplySetters(mfEl.Setters, menuFlyout);
        }
        return target;
    }

    private static WinUI.MenuFlyoutItemBase CreateMenuFlyoutItem(MenuFlyoutItemBase item)
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

    private UIElement MountLazyStack(LazyStackElementBase lazy, Action requestRerender)
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

    private WinUI.RelativePanel MountRelativePanel(RelativePanelElement rp, Action requestRerender)
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

    private WinUI.MediaPlayerElement MountMediaPlayerElement(MediaPlayerElementElement mpe)
    {
        var player = new WinUI.MediaPlayerElement
        {
            AreTransportControlsEnabled = mpe.AreTransportControlsEnabled,
            AutoPlay = mpe.AutoPlay,
        };
        // Tag + subscribe BEFORE assigning Source — setting Source starts the
        // pipeline immediately; a cached / fast-failing URI can raise
        // MediaOpened / MediaFailed before subscriptions would otherwise land.
        // MediaPlayer events fire on the player's worker thread; marshal back
        // to the element's dispatcher so handlers can mutate component state
        // safely. May fire after unmount — GetElementTag returns null then so
        // the handler invocation is a no-op.
        SetElementTag(player, mpe);

        var mp = player.MediaPlayer;
        if (mp is not null)
        {
            mp.MediaOpened += (s, _) => DispatchToElement<MediaPlayerElementElement>(player, el => el.OnMediaOpened?.Invoke());
            mp.MediaEnded += (s, _) => DispatchToElement<MediaPlayerElementElement>(player, el => el.OnMediaEnded?.Invoke());
            mp.MediaFailed += (s, args) =>
            {
                var msg = args.ErrorMessage ?? args.Error.ToString();
                DispatchToElement<MediaPlayerElementElement>(player, el => el.OnMediaFailed?.Invoke(msg));
            };
        }

        if (mpe.Source is not null)
            player.Source = global::Windows.Media.Core.MediaSource.CreateFromUri(new Uri(mpe.Source, UriKind.RelativeOrAbsolute));

        ApplySetters(mpe.Setters, player);
        return player;
    }

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

    private WinUI.AnimatedVisualPlayer MountAnimatedVisualPlayer(AnimatedVisualPlayerElement avp)
    {
        var player = new WinUI.AnimatedVisualPlayer { AutoPlay = avp.AutoPlay };
        SetElementTag(player, avp);
        ApplySetters(avp.Setters, player);
        return player;
    }

    // ── SemanticZoom ────────────────────────────────────────────────────

    private WinUI.SemanticZoom MountSemanticZoom(SemanticZoomElement sz, Action requestRerender)
    {
        var zoomedIn = Mount(sz.ZoomedInView, requestRerender);
        var zoomedOut = Mount(sz.ZoomedOutView, requestRerender);
        var semanticZoom = new WinUI.SemanticZoom();
        if (zoomedIn is ISemanticZoomInformation szi) semanticZoom.ZoomedInView = szi;
        if (zoomedOut is ISemanticZoomInformation szo) semanticZoom.ZoomedOutView = szo;
        SetElementTag(semanticZoom, sz);
        ApplySetters(sz.Setters, semanticZoom);
        return semanticZoom;
    }

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

    private WinUI.AnnotatedScrollBar MountAnnotatedScrollBar(AnnotatedScrollBarElement asb)
    {
        var scrollBar = new WinUI.AnnotatedScrollBar();
        ApplySetters(asb.Setters, scrollBar);
        return scrollBar;
    }

    // ── Popup ───────────────────────────────────────────────────────────

    private UIElement MountPopup(PopupElement popup, Action requestRerender)
    {
        // Popup is not a UIElement child, so we wrap it in a StackPanel
        var wrapper = new WinUI.StackPanel();
        var p = new WinPrim.Popup
        {
            IsOpen = popup.IsOpen,
            IsLightDismissEnabled = popup.IsLightDismissEnabled,
            HorizontalOffset = popup.HorizontalOffset,
            VerticalOffset = popup.VerticalOffset,
        };
        var child = Mount(popup.Child, requestRerender);
        p.Child = child as UIElement;
        SetElementTag(wrapper, popup);
        p.Opened += (s, _) => (GetElementTag(wrapper) as PopupElement)?.OnOpened?.Invoke();
        p.Closed += (s, _) => (GetElementTag(wrapper) as PopupElement)?.OnClosed?.Invoke();
        ApplySetters(popup.Setters, p);
        wrapper.Children.Add(p);
        return wrapper;
    }

    // ── RefreshContainer ────────────────────────────────────────────────

    private WinUI.RefreshContainer MountRefreshContainer(RefreshContainerElement rc, Action requestRerender)
    {
        var container = new WinUI.RefreshContainer
        {
            PullDirection = rc.PullDirection,
        };
        container.Content = Mount(rc.Content, requestRerender);
        SetElementTag(container, rc);
        container.RefreshRequested += (s, _) =>
            (GetElementTag((UIElement)s!) as RefreshContainerElement)?.OnRefreshRequested?.Invoke();
        ApplySetters(rc.Setters, container);
        return container;
    }

    // ── CommandBarFlyout ────────────────────────────────────────────────

    private UIElement? MountCommandBarFlyout(CommandBarFlyoutElement cbf, Action requestRerender)
    {
        var target = Mount(cbf.Target, requestRerender);
        if (target is FrameworkElement targetFe)
        {
            var flyout = new WinUI.CommandBarFlyout { Placement = cbf.Placement };
            if (cbf.PrimaryCommands is not null)
                foreach (var cmd in cbf.PrimaryCommands) flyout.PrimaryCommands.Add(CreateAppBarItem(cmd));
            if (cbf.SecondaryCommands is not null)
                foreach (var cmd in cbf.SecondaryCommands) flyout.SecondaryCommands.Add(CreateAppBarItem(cmd));
            SetElementTag(targetFe, cbf);
            WinPrim.FlyoutBase.SetAttachedFlyout(targetFe, flyout);
            ApplySetters(cbf.Setters, flyout);
        }
        return target;
    }

    // ── CalendarView ────────────────────────────────────────────────────

    private WinUI.CalendarView MountCalendarView(CalendarViewElement cv)
    {
        var calendarView = new WinUI.CalendarView
        {
            SelectionMode = cv.SelectionMode,
            IsGroupLabelVisible = cv.IsGroupLabelVisible,
            IsOutOfScopeEnabled = cv.IsOutOfScopeEnabled,
            NumberOfWeeksInView = cv.NumberOfWeeksInView,
            DisplayMode = cv.DisplayMode,
        };
        if (cv.CalendarIdentifier is not null) calendarView.CalendarIdentifier = cv.CalendarIdentifier;
        if (cv.Language is not null && global::Windows.Globalization.Language.IsWellFormed(cv.Language))
            calendarView.Language = cv.Language;
        if (cv.MinDate.HasValue) calendarView.MinDate = cv.MinDate.Value;
        if (cv.MaxDate.HasValue) calendarView.MaxDate = cv.MaxDate.Value;
        if (cv.FirstDayOfWeek.HasValue) calendarView.FirstDayOfWeek = cv.FirstDayOfWeek.Value;

        SetElementTag(calendarView, cv);

        // Initial selection set BEFORE subscribing so the declarative state
        // doesn't echo back into OnSelectedDatesChanged.
        if (cv.SelectedDates is { Count: > 0 })
        {
            foreach (var d in cv.SelectedDates) calendarView.SelectedDates.Add(d);
        }

        // Subscribe unconditionally so the handler picks up a later-attached
        // OnSelectedDatesChanged via GetElementTag without re-wiring.
        calendarView.SelectedDatesChanged += (s, _) =>
        {
            var c = (WinUI.CalendarView)s!;
            if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
            if (GetElementTag(c) is CalendarViewElement el && el.OnSelectedDatesChanged is { } h)
                h(c.SelectedDates.ToArray());
        };

        ApplySetters(cv.Setters, calendarView);
        return calendarView;
    }

    // ── SwipeControl ──────────────────────────────────────────────────

    private WinUI.SwipeControl MountSwipeControl(SwipeControlElement swipe, Action requestRerender)
    {
        var sc = new WinUI.SwipeControl();
        sc.Content = Mount(swipe.Content, requestRerender);

        if (swipe.LeftItems is { Length: > 0 })
        {
            var leftItems = new SwipeItems { Mode = swipe.LeftItemsMode };
            foreach (var item in swipe.LeftItems) leftItems.Add(CreateSwipeItem(item));
            sc.LeftItems = leftItems;
        }

        if (swipe.RightItems is { Length: > 0 })
        {
            var rightItems = new SwipeItems { Mode = swipe.RightItemsMode };
            foreach (var item in swipe.RightItems) rightItems.Add(CreateSwipeItem(item));
            sc.RightItems = rightItems;
        }

        SetElementTag(sc, swipe);
        ApplySetters(swipe.Setters, sc);
        return sc;
    }

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

    // ── ParallaxView ──────────────────────────────────────────────────

    private WinUI.ParallaxView MountParallaxView(ParallaxViewElement pv, Action requestRerender)
    {
        var parallax = new WinUI.ParallaxView
        {
            VerticalShift = pv.VerticalShift,
            HorizontalShift = pv.HorizontalShift,
            VerticalSourceStartOffset = pv.VerticalSourceStartOffset,
            VerticalSourceEndOffset = pv.VerticalSourceEndOffset,
        };
        if (pv.Source is not null) parallax.Source = pv.Source;
        parallax.Child = Mount(pv.Child, requestRerender) as UIElement;
        ApplySetters(pv.Setters, parallax);
        return parallax;
    }

    // ── MapControl ────────────────────────────────────────────────────

    private WinUI.MapControl MountMapControl(MapControlElement mc)
    {
        var map = new WinUI.MapControl
        {
            ZoomLevel = mc.ZoomLevel,
        };
        if (mc.MapServiceToken is not null) map.MapServiceToken = mc.MapServiceToken;
        ApplySetters(mc.Setters, map);
        return map;
    }

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
}
