using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Tells a host when WinUI has switched its content into or out of a Windows contrast theme
/// (issue #1506), so the host can resolve its theme references again.
/// </summary>
/// <remarks>
/// <para><see cref="Core.ThemeRef"/> values that Reactor resolves itself (<c>.Resources(...)</c>
/// overrides, <c>ThemeRef.Resolve</c> in a render, validation styling) are brushes picked once,
/// so they follow a theme switch only when the host renders again. A Light/Dark switch raises
/// <c>ActualThemeChanged</c>, but switching a contrast theme on does not, and Windows announces
/// it (<c>UISettings.ColorValuesChanged</c>) before WinUI has applied it: a resolve in that gap
/// still gets the old theme's brush.</para>
/// <para>The probe is a small resource dictionary merged into an element of the host's tree: one
/// color per theme dictionary (<c>Default</c>, <c>Light</c>, <c>HighContrast</c>), and a brush
/// whose color is a <c>{ThemeResource}</c> reference to it. WinUI updates that reference itself
/// when it applies a theme, which raises the brush's change callback. On a later dispatcher turn,
/// once WinUI has finished, the probe compares the dictionary WinUI picked with the last one and
/// calls the host back when the contrast state changed. A Light/Dark switch alone does not call
/// back; the host's <c>ActualThemeChanged</c> listener covers it. The probe subscribes to no
/// event of its element: subscribing to an element's <c>Loaded</c> while it enters the tree
/// during a render can keep WinUI from raising <c>Loaded</c> for it (#1509).</para>
/// <para>UI thread only: create, read and dispose it on the thread of its element.</para>
/// </remarks>
internal sealed class HighContrastProbe : IDisposable
{
    internal const string ColorKey = "ReactorHighContrastProbeColor";
    internal const string BrushKey = "ReactorHighContrastProbeBrush";

    // Transparent, and never drawn: only the blue channel tells the dictionaries apart.
    internal static readonly global::Windows.UI.Color DefaultColor = global::Windows.UI.Color.FromArgb(0, 0, 0, 1);
    internal static readonly global::Windows.UI.Color LightColor = global::Windows.UI.Color.FromArgb(0, 0, 0, 2);
    internal static readonly global::Windows.UI.Color ContrastColor = global::Windows.UI.Color.FromArgb(0, 0, 0, 3);

    private const string Xaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <ResourceDictionary.ThemeDictionaries>
            <ResourceDictionary x:Key="Default"><Color x:Key="ReactorHighContrastProbeColor">#00000001</Color></ResourceDictionary>
            <ResourceDictionary x:Key="Light"><Color x:Key="ReactorHighContrastProbeColor">#00000002</Color></ResourceDictionary>
            <ResourceDictionary x:Key="HighContrast"><Color x:Key="ReactorHighContrastProbeColor">#00000003</Color></ResourceDictionary>
          </ResourceDictionary.ThemeDictionaries>
          <SolidColorBrush x:Key="ReactorHighContrastProbeBrush" Color="{ThemeResource ReactorHighContrastProbeColor}" />
        </ResourceDictionary>
        """;

    private readonly FrameworkElement _owner;
    private readonly ResourceDictionary _dictionary;
    private readonly SolidColorBrush _brush;
    private readonly DispatcherQueue? _dispatcher;
    private readonly Action _onContrastChanged;
    private readonly long _token;
    private bool _lastHighContrast;
    private bool _checkPending;
    private bool _disposed;

    private HighContrastProbe(FrameworkElement owner, ResourceDictionary dictionary, SolidColorBrush brush, Action onContrastChanged)
    {
        _owner = owner;
        _dictionary = dictionary;
        _brush = brush;
        _onContrastChanged = onContrastChanged;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _lastHighContrast = IsHighContrast;
        _token = brush.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, OnColorChanged);
    }

    /// <summary>
    /// Merges a probe into <paramref name="owner"/>'s resources, calling
    /// <paramref name="onContrastChanged"/> on the UI thread each time WinUI switches it into or
    /// out of a contrast theme. Returns null, and leaves the element unchanged, when WinUI rejects
    /// it; the host then does without.
    /// </summary>
    internal static HighContrastProbe? TryAttach(FrameworkElement owner, Action onContrastChanged)
    {
        ResourceDictionary? dictionary = null;
        ResourceDictionary? resources = null;
        try
        {
            dictionary = (ResourceDictionary)XamlReader.Load(Xaml);
            resources = owner.Resources ??= new ResourceDictionary();
            resources.MergedDictionaries.Add(dictionary);
            var brush = (SolidColorBrush)dictionary[BrushKey];
            return new HighContrastProbe(owner, dictionary, brush, onContrastChanged);
        }
        catch (Exception ex) when (ex is global::System.Runtime.InteropServices.COMException or InvalidCastException or ArgumentException or XamlParseException)
        {
            if (dictionary is not null)
            {
                try { resources?.MergedDictionaries.Remove(dictionary); }
                catch (global::System.Runtime.InteropServices.COMException) { }
            }
            Core.Diagnostics.DiagnosticLog.SwallowedError(Core.Diagnostics.LogCategory.Hosting, "HighContrastProbe.TryAttach", ex);
            return null;
        }
    }

    /// <summary>The element whose resources hold the probe.</summary>
    internal FrameworkElement Owner => _owner;

    /// <summary>Whether WinUI resolves the probe from its <c>HighContrast</c> theme dictionary.</summary>
    internal bool IsHighContrast => ReaderForTest is { } reader ? reader() : _brush.Color == ContrastColor;

    /// <summary>
    /// Test seam: replaces what the probe reads from WinUI, so a selftest can play WinUI applying
    /// a contrast theme without changing the desktop. <c>null</c> restores the real read.
    /// </summary>
    internal Func<bool>? ReaderForTest { get; set; }

    /// <summary>Test seam: the color WinUI resolved, one of the three probe colors.</summary>
    internal global::Windows.UI.Color ColorForTest => _brush.Color;

    /// <summary>Test seam: how many times WinUI changed the probe.</summary>
    internal int ChangeCountForTest { get; private set; }

    /// <summary>Test seam: what WinUI changing the probe does.</summary>
    internal void SimulateChangeForTest() => OnColorChanged(_brush, SolidColorBrush.ColorProperty);

    private void OnColorChanged(DependencyObject sender, DependencyProperty property)
    {
        ChangeCountForTest++;
        if (_disposed || _checkPending) return;
        _checkPending = true;
        // WinUI raises the callback while it applies the theme: check on a later turn, once it
        // has finished, so the host resolves against the new theme. Low priority, so WinUI's own
        // queued work for the switch runs first.
        if (_dispatcher is null || !_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, Check))
            _checkPending = false;
    }

    private void Check()
    {
        _checkPending = false;
        if (_disposed) return;
        var highContrast = IsHighContrast;
        if (highContrast == _lastHighContrast) return;
        _lastHighContrast = highContrast;
        _onContrastChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _brush.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, _token);
        try { _owner.Resources?.MergedDictionaries.Remove(_dictionary); }
        catch (global::System.Runtime.InteropServices.COMException) { }
    }
}
