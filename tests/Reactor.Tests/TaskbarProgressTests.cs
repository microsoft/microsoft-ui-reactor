using Microsoft.UI.Reactor;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Pure-CLR coverage for the public surface of <see cref="TaskbarProgress"/>.
/// The shell-COM dispatch is exercised in the Phase-9 selftest matrix where a
/// real HWND is available; here we cover validation, defaults, and the
/// state/value invariants.
/// </summary>
public class TaskbarProgressTests
{
    [Fact]
    public void State_DefaultsTo_None()
    {
        // The 0 HWND short-circuits the COM call in TaskbarComSingleton, so
        // construction is safe in unit tests.
        var p = ConstructForTest();
        Assert.Equal(TaskbarProgressState.None, p.State);
    }

    [Fact]
    public void Value_DefaultsTo_Zero()
    {
        var p = ConstructForTest();
        Assert.Equal(0.0, p.Value);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Value_OutOfRange_Throws(double bad)
    {
        var p = ConstructForTest();
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Value = bad);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Value_InRange_Roundtrips(double v)
    {
        var p = ConstructForTest();
        p.Value = v;
        Assert.Equal(v, p.Value);
    }

    [Fact]
    public void SettingValue_FromNone_PromotesStateToNormal()
    {
        // Apps that say "show 30%" without first picking a state expect the
        // shell to render normal-green progress, not stay at None. (spec §11.1)
        var p = ConstructForTest();
        p.Value = 0.3;
        Assert.Equal(TaskbarProgressState.Normal, p.State);
    }

    [Fact]
    public void Clear_ResetsValueAndState()
    {
        var p = ConstructForTest();
        p.Value = 0.7;
        p.State = TaskbarProgressState.Paused;
        p.Clear();
        Assert.Equal(0.0, p.Value);
        Assert.Equal(TaskbarProgressState.None, p.State);
    }

    [Fact]
    public void State_AssignmentRoundtrips_AcrossAllValues()
    {
        var p = ConstructForTest();
        foreach (var s in new[]
        {
            TaskbarProgressState.None,
            TaskbarProgressState.Indeterminate,
            TaskbarProgressState.Normal,
            TaskbarProgressState.Paused,
            TaskbarProgressState.Error,
        })
        {
            p.State = s;
            Assert.Equal(s, p.State);
        }
    }

    private static TaskbarProgress ConstructForTest()
    {
        // We can't construct directly (the ctor is internal). Use the
        // ReactorWindow.Progress accessor via reflection-free invariants:
        // construction goes through the sealed internal ctor which we expose
        // via a helper file in the production assembly's InternalsVisibleTo
        // entry — same pattern as other window-tests. For the base shape
        // covered here, instantiate via the test-only factory.
        return TaskbarProgressTestFactory.New();
    }
}

/// <summary>
/// Lives next to the production type for InternalsVisibleTo discovery; calls
/// the internal constructor with a sentinel HWND of 0 so the shell COM init
/// short-circuits inside the singleton.
/// </summary>
internal static class TaskbarProgressTestFactory
{
    public static TaskbarProgress New()
    {
        // hwnd=0 hits TaskbarComSingleton.TryGet at runtime; init either
        // succeeds (no-op when the HWND is 0) or returns null. Either way the
        // setter logic is exercised without depending on the shell.
        return (TaskbarProgress)Activator.CreateInstance(
            typeof(TaskbarProgress),
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance,
            binder: null,
            args: new object[] { (nint)0, (Func<bool>)(() => false) },
            culture: null)!;
    }
}
