using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.DescriptorOptionalCoverage;

public class TemplatedFlipViewOptionalTests
{
    [Fact]
    public void ControlledEntry_UsesOptionalGateTransitions() =>
        DescriptorOptionalHarness.AssertOptionalGate<int>(
            TemplatedFlipViewDescriptor.Descriptor,
            new TemplatedFlipViewElement<string>(Array.Empty<string>(), static s => s, static (_, _) => new EmptyElement()),
            new TemplatedFlipViewElement<string>(Array.Empty<string>(), static s => s, static (_, _) => new EmptyElement()) { SelectedIndex = 1 },
            new TemplatedFlipViewElement<string>(Array.Empty<string>(), static s => s, static (_, _) => new EmptyElement()) { SelectedIndex = 1 },
            new TemplatedFlipViewElement<string>(Array.Empty<string>(), static s => s, static (_, _) => new EmptyElement()));
}

