using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Reports whether WinUI has applied a Windows contrast theme to a host's content (issue #1506),
/// so Reactor reads the theme dictionaries for a contrast switch only after WinUI has made it.
/// </summary>
/// <remarks>
/// <para>WinUI creates a theme dictionary entry the first time it is read, and resolves the
/// references inside it for the theme WinUI uses at that moment. An entry read for another
/// theme gets that theme's values, and WinUI then keeps it for every element in the app: the
/// <c>Light</c> entry of <c>CardBackgroundFillColorDefaultBrush</c>, read while WinUI is still in
/// a contrast theme, is red (<c>#FF0000</c>) for the rest of the session. Windows announces a
/// contrast switch (<c>WM_SETTINGCHANGE</c>, <c>UISettings.ColorValuesChanged</c>,
/// <c>SPI_GETHIGHCONTRAST</c>) before WinUI has applied it, so Reactor cannot use those.</para>
/// <para>The probe is a small resource dictionary merged into the host's root element: one color
/// per theme dictionary (<c>Default</c>, <c>Light</c>, <c>HighContrast</c>), and a brush whose color
/// is a <c>{ThemeResource}</c> reference to it. WinUI updates that reference itself when it
/// applies a theme, which raises the brush's change callback. On a later dispatcher turn, after
/// WinUI has finished, the probe reports what WinUI applied to
/// <see cref="HighContrastState.OnApplied"/>, which refreshes the hosts when that changed. Only
/// while its root is in the live tree: WinUI does not update a probe off the tree, so it neither
/// reports nor counts there, and with no probe in the tree the system setting stands in.</para>
/// <para>UI thread only: create, read and dispose it on the thread of its root element.</para>
/// </remarks>
internal sealed class ThemeProbe : IDisposable
{
    internal const string ColorKey = "ReactorThemeProbeColor";
    internal const string BrushKey = "ReactorThemeProbeBrush";

    // Transparent, and never drawn: only the blue channel tells the dictionaries apart.
    internal static readonly global::Windows.UI.Color DefaultColor = global::Windows.UI.Color.FromArgb(0, 0, 0, 1);
    internal static readonly global::Windows.UI.Color LightColor = global::Windows.UI.Color.FromArgb(0, 0, 0, 2);
    internal static readonly global::Windows.UI.Color ContrastColor = global::Windows.UI.Color.FromArgb(0, 0, 0, 3);

    private const string Xaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <ResourceDictionary.ThemeDictionaries>
            <ResourceDictionary x:Key="Default"><Color x:Key="ReactorThemeProbeColor">#00000001</Color></ResourceDictionary>
            <ResourceDictionary x:Key="Light"><Color x:Key="ReactorThemeProbeColor">#00000002</Color></ResourceDictionary>
            <ResourceDictionary x:Key="HighContrast"><Color x:Key="ReactorThemeProbeColor">#00000003</Color></ResourceDictionary>
          </ResourceDictionary.ThemeDictionaries>
          <SolidColorBrush x:Key="ReactorThemeProbeBrush" Color="{ThemeResource ReactorThemeProbeColor}" />
        </ResourceDictionary>
        """;

    private readonly FrameworkElement _owner;
    private readonly ResourceDictionary _dictionary;
    private readonly SolidColorBrush _brush;
    private readonly DispatcherQueue? _dispatcher;
    private readonly long _token;
    private bool _live;
    private bool _reportPending;
    private bool _disposed;

    private ThemeProbe(FrameworkElement owner, ResourceDictionary dictionary, SolidColorBrush brush, DispatcherQueue? dispatcher, bool takeOver)
    {
        _owner = owner;
        _dictionary = dictionary;
        _brush = brush;
        _dispatcher = dispatcher;
        _token = brush.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, OnColorChanged);
        // Only a probe in the live tree hears WinUI apply a theme, so only such a probe reports
        // and counts: one left on a root that is off the tree would keep a stale state. Loaded
        // and IsLoaded do not tell: a raw host's root, put in the tree during a render, gets
        // neither. So check the tree itself, on a later turn, after WinUI has taken the change.
        owner.Loaded += OnOwnerLoaded;
        owner.Unloaded += OnOwnerUnloaded;
        // A probe that replaces a live one, on a new root in the same place, counts at once, so
        // the count never passes through zero between the two and the system setting does not
        // stand in for that moment. The check below withdraws it if its root is off the tree.
        if (takeOver) GoLive();
        ScheduleLivenessCheck();
    }

    /// <summary>
    /// Merges a probe into <paramref name="owner"/>'s resources. Returns null, and leaves the
    /// element unchanged, when WinUI rejects it; the host then falls back to the system setting.
    /// Pass the probe this one replaces, before disposing it, so a live one hands over.
    /// </summary>
    internal static ThemeProbe? TryAttach(FrameworkElement owner, ThemeProbe? replacing = null)
    {
        ResourceDictionary? dictionary = null;
        ResourceDictionary? resources = null;
        try
        {
            dictionary = (ResourceDictionary)XamlReader.Load(Xaml);
            resources = owner.Resources ??= new ResourceDictionary();
            resources.MergedDictionaries.Add(dictionary);
            var brush = (SolidColorBrush)dictionary[BrushKey];
            return new ThemeProbe(owner, dictionary, brush, DispatcherQueue.GetForCurrentThread(), takeOver: replacing?.IsLive == true);
        }
        catch (Exception ex) when (ex is global::System.Runtime.InteropServices.COMException or InvalidCastException or ArgumentException)
        {
            if (dictionary is not null)
            {
                try { resources?.MergedDictionaries.Remove(dictionary); }
                catch (global::System.Runtime.InteropServices.COMException) { }
            }
            Core.Diagnostics.DiagnosticLog.SwallowedError(Core.Diagnostics.LogCategory.Hosting, "ThemeProbe.TryAttach", ex);
            return null;
        }
    }

    /// <summary>The element whose resources hold the probe.</summary>
    internal FrameworkElement Owner => _owner;

    /// <summary>Whether the probe is in the live tree, where WinUI updates it.</summary>
    internal bool IsLive => _live;

    /// <summary>Whether WinUI resolves the probe from its <c>HighContrast</c> theme dictionary.</summary>
    internal bool IsHighContrast => HighContrastState.ProbeReaderForTest is { } reader ? reader() : _brush.Color == ContrastColor;

    /// <summary>Test seam: the color WinUI resolved, one of the three probe colors.</summary>
    internal global::Windows.UI.Color ColorForTest => _brush.Color;

    /// <summary>Test seam: how many times WinUI changed the probe.</summary>
    internal int ChangeCountForTest { get; private set; }

    /// <summary>Test seam: what WinUI changing the probe does.</summary>
    internal void SimulateChangeForTest() => OnColorChanged(_brush, SolidColorBrush.ColorProperty);

    private void OnColorChanged(DependencyObject sender, DependencyProperty property)
    {
        ChangeCountForTest++;
        ScheduleReport();
    }

    private void OnOwnerLoaded(object sender, RoutedEventArgs e) => GoLive();

    // WinUI can raise a moved element's Unloaded after its new Loaded: decide on a later turn.
    private void OnOwnerUnloaded(object sender, RoutedEventArgs e) => ScheduleLivenessCheck();

    private void ScheduleLivenessCheck()
    {
        if (_dispatcher is null || !_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, CheckLiveness))
            CheckLiveness();
    }

    private void CheckLiveness()
    {
        if (_disposed) return;
        if (IsInLiveTree(_owner)) GoLive();
        else Withdraw();
    }

    // In the window's tree: its parents lead to the content of its XamlRoot. (A root in a popup
    // does not count, and the system setting stands in for it.)
    private static bool IsInLiveTree(FrameworkElement element)
    {
        var top = element.XamlRoot?.Content;
        if (top is null) return false;
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, top)) return true;
        }
        return false;
    }

    private void GoLive()
    {
        if (_disposed) return;
        if (!_live)
        {
            _live = true;
            HighContrastState.ProbeAttached();
        }
        // Say what WinUI applies now: it may have switched while the probe was off the tree.
        ScheduleReport();
    }

    private void Withdraw()
    {
        if (!_live) return;
        _live = false;
        HighContrastState.ProbeDetached();
    }

    private void ScheduleReport()
    {
        if (_disposed || !_live || _reportPending) return;
        _reportPending = true;
        // WinUI raises the callback while it applies the theme: report on a later turn, once
        // it has finished. Low priority, so its own queued work for the switch runs first.
        if (_dispatcher is null || !_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, Report))
            _reportPending = false;
    }

    private void Report()
    {
        _reportPending = false;
        if (_disposed || !_live) return;
        HighContrastState.OnApplied(IsHighContrast);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.Loaded -= OnOwnerLoaded;
        _owner.Unloaded -= OnOwnerUnloaded;
        _brush.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, _token);
        try { _owner.Resources?.MergedDictionaries.Remove(_dictionary); }
        catch (global::System.Runtime.InteropServices.COMException) { }
        if (_live)
        {
            _live = false;
            HighContrastState.ProbeDetached();
        }
    }
}
