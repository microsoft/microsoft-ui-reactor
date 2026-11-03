using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed class ContentDialogThemeFixture(Harness h) : SelfTestFixtureBase(h)
{
    private const string Title = "Dialog theme probe";

    public override async Task RunAsync()
    {
        var appTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark
            ? ElementTheme.Dark : ElementTheme.Light;
        var opposite = appTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        var state = new ScenarioState(opposite);
        Action<ScenarioState>? setState = null;
        var host = H.CreateHost();
        host.Mount(_ => Component<Scenario, ScenarioProps>(new(
            state, setter => setState = setter)));
        var mounted = await ContentDialogProbe.WaitForOpen(H, Title)
            ?? throw new InvalidOperationException("Dialog did not open.");
        await Harness.WaitFor(() => ContentDialogProbe.Walk<WinUI.TextBox>(mounted, _ => true) is not null);
        var editor = ContentDialogProbe.Walk<WinUI.TextBox>(mounted, _ => true)
            ?? throw new InvalidOperationException("Draft editor did not realize.");
        editor.Text = "unsaved theme-switch draft";
        editor.Select(2, 4);
        try
        {
            // The owner is opposite to the application, so accidentally using
            // the app fallback cannot pass the default-inheritance assertion.
            await CheckTheme("InheritsAtDeferredOpen", opposite);

            var themeChanges = 0;
            var themeToken = mounted.RegisterPropertyChangedCallback(
                FrameworkElement.RequestedThemeProperty, (_, _) => themeChanges++);
            try
            {
                // Prove the observer catches real theme writes before using a
                // zero count to detect unnecessary clear-and-restore work.
                mounted.RequestedTheme = appTheme;
                mounted.RequestedTheme = opposite;
                H.Check("DialogTheme_ThemeWriteObserverPositiveControl", themeChanges == 2);
                themeChanges = 0;

                state = state with { ButtonEnabled = false };
                setState!(state);
                await Harness.Render();
                H.Check("DialogTheme_NoSetterRenderUpdatesDialog", !mounted.IsPrimaryButtonEnabled);
                H.Check("DialogTheme_NoSetterRenderKeepsThemeStable", themeChanges == 0
                    && mounted.RequestedTheme == opposite);

                state = state with { ButtonEnabled = true };
                setState(state);
                await Harness.Render();
                H.Check("DialogTheme_RepeatedNoSetterRenderKeepsThemeStable", themeChanges == 0
                    && mounted.IsPrimaryButtonEnabled && mounted.RequestedTheme == opposite);
            }
            finally
            {
                mounted.UnregisterPropertyChangedCallback(FrameworkElement.RequestedThemeProperty, themeToken);
            }

            state = state with { ParentTheme = appTheme };
            setState!(state);
            await Harness.Render();
            await CheckTheme("FollowsParentRender", appTheme);

            var owner = H.FindControl<WinUI.StackPanel>(panel =>
                AutomationProperties.GetName(panel) == "Dialog theme owner")
                ?? throw new InvalidOperationException("Dialog owner is missing.");
            // Establish a contradictory starting value even if the preceding
            // parent-render check failed. Otherwise a missing subscription could
            // leave the dialog at `opposite` and this next check would pass for
            // precisely the wrong reason.
            Current().RequestedTheme = appTheme;
            if (!await Harness.WaitFor(() => Current().ActualTheme == appTheme))
                throw new InvalidOperationException("Native theme-change precondition could not be established.");
            // No Reactor render: prove the ActualThemeChanged subscription,
            // rather than merely testing that Update copies a theme property.
            owner.RequestedTheme = opposite;
            await CheckTheme("FollowsNativeAncestorChange", opposite);

            state = state with { DialogTheme = appTheme };
            setState!(state);
            await Harness.Render();
            await CheckTheme("ExplicitThemeReachesRealDialog", appTheme);
            owner.RequestedTheme = appTheme;
            await Harness.Render();
            owner.RequestedTheme = opposite;
            await CheckTheme("ExplicitThemeWinsOverAncestor", appTheme);

            state = state with { DialogTheme = opposite };
            setState!(state);
            await Harness.Render();
            await CheckTheme("ExplicitThemeUpdatesWhileOpen", opposite);

            state = state with { DialogTheme = ElementTheme.Default };
            setState(state);
            await Harness.Render();
            await CheckTheme("ExplicitDefaultFollowsOwner", opposite);
            owner.RequestedTheme = appTheme;
            await CheckTheme("ExplicitDefaultFollowsNativeChange", appTheme);
            owner.RequestedTheme = opposite;

            state = state with { DialogTheme = null };
            setState!(state);
            await Harness.Render();
            await CheckTheme("RemovingOverrideResumesFollowing", opposite);
            owner.RequestedTheme = appTheme;
            await CheckTheme("FollowingRemainsLiveAfterRemoval", appTheme);

            state = state with { NativeOverride = true };
            setState!(state);
            await Harness.Render();
            await CheckTheme("NativeSetterWins", opposite);
            owner.RequestedTheme = opposite;
            await Harness.Render();
            owner.RequestedTheme = appTheme;
            await CheckTheme("FollowingDoesNotOverwriteNativeSetter", opposite);

            // Setter equals the followed value. A value-comparison detector
            // misses this assignment and later lets the owner overwrite it.
            owner.RequestedTheme = opposite;
            state = state with { ParentTheme = opposite, NativeOverride = true };
            setState!(state);
            await Harness.Render();
            await CheckTheme("EqualNativeSetterWins", opposite);
            // The owner's modifier is applied after its children update. Render
            // once more with that owner already settled, changing only a dialog
            // prop so the setter is observed against the same followed value.
            state = state with { ButtonEnabled = false };
            setState!(state);
            await Harness.Render();
            await CheckTheme("EqualNativeSetterAfterOwnerSettles", opposite);
            owner.RequestedTheme = appTheme;
            await Harness.WaitFor(() => owner.ActualTheme == appTheme);
            await Harness.Render();
            await CheckTheme("EqualNativeSetterRemainsPinned", opposite);

            state = state with { NativeOverride = false, ParentTheme = appTheme, ButtonEnabled = true };
            setState!(state);
            await Harness.Render();
            await CheckTheme("RemovingNativeSetterResumesFollowing", appTheme);

            WinUI.ContentDialogClosingDeferral? closingDeferral = null;
            mounted.Closing += (_, args) => closingDeferral = args.GetDeferral();
            try
            {
                state = state with { IsOpen = false };
                setState!(state);
                await Harness.Render();
                if (!await Harness.WaitFor(() => closingDeferral is not null))
                    throw new InvalidOperationException("Dialog did not enter deferred closing.");
                var closingTheme = mounted.RequestedTheme;
                state = state with { ParentTheme = opposite, ButtonEnabled = false };
                setState(state);
                await Harness.Render();
                owner.RequestedTheme = opposite;
                await Harness.Render();
                H.Check("DialogTheme_ClosingStopsFollowing", mounted.RequestedTheme == closingTheme,
                    $"While closing: expected {closingTheme}; actual {mounted.RequestedTheme}");
                H.Check("DialogTheme_ClosingIgnoresPropUpdates", mounted.IsPrimaryButtonEnabled);
            }
            finally { closingDeferral?.Complete(); }
            H.Check("DialogTheme_Closes", await ContentDialogProbe.WaitForClosed(H, Title));

            state = state with { IsOpen = true };
            setState!(state);
            await Harness.Render();
            mounted = await ContentDialogProbe.WaitForOpen(H, Title)
                ?? throw new InvalidOperationException("Dialog did not reopen.");
            await Harness.WaitFor(() => ContentDialogProbe.Walk<WinUI.TextBox>(mounted, _ => true) is not null);
            var reopenedEditor = ContentDialogProbe.Walk<WinUI.TextBox>(mounted, _ => true)
                ?? throw new InvalidOperationException("Reopened editor did not realize.");
            // Pooling may legitimately reuse the old native TextBox. The user
            // contract is a reset draft, not allocation of a different object.
            H.Check("DialogTheme_ReopenResetsDraft", reopenedEditor.Text == "seed");
            editor = reopenedEditor;
            editor.Text = "unsaved theme-switch draft";
            editor.Select(2, 4);
            await CheckTheme("ReopenUsesCurrentOwnerTheme", opposite);

            state = state with { IncludeDialog = false };
            setState!(state);
            await Harness.Render();
            H.Check("DialogTheme_UnmountCloses", await ContentDialogProbe.WaitForClosed(H, Title));
        }
        finally
        {
            mounted.Hide();
            await ContentDialogProbe.WaitForClosed(H, Title);
        }

        WinUI.ContentDialog Current() => ContentDialogProbe.FindOpen(H, Title)
            ?? throw new InvalidOperationException("Dialog disappeared during theme update.");

        async Task CheckTheme(string check, ElementTheme expected)
        {
            await Harness.WaitFor(() => Current().ActualTheme == expected
                && ContentDialogProbe.FindText(Current(), "Themed dialog content")?.ActualTheme == expected);
            var current = Current();
            H.Check("DialogTheme_" + check, ReferenceEquals(current, mounted)
                && current.RequestedTheme == expected && current.ActualTheme == expected
                && ContentDialogProbe.FindText(current, "Themed dialog content")?.ActualTheme == expected,
                $"Expected {expected}; requested {current.RequestedTheme}; actual {current.ActualTheme}");
            var liveEditor = ContentDialogProbe.Walk<WinUI.TextBox>(current, _ => true);
            H.Check("DialogTheme_" + check + "_PreservesDraft", ReferenceEquals(liveEditor, editor)
                && liveEditor?.Text == "unsaved theme-switch draft"
                && liveEditor.SelectionStart == 2 && liveEditor.SelectionLength == 4);
        }
    }

    private sealed record ScenarioState(
        ElementTheme ParentTheme,
        ElementTheme? DialogTheme = null,
        bool IsOpen = true,
        bool IncludeDialog = true,
        bool NativeOverride = false,
        bool ButtonEnabled = true);

    private sealed record ScenarioProps(
        ScenarioState Initial,
        Action<Action<ScenarioState>> CaptureSetter);

    private sealed class Scenario : Component<ScenarioProps>
    {
        public override Element Render()
        {
            var (state, setState) = UseState(Props.Initial);
            Props.CaptureSetter(setState);
            var dialog = ContentDialog(Title,
                VStack(TextBlock("Themed dialog content"), TextBox("seed")), "OK") with
            {
                IsOpen = state.IsOpen,
                IsPrimaryButtonEnabled = state.ButtonEnabled,
            };
            if (state.DialogTheme is { } theme) dialog = dialog.RequestedTheme(theme);
            if (state.NativeOverride)
            {
                var opposite = Application.Current.RequestedTheme == ApplicationTheme.Dark
                    ? ElementTheme.Light : ElementTheme.Dark;
                dialog = dialog.Set(control => control.RequestedTheme = opposite);
            }
            return VStack(
                TextBlock("Owner"),
                state.IncludeDialog ? dialog : null)
                .AutomationName("Dialog theme owner")
                .RequestedTheme(state.ParentTheme);
        }
    }
}
