using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 050 Phase 2 execution coverage for Optional-aware descriptor prop entries.
/// Runs in the selftest host because it instantiates real WinUI FrameworkElement controls.
/// </summary>
internal static partial class PropEntryOptionalFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            ControlledPropEntry_Mount_Unset_NoWrite();
            ControlledPropEntry_Mount_HasValue_Writes();
            ControlledPropEntry_Update_UnsetToUnset_NoOp();
            ControlledPropEntry_Update_HasValueToUnset_NoOp();
            ControlledPropEntry_Update_UnsetToHasValue_Writes();
            ControlledPropEntry_Update_HasValueChange_Writes_ArmsEcho();
            ControlledPropEntry_Update_HasValueSame_NoOp_NoArm();

            HandCodedControlledPropEntry_Mount_Unset_NoWrite(valueDiffEcho: false);
            HandCodedControlledPropEntry_Mount_Unset_NoWrite(valueDiffEcho: true);
            HandCodedControlledPropEntry_Mount_HasValue_Writes(valueDiffEcho: false);
            HandCodedControlledPropEntry_Mount_HasValue_Writes(valueDiffEcho: true);
            HandCodedControlledPropEntry_Update_UnsetToUnset_NoOp(valueDiffEcho: false);
            HandCodedControlledPropEntry_Update_UnsetToUnset_NoOp(valueDiffEcho: true);
            HandCodedControlledPropEntry_Update_HasValueToUnset_NoOp(valueDiffEcho: false);
            HandCodedControlledPropEntry_Update_HasValueToUnset_NoOp(valueDiffEcho: true);
            HandCodedControlledPropEntry_Update_UnsetToHasValue_Writes(valueDiffEcho: false);
            HandCodedControlledPropEntry_Update_UnsetToHasValue_Writes(valueDiffEcho: true);
            HandCodedControlledPropEntry_Update_HasValueChange_Writes_EchoSuppression(valueDiffEcho: false);
            HandCodedControlledPropEntry_Update_HasValueChange_Writes_EchoSuppression(valueDiffEcho: true);
            HandCodedControlledPropEntry_Update_HasValueSame_NoOp_NoArm(valueDiffEcho: false);
            HandCodedControlledPropEntry_Update_HasValueSame_NoOp_NoArm(valueDiffEcho: true);

            InitialOnlyPropEntry_MountWrites_UpdateNeverWrites();

            OneWayClearValuePropEntry_Mount_HasValue_Writes();
            OneWayClearValuePropEntry_Mount_Unset_ClearsValue();
            OneWayClearValuePropEntry_Update_UnsetToUnset_NoOp();
            OneWayClearValuePropEntry_Update_HasValueToUnset_ClearsValue();
            OneWayClearValuePropEntry_Update_UnsetToHasValue_Writes();
            OneWayClearValuePropEntry_Update_HasValueToHasValue_DiffsValue();

            return Task.CompletedTask;
        }

        private void ControlledPropEntry_Mount_Unset_NoWrite()
        {
            var ctrl = new TestControl { RawValue = 9 };
            var entry = CreateControlledEntry();
            var payload = Reconciler.GetOrCreateControlEventPayload<DescriptorControlledPayload<TestElement, TestControl, int, EventArgs>>(ctrl);
            payload.HasExpectedEcho = true;
            payload.ExpectedEcho = 123;

            entry.Mount(ctrl, new TestElement());

            H.Check("PropEntryOptional_Controlled_Mount_Unset_NoWrite", ctrl.RawValue == 9 && ctrl.WriteCount == 0);
            H.Check("PropEntryOptional_Controlled_Mount_Unset_ClearsStalePayload", !payload.HasExpectedEcho && payload.ExpectedEcho == 0 && payload.EchoComparer is null);
        }

        private void ControlledPropEntry_Mount_HasValue_Writes()
        {
            var ctrl = new TestControl { RawValue = 1 };
            CreateControlledEntry().Mount(ctrl, new TestElement(Value: 5));

            H.Check("PropEntryOptional_Controlled_Mount_HasValue_Writes", ctrl.RawValue == 5 && ctrl.WriteCount == 1);
        }

        private void ControlledPropEntry_Update_UnsetToUnset_NoOp()
        {
            var ctrl = new TestControl { RawValue = 9 };
            CreateControlledEntry().Update(ctrl, new TestElement(), new TestElement());

            H.Check("PropEntryOptional_Controlled_Update_UnsetToUnset_NoOp", ctrl.RawValue == 9 && ctrl.WriteCount == 0);
        }

        private void ControlledPropEntry_Update_HasValueToUnset_NoOp()
        {
            var ctrl = new TestControl { RawValue = 42 };
            CreateControlledEntry().Update(ctrl, new TestElement(Value: 5), new TestElement());

            H.Check("PropEntryOptional_Controlled_Update_HasValueToUnset_NoOp", ctrl.RawValue == 42 && ctrl.WriteCount == 0);
        }

        private void ControlledPropEntry_Update_UnsetToHasValue_Writes()
        {
            var ctrl = new TestControl { RawValue = 42 };
            CreateControlledEntry().Update(ctrl, new TestElement(), new TestElement(Value: 7));

            H.Check("PropEntryOptional_Controlled_Update_UnsetToHasValue_Writes", ctrl.RawValue == 7 && ctrl.WriteCount == 1);
        }

        private void ControlledPropEntry_Update_HasValueChange_Writes_ArmsEcho()
        {
            var ctrl = new TestControl { RawValue = 1 };
            var entry = CreateControlledEntry();
            var el = new TestElement(Value: 1, OnValueChanged: _ => { });
            entry.EnsureSubscribed(default, ctrl, el);

            entry.Update(ctrl, el, new TestElement(Value: 2, OnValueChanged: _ => { }));

            var payload = Reconciler.TryGetControlEventPayload<DescriptorControlledPayload<TestElement, TestControl, int, EventArgs>>(ctrl);
            H.Check("PropEntryOptional_Controlled_Update_HasValueChange_Writes", ctrl.RawValue == 2 && ctrl.WriteCount == 1);
            H.Check("PropEntryOptional_Controlled_Update_HasValueChange_ArmsEcho", payload is { HasExpectedEcho: true, ExpectedEcho: 2 } && ReferenceEquals(payload.EchoComparer, EqualityComparer<int>.Default));
        }

        private void ControlledPropEntry_Update_HasValueSame_NoOp_NoArm()
        {
            var ctrl = new TestControl { RawValue = 2 };
            var entry = CreateControlledEntry();
            var el = new TestElement(Value: 2, OnValueChanged: _ => { });
            entry.EnsureSubscribed(default, ctrl, el);

            entry.Update(ctrl, el, new TestElement(Value: 2, OnValueChanged: _ => { }));

            var payload = Reconciler.TryGetControlEventPayload<DescriptorControlledPayload<TestElement, TestControl, int, EventArgs>>(ctrl);
            H.Check("PropEntryOptional_Controlled_Update_HasValueSame_NoOp_NoArm", ctrl.WriteCount == 0 && payload is { HasExpectedEcho: false });
        }

        private void HandCodedControlledPropEntry_Mount_Unset_NoWrite(bool valueDiffEcho)
        {
            var ctrl = new TestControl { RawValue = 9 };
            if (valueDiffEcho)
                ChangeEchoSuppressor.ArmExpectedEcho(ctrl, rb => Equals(rb, 123));

            CreateHandCodedEntry(valueDiffEcho).Mount(ctrl, new TestElement());

            H.Check($"PropEntryOptional_HandCoded_Mount_Unset_NoWrite_{valueDiffEcho}", ctrl.RawValue == 9 && ctrl.WriteCount == 0);
            if (valueDiffEcho)
                H.Check("PropEntryOptional_HandCoded_Mount_Unset_ClearsExpectedEcho", !ChangeEchoSuppressor.ShouldSuppressEcho(ctrl, 123));
        }

        private void HandCodedControlledPropEntry_Mount_HasValue_Writes(bool valueDiffEcho)
        {
            var ctrl = new TestControl { RawValue = 1 };
            CreateHandCodedEntry(valueDiffEcho).Mount(ctrl, new TestElement(Value: 5));

            H.Check($"PropEntryOptional_HandCoded_Mount_HasValue_Writes_{valueDiffEcho}", ctrl.RawValue == 5 && ctrl.WriteCount == 1);
        }

        private void HandCodedControlledPropEntry_Update_UnsetToUnset_NoOp(bool valueDiffEcho)
        {
            var ctrl = new TestControl { RawValue = 9 };
            CreateHandCodedEntry(valueDiffEcho).Update(ctrl, new TestElement(), new TestElement());

            H.Check($"PropEntryOptional_HandCoded_Update_UnsetToUnset_NoOp_{valueDiffEcho}", ctrl.RawValue == 9 && ctrl.WriteCount == 0);
        }

        private void HandCodedControlledPropEntry_Update_HasValueToUnset_NoOp(bool valueDiffEcho)
        {
            var ctrl = new TestControl { RawValue = 42 };
            CreateHandCodedEntry(valueDiffEcho).Update(ctrl, new TestElement(Value: 5), new TestElement());

            H.Check($"PropEntryOptional_HandCoded_Update_HasValueToUnset_NoOp_{valueDiffEcho}", ctrl.RawValue == 42 && ctrl.WriteCount == 0);
        }

        private void HandCodedControlledPropEntry_Update_UnsetToHasValue_Writes(bool valueDiffEcho)
        {
            var ctrl = new TestControl { RawValue = 42 };
            CreateHandCodedEntry(valueDiffEcho).Update(ctrl, new TestElement(), new TestElement(Value: 7));

            H.Check($"PropEntryOptional_HandCoded_Update_UnsetToHasValue_Writes_{valueDiffEcho}", ctrl.RawValue == 7 && ctrl.WriteCount == 1);
        }

        private void HandCodedControlledPropEntry_Update_HasValueChange_Writes_EchoSuppression(bool valueDiffEcho)
        {
            var ctrl = new TestControl { RawValue = 1 };
            CreateHandCodedEntry(valueDiffEcho).Update(ctrl, new TestElement(Value: 1, OnValueChanged: _ => { }), new TestElement(Value: 2, OnValueChanged: _ => { }));

            H.Check($"PropEntryOptional_HandCoded_Update_HasValueChange_Writes_{valueDiffEcho}", ctrl.RawValue == 2 && ctrl.WriteCount == 1);
            H.Check($"PropEntryOptional_HandCoded_Update_HasValueChange_EchoSuppression_{valueDiffEcho}",
                valueDiffEcho ? ChangeEchoSuppressor.ShouldSuppressEcho(ctrl, 2) : ChangeEchoSuppressor.ShouldSuppress(ctrl));
        }

        private void HandCodedControlledPropEntry_Update_HasValueSame_NoOp_NoArm(bool valueDiffEcho)
        {
            var ctrl = new TestControl { RawValue = 2 };
            CreateHandCodedEntry(valueDiffEcho).Update(ctrl, new TestElement(Value: 2, OnValueChanged: _ => { }), new TestElement(Value: 2, OnValueChanged: _ => { }));

            H.Check($"PropEntryOptional_HandCoded_Update_HasValueSame_NoOp_NoArm_{valueDiffEcho}",
                ctrl.WriteCount == 0 && !ChangeEchoSuppressor.ShouldSuppress(ctrl) && !ChangeEchoSuppressor.ShouldSuppressEcho(ctrl, 2));
        }

        private void InitialOnlyPropEntry_MountWrites_UpdateNeverWrites()
        {
            var descriptor = new ControlDescriptor<TestElement, TestControl>()
                .InitialOnly(e => e.Initial, (c, v) => c.RawValue = v);
            var entry = descriptor.Properties[0];
            var ctrl = new TestControl();

            entry.Mount(ctrl, new TestElement(Initial: 3));
            entry.Update(ctrl, new TestElement(Initial: 3), new TestElement(Initial: 8));

            H.Check("PropEntryOptional_InitialOnly_MountWrites_UpdateNeverWrites", ctrl.RawValue == 3);
        }

        private void OneWayClearValuePropEntry_Mount_HasValue_Writes()
        {
            var ctrl = new TestControl();
            CreateOneWayClearValueEntry().Mount(ctrl, new TestElement(Value: 4));

            H.Check("PropEntryOptional_OneWayClearValue_Mount_HasValue_Writes", ctrl.DpValue == 4 && ctrl.WriteCount == 1);
        }

        private void OneWayClearValuePropEntry_Mount_Unset_ClearsValue()
        {
            var ctrl = new TestControl { DpValue = 8 };
            CreateOneWayClearValueEntry().Mount(ctrl, new TestElement());

            H.Check("PropEntryOptional_OneWayClearValue_Mount_Unset_ClearsValue", ReferenceEquals(DependencyProperty.UnsetValue, ctrl.ReadLocalValue(TestControl.DpValueProperty)) && ctrl.DpValue == 0);
        }

        private void OneWayClearValuePropEntry_Update_UnsetToUnset_NoOp()
        {
            var ctrl = new TestControl { DpValue = 8 };
            ctrl.WriteCount = 0;
            CreateOneWayClearValueEntry().Update(ctrl, new TestElement(), new TestElement());

            H.Check("PropEntryOptional_OneWayClearValue_Update_UnsetToUnset_NoOp", ctrl.DpValue == 8 && ctrl.WriteCount == 0);
        }

        private void OneWayClearValuePropEntry_Update_HasValueToUnset_ClearsValue()
        {
            var ctrl = new TestControl { DpValue = 8 };
            ctrl.WriteCount = 0;
            CreateOneWayClearValueEntry().Update(ctrl, new TestElement(Value: 8), new TestElement());

            H.Check("PropEntryOptional_OneWayClearValue_Update_HasValueToUnset_ClearsValue", ReferenceEquals(DependencyProperty.UnsetValue, ctrl.ReadLocalValue(TestControl.DpValueProperty)) && ctrl.DpValue == 0 && ctrl.WriteCount == 0);
        }

        private void OneWayClearValuePropEntry_Update_UnsetToHasValue_Writes()
        {
            var ctrl = new TestControl();
            CreateOneWayClearValueEntry().Update(ctrl, new TestElement(), new TestElement(Value: 6));

            H.Check("PropEntryOptional_OneWayClearValue_Update_UnsetToHasValue_Writes", ctrl.DpValue == 6 && ctrl.WriteCount == 1);
        }

        private void OneWayClearValuePropEntry_Update_HasValueToHasValue_DiffsValue()
        {
            var ctrl = new TestControl { DpValue = 6 };
            ctrl.WriteCount = 0;
            var entry = CreateOneWayClearValueEntry();

            entry.Update(ctrl, new TestElement(Value: 6), new TestElement(Value: 6));
            var noOp = ctrl.WriteCount == 0;
            entry.Update(ctrl, new TestElement(Value: 6), new TestElement(Value: 7));

            H.Check("PropEntryOptional_OneWayClearValue_Update_HasValueToHasValue_DiffsValue", noOp && ctrl.DpValue == 7 && ctrl.WriteCount == 1);
        }

        private static ControlledPropEntry<TestElement, TestControl, int, EventArgs> CreateControlledEntry() =>
            new(
                get: e => e.Value,
                set: (c, v) => c.WriteRawValue(v),
                subscribe: (fe, h) => ((TestControl)fe).Changed += h,
                unsubscribe: (fe, h) => ((TestControl)fe).Changed -= h,
                getCallback: e => e.OnValueChanged,
                readBack: c => c.RawValue);

        private static HandCodedControlledPropEntry<TestElement, TestControl, TestPayload, int, EventHandler<EventArgs>> CreateHandCodedEntry(bool valueDiffEcho) =>
            new(
                get: e => e.Value,
                set: (c, v) => c.WriteRawValue(v),
                readBack: c => c.RawValue,
                subscribe: (c, h) => c.Changed += h,
                callback: e => e.OnValueChanged,
                trampoline: (_, _) => { },
                slotIsNull: p => p.Changed is null,
                setSlot: (p, h) => p.Changed = h,
                valueDiffEcho: valueDiffEcho);

        private static PropEntry<TestElement, TestControl> CreateOneWayClearValueEntry() =>
            new ControlDescriptor<TestElement, TestControl>()
                .OneWay(e => e.Value, (c, v) => c.WriteDpValue(v), TestControl.DpValueProperty)
                .Properties[0];
    }

    private record TestElement(
        Optional<int> Value = default,
        Action<int>? OnValueChanged = null,
        int Initial = 0) : Element;

    private sealed class TestPayload
    {
        public EventHandler<EventArgs>? Changed;
    }

    private sealed partial class TestControl : FrameworkElement
    {
        public static readonly DependencyProperty DpValueProperty =
            DependencyProperty.Register(
                nameof(DpValue),
                typeof(int),
                typeof(TestControl),
                new PropertyMetadata(0));

        public event EventHandler<EventArgs>? Changed;

        public int RawValue { get; set; }
        public int WriteCount { get; set; }

        public int DpValue
        {
            get => (int)GetValue(DpValueProperty);
            set => SetValue(DpValueProperty, value);
        }

        public void WriteRawValue(int value)
        {
            RawValue = value;
            WriteCount++;
        }

        public void WriteDpValue(int value)
        {
            DpValue = value;
            WriteCount++;
        }

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
