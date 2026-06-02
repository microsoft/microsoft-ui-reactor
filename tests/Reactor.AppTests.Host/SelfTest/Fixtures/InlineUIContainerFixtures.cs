using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #480 — selftest fixtures for <c>InlineUIContainer</c> support
/// inside <see cref="RichTextBlock(RichTextParagraph[])"/>. Exercises both
/// Route A (Reactor element child reconciled by the engine) and Route B
/// (imperative native factory).
/// </summary>
internal static class InlineUIContainerFixtures
{
    // ════════════════════════════════════════════════════════════════════
    //  Route A — Reactor element embedded inline; click through the
    //  embedded Button drives a parent state change and re-render.
    // ════════════════════════════════════════════════════════════════════
    internal class InlineUI_RouteA_ReactorChild(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (count, setCount) = ctx.UseState(0);
                return VStack(
                    RichTextBlock(new[]
                    {
                        Paragraph(
                            Run("Count is "),
                            InlineUI(Button($"Inc{count}", () => setCount(count + 1))),
                            Run(" — keep clicking"))
                    })
                );
            });

            await Harness.Render();

            // The RichTextBlock should host a real InlineUIContainer whose
            // Child is the Reactor-mounted Button.
            var rtb = H.FindControl<Microsoft.UI.Xaml.Controls.RichTextBlock>(_ => true);
            H.Check("InlineUI_RouteA_RTBMounted", rtb is not null);

            var iuc = FindInlineUIContainer(rtb!);
            H.Check("InlineUI_RouteA_InlineUIContainerPresent", iuc is not null);
            H.Check("InlineUI_RouteA_ChildIsButton",
                iuc?.Child is Microsoft.UI.Xaml.Controls.Button);

            // Click the embedded button — hook state should advance and the
            // RichTextBlock should rebuild with the new label.
            H.ClickButton("Inc0");
            await Harness.Render();

            H.Check("InlineUI_RouteA_StateAdvanced",
                H.FindButton("Inc1") is not null);

            // Old label is gone — could be either rebuild or in-place label
            // change. After issue #480's incremental update path the Button
            // identity is preserved AND its Content updates from "Inc0" to
            // "Inc1"; either way the old caption is no longer findable.
            H.Check("InlineUI_RouteA_OldLabelGone",
                H.FindButton("Inc0") is null);

            // Drive another click via the (same, incrementally-updated)
            // embedded button.
            H.ClickButton("Inc1");
            await Harness.Render();
            H.Check("InlineUI_RouteA_SecondClick",
                H.FindButton("Inc2") is not null);
        }

        private static Microsoft.UI.Xaml.Documents.InlineUIContainer? FindInlineUIContainer(
            Microsoft.UI.Xaml.Controls.RichTextBlock rtb)
        {
            foreach (var block in rtb.Blocks)
            {
                if (block is not Microsoft.UI.Xaml.Documents.Paragraph p) continue;
                foreach (var inline in p.Inlines)
                    if (inline is Microsoft.UI.Xaml.Documents.InlineUIContainer iuc) return iuc;
            }
            return null;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Route B — imperative factory inline; ProgressRing is native-only.
    // ════════════════════════════════════════════════════════════════════
    internal class InlineUI_RouteB_NativeFactory(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdRouteB", () => set(1)),
                    RichTextBlock(new[]
                    {
                        Paragraph(
                            Run("Loading "),
                            InlineUI(() => new Microsoft.UI.Xaml.Controls.ProgressRing
                            {
                                IsActive = true,
                                Width = 16,
                                Height = 16,
                            }),
                            Run(phase == 0 ? " (initial)" : " (updated)"))
                    })
                );
            });

            await Harness.Render();

            var rtb = H.FindControl<Microsoft.UI.Xaml.Controls.RichTextBlock>(_ => true);
            H.Check("InlineUI_RouteB_RTBMounted", rtb is not null);

            // Native ProgressRing should be present inside the rich text flow.
            var ring = H.FindControl<Microsoft.UI.Xaml.Controls.ProgressRing>(_ => true);
            H.Check("InlineUI_RouteB_NativeRingMounted", ring is not null);
            H.Check("InlineUI_RouteB_NativeRingActive", ring?.IsActive == true);

            // Toggle phase — RichTextBlock rebuilds; factory is invoked again
            // to produce a fresh native ProgressRing.
            H.ClickButton("UpdRouteB");
            await Harness.Render();

            // A single ring should still be present (the old one was dropped,
            // a new one was created).
            var rings = H.FindAllControls<Microsoft.UI.Xaml.Controls.ProgressRing>(_ => true);
            H.Check("InlineUI_RouteB_RingCountAfterRebuild", rings.Count == 1);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Unmount — RichTextBlock teardown should NOT leak Route A children;
    //  re-mount a fresh RichTextBlock and verify no stale embedded button
    //  responds to clicks.
    // ════════════════════════════════════════════════════════════════════
    internal class InlineUI_Unmount_TearsDownReactorChild(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            int clickCount = 0;
            host.Mount(ctx =>
            {
                var (showBlock, setShow) = ctx.UseState(true);
                return VStack(
                    Button("ToggleBlock", () => setShow(!showBlock)),
                    showBlock
                        ? (Element)RichTextBlock(new[]
                            {
                                Paragraph(
                                    Run("X "),
                                    InlineUI(Button("Embed", () => clickCount++)))
                            })
                        : TextBlock("(no block)")
                );
            });

            await Harness.Render();

            H.Check("InlineUI_Unmount_EmbedPresent",
                H.FindButton("Embed") is not null);

            // Click once — sanity check.
            H.ClickButton("Embed");
            await Harness.Render();
            H.Check("InlineUI_Unmount_FirstClickCounted", clickCount == 1);

            // Hide the RichTextBlock. The embedded Button (Route A child)
            // should be unmounted along with the block.
            H.ClickButton("ToggleBlock");
            await Harness.Render();

            H.Check("InlineUI_Unmount_RTBGone",
                H.FindControl<Microsoft.UI.Xaml.Controls.RichTextBlock>(_ => true) is null);
            H.Check("InlineUI_Unmount_EmbedGone",
                H.FindButton("Embed") is null);

            // Bring the block back — fresh embed button, fresh subtree.
            H.ClickButton("ToggleBlock");
            await Harness.Render();

            H.Check("InlineUI_Unmount_EmbedFreshAfterRemount",
                H.FindButton("Embed") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Issue #480 follow-up — incremental RichTextBlock update path.
    //  After re-render the embedded Reactor child should retain its WinUI
    //  control identity (no Mount/Unmount churn) so state like Slider drag
    //  and focus survive, AND the reconcile-highlight overlay only flashes
    //  the changed run rather than the entire block.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that re-rendering with a NEW paragraph array containing the
    /// same shape preserves the embedded Reactor child's underlying
    /// FrameworkElement instance (UpdateChild path, not Mount).
    /// </summary>
    internal class InlineUI_IncrementalUpdate_PreservesChildIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(1);
                return VStack(
                    Button("Bump", () => setN(n + 1)),
                    RichTextBlock(new[]
                    {
                        Paragraph(
                            Run($"Value before: {n}"),
                            InlineUI(Button("Inline", () => { })),
                            Run($" — value after: {n}"))
                    })
                );
            });

            await Harness.Render();

            var firstInline = H.FindButton("Inline");
            H.Check("InlineUI_Incr_InitialMount", firstInline is not null);

            // Bump the parent state — paragraphs array is regenerated, but
            // the inline button element shape is identical (same type, same
            // caption). Incremental update should reconcile the embedded
            // Button via ReconcileV1Child → same FrameworkElement instance.
            H.ClickButton("Bump");
            await Harness.Render();

            var secondInline = H.FindButton("Inline");
            H.Check("InlineUI_Incr_PreservedAfterRerender",
                secondInline is not null
                && ReferenceEquals(firstInline, secondInline));

            // Bump several more times to confirm identity holds across
            // multiple re-renders.
            H.ClickButton("Bump");
            await Harness.Render();
            H.ClickButton("Bump");
            await Harness.Render();
            var fourthInline = H.FindButton("Inline");
            H.Check("InlineUI_Incr_PreservedAcrossMultipleRerenders",
                ReferenceEquals(firstInline, fourthInline));
        }
    }

    /// <summary>
    /// Verifies that a Run inside a paragraph can have its text mutated in
    /// place across renders — confirms the incremental path is firing for
    /// pure-text inlines, not just for InlineUIContainer.
    /// </summary>
    internal class InlineUI_IncrementalUpdate_RunMutatedInPlace(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                return VStack(
                    Button("Tick", () => setN(n + 1)),
                    RichTextBlock(new[]
                    {
                        Paragraph(
                            Run("Stable text "),
                            Run($"changing: {n}"))
                    })
                );
            });

            await Harness.Render();

            var rtb = H.FindControl<Microsoft.UI.Xaml.Controls.RichTextBlock>(_ => true);
            H.Check("InlineUI_RunIncr_RTBMounted", rtb is not null);

            // Capture the first Run reference (the "Stable text" one).
            var stableRun = FindFirstRun(rtb!);
            H.Check("InlineUI_RunIncr_StableRunCaptured", stableRun is not null);

            // Tick several times — incremental update should mutate the
            // SECOND run's text in place while leaving the first run
            // untouched. The first run instance must survive renders.
            H.ClickButton("Tick");
            await Harness.Render();
            H.ClickButton("Tick");
            await Harness.Render();

            var stableRunAfter = FindFirstRun(rtb!);
            H.Check("InlineUI_RunIncr_StableRunIdentityPreserved",
                ReferenceEquals(stableRun, stableRunAfter));
            H.Check("InlineUI_RunIncr_StableRunTextUnchanged",
                stableRunAfter?.Text == "Stable text ");

            // The changing run text should reflect the new value.
            var changingRun = FindLastRun(rtb!);
            H.Check("InlineUI_RunIncr_ChangingRunUpdated",
                changingRun?.Text == "changing: 2");
        }

        private static Microsoft.UI.Xaml.Documents.Run? FindFirstRun(
            Microsoft.UI.Xaml.Controls.RichTextBlock rtb)
        {
            foreach (var block in rtb.Blocks)
            {
                if (block is not Microsoft.UI.Xaml.Documents.Paragraph p) continue;
                foreach (var inline in p.Inlines)
                    if (inline is Microsoft.UI.Xaml.Documents.Run r) return r;
            }
            return null;
        }

        private static Microsoft.UI.Xaml.Documents.Run? FindLastRun(
            Microsoft.UI.Xaml.Controls.RichTextBlock rtb)
        {
            Microsoft.UI.Xaml.Documents.Run? last = null;
            foreach (var block in rtb.Blocks)
            {
                if (block is not Microsoft.UI.Xaml.Documents.Paragraph p) continue;
                foreach (var inline in p.Inlines)
                    if (inline is Microsoft.UI.Xaml.Documents.Run r) last = r;
            }
            return last;
        }
    }
}
