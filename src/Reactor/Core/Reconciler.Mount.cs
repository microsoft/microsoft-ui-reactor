using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Hosting;
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

        // Spec 048 §8 — four-arm dispatch precedence:
        //   (1) per-host `_v1Handlers` (explicit RegisterHandler + cached
        //       global registry hits),
        //   (2) per-host `_typeRegistry` (legacy RegisterType callbacks),
        //   (3) global `ControlRegistry` (lazy factory-as-registration table),
        //   (4) composition-primitive switch (Func/Memo/Error boundary…).
        // Arm 1 is the fast steady-state path; arm 3 caches into
        // _v1Handlers on first hit so arm 1 catches it next time.
        if (_v1Handlers.TryGet(element.GetType(), out var v1Entry))
        {
            control = v1Entry.Mount(element, requestRerender, this);
        }
        // Registered types checked first
        else if (_typeRegistry.TryGetValue(element.GetType(), out var reg))
        {
            control = reg.Mount(element, requestRerender, this);
        }
        else if (TryResolveFromControlRegistry(element.GetType(), out v1Entry))
        {
            control = v1Entry.Mount(element, requestRerender, this);
        }
        else
        {
        control = element switch
        {
            ErrorBoundaryElement eb => MountErrorBoundary(eb, requestRerender),
            ComponentElement comp => MountComponent(comp, requestRerender),
            FuncElement func => MountFuncComponent(func, requestRerender),
            MemoElement memo => MountMemoComponent(memo, requestRerender),
            // EmptyElement is a no-op sentinel — callers (Reconcile, panel
            // children loops, ChildReconciler) already filter it before
            // reaching Mount, but MountContext.MountChild does not, so a V1
            // handler that forwards an EmptyElement child must still land
            // here as null rather than tripping the unregistered-type throw.
            EmptyElement => null,
            _ => ThrowNoHandlerRegistered(element),
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

    /// <summary>
    /// Final dispatch arm: the four resolution arms in <see cref="Mount"/>
    /// (per-host <c>_v1Handlers</c>, per-host <c>_typeRegistry</c>, global
    /// <see cref="V1Protocol.ControlRegistry"/>, and the composition-primitive
    /// switch) have all missed. Throw with an actionable message instead of
    /// silently returning null — silent-null lets a misconfigured tree mount
    /// as if the element didn't exist, which is one of the hardest classes of
    /// Reactor bugs to diagnose.
    /// </summary>
    /// <remarks>
    /// The most common cause (after spec-048 §3.4 deletes
    /// <c>RegisterV1BuiltInHandlers</c>) is that the caller bypassed the
    /// factory method (e.g. <c>Factories.TextBlock(...)</c>) and constructed
    /// the element record directly (<c>new TextBlockElement(...)</c>). The
    /// factory body contains the <c>Reg&lt;TElement, TControl, THandler&gt;.Done</c>
    /// touch that registers the handler on first call; direct-record
    /// construction skips that touch. See issue
    /// <see href="https://github.com/microsoft/microsoft-ui-reactor/issues/486"/>
    /// for the full discussion of the trade-off.
    /// </remarks>
    [global::System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static UIElement? ThrowNoHandlerRegistered(Element element)
    {
        var elementTypeName = element.GetType().FullName ?? element.GetType().Name;
        throw new InvalidOperationException(
            $"No handler is registered for element type '{elementTypeName}'. " +
            "The reconciler tried all four dispatch arms (per-host _v1Handlers, " +
            "per-host _typeRegistry, global ControlRegistry, and the composition-" +
            "primitive switch) and none of them knew how to mount this element.\n\n" +
            "Most common cause: the element record was constructed directly " +
            $"(e.g. `new {element.GetType().Name}(...)`) instead of through its " +
            "factory method. Factory methods carry the registration touch (a " +
            "`_ = Reg<TElement, TControl, THandler>.Done;` statement) that " +
            "registers the handler on first call; bypassing the factory skips " +
            "that touch.\n\n" +
            "Fixes:\n" +
            $"  (1) Call the factory method at least once before mounting (e.g. " +
            "`Factories.TextBlock(\"\")` for built-ins) so the handler registers, " +
            "then continue using the direct-record idiom for the hot path.\n" +
            "  (2) Register the handler explicitly up front: " +
            $"`Microsoft.UI.Reactor.Core.V1Protocol.ControlRegistry.Register" +
            $"<{element.GetType().Name}, TControl>(static () => new YourHandler())`.\n" +
            "  (3) For custom controls authored by your project, follow the " +
            "Pattern A factory-as-registration recipe in " +
            "docs/_pipeline/templates/extending-reactor-controls.md.dt.\n\n" +
            "See https://github.com/microsoft/microsoft-ui-reactor/issues/486 " +
            "for background.");
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








    private static WinUI.NavigationViewItem CreateNavItem(NavigationViewItemData data)
    {
        var item = new WinUI.NavigationViewItem { Content = data.Content, Tag = data.Tag ?? data.Content };
        var icon = IconResolver.ResolveIcon(data.IconElement, data.Icon);
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
    // Mount/Update bodies relocated to Reconciler.TemplatedTree.cs (spec 047 §14).

    /// <summary>
    /// Shared ContainerContentChanging handler for all templated items controls.
    /// On materialize: calls viewBuilder, mounts element, stores in ContentControl.
    /// On recycle: unmounts child, clears content.
    /// </summary>
    // Internal so the V1-owned TemplatedListLifecycle can wire it as the shared
    // realize/recycle handler; also reused 1:1 by Reconciler.KeyedItemsBinding.cs.
    internal void HandleTemplatedContainerContentChanging(object sender, ContainerContentChangingEventArgs args, Action requestRerender)
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
        UIElement? childControl = Mount(childElement, componentRerender);

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
        UIElement? childControl = Mount(childElement, componentRerender);

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
        UIElement? childControl = Mount(childElement, componentRerender);

        wrapper.Child = childControl;
        node.RenderedElement = childElement;
        return wrapper;
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


}
