using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class TitleBarLeftHeaderFixture
{
    private sealed class Lifetime
    {
        public int Mounts;
        public int Cleanups;
    }

    private sealed record ProbeProps(string Role, string Label, Lifetime Lifetime);

    private sealed class Probe : Component<ProbeProps>
    {
        public override Element Render()
        {
            var (draft, setDraft) = UseState("initial draft");
            var (count, setCount) = UseState(0);
            UseEffect(() =>
            {
                Props.Lifetime.Mounts++;
                return () => Props.Lifetime.Cleanups++;
            });
            return HStack(4,
                TextBlock(Props.Label).AutomationId($"LH_{Props.Role}_Label"),
                TextBox(draft, setDraft).Width(96).AutomationName($"{Props.Role} draft")
                    .AutomationId($"LH_{Props.Role}_Draft"),
                Button($"{count}", () => setCount(count + 1)).AutomationName($"{Props.Role} action")
                    .AutomationId($"LH_{Props.Role}_Button"));
        }
    }

    private sealed class Scene : Component
    {
        public readonly Lifetime Left = new();
        public readonly Lifetime Center = new();
        public readonly Lifetime Right = new();
        public Action<int>? SetPhase;
        public WinUI.TitleBar? Bar;

        public override Element Render()
        {
            var (phase, setPhase) = UseState(0);
            SetPhase = setPhase;
            Element? left = phase switch
            {
                0 or 4 => null,
                3 => TextBlock("replacement").AutomationId("LH_Replacement"),
                _ => Component<Probe, ProbeProps>(new("Left", phase == 2 ? "updated" : "initial", Left)),
            };
            var bar = TitleBar("Header slots")
                .Content(Component<Probe, ProbeProps>(new("Center", "center", Center)))
                .RightHeader(Component<Probe, ProbeProps>(new("Right", "right", Right)));
            return VStack((bar with { LeftHeader = left }).Set(control => Bar = control), TextBlock("Body"));
        }
    }

    internal sealed class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previousPolicy = ReactorApp.ShutdownPolicy;
            var previousDispatcher = ReactorApp.UIDispatcher;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorApp.UIDispatcher ??= DispatcherQueue.GetForCurrentThread();
            var scene = new Scene();
            ReactorWindow? window = null;
            try
            {
                window = ReactorApp.OpenWindow(new WindowSpec
                {
                    Title = "TitleBar LeftHeader",
                    Width = 1120,
                    Height = 260,
                }, () => scene);
                await Settle();
                H.Check("TitleBarLeftHeader_DefaultAbsent", scene.Bar is { LeftHeader: null }
                    && Find<WinUI.TextBox>("LH_Left_Draft") is null && scene.Left.Mounts == 0);
                var center = Find<WinUI.TextBox>("LH_Center_Draft");
                var right = Find<WinUI.TextBox>("LH_Right_Draft");
                H.Check("TitleBarLeftHeader_OtherSlotsVisible", center is { ActualWidth: > 0, ActualHeight: > 0 }
                    && right is { ActualWidth: > 0, ActualHeight: > 0 });

                await Phase(1);
                var draft = Find<WinUI.TextBox>("LH_Left_Draft");
                H.Check("TitleBarLeftHeader_MountedAndRendered", draft is { ActualWidth: > 0, ActualHeight: > 0 }
                    && Find<WinUI.TextBlock>("LH_Left_Label")?.Text == "initial"
                    && scene.Left.Mounts == 1 && scene.Left.Cleanups == 0);
                if (draft is null) return;
                draft.Text = "user draft";
                await Settle();
                var button = Find<WinUI.Button>("LH_Left_Button");
                var invoke = button is null ? null
                    : FrameworkElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                H.Check("TitleBarLeftHeader_ActionAvailable", invoke is not null);
                invoke?.Invoke();
                await Settle();
                H.Check("TitleBarLeftHeader_ActionUpdatesChild", Find<WinUI.Button>("LH_Left_Button")?.Content as string == "1");

                await Phase(2);
                H.Check("TitleBarLeftHeader_UpdatePreservesDraftAndState", ReferenceEquals(draft, Find<WinUI.TextBox>("LH_Left_Draft"))
                    && Find<WinUI.TextBox>("LH_Left_Draft")?.Text == "user draft"
                    && Find<WinUI.TextBlock>("LH_Left_Label")?.Text == "updated"
                    && Find<WinUI.Button>("LH_Left_Button")?.Content as string == "1"
                    && scene.Left.Mounts == 1 && scene.Left.Cleanups == 0);
                H.Check("TitleBarLeftHeader_UpdateLeavesOtherSlotsMounted", ReferenceEquals(center, Find<WinUI.TextBox>("LH_Center_Draft"))
                    && ReferenceEquals(right, Find<WinUI.TextBox>("LH_Right_Draft"))
                    && scene.Center.Mounts == 1 && scene.Right.Mounts == 1);

                await Phase(3);
                H.Check("TitleBarLeftHeader_TypeReplacementCleansOldChild", Find<WinUI.TextBox>("LH_Left_Draft") is null
                    && Find<WinUI.TextBlock>("LH_Replacement") is { Text: "replacement", ActualWidth: > 0 }
                    && scene.Left.Cleanups == 1);
                await Phase(4);
                H.Check("TitleBarLeftHeader_RemovalClearsNativeSlot", scene.Bar?.LeftHeader is null
                    && Find<WinUI.TextBlock>("LH_Replacement") is null);
                await Phase(5);
                H.Check("TitleBarLeftHeader_RemountStartsFresh", Find<WinUI.TextBox>("LH_Left_Draft")?.Text == "initial draft"
                    && Find<WinUI.Button>("LH_Left_Button")?.Content as string == "0"
                    && scene.Left.Mounts == 2 && scene.Left.Cleanups == 1);
                window.Host.Dispose();
                H.Check("TitleBarLeftHeader_HostDisposalCleansAllSlots", scene.Left.Cleanups == 2
                    && scene.Center.Cleanups == 1 && scene.Right.Cleanups == 1);
            }
            finally
            {
                try
                {
                    await AsyncTeardownGuard.CloseAndSettleAsync("SelfTest.TitleBarLeftHeader.Close", () =>
                    {
                        try { window?.Close(); }
                        catch (Exception exception) when (exception is COMException or InvalidOperationException or ArgumentException)
                        {
                            Console.WriteLine($"# TitleBarLeftHeader teardown: {exception.GetType().Name} 0x{exception.HResult:X8}");
                        }
                    }, settleMs: 120);
                }
                finally
                {
                    ReactorApp.ShutdownPolicy = previousPolicy;
                    ReactorApp.UIDispatcher = previousDispatcher;
                }
            }

            async Task Settle()
            {
                await window!.Host.WaitForIdleAsync();
                await Harness.Render(100);
                (window.NativeWindow.Content as UIElement)?.UpdateLayout();
            }

            async Task Phase(int phase)
            {
                scene.SetPhase!(phase);
                await Settle();
            }

            T? Find<T>(string automationId) where T : FrameworkElement =>
                FindVisual<T>(window!.NativeWindow.Content as DependencyObject, automationId);
        }
    }

    private static T? FindVisual<T>(DependencyObject? root, string automationId) where T : FrameworkElement
    {
        if (root is null) return null;
        if (root is T found && AutomationProperties.GetAutomationId(found) == automationId) return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindVisual<T>(VisualTreeHelper.GetChild(root, i), automationId) is { } child) return child;
        return null;
    }
}
