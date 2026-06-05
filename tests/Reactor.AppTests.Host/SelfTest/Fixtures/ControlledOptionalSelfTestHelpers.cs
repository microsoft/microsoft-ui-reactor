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
