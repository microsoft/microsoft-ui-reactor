using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using WinUI = Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #949 — headless surface tests for the descriptor teardown seam
/// (<c>ControlDescriptor.OnUnmount</c> / <c>.WithUnmount(...)</c>, forwarded by
/// <c>DescriptorHandler.Unmount</c>).
///
/// <para>Before this seam existed, <c>DescriptorHandler</c> inherited
/// <c>IElementHandler.Unmount</c>'s default no-op, so a descriptor had no way to invalidate
/// control-scoped state it created at mount. The live end-to-end proof is the TeachingTip
/// selftest (<c>MountOpen_TeachingTip_UnmountBeforeLoadCancelsPendingOpen</c>); this file pins
/// the plumbing itself, which needs no WinUI control — <c>WinUI.Button</c> is used only as a
/// type argument, never instantiated, and the forwarding path never dereferences the control.
/// (Headless tests cannot construct any <c>Microsoft.UI.Xaml</c> object.)</para>
/// </summary>
public class DescriptorUnmountHookTests
{
    private sealed record UnmountProbeElement : Element;

    private static ControlDescriptor<UnmountProbeElement, WinUI.Button> Descriptor() => new();

    [Fact]
    public void WithUnmount_StoresTheCallback_AndReturnsTheSameDescriptor()
    {
        var descriptor = Descriptor();
        Assert.Null(descriptor.OnUnmount);

        var chained = descriptor.WithUnmount(static (in UnmountContext _, WinUI.Button _) => { });

        Assert.Same(descriptor, chained);
        Assert.NotNull(descriptor.OnUnmount);
    }

    [Fact]
    public void WithUnmount_Rejects_Null()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => Descriptor().WithUnmount(null!));

        // Pin the parameter name, not just the exception type: an ArgumentNullException thrown
        // from somewhere else in the builder would otherwise satisfy the assertion.
        Assert.Equal("callback", ex.ParamName);
    }

    /// <summary>
    /// The behavioural half. Two descriptors that differ ONLY by the presence of the hook must
    /// produce different observable results from the same <c>Unmount</c> call — so this fails
    /// both if the forward is dropped and if it fires unconditionally.
    /// </summary>
    [Fact]
    public void DescriptorHandler_Unmount_Invokes_The_Hook_Exactly_Once_And_Only_When_Declared()
    {
        var reconciler = new Reconciler();
        var ctx = new UnmountContext(reconciler);

        int withHookCalls = 0;
        var withHook = new DescriptorHandler<UnmountProbeElement, WinUI.Button>(
            Descriptor().WithUnmount((in UnmountContext _, WinUI.Button _) => withHookCalls++));

        var withoutHook = new DescriptorHandler<UnmountProbeElement, WinUI.Button>(Descriptor());

        withHook.Unmount(ctx, null!);
        withoutHook.Unmount(ctx, null!);

        Assert.Equal(1, withHookCalls);
    }
}
