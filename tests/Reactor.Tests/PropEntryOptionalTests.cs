using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Headless surface tests for the Optional-aware PropEntry descriptor shapes.
/// Execution-style Mount/Update coverage lives in
/// tests/Reactor.AppTests.Host/SelfTest/Fixtures/PropEntryOptionalFixture.cs,
/// because Reactor.Tests cannot instantiate real WinUI controls headlessly.
/// </summary>
public class PropEntryOptionalTests
{
    [Fact]
    public void Controlled_Builder_Adds_OptionalControlledEntry()
    {
        var descriptor = new ControlDescriptor<TestElement, WinUI.Button>()
            .Controlled<bool, EventArgs>(
                get: e => e.Value,
                set: (c, v) => c.IsEnabled = v,
                subscribe: (_, _) => { },
                unsubscribe: (_, _) => { },
                callback: e => e.OnValueChanged,
                readBack: c => c.IsEnabled);

        var entry = Assert.Single(descriptor.Properties);
        Assert.IsType<ControlledPropEntry<TestElement, WinUI.Button, bool, EventArgs>>(entry);
    }

    [Fact]
    public void HandCodedControlled_Builder_Adds_OptionalHandCodedEntry_WithValueDiffEchoParameter()
    {
        var descriptor = new ControlDescriptor<TestElement, WinUI.Button>()
            .HandCodedControlled<FakePayload, bool, EventHandler<EventArgs>>(
                get: e => e.Value,
                set: (c, v) => c.IsEnabled = v,
                readBack: c => c.IsEnabled,
                subscribe: (_, _) => { },
                callback: e => e.OnValueChanged,
                trampoline: (_, _) => { },
                slotIsNull: p => p.Changed is null,
                setSlot: (p, h) => p.Changed = h,
                valueDiffEcho: true);

        var entry = Assert.Single(descriptor.Properties);
        Assert.IsType<HandCodedControlledPropEntry<TestElement, WinUI.Button, FakePayload, bool, EventHandler<EventArgs>>>(entry);
    }

    [Fact]
    public void InitialOnly_Builder_Adds_InitialOnlyEntry()
    {
        var descriptor = new ControlDescriptor<TestElement, WinUI.Button>()
            .InitialOnly(e => e.Initial, (c, v) => c.Content = v);

        var entry = Assert.Single(descriptor.Properties);
        Assert.IsType<InitialOnlyPropEntry<TestElement, WinUI.Button, string>>(entry);
    }

    [Fact]
    public void OneWay_WithDp_RoutesToClearValueEntry()
    {
        var descriptor = new ControlDescriptor<TestElement, WinUI.Button>()
            .OneWay(e => e.Opacity, (c, v) => c.Opacity = v, UIElement.OpacityProperty);

        var entry = Assert.Single(descriptor.Properties);
        Assert.IsType<OneWayClearValuePropEntry<TestElement, WinUI.Button, double>>(entry);
    }

    [Fact]
    public void OneWay_WithoutDp_KeepsPlainOneWayEntry()
    {
        var descriptor = new ControlDescriptor<TestElement, WinUI.Button>()
            .OneWay(e => e.Opacity.GetValueOrDefault(1.0), (c, v) => c.Opacity = v);

        var entry = Assert.Single(descriptor.Properties);
        Assert.IsType<OneWayPropEntry<TestElement, WinUI.Button, double>>(entry);
    }

    [Fact]
    public void OptionalGetter_DefaultElement_ReturnsUnset()
    {
        var element = new TestElement();
        Optional<bool> value = element.Value;
        Optional<double> opacity = element.Opacity;

        Assert.False(value.HasValue);
        Assert.Equal(Optional<bool>.Unset, value);
        Assert.False(opacity.HasValue);
        Assert.Equal(Optional<double>.Unset, opacity);
    }

    [Fact]
    public void OptionalGetter_ExplicitValue_ReturnsHasValue()
    {
        var element = new TestElement(Value: true, Opacity: 0.5);

        Assert.True(element.Value.HasValue);
        Assert.True(element.Value.Value);
        Assert.True(element.Opacity.HasValue);
        Assert.Equal(0.5, element.Opacity.Value);
    }

    private record TestElement(
        Optional<bool> Value = default,
        Optional<double> Opacity = default,
        Action<bool>? OnValueChanged = null,
        string Initial = "seed") : Element;

    private sealed class FakePayload
    {
        public EventHandler<EventArgs>? Changed;
    }
}
