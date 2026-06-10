using System.Linq;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 057 task 1.8 headless surface-parity coverage. Live RefNode slot parity
/// and live TeachingTip.Target writes are covered by the RefNode_SurfaceParity
/// self-host fixture because both require a XAML host.
/// </summary>
public class SurfaceParityTests
{
    [Fact]
    public void TeachingTip_Target_Record_Fluent_And_Factory_Carry_Same_Cell()
    {
        var target = TypedElementRef.Create<FrameworkElement>();
        ElementRef targetCell = target;

        var record = new TeachingTipElement("record") { Target = target };
        var fluent = TeachingTip("fluent").Target(target);
        var factory = TeachingTip("factory", target: target);

        Assert.Same(targetCell, record.Target);
        Assert.Same(targetCell, fluent.Target);
        Assert.Same(targetCell, factory.Target);
        Assert.Same(record.Target, fluent.Target);
        Assert.Same(fluent.Target, factory.Target);
    }

    [Fact]
    public void TeachingTip_Descriptor_Declares_Exactly_One_Target_Reference_Entry()
    {
        var referenceEntries = TeachingTipDescriptor.Descriptor.Properties
            .OfType<UntypedReferencePropEntry<TeachingTipElement, WinUI.TeachingTip>>()
            .ToArray();

        Assert.Single(referenceEntries);
    }
}
