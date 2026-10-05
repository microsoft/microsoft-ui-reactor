using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// The caption-derived default AutomationName reported by
/// <see cref="ReactorDiagnostics.GetAppliedProperties"/>. The live read is covered by the
/// <c>Diagnostics_ComponentInspection</c> selftest.
/// </summary>
public class ReactorDiagnosticsDefaultAutomationNameTests
{

    [Fact]
    public void DefaultAutomationName_ReportedWhileLiveNameIsTheCaption()
    {
        var entry = AppliedModifierMap.DescribeDefaultAutomationName(Button("Save"), liveName: "Save");

        Assert.Equal(new AppliedModifier("DefaultAutomationName", "AutomationProperties.Name", "Save"), entry);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Save draft")]
    public void DefaultAutomationName_NotReportedWhenLiveNameDiffers(string? liveName)
    {
        // A different live name means the app wrote it (setter / code-behind); an empty
        // one means nothing was applied.
        Assert.Null(AppliedModifierMap.DescribeDefaultAutomationName(Button("Save"), liveName));
    }

    [Fact]
    public void DefaultAutomationName_ExplicitModifierSuppressesTheDefault()
    {
        var named = Button("Save").AutomationName("Save");

        Assert.Null(AppliedModifierMap.DescribeDefaultAutomationName(named, liveName: "Save"));
    }

    [Fact]
    public void DefaultAutomationName_UsesTheSameTruncationAsTheApplyPath()
    {
        var caption = new string('x', 140);
        var applied = Reconciler.DefaultAutomationNameFromCaption(caption);

        Assert.Equal(100, applied!.Length);
        Assert.Equal(applied, AppliedModifierMap.DescribeDefaultAutomationName(Button(caption), applied)?.Value);
        Assert.Null(AppliedModifierMap.DescribeDefaultAutomationName(Button(caption), caption));
    }

    [Fact]
    public void DefaultAutomationName_UncaptionedOrBlankCaption_NotReported()
    {
        Assert.Null(AppliedModifierMap.DescribeDefaultAutomationName(VStack(), liveName: "anything"));
        Assert.Null(AppliedModifierMap.DescribeDefaultAutomationName(Button("   "), liveName: "   "));
        Assert.Null(Reconciler.DefaultAutomationNameFromCaption("  "));
        Assert.Null(Reconciler.DefaultAutomationNameFromCaption(null));
    }

    [Fact]
    public void DefaultAutomationName_CoversOtherCaptionedControls()
    {
        Assert.Equal("Agree",
            AppliedModifierMap.DescribeDefaultAutomationName(CheckBox(label: "Agree"), liveName: "Agree")?.Value);
        Assert.Equal("hello",
            AppliedModifierMap.DescribeDefaultAutomationName(TextBlock("hello"), liveName: "hello")?.Value);
    }
}
