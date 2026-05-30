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
            // Typed, data-driven TreeView<T> uses hand-coded per-container
            // hosting (ContainerContentChanging on the internal TreeViewList),
            // so it stays on the composition-primitive switch rather than the
            // V1 descriptor registry. (#447)
            TemplatedTreeViewElementBase ttv => MountTemplatedTreeView(ttv, requestRerender),
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

    // Legacy TreeViewNodeData.ContentElement reads — the property is [Obsolete]
    // in favor of typed TreeView<T> (issue #447) but the path stays functional
    // for back-compat, so suppress CS0618 at the internal use sites.
#pragma warning disable CS0618
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
#pragma warning restore CS0618

    /// <summary>Backward-compatible overload for non-ContentElement code paths.</summary>
    private static WinUI.TreeViewNode CreateTreeNode(TreeViewNodeData data)
    {
        var node = new WinUI.TreeViewNode { Content = data, IsExpanded = data.IsExpanded };
        if (data.Children is not null)
            foreach (var child in data.Children) node.Children.Add(CreateTreeNode(child));
        return node;
    }

    // ── Typed, data-driven TreeView<T> ───────────────────────────────────

    /// <summary>
    /// Mounts a typed <see cref="TemplatedTreeViewElementBase"/>. Builds a WinUI
    /// node-mode <c>TreeView</c> whose <c>ItemTemplate</c> is an empty
    /// <c>ContentControl</c> shell; each node's view is mounted imperatively
    /// into the realized container on demand (see
    /// <see cref="OnTypedTreeContainerContentChanging"/>) — the same
    /// realize/recycle hosting as the typed <c>ListView</c>, which keeps
    /// expand/collapse robust under container recycling. <c>node.Content</c>
    /// holds the developer's data item.
    /// </summary>
    private WinUI.TreeView MountTemplatedTreeView(TemplatedTreeViewElementBase el, Action requestRerender)
    {
        var treeView = new WinUI.TreeView
        {
            SelectionMode = el.GetSelectionMode(),
            CanDragItems = el.GetCanDragItems(),
            AllowDrop = el.GetAllowDrop(),
            CanReorderItems = el.GetCanReorderItems(),
            ItemTemplate = SharedContentControlTemplate.Value,
        };

        foreach (var root in el.GetRoots())
            treeView.RootNodes.Add(BuildTemplatedTreeNode(el, root));

        SetElementTag(treeView, el);

        // Trampolines resolve the live element + the data item (node.Content)
        // on dispatch, so they're wired unconditionally and never need
        // re-subscribing on Update (a no-op when the user supplied no callback).
        treeView.ItemInvoked += TemplatedTreeItemInvoked;
        treeView.Expanding += TemplatedTreeExpanding;

        // Hook the internal TreeViewList ("ListControl") ContainerContentChanging
        // so node views are mounted into their realized containers. The
        // ListControl only exists once the template applies (after the control
        // loads in-tree), so we subscribe on Loaded and populate the
        // already-realized initial containers there. Loaded also re-attaches
        // after an Unloaded/Loaded cycle; the attach is idempotent.
        treeView.Loaded += (s, _) => AttachTypedTreeHosting((WinUI.TreeView)s!, requestRerender);

        el.ApplyControlSetters(treeView);
        return treeView;
    }

    /// <summary>
    /// Recursively materializes a <see cref="WinUI.TreeViewNode"/> for
    /// <paramref name="item"/>. <c>node.Content</c> holds the data item; the
    /// view itself is mounted lazily per realized container, so nothing is
    /// mounted here.
    /// </summary>
    private static WinUI.TreeViewNode BuildTemplatedTreeNode(TemplatedTreeViewElementBase el, object item)
    {
        var node = new WinUI.TreeViewNode { Content = item, IsExpanded = el.GetIsExpanded(item) };
        var children = el.GetChildren(item);
        if (children is not null)
            foreach (var child in children)
                node.Children.Add(BuildTemplatedTreeNode(el, child));
        return node;
    }

    /// <summary>
    /// Subscribes the typed TreeView's internal list to ContainerContentChanging
    /// (once) and populates any containers that realized before the subscription.
    /// Idempotent — presence in <see cref="_typedTreeListControls"/> marks
    /// "already subscribed", so it's safe to call on every Loaded.
    /// </summary>
    private void AttachTypedTreeHosting(WinUI.TreeView treeView, Action requestRerender)
    {
        if (_typedTreeListControls.TryGetValue(treeView, out _)) return; // already subscribed

        var list = FindDescendantListView(treeView);
        if (list is null) return; // template not applied yet — a later Loaded will retry

        _typedTreeListControls.Add(treeView, list); // mark subscribed + cache for Update
        list.ContainerContentChanging += (_, args) =>
            OnTypedTreeContainerContentChanging(treeView, args, requestRerender);

        // Host the containers that realized before we subscribed — their
        // ContainerContentChanging already fired and won't fire again. They may
        // not be ready yet (their ContentTemplateRoot is generated a layout pass
        // later — observed under NativeAOT, where the realized container is even
        // still the base ListViewItem at Loaded time), so re-attempt on
        // LayoutUpdated until every realized container is hosted, then detach.
        // Everything realized AFTER this point flows through CCC. The pass count
        // is bounded so the handler always detaches (no dangling subscription),
        // and CCC still covers anything not hosted by then.
        if (!PopulateRealizedTreeContainers(treeView, list, requestRerender))
        {
            int passesLeft = 8;
            EventHandler<object>? onLayout = null;
            onLayout = (_, _) =>
            {
                if (PopulateRealizedTreeContainers(treeView, list, requestRerender) || --passesLeft <= 0)
                    list.LayoutUpdated -= onLayout;
            };
            list.LayoutUpdated += onLayout;
        }
    }

    /// <summary>
    /// Hosts every currently-realized container that's ready and not yet hosted.
    /// Returns true when no realized container remains unhosted (so the caller
    /// can stop re-attempting). Virtualized-out indices (null container) are not
    /// counted — ContainerContentChanging hosts them when they realize.
    /// </summary>
    private bool PopulateRealizedTreeContainers(WinUI.TreeView treeView, WinUI.ListView list, Action requestRerender)
    {
        bool complete = true;
        for (int i = 0; i < list.Items.Count; i++)
        {
            // The container is a ListViewItem/TreeViewItem (both ContentControl).
            // Don't filter on TreeViewItem — under AOT it can still be the base
            // ListViewItem when first realized.
            if (list.ContainerFromIndex(i) is not ContentControl container) continue;
            if (container.ContentTemplateRoot is null) { complete = false; continue; } // not ready yet
            PopulateTypedTreeContainer(treeView, container, list.Items[i], requestRerender);
        }
        return complete;
    }

    /// <summary>
    /// Realize/recycle handler for the typed TreeView's containers. On realize,
    /// mounts the node's view into the container's ContentControl; on recycle,
    /// unmounts it. Does <b>not</b> set <c>args.Handled</c> — the internal
    /// TreeViewList runs its own ContainerContentChanging handler (indentation /
    /// selection visuals) and must keep doing so.
    /// </summary>
    private void OnTypedTreeContainerContentChanging(
        WinUI.TreeView treeView, ContainerContentChangingEventArgs args, Action requestRerender)
    {
        if (args.ItemContainer?.ContentTemplateRoot is not ContentControl cc) return;

        if (args.InRecycleQueue)
        {
            if (cc.Content is UIElement old) UnmountChild(old);
            cc.Content = null;
            ClearElementTag(cc);
            return;
        }

        PopulateTypedTreeContainer(treeView, args.ItemContainer, args.Item, requestRerender);
    }

    /// <summary>
    /// Mounts the node's view into a realized container's ContentControl, unless
    /// it's already populated. The developer's data item is <c>node.Content</c>.
    /// </summary>
    private void PopulateTypedTreeContainer(
        WinUI.TreeView treeView, WinUI.Control container, object? item, Action requestRerender)
    {
        if (GetElementTag(treeView) is not TemplatedTreeViewElementBase el) return;
        if ((container as ContentControl)?.ContentTemplateRoot is not ContentControl cc) return;
        if (cc.Content is not null) return; // already hosted for this realization
        if (item is not WinUI.TreeViewNode node || node.Content is not { } data) return;

        var view = el.BuildView(data);
        cc.Content = Mount(view, requestRerender);
        SetElementTag(cc, view);
    }

    private static void TemplatedTreeItemInvoked(WinUI.TreeView sender, WinUI.TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is WinUI.TreeViewNode node
            && node.Content is { } item
            && GetElementTag(sender) is TemplatedTreeViewElementBase el)
            el.InvokeItemInvoked(item);
    }

    private static void TemplatedTreeExpanding(WinUI.TreeView sender, WinUI.TreeViewExpandingEventArgs args)
    {
        if (args.Node.Content is { } item
            && GetElementTag(sender) is TemplatedTreeViewElementBase el)
            el.InvokeExpanding(item);
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

    // ── RelativePanel ───────────────────────────────────────────────────


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
