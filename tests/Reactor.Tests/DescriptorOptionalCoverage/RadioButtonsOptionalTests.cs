using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class RadioButtonsOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<int>(
            RadioButtonsDescriptor.Descriptor,
            new RadioButtonsElement(Array.Empty<string>()),
            new RadioButtonsElement(Array.Empty<string>(), 1),
            new RadioButtonsElement(Array.Empty<string>(), 1),
            new RadioButtonsElement(Array.Empty<string>()));
}

