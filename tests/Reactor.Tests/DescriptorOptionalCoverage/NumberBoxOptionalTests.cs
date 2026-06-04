using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class NumberBoxOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<double>(
            NumberBoxDescriptor.Descriptor,
            new NumberBoxElement(),
            new NumberBoxElement(5.0),
            new NumberBoxElement(5.0),
            new NumberBoxElement());
}

