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
/// removed from <see cref="Harness.ClickButton"/> / <see cref="Harness.ToggleCheckBox"/> /
/// <see cref="Harness.RequireButtonDisabled"/>.</para>
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
            await RequireButtonDisabledAssertsBothWays();
            await ToggleCheckBoxThrowsWhenMissing();
            await LabelsAreFlattenedForTap();
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

            // Same rule for the assertion variant: a wrong label is always a broken fixture,
            // and must never be mistaken for "the button was disabled, as expected".
            H.Check("HarnessGuard_Missing_RequireDisabledThrowsToo",
                MessageIfThrows(() => H.RequireButtonDisabled(MissingLabel)) is not null);
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

        // RequireButtonDisabled is the sanctioned way to prove a disabled button ignores
        // clicks. It must be loud in BOTH directions and must not click anything itself.
        private async Task RequireButtonDisabledAssertsBothWays()
        {
            int enabledClicks = 0, disabledClicks = 0;
            using var host = H.CreateHost();
            host.Mount(_ => VStack(
                Button("HarnessGuard_Live", () => enabledClicks++),
                Button("HarnessGuard_Inert", () => disabledClicks++).IsEnabled(false)));
            await Harness.Render();

            // Expected case: present and disabled — passes quietly.
            H.Check("HarnessGuard_RequireDisabled_PassesOnDisabled",
                MessageIfThrows(() => H.RequireButtonDisabled("HarnessGuard_Inert")) is null);
            H.Check("HarnessGuard_RequireDisabled_DisabledHandlerNotRun", disabledClicks == 0);

            // The direction a bool-returning variant could not enforce: a button the fixture
            // believed was disabled is actually live, so the next "nothing happened" assertion
            // would have been measuring the wrong thing. That must be loud, not a dropped flag.
            H.Check("HarnessGuard_RequireDisabled_ThrowsOnEnabled",
                MessageIfThrows(() => H.RequireButtonDisabled("HarnessGuard_Live")) is not null);
            await Harness.Render();
            // It asserts; it must never deliver a click as a side effect.
            H.Check("HarnessGuard_RequireDisabled_DidNotClickEnabled", enabledClicks == 0);

            // Control: the enabled button really is clickable, so the check above failed for
            // the stated reason (it is enabled) rather than because nothing works here.
            H.ClickButton("HarnessGuard_Live");
            await Harness.Render();
            H.Check("HarnessGuard_RequireDisabled_EnabledStillClickable", enabledClicks == 1);
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

        // The message ends up inside a single-line TAP record
        // (`not ok <n> <fixture>_CRASH - <msg>`, SelfTestRunner.cs), and SelfTestBatch.ParseTap
        // reads that stream line by line. A raw newline inside a control label would therefore
        // split one failure into two records, and the tail could be re-read as a forged `ok` or
        // `# Total failures:` line — turning a diagnostic into a corrupted run report.
        private async Task LabelsAreFlattenedForTap()
        {
            using var host = H.CreateHost();
            host.Mount(_ => VStack(Button("HarnessGuard_Two\nLines", () => { })));
            await Harness.Render();

            var message = MessageIfThrows(() => H.ClickButton(MissingLabel));
            H.Check("HarnessGuard_Tap_Throws", message is not null);
            H.Check("HarnessGuard_Tap_NoRawNewlineInMessage",
                message is not null && !message.Contains('\n') && !message.Contains('\r'));
            // Escaped rather than dropped, so the label is still identifiable in the report.
            H.Check("HarnessGuard_Tap_NewlineEscapedNotDropped",
                message?.Contains("HarnessGuard_Two\\nLines", StringComparison.Ordinal) == true);
        }
    }
}
