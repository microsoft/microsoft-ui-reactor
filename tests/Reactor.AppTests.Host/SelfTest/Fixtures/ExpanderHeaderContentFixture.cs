using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Xaml.Automation;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed class ExpanderHeaderContentFixture(Harness h, bool useDescriptor = false) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var reconciler = new Reconciler();
        if (useDescriptor)
            reconciler.RegisterHandler<ExpanderElement, WinUI.Expander>(
                new DescriptorHandler<ExpanderElement, WinUI.Expander>(ExpanderElement.Descriptor));

        int headerCleanups = 0, bodyCleanups = 0, actionVersion = 0;
        var body = TextBox().AutomationId("EHC_Body").OnUnmount(_ => bodyCleanups++);
        var current = Expander("plain header", body, isExpanded: true);
        var expander = (WinUI.Expander)reconciler.Mount(current, Noop)!;
        H.SetContent(expander);
        try
        {
            await Harness.Render();
            H.Check("ExpanderHeaderContent_StringHeader", expander.Header as string == "plain header"
                && expander.HeaderTemplate is null);
            var bodyInput = Input("EHC_Body");
            H.Check("ExpanderHeaderContent_BodyVisible", bodyInput is { ActualWidth: > 0, ActualHeight: > 0 });
            if (bodyInput is null) return;
            bodyInput.Text = "body draft";

            await Update((current with { Header = "fallback" }).HeaderContent(Header(1)));
            var headerInput = Input("EHC_Input");
            H.Check("ExpanderHeaderContent_CompositionMountedInHeader", headerInput is { ActualWidth: > 0, ActualHeight: > 0 }
                && H.FindText("header 1") is { ActualWidth: > 0 }
                && expander.Header is Microsoft.UI.Xaml.FrameworkElement { ActualWidth: > 0 }
                && expander.HeaderTemplate is null);
            if (headerInput is null) return;
            headerInput.Text = "header draft";
            H.ClickButton("header action");
            H.Check("ExpanderHeaderContent_ActionDispatches", actionVersion == 1);

            await Update(current.HeaderContent(Header(2)));
            H.Check("ExpanderHeaderContent_UpdatePreservesInput", ReferenceEquals(headerInput, Input("EHC_Input"))
                && Input("EHC_Input")?.Text == "header draft"
                && H.FindText("header 2") is not null && H.FindText("header 1") is null
                && headerCleanups == 0);
            H.ClickButton("header action");
            H.Check("ExpanderHeaderContent_UpdateRefreshesCallback", actionVersion == 2);
            H.Check("ExpanderHeaderContent_HeaderUpdatesPreserveBody", ReferenceEquals(bodyInput, Input("EHC_Body"))
                && Input("EHC_Body")?.Text == "body draft" && bodyCleanups == 0);

            await Update(current with { HeaderContent = null });
            H.Check("ExpanderHeaderContent_NullRestoresUnchangedString", expander.Header as string == "fallback"
                && Input("EHC_Input") is null && headerCleanups == 1 && bodyCleanups == 0);
            await Update(current.HeaderContent(Header(3)));
            H.Check("ExpanderHeaderContent_RemountStartsFresh", Input("EHC_Input") is { Text: "" }
                && H.FindText("header 3") is not null);
            await Update(current.HeaderContent(TextBlock("replacement header")));
            H.Check("ExpanderHeaderContent_TypeReplacement", expander.Header is WinUI.TextBlock { Text: "replacement header" }
                && H.FindText("replacement header") is { ActualWidth: > 0 }
                && Input("EHC_Input") is null && headerCleanups == 2);
            await Update(current with { HeaderContent = null, Header = "new fallback" });
            H.Check("ExpanderHeaderContent_StringFallbackUpdates", expander.Header as string == "new fallback"
                && H.FindText("replacement header") is null);
        }
        finally
        {
            reconciler.UnmountChild(expander);
            H.SetContent(null);
        }
        H.Check("ExpanderHeaderContent_BodyCleanup", bodyCleanups == 1 && headerCleanups == 2);

        Element Header(int version) => HStack(8,
                TextBlock($"header {version}"),
                TextBox().AutomationId("EHC_Input"),
                Button("header action", () => actionVersion = version))
            .OnUnmount(_ => headerCleanups++);

        WinUI.TextBox? Input(string id) => H.FindControl<WinUI.TextBox>(
            input => AutomationProperties.GetAutomationId(input) == id);

        async Task Update(ExpanderElement next)
        {
            reconciler.Reconcile(current, next, expander, Noop);
            current = next;
            await Harness.Render();
        }
    }

    private static void Noop() { }
}
