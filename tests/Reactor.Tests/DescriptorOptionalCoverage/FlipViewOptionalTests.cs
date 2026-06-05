using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class FlipViewOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<int>(
            FlipViewDescriptor.Descriptor,
            new FlipViewElement(Array.Empty<Element>()),
            new FlipViewElement(Array.Empty<Element>()) { SelectedIndex = 2 },
            new FlipViewElement(Array.Empty<Element>()) { SelectedIndex = 2 },
            new FlipViewElement(Array.Empty<Element>()));
}

