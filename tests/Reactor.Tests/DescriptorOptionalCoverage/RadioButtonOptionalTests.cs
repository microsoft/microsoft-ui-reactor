using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class RadioButtonOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<bool>(
            RadioButtonDescriptor.Descriptor,
            new RadioButtonElement("r"),
            new RadioButtonElement("r", true),
            new RadioButtonElement("r", true),
            new RadioButtonElement("r"));
}

