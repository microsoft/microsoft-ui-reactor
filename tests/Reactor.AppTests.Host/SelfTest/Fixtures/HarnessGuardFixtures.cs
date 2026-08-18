using Microsoft.UI.Reactor.Core;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1063 — the harness's own click stimulus must fail loudly.
///
/// <para><c>ClickButton</c> used to have no <c>else</c>, no throw and no return value, so a
/// missing, renamed or disabled button was byte-identical to a landed click at all ~700
/// call sites. A fixture is a stimulus followed by an assertion; when the stimulus silently
/// no-ops the assertion measures the UNSTIMULATED state, and every "X was left alone" /
/// "X was restored" / "X is still within tolerance" check passes on that state.</para>
///
/// <para>This fixture is the guard's own guard. Every check here fails if the throw is
/// removed from <see cref="Harness.ClickButton"/> / <see cref="Harness.ToggleCheckBox"/>:
/// the "threw" checks flip to false directly, and the two behavioural checks
/// (<c>ClickButtonIfEnabled</c> returning true/false) fail because the fail-open bodies
/// return <c>void</c> and cannot compile a caller that reads a result.</para>
/// </summary>
internal static class HarnessGuardFixtures
{
    private const string MissingLabel = "HarnessGuard_NoSuchButton";

    /// <summary>Runs <paramref name="act"/> and returns the message of the
    /// InvalidOperationException it threw, or null if it did not throw one.</summary>
    private static string? MessageIfThrows(Action act)
    {
        try { act(); return null; }
        catch (InvalidOperationException ex) { return ex.Message; }
    }

    internal class ClickButtonFailsLoudly(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await MissingButtonThrows();
            await DisabledButtonThrows();
            await IfEnabledReportsInsteadOfThrowing();
            await ToggleCheckBoxThrowsWhenMissing();
        }

        // A label that is nowhere in the tree must abort the fixture, and the message must
        // carry enough to fix it without a debugger: the bad label AND what was there.
        private async Task MissingButtonThrows()
        {
            using var host = H.CreateHost();
            host.Mount(_ => VStack(
                Button("HarnessGuard_Present", () => { }),
                TextBlock("HarnessGuard_Missing_Mounted")));
            await Harness.Render();

            H.Check("HarnessGuard_Missing_TreeReady", H.FindButton("HarnessGuard_Present") is not null);

            var message = MessageIfThrows(() => H.ClickButton(MissingLabel));
            H.Check("HarnessGuard_Missing_Throws", message is not null);
            H.Check("HarnessGuard_Missing_MessageNamesLabel",
                message?.Contains(MissingLabel, StringComparison.Ordinal) == true);
            // The "here is what I did find" suffix is what turns the crash line into a fix.
            H.Check("HarnessGuard_Missing_MessageListsCandidates",
                message?.Contains("HarnessGuard_Present", StringComparison.Ordinal) == true);

            // Same rule for the opt-in variant: a wrong label is always a broken fixture, so
            // `false` keeps meaning "disabled" rather than "disabled, or I typo'd it".
            H.Check("HarnessGuard_Missing_IfEnabledThrowsToo",
                MessageIfThrows(() => H.ClickButtonIfEnabled(MissingLabel)) is not null);
        }

        // The disabled case is the one that used to look most like success: the button is
        // right there, the call returns normally, and nothing was invoked.
        private async Task DisabledButtonThrows()
        {
            int clicks = 0;
            using var host = H.CreateHost();
            host.Mount(_ => VStack(
                Button("HarnessGuard_Disabled", () => clicks++).IsEnabled(false)));
            await Harness.Render();

            var btn = H.FindButton("HarnessGuard_Disabled");
            H.Check("HarnessGuard_Disabled_Mounted", btn is not null && !btn.IsEnabled);

            var message = MessageIfThrows(() => H.ClickButton("HarnessGuard_Disabled"));
            H.Check("HarnessGuard_Disabled_Throws", message is not null);
            H.Check("HarnessGuard_Disabled_MessageSaysDisabled",
                message?.Contains("disabled", StringComparison.OrdinalIgnoreCase) == true);
            // And the click genuinely did not land — the throw replaced a no-op, it did not
            // paper over a delivered click.
            H.Check("HarnessGuard_Disabled_HandlerNotRun", clicks == 0);
        }

        // ClickButtonIfEnabled is the escape hatch for fixtures that mean to prove a disabled
        // button ignores clicks. It must distinguish the two outcomes by return value.
        private async Task IfEnabledReportsInsteadOfThrowing()
        {
            int enabledClicks = 0, disabledClicks = 0;
            using var host = H.CreateHost();
            host.Mount(_ => VStack(
                Button("HarnessGuard_Live", () => enabledClicks++),
                Button("HarnessGuard_Inert", () => disabledClicks++).IsEnabled(false)));
            await Harness.Render();

            H.Check("HarnessGuard_IfEnabled_FalseOnDisabled",
                !H.ClickButtonIfEnabled("HarnessGuard_Inert"));
            H.Check("HarnessGuard_IfEnabled_DisabledHandlerNotRun", disabledClicks == 0);

            H.Check("HarnessGuard_IfEnabled_TrueOnEnabled",
                H.ClickButtonIfEnabled("HarnessGuard_Live"));
            await Harness.Render();
            // A `true` that did not actually invoke anything would be the same lie in a new
            // shape, so pin the side effect too.
            H.Check("HarnessGuard_IfEnabled_EnabledHandlerRan", enabledClicks == 1);
        }

        // ToggleCheckBox had the same fail-open shape (silent when the CheckBox is absent).
        private async Task ToggleCheckBoxThrowsWhenMissing()
        {
            using var host = H.CreateHost();
            host.Mount(_ => VStack(
                CheckBox(Optional<bool?>.Unset, _ => { }, "HarnessGuard_Box")));
            await Harness.Render();

            var cb = H.FindControl<WinUI.CheckBox>(c => c.Content as string == "HarnessGuard_Box");
            H.Check("HarnessGuard_Toggle_Mounted", cb is not null && cb.IsChecked != true);

            var message = MessageIfThrows(() => H.ToggleCheckBox("HarnessGuard_NoSuchBox"));
            H.Check("HarnessGuard_Toggle_ThrowsWhenMissing", message is not null);
            H.Check("HarnessGuard_Toggle_MessageNamesLabel",
                message?.Contains("HarnessGuard_NoSuchBox", StringComparison.Ordinal) == true);

            // The present CheckBox still toggles — the guard did not break the happy path.
            H.ToggleCheckBox("HarnessGuard_Box");
            await Harness.Render();
            H.Check("HarnessGuard_Toggle_PresentStillToggles", cb?.IsChecked == true);
        }
    }
}
