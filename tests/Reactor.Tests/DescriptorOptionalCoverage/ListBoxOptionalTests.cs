using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class ListBoxOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<int>(
            ListBoxDescriptor.Descriptor,
            new ListBoxElement(Array.Empty<string>()),
            new ListBoxElement(Array.Empty<string>()) { SelectedIndex = 2 },
            new ListBoxElement(Array.Empty<string>()) { SelectedIndex = 2 },
            new ListBoxElement(Array.Empty<string>()));
}

