using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Proves the <c>.ApplyStyle(...)</c> mount action actually resolves WinUI named
/// styles against the live application resources, and that each type-ramp factory
/// is pointed at the right key.
///
/// <para>
/// The unit layer cannot cover this: a headless test cannot construct a
/// <c>TextBlock</c> or read <c>Application.Current.Resources</c>, so it can only
/// assert that <i>an</i> OnMount action exists. Everything about whether the key
/// resolves — and to which style — is only observable against a real control.
/// </para>
///
/// <para>
/// This is also the regression guard for the resolution mechanism itself.
/// <c>ApplyStyle</c> resolves through <c>ResourceLookup.TryFind</c>, which walks
/// merged dictionaries; the type-ramp styles live in <c>XamlControlsResources</c>.
/// Because an unresolved key no longer throws, a lookup regression would be
/// invisible without an assertion on the resulting font size — a "mounted without
/// throwing" check would pass either way.
/// </para>
/// </summary>
internal static class NamedStyleResolutionFixture
{
    // WinUI 3 type ramp (Windows App SDK generic.xaml). Font size is the
    // cheapest observable that differs per style, so it doubles as the
    // "right key" check: swapping any two keys reddens the pair.
    private static readonly (string Label, double FontSize)[] Ramp =
    [
        ("ramp-Body", 14),
        ("ramp-BodyStrong", 14),
        ("ramp-BodyLarge", 18),
        ("ramp-Subtitle", 20),
        ("ramp-Title", 28),
        ("ramp-TitleLarge", 40),
        ("ramp-Display", 68),
    ];

    /// <summary>
    /// Every type-ramp factory resolves its named style and lands the expected
    /// font size on the real control. Deleting the <c>fe.Style = style</c>
    /// assignment in <c>ApplyNamedStyle</c>, or breaking merged-dictionary
    /// lookup, drops every element to the theme default (14px) and fails all
    /// rows except the two that are legitimately 14px.
    /// </summary>
    internal class TypeRampStylesResolve(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                Body("ramp-Body"),
                BodyStrong("ramp-BodyStrong"),
                BodyLarge("ramp-BodyLarge"),
                Subtitle("ramp-Subtitle"),
                Title("ramp-Title"),
                TitleLarge("ramp-TitleLarge"),
                Display("ramp-Display")));

            await Harness.Render();

            foreach (var (label, expected) in Ramp)
            {
                var tb = H.FindText(label);
                H.Check($"NamedStyle_{label}_Mounted", tb is not null);

                // A Style must actually be attached — the direct negative
                // control for a lookup that silently found nothing.
                H.Check($"NamedStyle_{label}_HasStyle", tb?.Style is not null);

                H.Check($"NamedStyle_{label}_FontSize{expected}",
                    tb is not null && Math.Abs(tb.FontSize - expected) < 0.01);
            }
        }
    }

    /// <summary>
    /// Positive control for the fixture above. The two keys this PR added are
    /// the ones with no prior coverage anywhere, and they are the ones most
    /// likely to be wrong (a typo yields a silent 14px fallback now that
    /// unresolved keys no longer throw). Assert they differ from the default
    /// AND from each other, so neither "both fell back" nor "both point at the
    /// same key" can pass.
    /// </summary>
    internal class NewRampKeysAreDistinctAndNonDefault(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                TextBlock("ramp-plain"),
                TitleLarge("ramp-large"),
                Display("ramp-display")));

            await Harness.Render();

            var plain = H.FindText("ramp-plain");
            var large = H.FindText("ramp-large");
            var display = H.FindText("ramp-display");

            H.Check("NamedStyle_NewKeys_AllMounted",
                plain is not null && large is not null && display is not null);

            // Establishes that an unstyled TextBlock really is the 14px default
            // in this host, so "differs from default" below means something.
            H.Check("NamedStyle_PlainIsThemeDefault",
                plain is not null && Math.Abs(plain.FontSize - 14) < 0.01);

            H.Check("NamedStyle_TitleLarge_DiffersFromDefault",
                large is not null && plain is not null
                    && large.FontSize > plain.FontSize + 1);
            H.Check("NamedStyle_Display_DiffersFromDefault",
                display is not null && plain is not null
                    && display.FontSize > plain.FontSize + 1);
            H.Check("NamedStyle_TitleLarge_DiffersFromDisplay",
                large is not null && display is not null
                    && Math.Abs(large.FontSize - display.FontSize) > 1);
        }
    }

    /// <summary>
    /// An unresolved key degrades instead of throwing: the element mounts, keeps
    /// the theme default, and the surrounding tree is unaffected. Before this
    /// behavior existed the missing key threw out of <c>OnMountAction</c>, which
    /// <c>Reconciler.ApplyModifiers</c> invokes unguarded, so the sibling below
    /// would never have rendered.
    /// </summary>
    internal class UnresolvedStyleKeyDoesNotBreakRender(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                TextBlock("bad-style-target").ApplyStyle("ReactorNoSuchStyleKey_SelfTest"),
                TextBlock("bad-style-sibling")));

            await Harness.Render();

            var target = H.FindText("bad-style-target");
            var sibling = H.FindText("bad-style-sibling");

            H.Check("NamedStyle_Unresolved_TargetStillMounts", target is not null);

            // The load-bearing assertion: the mount action threw before, which
            // aborted the whole subtree. A sibling rendered after the bad key
            // proves the render survived rather than merely that one element exists.
            H.Check("NamedStyle_Unresolved_SiblingStillRenders", sibling is not null);

            H.Check("NamedStyle_Unresolved_NoStyleApplied", target?.Style is null);
            H.Check("NamedStyle_Unresolved_KeepsThemeDefaultSize",
                target is not null && Math.Abs(target.FontSize - 14) < 0.01);
        }
    }
}
