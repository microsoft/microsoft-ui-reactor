using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class RatingControlOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<double>(
            RatingControlDescriptor.Descriptor,
            new RatingControlElement(),
            new RatingControlElement(3.0),
            new RatingControlElement(3.0),
            new RatingControlElement());
}

