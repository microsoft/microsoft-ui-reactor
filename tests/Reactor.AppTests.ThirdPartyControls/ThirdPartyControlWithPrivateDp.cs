using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Reactor.AppTests.ThirdPartyControls;

/// <summary>
/// Mirrors the customer's repro from
/// https://github.com/microsoft/microsoft-ui-reactor/issues/142, but living
/// in a *separate* assembly to simulate a real third-party control NuGet.
///
/// The default Style + ControlTemplate is in <c>Themes/Generic.xaml</c> and
/// is loaded automatically by WinUI on first instance creation; the template
/// uses <c>{TemplateBinding MyText}</c>, so the lifted XAML parser must be
/// able to resolve the private DP at template-application time.
/// </summary>
public sealed partial class ThirdPartyControlWithPrivateDp : Control
{
    public ThirdPartyControlWithPrivateDp()
    {
        DefaultStyleKey = typeof(ThirdPartyControlWithPrivateDp);
    }

    public string MyText
    {
        get => (string)GetValue(MyTextProperty);
        private set => SetValue(MyTextProperty, value);
    }

    // Intentionally private — see issue #142.
    private static readonly DependencyProperty MyTextProperty =
        DependencyProperty.Register(
            nameof(MyText),
            typeof(string),
            typeof(ThirdPartyControlWithPrivateDp),
            new PropertyMetadata("ThirdPartyDefault"));
}
