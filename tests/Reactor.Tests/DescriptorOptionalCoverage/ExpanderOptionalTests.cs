using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class ExpanderOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<bool>(
            ExpanderDescriptor.Descriptor,
            new ExpanderElement("h", new EmptyElement()),
            new ExpanderElement("h", new EmptyElement()) { IsExpanded = true },
            new ExpanderElement("h", new EmptyElement()) { IsExpanded = true },
            new ExpanderElement("h", new EmptyElement()));
}

