using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using ReactorSourceMap = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap;

namespace Microsoft.UI.Reactor.Core;

// Default automation names and Tier 2/3 accessibility modifiers, applied by the reconciler.
internal static class AccessibilityApplier
{
    // WPF/WinUI auto-populate UIA Name from a control's string Content through the
    // automation peer, but AutomationProperties.GetName on the raw element only
    // returns an explicitly-set attached property. UIA clients that read the attached
    // property directly (our own tree walker, Appium's AutomationName getter, some
    // screen readers that probe before invoking the peer) see an empty string when
    // the author never set .AutomationName(). Mirroring the caption into the
    // attached property at mount makes both lookup paths agree, so
    // click { selector: "[name='+ 1']" } and a screen reader saying "+ 1" both work
    // without the author having to say so twice. Skips when the author already set
    // an AutomationName via modifier or setter — explicit always wins.
    /// <inheritdoc cref="Reconciler.ApplyDefaultAutomationName(FrameworkElement, string)"/>
    internal static void ApplyDefaultAutomationName(FrameworkElement fe, string? caption)
    {
        if (fe is null) return;
        if (DefaultAutomationNameFromCaption(caption) is not { } trimmed) return;
        var existing = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(fe);
        if (!string.IsNullOrEmpty(existing)) return;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, trimmed);
    }

    /// <summary>
    /// The name <see cref="ApplyDefaultAutomationName"/> / <see cref="UpdateDefaultAutomationName"/>
    /// derive from <paramref name="caption"/> (null when it provides no name). Shared with
    /// <c>ReactorDiagnostics.GetAppliedProperties</c> so the reported default cannot drift
    /// from the one applied.
    /// </summary>
    internal static string? DefaultAutomationNameFromCaption(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption)) return null;
        return caption.Length > 100 ? caption.Substring(0, 100) : caption;
    }

    // Update variant: a label change ("+ 1" → "+ 2") should update UIA Name as
    // long as the author didn't override it. We can't distinguish "author set
    // it to the previous caption" from "our default set it" without tracking
    // provenance, so the rule is: overwrite when the current value is empty or
    // equals the previous caption — any other value means the author intervened
    // (via modifier or setter) and we leave it alone.
    /// <inheritdoc cref="Reconciler.UpdateDefaultAutomationName(FrameworkElement, string, string)"/>
    internal static void UpdateDefaultAutomationName(FrameworkElement fe, string? oldCaption, string? newCaption)
    {
        if (fe is null) return;
        if (DefaultAutomationNameFromCaption(oldCaption) is null)
        {
            // Captionless controls pay no WinRT calls. A newly introduced
            // caption may still seed a default through the mount-time helper.
            ApplyDefaultAutomationName(fe, newCaption);
            return;
        }
        var trimmed = DefaultAutomationNameFromCaption(newCaption);
        var current = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(fe);
        if (!MayReplaceDefaultAutomationName(current, oldCaption)) return;
        if (trimmed is null)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty);
        else
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, trimmed);
    }

    /// <summary>
    /// True when the live name is Reactor's own default for <paramref name="oldCaption"/> (or
    /// empty), so a caption change may replace it. The default for a long caption is its
    /// truncated form (<see cref="DefaultAutomationNameFromCaption"/>), so both the full and the
    /// truncated caption count as Reactor's; any other value is the author's and is kept.
    /// </summary>
    internal static bool MayReplaceDefaultAutomationName(string? current, string? oldCaption)
    {
        if (string.IsNullOrEmpty(current)) return true;
        if (oldCaption is null) return false;
        return string.Equals(current, oldCaption, StringComparison.Ordinal)
            || string.Equals(current, DefaultAutomationNameFromCaption(oldCaption), StringComparison.Ordinal);
    }

    internal static string? ExtractElementCaption(Element? element) => element switch
    {
        TextBlockElement te => te.Content,
        null => null,
        _ => null,
    };

    // Resolves the caption-bearing string for controls whose visible text is
    // implicit (Button's Label, CheckBox's Label, TextBlock's Content, …). Returns
    // null for elements without a natural caption so ApplyDefaultAutomationName
    // leaves them alone. Kept alongside the helper so adding new caption-bearing
    // controls is one place to change.
    internal static string? ResolveCaptionForElement(Element element) => element switch
    {
        TextBlockElement te => te.Content,
        ButtonElement be => ContentCaption(be.ContentElement, be.Label),
        HyperlinkButtonElement hle => ContentCaption(hle.ContentElement, hle.Content),
        RepeatButtonElement rbe => ContentCaption(rbe.ContentElement, rbe.Label),
        ToggleButtonElement tbe => ContentCaption(tbe.ContentElement, tbe.Label),
        DropDownButtonElement dde => ContentCaption(dde.ContentElement, dde.Label),
        SplitButtonElement sbe => ContentCaption(sbe.ContentElement, sbe.Label),
        ToggleSplitButtonElement tsbe => ContentCaption(tsbe.ContentElement, tsbe.Label),
        CheckBoxElement cbe => ContentCaption(cbe.ContentElement, cbe.Label),
        RadioButtonElement rbe => ContentCaption(rbe.ContentElement, rbe.Label),
        ToggleSwitchElement tse => tse.Header as string ?? tse.OnContent ?? tse.OffContent,
        TextBoxElement tfe => tfe.Header as string ?? tfe.PlaceholderText,
        _ => null,
    };

    private static string? ContentCaption(Element? content, string? fallback)
        => content is null ? fallback : ExtractElementCaption(content);

    // ════════════════════════════════════════════════════════════════
    //  Accessibility modifiers (Tier 2/3 sub-record)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies the Tier 2/3 accessibility sub-record.
    /// </summary>
    /// <remarks>
    /// Called whenever <em>either</em> bag is non-null, which includes the case where the whole
    /// sub-record was dropped this render (<paramref name="a"/> null, <paramref name="oldA"/>
    /// not). That is why there is no <c>a is null</c> early return any more: it swallowed
    /// exactly the transition it needed to handle, so dropping <c>.HelpText(...)</c> — or the
    /// entire accessibility bag — left every automation property pinned on the control forever
    /// (issue #986). Every property is therefore null-tolerant on <paramref name="a"/> — set
    /// arms guard with <c>a is not null &amp;&amp; a.X…</c>, reset arms read <c>a?.X</c> — so the
    /// dropped-bag case takes the same reset path as dropping one modifier from a surviving bag.
    /// <para>
    /// Resets go through <c>ClearValue</c>, never through writing the property's default: a
    /// local value outranks every <c>Style</c> setter in WinUI's precedence order, so the
    /// assignment would pin the default rather than hand the property back (issue #952).
    /// </para>
    /// </remarks>
    internal static void ApplyAccessibilityModifiers(FrameworkElement fe, AccessibilityModifiers? oldA, AccessibilityModifiers? a)
    {
        if (a is null && oldA is null) return;

        if (a is not null && a.HelpText is not null && a.HelpText != oldA?.HelpText)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(fe, a.HelpText);
        else if (a?.HelpText is null && oldA?.HelpText is not null)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.HelpTextProperty);

        if (a is not null && a.FullDescription is not null && a.FullDescription != oldA?.FullDescription)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetFullDescription(fe, a.FullDescription);
        else if (a?.FullDescription is null && oldA?.FullDescription is not null)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.FullDescriptionProperty);

        if (a is not null && a.LandmarkType.HasValue && a.LandmarkType != oldA?.LandmarkType)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLandmarkType(fe, a.LandmarkType.Value);
        else if (a?.LandmarkType.HasValue != true && oldA?.LandmarkType.HasValue == true)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.LandmarkTypeProperty);

        if (a is not null && a.AccessibilityView.HasValue && a.AccessibilityView != oldA?.AccessibilityView)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(fe, a.AccessibilityView.Value);
        else if (a?.AccessibilityView.HasValue != true && oldA?.AccessibilityView.HasValue == true)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AccessibilityViewProperty);

        if (a is not null && a.IsRequiredForForm.HasValue && a.IsRequiredForForm != oldA?.IsRequiredForForm)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetIsRequiredForForm(fe, a.IsRequiredForForm.Value);
        else if (a?.IsRequiredForForm.HasValue != true && oldA?.IsRequiredForForm.HasValue == true)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.IsRequiredForFormProperty);

        if (a is not null && a.LiveSetting.HasValue && a.LiveSetting != oldA?.LiveSetting)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(fe, a.LiveSetting.Value);
        else if (a?.LiveSetting.HasValue != true && oldA?.LiveSetting.HasValue == true)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.LiveSettingProperty);

        if (a is not null && a.PositionInSet.HasValue && a.PositionInSet != oldA?.PositionInSet)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetPositionInSet(fe, a.PositionInSet.Value);
        else if (a?.PositionInSet.HasValue != true && oldA?.PositionInSet.HasValue == true)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.PositionInSetProperty);

        if (a is not null && a.SizeOfSet.HasValue && a.SizeOfSet != oldA?.SizeOfSet)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetSizeOfSet(fe, a.SizeOfSet.Value);
        else if (a?.SizeOfSet.HasValue != true && oldA?.SizeOfSet.HasValue == true)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.SizeOfSetProperty);

        if (a is not null && a.Level.HasValue && a.Level != oldA?.Level)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLevel(fe, a.Level.Value);
        else if (a?.Level.HasValue != true && oldA?.Level.HasValue == true)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.LevelProperty);

        if (a is not null && a.ItemStatus is not null && a.ItemStatus != oldA?.ItemStatus)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(fe, a.ItemStatus);
        else if (a?.ItemStatus is null && oldA?.ItemStatus is not null)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.ItemStatusProperty);

        if (a is not null && a.TabFocusNavigation.HasValue && a.TabFocusNavigation != oldA?.TabFocusNavigation)
            fe.TabFocusNavigation = a.TabFocusNavigation.Value;
        else if (a?.TabFocusNavigation.HasValue != true && oldA?.TabFocusNavigation.HasValue == true)
            fe.ClearValue(UIElement.TabFocusNavigationProperty);

        // LabeledBy — resolve AutomationId string to the target element in the visual tree.
        // During mount the element may not be in the visual tree yet (XamlRoot is null),
        // so defer resolution to the Loaded event if needed.
        if (a is not null && a.LabeledBy is not null && a.LabeledBy != oldA?.LabeledBy)
        {
            var target = FindByAutomationId(fe, a.LabeledBy);
            if (target is not null)
            {
                // Resolved synchronously — retire any deferred request from an
                // earlier render so its parked handler can't overwrite this one.
                // TryGet, not GetOrCreate: with no state there is no parked handler
                // and nothing to retire, and this is the common path — allocating and
                // attaching a ReactorState here would cost one per element that ever
                // resolves a LabeledBy synchronously.
                if (Reconciler.TryGetReactorState(fe, out var resolvedState))
                    resolvedState.PendingLabeledBy = null;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetLabeledBy(fe, target);
            }
            else
            {
                // Element not yet in visual tree — defer until Loaded. The handler
                // outlives this render, so publish the request first and have the
                // handler re-check it: if LabeledBy is dropped or retargeted before
                // Loaded fires, the stale handler must no-op rather than stomp the
                // reset arm below (issue #986).
                var labelId = a.LabeledBy;
                Reconciler.GetOrCreateReactorState(fe).PendingLabeledBy = labelId;
                void OnLoaded(object sender, RoutedEventArgs _)
                {
                    fe.Loaded -= OnLoaded;
                    if (!Reconciler.TryGetReactorState(fe, out var pending) || pending.PendingLabeledBy != labelId)
                        return;
                    var deferred = FindByAutomationId(fe, labelId);
                    if (deferred is not null)
                    {
                        pending.PendingLabeledBy = null;
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLabeledBy(fe, deferred);
                    }
                    // Not found: the request stays recorded as pending (still unresolved), so
                    // ReactorDiagnostics.GetReferenceEdges keeps reporting it. Nothing retries it
                    // here; a later render that changes or drops the id, or pool return, retires it.
                }
                fe.Loaded += OnLoaded;
            }
        }
        else if (a?.LabeledBy is null && oldA?.LabeledBy is not null)
        {
            // Cancel a still-parked deferred resolution before clearing, or it would
            // re-apply the dropped label the moment the element loads.
            if (Reconciler.TryGetReactorState(fe, out var pendingState))
                pendingState.PendingLabeledBy = null;
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.LabeledByProperty);
        }
    }

    /// <summary>
    /// Walks the visual tree from <paramref name="element"/>'s XamlRoot to find
    /// the first UIElement whose AutomationProperties.AutomationId matches <paramref name="automationId"/>.
    /// </summary>
    private static UIElement? FindByAutomationId(FrameworkElement element, string automationId)
    {
        var root = element.XamlRoot?.Content;
        if (root is null) return null;
        return WalkForAutomationId(root, automationId);
    }

    private static UIElement? WalkForAutomationId(DependencyObject node, string automationId)
    {
        if (node is UIElement uie
            && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(uie) == automationId)
            return uie;

        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i);
            var found = WalkForAutomationId(child, automationId);
            if (found is not null) return found;
        }
        return null;
    }
}
