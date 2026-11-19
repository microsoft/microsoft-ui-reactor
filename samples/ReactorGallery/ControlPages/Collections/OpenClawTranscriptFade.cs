// Adapted from OpenClaw's ChatTranscriptFade; see OpenClaw-LICENSE.txt.
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace WinUIGalleryReactor.ControlPages.Collections;

internal sealed class OpenClawTranscriptFade : IDisposable
{
    private sealed record FadeBorderElement(Element Child) : Element
    {
        static FadeBorderElement() => Microsoft.UI.Reactor.Core.V1Protocol.ControlRegistry.Register<FadeBorderElement, Border>(static () =>
            new DescriptorHandler<FadeBorderElement, Border>(new ControlDescriptor<FadeBorderElement, Border>
            {
                PoolPolicy = new() { IsPoolable = false },
                Children = new SingleContent<FadeBorderElement, Border>(static e => e.Child, static (c, child) => c.Child = child)
                {
                    GetCurrentChild = static c => c.Child,
                },
            }));
    }

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "OpenClawTranscriptFade", typeof(object), typeof(OpenClawTranscriptFade), new PropertyMetadata(null));
    private readonly Border _host;
    private readonly AccessibilitySettings _accessibility = new();
    private readonly UISettings _settings = new();
    private Band? _band;
    private bool _disposed;

    internal static Element Wrap(Element child) => new FadeBorderElement(new FadeBorderElement(child))
        .OnMount(control => Attach((Border)control))
        .OnUnmount(control => Detach((Border)control));

    private OpenClawTranscriptFade(Border host)
    {
        _host = host;
        host.Loaded += OnLoaded;
        host.Unloaded += OnUnloaded;
        host.SizeChanged += OnSizeChanged;
        _settings.ColorValuesChanged += OnColorsChanged;
    }

    private static void Attach(Border host)
    {
        var state = new OpenClawTranscriptFade(host);
        host.SetValue(StateProperty, state);
        state.Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => Refresh();
    private void OnUnloaded(object sender, RoutedEventArgs args) => Release();
    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => Refresh();
    private void OnColorsChanged(UISettings sender, object args)
    {
        if (!_disposed && !_host.DispatcherQueue.TryEnqueue(() => { if (!_disposed) Refresh(); }))
            System.Diagnostics.Trace.TraceWarning("OpenClaw transcript contrast update could not be dispatched.");
    }

    private void Refresh()
    {
        if (!_host.IsLoaded || _accessibility.HighContrast)
        {
            Release();
            return;
        }
        if (_host.Child is not Border { Child: { } content } clipper) return;
        if (_band is null || !ReferenceEquals(_band.Content, content))
        {
            Release();
            _band = new Band(_host, clipper, content);
        }
        _band.Update();
    }

    private void Release()
    {
        if (_band is null) return;
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        _band.Dispose();
        _band = null;
    }

    private static void Detach(Border host)
    {
        (host.GetValue(StateProperty) as OpenClawTranscriptFade)?.Dispose();
        host.ClearValue(StateProperty);
    }

    public void Dispose()
    {
        _disposed = true;
        _host.Loaded -= OnLoaded;
        _host.Unloaded -= OnUnloaded;
        _host.SizeChanged -= OnSizeChanged;
        _settings.ColorValuesChanged -= OnColorsChanged;
        Release();
    }

    private sealed class Band : IDisposable
    {
        private readonly Border _clipper;
        private readonly Visual _clipperVisual;
        private readonly CompositionClip? _originalClip;
        private readonly InsetClip _clip;
        private readonly CompositionVisualSurface _surface;
        private readonly CompositionSurfaceBrush _source;
        private readonly CompositionLinearGradientBrush _gradient;
        private readonly CompositionMaskBrush _mask;
        private readonly SpriteVisual _sprite;
        internal UIElement Content { get; }

        internal Band(Border host, Border clipper, UIElement content)
        {
            _clipper = clipper;
            Content = content;
            _clipperVisual = ElementCompositionPreview.GetElementVisual(clipper);
            _originalClip = _clipperVisual.Clip;
            var visual = ElementCompositionPreview.GetElementVisual(content);
            var compositor = visual.Compositor;
            _clip = compositor.CreateInsetClip();
            _surface = compositor.CreateVisualSurface();
            _surface.SourceVisual = visual;
            _source = compositor.CreateSurfaceBrush(_surface);
            _source.Stretch = CompositionStretch.None;
            _source.HorizontalAlignmentRatio = 0;
            _source.VerticalAlignmentRatio = 0;
            _gradient = compositor.CreateLinearGradientBrush();
            _gradient.StartPoint = Vector2.Zero;
            _gradient.EndPoint = Vector2.UnitY;
            _gradient.ColorStops.Add(compositor.CreateColorGradientStop(0, Colors.White));
            _gradient.ColorStops.Add(compositor.CreateColorGradientStop(1, Colors.Transparent));
            _mask = compositor.CreateMaskBrush();
            _mask.Source = _source;
            _mask.Mask = _gradient;
            _sprite = compositor.CreateSpriteVisual();
            _sprite.Brush = _mask;
            Update();
            _clipperVisual.Clip = _clip;
            ElementCompositionPreview.SetElementChildVisual(host, _sprite);
        }

        internal void Update()
        {
            var width = (float)_clipper.ActualWidth;
            var height = (float)_clipper.ActualHeight;
            var bandHeight = Math.Min(32, height);
            _clip.BottomInset = bandHeight;
            _surface.SourceOffset = new(0, height - bandHeight);
            _surface.SourceSize = new(width, bandHeight);
            _sprite.Size = new(width, bandHeight);
            _sprite.Offset = new(0, height - bandHeight, 0);
        }

        public void Dispose()
        {
            _clipperVisual.Clip = _originalClip;
            _sprite.Brush = null;
            _surface.SourceVisual = null;
            _sprite.Dispose();
            _mask.Dispose();
            _source.Dispose();
            foreach (var stop in _gradient.ColorStops) stop.Dispose();
            _gradient.Dispose();
            _surface.Dispose();
            _clip.Dispose();
        }
    }
}
