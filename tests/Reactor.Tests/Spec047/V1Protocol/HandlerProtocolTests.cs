using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol;

/// <summary>
/// Spec 047 §4 / §14 Phase 1 (1.6) — IElementHandler protocol shape tests.
///
/// The full Mount→Update→Unmount lifecycle through a real WinUI control
/// requires the STA dispatcher and lives in the AppTests.Host fixture
/// site once Sections 1.11+ port the first control. Here we exercise the
/// pure-API parts: registration, dispatch routing, and the open-generic /
/// duplicate throws (those mirror 1.9's RegisterTypeV1Tests but assert
/// from the RegisterHandler side).
/// </summary>
public class HandlerProtocolTests
{
    public record FakeElement(string Label) : Element;

    public sealed class FakeHandler : IElementHandler<FakeElement, UIElement>
    {
        public UIElement Mount(MountContext ctx, FakeElement element) => null!;
        public void Update(UpdateContext ctx, FakeElement oldEl, FakeElement newEl, UIElement control) { }
    }

    [Fact]
    public void RegisterHandler_Throws_On_Null_Handler()
    {
        var rec = new Reconciler();
        Assert.Throws<ArgumentNullException>(
            () => rec.RegisterHandler<FakeElement, UIElement>(null!));
    }

    [Fact]
    public void RegisterHandler_Allows_One_Handler_Per_Element_Type()
    {
        var rec = new Reconciler();
        rec.RegisterHandler<FakeElement, UIElement>(new FakeHandler());
        // No throw — first registration succeeds.
    }

    [Fact]
    public void RegisterHandler_Throws_On_Duplicate_RegisterHandler()
    {
        var rec = new Reconciler();
        rec.RegisterHandler<FakeElement, UIElement>(new FakeHandler());
        var ex = Assert.Throws<InvalidOperationException>(
            () => rec.RegisterHandler<FakeElement, UIElement>(new FakeHandler()));
        Assert.Contains("FakeElement", ex.Message);
    }

    [Fact(Skip = "Requires WinUI dispatcher to mount a real UIElement; covered in AppTests.Host fixture site (1.11+)")]
    public void V1_Mount_Path_Dispatches_Through_RegisterHandler()
    {
        // TODO(1.11): with the WinUI dispatcher available, mount a FakeElement
        // through Reconciler.Reconcile and assert the registered handler's
        // Mount(ctx, el) ran and returned the expected control.
    }

    [Fact(Skip = "Requires WinUI dispatcher to mount a real UIElement; covered in AppTests.Host fixture site (1.11+)")]
    public void V1_Update_Path_Dispatches_Through_RegisterHandler()
    {
        // TODO(1.11): mount once, then reconcile with a new FakeElement,
        // assert the handler's Update ran with the right old/new pair.
    }
}
