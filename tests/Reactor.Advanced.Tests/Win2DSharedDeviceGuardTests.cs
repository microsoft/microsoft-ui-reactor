using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Xunit;

namespace Microsoft.UI.Reactor.Advanced.Tests;

/// <summary>
/// Verifies the internal <c>Win2DSharedDeviceGuard</c> contract that backs the canvas handlers'
/// "UseSharedDevice is fixed at mount" rule. The guard is internal, so it is invoked via reflection
/// (the same approach <see cref="ElementConstructorTests"/> uses for internal element members).
/// </summary>
public sealed class Win2DSharedDeviceGuardTests
{
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Test-only: resolves the internal Win2DSharedDeviceGuard by name from the Reactor.Advanced assembly to exercise its mount-time invariant. The type is kept alive by the canvas handlers that call it; the trimmer can't carry that through Assembly.GetType's string lookup. Reactor.Advanced.Tests is a headless xUnit runner, not NativeAOT-published.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Same site: Assembly.GetType returns a Type without member annotations, so the follow-on GetMethod for the public static EnsureUseSharedDeviceUnchanged is flagged. The method is a public API of a type kept by its handler callers; test-only reflection, not NativeAOT-published.")]
    private static MethodInfo GuardMethod()
    {
        var type = typeof(Win2DCanvasElement).Assembly
            .GetType("Microsoft.UI.Reactor.Advanced.Win2D.Win2DSharedDeviceGuard", throwOnError: true)!;
        return type.GetMethod("EnsureUseSharedDeviceUnchanged", BindingFlags.Public | BindingFlags.Static)!;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void EnsureUseSharedDeviceUnchanged_SameValue_DoesNotThrow(bool oldValue, bool newValue)
    {
        var ex = Record.Exception(() => GuardMethod().Invoke(null, [oldValue, newValue]));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void EnsureUseSharedDeviceUnchanged_ChangedValue_FailsFastInDebug(bool oldValue, bool newValue)
    {
        var ex = Record.Exception(() => GuardMethod().Invoke(null, [oldValue, newValue]));
#if DEBUG
        // Reactor.Advanced built Debug: toggling across renders fails fast with a clear message
        // rather than performing the crash-prone in-place device recreation.
        Assert.IsType<InvalidOperationException>(Assert.IsType<TargetInvocationException>(ex).InnerException);
#else
        // Release: the new value is intentionally ignored (control keeps its mount-time device).
        Assert.Null(ex);
#endif
    }
}
