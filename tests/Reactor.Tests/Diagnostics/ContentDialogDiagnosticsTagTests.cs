using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// The gate that decides whether the realized ContentDialog is tagged with its element. The
/// live tag is covered by the <c>Diagnostics_ContentDialogTag</c> selftest. In the
/// SourceMapGlobals collection because these cases flip <see cref="ReactorSourceMap.Enabled"/>.
/// </summary>
[Collection("SourceMapGlobals")]
public class ContentDialogDiagnosticsTagTests
{
    private static readonly SourceLocation Site = new(@"C:\app\Dialogs.cs", 12);


    [Fact]
    public void ShouldTagDialog_StampedElement_TagsEvenWithSourceMapOff()
    {
        var previous = ReactorSourceMap.Enabled;
        try
        {
            ReactorSourceMap.Enabled = false;
            var stamped = ContentDialog("Delete?", TextBlock("Sure?")) with { CallSite = Site };
            Assert.True(OverlayLifecycle.ShouldTagDialog(stamped));
        }
        finally { ReactorSourceMap.Enabled = previous; }
    }

    [Fact]
    public void ShouldTagDialog_FollowsSourceMapFlag_ForUnstampedElement()
    {
        var previous = ReactorSourceMap.Enabled;
        try
        {
            var plain = ContentDialog("Delete?", TextBlock("Sure?")) with { CallSite = null };

            ReactorSourceMap.Enabled = false;
            Assert.False(OverlayLifecycle.ShouldTagDialog(plain));

            ReactorSourceMap.Enabled = true;
            Assert.True(OverlayLifecycle.ShouldTagDialog(plain));
        }
        finally { ReactorSourceMap.Enabled = previous; }
    }
}
