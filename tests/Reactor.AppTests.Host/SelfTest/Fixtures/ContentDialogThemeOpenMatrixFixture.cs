using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;
using Color = global::Windows.UI.Color;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Compare the rendered dialog to a conventional native WinUI dialog, not to a
/// second invocation of Reactor's theme resolver. Each case has a contradictory
/// outer theme, and both Light/Dark run regardless of the machine's app theme.
/// Separate fixtures keep a default-inheritance failure from hiding explicit
/// theme failures in a pre-fix/mutation run.
/// </summary>
internal sealed class ContentDialogThemeOpenMatrixFixture(Harness h, bool explicitTheme) : SelfTestFixtureBase(h)
{
    private const string Title = "Dialog theme open matrix";
    private const string ContentText = "Native theme comparison";

    public override async Task RunAsync()
    {
        foreach (var requested in new[] { ElementTheme.Light, ElementTheme.Dark, ElementTheme.Default })
        {
            // Default on an inherited owner just repeats the application fallback;
            // test Default as an explicit request against an opposite local owner.
            if (!explicitTheme && requested == ElementTheme.Default) continue;
            var referenceTheme = requested == ElementTheme.Default
                ? Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Light : ElementTheme.Dark
                : requested;
            var reference = await ReadNativeReference(referenceTheme);
            var opposite = reference.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            foreach (var openAtMount in new[] { true, false })
            {
                var caseName = $"{(explicitTheme ? "Explicit" : "Inherited")}_{requested}_{(openAtMount ? "Deferred" : "RisingEdge")}";
                var state = new ScenarioState(openAtMount);
                Action<ScenarioState>? setState = null;
                var host = H.CreateHost();
                host.Mount(_ => Component<Scenario, ScenarioProps>(new(
                    state,
                    // The nearest owner disagrees with the outer root, so using
                    // the window root instead of the declaration site is wrong.
                    explicitTheme && requested != ElementTheme.Default ? reference.ActualTheme : opposite,
                    explicitTheme && requested != ElementTheme.Default ? opposite : reference.ActualTheme,
                    explicitTheme ? requested : null,
                    setter => setState = setter)));
                if (!openAtMount)
                {
                    await Harness.Render();
                    H.Check("DialogThemeMatrix_" + caseName + "_InitiallyClosed",
                        ContentDialogProbe.FindOpen(H, Title) is null);
                    setState!(state with { IsOpen = true });
                }

                var dialog = await ContentDialogProbe.WaitForOpen(H, Title)
                    ?? throw new InvalidOperationException($"{caseName}: dialog did not open.");
                try
                {
                    await Harness.WaitFor(() => ContentDialogProbe.FindText(dialog, ContentText)?.Foreground is SolidColorBrush);
                    // Re-find in the popup after it realizes. Reading only the
                    // element record or a control captured before opening cannot
                    // prove the real, visible dialog received the theme.
                    var current = ContentDialogProbe.FindOpen(H, Title)!;
                    var text = ContentDialogProbe.FindText(current, ContentText);
                    var expectedRequest = explicitTheme && requested != ElementTheme.Default ? requested : reference.ActualTheme;
                    H.Check("DialogThemeMatrix_" + caseName + "_RequestedTheme",
                        current.RequestedTheme == expectedRequest,
                        $"App={Application.Current.RequestedTheme}; expected={expectedRequest}; actual={current.RequestedTheme}");
                    H.Check("DialogThemeMatrix_" + caseName + "_RenderedTheme",
                        current.ActualTheme == reference.ActualTheme && text?.ActualTheme == reference.ActualTheme,
                        $"Native={reference.ActualTheme}; dialog={current.ActualTheme}; content={text?.ActualTheme}");
                    var foreground = (text?.Foreground as SolidColorBrush)?.Color;
                    H.Check("DialogThemeMatrix_" + caseName + "_NativeForeground",
                        foreground == reference.Foreground,
                        $"Native={reference.Foreground}; Reactor={foreground}");
                    H.Check("DialogThemeMatrix_" + caseName + "_ContentThemeResource",
                        text?.Resources["DialogThemeProbeBrush"] is SolidColorBrush resourceBrush
                        && resourceBrush.Color == reference.Foreground,
                        $"ThemeRef must resolve against native reference theme {reference.ActualTheme} on first open.");
                }
                finally
                {
                    dialog.Hide();
                    await ContentDialogProbe.WaitForClosed(H, Title);
                }
            }
        }
    }

    private async Task<NativeReference> ReadNativeReference(ElementTheme theme)
    {
        // Plain WinUI is the positive control: no Reactor modifiers or binding
        // helper participate in computing the expected rendered theme/brush.
        var native = new WinUI.ContentDialog
        {
            XamlRoot = H.Window.Content.XamlRoot,
            Title = Title,
            Content = new WinUI.TextBlock { Text = ContentText },
            PrimaryButtonText = "OK",
            RequestedTheme = theme,
        };
        var showing = native.ShowAsync();
        try
        {
            var opened = await ContentDialogProbe.WaitForOpen(H, Title)
                ?? throw new InvalidOperationException("Native positive-control dialog did not open.");
            await Harness.WaitFor(() => ContentDialogProbe.FindText(opened, ContentText)?.Foreground is SolidColorBrush);
            var text = ContentDialogProbe.FindText(opened, ContentText)
                ?? throw new InvalidOperationException("Native reference text did not realize.");
            if (text.Foreground is not SolidColorBrush brush)
                throw new InvalidOperationException("Native reference text has no measurable solid foreground.");
            H.Check($"DialogThemeMatrix_Native_{theme}_PositiveControl",
                ReferenceEquals(opened, native) && native.RequestedTheme == theme
                && text.ActualTheme == native.ActualTheme
                && (theme == ElementTheme.Default || native.ActualTheme == theme));
            return new(native.ActualTheme, brush.Color);
        }
        finally
        {
            native.Hide();
            await showing;
            await ContentDialogProbe.WaitForClosed(H, Title);
        }
    }

    private sealed record NativeReference(ElementTheme ActualTheme, Color Foreground);
    private sealed record ScenarioState(bool IsOpen);
    private sealed record ScenarioProps(
        ScenarioState Initial,
        ElementTheme RootTheme,
        ElementTheme OwnerTheme,
        ElementTheme? DialogTheme,
        Action<Action<ScenarioState>> CaptureSetter);

    private sealed class Scenario : Component<ScenarioProps>
    {
        public override Element Render()
        {
            var (state, setState) = UseState(Props.Initial);
            Props.CaptureSetter(setState);
            var dialog = ContentDialog(Title, TextBlock(ContentText)
                .Resources(resources => resources.Set("DialogThemeProbeBrush", Theme.PrimaryText)), "OK")
                with { IsOpen = state.IsOpen };
            if (Props.DialogTheme is { } theme) dialog = dialog.RequestedTheme(theme);
            return VStack(VStack(dialog).RequestedTheme(Props.OwnerTheme)).RequestedTheme(Props.RootTheme);
        }
    }
}
