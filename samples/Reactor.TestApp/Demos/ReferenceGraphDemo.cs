using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

// Spec 057 §11 Phase 3 (3.1) — a hands-on driver for the devtools reference-graph
// overlay. Every kind of reactive reference edge the overlay can draw is wired up
// here in one screen: a scalar accessibility edge, a list edge, a directed cycle,
// and a deliberately-unresolved edge you can toggle. Launch the app with
// `--devtools run`, open the VS Code preview and hit the **References** toggle (or
// run `mur devtools call references --pretty`) to watch the edges and the
// cycle / unresolved diagnostics update live.
class ReferenceGraphDemo : Component
{
    public override Element Render()
    {
        // One stable ElementRef per referenced control. UseElementRef keeps the
        // same cell across renders so the reconciler can wire reactive edges to it.
        var labelRef = this.UseElementRef<FrameworkElement>();
        var desc1Ref = this.UseElementRef<FrameworkElement>();
        var desc2Ref = this.UseElementRef<FrameworkElement>();
        var ringARef = this.UseElementRef<FrameworkElement>();
        var ringBRef = this.UseElementRef<FrameworkElement>();
        var missingRef = this.UseElementRef<FrameworkElement>();

        var (showLabelTarget, setShowLabelTarget) = UseState(true);

        return ScrollView(VStack(16,
            Heading("Reference Graph"),
            TextBlock(
                "Each section below declares a reactive reference edge — the same edges " +
                "the devtools 'references' overlay visualizes. Open the References overlay " +
                "in the VS Code preview (or run `mur devtools call references --pretty`) to " +
                "see them, plus the cycle and unresolved diagnostics, update live."),

            // 1. Scalar accessibility edge: LabeledBy → one target.
            SubHeading("1. LabeledBy — a scalar reference edge"),
            TextBlock("The input is labelled by the target button (AutomationProperties.LabeledBy)."),
            HStack(8,
                Button("Label Target", () => { }).Ref(labelRef),
                Button("Labelled Input", () => { }).LabeledBy(labelRef)),

            // 2. List-valued accessibility edge: DescribedBy → ordered list of targets.
            SubHeading("2. DescribedBy — a list reference edge"),
            TextBlock("The input is described by two targets, in declaration order."),
            HStack(8,
                Button("Description One", () => { }).Ref(desc1Ref),
                Button("Description Two", () => { }).Ref(desc2Ref),
                Button("Described Input", () => { }).DescribedBy(desc1Ref, desc2Ref)),

            // 3. Directed cycle: A → B and B → A via XYFocus. Cycles are a supported
            //    topology (spec §3.3) and are reported informationally, not as errors.
            SubHeading("3. XYFocus ring — a reference cycle"),
            TextBlock("A points right to B and B points right to A — a 2-node cycle the overlay flags as a 'cycle' diagnostic."),
            HStack(8,
                Button("Ring A", () => { }).Ref(ringARef).XYFocusRight(ringBRef),
                Button("Ring B", () => { }).Ref(ringBRef).XYFocusRight(ringARef)),

            // 4. Unresolved edge: the input references a target that may not be mounted.
            //    With the target hidden, the edge's cell is null → an 'unresolved' diagnostic.
            SubHeading("4. Unresolved reference — toggle the target"),
            TextBlock("Uncheck the box to unmount the target. The input keeps its LabeledBy edge, but it now resolves to nothing — the overlay reports it as 'unresolved'."),
            CheckBox(showLabelTarget, setShowLabelTarget, label: "Mount the unresolved-demo target"),
            HStack(8,
                showLabelTarget
                    ? Button("Toggleable Target", () => { }).Ref(missingRef) with { Key = "ref-graph-toggle-target" }
                    : Empty(),
                Button("Input With Maybe-Target", () => { }).LabeledBy(missingRef))
        ));
    }
}
