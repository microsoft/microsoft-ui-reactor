using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class ToggleSwitchOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<bool>(
            ToggleSwitchDescriptor.Descriptor,
            new ToggleSwitchElement(),
            new ToggleSwitchElement(true),
            new ToggleSwitchElement(true),
            new ToggleSwitchElement());
}

