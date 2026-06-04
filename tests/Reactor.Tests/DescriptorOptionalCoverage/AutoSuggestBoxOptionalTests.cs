using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class AutoSuggestBoxOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<string>(
            AutoSuggestBoxDescriptor.Descriptor,
            new AutoSuggestBoxElement(),
            new AutoSuggestBoxElement("abc"),
            new AutoSuggestBoxElement("abc"),
            new AutoSuggestBoxElement());
}

