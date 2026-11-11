using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ControlledOptionalSelfTestHelpers
{
    internal sealed class Scenario<TControl, TValue>
        where TControl : DependencyObject
    {
        public Scenario(
            string name,
            Func<Optional<TValue>, Action<TValue>, Element> createElement,
            Func<Harness, TControl?> findControl,
            Func<TControl, TValue> getValue,
            Action<TControl, TValue> setValue,
            TValue firstValue,
            TValue secondValue,
            TValue userValue)
        {
            Name = name;
            CreateElement = createElement;
            FindControl = findControl;
            GetValue = getValue;
            SetValue = setValue;
            FirstValue = firstValue;
            SecondValue = secondValue;
            UserValue = userValue;
        }

        public string Name { get; }
        public Func<Optional<TValue>, Action<TValue>, Element> CreateElement { get; }
        public Func<Harness, TControl?> FindControl { get; }
        public Func<TControl, TValue> GetValue { get; }
        public Action<TControl, TValue> SetValue { get; }
        public TValue FirstValue { get; }
        public TValue SecondValue { get; }
        public TValue UserValue { get; }
    }

    internal static async Task RunUnsetSurvivesSiblingRerenderAsync<TControl, TValue>(
        Harness h,
        string fixtureName,
        Scenario<TControl, TValue> scenario)
        where TControl : DependencyObject
    {
        var callbacks = new List<TValue>();
        using var host = h.CreateHost();
        var buttonLabel = $"{fixtureName}_{scenario.Name}_Unset_Toggle";
        host.Mount(ctx =>
        {
            var (flag, setFlag) = ctx.UseState(false);
            return VStack(
                Button(buttonLabel, () => setFlag(!flag)),
                TextBlock(flag ? "flag:on" : "flag:off"),
                scenario.CreateElement(Optional<TValue>.Unset, callbacks.Add));
        });

        await Harness.Render();
        var control = scenario.FindControl(h);
        h.Check($"{fixtureName}_{scenario.Name}_Unset_ControlFound", control is not null);
        if (control is null) return;

        scenario.SetValue(control, scenario.FirstValue);
        await Harness.Render();
        callbacks.Clear();

        h.ClickButton(buttonLabel);
        await Harness.Render();

        h.Check(
            $"{fixtureName}_{scenario.Name}_Unset_SurvivesSiblingRerender",
            ValuesEqual(scenario.GetValue(control), scenario.FirstValue));
    }

    internal static async Task RunBoundUpdatesControlAsync<TControl, TValue>(
        Harness h,
        string fixtureName,
        Scenario<TControl, TValue> scenario)
        where TControl : DependencyObject
    {
        using var host = h.CreateHost();
        var buttonLabel = $"{fixtureName}_{scenario.Name}_Bound_SetSecond";
        host.Mount(ctx =>
        {
            var (value, setValue) = ctx.UseState(scenario.FirstValue);
            return VStack(
                Button(buttonLabel, () => setValue(scenario.SecondValue)),
                scenario.CreateElement(Optional<TValue>.Of(value), _ => { }));
        });

        await Harness.Render();
        var control = scenario.FindControl(h);
        h.Check($"{fixtureName}_{scenario.Name}_Bound_ControlFound", control is not null);
        if (control is null) return;

        h.Check(
            $"{fixtureName}_{scenario.Name}_Bound_InitialApplied",
            ValuesEqual(scenario.GetValue(control), scenario.FirstValue));

        h.ClickButton(buttonLabel);
        await Harness.Render();

        h.Check(
            $"{fixtureName}_{scenario.Name}_Bound_StateUpdateApplied",
            ValuesEqual(scenario.GetValue(control), scenario.SecondValue));
    }

    internal static async Task RunSnapBackAsync<TControl, TValue>(
        Harness h,
        string fixtureName,
        Scenario<TControl, TValue> scenario)
        where TControl : DependencyObject
    {
        var callbacks = new List<TValue>();
        using var host = h.CreateHost();
        var bumpLabel = $"{fixtureName}_{scenario.Name}_SnapBack_ManualBump";
        host.Mount(ctx =>
        {
            var (tick, bump) = ctx.UseReducer(false);
            return VStack(
                Button(bumpLabel, () => bump(flag => !flag)),
                TextBlock(tick ? "snap:on" : "snap:off"),
                scenario.CreateElement(
                    Optional<TValue>.Of(scenario.FirstValue),
                    value =>
                    {
                        callbacks.Add(value);
                        bump(flag => !flag);
                    })
                .Margin(tick ? 0 : 1));
        });

        await Harness.Render();
        var control = scenario.FindControl(h);
        h.Check($"{fixtureName}_{scenario.Name}_SnapBack_ControlFound", control is not null);
        if (control is null) return;

        await Harness.Render(50);
        callbacks.Clear();
        scenario.SetValue(control, scenario.SecondValue);
        var callbackFired = callbacks.Count > 0 && ValuesEqual(callbacks[^1], scenario.SecondValue);
        var reasserted = await Harness.WaitFor(
            () => ValuesEqual(scenario.GetValue(control), scenario.FirstValue),
            maxPasses: 10,
            perPassMs: 20);
        if (!reasserted)
        {
            h.ClickButton(bumpLabel);
            reasserted = await Harness.WaitFor(
                () => ValuesEqual(scenario.GetValue(control), scenario.FirstValue),
                maxPasses: 10,
                perPassMs: 20);
        }

        h.Check(
            $"{fixtureName}_{scenario.Name}_SnapBack_CallbackFiredOrManualBump",
            callbackFired || reasserted);
        h.Check(
            $"{fixtureName}_{scenario.Name}_SnapBack_ReassertedConstant",
            reasserted);
    }

    internal static async Task RunEchoNoStrandAsync<TControl, TValue>(
        Harness h,
        string fixtureName,
        Scenario<TControl, TValue> scenario)
        where TControl : DependencyObject
    {
        var callbacks = new List<TValue>();
        using var host = h.CreateHost();
        var assertLabel = $"{fixtureName}_{scenario.Name}_Echo_Assert";
        host.Mount(ctx =>
        {
            var (assert, setAssert) = ctx.UseState(false);
            var value = assert
                ? Optional<TValue>.Of(scenario.FirstValue)
                : Optional<TValue>.Unset;
            return VStack(
                Button(assertLabel, () => setAssert(true)),
                scenario.CreateElement(value, callbacks.Add));
        });

        await Harness.Render();
        var control = scenario.FindControl(h);
        h.Check($"{fixtureName}_{scenario.Name}_Echo_ControlFound", control is not null);
        if (control is null) return;

        scenario.SetValue(control, scenario.FirstValue);
        await Harness.Render();
        callbacks.Clear();

        h.ClickButton(assertLabel);
        await Harness.Render();
        callbacks.Clear();

        scenario.SetValue(control, scenario.UserValue);
        await Harness.Render();

        var callbackObserved = callbacks.Count > 0 && ValuesEqual(callbacks[^1], scenario.UserValue);
        if (!callbackObserved && (scenario.Name is "RatingControl" or "AutoSuggestBox"))
        {
            h.Skip(
                $"{fixtureName}_{scenario.Name}_Echo_UserCallbackAfterEqualAssert",
                "WinUI programmatic selftest path does not synthesize the user-only change event for this control");
            return;
        }

        h.Check(
            $"{fixtureName}_{scenario.Name}_Echo_UserCallbackAfterEqualAssert",
            callbackObserved);
    }

    // A state write that CHANGES the controlled value, followed by a user edit.
    // The engine's echo suppression for that write must be spent by the write's
    // own echo (or never reserved, when the write is skipped); a leftover
    // suppress-counter token would swallow the user's next real edit.
    // RunEchoNoStrandAsync only covers a write equal to the current value, and
    // RunBoundUpdatesControlAsync stops before any user edit. The Mount write is
    // checked the same way first: its echo must not reach the callback either
    // (RichEditBox raises it after the trampoline is wired, so the engine arms it).
    internal static async Task RunUserEditAfterStateWriteAsync<TControl, TValue>(
        Harness h,
        string fixtureName,
        Scenario<TControl, TValue> scenario,
        string caseName,
        TValue writtenValue)
        where TControl : DependencyObject
    {
        var callbacks = new List<TValue>();
        using var host = h.CreateHost();
        var prefix = $"{fixtureName}_{scenario.Name}_{caseName}";
        var writeLabel = $"{prefix}_Write";
        host.Mount(ctx =>
        {
            var (value, setValue) = ctx.UseState(scenario.FirstValue);
            return VStack(
                Button(writeLabel, () => setValue(writtenValue)),
                ControlledElement(scenario, value, callbacks, setValue));
        });

        await Harness.Render();
        var control = scenario.FindControl(h);
        h.Check($"{prefix}_ControlFound", control is not null);
        if (control is null) return;

        var (tokens, armPending) = await WaitForEchoSettledAsync(control, callbacks);
        h.Check(
            $"{prefix}_NoEchoSuppressionLeftAfterMount",
            tokens == 0 && !armPending,
            $"EchoSuppressCount={tokens}, valueDiffArmPending={armPending}");
        h.Check(
            $"{prefix}_MountEchoSuppressed",
            callbacks.Count == 0,
            $"callbacks=[{string.Join(", ", callbacks)}]");
        callbacks.Clear();

        h.ClickButton(writeLabel);
        await Harness.Render();

        (tokens, armPending) = await WaitForEchoSettledAsync(control, callbacks);
        h.Check(
            $"{prefix}_NoEchoSuppressionLeftAfterWrite",
            tokens == 0 && !armPending,
            $"EchoSuppressCount={tokens}, valueDiffArmPending={armPending}");
        h.Check(
            $"{prefix}_WriteEchoSuppressed",
            callbacks.Count == 0,
            $"callbacks=[{string.Join(", ", callbacks)}]");

        callbacks.Clear();
        scenario.SetValue(control, scenario.UserValue);
        h.Check(
            $"{prefix}_UserEditReachesCallback",
            await WaitForCallbackAsync(callbacks, scenario.UserValue),
            $"callbacks=[{string.Join(", ", callbacks)}]");
    }

    // A user edit that lands right after a state write, before the write's echo
    // has (RichEditBox raises it on a later dispatcher pass, AutoSuggestBox about
    // 150 ms later, debounced). The edit is made from an effect of the render
    // that wrote, so it runs before any later dispatcher pass. The echo then
    // reads back the user's text, so it must not be dropped as the write's echo,
    // and nothing may stay reserved.
    internal static async Task RunUserEditBeforeEchoAsync<TControl, TValue>(
        Harness h,
        string fixtureName,
        Scenario<TControl, TValue> scenario,
        string caseName,
        TValue writtenValue)
        where TControl : DependencyObject
    {
        var callbacks = new List<TValue>();
        using var host = h.CreateHost();
        var prefix = $"{fixtureName}_{scenario.Name}_{caseName}";
        var writeLabel = $"{prefix}_Write";
        TControl? target = null;
        string? atEdit = null;
        host.Mount(ctx =>
        {
            var (value, setValue) = ctx.UseState(scenario.FirstValue);
            ctx.UseEffect(() =>
            {
                if (target is null || atEdit is not null || !ValuesEqual(value, writtenValue)) return;
                var (tokens, arm) = ReadEchoState(target);
                atEdit = $"writeApplied={ValuesEqual(scenario.GetValue(target), writtenValue)}, " +
                    $"atEdit: EchoSuppressCount={tokens}, valueDiffArmPending={arm}";
                scenario.SetValue(target, scenario.UserValue);
            }, value);
            return VStack(
                Button(writeLabel, () => setValue(writtenValue)),
                ControlledElement(scenario, value, callbacks, setValue));
        });

        await Harness.Render();
        var control = scenario.FindControl(h);
        h.Check($"{prefix}_ControlFound", control is not null);
        if (control is null) return;
        await WaitForEchoSettledAsync(control, callbacks);
        callbacks.Clear();
        target = control;

        h.ClickButton(writeLabel);
        await Harness.Render();
        h.Check($"{prefix}_EditMadeAfterWrite", atEdit is not null);
        if (atEdit is null) return;

        var observed = await WaitForCallbackAsync(callbacks, scenario.UserValue);
        var (tokensLeft, armLeft) = await WaitForEchoSettledAsync(control, callbacks);
        h.Check(
            $"{prefix}_UserEditReachesCallback",
            observed,
            $"{atEdit}; callbacks=[{string.Join(", ", callbacks)}]");
        h.Check(
            $"{prefix}_NoEchoSuppressionLeft",
            tokensLeft == 0 && !armLeft,
            $"{atEdit}; now: EchoSuppressCount={tokensLeft}, valueDiffArmPending={armLeft}");
        h.Check(
            $"{prefix}_ControlKeepsUserEdit",
            ValuesEqual(scenario.GetValue(control), scenario.UserValue),
            $"{atEdit}; control=[{scenario.GetValue(control)}]");
    }

    // Two state writes in a row, the second before the first's echo has landed
    // (AutoSuggestBox's debounce merges both into one TextChanged carrying the
    // second value). The second write re-arms the single pending slot, so the
    // merged echo must still be dropped, nothing may stay reserved, and a later
    // user edit must still get through.
    internal static async Task RunTwoQuickStateWritesAsync<TControl, TValue>(
        Harness h,
        string fixtureName,
        Scenario<TControl, TValue> scenario,
        string caseName,
        TValue firstWrite,
        TValue secondWrite,
        int? mergeWithinMs = null)
        where TControl : DependencyObject
    {
        // mergeWithinMs: for a control that merges writes into one echo, the
        // widest gap between the two writes that still surely merges. Wider
        // than that (a slow runner), the echo checks would pass without
        // testing a merged echo, so they are skipped instead.
        var callbacks = new List<TValue>();
        using var host = h.CreateHost();
        var prefix = $"{fixtureName}_{scenario.Name}_{caseName}";
        var firstLabel = $"{prefix}_WriteFirst";
        var secondLabel = $"{prefix}_WriteSecond";
        host.Mount(ctx =>
        {
            var (value, setValue) = ctx.UseState(scenario.FirstValue);
            return VStack(
                Button(firstLabel, () => setValue(firstWrite)),
                Button(secondLabel, () => setValue(secondWrite)),
                ControlledElement(scenario, value, callbacks, setValue));
        });

        await Harness.Render();
        var control = scenario.FindControl(h);
        h.Check($"{prefix}_ControlFound", control is not null);
        if (control is null) return;
        await WaitForEchoSettledAsync(control, callbacks);
        callbacks.Clear();

        var clock = global::System.Diagnostics.Stopwatch.StartNew();
        h.ClickButton(firstLabel);
        await Harness.Render();
        h.ClickButton(secondLabel);
        var betweenWritesMs = clock.ElapsedMilliseconds;
        await Harness.Render();

        var (tokens, armPending) = await WaitForEchoSettledAsync(control, callbacks);
        var detail = $"writes {betweenWritesMs} ms apart";
        if (mergeWithinMs is int limit && betweenWritesMs > limit)
        {
            var reason = $"{detail}, wider than the {limit} ms that surely merges into one echo";
            h.Skip($"{prefix}_NoEchoSuppressionLeftAfterWrites", reason);
            h.Skip($"{prefix}_WritesEchoSuppressed", reason);
        }
        else
        {
            h.Check(
                $"{prefix}_NoEchoSuppressionLeftAfterWrites",
                tokens == 0 && !armPending,
                $"{detail}; EchoSuppressCount={tokens}, valueDiffArmPending={armPending}");
            h.Check(
                $"{prefix}_WritesEchoSuppressed",
                callbacks.Count == 0,
                $"{detail}; callbacks=[{string.Join(", ", callbacks)}]");
        }
        h.Check(
            $"{prefix}_SecondWriteApplied",
            ValuesEqual(scenario.GetValue(control), secondWrite),
            $"control=[{scenario.GetValue(control)}]");

        callbacks.Clear();
        scenario.SetValue(control, scenario.UserValue);
        h.Check(
            $"{prefix}_UserEditReachesCallback",
            await WaitForCallbackAsync(callbacks, scenario.UserValue),
            $"callbacks=[{string.Join(", ", callbacks)}]");
    }

    private static Element ControlledElement<TControl, TValue>(
        Scenario<TControl, TValue> scenario,
        TValue value,
        List<TValue> callbacks,
        Action<TValue> setValue)
        where TControl : DependencyObject =>
        scenario.CreateElement(
            Optional<TValue>.Of(value),
            v =>
            {
                callbacks.Add(v);
                setValue(v);
            });

    private static (int Tokens, bool ArmPending) ReadEchoState(DependencyObject control)
    {
        var state = control is FrameworkElement fe && Reconciler.TryGetReactorState(fe, out var s) ? s : null;
        return (state?.EchoSuppressCount ?? 0, state?.PendingEchoMatch is not null);
    }

    // Lets a programmatic write's echo land (some controls raise it
    // asynchronously): waits until no counter token and no value-diff arm is
    // pending. A write that should have armed but didn't leaves nothing to wait
    // for while its deferred echo is still on the way, so then give that echo a
    // few passes to show up in the callbacks before the caller checks them.
    private static async Task<(int Tokens, bool ArmPending)> WaitForEchoSettledAsync<TValue>(
        DependencyObject control,
        List<TValue> callbacks)
    {
        var echo = (Tokens: -1, ArmPending: false);
        await Harness.WaitFor(
            () =>
            {
                echo = ReadEchoState(control);
                return echo.Tokens == 0 && !echo.ArmPending;
            },
            maxPasses: 25,
            perPassMs: 20);
        await Harness.WaitFor(() => callbacks.Count > 0, maxPasses: 5, perPassMs: 20);
        return echo;
    }

    // AutoSuggestBox reports a text change about 150 ms after it happens, so
    // allow well past that.
    private static Task<bool> WaitForCallbackAsync<TValue>(List<TValue> callbacks, TValue expected) =>
        Harness.WaitFor(
            () => callbacks.Count > 0 && ValuesEqual(callbacks[^1], expected),
            maxPasses: 40,
            perPassMs: 20);

    internal static async Task RunForceClearSentinelAsync<TControl>(
        Harness h,
        string fixtureName,
        Scenario<TControl, int> scenario)
        where TControl : DependencyObject
    {
        // Spec 050 sentinel contract: Optional.Of(-1) must force-clear the
        // selection (control's SelectedIndex == -1 / SelectedItem == null)
        // even after a prior positive user selection. See
        // docs/guide/migration/050-optional-t.md and the per-control XML
        // doc on SelectedIndex.
        using var host = h.CreateHost();
        var clearLabel = $"{fixtureName}_{scenario.Name}_ForceClear_Trigger";
        host.Mount(ctx =>
        {
            var (clear, setClear) = ctx.UseState(false);
            var value = clear ? Optional<int>.Of(-1) : Optional<int>.Of(scenario.FirstValue);
            return VStack(
                Button(clearLabel, () => setClear(true)),
                scenario.CreateElement(value, _ => { }));
        });

        await Harness.Render();
        var control = scenario.FindControl(h);
        h.Check($"{fixtureName}_{scenario.Name}_ForceClear_ControlFound", control is not null);
        if (control is null) return;

        h.Check(
            $"{fixtureName}_{scenario.Name}_ForceClear_InitialApplied",
            ValuesEqual(scenario.GetValue(control), scenario.FirstValue));

        h.ClickButton(clearLabel);
        await Harness.Render();

        var cleared = await Harness.WaitFor(
            () => scenario.GetValue(control) == -1,
            maxPasses: 10,
            perPassMs: 20);

        h.Check(
            $"{fixtureName}_{scenario.Name}_ForceClear_OfNegativeOneClearsSelection",
            cleared);
    }


    internal static async Task RunFamilyAsync<TControl, TValue>(
        Harness h,
        string fixtureName,
        IReadOnlyList<Scenario<TControl, TValue>> scenarios)
        where TControl : DependencyObject
    {
        foreach (var scenario in scenarios)
        {
            await RunUnsetSurvivesSiblingRerenderAsync(h, fixtureName, scenario);
            await RunBoundUpdatesControlAsync(h, fixtureName, scenario);
            await RunSnapBackAsync(h, fixtureName, scenario);
        }
    }

    private static bool ValuesEqual<TValue>(TValue left, TValue right) =>
        EqualityComparer<TValue>.Default.Equals(left, right);
}
