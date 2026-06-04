using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class ComboBoxOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<int>(
            ComboBoxDescriptor.Descriptor,
            new ComboBoxElement(Array.Empty<string>()),
            new ComboBoxElement(Array.Empty<string>(), 2),
            new ComboBoxElement(Array.Empty<string>(), 2),
            new ComboBoxElement(Array.Empty<string>()));
}

