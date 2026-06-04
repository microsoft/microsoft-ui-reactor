using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class ListViewOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<int>(
            ListViewDescriptor.Descriptor,
            new ListViewElement(Array.Empty<Element>()),
            new ListViewElement(Array.Empty<Element>()) { SelectedIndex = 2 },
            new ListViewElement(Array.Empty<Element>()) { SelectedIndex = 2 },
            new ListViewElement(Array.Empty<Element>()));
}

