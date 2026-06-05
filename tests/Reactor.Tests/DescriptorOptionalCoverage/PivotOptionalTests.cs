using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class PivotOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<int>(
            PivotDescriptor.Descriptor,
            new PivotElement(Array.Empty<PivotItemData>()),
            new PivotElement(Array.Empty<PivotItemData>()) { SelectedIndex = 1 },
            new PivotElement(Array.Empty<PivotItemData>()) { SelectedIndex = 1 },
            new PivotElement(Array.Empty<PivotItemData>()));
}

