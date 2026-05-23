using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
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
