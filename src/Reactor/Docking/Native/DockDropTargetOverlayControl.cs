using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.3 — Reactor-native drop-target overlay.
//
//  Translates WinUI.Dock's DockTargetButton + Preview.xaml composition into
//  a single self-contained Reactor primitive. The overlay:
//    • renders 9 target buttons (5 split + 4 edge — see DockTarget enum);
//    • each button is at minimum 44×44 DIP (spec §8.7 WCAG 2.5.5);
//    • exposes a focusable, arrow-key-navigable surface (spec §8.7) —
//      arrow keys move between buttons in spatial order; Enter confirms;
//      Esc dismisses;
//    • paints a preview rectangle for the currently-hovered target
//      (replaces upstream Preview.xaml.cs);
//    • respects reduced-motion: when UISettings.AnimationsEnabled = false,
//      the hover-preview snaps without easing (no animation is wired today
//      either, so this is satisfied by construction; the check is kept so
//      future animations bypass when the user disables them);
//    • exposes each button with AT role Button + a localized name string
//      ("Dock left", "Split right", "Add as tab"). Resource keys live in
//      Docking.* once §2.21 wires Loc generation; for now the strings are
//      inline English with the same key shape (matches §2.5 side strip
//      behavior pending the full Intl pass).
//
//  The control is z-priority placed *over* the dock content. The §2.3
//  spec calls for a tooltip-but-below-dialogs slot via spec 036's overlay
//  priority enum; that enum doesn't exist yet, so for P2 we rely on the
//  Grid-overlay pattern in DockHostNativeComponent (same row/column ⇒
//  later children paint above earlier ones), which matches the upstream
//  WinUI.Dock layout.
//
//  Hover-state perf: HitTestForTarget is a 9-iteration linear scan over
//  rect-contains checks against cached button bounds. No allocations on
//  pointer-move (no LINQ, no boxing). Hit-test cache is rebuilt only on
//  size change. Spec §8.1 budget is ≤ 2 ms per pointer-move — this path
//  is comfortably inside that.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Args raised when the overlay's hovered target changes or is confirmed
/// (click / Enter / drop). <see cref="Target"/> is null when no target is
/// currently hovered. Allocation-bounded — the args record is recreated on
/// each event, but the hot pointer-move path does an early-return before
/// allocating when the hovered target hasn't changed.
/// </summary>
internal sealed class DockDropTargetEventArgs : EventArgs
{
    public DockDropTargetEventArgs(DockTarget? target, bool confirmed)
    {
        Target = target;
        Confirmed = confirmed;
    }

    /// <summary>The target under the pointer / focus (null = none).</summary>
    public DockTarget? Target { get; }

    /// <summary>
    /// True when the gesture confirms the target (click, Enter, or drop).
    /// False for hover-state updates.
    /// </summary>
    public bool Confirmed { get; }
}

/// <summary>
/// Determines which target subset the overlay surfaces.
/// <see cref="Host"/> is the full 9-target cluster (5 inner splits + 4
/// outer dock edges) painted at manager scope. <see cref="GroupInner"/>
/// shows only the 5 inner targets (Center + Split L/T/R/B) — used by
/// the per-tab-group overlay so the user can drop a tab into a specific
/// group's center, or split that group on any side.
/// </summary>
internal enum DockDropOverlayMode
{
    /// <summary>Manager-scope 9-target overlay (5 inner + 4 outer edges).</summary>
    Host,
    /// <summary>Tab-group-scope 5-target overlay (Center + 4 splits).</summary>
    GroupInner,
    /// <summary>
    /// Floating-window-scope single-target overlay — only the Center button
    /// surfaces (add-as-tab). Spec 045 §4.2 / §4.3: floating windows in the
    /// tabs-in-titlebar layout don't host splits (would require collapsing
    /// the titlebar pattern back to a standard chrome+content layout), so
    /// the cross-window dock-in path is intentionally limited to Center.
    /// </summary>
    CenterOnly,
}

/// <summary>
/// Spec 045 §2.3 drop-target overlay — 9 targets + hover preview rectangle.
/// </summary>
internal sealed partial class DockDropTargetOverlayControl : Grid
{
    /// <summary>
    /// Each target button is at minimum 44 DIP per WCAG 2.5.5 (spec §8.7).
    /// The visual content of the button (the icon area) is centered inside
    /// the touch target.
    /// </summary>
    public const double ButtonSizeDip = 44.0;

    /// <summary>
    /// Side-anchored targets (DockLeft/Right/Top/Bottom) preview rectangle
    /// occupies this fraction of the host along the anchor axis.
    /// </summary>
    public const double EdgePreviewFraction = 0.30;

    /// <summary>
    /// Center cluster gap from cluster center to each split-edge target.
    /// 48 DIP keeps cluster total at ~140 DIP wide (44 + 48 + 44 + ...).
    /// </summary>
    private const double ClusterGapDip = 4.0;

    private readonly Border _previewRect;
    private readonly (DockTarget Target, Border Button, Rectangle Visual)[] _buttons;
    private DockTarget? _hoveredTarget;
    private DockTarget? _focusedTarget;
    private bool _animationsEnabled = true;
    private DockDropOverlayMode _mode = DockDropOverlayMode.Host;

    /// <summary>
    /// Spec 045 §2.3 — overlay scope. Changing the mode hides / shows
    /// the outer dock-edge buttons (DockLeft/Right/Top/Bottom).
    /// <see cref="DockDropOverlayMode.GroupInner"/> is used by the
    /// per-tab-group overlay (5 inner targets only).
    /// </summary>
    public DockDropOverlayMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            ApplyModeVisibility();
        }
    }
    private KeyEventHandler? _globalEscapeHandler;
    private UIElement? _globalEscapeTarget;

    public event EventHandler<DockDropTargetEventArgs>? TargetHovered;
    public event EventHandler<DockDropTargetEventArgs>? TargetConfirmed;
    public event EventHandler? OverlayDismissed;

    public DockDropTargetOverlayControl()
    {
        Background = new SolidColorBrush(Color.FromArgb(0x00, 0, 0, 0));
        IsHitTestVisible = true;
        // AllowDrop so DragEnter/DragOver/Drop fire on us during a WinUI
        // drag operation. During a tab drag, PointerMoved/Tapped on
        // unrelated elements do NOT fire — the pointer is captured by
        // the drag operation and only AllowDrop=true elements under the
        // pointer receive drag events. Matches upstream DockTargetButton.
        AllowDrop = true;
        // Focus root: arrow-key nav stays inside the overlay until Esc.
        IsTabStop = false;
        AutomationProperties.SetName(this, DockingStrings.Get(DockingStringKeys.DropTargetHostLandmark));
        AutomationProperties.SetLandmarkType(this, AutomationLandmarkType.Custom);

        // Preview rectangle — semi-transparent fill with active border,
        // matching upstream WinUI.Dock's PreviewStyle. Spec 045 §2.22 —
        // chrome resolves theme brushes so high-contrast keeps the
        // preview legible (system accent for border + accent-fill with
        // explicit Opacity for the body). Falls back to literal ARGB
        // when no Application instance is available.
        _previewRect = new Border
        {
            Background = ThemedBrush(
                "SystemControlBackgroundAccentBrush",
                Color.FromArgb(0xFF, 0x33, 0x99, 0xFF)),
            BorderBrush = ThemedBrush(
                "SystemControlHighlightAccentBrush",
                Color.FromArgb(0xFF, 0x33, 0x99, 0xFF)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(2),
            Opacity = 0.30,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Children.Add(_previewRect);

        _buttons =
        [
            BuildButton(DockTarget.Center,      HorizontalAlignment.Center, VerticalAlignment.Center, clusterRow: 1, clusterCol: 1),
            BuildButton(DockTarget.SplitLeft,   HorizontalAlignment.Center, VerticalAlignment.Center, clusterRow: 1, clusterCol: 0),
            BuildButton(DockTarget.SplitTop,    HorizontalAlignment.Center, VerticalAlignment.Center, clusterRow: 0, clusterCol: 1),
            BuildButton(DockTarget.SplitRight,  HorizontalAlignment.Center, VerticalAlignment.Center, clusterRow: 1, clusterCol: 2),
            BuildButton(DockTarget.SplitBottom, HorizontalAlignment.Center, VerticalAlignment.Center, clusterRow: 2, clusterCol: 1),
            BuildButton(DockTarget.DockLeft,    HorizontalAlignment.Left,   VerticalAlignment.Center, clusterRow: -1, clusterCol: -1),
            BuildButton(DockTarget.DockTop,     HorizontalAlignment.Center, VerticalAlignment.Top,    clusterRow: -1, clusterCol: -1),
            BuildButton(DockTarget.DockRight,   HorizontalAlignment.Right,  VerticalAlignment.Center, clusterRow: -1, clusterCol: -1),
            BuildButton(DockTarget.DockBottom,  HorizontalAlignment.Center, VerticalAlignment.Bottom, clusterRow: -1, clusterCol: -1),
        ];

        ReadAnimationsSetting();
        ApplyModeVisibility();

        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
        // Spec 045 §2.4 — drag-aware events. Fire during a WinUI tab
        // drag where pointer events are captured by the drag operation
        // and would otherwise never reach this control.
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown), handledEventsToo: true);
        SizeChanged += (_, _) => UpdateClusterLayout();
        // Window-level Esc listener (§2.4). The overlay's own KeyDown
        // doesn't fire during a TabView tab drag — focus stays with the
        // dragged TabViewItem. Hook the XamlRoot's content KeyDown so
        // Esc always reaches us. Attached on Loaded so XamlRoot exists.
        Loaded += (_, _) => HookGlobalEscape();
        Unloaded += (_, _) => UnhookGlobalEscape();
    }

    private void HookGlobalEscape()
    {
        if (_globalEscapeHandler is not null) return;
        if (XamlRoot?.Content is not UIElement root) return;
        _globalEscapeTarget = root;
        _globalEscapeHandler = OnGlobalKeyDown;
        root.AddHandler(KeyDownEvent, _globalEscapeHandler, handledEventsToo: true);
    }

    private void UnhookGlobalEscape()
    {
        if (_globalEscapeHandler is null || _globalEscapeTarget is null) return;
        _globalEscapeTarget.RemoveHandler(KeyDownEvent, _globalEscapeHandler);
        _globalEscapeHandler = null;
        _globalEscapeTarget = null;
    }

    /// <summary>
    /// Reconciler-unmount hook — Reactor unmount is the reliable lifecycle
    /// boundary; relying on WinUI <c>Unloaded</c> alone can leak the global
    /// Esc handler when the visual tree is replaced under a drag/popup.
    /// Idempotent.
    /// </summary>
    internal void DetachGlobalHandlers() => UnhookGlobalEscape();

    private void ApplyModeVisibility()
    {
        // Two-overlay architecture (spec 045 §2.3):
        //   • Host mode: only the 4 outer Dock-edge buttons (DockL/T/R/B)
        //     are visible. The 5 inner cluster buttons (Center + Split
        //     L/T/R/B) are hidden — per-group overlays handle those at
        //     each tab group's bounds. The Grid Background is null so
        //     drag events at non-button positions fall through to the
        //     underlying per-group overlay.
        //   • GroupInner mode: only the 5 inner cluster buttons are
        //     visible, but they start `Collapsed` and only appear once
        //     the drag pointer enters the overlay (DragEnter). This
        //     keeps the visual clutter down — only the group the user
        //     is dragging INTO surfaces its targets. The Grid keeps
        //     its transparent Background so it can catch DragEnter
        //     across its full area.
        if (_buttons is null) return;
        bool hostMode = _mode == DockDropOverlayMode.Host;
        bool centerOnly = _mode == DockDropOverlayMode.CenterOnly;
        foreach (var entry in _buttons)
        {
            bool isEdge = entry.Target is DockTarget.DockLeft or DockTarget.DockRight
                or DockTarget.DockTop or DockTarget.DockBottom;
            if (hostMode)
            {
                // Host: edges always visible (they're the only buttons
                // surfaced); inner cluster always hidden.
                entry.Button.Visibility = isEdge ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (centerOnly)
            {
                // CenterOnly (spec 045 §4.2/§4.3 floating-window scope):
                // edges always collapsed; inner cluster except Center
                // permanently hidden; Center hidden until DragEnter
                // reveals it.
                entry.Button.Visibility = (isEdge || entry.Target != DockTarget.Center)
                    ? Visibility.Collapsed
                    : (_groupOverlayRevealed ? Visibility.Visible : Visibility.Collapsed);
            }
            else
            {
                // GroupInner: edges always collapsed; inner cluster
                // hidden by default — DragEnter unmasks it.
                entry.Button.Visibility = isEdge
                    ? Visibility.Collapsed
                    : (_groupOverlayRevealed ? Visibility.Visible : Visibility.Collapsed);
            }
        }
        // Host mode passes drag events through non-button regions so
        // the underlying per-group overlay sees DragEnter when the
        // pointer is over a tab group.
        Background = hostMode
            ? null
            : new SolidColorBrush(Color.FromArgb(0x00, 0, 0, 0));
    }

    private bool _groupOverlayRevealed;
    private void SetGroupOverlayRevealed(bool value)
    {
        if (_groupOverlayRevealed == value) return;
        _groupOverlayRevealed = value;
        ApplyModeVisibility();
    }

    private void OnGlobalKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        // Drag is in flight if our state still flags a session — fire
        // OverlayDismissed which the host's OnDismiss handler routes
        // through DockDragSession.Cancel() + clears dragActive. The
        // subsequent TabDragCompleted with DropResult=None then sees
        // session.IsActive == false and skips the tear-out path.
        OverlayDismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The target currently under the pointer (null = none).</summary>
    public DockTarget? HoveredTarget => _hoveredTarget;

    /// <summary>The button-with-keyboard-focus (null = none).</summary>
    public DockTarget? FocusedTarget => _focusedTarget;

    /// <summary>
    /// Visible bounds of the preview rectangle (DIP, relative to this
    /// overlay). <c>Width</c> / <c>Height</c> are 0 when collapsed.
    /// </summary>
    public Rect PreviewBounds
    {
        get
        {
            if (_previewRect.Visibility != Visibility.Visible) return Rect.Empty;
            return new Rect(
                _previewRect.Margin.Left,
                _previewRect.Margin.Top,
                _previewRect.Width,
                _previewRect.Height);
        }
    }

    /// <summary>
    /// Reads <see cref="UISettings.AnimationsEnabled"/> once at construction
    /// and applies it. Spec §8.7 reduced-motion: when off, hover preview
    /// snaps without ease; no animations are wired today, so this is a
    /// future-proof check.
    /// </summary>
    private void ReadAnimationsSetting()
    {
        _animationsEnabled = new UISettings().AnimationsEnabled;
    }

    private (DockTarget Target, Border Button, Rectangle Visual) BuildButton(
        DockTarget target,
        HorizontalAlignment hAlign,
        VerticalAlignment vAlign,
        int clusterRow,
        int clusterCol)
    {
        // Outer outlined rectangle — the button face. Stroke + fill are
        // applied by ApplyVisualState. Spec 045 §2.22 — themed brushes
        // so high-contrast renders the button against a clearly
        // differentiated background; literal-ARGB fallback only when
        // no Application instance is available.
        var visual = new Rectangle
        {
            Width = ButtonSizeDip - 8,
            Height = ButtonSizeDip - 8,
            Fill = ThemedBrush(
                "SystemControlBackgroundChromeMediumLowBrush",
                Color.FromArgb(0xCC, 0x20, 0x20, 0x20)),
            Stroke = ThemedBrush(
                "SystemControlHighlightAccentBrush",
                Color.FromArgb(0xFF, 0x80, 0xBB, 0xFF)),
            StrokeThickness = 1,
            RadiusX = 4,
            RadiusY = 4,
        };
        ApplyVisualState(visual, target);

        // Inner directional indicator — a small filled rectangle pinned to
        // the edge that matches the dock direction. HorizontalAlignment /
        // VerticalAlignment carry FlowDirection inheritance, so under
        // RTL the Left-anchored indicator auto-mirrors to the right edge
        // and DockLeft / SplitLeft glyphs read correctly (spec §2.23
        // drop-target overlay mirror).
        var indicator = BuildDirectionIndicator(target);

        var glyph = new Grid
        {
            Width = ButtonSizeDip - 8,
            Height = ButtonSizeDip - 8,
        };
        glyph.Children.Add(visual);
        if (indicator is not null) glyph.Children.Add(indicator);

        var button = new Border
        {
            Width = ButtonSizeDip,
            Height = ButtonSizeDip,
            HorizontalAlignment = hAlign,
            VerticalAlignment = vAlign,
            Background = new SolidColorBrush(Color.FromArgb(0x00, 0, 0, 0)),
            Child = glyph,
            IsTabStop = true,
            UseSystemFocusVisuals = true,
        };
        AutomationProperties.SetName(button, GetLocalizedName(target));
        AutomationProperties.SetHelpText(button, GetLocalizedName(target));
        AutomationProperties.SetAccessibilityView(button, AccessibilityView.Control);
        ToolTipService.SetToolTip(button, GetLocalizedName(target));
        // Cluster positioning is computed lazily in UpdateClusterLayout
        // (size-change driven); we just record the cluster-grid coords
        // on the Border via Tag for the layout pass to consult.
        button.Tag = new ClusterSlot(clusterRow, clusterCol);
        button.PointerEntered += (_, _) => SetHovered(target);
        button.PointerExited += (_, _) =>
        {
            if (_hoveredTarget == target) SetHovered(null);
        };
        button.Tapped += (_, e) =>
        {
            ConfirmTarget(target);
            e.Handled = true;
        };
        button.GotFocus += (_, _) =>
        {
            _focusedTarget = target;
            SetHovered(target);
        };
        Children.Add(button);
        return (target, button, visual);
    }

    /// <summary>
    /// Re-position the 5 split-cluster buttons inside the host on every
    /// size change. The cluster's 3×3 grid sits centered. Edge buttons
    /// are already positioned via Horizontal/VerticalAlignment so they
    /// reflow automatically.
    /// </summary>
    private void UpdateClusterLayout()
    {
        var hostW = ActualWidth;
        var hostH = ActualHeight;
        if (hostW < 1 || hostH < 1) return;

        var clusterCenterX = hostW / 2;
        var clusterCenterY = hostH / 2;
        var step = ButtonSizeDip + ClusterGapDip;
        foreach (var entry in _buttons)
        {
            if (entry.Button.Tag is not ClusterSlot slot) continue;
            if (slot.Row < 0 || slot.Col < 0) continue; // edge button: skip

            var dx = (slot.Col - 1) * step;
            var dy = (slot.Row - 1) * step;
            entry.Button.HorizontalAlignment = HorizontalAlignment.Left;
            entry.Button.VerticalAlignment = VerticalAlignment.Top;
            entry.Button.Margin = new Thickness(
                clusterCenterX - ButtonSizeDip / 2 + dx,
                clusterCenterY - ButtonSizeDip / 2 + dy,
                0, 0);
        }
        // If a target is already focused / hovered, refresh the preview
        // rect so it tracks the new layout.
        if (_hoveredTarget is DockTarget t) UpdatePreview(t);
        else _previewRect.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Builds the directional indicator overlay for a target — a small
    /// filled rectangle pinned to the edge that corresponds to where the
    /// dragged pane will land. Returns null for Center (the outer fill
    /// alone conveys "drop as a tab here"). Spec §2.3 + §2.23 (RTL mirror).
    /// </summary>
    /// <remarks>
    /// The indicator is positioned via <see cref="HorizontalAlignment"/> /
    /// <see cref="VerticalAlignment"/> so WinUI's FlowDirection inheritance
    /// auto-mirrors it under RTL: a <c>HorizontalAlignment.Left</c>
    /// indicator on the DockLeft glyph paints at the right edge of the
    /// button when the overlay's FlowDirection resolves to RightToLeft.
    /// </remarks>
    private static Rectangle? BuildDirectionIndicator(DockTarget target)
    {
        // Indicator color matches the active stroke so the side-stripe
        // reads as a continuation of the outline. Spec 045 §2.22 —
        // themed accent brush for HC legibility; ARGB fallback only
        // when no Application instance is available.
        var fill = ThemedBrush(
            "SystemControlHighlightAccentBrush",
            Color.FromArgb(0xFF, 0x33, 0x99, 0xFF));
        const double thin = 10.0;   // narrow axis (side-stripe thickness)
        const double full = ButtonSizeDip - 8;
        return target switch
        {
            // Inner-cluster splits — thin stripe on the matching edge.
            DockTarget.SplitLeft   => new Rectangle { Width = thin, Height = full, Fill = fill,
                HorizontalAlignment = HorizontalAlignment.Left,  VerticalAlignment = VerticalAlignment.Stretch },
            DockTarget.SplitRight  => new Rectangle { Width = thin, Height = full, Fill = fill,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch },
            DockTarget.SplitTop    => new Rectangle { Width = full, Height = thin, Fill = fill,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top },
            DockTarget.SplitBottom => new Rectangle { Width = full, Height = thin, Fill = fill,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Bottom },
            // Edge docks — same alignment, slightly wider stripe so they
            // visually outrank the inner-cluster targets at a glance.
            DockTarget.DockLeft    => new Rectangle { Width = thin + 2, Height = full, Fill = fill,
                HorizontalAlignment = HorizontalAlignment.Left,  VerticalAlignment = VerticalAlignment.Stretch },
            DockTarget.DockRight   => new Rectangle { Width = thin + 2, Height = full, Fill = fill,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch },
            DockTarget.DockTop     => new Rectangle { Width = full, Height = thin + 2, Fill = fill,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top },
            DockTarget.DockBottom  => new Rectangle { Width = full, Height = thin + 2, Fill = fill,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Bottom },
            _ => null,
        };
    }

    /// <summary>
    /// Draws a small glyph inside the icon area that reflects the target
    /// orientation. Matches DockTargetButton.xaml visual contract.
    /// </summary>
    private static void ApplyVisualState(Rectangle visual, DockTarget target)
    {
        // The Rectangle holds the outer outline; for Phase 2 first cut we
        // paint a single accent dash on the side that corresponds to the
        // target's docking edge by varying StrokeDashArray. A richer per-
        // target glyph (matching upstream's FontIcon dashed-line + filled
        // rectangle pair) lands when the icon set is folded in.
        switch (target)
        {
            case DockTarget.Center:
                visual.Fill = ThemedBrush(
                    "SystemControlBackgroundAccentBrush",
                    Color.FromArgb(0xCC, 0x33, 0x77, 0xCC));
                break;
            case DockTarget.SplitLeft:
            case DockTarget.DockLeft:
                visual.Stroke = ThemedBrush(
                    "SystemControlHighlightAccentBrush",
                    Color.FromArgb(0xFF, 0x33, 0x99, 0xFF));
                visual.StrokeDashArray = new Microsoft.UI.Xaml.Media.DoubleCollection { 3, 1 };
                break;
            case DockTarget.SplitRight:
            case DockTarget.DockRight:
                visual.Stroke = ThemedBrush(
                    "SystemControlHighlightAccentBrush",
                    Color.FromArgb(0xFF, 0x33, 0x99, 0xFF));
                visual.StrokeDashArray = new Microsoft.UI.Xaml.Media.DoubleCollection { 3, 1 };
                break;
            case DockTarget.SplitTop:
            case DockTarget.DockTop:
                visual.Stroke = ThemedBrush(
                    "SystemControlHighlightAccentBrush",
                    Color.FromArgb(0xFF, 0x33, 0x99, 0xFF));
                visual.StrokeDashArray = new Microsoft.UI.Xaml.Media.DoubleCollection { 3, 1 };
                break;
            case DockTarget.SplitBottom:
            case DockTarget.DockBottom:
                visual.Stroke = ThemedBrush(
                    "SystemControlHighlightAccentBrush",
                    Color.FromArgb(0xFF, 0x33, 0x99, 0xFF));
                visual.StrokeDashArray = new Microsoft.UI.Xaml.Media.DoubleCollection { 3, 1 };
                break;
        }
    }

    /// <summary>
    /// Compute the bounds of the preview rectangle for a given target,
    /// in DIP relative to this overlay. Exposed for tests and the §2.4
    /// drag pipeline to query without going through a live hover.
    /// </summary>
    internal static Rect ComputePreviewBounds(DockTarget target, double hostW, double hostH)
    {
        if (hostW < 1 || hostH < 1) return Rect.Empty;
        return target switch
        {
            DockTarget.Center       => new Rect(0, 0, hostW, hostH),
            DockTarget.SplitLeft    => new Rect(0, 0, hostW / 2, hostH),
            DockTarget.SplitRight   => new Rect(hostW / 2, 0, hostW / 2, hostH),
            DockTarget.SplitTop     => new Rect(0, 0, hostW, hostH / 2),
            DockTarget.SplitBottom  => new Rect(0, hostH / 2, hostW, hostH / 2),
            DockTarget.DockLeft     => new Rect(0, 0, hostW * EdgePreviewFraction, hostH),
            DockTarget.DockRight    => new Rect(hostW - hostW * EdgePreviewFraction, 0, hostW * EdgePreviewFraction, hostH),
            DockTarget.DockTop      => new Rect(0, 0, hostW, hostH * EdgePreviewFraction),
            DockTarget.DockBottom   => new Rect(0, hostH - hostH * EdgePreviewFraction, hostW, hostH * EdgePreviewFraction),
            _ => Rect.Empty,
        };
    }

    /// <summary>
    /// Localized AT name for each target. Routes through
    /// <see cref="DockingStrings.Get"/> so apps that have installed a
    /// resolver receive their translation; otherwise English defaults
    /// match the entries in <c>Reactor.Docking.resw</c>. Spec §8.6 / §2.21.
    /// </summary>
    internal static string GetLocalizedName(DockTarget target) => target switch
    {
        DockTarget.Center       => DockingStrings.Get(DockingStringKeys.DropTargetCenter),
        DockTarget.SplitLeft    => DockingStrings.Get(DockingStringKeys.DropTargetSplitLeft),
        DockTarget.SplitRight   => DockingStrings.Get(DockingStringKeys.DropTargetSplitRight),
        DockTarget.SplitTop     => DockingStrings.Get(DockingStringKeys.DropTargetSplitTop),
        DockTarget.SplitBottom  => DockingStrings.Get(DockingStringKeys.DropTargetSplitBottom),
        DockTarget.DockLeft     => DockingStrings.Get(DockingStringKeys.DropTargetDockLeft),
        DockTarget.DockRight    => DockingStrings.Get(DockingStringKeys.DropTargetDockRight),
        DockTarget.DockTop      => DockingStrings.Get(DockingStringKeys.DropTargetDockTop),
        DockTarget.DockBottom   => DockingStrings.Get(DockingStringKeys.DropTargetDockBottom),
        _ => "Dock target",
    };

    private void UpdatePreview(DockTarget target)
    {
        var rect = ComputePreviewBounds(target, ActualWidth, ActualHeight);
        if (rect.Width < 1 || rect.Height < 1)
        {
            _previewRect.Visibility = Visibility.Collapsed;
            return;
        }
        _previewRect.Width = rect.Width;
        _previewRect.Height = rect.Height;
        _previewRect.Margin = new Thickness(rect.X, rect.Y, 0, 0);
        _previewRect.HorizontalAlignment = HorizontalAlignment.Left;
        _previewRect.VerticalAlignment = VerticalAlignment.Top;
        _previewRect.Visibility = Visibility.Visible;
        // The leading-edge margin path is the reduced-motion-safe path —
        // no transform animations needed. _animationsEnabled is read on
        // construction; future easing here gates on the flag.
        _ = _animationsEnabled;
    }

    /// <summary>
    /// Hit-test a pointer position (in overlay-local DIPs) against the 9
    /// target buttons. O(9) linear scan; zero allocations per call.
    /// Returns null when the pointer is outside every button.
    /// </summary>
    internal DockTarget? HitTestForTarget(Point local)
    {
        for (int i = 0; i < _buttons.Length; i++)
        {
            var btn = _buttons[i].Button;
            // Skip collapsed buttons — happens when Mode = GroupInner
            // hides the 4 outer dock-edge targets.
            if (btn.Visibility != Visibility.Visible) continue;
            // GetBoundsRelativeTo would round-trip through a transform; for
            // the hot path we read Margin + Width directly. Margin is set
            // by UpdateClusterLayout (split cluster) or implicit zero
            // (edge buttons positioned via Horizontal/VerticalAlignment).
            var rect = GetBoundsLocal(btn);
            if (rect.Contains(local)) return _buttons[i].Target;
        }
        return null;
    }

    private Rect GetBoundsLocal(Border button)
    {
        // Edge buttons rely on alignment, not Margin — compute their rect
        // from alignment + size against host extent. For cluster buttons
        // the margin already encodes the absolute position.
        var w = button.Width;
        var h = button.Height;
        if (button.Tag is ClusterSlot { Row: >= 0 })
        {
            return new Rect(button.Margin.Left, button.Margin.Top, w, h);
        }
        var hostW = ActualWidth;
        var hostH = ActualHeight;
        double x = button.HorizontalAlignment switch
        {
            HorizontalAlignment.Left => 0,
            HorizontalAlignment.Right => Math.Max(0, hostW - w),
            HorizontalAlignment.Stretch => 0,
            _ => Math.Max(0, (hostW - w) / 2),
        };
        double y = button.VerticalAlignment switch
        {
            VerticalAlignment.Top => 0,
            VerticalAlignment.Bottom => Math.Max(0, hostH - h),
            VerticalAlignment.Stretch => 0,
            _ => Math.Max(0, (hostH - h) / 2),
        };
        return new Rect(x, y, w, h);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(this).Position;
        var newHover = HitTestForTarget(p);
        if (newHover != _hoveredTarget) SetHovered(newHover);
        e.Handled = false;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_hoveredTarget is not null) SetHovered(null);
    }

    // ── Drag-mode handlers (spec 045 §2.4) ─────────────────────────────
    //
    // During a TabView tab drag, WinUI captures the pointer and routes
    // events as Drag* on AllowDrop=true elements under it. Plain
    // PointerMoved/Tapped do not fire on the overlay — so the hover
    // preview, target highlight, and click-to-confirm all need a
    // drag-aware path.

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        // Per-group / center-only overlay: reveal its inner button(s)
        // only while the drag is over THIS surface. Reduces visual
        // clutter to a single overlay at a time. Host-level overlay is
        // unaffected (its 4 outer Dock-edge buttons are always visible).
        if (_mode == DockDropOverlayMode.GroupInner || _mode == DockDropOverlayMode.CenterOnly)
            SetGroupOverlayRevealed(true);
        var p = e.GetPosition(this);
        var target = HitTestForTarget(p);
        if (target is not null && target != _hoveredTarget) SetHovered(target);
        // Move signals to WinUI that this surface is a willing drop site;
        // without it the cursor stays "no drop" and Drop never fires.
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
        e.DragUIOverride.IsGlyphVisible = false;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        // Reveal-while-dragging: in case DragEnter was suppressed by
        // capture timing (e.g. fast drag entered the area), DragOver
        // is the safer signal for revealing.
        if (_mode == DockDropOverlayMode.GroupInner || _mode == DockDropOverlayMode.CenterOnly)
            SetGroupOverlayRevealed(true);
        var p = e.GetPosition(this);
        var target = HitTestForTarget(p);
        if (target != _hoveredTarget) SetHovered(target);
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
        e.DragUIOverride.IsGlyphVisible = false;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (_mode == DockDropOverlayMode.GroupInner || _mode == DockDropOverlayMode.CenterOnly)
            SetGroupOverlayRevealed(false);
        if (_hoveredTarget is not null) SetHovered(null);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(this);
        var target = HitTestForTarget(p);
        if (target is DockTarget t)
        {
            // Mark the drop as accepted BEFORE invoking the confirm
            // callback so the TabView's TabDragCompleted sees
            // DropResult=Move (and the host's wasOutside check skips
            // the tear-out path).
            e.AcceptedOperation = DataPackageOperation.Move;
            ConfirmTarget(t);
        }
        else
        {
            // Drop landed in the overlay's bounds but missed every
            // button (e.g. user released on the tab strip or empty
            // body). Accept the drop so WinUI sees DropResult=Move and
            // the source TabView's TabDragCompleted does NOT trigger
            // the tear-out path (otherwise a missed drop turns the
            // dragged tab into a floating window — surprising and
            // expensive — instead of cancelling). Fire OverlayDismissed
            // so the host clears drag state.
            e.AcceptedOperation = DataPackageOperation.Move;
            if (_mode == DockDropOverlayMode.GroupInner || _mode == DockDropOverlayMode.CenterOnly)
                SetGroupOverlayRevealed(false);
            OverlayDismissed?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }

    private void SetHovered(DockTarget? target)
    {
        if (_hoveredTarget == target) return;
        _hoveredTarget = target;
        if (target is DockTarget t) UpdatePreview(t);
        else _previewRect.Visibility = Visibility.Collapsed;
        TargetHovered?.Invoke(this, new DockDropTargetEventArgs(target, confirmed: false));
    }

    private void ConfirmTarget(DockTarget target)
    {
        TargetConfirmed?.Invoke(this, new DockDropTargetEventArgs(target, confirmed: true));
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                OverlayDismissed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            case VirtualKey.Enter when _focusedTarget is DockTarget current:
                ConfirmTarget(current);
                e.Handled = true;
                return;
            case VirtualKey.Left:
            case VirtualKey.Right:
            case VirtualKey.Up:
            case VirtualKey.Down:
                MoveFocus(e.Key);
                e.Handled = true;
                return;
        }
    }

    /// <summary>
    /// Spatial arrow-key navigation between the 9 targets. The split
    /// cluster (5 buttons) forms a cross; arrow keys move along the
    /// arms. From an edge button (DockLeft/Right/Top/Bottom), arrow
    /// keys move INTO the cluster on the matching side.
    /// </summary>
    private void MoveFocus(VirtualKey key)
    {
        var current = _focusedTarget ?? _buttons[0].Target;
        var next = NextFocus(current, key);
        FocusTarget(next);
    }

    internal static DockTarget NextFocus(DockTarget current, VirtualKey key) => (current, key) switch
    {
        // Cluster cross navigation
        (DockTarget.Center, VirtualKey.Left)   => DockTarget.SplitLeft,
        (DockTarget.Center, VirtualKey.Right)  => DockTarget.SplitRight,
        (DockTarget.Center, VirtualKey.Up)     => DockTarget.SplitTop,
        (DockTarget.Center, VirtualKey.Down)   => DockTarget.SplitBottom,
        (DockTarget.SplitLeft, VirtualKey.Right) => DockTarget.Center,
        (DockTarget.SplitLeft, VirtualKey.Left)  => DockTarget.DockLeft,
        (DockTarget.SplitRight, VirtualKey.Left)  => DockTarget.Center,
        (DockTarget.SplitRight, VirtualKey.Right) => DockTarget.DockRight,
        (DockTarget.SplitTop, VirtualKey.Down)    => DockTarget.Center,
        (DockTarget.SplitTop, VirtualKey.Up)      => DockTarget.DockTop,
        (DockTarget.SplitBottom, VirtualKey.Up)    => DockTarget.Center,
        (DockTarget.SplitBottom, VirtualKey.Down)  => DockTarget.DockBottom,
        // From edges, the inward arrow returns to the matching cluster arm
        (DockTarget.DockLeft, VirtualKey.Right)  => DockTarget.SplitLeft,
        (DockTarget.DockRight, VirtualKey.Left)  => DockTarget.SplitRight,
        (DockTarget.DockTop, VirtualKey.Down)    => DockTarget.SplitTop,
        (DockTarget.DockBottom, VirtualKey.Up)   => DockTarget.SplitBottom,
        // Sideways from edges wraps along the edge ring
        (DockTarget.DockLeft, VirtualKey.Up)     => DockTarget.DockTop,
        (DockTarget.DockLeft, VirtualKey.Down)   => DockTarget.DockBottom,
        (DockTarget.DockRight, VirtualKey.Up)    => DockTarget.DockTop,
        (DockTarget.DockRight, VirtualKey.Down)  => DockTarget.DockBottom,
        (DockTarget.DockTop, VirtualKey.Left)    => DockTarget.DockLeft,
        (DockTarget.DockTop, VirtualKey.Right)   => DockTarget.DockRight,
        (DockTarget.DockBottom, VirtualKey.Left) => DockTarget.DockLeft,
        (DockTarget.DockBottom, VirtualKey.Right) => DockTarget.DockRight,
        _ => current,
    };

    /// <summary>
    /// Programmatically move focus to a target. Public for the keyboard-
    /// initiated drag mode (Ctrl+Shift+M, §2.10) which enters the overlay
    /// and seeds focus on Center.
    /// </summary>
    internal void FocusTarget(DockTarget target)
    {
        foreach (var entry in _buttons)
        {
            if (entry.Target == target)
            {
                entry.Button.Focus(FocusState.Programmatic);
                _focusedTarget = target;
                SetHovered(target);
                return;
            }
        }
    }

    /// <summary>Test hook — confirm a target without going through pointer/keyboard.</summary>
    internal void ConfirmTargetForTest(DockTarget target) => ConfirmTarget(target);

    /// <summary>Test hook — set the hovered target without a pointer event.</summary>
    internal void SetHoveredForTest(DockTarget? target) => SetHovered(target);

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new DockDropTargetOverlayAutomationPeer(this);

    private sealed partial class DockDropTargetOverlayAutomationPeer : FrameworkElementAutomationPeer
    {
        public DockDropTargetOverlayAutomationPeer(DockDropTargetOverlayControl owner) : base(owner) { }
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => "DockDropTargetOverlay";
        protected override string GetLocalizedControlTypeCore() => "dock target group";
    }

    private readonly record struct ClusterSlot(int Row, int Col);

    // Spec 045 §2.22 — resolve a theme resource brush with a literal
    // ARGB fallback. The lookup walks `Application.Current.Resources`
    // which is the same dictionary high-contrast theme swaps populate,
    // so the overlay chrome updates on a system HC toggle without
    // bespoke wiring.
    private static Brush ThemedBrush(string key, global::Windows.UI.Color fallback)
    {
        if (Application.Current?.Resources is { } res &&
            res.TryGetValue(key, out var v) &&
            v is Brush b)
            return b;
        return new SolidColorBrush(fallback);
    }
}
