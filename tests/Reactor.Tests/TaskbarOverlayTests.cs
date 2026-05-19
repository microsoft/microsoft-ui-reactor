using System;
using System.Reflection;
using Microsoft.UI.Reactor;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 036 §11.2 — <see cref="TaskbarOverlay"/> setter & dispose-guard
/// pins. Full Apply() exercises ITaskbarList3 COM which requires a real
/// shell context; here we test:
///   (a) constructor + getter/setter round-trips;
///   (b) the `_isDisposed()` short-circuit in Apply (set Icon / Description
///       after dispose must be silent no-op, not throw);
///   (c) the LoadIconFor static helper's null / IsResource arms.
///
/// xUnit runs without a captured UIDispatcher, so
/// ThreadAffinity.ThrowIfNotOnUIThread is a no-op (its docs explicitly
/// permit unit-test fixtures). The COM singleton `TaskbarComSingleton.TryGet`
/// returns null in this context — that's the second branch Apply takes.
/// </summary>
public class TaskbarOverlayTests
{
    // ══════════════════════════════════════════════════════════════
    //  Construction + getter/setter round-trips
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Construct_With_Disposed_Predicate_Apply_Returns_Early()
    {
        // isDisposed returning true short-circuits Apply before any COM call.
        // The Icon setter still stores the value, so the getter round-trip
        // works. Bug shape: a regression that called the COM TryGet before
        // checking the dispose flag would NRE when the host is mid-teardown.
        var overlay = new TaskbarOverlay(IntPtr.Zero, () => true);
        var icon = WindowIcon.FromResource("ms-appx:///Assets/x.ico");
        overlay.Icon = icon;
        Assert.Same(icon, overlay.Icon);
    }

    [Fact]
    public void Icon_Setter_Accepts_Null_To_Clear()
    {
        var overlay = new TaskbarOverlay(IntPtr.Zero, () => true);
        overlay.Icon = WindowIcon.FromResource("ms-appx:///Assets/x.ico");
        overlay.Icon = null;
        Assert.Null(overlay.Icon);
    }

    [Fact]
    public void AccessibleDescription_Roundtrips()
    {
        // The pszDescription parameter on SetOverlayIcon — assistive tech
        // can't announce the overlay without it (spec §0.6).
        var overlay = new TaskbarOverlay(IntPtr.Zero, () => true);
        overlay.AccessibleDescription = "New message indicator";
        Assert.Equal("New message indicator", overlay.AccessibleDescription);
    }

    [Fact]
    public void AccessibleDescription_Accepts_Null_To_Clear()
    {
        var overlay = new TaskbarOverlay(IntPtr.Zero, () => true);
        overlay.AccessibleDescription = "first";
        overlay.AccessibleDescription = null;
        Assert.Null(overlay.AccessibleDescription);
    }

    [Fact]
    public void Setters_After_Disposed_Are_Silent_NoOp_Do_Not_Throw()
    {
        // The dispose-flag guard inside Apply protects against use-after-
        // teardown. Pin: a regression that removed the guard would
        // ObjectDisposedException from the COM singleton when called past
        // window close.
        var overlay = new TaskbarOverlay(IntPtr.Zero, () => true);
        // Must not throw.
        overlay.Icon = WindowIcon.FromResource("ms-appx:///x.ico");
        overlay.AccessibleDescription = "anything";
        overlay.Icon = null;
        overlay.AccessibleDescription = null;
    }

    [Fact]
    public void Setters_When_Live_But_No_COM_Singleton_Are_Silent_NoOp()
    {
        // The second early-return in Apply: `TaskbarComSingleton.TryGet() is null`.
        // In xUnit the singleton is uninitialised so TryGet returns null,
        // and Apply must return without calling SetOverlayIcon.
        var overlay = new TaskbarOverlay(IntPtr.Zero, () => false);
        // Must not throw — no COM init in test context.
        overlay.Icon = WindowIcon.FromResource("ms-appx:///x.ico");
        overlay.AccessibleDescription = "live but no COM";
    }

    // ══════════════════════════════════════════════════════════════
    //  LoadIconFor — private static helper. Reached via reflection.
    // ══════════════════════════════════════════════════════════════

    private static IntPtr InvokeLoadIconFor(WindowIcon? icon)
    {
        var mi = typeof(TaskbarOverlay).GetMethod(
            "LoadIconFor",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("LoadIconFor not found");
        return (IntPtr)mi.Invoke(null, new object?[] { icon })!;
    }

    [Fact]
    public void LoadIconFor_Null_Returns_Zero()
    {
        // Bug shape: a regression that NRE'd on null would crash every
        // overlay clear (Icon = null path).
        Assert.Equal(IntPtr.Zero, InvokeLoadIconFor(null));
    }

    [Fact]
    public void LoadIconFor_Resource_Icon_Returns_Zero()
    {
        // ms-appx:/// resources are WinRT-only — the shell overlay surface
        // needs an HICON, so the renderer routes back through Apply with 0
        // and the resource path is silently no-op on the overlay surface.
        // Pin the documented "skip resource path" contract.
        var icon = WindowIcon.FromResource("ms-appx:///Assets/x.ico");
        Assert.Equal(IntPtr.Zero, InvokeLoadIconFor(icon));
    }

    [Fact]
    public void LoadIconFor_Missing_File_Path_Returns_Zero_Without_Throwing()
    {
        // LoadImageW returns 0 for a non-existent file; the catch arm also
        // swallows any thrown exception. Either way, no overlay rendered,
        // no host crash. Bug shape: an unhandled exception would crash the
        // entire host on a missing-asset deployment.
        var icon = WindowIcon.FromPath(@"C:\definitely_not_a_real_path\bogus.ico");
        // Must not throw and must return a sentinel (0 or whatever LoadImageW returns for missing).
        var result = InvokeLoadIconFor(icon);
        // LoadImageW returns 0 for missing files when LR_LOADFROMFILE is set.
        Assert.Equal(IntPtr.Zero, result);
    }
}
