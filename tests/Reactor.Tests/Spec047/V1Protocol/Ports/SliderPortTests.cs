using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Handlers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol.Ports;

/// <summary>
/// Spec 047 §14 Phase 1 (1.12) — Slider port tests.
/// Behavior-on-real-control tests live in AppTests.Host (1.12 fixture).
/// </summary>
public class SliderPortTests
{
    [Fact]
    public void Flag_On_Registers_SliderHandler_Automatically()
    {
        var rec = new Reconciler();
        Assert.Throws<InvalidOperationException>(
            () => rec.RegisterHandler<SliderElement, Microsoft.UI.Xaml.Controls.Slider>(
                new SliderHandler()));
    }

    [Fact(Skip = "Requires WinUI dispatcher; covered in AppTests.Host SelfTest/Fixtures/Spec047V1ProtocolFixtures.cs (1.12)")]
    public void Coercion_Tolerance_Suppresses_Min_Max_Coerced_Echoes()
    {
        // TODO(AppTests.Host): mount Slider with Value=50, Min=0, Max=100.
        // Reconcile with Min=60 → Value coerced from 50 to 60 → SuppressedToken
        // drops the echo, OnValueChanged does NOT fire.
        // Repeat for Max coercion.
    }

    [Fact(Skip = "Requires WinUI dispatcher; covered in AppTests.Host SelfTest/Fixtures/Spec047V1ProtocolFixtures.cs (1.12)")]
    public void Programmatic_Value_Write_Does_Not_Re_Fire_Callback()
    {
        // TODO(AppTests.Host): drag thumb → OnValueChanged fires once.
        // Reconcile with new Value → no extra fire.
    }
}
