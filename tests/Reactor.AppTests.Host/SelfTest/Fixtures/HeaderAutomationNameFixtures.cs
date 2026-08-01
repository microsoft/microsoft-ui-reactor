using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using WinXF = Microsoft.UI.Xaml.FrameworkElement;
using WinXC = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// Issue #981 — the ReactorGallery "Basic ComboBox" and "Basic ToggleSwitch" cards
// shipped a bare control, so UIA reported Name: (null) on the very sample a
// newcomer is most likely to copy.
//
// The gallery itself has no test tier, so what is pinned down here is the
// *mechanism* the fix leans on: that a label declared on the element actually
// reaches the control's UIA Name. Each fixture mounts an unlabelled baseline
// alongside the labelled variants in one host, so every reading is taken against
// the same baseline under the same conditions. Some variants deliberately carry
// a second modifier too — a rival name source, or the editable template — and
// those are called out where they are mounted:
//
//   * the "no name" arm fails if WinUI ever starts naming a bare control (which
//     would mean the premise of the issue moved, and the fix is no longer what
//     supplies the name);
//   * the "is name" arm fails if Reactor stops forwarding Header /
//     AutomationName / on-off content to the control.
//
// Neither arm can pass vacuously on the strength of the other: a PeerName that
// degraded to always-empty would satisfy the first and fail the second — that is
// what stops a dead environment reading as a pass, and it holds with or without
// the logging. The raw names are logged as well, so that such a failure is
// diagnosable from the output instead of several distinct causes collapsing into
// the same red verdict.
//
// Reactor's caption mirror serves two channels unequally, and the distinction is
// worth stating precisely because the loose version ("WinUI names these anyway")
// is one a later reader would act on by deleting the arm. Measured, not read:
//
//   * returning a sentinel from the ToggleSwitchElement arm of
//     Reconciler.ResolveCaptionForElement (which resolves
//     `Header ?? OnContent ?? OffContent` and is mirrored into
//     AutomationProperties.Name by ApplyDefaultAutomationName) propagates that
//     sentinel to every switch below carrying no explicit AutomationName. So the
//     arm does run, and its output wins at the peer;
//   * returning *null* from the same arm leaves every peer check below green.
//     For the three caption-bearing switches that set no explicit
//     AutomationName that is the informative reason: ApplyDefaultAutomationName
//     early-returns on an empty caption, and WinUI's ToggleSwitchAutomationPeer
//     then derives the identical string from Header / OnContent / OffContent by
//     itself. The bare and the explicitly-named switch stay green for reasons of
//     their own and carry no weight here.
//
// Those two results together — and only together — say what the arm is worth.
// It does take precedence at the peer when it writes something the peer would
// not have derived (that is what the sentinel shows); but in normal operation it
// writes exactly what the peer derives anyway, so *removing* it is invisible to
// a peer reader while AutomationProperties.Name goes empty and nothing else
// fills it. That property is the channel the arm exists to serve ("so UIA
// clients that read the attached property directly (not via the peer) still find
// the name"). The Attached checks below read it, so deleting the arm reddens
// them while every Peer check stays green. All three branches of the resolver
// get one switch each, since a single branch would not support the claim for the
// other two. Nothing in Reactor mirrors a ComboBox caption at all.
//
// The gallery-facing checks are anchored to the modifier the fix uses rather than
// to that mirror. The two mutations redden overlapping but distinct sets, which
// is what separates the modifier's contribution from the mirror's:
//
//   * .Header(...) made a no-op for both element types → 5 red, 9 green: the four
//     Header peer checks plus HeaderIsAttachedName (which depends on both);
//   * the caption arm returning null → 3 red, 11 green: only the three
//     caption-bearing Attached checks, every peer check untouched.
internal static class HeaderAutomationNameFixtures
{
    private static readonly string[] Colors = ["Red", "Green", "Blue", "Yellow"];

    /// <summary>The control's name as UIA sees it; empty when it has none.</summary>
    private static string PeerName(WinXF fe) =>
        FrameworkElementAutomationPeer.CreatePeerForElement(fe)?.GetName() ?? string.Empty;

    /// <summary>
    /// AutomationProperties.Name as an attached-property reader sees it — a
    /// different channel from <see cref="PeerName"/>, which falls back to WinUI's
    /// own peer derivation when this property is unset. For the three
    /// ToggleSwitches below that are named by Reactor's caption mirror and set no
    /// explicit AutomationName, removing the mirror shows up in this reading and,
    /// as measured here, in none of the corresponding peer readings. A control
    /// that sets AutomationName explicitly writes this property directly and is
    /// unaffected either way.
    /// </summary>
    private static string AttachedName(WinXF fe) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(fe) ?? string.Empty;

    // ── ComboBox ────────────────────────────────────────────────────────

    // The gallery's three ComboBox cards are fixed with .Header(...), which is
    // only a fix if a ComboBox header reaches the UIA Name. The issue verified
    // that for ToggleSwitch but only assumed it for ComboBox, so it is measured
    // here rather than taken on trust. Nothing in Reactor mirrors a ComboBox
    // header into AutomationProperties.Name — Reconciler.ResolveCaptionForElement
    // has no ComboBoxElement arm — so the name can only come from WinUI's own
    // ComboBoxAutomationPeer, which is exactly what is being relied on.
    //
    // One arm per gallery card, because each card is a different control at
    // runtime and none of them is implied by the others: PlaceholderText is a
    // rival source of caption text, and IsEditable swaps in a template that
    // contains its own TextBox.
    internal class ComboBoxHeaderBecomesName(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(8,
                ComboBox(Colors, 0, _ => { }),
                ComboBox(Colors, 0, _ => { }).Header("Colors"),
                ComboBox(Colors).Header("Colors").PlaceholderText("Pick a color"),
                ComboBox(Colors, 0, _ => { }).Header("Colors").IsEditable()
            ));
            await Harness.Render();
            await Harness.Render();

            var boxes = H.FindAllControls<WinXC.ComboBox>(_ => true);
            if (boxes.Count < 4) { H.Check("HAN_ComboBox_AllRealized", false); return; }

            var bare = PeerName(boxes[0]);
            var headed = PeerName(boxes[1]);
            var headedPlaceholder = PeerName(boxes[2]);
            var headedEditable = PeerName(boxes[3]);
            Console.WriteLine(
                $"# HAN ComboBox: bare=<{bare}> headed=<{headed}> " +
                $"headedPlaceholder=<{headedPlaceholder}> headedEditable=<{headedEditable}> " +
                $"editable={boxes[3].IsEditable}");

            H.Check("HAN_ComboBox_BareHasNoName", bare.Length == 0);
            H.Check("HAN_ComboBox_HeaderIsName", headed == "Colors");
            H.Check("HAN_ComboBox_HeaderBeatsPlaceholder", headedPlaceholder == "Colors");
            // Guards the premise of the arm below: if IsEditable stopped being
            // applied, the "editable" reading would silently become a second
            // copy of the non-editable one and could no longer fail on its own.
            H.Check("HAN_ComboBox_EditableIsEditable", boxes[3].IsEditable);
            H.Check("HAN_ComboBox_EditableHeaderIsName", headedEditable == "Colors");
        }
    }

    // ── ToggleSwitch ────────────────────────────────────────────────────

    // One host, five switches, one label mechanism each. The gallery's first
    // card is fixed with .AutomationName(...) (its third card already owns the
    // .Header(...) demonstration), and its second card relies on the on/off
    // content — so all three of the mechanisms the page depends on are read
    // against the same unlabelled baseline. The fifth switch is not a gallery
    // path; see the off-content check below.
    internal class ToggleSwitchLabelBecomesName(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(8,
                ToggleSwitch(false, _ => { }),
                ToggleSwitch(false, _ => { }).AutomationName("Basic toggle"),
                ToggleSwitch(false, _ => { }).Header("Wi-Fi"),
                ToggleSwitch(true, _ => { }, "Working", "Not working"),
                ToggleSwitch(false, _ => { }, offContent: "Off only")
            ));
            await Harness.Render();
            await Harness.Render();

            var switches = H.FindAllControls<WinXC.ToggleSwitch>(_ => true);
            if (switches.Count < 5) { H.Check("HAN_ToggleSwitch_AllRealized", false); return; }

            var bare = PeerName(switches[0]);
            var named = PeerName(switches[1]);
            var headed = PeerName(switches[2]);
            var onOff = PeerName(switches[3]);
            var offOnly = PeerName(switches[4]);
            Console.WriteLine(
                $"# HAN ToggleSwitch: bare=<{bare}> automationName=<{named}> " +
                $"header=<{headed}> onOffContent=<{onOff}> offContentOnly=<{offOnly}>");

            H.Check("HAN_ToggleSwitch_BareHasNoName", bare.Length == 0);
            H.Check("HAN_ToggleSwitch_AutomationNameIsName", named == "Basic toggle");
            H.Check("HAN_ToggleSwitch_HeaderIsName", headed == "Wi-Fi");
            // Exact, not merely non-empty. The on-content wins deterministically
            // rather than tracking state — the switch is mounted IsOn=true and
            // names as the *on* content. A non-emptiness check would have stayed
            // green if the off-content started winning instead, or if some
            // unrelated default supplied a name; "Working" fails on both.
            H.Check("HAN_ToggleSwitch_OnOffContentIsName", onOff == "Working");
            // The third and last fallback of the caption resolver. Not a gallery
            // path — it is here so the header comment's account of the mirror
            // covers every branch of `Header ?? OnContent ?? OffContent` rather
            // than the two a reader could check from the cards.
            H.Check("HAN_ToggleSwitch_OffContentOnlyIsName", offOnly == "Off only");

            // The mirror's own channel. The caption-bearing Peer readings above
            // survive deletion of the ToggleSwitchElement caption arm because
            // WinUI's peer re-derives the same strings; the bare switch still
            // goes through that arm but yields no caption, and the
            // explicitly-named switch stays green for its own separate reason.
            // Measured, that leaves these Attached checks as the only ones in the
            // file that redden when the arm goes away. One per resolver branch,
            // plus the bare case to show the mirror invents nothing when there is
            // no caption.
            var bareAttached = AttachedName(switches[0]);
            var headedAttached = AttachedName(switches[2]);
            var onOffAttached = AttachedName(switches[3]);
            var offOnlyAttached = AttachedName(switches[4]);
            Console.WriteLine(
                $"# HAN ToggleSwitch attached: bare=<{bareAttached}> header=<{headedAttached}> " +
                $"onOffContent=<{onOffAttached}> offContentOnly=<{offOnlyAttached}>");

            H.Check("HAN_ToggleSwitch_BareHasNoAttachedName", bareAttached.Length == 0);
            H.Check("HAN_ToggleSwitch_HeaderIsAttachedName", headedAttached == "Wi-Fi");
            H.Check("HAN_ToggleSwitch_OnContentIsAttachedName", onOffAttached == "Working");
            H.Check("HAN_ToggleSwitch_OffContentIsAttachedName", offOnlyAttached == "Off only");
        }
    }
}
