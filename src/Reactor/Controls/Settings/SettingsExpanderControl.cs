// Portions of this file are ported from the Windows Community Toolkit
// (https://github.com/CommunityToolkit/Windows, components/SettingsControls/src:
// SettingsExpander/SettingsExpander.cs, SettingsExpander/SettingsExpander.xaml and
// SettingsExpander/SettingsExpanderAutomationPeer.cs).
// Original notice:
//   Licensed to the .NET Foundation under one or more agreements.
//   The .NET Foundation licenses this file to you under the MIT license.
//   See the LICENSE file in the project root for more information.

using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// The native control behind <see cref="SettingsExpanderElement"/> (spec 065): a port of the
/// Toolkit's <c>SettingsExpander</c>. Like the Toolkit control, it hosts a WinUI
/// <see cref="Expander"/> styled as a settings card: the header is a non-clickable
/// <see cref="SettingsCardControl"/>, and the body stacks the items header, the items and
/// the items footer.
/// </summary>
/// <remarks>
/// The Toolkit builds this tree in its control template and template-binds the size, corner
/// and name properties to the inner expander. Here the tree is built in the constructor and
/// those properties are mirrored from code, so the inner expander exists before the first
/// layout and the reconciler can set its expanded state at mount.
/// </remarks>
internal sealed partial class SettingsExpanderControl : UserControl, IReactorBaseStyleHost
{
    private readonly Expander _expander;
    private readonly Grid _itemsRoot;
    private readonly ContentPresenter _itemsHeaderPresenter = new();
    private readonly ContentPresenter _itemsPresenter = new();
    private readonly ContentPresenter _itemsFooterPresenter = new();
    private bool _writingIsExpanded;
    private AnimatedIcon? _chevron;
    private long _chevronTagToken;

    /// <summary>Raised when the user expands (<c>true</c>) or collapses (<c>false</c>) the
    /// expander; not raised for writes through <see cref="IsExpanded"/>.</summary>
    internal event Action<SettingsExpanderControl, bool>? IsExpandedChanged;

    public SettingsExpanderControl()
    {
        Style = SettingsCardResources.ExpanderStyle;

        // Toolkit template: the header SettingsCard.
        HeaderCard = new SettingsCardControl
        {
            Padding = new Thickness(16, 16, 4, 16),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };

        // Toolkit template: the body grid (items header, items, items footer).
        _itemsRoot = new Grid();
        _itemsRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _itemsRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _itemsRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_itemsPresenter, 1);
        Grid.SetRow(_itemsFooterPresenter, 2);
        _itemsRoot.Children.Add(_itemsHeaderPresenter);
        _itemsRoot.Children.Add(_itemsPresenter);
        _itemsRoot.Children.Add(_itemsFooterPresenter);

        _expander = new Expander
        {
            Style = SettingsCardResources.Current.ExpanderExpanderStyle,
            // The outer control positions itself; the expander fills it.
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = HeaderCard,
            Content = _itemsRoot,
        };
        _expander.Expanding += OnExpanding;
        _expander.Collapsed += OnCollapsed;
        _expander.Loaded += OnExpanderLoaded;
        Content = _expander;

        RegisterPropertyChangedCallback(CornerRadiusProperty, OnMirroredPropertyChanged);
        RegisterPropertyChangedCallback(MinWidthProperty, OnMirroredPropertyChanged);
        RegisterPropertyChangedCallback(MinHeightProperty, OnMirroredPropertyChanged);
        RegisterPropertyChangedCallback(HorizontalContentAlignmentProperty, OnMirroredPropertyChanged);
        RegisterPropertyChangedCallback(AutomationProperties.NameProperty, OnMirroredPropertyChanged);
        RegisterPropertyChangedCallback(AutomationProperties.HelpTextProperty, OnMirroredPropertyChanged);
        SyncMirroredProperties();
    }

    /// <summary>The header row: a non-clickable card that holds the header, description, icon
    /// and the header-row content.</summary>
    internal SettingsCardControl HeaderCard { get; }

    internal UIElement? ItemsHeaderChild
    {
        get => _itemsHeaderPresenter.Content as UIElement;
        set => _itemsHeaderPresenter.Content = value;
    }

    internal UIElement? ItemsChild
    {
        get => _itemsPresenter.Content as UIElement;
        set => _itemsPresenter.Content = value;
    }

    internal UIElement? ItemsFooterChild
    {
        get => _itemsFooterPresenter.Content as UIElement;
        set => _itemsFooterPresenter.Content = value;
    }

    internal bool IsExpanded
    {
        get => _expander.IsExpanded;
        set
        {
            if (_expander.IsExpanded == value) return;
            _writingIsExpanded = true;
            try { _expander.IsExpanded = value; }
            finally { _writingIsExpanded = false; }
        }
    }

    Style IReactorBaseStyleHost.BaseStyle => SettingsCardResources.ExpanderStyle;

    string IReactorBaseStyleHost.BaseStyleKey => "ReactorSettingsExpanderStyle";

    string IReactorBaseStyleHost.BaseStyleTargetType => "UserControl";

    /// <summary>The header text when no element header replaces it; the peer's name.</summary>
    internal string? HeaderName => HeaderCard.Header as string;

    /// <summary>Re-reads the header after it changed, for the names mirrored onto the inner expander.</summary>
    internal void OnHeaderChanged() => SyncAutomationName();

    /// <summary>
    /// Switches the header card and the inner expander to the markup for the current Windows
    /// contrast setting; see <see cref="SettingsCardControl.SyncHighContrast"/>.
    /// </summary>
    internal void SyncHighContrast()
    {
        HeaderCard.SyncHighContrast();
        var style = SettingsCardResources.Current.ExpanderExpanderStyle;
        if (ReferenceEquals(_expander.Style, style)) return;
        _expander.Style = style;
        // The new template brings a new chevron; find it after the next layout.
        _expander.LayoutUpdated += OnLayoutUpdatedAfterRestyle;
    }

    private void OnLayoutUpdatedAfterRestyle(object? sender, object e)
    {
        _expander.LayoutUpdated -= OnLayoutUpdatedAfterRestyle;
        HookChevron();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SettingsExpanderAutomationPeer(this);

    private void OnExpanding(Expander sender, ExpanderExpandingEventArgs args) => OnIsExpandedChanged(true);

    private void OnCollapsed(Expander sender, ExpanderCollapsedEventArgs args) => OnIsExpandedChanged(false);

    private void OnIsExpandedChanged(bool isExpanded)
    {
        (FrameworkElementAutomationPeer.FromElement(this) as SettingsExpanderAutomationPeer)?.RaiseExpandedChangedEvent(isExpanded);
        if (!_writingIsExpanded) IsExpandedChanged?.Invoke(this, isExpanded);
    }

    private void OnMirroredPropertyChanged(DependencyObject sender, DependencyProperty dp) => SyncMirroredProperties();

    // The header template's visual states set the chevron's Tag (XamlReader cannot resolve the
    // attached AnimatedIcon.State the Toolkit sets); forward it to the chevron's state.
    private void OnExpanderLoaded(object sender, RoutedEventArgs e) => HookChevron();

    private void HookChevron()
    {
        var chevron = FindDescendant<AnimatedIcon>(_expander, "ExpandCollapseChevron");
        if (ReferenceEquals(chevron, _chevron)) return;
        _chevron?.UnregisterPropertyChangedCallback(TagProperty, _chevronTagToken);
        _chevron = chevron;
        if (chevron is null) return;
        _chevronTagToken = chevron.RegisterPropertyChangedCallback(TagProperty, OnChevronTagChanged);
        ForwardChevronState(chevron);
    }

    private static void OnChevronTagChanged(DependencyObject sender, DependencyProperty dp)
        => ForwardChevronState((AnimatedIcon)sender);

    private static void ForwardChevronState(AnimatedIcon chevron)
    {
        if (chevron.Tag is string state) AnimatedIcon.SetState(chevron, state);
    }

    private static T? FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.Name == name) return match;
            if (FindDescendant<T>(child, name) is { } nested) return nested;
        }
        return null;
    }

    private void SyncMirroredProperties()
    {
        var radius = CornerRadius;
        _expander.CornerRadius = radius;
        // Toolkit CornerRadiusConverter: the body keeps only the bottom corners.
        _itemsRoot.CornerRadius = new CornerRadius(0, 0, radius.BottomRight, radius.BottomLeft);
        _expander.MinWidth = MinWidth;
        _expander.MinHeight = MinHeight;
        _expander.HorizontalContentAlignment = HorizontalContentAlignment;
        SyncAutomationName();
    }

    // The Toolkit names the expander after its header, and its template binds that name (and
    // the help text) to the inner expander, whose template passes it to the header toggle.
    private void SyncAutomationName()
    {
        var name = AutomationProperties.GetName(this);
        if (string.IsNullOrEmpty(name)) name = HeaderName ?? string.Empty;
        AutomationProperties.SetName(_expander, name);
        AutomationProperties.SetHelpText(_expander, AutomationProperties.GetHelpText(this));
    }
}

/// <summary>
/// UI Automation peer of <see cref="SettingsExpanderControl"/>, ported from the Toolkit's
/// <c>SettingsExpanderAutomationPeer</c>: a <c>Group</c> named after the header that raises an
/// expand/collapse state change when the expander opens or closes. The expand and collapse
/// pattern itself is on the inner expander's header button.
/// </summary>
internal sealed partial class SettingsExpanderAutomationPeer : FrameworkElementAutomationPeer
{
    public SettingsExpanderAutomationPeer(SettingsExpanderControl owner)
        : base(owner)
    {
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => "SettingsExpander";

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        if (Owner is SettingsExpanderControl owner)
        {
            var authored = AutomationProperties.GetName(owner);
            if (!string.IsNullOrEmpty(authored)) name = authored;
            else if (owner.HeaderName is { Length: > 0 } header) name = header;
        }
        return name;
    }

    /// <summary>
    /// Raises the expand/collapse state change for this peer. Narrator does not announce it
    /// (https://github.com/microsoft/microsoft-ui-xaml/issues/3469), as in the Toolkit.
    /// </summary>
    internal void RaiseExpandedChangedEvent(bool isExpanded)
    {
        var newState = isExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        var oldState = isExpanded ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded;
        RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, oldState, newState);
    }
}
