using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class GridViewOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<int>(
            GridViewDescriptor.Descriptor,
            new GridViewElement(Array.Empty<Element>()),
            new GridViewElement(Array.Empty<Element>()) { SelectedIndex = 2 },
            new GridViewElement(Array.Empty<Element>()) { SelectedIndex = 2 },
            new GridViewElement(Array.Empty<Element>()));
}

