using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class DatePickerOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<DateTimeOffset>(
            DatePickerDescriptor.Descriptor,
            new DatePickerElement(),
            new DatePickerElement(new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            new DatePickerElement(new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            new DatePickerElement());
}

