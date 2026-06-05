using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class PipsPagerOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<int>(
            PipsPagerDescriptor.Descriptor,
            new PipsPagerElement(5),
            new PipsPagerElement(5) { SelectedPageIndex = 2 },
            new PipsPagerElement(5) { SelectedPageIndex = 2 },
            new PipsPagerElement(5));
}

