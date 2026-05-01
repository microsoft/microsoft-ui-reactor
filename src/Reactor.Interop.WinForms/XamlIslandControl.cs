using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;
using SWF = global::System.Windows.Forms;
using ReactorComponent = Microsoft.UI.Reactor.Core.Component;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Interop.WinForms;

/// <summary>
/// A WinForms control that hosts WinUI/XAML content via DesktopWindowXamlSource (XAML Islands).
/// Place this in any WinForms layout — Panel, SplitContainer, TabPage, etc.
///
/// Works in the WinForms designer — shows a labeled placeholder at design time
/// and initializes the XAML Island at runtime.
///
/// Three ways to set content (checked in this order at runtime):
///   1. <see cref="XamlContent"/> — set a UIElement directly (caller must ensure
///      the WinUI object is not created at design time).
///   2. <see cref="ContentFactory"/> — set a factory that creates the UIElement.
///      The factory is only invoked at runtime, making it safe to call from a
///      form constructor that the designer also executes.
///   3. <see cref="ComponentType"/> — set a Reactor Component type. The control
///      creates the ReactorHostControl and mounts the component automatically at
///      runtime. Fully designer-compatible — the type appears in the Properties
///      grid with a dropdown of available Component subclasses.
///
/// Prerequisites (call before creating this control at runtime):
///   - DispatcherQueue must exist on the UI thread
///   - A WinUI Application must be instantiated (for theme resources / metadata)
///   - ContentPreTranslateMessage must be hooked (for keyboard input)
///   Use <see cref="XamlIslandBootstrap.Run"/> to handle all three.
/// </summary>
public class XamlIslandControl : SWF.Control
{
    private DesktopWindowXamlSource? _source;
    /// <summary>
    /// Last <see cref="ReactorHostControl"/> hosted by this island. Tracked so
    /// it can be disposed on dispose / handle-recreate / ComponentType change
    /// — the source's Dispose doesn't dispose its content. TASK-079.
    /// </summary>
    private ReactorHostControl? _hostedReactorControl;
    private UIElement? _pendingContent;
    private Func<UIElement>? _contentFactory;
    private Type? _componentType;

    /// <summary>
    /// The WinUI content to display. Can be set before or after the control handle is created.
    /// Not designer-safe — the UIElement must not be instantiated at design time.
    /// Prefer <see cref="ComponentType"/> or <see cref="ContentFactory"/> when the
    /// control is used in designer-hosted forms.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public UIElement? XamlContent
    {
        get => _source?.Content ?? _pendingContent;
        set
        {
            if (_source is not null)
                _source.Content = value;
            else
                _pendingContent = value;
        }
    }

    /// <summary>
    /// A factory that creates the WinUI content. Invoked once at runtime when the
    /// XAML Island is initialized — never at design time. Designer-safe but not
    /// visible in the Properties grid (lambdas aren't serializable).
    ///
    /// Example:
    ///   island.ContentFactory = () => new ReactorHostControl(new MyComponent());
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Func<UIElement>? ContentFactory
    {
        get => _contentFactory;
        set
        {
            _contentFactory = value;
            // If WinUI is already initialized and no content has been set, invoke now
            if (_source is not null && _source.Content is null && value is not null)
                _source.Content = value();
        }
    }

    /// <summary>
    /// A Reactor Component type to host. The control creates a ReactorHostControl and
    /// mounts an instance of this type automatically at runtime.
    ///
    /// The type must be a concrete subclass of <see cref="ReactorComponent"/> with
    /// a parameterless constructor.
    ///
    /// Fully designer-compatible: appears in the Properties grid with a dropdown
    /// of available Component types. The designer serializes it as a typeof(...)
    /// expression — no WinUI objects are created at design time.
    /// </summary>
    [Browsable(true)]
    [Category("Reactor")]
    [Description("The Reactor Component type to host. Creates a ReactorHostControl automatically at runtime.")]
    [TypeConverter(typeof(ReactorComponentTypeConverter))]
    [DefaultValue(null)]
    public Type? ComponentType
    {
        get => _componentType;
        set
        {
            _componentType = value;
            if (DesignMode) { Invalidate(); return; }
            // If WinUI is already initialized and no content has been set, mount now
            if (_source is not null && _source.Content is null && value is not null)
                MountComponentType(value);
        }
    }

    /// <summary>
    /// The underlying DesktopWindowXamlSource. Available after the control handle is created.
    /// Null at design time.
    /// </summary>
    [Browsable(false)]
    public DesktopWindowXamlSource? Source => _source;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (DesignMode) return;

        // SECURITY (TASK-078): handle-recreation cycles must not leak the
        // prior source. Belt-and-braces dispose any survivor before
        // allocating a fresh one.
        DisposeHostedAndSource();
        _source = new DesktopWindowXamlSource();

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(Handle);
        _source.Initialize(windowId);

        // Constrain WinUI popups/flyouts/tooltips to the work area so they don't
        // escape the WinForms window bounds.
        _source.ShouldConstrainPopupsToWorkArea = true;

        // Initial size — may be default size before Dock layout runs
        UpdateBridgeSize();

        // Content priority: direct content > factory > ComponentType
        if (_pendingContent is not null)
        {
            _source.Content = _pendingContent;
            _pendingContent = null;
        }
        else if (_contentFactory is not null)
        {
            _source.Content = _contentFactory();
        }
        else if (_componentType is not null)
        {
            MountComponentType(_componentType);
        }

        // When XAML releases focus (Tab out), move to the next WinForms control.
        // Only handle First (forward Tab) and Last (Shift+Tab) — ignore Programmatic,
        // Restore, and directional reasons which shouldn't leave the island.
        _source.TakeFocusRequested += (_, args) =>
        {
            var reason = args.Request.Reason;
            if (reason is XamlSourceFocusNavigationReason.First
                       or XamlSourceFocusNavigationReason.Last)
            {
                bool forward = reason == XamlSourceFocusNavigationReason.First;
                Parent?.SelectNextControl(this, forward,
                    tabStopOnly: true, nested: true, wrap: true);
            }
        };
    }

    private void MountComponentType(Type type)
    {
        if (_source is null) return;
        // SECURITY (TASK-079): drop any prior hosted control before mounting
        // a new one — without this, ComponentType changes leak reconciler /
        // ETW / overlay state for the old component.
        DisposeHostedReactorControl();
        var component = (ReactorComponent)Activator.CreateInstance(type)!;
        var host = new ReactorHostControl(component);
        _hostedReactorControl = host;
        _source.Content = host;
    }

    private void DisposeHostedReactorControl()
    {
        if (_hostedReactorControl is IDisposable disp)
        {
            try { disp.Dispose(); } catch { }
        }
        _hostedReactorControl = null;
    }

    private void DisposeHostedAndSource()
    {
        DisposeHostedReactorControl();
        if (_source is not null)
        {
            try { _source.Dispose(); } catch { }
            _source = null;
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        // SECURITY (TASK-078 / TASK-079): COM source + hosted control must
        // be disposed when the WinForms HWND goes away, otherwise handle-
        // recreation cycles leak both.
        if (!DesignMode) DisposeHostedAndSource();
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeHostedAndSource();
        base.Dispose(disposing);
    }

    protected override void OnPaint(SWF.PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!DesignMode) return;

        // Draw a placeholder so the control is visible in the WinForms designer.
        var g = e.Graphics;
        var rc = ClientRectangle;

        // Background
        using var bgBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
        g.FillRectangle(bgBrush, rc);

        // Dashed border
        using var borderPen = new Pen(Color.FromArgb(0, 120, 212), 2f)
            { DashStyle = DashStyle.Dash };
        g.DrawRectangle(borderPen,
            rc.X + 1, rc.Y + 1, rc.Width - 3, rc.Height - 3);

        // Labels
        using var font = new Font("Segoe UI", 10f);
        using var textBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
        using var typeBrush = new SolidBrush(Color.FromArgb(0, 120, 212));

        var title = "XAML Island";
        var titleSize = g.MeasureString(title, font);
        float cy = (rc.Height - titleSize.Height) / 2;

        if (_componentType is not null)
        {
            // Two lines: "XAML Island" + component type name
            var typeName = _componentType.Name;
            var typeSize = g.MeasureString(typeName, font);
            float totalH = titleSize.Height + typeSize.Height + 2;
            cy = (rc.Height - totalH) / 2;

            g.DrawString(title, font, textBrush,
                (rc.Width - titleSize.Width) / 2, cy);
            g.DrawString(typeName, font, typeBrush,
                (rc.Width - typeSize.Width) / 2, cy + titleSize.Height + 2);
        }
        else
        {
            if (_contentFactory is not null || _pendingContent is not null)
                title += " (content set)";

            titleSize = g.MeasureString(title, font);
            cy = (rc.Height - titleSize.Height) / 2;
            g.DrawString(title, font, textBrush,
                (rc.Width - titleSize.Width) / 2, cy);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (DesignMode) { Invalidate(); return; }
        UpdateBridgeSize();
    }

    protected override void OnLayout(SWF.LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (DesignMode) return;
        // OnLayout fires when Dock/Anchor layout adjusts this control's bounds —
        // more reliable than OnResize alone for catching the initial Dock=Fill sizing.
        UpdateBridgeSize();
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private void UpdateBridgeSize()
    {
        if (_source is null || !IsHandleCreated) return;

        // Get the physical pixel size of this control's HWND
        if (!GetClientRect(Handle, out var rc)) return;
        int w = rc.Right - rc.Left;
        int h = rc.Bottom - rc.Top;
        if (w <= 0 || h <= 0) return;

        // Resize via the SiteBridge API
        _source.SiteBridge.MoveAndResize(new RectInt32(0, 0, w, h));

        // Also directly resize the bridge's child HWND as a fallback —
        // some WinAppSDK experimental versions don't fully apply MoveAndResize.
        try
        {
            var bridgeHwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(
                _source.SiteBridge.WindowId);
            if (bridgeHwnd != IntPtr.Zero)
                SetWindowPos(bridgeHwnd, IntPtr.Zero, 0, 0, w, h,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        catch (COMException)
        {
            // WindowId may not be available in all WinAppSDK configurations.
            // The primary MoveAndResize call above is the supported path; this
            // fallback only matters for experimental SDK versions.
            global::System.Diagnostics.Debug.WriteLine(
                "[XamlIsland] SiteBridge child HWND resize fallback failed (non-critical)");
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        // Check Shift state at the moment focus arrives: if Shift is held, the user
        // Shift+Tabbed backward into the island → focus the last element. Otherwise
        // focus the first element (forward Tab or programmatic focus).
        bool shiftHeld = (SWF.Control.ModifierKeys & SWF.Keys.Shift) != 0;
        var reason = shiftHeld
            ? XamlSourceFocusNavigationReason.Last
            : XamlSourceFocusNavigationReason.First;
        _source?.NavigateFocus(new XamlSourceFocusNavigationRequest(reason));
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateBridgeSize();
    }

}
