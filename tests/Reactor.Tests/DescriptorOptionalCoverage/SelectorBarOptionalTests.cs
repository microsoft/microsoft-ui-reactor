using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class SelectorBarOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<int>(
            SelectorBarDescriptor.Descriptor,
            new SelectorBarElement(Array.Empty<SelectorBarItemData>()),
            new SelectorBarElement(Array.Empty<SelectorBarItemData>()) { SelectedIndex = 1 },
            new SelectorBarElement(Array.Empty<SelectorBarItemData>()) { SelectedIndex = 1 },
            new SelectorBarElement(Array.Empty<SelectorBarItemData>()));
}

