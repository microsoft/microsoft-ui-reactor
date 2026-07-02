using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// E2E fixture for issue #679 (b) — host theme-change re-resolution of a ThemeRef brush,
/// proving the shipped #86/#751 wiring (the host's <c>ActualThemeChanged</c> handler →
/// <c>ThemeRef.InvalidateResolutionCache()</c> + <c>RequestRender()</c>) end-to-end.
///
/// <para><b>Why a CONCRETE ResourceOverride ThemeRef, not <c>.Foreground(Theme.X)</c>:</b>
/// a <c>.Foreground(Theme.X)</c> binding compiles to a <c>{ThemeResource}</c> style setter that
/// WinUI re-resolves NATIVELY on an effective-theme change — it would flip even if the Reactor
/// wiring were broken (vacuous). A <c>.Resources(r =&gt; r.Set(key, Theme.Ref(key)))</c> override
/// instead stores a CONCRETE <see cref="SolidColorBrush"/> in <c>fe.Resources[key]</c> that does
/// NOT self-heal; it only updates when Reactor re-runs <c>ApplyResourceOverrides</c> on a
/// re-render, which re-resolves the ThemeRef against the probe's live <c>ActualTheme</c>.</para>
///
/// <para><b>Why the toggle does NOT call setState:</b> the button changes only the window-root
/// <c>RequestedTheme</c> (the canonical app theme switch) and forces no fixture re-render. So the
/// ONLY thing that can re-render Reactor — and therefore re-resolve the concrete brush — is the
/// host's <c>ActualThemeChanged → RequestRender</c> wiring. Break that wiring and the surfaced
/// color never changes. (Selftests drive the re-render via fixture setState, so they never
/// exercise this host wiring; that is the unique E2E value.) A <c>renders:</c> readout makes the
/// host-driven re-render observable.</para>
///
/// <para>A distinct Light/Dark brush pair is installed under a unique key so the concrete override
/// resolves (system brushes like <c>TextFillColorPrimaryBrush</c> live in nested
/// XamlControlsResources theme dictionaries that <c>ThemeRef.Resolve</c> does not reach — mirrors
/// <c>ResourceOverrideMountThemeFixtures</c>). The dict, cache and root theme are torn down on
/// unmount so the shared Host session is never left mutated.</para>
/// </summary>
internal static class ThemeChangeE2EFixtures
{
    private const string ProbeKey = "ReactorE2E_ThemeProbeBrush";

    internal class ThemeReResolveComponent : Component
    {
        private FrameworkElement? _probe;   // carries the concrete ResourceOverride
        private FrameworkElement? _anchor;  // reaches XamlRoot.Content (the window root)
        private ElementTheme? _originalRootTheme;

        // Participate in parent/host re-renders. A propless Component defaults ShouldUpdate() to
        // false, so the host's theme-change RequestRender (a NON-self-triggered parent re-render)
        // would otherwise skip this component entirely — its ThemeRef overrides never re-resolve
        // and the reader effect never re-runs. Returning true is exactly how any component with
        // host-reactive content behaves; it is what lets the shipped ActualThemeChanged →
        // RequestRender wiring reach this subtree end-to-end.
        protected internal override bool ShouldUpdate() => true;

        public override Element Render()
        {
            // Install the themed dictionary BEFORE the probe mounts (guarded to run once) so the
            // probe's ThemeRef override resolves to a concrete brush at first mount.
            var dictRef = UseRef<ResourceDictionary?>(null);
            if (dictRef.Current is null)
                dictRef.Current = InstallThemedDict();

            var (colorText, setColorText) = UseState("Probe: (pending)");
            var (settle, setSettle) = UseState(0);

            // Per-render counter (Ref, so bumping it doesn't itself request a render). Used both as
            // an observable "the host re-rendered" signal AND (with the settle state) as the reader
            // effect's dependency so the reader re-runs after every render — including the host-
            // driven one that has no changed state/props of its own.
            var rc = UseRef(0);
            rc.Current++;
            var renderNo = rc.Current;

            // Reader runs AFTER reconciliation, when the probe's ActualTheme has settled to the
            // host theme. It resolves the SAME ThemeRef the .Resources() override below uses, via
            // the public ThemeRef.Resolve(key, fe) — the exact resolution the shipped host wiring
            // makes re-run. Reading it here (rather than the reconciler-applied fe.Resources[key],
            // which lags one layout pass during the theme-change reconcile) gives a stable, timely
            // observation. Keyed on renderNo so it re-runs each render; on the host-driven
            // re-render it re-resolves against the now-current theme. Break the host RequestRender
            // wiring → the component never re-renders → this never re-runs → the color stays stale.
            UseEffect(() =>
            {
                var brush = _probe is not null ? ThemeRef.Resolve(ProbeKey, _probe) : null;
                if (brush is SolidColorBrush b)
                {
                    var c = b.Color;
                    var text = $"Probe: #{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                    if (text != colorText) setColorText(text);
                }
                else if (settle < 12)
                {
                    setSettle(settle + 1);
                }
            }, renderNo, settle);

            // Run-once: tear down on unmount (navigate away / Reset) — remove the installed dict,
            // drop the resolution cache, and restore the original root theme.
            UseEffect(() => (Action)(() =>
            {
                if (dictRef.Current is { } d)
                    Application.Current.Resources.MergedDictionaries.Remove(d);
                ThemeRef.InvalidateResolutionCache();
                if (_anchor?.XamlRoot?.Content is FrameworkElement root && _originalRootTheme is { } t)
                    root.RequestedTheme = t;
            }));

            return VStack(8,
                TextBlock("Host theme-change probe (concrete ResourceOverride ThemeRef)"),

                // The realistic user path — a concrete ThemeRef ResourceOverride. NO .RequestedTheme
                // so the probe's ActualTheme tracks the host theme; the host theme change flips it.
                Border(TextBlock("theme probe"))
                    .Resources(r => r.Set(ProbeKey, Theme.Ref(ProbeKey)))
                    .Set(fe => _probe = fe)
                    .Padding(12)
                    .AutomationId("ThemeProbe"),

                TextBlock(colorText).AutomationId("ThemeProbeColor"),
                TextBlock($"renders: {renderNo}").AutomationId("RenderCount"),

                Button("Toggle Root Theme", () =>
                {
                    if (_anchor?.XamlRoot?.Content is FrameworkElement root)
                    {
                        _originalRootTheme ??= root.RequestedTheme;
                        // Flip off the EFFECTIVE (Actual) theme; the root starts at
                        // RequestedTheme=Default (effective Dark via the app theme), so keying off
                        // RequestedTheme would set Dark→Dark (a no-op first click).
                        root.RequestedTheme = root.ActualTheme == ElementTheme.Dark
                            ? ElementTheme.Light
                            : ElementTheme.Dark;
                        // No setState — rely on host ActualThemeChanged → RequestRender.
                    }
                })
                .Set(fe => _anchor = fe)
                .AutomationId("ThemeToggleBtn")
            );
        }

        // A Source-less merged dictionary whose ThemeDictionaries carry a different concrete brush
        // per theme under a unique key; ThemeRef.Resolve discovers it via its ThemeDictionaries
        // scan over MergedDictionaries. Cache invalidated on install so a prior run can't leak.
        private static ResourceDictionary InstallThemedDict()
        {
            var light = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 0, 0, 140));   // #FF00008C
            var dark = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 200, 40, 0));   // #FFC82800
            var dict = new ResourceDictionary();
            dict.ThemeDictionaries["Light"] = new ResourceDictionary { [ProbeKey] = light };
            dict.ThemeDictionaries["Dark"] = new ResourceDictionary { [ProbeKey] = dark };
            Application.Current.Resources.MergedDictionaries.Add(dict);
            ThemeRef.InvalidateResolutionCache();
            return dict;
        }
    }

    internal static Element ThemeReResolve(RenderContext ctx) => Component<ThemeReResolveComponent>();
}
