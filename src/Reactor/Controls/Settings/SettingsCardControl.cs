// Portions of this file are ported from the Windows Community Toolkit
// (https://github.com/CommunityToolkit/Windows, components/SettingsControls/src:
// SettingsCard/SettingsCard.cs, SettingsCard/SettingsCard.Properties.cs and
// SettingsCard/SettingsCardAutomationPeer.cs).
// Original notice:
//   Licensed to the .NET Foundation under one or more agreements.
//   The .NET Foundation licenses this file to you under the MIT license.
//   See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using IconElement = Microsoft.UI.Xaml.Controls.IconElement;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// The native control behind <see cref="SettingsCardElement"/> (spec 065): a port of the
/// Toolkit's <c>SettingsCard</c>. Its look comes from <see cref="SettingsCardResources"/>; this
/// class carries the Toolkit's code-behind, plus the visual-state logic the Toolkit expressed
/// with its own state triggers.
/// </summary>
internal sealed partial class SettingsCardControl : ButtonBase, IReactorBaseStyleHost
{
    internal const string NormalState = "Normal";
    internal const string PointerOverState = "PointerOver";
    internal const string PressedState = "Pressed";
    internal const string DisabledState = "Disabled";

    internal const string BitmapHeaderIconEnabledState = "BitmapHeaderIconEnabled";
    internal const string BitmapHeaderIconDisabledState = "BitmapHeaderIconDisabled";

    internal const string RightState = "Right";
    internal const string RightWrappedState = "RightWrapped";
    internal const string RightWrappedNoIconState = "RightWrappedNoIcon";
    internal const string LeftState = "Left";
    internal const string VerticalState = "Vertical";

    internal const string ContentVisibleState = "Visible";
    internal const string ContentCollapsedState = "Collapsed";

    internal const string NoContentSpacingState = "NoContentSpacing";
    internal const string ContentSpacingState = "ContentSpacing";

    /// <summary>The Toolkit's <c>SettingsCardWrapThreshold</c>: below this card width the content moves under the header.</summary>
    internal const double WrapThreshold = 476;

    /// <summary>The Toolkit's <c>SettingsCardWrapNoIconThreshold</c>: below this card width the icon is hidden too.</summary>
    internal const double WrapNoIconThreshold = 286;

    private const string DefaultActionIconGlyph = "\uE974";

    private string? _headerText;
    private UIElement? _headerChild;
    private string? _descriptionText;
    private UIElement? _descriptionChild;
    private IconElement? _headerIcon;
    private IconElement? _actionIcon;
    private IconElement? _defaultActionIcon;
    private string? _actionIconToolTip;
    private bool _isActionIconVisible = true;
    private bool _isClickEnabled;
    private SettingsCardContentAlignment _contentAlignment;
    private bool _isExpanderItem;
    private SettingsCardResources.Variant _variant;
    private Style _baseStyle;

    private FrameworkElement? _rootGrid;
    private FrameworkElement? _headerIconHolder;
    private ContentPresenter? _headerIconPresenter;
    private ContentPresenter? _headerPresenter;
    private ContentPresenter? _descriptionPresenter;
    private FrameworkElement? _actionIconHolder;
    private ContentPresenter? _actionIconPresenter;

    // The width of PART_RootGrid at its last layout; NaN until the first one. The Toolkit's
    // ControlSizeTriggers watch the same element.
    private double _rootWidth = double.NaN;

    // The content this card named after its header, and the name it gave, so a later header
    // or content change can take the name back without touching a name someone else set.
    private UIElement? _autoNamedContent;
    private string? _autoName;

    /// <summary>Raised when a clickable card is clicked (pointer, Enter, Space) or invoked
    /// through UI Automation.</summary>
    internal event Action<SettingsCardControl>? Invoked;

    public SettingsCardControl()
    {
        // The template is a local value so a style written over this card (a themed style
        // from Reconciler.ApplyThemeBindings, or an author's) never re-templates it.
        _variant = SettingsCardResources.Current;
        _baseStyle = _variant.CardStyle;
        Style = _baseStyle;
        Template = _variant.CardTemplate;

        // Toolkit: the card style's StyleExtensions.Resources merges a copy of the content
        // styles into each card, where they reach the card's content as implicit styles.
        var contentStyles = new ResourceDictionary();
        foreach (var entry in SettingsCardResources.ContentStyles)
            contentStyles[entry.Key] = entry.Value;
        Resources.MergedDictionaries.Add(contentStyles);

        Click += OnClick;
        IsEnabledChanged += OnIsEnabledChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        RegisterPropertyChangedCallback(ContentProperty, OnContentChanged);
    }

    /// <summary>The header shown: the element header when set, else the header text.</summary>
    internal object? Header => (object?)_headerChild ?? _headerText;

    /// <summary>The description shown: the element description when set, else the text.</summary>
    internal object? Description => (object?)_descriptionChild ?? _descriptionText;

    internal string? HeaderText
    {
        get => _headerText;
        set
        {
            if (_headerText == value) return;
            _headerText = value;
            OnHeaderChanged();
        }
    }

    internal UIElement? HeaderChild
    {
        get => _headerChild;
        set
        {
            if (ReferenceEquals(_headerChild, value)) return;
            _headerChild = value;
            OnHeaderChanged();
        }
    }

    internal string? DescriptionText
    {
        get => _descriptionText;
        set
        {
            if (_descriptionText == value) return;
            _descriptionText = value;
            OnDescriptionChanged();
        }
    }

    internal UIElement? DescriptionChild
    {
        get => _descriptionChild;
        set
        {
            if (ReferenceEquals(_descriptionChild, value)) return;
            _descriptionChild = value;
            OnDescriptionChanged();
        }
    }

    internal IconElement? HeaderIcon
    {
        get => _headerIcon;
        set
        {
            if (ReferenceEquals(_headerIcon, value)) return;
            _headerIcon = value;
            OnHeaderIconChanged();
        }
    }

    /// <summary>The action icon of a clickable card; <c>null</c> shows the default chevron.</summary>
    internal IconElement? ActionIcon
    {
        get => _actionIcon;
        set
        {
            if (ReferenceEquals(_actionIcon, value)) return;
            _actionIcon = value;
            OnActionIconContentChanged();
        }
    }

    internal string? ActionIconToolTip
    {
        get => _actionIconToolTip;
        set
        {
            if (_actionIconToolTip == value) return;
            _actionIconToolTip = value;
            OnActionIconContentChanged();
        }
    }

    internal bool IsActionIconVisible
    {
        get => _isActionIconVisible;
        set
        {
            if (_isActionIconVisible == value) return;
            _isActionIconVisible = value;
            OnActionIconChanged();
        }
    }

    internal bool IsClickEnabled
    {
        get => _isClickEnabled;
        set
        {
            if (_isClickEnabled == value) return;
            _isClickEnabled = value;
            OnIsClickEnabledChanged();
            UpdateBaseStyle();
        }
    }

    internal SettingsCardContentAlignment ContentAlignment
    {
        get => _contentAlignment;
        set
        {
            if (_contentAlignment == value) return;
            _contentAlignment = value;
            UpdateContentAlignmentState();
        }
    }

    /// <summary>Whether the card is an item of a settings expander, which gives it the Toolkit's
    /// expander-item style (or the clickable variant).</summary>
    internal bool IsExpanderItem
    {
        get => _isExpanderItem;
        set
        {
            if (_isExpanderItem == value) return;
            _isExpanderItem = value;
            UpdateBaseStyle();
        }
    }

    Style IReactorBaseStyleHost.BaseStyle => _baseStyle;

    string IReactorBaseStyleHost.BaseStyleKey
        => !_isExpanderItem ? _variant.CardStyleKey
            : _isClickEnabled ? _variant.ExpanderClickableItemStyleKey
            : _variant.ExpanderItemStyleKey;

    string IReactorBaseStyleHost.BaseStyleTargetType => "ButtonBase";

    /// <summary>Whether the card uses the contrast-theme markup.</summary>
    internal bool IsHighContrast => _variant.IsHighContrast;

    /// <summary>
    /// Switches to the markup for the current Windows contrast setting. The handler calls it
    /// on every reconcile, and a contrast change triggers a full one, so every card follows.
    /// </summary>
    internal void SyncHighContrast()
    {
        var variant = SettingsCardResources.Current;
        if (ReferenceEquals(variant, _variant)) return;
        _variant = variant;
        Template = variant.CardTemplate;
        UpdateBaseStyle();
    }

    /// <summary>
    /// The visual state of the <c>ContentAlignmentStates</c> group for a card of this alignment
    /// and root width (<see cref="double.NaN"/> before the first layout).
    /// </summary>
    /// <remarks>
    /// The Toolkit declares two <c>ControlSizeTrigger</c>s on <c>PART_RootGrid</c>
    /// (<c>RightWrapped</c> for 286 ≤ width &lt; 476, <c>RightWrappedNoIcon</c> below 286) ahead
    /// of the <c>Left</c> and <c>Vertical</c> alignment triggers. When several triggers are
    /// active the first declared state wins, so a narrow card wraps whatever its alignment. The
    /// Toolkit reference captures show this for a narrow vertical card (spec 065).
    /// </remarks>
    internal static string ResolveContentAlignmentState(SettingsCardContentAlignment alignment, double rootWidth)
    {
        if (!double.IsNaN(rootWidth))
        {
            if (rootWidth >= WrapNoIconThreshold && rootWidth < WrapThreshold) return RightWrappedState;
            if (rootWidth < WrapNoIconThreshold) return RightWrappedNoIconState;
        }
        return alignment switch
        {
            SettingsCardContentAlignment.Left => LeftState,
            SettingsCardContentAlignment.Vertical => VerticalState,
            _ => RightState,
        };
    }

    /// <summary>Whether the content gets the 8 pixel spacing below the header: it is wrapped or
    /// vertical, and there is both content and a header or description.</summary>
    internal static bool NeedsContentSpacing(string alignmentState, bool hasContent, bool hasHeaderOrDescription)
        => alignmentState is RightWrappedState or RightWrappedNoIconState or VerticalState
            && hasContent && hasHeaderOrDescription;

    protected override void OnApplyTemplate()
    {
        if (_rootGrid is not null) _rootGrid.SizeChanged -= OnRootGridSizeChanged;

        // A new template (the contrast variant) gets the same header, description and icons;
        // release them from the old parts first, or WinUI refuses the second parent.
        if (_headerPresenter is not null) _headerPresenter.Content = null;
        if (_descriptionPresenter is not null) _descriptionPresenter.Content = null;
        if (_headerIconPresenter is not null) _headerIconPresenter.Content = null;
        if (_actionIconPresenter is not null) _actionIconPresenter.Content = null;

        base.OnApplyTemplate();

        _rootGrid = GetTemplateChild("PART_RootGrid") as FrameworkElement;
        _headerIconHolder = GetTemplateChild("PART_HeaderIconPresenterHolder") as FrameworkElement;
        _headerIconPresenter = GetTemplateChild("PART_HeaderIconPresenter") as ContentPresenter;
        _headerPresenter = GetTemplateChild("PART_HeaderPresenter") as ContentPresenter;
        _descriptionPresenter = GetTemplateChild("PART_DescriptionPresenter") as ContentPresenter;
        _actionIconHolder = GetTemplateChild("PART_ActionIconPresenterHolder") as FrameworkElement;
        _actionIconPresenter = GetTemplateChild("PART_ActionIconPresenter") as ContentPresenter;

        _rootWidth = double.NaN;
        if (_rootGrid is not null) _rootGrid.SizeChanged += OnRootGridSizeChanged;

        OnActionIconContentChanged();
        OnActionIconChanged();
        OnHeaderChanged();
        OnHeaderIconChanged();
        OnDescriptionChanged();
        OnIsClickEnabledChanged();
        CheckInitialVisualState();
        UpdateAccessibleContentName();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SettingsCardAutomationPeer(this);

    /// <summary>Raises <see cref="Invoked"/>; the automation peer's Invoke calls it.</summary>
    internal void RaiseInvoked() => Invoked?.Invoke(this);

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (_isClickEnabled) RaiseInvoked();
    }

    // ── Pointer and keyboard states (Toolkit: EnableButtonInteraction) ─────────

    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        if (_isClickEnabled) VisualStateManager.GoToState(this, PointerOverState, true);
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerExited(e);
        if (_isClickEnabled) VisualStateManager.GoToState(this, NormalState, true);
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_isClickEnabled) VisualStateManager.GoToState(this, NormalState, true);
    }

    protected override void OnPointerCanceled(PointerRoutedEventArgs e)
    {
        base.OnPointerCanceled(e);
        if (_isClickEnabled) VisualStateManager.GoToState(this, NormalState, true);
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (_isClickEnabled)
        {
            base.OnPointerPressed(e);
            VisualStateManager.GoToState(this, PressedState, true);
        }
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (_isClickEnabled)
        {
            base.OnPointerReleased(e);
            VisualStateManager.GoToState(this, NormalState, true);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Only show the pressed state when the card itself has focus, not its content.
        if (_isClickEnabled && IsActivationKey(e.Key) && ReferenceEquals(GetFocusedElement(), this))
            VisualStateManager.GoToState(this, PressedState, true);
    }

    private void OnPreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_isClickEnabled && IsActivationKey(e.Key))
            VisualStateManager.GoToState(this, NormalState, true);
    }

    private static bool IsActivationKey(VirtualKey key)
        => key is VirtualKey.Enter or VirtualKey.Space or VirtualKey.GamepadA;

    private object? GetFocusedElement()
        => XamlRoot is { } root ? FocusManager.GetFocusedElement(root) : null;

    // ── State changes ──────────────────────────────────────────────────────────

    private void OnIsClickEnabledChanged()
    {
        OnActionIconChanged();
        IsTabStop = _isClickEnabled;
        // A card that stops being clickable must not keep a hover or pressed look.
        if (!_isClickEnabled)
            VisualStateManager.GoToState(this, IsEnabled ? NormalState : DisabledState, true);
    }

    private void OnIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        VisualStateManager.GoToState(this, IsEnabled ? NormalState : DisabledState, true);
        CheckHeaderIconState();
    }

    private void CheckInitialVisualState()
    {
        VisualStateManager.GoToState(this, IsEnabled ? NormalState : DisabledState, true);
        UpdateContentAlignmentState();
        UpdateContentVisibilityState();
        CheckHeaderIconState();
    }

    private void CheckHeaderIconState()
    {
        // The Disabled state only sets foreground brushes; a bitmap icon dims through opacity.
        if (_headerIcon is BitmapIcon)
            VisualStateManager.GoToState(this, IsEnabled ? BitmapHeaderIconEnabledState : BitmapHeaderIconDisabledState, true);
    }

    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _rootWidth = e.NewSize.Width;
        UpdateContentAlignmentState();
    }

    private void UpdateContentAlignmentState()
    {
        var state = ResolveContentAlignmentState(_contentAlignment, _rootWidth);
        VisualStateManager.GoToState(this, state, true);
        var spacing = NeedsContentSpacing(state, Content is not null,
            !IsNullOrEmpty(Header) || !IsNullOrEmpty(Description));
        VisualStateManager.GoToState(this, spacing ? ContentSpacingState : NoContentSpacingState, true);
    }

    private void UpdateContentVisibilityState()
        => VisualStateManager.GoToState(this, IsNullOrEmpty(Content) ? ContentCollapsedState : ContentVisibleState, true);

    private void OnContentChanged(DependencyObject sender, DependencyProperty dp)
    {
        UpdateContentVisibilityState();
        UpdateContentAlignmentState();
        UpdateAccessibleContentName();
    }

    private void OnActionIconChanged()
    {
        if (_actionIconHolder is not null)
            _actionIconHolder.Visibility = _isClickEnabled && _isActionIconVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnActionIconContentChanged()
    {
        if (_actionIconPresenter is null) return;
        _actionIconPresenter.Content = _actionIcon
            ?? (_defaultActionIcon ??= new FontIcon { Glyph = DefaultActionIconGlyph, MirroredWhenRightToLeft = true });
        ToolTipService.SetToolTip(_actionIconPresenter, _actionIconToolTip);
    }

    private void OnHeaderIconChanged()
    {
        if (_headerIconPresenter is not null) _headerIconPresenter.Content = _headerIcon;
        if (_headerIconHolder is not null)
            _headerIconHolder.Visibility = _headerIcon is not null ? Visibility.Visible : Visibility.Collapsed;
        CheckHeaderIconState();
    }

    private void OnHeaderChanged()
    {
        var header = Header;
        if (_headerPresenter is not null)
        {
            _headerPresenter.Content = header;
            _headerPresenter.Visibility = IsNullOrEmpty(header) ? Visibility.Collapsed : Visibility.Visible;
        }
        UpdateContentAlignmentState();
        UpdateAccessibleContentName();
    }

    private void OnDescriptionChanged()
    {
        var description = Description;
        if (_descriptionPresenter is not null)
        {
            _descriptionPresenter.Content = description;
            _descriptionPresenter.Visibility = IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;
        }
        UpdateContentAlignmentState();
    }

    private void UpdateBaseStyle()
    {
        var next = !_isExpanderItem ? _variant.CardStyle
            : _isClickEnabled ? _variant.ExpanderClickableItemStyle
            : _variant.ExpanderItemStyle;
        if (ReferenceEquals(next, _baseStyle)) return;
        var previous = _baseStyle;
        _baseStyle = next;
        // Swap only our own base style. A themed style built on the previous base is rebuilt on
        // the new one by the same reconcile pass; an author's own style is left alone.
        if (ReferenceEquals(Style, previous)) Style = next;
    }

    /// <summary>
    /// Names content that has no UI Automation name after the header, as the Toolkit does, so
    /// Narrator reads "Notifications, toggle switch, on" rather than an unnamed switch. Content
    /// derived directly from <see cref="ButtonBase"/> (it is named by its own content) and text
    /// blocks are left alone, as in the Toolkit.
    /// </summary>
    private void UpdateAccessibleContentName()
    {
        var content = Content as UIElement;
        var name = Header is string { Length: > 0 } headerText ? headerText : null;

        if (_autoNamedContent is not null && (!ReferenceEquals(_autoNamedContent, content) || name != _autoName))
        {
            if (AutomationProperties.GetName(_autoNamedContent) == _autoName)
                _autoNamedContent.ClearValue(AutomationProperties.NameProperty);
            _autoNamedContent = null;
            _autoName = null;
        }

        if (name is null || content is null || _autoNamedContent is not null) return;
        if (!string.IsNullOrEmpty(AutomationProperties.GetName(content))) return;
        if (content.GetType().BaseType == typeof(ButtonBase) || content is TextBlock) return;

        AutomationProperties.SetName(content, name);
        _autoNamedContent = content;
        _autoName = name;
    }

    private static bool IsNullOrEmpty(object? value) => value is null || value is string { Length: 0 };
}

/// <summary>
/// UI Automation peer of <see cref="SettingsCardControl"/>, ported from the Toolkit's
/// <c>SettingsCardAutomationPeer</c>. A clickable card is a <c>Button</c> named after its
/// header that supports Invoke; any other card is an unnamed <c>Group</c>, because it never
/// takes focus (its content carries the name instead).
/// </summary>
/// <remarks>
/// The Toolkit returns this peer for the Invoke pattern but never implements
/// <see cref="IInvokeProvider"/>, so its clickable card exposes no working Invoke. This port
/// implements it.
/// </remarks>
internal sealed partial class SettingsCardAutomationPeer : ButtonBaseAutomationPeer, IInvokeProvider
{
    // UIA_E_ELEMENTNOTENABLED: the HRESULT UI Automation expects from Invoke on a disabled element.
    private const int ElementNotEnabled = unchecked((int)0x80040200);

    public SettingsCardAutomationPeer(SettingsCardControl owner)
        : base(owner)
    {
    }

    private SettingsCardControl Card => (SettingsCardControl)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => Card.IsClickEnabled ? AutomationControlType.Button : AutomationControlType.Group;

    protected override string GetClassNameCore() => "SettingsCard";

    protected override string GetNameCore()
    {
        // Only a clickable card is named after its header: any other card never takes focus.
        if (Card.IsClickEnabled)
        {
            var name = AutomationProperties.GetName(Card);
            if (!string.IsNullOrEmpty(name)) return name;
            if (Card.Header is string { Length: > 0 } header) return header;
        }
        return base.GetNameCore();
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Invoke)
            return Card.IsClickEnabled ? this : null;
        return base.GetPatternCore(patternInterface);
    }

    public void Invoke()
    {
        if (!IsEnabled())
            throw new COMException("The settings card is disabled.", ElementNotEnabled);
        RaiseAutomationEvent(AutomationEvents.InvokePatternOnInvoked);
        Card.RaiseInvoked();
    }
}
