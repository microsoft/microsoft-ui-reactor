using System;
using System.Linq;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

// Spec 047 §14 Target 1 — V1-owned lifecycle logic for composite controls.
internal static class CompositeLifecycle
{
    internal static WinUI.Grid MountCommandHost(Reconciler reconciler, CommandHostElement ch, Action requestRerender)
    {
        var host = new WinUI.Grid();
        var child = reconciler.Mount(ch.Child, requestRerender);
        if (child is not null) host.Children.Add(child);

        AddCommandHostAccelerators(host, ch.Commands);

        Reconciler.SetElementTag(host, ch);
        return host;
    }

    internal static UIElement? UpdateCommandHost(Reconciler reconciler, CommandHostElement o, CommandHostElement n, WinUI.Grid host, Action requestRerender)
    {
        // Update child element
        if (host.Children.Count > 0 && host.Children[0] is UIElement existingChild)
        {
            var replacement = reconciler.UpdateChild(o.Child, n.Child, existingChild, requestRerender);
            if (replacement is not null)
            {
                reconciler.UnmountChild(existingChild);
                // Explicit RemoveAt+Insert instead of indexer assignment — WinUI's
                // Children[i] = x can leave the old element's internal parent state
                // attached, causing a COMException when it is later reused from the
                // pool (mirrors PanelChildCollection.Replace).
                host.Children.RemoveAt(0);
                host.Children.Insert(0, replacement);
            }
        }
        else
        {
            var child = reconciler.Mount(n.Child, requestRerender);
            if (child is not null) host.Children.Add(child);
        }

        // Rebuild accelerators — clear and re-add (commands may have changed enabled state or handlers)
        host.KeyboardAccelerators.Clear();
        AddCommandHostAccelerators(host, n.Commands);

        Reconciler.SetElementTag(host, n);
        return null;
    }

    internal static WinUI.StackPanel MountFormField(Reconciler reconciler, FormFieldElement ff, Action requestRerender)
    {
        var panel = new WinUI.StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };

        // Resolve field name from explicit or auto-detected from Content's ValidationAttached
        var fieldName = FormFieldHelpers.ResolveFieldName(ff.FieldName, ff.Content);

        // Auto-validate: if Content has attached validators with a Value, run them now
        var attached = ff.Content.GetAttached<ValidationAttached>();
        var valCtx = reconciler.ReadContext(ValidationContexts.Current);
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
        var contentControl = reconciler.Mount(ff.Content, requestRerender);
        if (contentControl is not null)
        {
            ApplyFormFieldAutomation(contentControl, ff.Label);
            ApplyFormFieldErrorStyling(contentControl, valCtx, fieldName, ff.ShowWhen);
            WireTouchedOnBlur(panel, contentControl, valCtx, fieldName);
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

        Reconciler.SetElementTag(panel, ff);
        return panel;
    }

    internal static UIElement? UpdateFormField(
        Reconciler reconciler, FormFieldElement oldFf, FormFieldElement newFf,
        WinUI.StackPanel panel, Action requestRerender)
    {
        // Fixed 3-child layout: [0] label, [1] content, [2] description/error
        if (panel.Children.Count != 3)
            return reconciler.Mount(newFf, requestRerender);

        var fieldName = FormFieldHelpers.ResolveFieldName(newFf.FieldName, newFf.Content);

        // Auto-validate
        var attached = newFf.Content.GetAttached<ValidationAttached>();
        var valCtx = reconciler.ReadContext(ValidationContexts.Current);
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
        if (reconciler.CanUpdate(oldFf.Content, newFf.Content))
        {
            var replacement = reconciler.Update(oldFf.Content, newFf.Content, existingContent, requestRerender);
            if (replacement is not null)
            {
                // WinUI indexer assignment doesn't fully disconnect the old element's
                // parent state — use RemoveAt+Insert (see ChildCollection.Replace).
                reconciler.UnmountChild(existingContent);
                panel.Children.RemoveAt(1);
                panel.Children.Insert(1, replacement);
                existingContent = replacement;
            }
        }
        else
        {
            // Content element type changed — must remount
            reconciler.UnmountChild(existingContent);
            panel.Children.RemoveAt(1);
            var newContent = reconciler.Mount(newFf.Content, requestRerender)
                ?? new WinUI.StackPanel { Visibility = Visibility.Collapsed };
            panel.Children.Insert(1, newContent);
            existingContent = newContent;
        }

        ApplyFormFieldAutomation(existingContent, newFf.Label);
        ApplyFormFieldErrorStyling(existingContent, valCtx, fieldName, newFf.ShowWhen);
        WireTouchedOnBlur(panel, existingContent, valCtx, fieldName);

        // [2] Update description/error text
        if (panel.Children[2] is TextBlock descTb)
        {
            ApplyFormFieldDescription(descTb, valCtx, fieldName, newFf.Description, newFf.ShowWhen);
        }

        Reconciler.SetElementTag(panel, newFf);
        return null; // patched in-place
    }

    internal static WinUI.StackPanel MountValidationVisualizer(
        Reconciler reconciler, ValidationVisualizerElement vv, Action requestRerender)
    {
        var panel = new WinUI.StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        var valCtx = reconciler.ReadContext(ValidationContexts.Current);

        // Mount the content subtree first
        var contentControl = reconciler.Mount(vv.Content, requestRerender);

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
                var customControl = reconciler.Mount(customElement, requestRerender);
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

        Reconciler.SetElementTag(panel, vv);
        return panel;
    }

    internal static UIElement? UpdateValidationVisualizer(
        Reconciler reconciler, ValidationVisualizerElement oldVv, ValidationVisualizerElement newVv,
        WinUI.StackPanel panel, Action requestRerender)
    {
        // The visualizer layout varies by style and message state, so a full in-place
        // patch is complex. However, we can at least reconcile the content child when
        // styles match and the content element is updatable.
        if (oldVv.Style != newVv.Style)
            return reconciler.Mount(newVv, requestRerender);

        // Find the content child — it's the form control, not the error display chrome.
        // In MountValidationVisualizer, content is added after style-specific elements,
        // except for Inline where error text comes after content.
        // For simplicity and correctness, remount the visualizer but reconcile the
        // content subtree to preserve control state.
        return reconciler.Mount(newVv, requestRerender);
    }

    internal static UIElement MountValidationRule(Reconciler reconciler, ValidationRuleElement rule)
    {
        // The collapsed placeholder is this rule's durable identity: the reconciler keeps
        // it across re-renders, so it can name the rule as a message producer even though
        // the element record itself is rebuilt every pass. Keying on the message instead
        // would break for an interpolated one and would conflate two rules that happen to
        // share text (issue #1262 review).
        var placeholder = new WinUI.StackPanel { Visibility = Visibility.Collapsed };
        var binding = GetOrCreateRuleBinding(placeholder);

        var valCtx = reconciler.ReadContext(ValidationContexts.Current);
        if (valCtx is not null)
        {
            rule.Evaluate(valCtx, binding.Producer);
            binding.Context = valCtx;
            binding.Field = rule.Field;
        }

        Reconciler.SetElementTag(placeholder, rule);
        return placeholder;
    }

    internal static UIElement? UpdateValidationRule(Reconciler reconciler, ValidationRuleElement rule, UIElement control)
    {
        var binding = GetOrCreateRuleBinding(control);
        var valCtx = reconciler.ReadContext(ValidationContexts.Current);

        // A rule can move: to a different field, or into a different provider's context.
        // Its old contribution has to be withdrawn from where it used to live, or that
        // context stays invalid forever with a message nothing owns any more.
        if (binding.Context is { } previousCtx && binding.Field is { } previousField
            && (!ReferenceEquals(previousCtx, valCtx)
                || !string.Equals(previousField, rule.Field, StringComparison.Ordinal)))
        {
            previousCtx.ApplyOwned(previousField, binding.Producer, []);
            binding.Context = null;
            binding.Field = null;
        }

        if (valCtx is not null)
        {
            rule.Evaluate(valCtx, binding.Producer);
            binding.Context = valCtx;
            binding.Field = rule.Field;
        }

        return null; // keep existing collapsed placeholder
    }

    /// <summary>
    /// Withdraws a mounted rule's contribution when it leaves the tree — a conditionally
    /// rendered rule disappearing must not leave the form permanently invalid.
    /// </summary>
    internal static void RetractValidationRule(UIElement placeholder)
    {
        if (!_ruleBindings.TryGetValue(placeholder, out var binding)) return;

        if (binding.Context is { } ctx && binding.Field is { } field)
            ctx.ApplyOwned(field, binding.Producer, []);

        binding.Context = null;
        binding.Field = null;
    }

    private sealed class RuleBinding
    {
        internal string Producer = "";
        internal ValidationContext? Context;
        internal string? Field;
    }

    private static long s_ruleProducerSeed;
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, RuleBinding> _ruleBindings = new();

    private static RuleBinding GetOrCreateRuleBinding(UIElement placeholder)
    {
        if (_ruleBindings.TryGetValue(placeholder, out var existing)) return existing;

        var binding = new RuleBinding
        {
            Producer = "rule#" + global::System.Threading.Interlocked
                .Increment(ref s_ruleProducerSeed)
                .ToString(global::System.Globalization.CultureInfo.InvariantCulture),
        };
        _ruleBindings.Add(placeholder, binding);
        return binding;
    }

    /// <summary>
    /// Marks a field touched when its editor loses focus.
    /// <para>
    /// <c>FormField</c> defaults to <see cref="ShowWhen.WhenTouched"/> and the guide
    /// promises "errors appear below the field after the field is touched (focus then
    /// blur)" — but nothing in the framework ever called
    /// <see cref="ValidationContext.MarkTouched"/>, so that default could only ever
    /// reveal an error in apps that marked fields by hand. The documented FormField
    /// example does not, which left its error display permanently unreachable
    /// (issue #1262).
    /// </para>
    /// <para>
    /// The handler is attached once per control and reads the field name and context
    /// from a mutable binding at invocation time, so a control recycled through the
    /// element pool — or re-targeted at a different field by an update — reports for
    /// whatever field it currently hosts rather than the one it was mounted with.
    /// </para>
    /// </summary>
    private static void WireTouchedOnBlur(UIElement formFieldRoot, UIElement contentControl, ValidationContext? valCtx, string? fieldName)
    {
        if (contentControl is not FrameworkElement fe) return;

        // No context or field to report to — neutralize any binding this control still
        // carries from a previous FormField rather than leaving it pointed at the old one.
        if (valCtx is null || string.IsNullOrEmpty(fieldName))
        {
            // An update can drop the context *and* swap the content control in one pass.
            // The root still points at the old editor's binding, and that editor is on
            // its way to the pool with a live context — so neutralize what the root
            // points at, not just the incoming control.
            ClearFormFieldTouchBinding(formFieldRoot);
            _rootBindings.Remove(formFieldRoot);
            ClearTouchBinding(fe);
            return;
        }

        if (_touchBindings.TryGetValue(fe, out var existing))
        {
            existing.Context = valCtx;
            existing.FieldName = fieldName;
            ReplaceRootBinding(formFieldRoot, existing);
            return;
        }

        var binding = new TouchBinding { Context = valCtx, FieldName = fieldName };
        _touchBindings.Add(fe, binding);
        ReplaceRootBinding(formFieldRoot, binding);
        fe.LostFocus += (_, _) =>
        {
            if (binding.Context is { } ctx && binding.FieldName is { Length: > 0 } field)
                ctx.MarkTouched(field);
        };
    }

    /// <summary>
    /// Points a FormField root at its current content control's binding, neutralizing
    /// whichever binding it pointed at before.
    /// <para>
    /// An update that swaps the content control unmounts the old editor into the pool
    /// and maps the root to the new one. Without clearing the displaced binding, that
    /// pooled editor would keep marking the old field when rented out elsewhere — the
    /// same leak as an unmounted FormField, reached by a different route.
    /// </para>
    /// </summary>
    private static void ReplaceRootBinding(UIElement formFieldRoot, TouchBinding binding)
    {
        if (_rootBindings.TryGetValue(formFieldRoot, out var previous))
        {
            if (ReferenceEquals(previous, binding)) return;

            previous.Context = null;
            previous.FieldName = null;
            _rootBindings.Remove(formFieldRoot);
        }

        _rootBindings.Add(formFieldRoot, binding);
    }

    /// <summary>
    /// Neutralizes the blur binding on a <c>FormField</c>'s content control when the
    /// field unmounts.
    /// <para>
    /// The <c>LostFocus</c> handler is attached once for the control's lifetime, and
    /// controls such as <c>TextBox</c> are poolable. Without this, a control rented back
    /// out for some non-FormField use would still mark the field it used to host on
    /// every blur, and the pool would keep that <see cref="ValidationContext"/> alive.
    /// Clearing the live state leaves the one-time handler harmless and lets a later
    /// mount re-point the same binding.
    /// </para>
    /// </summary>
    internal static void ClearFormFieldTouchBinding(UIElement formFieldRoot)
    {
        // Looked up by root rather than by walking Children: unmount runs while the
        // subtree is being torn down, and reading a panel's visual children at that
        // point is exactly the kind of teardown-state access worth not doing.
        if (_rootBindings.TryGetValue(formFieldRoot, out var binding))
        {
            binding.Context = null;
            binding.FieldName = null;
        }
    }

    private static void ClearTouchBinding(UIElement contentControl)
    {
        if (contentControl is FrameworkElement fe && _touchBindings.TryGetValue(fe, out var binding))
        {
            binding.Context = null;
            binding.FieldName = null;
        }
    }

    // Test-only accessor (InternalsVisibleTo Reactor.Tests / Reactor.AppTests.Host):
    // reports whether a control's once-per-lifetime LostFocus handler would still
    // mark a field. The leak this guards — a displaced editor keeping the old
    // context alive — is otherwise observable only through element-pool reuse,
    // which is not deterministic enough to assert on.
    internal static bool HasLiveTouchBindingForTests(UIElement contentControl) =>
        contentControl is FrameworkElement fe
        && _touchBindings.TryGetValue(fe, out var binding)
        && binding.Context is not null
        && !string.IsNullOrEmpty(binding.FieldName);

    private sealed class TouchBinding
    {
        internal ValidationContext? Context;
        internal string? FieldName;
    }

    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, TouchBinding> _touchBindings = new();

    // FormField root -> the binding of its current content control, so unmount can
    // neutralize it without touching the visual tree mid-teardown.
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, TouchBinding> _rootBindings = new();

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
}
