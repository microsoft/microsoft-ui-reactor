using System;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Hook-order-exception coverage for the hook flavours not exercised by
/// <see cref="HookStateRefactorTests"/>: UseReducer (1-arg + 2-arg overloads),
/// UseMemo, and UseRef. Also pins the internal devtools test-only setter
/// <c>UseStateSetterByIndex&lt;T&gt;</c> behaviour.
///
/// React's #1 cardinal sin is calling hooks in different orders across
/// renders — the framework's contract is that hook at slot N must always be
/// the same flavour. These tests pin the loud-failure path for the cases
/// that no other test exercises today.
/// </summary>
public class RenderContextHookOrderTests
{
    // ════════════════════════════════════════════════════════════════
    //  UseReducer<T> (single-arg overload) — hook-order exception
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseReducer_SingleArg_At_Effect_Position_Throws_HookOrderException()
    {
        // Bug shape: a developer reorders an effect into a reducer's slot
        // (or vice versa). Silent slot reuse would corrupt the cell type
        // and yield InvalidCastException deep inside the Updater closure;
        // throwing at the BeginRender boundary surfaces the developer error
        // immediately with a descriptive message.
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        ctx.UseEffect(() => { }, "dep");
        ctx.FlushEffects();

        ctx.BeginRender(() => { });
        var ex = Assert.Throws<HookOrderException>(() => ctx.UseReducer(0));
        Assert.Contains("EffectHookState", ex.Message);
        Assert.Contains("ValueHookState", ex.Message);
        Assert.Contains("UseReducer", ex.Message);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseReducer<TState, TAction> (two-arg overload) — hook-order exception
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseReducer_TwoArg_At_Effect_Position_Throws_HookOrderException()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        ctx.UseEffect(() => { }, "dep");
        ctx.FlushEffects();

        ctx.BeginRender(() => { });
        var ex = Assert.Throws<HookOrderException>(() =>
            ctx.UseReducer<int, string>((s, _) => s + 1, 0));
        Assert.Contains("EffectHookState", ex.Message);
        Assert.Contains("ValueHookState", ex.Message);
        Assert.Contains("UseReducer", ex.Message);
    }

    [Fact]
    public void UseReducer_TwoArg_TypeMismatch_At_Existing_Slot_Throws_HookOrderException()
    {
        // First render: UseState<string>. Second render: UseReducer<int, ...>
        // collides because the generic argument is different.
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        ctx.UseState("hello");

        ctx.BeginRender(() => { });
        var ex = Assert.Throws<HookOrderException>(() =>
            ctx.UseReducer<int, string>((s, _) => s, 0));
        Assert.Contains("ValueHookState", ex.Message);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseMemo<T> — hook-order exception
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseMemo_At_Effect_Position_Throws_HookOrderException()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        ctx.UseEffect(() => { }, "dep");
        ctx.FlushEffects();

        ctx.BeginRender(() => { });
        var ex = Assert.Throws<HookOrderException>(() => ctx.UseMemo(() => 42, "dep"));
        Assert.Contains("EffectHookState", ex.Message);
        Assert.Contains("MemoHookState", ex.Message);
        Assert.Contains("Int32", ex.Message);
    }

    [Fact]
    public void UseMemo_Different_Generic_Type_At_Same_Slot_Throws()
    {
        // First render: UseMemo<int>. Second render: UseMemo<string> at same slot.
        // The framework treats these as different hook kinds because the
        // MemoHookState<T> instances are not assignable to each other.
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        ctx.UseMemo(() => 42, "dep");

        ctx.BeginRender(() => { });
        var ex = Assert.Throws<HookOrderException>(() => ctx.UseMemo(() => "hello", "dep"));
        Assert.Contains("MemoHookState", ex.Message);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseRef<T> — hook-order exception
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseRef_At_Effect_Position_Throws_HookOrderException()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        ctx.UseEffect(() => { }, "dep");
        ctx.FlushEffects();

        ctx.BeginRender(() => { });
        var ex = Assert.Throws<HookOrderException>(() => ctx.UseRef(0));
        // The message uses "Ref<Int32>" template per the source.
        Assert.Contains("ValueHookState", ex.Message);
        Assert.Contains("Ref", ex.Message);
        Assert.Contains("Int32", ex.Message);
    }

    [Fact]
    public void UseRef_Different_Generic_Type_At_Same_Slot_Throws()
    {
        // First render: UseRef<int>(0). Second render: UseRef<string>("").
        // ValueHookState<Ref<int>> ≠ ValueHookState<Ref<string>> → throws.
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        ctx.UseRef(0);

        ctx.BeginRender(() => { });
        var ex = Assert.Throws<HookOrderException>(() => ctx.UseRef(""));
        Assert.Contains("ValueHookState", ex.Message);
        Assert.Contains("Ref", ex.Message);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseStateSetterByIndex (devtools / test-only)
    //
    //  Bug shape: a devtools call mutates the wrong hook cell (wrong type,
    //  out-of-range index). Silent failure is OK by design — the contract
    //  is "no-op when the slot doesn't match" — but a regression that
    //  threw or wrote to the wrong cell would corrupt state.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseStateSetterByIndex_Updates_Matching_Cell_And_Triggers_Rerender()
    {
        var ctx = new RenderContext();
        int rerenders = 0;

        ctx.BeginRender(() => rerenders++);
        var (initial, _) = ctx.UseState(7);
        Assert.Equal(7, initial);

        // Direct cell write via the internal devtools API.
        ctx.UseStateSetterByIndex(0, 99);
        Assert.Equal(1, rerenders);

        // Next render observes the new value.
        ctx.BeginRender(() => rerenders++);
        var (after, _) = ctx.UseState(0); // initial value ignored on re-render
        Assert.Equal(99, after);
    }

    [Fact]
    public void UseStateSetterByIndex_Out_Of_Range_Is_Silent_NoOp()
    {
        // Branch: `index < _hooks.Count` false arm.
        var ctx = new RenderContext();
        int rerenders = 0;
        ctx.BeginRender(() => rerenders++);
        ctx.UseState(7);

        // No exception, no re-render.
        ctx.UseStateSetterByIndex(5, 999);
        Assert.Equal(0, rerenders);
    }

    [Fact]
    public void UseStateSetterByIndex_Type_Mismatch_Is_Silent_NoOp()
    {
        // Branch: `_hooks[index] is ValueHookState<T> hook` false arm.
        // Caller asks for ValueHookState<string> but the slot holds <int>.
        // The setter must not corrupt the cell or trigger a re-render.
        var ctx = new RenderContext();
        int rerenders = 0;
        ctx.BeginRender(() => rerenders++);
        ctx.UseState(7);

        ctx.UseStateSetterByIndex(0, "wrong-type");
        Assert.Equal(0, rerenders);

        // The original int slot is intact.
        ctx.BeginRender(() => rerenders++);
        var (val, _) = ctx.UseState(0);
        Assert.Equal(7, val);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseColorScheme — application-current null path
    //
    //  In headless unit tests Application.Current is null, so the
    //  `theme?` evaluates to null and the switch falls into the
    //  default arm (ElementTheme.Default). Pin the contract that the
    //  hook never throws when there's no Application.Current.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseColorScheme_When_NoApplication_Returns_Default_Without_Throwing()
    {
        // Bug shape: if a regression dereferenced `theme.Value` without a
        // null check, every headless test hosting a Reactor RenderContext
        // would NRE at startup. The contract is "graceful default".
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var scheme = ctx.UseColorScheme();
        // The default-arm output of ColorSchemeContext.FromActualTheme(Default)
        // is a stable concrete value; just assert it doesn't throw and the
        // hook is callable. The exact ColorScheme value is the
        // FromActualTheme(Default) result, which is observable.
        Assert.True(scheme == ColorScheme.Light || scheme == ColorScheme.Dark,
            "UseColorScheme must return a concrete ColorScheme; the default-arm " +
            "path translates ElementTheme.Default into one of Light/Dark, never " +
            "throwing.");
    }

    [Fact]
    public void UseIsDarkTheme_When_NoApplication_Returns_False_Or_True_Without_Throwing()
    {
        // Wrapper over UseColorScheme — just confirms the wrapper doesn't
        // add its own NRE on the null-Application.Current path.
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var result = ctx.UseIsDarkTheme();
        Assert.IsType<bool>(result);
    }
}
