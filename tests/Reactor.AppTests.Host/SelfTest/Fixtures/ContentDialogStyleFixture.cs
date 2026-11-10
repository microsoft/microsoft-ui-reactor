using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// The entrance animation itself is verified by frame-by-frame recordings.
// These checks exercise the style's rendered effect and the existing dialog
// contract, without depending on animation timing or WinUI template part names.
internal sealed class ContentDialogStyleFixture(Harness h, bool setterStyle) : SelfTestFixtureBase(h)
{
    private const string ResourceKey = "DefaultContentDialogStyle";
    private const string Title = "ContentDialog style regression";
    private static readonly global::Windows.UI.Color DefaultColor = global::Windows.UI.Color.FromArgb(255, 20, 110, 180);
    private static readonly global::Windows.UI.Color FirstColor = global::Windows.UI.Color.FromArgb(255, 30, 130, 70);
    private static readonly global::Windows.UI.Color SecondColor = global::Windows.UI.Color.FromArgb(255, 150, 50, 140);

    public override async Task RunAsync()
    {
        var originalResources = Application.Current.Resources;
        if (!originalResources.TryGetValue(ResourceKey, out var value) || value is not Style original)
            throw new InvalidOperationException("The host must load WinUI's default ContentDialog style.");
        var fallback = ColoredStyle(original, DefaultColor);
        var first = ColoredStyle(original, FirstColor);
        var second = ColoredStyle(original, SecondColor);
        var counts = new Counts();
        var state = new State(CustomStyle: setterStyle ? first : null);
        Action<State>? setState = null;
        var host = H.CreateHost();
        // XamlControlsResources is Source-backed and rejects local entries.
        // A temporary outer dictionary overrides just this key and is restored
        // after the dialog is gone, keeping other fixtures' resources intact.
        var resources = new ResourceDictionary { [ResourceKey] = fallback };
        resources.MergedDictionaries.Add(originalResources);
        try
        {
            Application.Current.Resources = resources;
            host.Mount(_ => Component<Scene, SceneProps>(new(state, setter => setState = setter, counts)));
            var dialog = await Open();
            await Surface("InitialRenderedStyle", setterStyle ? FirstColor : DefaultColor);
            var editor = Editor(dialog) ?? throw new InvalidOperationException("Editor did not realize.");
            editor.Text = "user draft";
            await Harness.Render();
            H.Check("DialogStyle_ChildClickLanded", await ContentDialogProbe.WaitAndClick(dialog, "Count 0"));
            H.Check("DialogStyle_ChildStateUpdated", await Harness.WaitFor(() =>
                ContentDialogProbe.FindButton(Current(), "Count 1") is { ActualWidth: > 0 }));

            state = state with { Revision = 1, CustomStyle = setterStyle ? second : null };
            setState!(state);
            await Harness.Render();
            await Surface("UpdatedRenderedStyle", setterStyle ? SecondColor : DefaultColor);
            var updatedEditor = Editor(Current());
            H.Check("DialogStyle_UpdatePreservesInput", ReferenceEquals(dialog, Current())
                && ReferenceEquals(editor, updatedEditor) && updatedEditor?.Text == "user draft"
                && ContentDialogProbe.FindButton(Current(), "Count 1") is not null
                && counts.Mounts == 1 && counts.Cleanups == 0);
            H.Check("DialogStyle_UpdateReconcilesVisibleContent", await Harness.WaitFor(() =>
                ContentDialogProbe.FindText(Current(), "revision 1") is { ActualWidth: > 0 }
                && ContentDialogProbe.FindButton(Current(), "Continue 1") is { ActualWidth: > 0 }));
            H.Check("DialogStyle_UpdateDoesNotReopen", counts.Opened == 1 && counts.Closed == 0);

            state = state with { IsOpen = false };
            setState(state);
            H.Check("DialogStyle_DeclaredCloseHidesDialog", await ContentDialogProbe.WaitForClosed(H, Title));
            await Harness.WaitFor(() => counts.Closed == 1 && counts.Cleanups == 1);
            H.Check("DialogStyle_CloseCleansOnceAndUsesLatestCallback", counts.Closed == 1
                && counts.LastClosedRevision == 1 && counts.LastResult == WinUI.ContentDialogResult.None
                && counts.Cleanups == 1);

            state = state with { IsOpen = true, Revision = 2, CustomStyle = setterStyle ? first : null };
            setState(state);
            dialog = await Open();
            await Surface("ReopenedRenderedStyle", setterStyle ? FirstColor : DefaultColor);
            H.Check("DialogStyle_ReopenStartsFresh", Editor(dialog)?.Text == "seed"
                && ContentDialogProbe.FindButton(dialog, "Count 0") is not null
                && counts.Mounts == 2 && counts.Cleanups == 1 && counts.Opened == 2);
            H.Check("DialogStyle_ReopenedPrimaryActionLanded", await ContentDialogProbe.WaitAndClick(dialog, "Continue 2"));
            H.Check("DialogStyle_PrimaryActionClosesDialog", await ContentDialogProbe.WaitForClosed(H, Title));
            await Harness.WaitFor(() => counts.Closed == 2 && counts.Cleanups == 2);
            H.Check("DialogStyle_PrimaryResultAndCleanup", counts.Closed == 2 && counts.LastClosedRevision == 2
                && counts.LastResult == WinUI.ContentDialogResult.Primary && counts.Cleanups == 2);
        }
        finally
        {
            try
            {
                host.Dispose();
                await ContentDialogProbe.WaitForClosed(H, Title);
            }
            finally
            {
                resources.MergedDictionaries.Remove(originalResources);
                Application.Current.Resources = originalResources;
            }
        }

        WinUI.ContentDialog Current() => ContentDialogProbe.FindOpen(H, Title)
            ?? throw new InvalidOperationException("Dialog disappeared before the requested close.");

        async Task<WinUI.ContentDialog> Open()
        {
            var opened = await ContentDialogProbe.WaitForOpen(H, Title)
                ?? throw new InvalidOperationException("Dialog did not open.");
            await Harness.WaitFor(() => Editor(Current()) is { ActualWidth: > 0 });
            return opened;
        }

        async Task Surface(string check, global::Windows.UI.Color color)
        {
            bool rendered = await Harness.WaitFor(() => ContentDialogProbe.Walk<WinUI.Border>(Current(), border =>
                border is { ActualWidth: > 0, ActualHeight: > 0, Background: SolidColorBrush brush }
                && brush.Color == color) is not null);
            H.Check("DialogStyle_" + check, rendered, $"Expected rendered dialog surface {color}.");
        }
    }

    private static Style ColoredStyle(Style basis, global::Windows.UI.Color color)
    {
        var style = new Style { TargetType = typeof(WinUI.ContentDialog), BasedOn = basis };
        style.Setters.Add(new Setter(WinUI.Control.BackgroundProperty, new SolidColorBrush(color)));
        return style;
    }

    private static WinUI.TextBox? Editor(WinUI.ContentDialog dialog) => ContentDialogProbe.Walk<WinUI.TextBox>(dialog,
        editor => AutomationProperties.GetAutomationId(editor) == "DialogStyle_Draft");

    private sealed class Counts
    {
        internal int Mounts, Cleanups, Opened, Closed;
        internal int LastClosedRevision = -1;
        internal WinUI.ContentDialogResult? LastResult;
    }

    private sealed record State(bool IsOpen = true, int Revision = 0, Style? CustomStyle = null);
    private sealed record SceneProps(State Initial, Action<Action<State>> CaptureSetter, Counts Counts);
    private sealed record EditorProps(int Revision, Counts Counts);

    private sealed class Scene : Component<SceneProps>
    {
        public override Element Render()
        {
            var (state, setState) = UseState(Props.Initial);
            Props.CaptureSetter(setState);
            var dialog = ContentDialog(Title, Component<EditorContent, EditorProps>(new(state.Revision, Props.Counts)),
                primaryButtonText: $"Continue {state.Revision}") with
            {
                IsOpen = state.IsOpen,
                OnOpened = () => Props.Counts.Opened++,
                OnClosed = result =>
                {
                    Props.Counts.Closed++;
                    Props.Counts.LastClosedRevision = state.Revision;
                    Props.Counts.LastResult = result;
                },
            };
            return state.CustomStyle is { } style ? dialog.Set(native => native.Style = style) : dialog;
        }
    }

    private sealed class EditorContent : Component<EditorProps>
    {
        public override Element Render()
        {
            var (draft, setDraft) = UseState("seed");
            var (count, setCount) = UseState(0);
            var counts = Props.Counts;
            UseEffect(() =>
            {
                counts.Mounts++;
                return () => counts.Cleanups++;
            });
            return VStack(8, TextBlock($"revision {Props.Revision}"),
                TextBox(draft, setDraft).AutomationName("Draft").AutomationId("DialogStyle_Draft"),
                Button($"Count {count}", () => setCount(count + 1)));
        }
    }
}
