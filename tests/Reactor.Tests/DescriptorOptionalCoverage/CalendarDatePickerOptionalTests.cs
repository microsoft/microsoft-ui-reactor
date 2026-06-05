using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class CalendarDatePickerOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<DateTimeOffset?>(
            CalendarDatePickerDescriptor.Descriptor,
            new CalendarDatePickerElement(),
            new CalendarDatePickerElement(new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            new CalendarDatePickerElement(new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            new CalendarDatePickerElement());
}

