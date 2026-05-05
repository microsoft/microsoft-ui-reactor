using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Repro for https://github.com/microsoft/microsoft-ui-reactor/issues/142
///
/// Custom control whose backing <c>DependencyProperty</c> field is
/// <c>private static readonly</c>. WinUI's lifted XAML parser fails to
/// resolve the DP by name when the default Style applies (loaded from
/// <c>Themes/Generic.xaml</c>):
///   "Failed to create a 'Microsoft.UI.Xaml.DependencyProperty' from the text 'MyText'."
///
/// Mirrors the customer's WinUI Class Library + Custom Control template repro.
/// </summary>
public sealed partial class CustomControlWithPrivateDp : Control
{
    public CustomControlWithPrivateDp()
    {
        DefaultStyleKey = typeof(CustomControlWithPrivateDp);
    }

    public string MyText
    {
        get => (string)GetValue(MyTextProperty);
        private set => SetValue(MyTextProperty, value);
    }

    // Intentionally private — the bug repro hinges on this being non-public.
    private static readonly DependencyProperty MyTextProperty =
        DependencyProperty.Register(
            nameof(MyText),
            typeof(string),
            typeof(CustomControlWithPrivateDp),
            new PropertyMetadata("MyCustomControl"));
}

internal static class Issue142Fixtures
{
    internal class CustomControlPrivateDp_Renders(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ =>
                VStack(
                    TextBlock("Issue142 Repro"),
                    new XamlHostElement(() => new CustomControlWithPrivateDp())
                )
            );

            await Harness.Render();

            H.Check("Issue142_TitleVisible",
                H.FindText("Issue142 Repro") is not null);

            var custom = H.FindControl<CustomControlWithPrivateDp>(_ => true);
            H.Check("Issue142_CustomControlMounted", custom is not null);

            // The ControlTemplate's TextBlock should render the default DP
            // value ("MyCustomControl") via {TemplateBinding MyText}. If the
            // XAML parser fails to resolve the DP, the Template setter is
            // dropped and no TextBlock with that text appears.
            H.Check("Issue142_TemplateBoundTextRendered",
                H.FindText("MyCustomControl") is not null);
        }
    }

    /// <summary>
    /// Variant of the issue #142 repro where the custom control lives in a
    /// *separate* assembly (Reactor.AppTests.ThirdPartyControls), simulating a
    /// real third-party control NuGet. The entry-assembly auto-discovery in
    /// <see cref="ReactorApp"/> can't see this provider; the consuming app
    /// must opt in via <see cref="ReactorApp.RegisterControlAssembly(global::System.Reflection.Assembly)"/>
    /// — analogous to Win2D / CommunityToolkit's documented setup in pure WinUI.
    ///
    /// We also assert that the registration API actually surfaces a non-empty
    /// provider list, which is the part that makes a difference for no-XAML
    /// consumer apps where the compiler's auto-chaining doesn't run.
    /// </summary>
    internal class ThirdPartyControlPrivateDp_Renders(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Register before the 3P control instance is created. In a Reactor app with
            // *no XAML files of its own*, this call is required: there's no consuming-app
            // compiler-generated XamlMetaDataProvider whose `OtherProviders` would
            // transitively chain to the 3P library, so the lifted XAML loader otherwise
            // can't resolve `tp:` or {TemplateBinding MyText} from the 3P Generic.xaml.
            // (This test host happens to have its own Themes/Generic.xaml, so the bug
            // wouldn't crash here without this call — but the registration is what real
            // no-XAML Reactor apps need, and we verify the plumbing below.)
            ReactorApp.RegisterControlAssembly(typeof(global::Reactor.AppTests.ThirdPartyControls.ThirdPartyControlWithPrivateDp).Assembly);

            H.Check("Issue142_3P_ProviderRegistered",
                ReactorApp.RegisteredControlAssemblyProviders.Length > 0);

            var host = H.CreateHost();
            host.Mount(_ =>
                VStack(
                    TextBlock("Issue142 3P Repro"),
                    new XamlHostElement(() => new global::Reactor.AppTests.ThirdPartyControls.ThirdPartyControlWithPrivateDp())
                )
            );

            await Harness.Render();

            H.Check("Issue142_3P_TitleVisible",
                H.FindText("Issue142 3P Repro") is not null);

            var custom = H.FindControl<global::Reactor.AppTests.ThirdPartyControls.ThirdPartyControlWithPrivateDp>(_ => true);
            H.Check("Issue142_3P_ControlMounted", custom is not null);

            // ControlTemplate in the 3P library's Generic.xaml renders the default
            // value ("ThirdPartyDefault") through {TemplateBinding MyText}. If the
            // registered provider isn't consulted, the parse fails and no TextBlock
            // with that text appears.
            H.Check("Issue142_3P_TemplateBoundTextRendered",
                H.FindText("ThirdPartyDefault") is not null);
        }
    }
}
