using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class TimePickerOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<TimeSpan>(
            TimePickerDescriptor.Descriptor,
            new TimePickerElement(),
            new TimePickerElement(TimeSpan.FromHours(2)),
            new TimePickerElement(TimeSpan.FromHours(2)),
            new TimePickerElement());
}

