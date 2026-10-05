using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// The realized <see cref="Microsoft.UI.Xaml.Controls.ContentDialog"/> must point back at
/// its <c>ContentDialog(...)</c> element, so an inspector that picks the dialog's chrome
/// (title, primary/secondary/close buttons) and walks up to the dialog can attribute it.
/// The collapsed placeholder carries the tag too, but it lives in the owner's tree while
/// the dialog is hosted in a popup, so no ancestor walk from a dialog button reaches it.
/// The tagging gate itself is unit-tested in <c>ReactorDiagnosticsFollowupsTests</c>.
/// </summary>
internal static class ContentDialogTagFixtures
{
    internal class DialogIsTagged(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            Microsoft.UI.Xaml.Controls.ContentDialog? dialog = null;
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (title, setTitle) = ctx.UseState("TagDialog");
                    return VStack(
                        TextBlock("anchor"),
                        ContentDialog(title, Button("Retitle", () => setTitle("TagDialog2")), "OK") with { IsOpen = true });
                });

                dialog = await ContentDialogProbe.WaitForOpen(H, "TagDialog");
                H.Check("ContentDialogTag_Opened", dialog is not null);
                if (dialog is null) return;

                var tag = Reconciler.GetElementTag(dialog) as ContentDialogElement;
                H.Check("ContentDialogTag_DialogCarriesItsElement", tag?.Title == "TagDialog");

                // The tag follows re-renders while the dialog stays open, so attribution
                // reads the live element rather than the one that opened the dialog.
                H.Check("ContentDialogTag_RetitleClicked", await ContentDialogProbe.WaitAndClick(dialog, "Retitle"));
                await Harness.WaitFor(() => dialog.Title as string == "TagDialog2");
                H.Check("ContentDialogTag_RetagsOnUpdate",
                    (Reconciler.GetElementTag(dialog) as ContentDialogElement)?.Title == "TagDialog2");

#if REACTOR_SOURCEMAP
                H.Check("ContentDialogTag_GetSourceResolves",
                    ReactorSourceMap.GetSource(dialog)?.FilePath.EndsWith("ContentDialogTagFixtures.cs", StringComparison.Ordinal) == true);
#else
                H.Skip("ContentDialogTag_GetSourceResolves",
                    "assembly built without REACTOR_SOURCEMAP (Release) - no call site is stamped");
#endif
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
                if (dialog is not null)
                {
                    dialog.Hide();
                    await ContentDialogProbe.WaitForNoneOpen(H);
                }
            }
        }
    }
}
