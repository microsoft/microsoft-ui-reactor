using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

using Component = Microsoft.UI.Reactor.Core.Component;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Wave 2 coverage boost fixtures: SemanticElement, ValidationVisualizer styles,
/// ReconcileChild paths, RichToolTip update, FlexPanel child property changes,
/// ElementPool interactive control cleanup, animation curves, RichTextBlock rebuild.
/// </summary>
internal static class CoverageBoostFixtures2
{
    // ════════════════════════════════════════════════════════════════════════
    //  1. SemanticElement — mount + update exercises SemanticPanel/Peer and
    //     SemanticDescription property reconciliation
    // ════════════════════════════════════════════════════════════════════════

    internal class SemanticElementExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var semantics = phase switch
                {
                    0 => new SemanticDescription(
                        Role: "slider",
                        Value: "50%",
                        RangeMin: 0,
                        RangeMax: 100,
                        RangeValue: 50,
                        IsReadOnly: false),
                    _ => new SemanticDescription(
                        Role: "progressbar",
                        Value: "75%",
                        RangeMin: 0,
                        RangeMax: 100,
                        RangeValue: 75,
                        IsReadOnly: true),
                };
                return VStack(
                    new SemanticElement(
                        TextBlock($"Semantic:{phase}"),
                        semantics),
                    Button("UpdateSemantic", () => set(1))
                );
            });

            await Harness.Render();
            H.Check("Semantic_Mounted", H.FindText("Semantic:0") is not null);

            H.ClickButton("UpdateSemantic");
            await Harness.Render();
            H.Check("Semantic_Updated", H.FindText("Semantic:1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. ValidationVisualizer InfoBar + Summary styles — exercises the
    //     uncovered InfoBar/Summary mount branches in Reconciler.Mount.cs
    // ════════════════════════════════════════════════════════════════════════

    internal class ValidationVisualizerStyles(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);

                Element content;
                if (phase == 0)
                {
                    // InfoBar style visualizer with validated field
                    content = ValidationVisualizerDsl.ValidationVisualizer(
                        VisualizerStyle.InfoBar,
                        VStack(
                            TextBlock("InfoBarViz"),
                            TextBox("", _ => { })
                                .Validate("testField", "", Validate.Required("Required"))
                        ),
                        title: "Validation Errors",
                        showWhen: ShowWhen.Always
                    );
                }
                else if (phase == 1)
                {
                    // Summary style visualizer
                    content = ValidationVisualizerDsl.ValidationVisualizer(
                        VisualizerStyle.Summary,
                        VStack(
                            TextBlock("SummaryViz"),
                            TextBox("", _ => { })
                                .Validate("testField2", "", Validate.Required("Required"))
                        ),
                        title: "Validation Summary",
                        showWhen: ShowWhen.Always
                    );
                }
                else
                {
                    // Custom style visualizer
                    content = ValidationVisualizerDsl.ValidationVisualizer(
                        msgs => TextBlock($"Custom:{msgs.Count} errors"),
                        VStack(
                            TextBlock("CustomViz"),
                            TextBox("", _ => { })
                                .Validate("testField3", "", Validate.Required("Required"))
                        ),
                        showWhen: ShowWhen.Always
                    );
                }

                return VStack(
                    content,
                    Button("NextStyle", () => set(phase + 1))
                );
            });

            await Harness.Render();
            H.Check("ValViz_InfoBarMounted", H.FindText("InfoBarViz") is not null);

            H.ClickButton("NextStyle");
            await Harness.Render();
            H.Check("ValViz_SummaryMounted", H.FindText("SummaryViz") is not null);

            H.ClickButton("NextStyle");
            await Harness.Render();
            H.Check("ValViz_CustomMounted", H.FindText("CustomViz") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3. TitleBar mount + update
    // ════════════════════════════════════════════════════════════════════════

    internal class TitleBarMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    TitleBar(phase == 0 ? "App Title" : "Updated Title"),
                    TextBlock($"TitlePhase:{phase}"),
                    Button("UpdateTitle", () => set(1))
                );
            });

            await Harness.Render();
            H.Check("TitleBar_Mounted", H.FindText("TitlePhase:0") is not null);

            H.ClickButton("UpdateTitle");
            await Harness.Render();
            H.Check("TitleBar_Updated", H.FindText("TitlePhase:1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3b. TitleBar RightHeader caption-button inset — regression for #511.
    //
    //  WinUI 3 TitleBar.OnApplyTemplate calls UpdatePadding(), which reads
    //  AppWindow.TitleBar.RightInset to size RightPaddingColumn (the column
    //  that pads RightHeader away from the system min/max/close buttons).
    //  RightInset is non-zero only when Window.ExtendsContentIntoTitleBar is
    //  true at template-apply time. PR #455 moved the
    //  ExtendsContentIntoTitleBar/SetTitleBar write into the descriptor's
    //  Loaded handler, which fires AFTER OnApplyTemplate — so the column
    //  stayed at width 0 and RightHeader overlapped the caption buttons.
    //  The fix restores the legacy mount-time (pre-tree-attach) write; this
    //  fixture pins that ordering by checking RightPaddingColumn.Width > 0
    //  after one render.
    // ════════════════════════════════════════════════════════════════════════

    internal class TitleBarRightHeaderInset(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Use a separate window so this regression check doesn't mutate
            // the shared selftest harness window's ExtendsContentIntoTitleBar
            // / SetTitleBar registration. The TitleBar descriptor reads
            // ReactorApp.ActiveHostInternal at mount-time, so we host the
            // probe inside an isolated ReactorHost on a fresh Window.
            var prevActiveHost = ReactorApp.ActiveHostInternal;
            var window = new Window { Title = "TitleBar Inset Probe" };
            window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(600, 200));
            window.Activate();

            try
            {
                var host = new ReactorHost(window);
                host.Mount(_ => (TitleBar("InsetProbe") with
                {
                    RightHeader = TextBlock("RH").AutomationName("TitleBarRH"),
                }));

                // Drive the isolated host's render loop directly — Harness.Render
                // only pumps ReactorApp.PrimaryWindow's host (the shared harness
                // window), not the one we just created here, so it can't tell
                // us when *our* host has finished. host.WaitForIdleAsync is the
                // bounded-convergence wait Reactor exposes for exactly this
                // case. See TESTING.md → "Selftest waiting patterns".
                await host.WaitForIdleAsync();
                ((UIElement?)window.Content)?.UpdateLayout();

                // WinUI 3 TitleBar realizes its template + runs UpdatePadding
                // via Normal-priority dispatcher messages scheduled by the
                // first layout pass. Drain them with one Low-priority yield
                // (mirrors Harness.Render's wave pattern) and re-layout so
                // the probe sees the final RightPaddingColumn width.
                var dq = DispatcherQueue.GetForCurrentThread();
                var yieldTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!dq.TryEnqueue(DispatcherQueuePriority.Low, () => yieldTcs.SetResult()))
                    yieldTcs.SetResult();
                await yieldTcs.Task;
                ((UIElement?)window.Content)?.UpdateLayout();

                // Pump until the TitleBar materializes in the visual tree
                // (template + content realization can take an extra dispatcher
                // wave on contended runners). Re-queries each pass — never
                // captures a stale snapshot. Bounded so a real regression
                // still fails instead of hanging.
                Microsoft.UI.Xaml.Controls.TitleBar? titleBar = null;
                for (int pass = 0; pass < 8; pass++)
                {
                    titleBar = FindFirst<Microsoft.UI.Xaml.Controls.TitleBar>(window.Content);
                    if (titleBar is not null) break;
                    await host.WaitForIdleAsync();
                    ((UIElement?)window.Content)?.UpdateLayout();
                }
                H.Check("TBInset_TitleBarFound", titleBar is not null);
                if (titleBar is null) return;

                // Force template application + layout so the template parts
                // exist and UpdatePadding() has run.
                titleBar.ApplyTemplate();
                titleBar.UpdateLayout();

                var rightPaddingColumn = FindRightPaddingColumn(titleBar);
                H.Check("TBInset_RightPaddingColumnFound", rightPaddingColumn is not null);
                if (rightPaddingColumn is null) return;

                // Caption buttons exist on every WinUI 3 OverlappedPresenter
                // window → RightInset is always > 0, so a 0-width
                // RightPaddingColumn is unambiguous evidence of the #511
                // timing regression.
                H.Check(
                    $"TBInset_RightPaddingColumnNonZero (width={rightPaddingColumn.Width.Value})",
                    rightPaddingColumn.Width.Value > 0);

                H.Check("TBInset_WindowExtended", window.ExtendsContentIntoTitleBar);

                host.Dispose();
            }
            finally
            {
                window.Close();
                // Restore the harness host as ActiveHostInternal so subsequent
                // fixtures see the shared host they expect.
                if (prevActiveHost is not null)
                    ReactorApp.ActiveHostInternal = prevActiveHost;
            }
        }

        private static T? FindFirst<T>(DependencyObject? root) where T : class
        {
            if (root is null) return null;
            if (root is T match) return match;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var hit = FindFirst<T>(VisualTreeHelper.GetChild(root, i));
                if (hit is not null) return hit;
            }
            return null;
        }

        private static ColumnDefinition? FindRightPaddingColumn(Microsoft.UI.Xaml.Controls.TitleBar titleBar)
        {
            // The "RightPaddingColumn" template part lives on the root Grid of
            // the TitleBar template; reach it via the standard FindName lookup
            // against the visual tree root.
            if (VisualTreeHelper.GetChildrenCount(titleBar) == 0) return null;
            var templateRoot = VisualTreeHelper.GetChild(titleBar, 0) as FrameworkElement;
            return templateRoot?.FindName("RightPaddingColumn") as ColumnDefinition;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4. ReconcileChild coverage — exercises the generic ReconcileChild
    //     method with all 3 paths through Expander header/content changes
    // ════════════════════════════════════════════════════════════════════════

    internal class ReconcileChildPaths(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return phase switch
                {
                    0 => VStack(
                        Expander("HeaderA", TextBlock("ContentA"), isExpanded: true),
                        Button("PhaseNext", () => set(1))
                    ),
                    1 => VStack(
                        Expander("HeaderB", TextBlock("ContentB"), isExpanded: true),
                        Button("PhaseNext", () => set(2))
                    ),
                    _ => VStack(
                        TextBlock("NoExpander"),
                        Button("PhaseNext", () => set(3))
                    ),
                };
            });

            await Harness.Render();
            H.Check("RecChild_InitialMount", H.FindText("ContentA") is not null);

            H.ClickButton("PhaseNext");
            await Harness.Render();
            H.Check("RecChild_Updated", H.FindText("ContentB") is not null);

            H.ClickButton("PhaseNext");
            await Harness.Render();
            H.Check("RecChild_Unmounted", H.FindText("NoExpander") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5. RichToolTip update — exercises the ToolTip reconciliation path
    //     where both old and new elements have rich tooltips
    // ════════════════════════════════════════════════════════════════════════

    internal class RichToolTipUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var tooltip = phase == 0
                    ? TextBlock("Tip text A")
                    : TextBlock("Tip text B");
                return VStack(
                    TextBlock($"TipPhase:{phase}")
                        .WithToolTip(tooltip),
                    Button("UpdateTip", () => set(phase + 1))
                );
            });

            await Harness.Render();
            H.Check("Tip_Mounted", H.FindText("TipPhase:0") is not null);

            H.ClickButton("UpdateTip");
            await Harness.Render();
            H.Check("Tip_Updated", H.FindText("TipPhase:1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  6. FlexPanel child property changes at runtime
    // ════════════════════════════════════════════════════════════════════════

    internal class FlexChildPropChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (grow, setGrow) = ctx.UseState(0.0);
                return VStack(
                    FlexRow(
                        TextBlock("FlexA").Flex(grow: grow),
                        TextBlock("FlexB").Flex(grow: 1.0)
                    ).Height(100).Width(300),
                    TextBlock($"Grow:{grow:F1}"),
                    Button("SetGrow", () => setGrow(2.0))
                );
            });

            await Harness.Render();
            H.Check("FlexChild_Initial", H.FindText("Grow:0.0") is not null);

            H.ClickButton("SetGrow");
            await Harness.Render();
            H.Check("FlexChild_Changed", H.FindText("Grow:2.0") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  7. Reconciler resource override cleanup
    // ════════════════════════════════════════════════════════════════════════

    internal class ResourceOverrideCleanup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var btn = Button($"ResOverride:{phase}", () => { });
                if (phase == 0)
                {
                    btn = btn.Resources(r => r
                        .Set("ButtonBackground", "#FF0000")
                        .Set("ButtonBackgroundPointerOver", "#CC0000"));
                }
                return VStack(btn, Button("ClearRes", () => set(1)));
            });

            await Harness.Render();
            H.Check("ResOverride_WithResource", H.FindButton("ResOverride:0") is not null);

            H.ClickButton("ClearRes");
            await Harness.Render();
            H.Check("ResOverride_Cleared", H.FindButton("ResOverride:1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  8. NavigationView mount + content area
    // ════════════════════════════════════════════════════════════════════════

    internal class NavigationViewExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return NavigationView(
                    new[]
                    {
                        NavItem("Page1", icon: "Home"),
                        NavItem("Page2", icon: "Settings"),
                    },
                    content: TextBlock("NavContent")
                );
            });

            await Harness.Render();
            H.Check("NavView_Mounted", H.FindText("NavContent") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  9. ListView with keyed items — exercises positional child
    //     reconciliation with item count changes
    // ════════════════════════════════════════════════════════════════════════

    internal class TemplatedListExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var data = phase == 0
                    ? new[] { "Alpha", "Beta", "Gamma" }
                    : new[] { "Delta", "Epsilon" };
                return VStack(
                    ListView<string>(data, s => s, (item, _) => TextBlock(item)),
                    TextBlock($"ListPhase:{phase}"),
                    Button("ChangeList", () => set(1))
                );
            });

            await Harness.Render();
            H.Check("TList_InitialMount", H.FindText("ListPhase:0") is not null);

            H.ClickButton("ChangeList");
            await Harness.Render();
            H.Check("TList_Updated", H.FindText("ListPhase:1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  10. Implicit composition animation via .Animate() modifier —
    //      exercises ApplyPropertyAnimation path in Reconciler
    // ════════════════════════════════════════════════════════════════════════

    internal class CompositionTransitionExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    TextBlock("TransTarget")
                        .Animate(Animation.Curve.Ease(200))
                        .Opacity(phase == 0 ? 1.0 : 0.5),
                    TextBlock($"TransPhase:{phase}"),
                    Button("AnimateChange", () => set(phase + 1))
                );
            });

            await Harness.Render();
            H.Check("CompTrans_Mounted", H.FindText("TransPhase:0") is not null);

            H.ClickButton("AnimateChange");
            await Harness.Render();
            H.Check("CompTrans_Animated", H.FindText("TransPhase:1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  11. AnimationHelper curve paths — exercises Spring/Linear curves
    //      through .Animate() modifier alongside Ease from fixture 10
    // ════════════════════════════════════════════════════════════════════════

    internal class AnimationCurveExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    TextBlock("SpringAnim")
                        .Animate(Animation.Curve.Spring())
                        .Opacity(phase == 0 ? 1.0 : 0.7),
                    TextBlock("LinearAnim")
                        .Animate(Animation.Curve.Linear(200))
                        .Opacity(phase == 0 ? 1.0 : 0.8),
                    TextBlock($"CurvePhase:{phase}"),
                    Button("TriggerAnims", () => set(phase + 1))
                );
            });

            await Harness.Render();
            H.Check("AnimCurve_Mounted", H.FindText("CurvePhase:0") is not null);

            H.ClickButton("TriggerAnims");
            await Harness.Render();
            H.Check("AnimCurve_Triggered", H.FindText("CurvePhase:1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  12. Reconciler.Update RichTextBlock rebuild — exercises paragraph
    //      rebuild path when paragraph count changes
    // ════════════════════════════════════════════════════════════════════════

    internal class RichTextRebuild(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var paragraphs = phase switch
                {
                    0 => new[] { Paragraph(Run("Single paragraph")) },
                    1 => new[]
                    {
                        Paragraph(Run("First paragraph")),
                        Paragraph(Run("Second paragraph")),
                        Paragraph(Run("Third paragraph"))
                    },
                    _ => new[] { Paragraph(Run("Back to one")) },
                };
                return VStack(
                    RichTextBlock(paragraphs),
                    Button("ChangeParas", () => set(phase + 1))
                );
            });

            await Harness.Render();
            var rtb = H.FindControl<RichTextBlock>(_ => true);
            H.Check("RTBRebuild_InitialCount", rtb?.Blocks.Count == 1);

            H.ClickButton("ChangeParas");
            await Harness.Render();
            rtb = H.FindControl<RichTextBlock>(_ => true);
            H.Check("RTBRebuild_ExpandedCount", rtb?.Blocks.Count == 3);

            H.ClickButton("ChangeParas");
            await Harness.Render();
            rtb = H.FindControl<RichTextBlock>(_ => true);
            H.Check("RTBRebuild_ShrunkCount", rtb?.Blocks.Count == 1);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  13. ElementPool interactive control reset — exercises cleanup paths
    //      for Button, TextBox, ToggleSwitch, CheckBox, Slider, NumberBox
    // ════════════════════════════════════════════════════════════════════════

    internal class ElementPoolInteractiveReset(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return phase switch
                {
                    0 => VStack(
                        Button("PoolBtn1", () => { }),
                        TextBox("text1", _ => { }),
                        ToggleSwitch(true, _ => { }, header: "Toggle1"),
                        CheckBox(true, _ => { }, label: "Check1"),
                        Slider(50, onValueChanged: _ => { }),
                        NumberBox(42, onValueChanged: _ => { }),
                        Button("PoolCycle", () => set(1))
                    ),
                    1 => VStack(
                        TextBlock("PoolInteractive_Cleared"),
                        Button("PoolRestore", () => set(2))
                    ),
                    _ => VStack(
                        Button("PoolBtn2", () => { }),
                        TextBox("text2", _ => { }),
                        ToggleSwitch(false, _ => { }),
                        CheckBox(false, _ => { }, label: "Check2"),
                        Slider(75, onValueChanged: _ => { }),
                        NumberBox(99, onValueChanged: _ => { }),
                        TextBlock("PoolInteractive_Restored")
                    ),
                };
            });

            await Harness.Render();
            H.Check("PoolIR_Initial", H.FindButton("PoolBtn1") is not null);

            H.ClickButton("PoolCycle");
            await Harness.Render();
            H.Check("PoolIR_Cleared", H.FindText("PoolInteractive_Cleared") is not null);

            H.ClickButton("PoolRestore");
            await Harness.Render();
            H.Check("PoolIR_Restored", H.FindText("PoolInteractive_Restored") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  14. AutoSuggestBox + ComboBox mount/update — exercises mount/update
    //      paths for selection controls that have event wiring cleanup
    // ════════════════════════════════════════════════════════════════════════

    internal class DataGridSearchSort(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (text, setText) = ctx.UseState("Hello");
                var (phase, setPhase) = ctx.UseState(0);
                return VStack(
                    AutoSuggestBox(text, onTextChanged: v => setText(v)),
                    ComboBox(
                        new[] { "Red", "Green", "Blue" },
                        0,
                        _ => { }
                    ),
                    TextBlock($"ASBPhase:{phase}"),
                    Button("UpdateASB", () => { setText("World"); setPhase(phase + 1); })
                );
            });

            await Harness.Render();
            H.Check("ASB_Mounted", H.FindText("ASBPhase:0") is not null);

            H.ClickButton("UpdateASB");
            await Harness.Render();
            H.Check("ASB_Updated", H.FindText("ASBPhase:1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  15. SplitView mount + pane toggle — exercises SplitView reconciliation
    //      paths including pane content child reconciliation
    // ════════════════════════════════════════════════════════════════════════

    internal class SplitViewExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    SplitView(
                        pane: TextBlock($"Pane:{phase}"),
                        content: TextBlock($"SplitContent:{phase}")
                    ),
                    Button("UpdateSplit", () => set(phase + 1))
                );
            });

            await Harness.Render();
            H.Check("SplitView_Mounted", H.FindText("SplitContent:0") is not null);

            H.ClickButton("UpdateSplit");
            await Harness.Render();
            H.Check("SplitView_Updated", H.FindText("SplitContent:1") is not null);
        }
    }
}
