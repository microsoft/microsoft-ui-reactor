using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Mount-based fixtures for Phase 6a drag-and-drop (spec 027 Tier 6).
/// DragStarting/Drop event args are not constructible outside the WinUI input
/// pipeline, so these fixtures assert on the declarative surface: CanDrag,
/// AllowDrop, and DraggableWhen's DragStarting-cancel path. End-to-end
/// payload round-tripping lives in the E2E DragDropTests class (Phase 6d).
/// </summary>
internal static class DragDropFixtures
{
    private sealed record CardPayload(string Id);

    internal class OnDragStartAutoSetsCanDrag(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => TextBlock("drag-me")
                .OnDragStart<TextBlockElement, CardPayload>(() => new CardPayload("A1"))
                .Set(tb => tb.Name = "srcCanDrag"));
            await Harness.Render();

            var tb = H.FindControl<TextBlock>(t => t.Name == "srcCanDrag");
            H.Check("DragDrop_OnDragStart_Mounted", tb is not null);
            H.Check("DragDrop_OnDragStart_CanDragTrue", tb is not null && tb.CanDrag);
        }
    }

    internal class OnDropAutoSetsAllowDrop(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => TextBlock("drop-here")
                .OnDrop<TextBlockElement, CardPayload>(_ => { })
                .Set(tb => tb.Name = "tgtAllowDrop"));
            await Harness.Render();

            var tb = H.FindControl<TextBlock>(t => t.Name == "tgtAllowDrop");
            H.Check("DragDrop_OnDrop_Mounted", tb is not null);
            H.Check("DragDrop_OnDrop_AllowDropTrue", tb is not null && tb.AllowDrop);
        }
    }

    internal class RawOnDropAutoSetsAllowDrop(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => TextBlock("drop-here")
                .OnDrop<TextBlockElement>(_ => { })
                .Set(tb => tb.Name = "tgtRawAllowDrop"));
            await Harness.Render();

            var tb = H.FindControl<TextBlock>(t => t.Name == "tgtRawAllowDrop");
            H.Check("DragDrop_RawOnDrop_Mounted", tb is not null);
            H.Check("DragDrop_RawOnDrop_AllowDropTrue", tb is not null && tb.AllowDrop);
        }
    }

    internal class DragEnterHandlerAutoSetsAllowDrop(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => TextBlock("drop-here")
                .OnDragEnter(_ => { })
                .Set(tb => tb.Name = "tgtDragEnter"));
            await Harness.Render();

            var tb = H.FindControl<TextBlock>(t => t.Name == "tgtDragEnter");
            H.Check("DragDrop_OnDragEnter_AllowDropTrue", tb is not null && tb.AllowDrop);
        }
    }

    internal class SourceAndTargetOnSameElement(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => TextBlock("both")
                .OnDragStart<TextBlockElement, CardPayload>(() => new CardPayload("X"))
                .OnDrop<TextBlockElement, CardPayload>(_ => { })
                .Set(tb => tb.Name = "srcAndTgt"));
            await Harness.Render();

            var tb = H.FindControl<TextBlock>(t => t.Name == "srcAndTgt");
            H.Check("DragDrop_SourceAndTarget_Mounted", tb is not null);
            H.Check("DragDrop_SourceAndTarget_CanDrag", tb is not null && tb.CanDrag);
            H.Check("DragDrop_SourceAndTarget_AllowDrop", tb is not null && tb.AllowDrop);
        }
    }

    internal class DraggableWhenWithoutPayloadStillSetsCanDrag(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => TextBlock("gated")
                .DraggableWhen(() => true)
                .Set(tb => tb.Name = "tgtGated"));
            await Harness.Render();

            var tb = H.FindControl<TextBlock>(t => t.Name == "tgtGated");
            H.Check("DragDrop_DraggableWhen_Mounted", tb is not null);
            H.Check("DragDrop_DraggableWhen_CanDragTrue", tb is not null && tb.CanDrag);
        }
    }
}
