using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 047 §8 — <b>value-diff</b> echo-suppression proof-of-concept fixtures
/// for the descriptor controlled fast path (<c>ControlledPropEntry</c>).
///
/// <para><b>What changed:</b> on this path a programmatic <c>Update</c> write no
/// longer bumps the causal <c>ChangeEchoSuppressor</c> counter. Instead it arms a
/// per-control <c>ExpectedEcho</c> on the <c>DescriptorControlledPayload</c>; the
/// change-event trampoline drops the single event whose readback equals that
/// expected value (then clears it). The counter is retained for every other path
/// (hand-coded / coercing / collection entries, the Slider/TextBox/ToggleSwitch
/// handlers, the setter scope, and the public <c>WriteSuppressed</c> primitive).</para>
///
/// <para><b>What these lock down:</b> the <em>drift</em> case the existing
/// <c>Echo_*_RealInput</c> stranding fixtures don't cover — those update the
/// control to the value the user already produced (no drift, no write), whereas
/// these drive a real programmatic change (control at X, element now Y, control
/// not yet at Y) so the suppressed write is genuine and the value-diff path is
/// exercised end to end. Each fixture asserts: (1) the programmatic update lands
/// on the control, (2) it does NOT echo into the user callback, and (3) a
/// subsequent real interaction still fires — distinguishing a correct value-diff
/// suppressor from one that is over-eager (kills real input) or under-eager
/// (regression).</para>
/// </summary>
internal static class Spec047ValueDiffEchoFixtures
{
    private static readonly Action _noOp = static () => { };

    /// <summary>RadioButton.IsChecked — generic <c>.Controlled</c> entry.</summary>
    internal class RadioButtonProgrammaticDrift(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var el1 = new RadioButtonElement(
                Label: "Option A", IsChecked: false, OnIsCheckedChanged: _ => fireCount++,
                GroupName: "vd-g1");

            if (rec.Mount(el1, _noOp) is WinUI.RadioButton rb)
            {
                parent.Children.Add(rb);
                await Harness.Render();
                H.Check("ValueDiff_RadioButton_MountNoFire", fireCount == 0);

                // Programmatic drift: control is false, element re-renders to true.
                // The synthesized Checked event must be recognized as the echo of
                // our own write (readback == ExpectedEcho) and dropped.
                rec.UpdateChild(el1, el1 with { IsChecked = true }, rb, _noOp);
                await Harness.Render();
                H.Check("ValueDiff_RadioButton_UpdateAppliedValue", rb.IsChecked == true);
                H.Check("ValueDiff_RadioButton_NoEchoCall", fireCount == 0);

                // Real user interaction must still fire (one-shot arm was consumed).
                rb.IsChecked = false;
                await Harness.Render();
                H.Check("ValueDiff_RadioButton_RealInputFires", fireCount == 1);

                rec.UnmountChild(rb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("ValueDiff_RadioButton_Mounted", false);
            }
        }
    }

    /// <summary>ToggleSplitButton.IsChecked — generic <c>.Controlled</c> entry.</summary>
    internal class ToggleSplitButtonProgrammaticDrift(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var el1 = new ToggleSplitButtonElement(
                Label: "Run", IsChecked: false, OnIsCheckedChanged: _ => fireCount++);

            if (rec.Mount(el1, _noOp) is WinUI.ToggleSplitButton tsb)
            {
                parent.Children.Add(tsb);
                await Harness.Render();
                H.Check("ValueDiff_ToggleSplitButton_MountNoFire", fireCount == 0);

                rec.UpdateChild(el1, el1 with { IsChecked = true }, tsb, _noOp);
                await Harness.Render();
                H.Check("ValueDiff_ToggleSplitButton_UpdateAppliedValue", tsb.IsChecked == true);
                H.Check("ValueDiff_ToggleSplitButton_NoEchoCall", fireCount == 0);

                tsb.IsChecked = false;
                await Harness.Render();
                H.Check("ValueDiff_ToggleSplitButton_RealInputFires", fireCount == 1);

                rec.UnmountChild(tsb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("ValueDiff_ToggleSplitButton_Mounted", false);
            }
        }
    }

    /// <summary>TextBox.Value — hand-coded <c>TextBoxHandler</c> (live dispatched
    /// path). Programmatic drift: control text "abc", element re-renders to "xyz".
    /// The synthesized TextChanged must be recognized as the echo of our own
    /// write (readback == ExpectedEchoText) and dropped.</summary>
    internal class TextBoxProgrammaticDrift(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            string? lastValue = null;
            var el1 = new TextBoxElement("abc", v => { fireCount++; lastValue = v; });

            if (rec.Mount(el1, _noOp) is WinUI.TextBox tb)
            {
                parent.Children.Add(tb);
                await Harness.Render();
                H.Check("ValueDiff_TextBox_MountNoFire", fireCount == 0);
                H.Check("ValueDiff_TextBox_MountValue", tb.Text == "abc");

                // Programmatic drift: element value changes abc -> xyz.
                rec.UpdateChild(el1, el1 with { Value = "xyz" }, tb, _noOp);
                await Harness.Render();
                H.Check("ValueDiff_TextBox_UpdateAppliedValue", tb.Text == "xyz");
                H.Check("ValueDiff_TextBox_NoEchoCall", fireCount == 0);

                // Real user input must still fire (one-shot arm was consumed).
                tb.Text = "user typed";
                await Harness.Render();
                H.Check("ValueDiff_TextBox_RealInputFires", fireCount == 1 && lastValue == "user typed");

                rec.UnmountChild(tb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("ValueDiff_TextBox_Mounted", false);
            }
        }
    }

    /// <summary>TextBox controlled-mode snap-back. User input "ab" is rejected by
    /// the callback back to the controlled value "a"; the re-write of "a" must NOT
    /// echo into the callback, but the real input that triggered it must have fired
    /// exactly once.</summary>
    internal class TextBoxControlledSnapBack(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            string? lastValue = null;
            var el1 = new TextBoxElement("a", v => { fireCount++; lastValue = v; });

            if (rec.Mount(el1, _noOp) is WinUI.TextBox tb)
            {
                parent.Children.Add(tb);
                await Harness.Render();

                // Simulate real user input "ab" — fires TextChanged once.
                tb.Text = "ab";
                await Harness.Render();
                H.Check("ValueDiff_TextBox_SnapBack_RealInputFired", fireCount == 1 && lastValue == "ab");

                // Component rejected the edit: element value stays "a" while the
                // control still holds "ab". A real re-render forces Update through
                // the ShallowEquals short-circuit via the dirty-ancestor path;
                // here we force it by also changing a benign field (PlaceholderText)
                // so Update runs and branch B snaps the control back to "a" under
                // value-diff suppression — which must NOT re-fire the callback.
                rec.UpdateChild(el1, el1 with { PlaceholderText = "ph" }, tb, _noOp);
                await Harness.Render();
                H.Check("ValueDiff_TextBox_SnapBack_ControlReverted", tb.Text == "a");
                H.Check("ValueDiff_TextBox_SnapBack_NoEchoCall", fireCount == 1);

                rec.UnmountChild(tb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("ValueDiff_TextBox_SnapBack_Mounted", false);
            }
        }
    }

    /// <summary>TextBox null→non-null <c>OnChanged</c> transition combined with a
    /// value change in the same Update. The controlled write happens before
    /// EnsureTextBoxWiring wires the TextChanged trampoline, so arming must still
    /// occur (the trampoline goes live before any deferred echo is delivered).
    /// Regression guard: the legacy counter suppressed this echo regardless of
    /// subscription timing; value-diff must too.</summary>
    internal class TextBoxControlledTransitionDrift(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            string? lastValue = null;
            // Start uncontrolled: no OnChanged wired.
            var el1 = new TextBoxElement("abc");

            if (rec.Mount(el1, _noOp) is WinUI.TextBox tb)
            {
                parent.Children.Add(tb);
                await Harness.Render();
                H.Check("ValueDiff_TextBox_Transition_MountValue", tb.Text == "abc");

                // Single Update: gain OnChanged AND change the value abc -> xyz.
                var el2 = new TextBoxElement("xyz", v => { fireCount++; lastValue = v; });
                rec.UpdateChild(el1, el2, tb, _noOp);
                await Harness.Render();
                H.Check("ValueDiff_TextBox_Transition_UpdateAppliedValue", tb.Text == "xyz");
                H.Check("ValueDiff_TextBox_Transition_NoEchoCall", fireCount == 0);

                // Real input still fires after the transition.
                tb.Text = "typed";
                await Harness.Render();
                H.Check("ValueDiff_TextBox_Transition_RealInputFires", fireCount == 1 && lastValue == "typed");

                rec.UnmountChild(tb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("ValueDiff_TextBox_Transition_Mounted", false);
            }
        }
    }

    /// <summary>ComboBox.SelectedIndex — hand-coded <c>HandCodedControlledPropEntry</c>
    /// with the opt-in shared-arm value-diff path (<c>valueDiffEcho: true</c>, int
    /// readback). Programmatic drift 0→1 must apply, must NOT echo, and a later real
    /// selection must still fire. Locks the new shared <c>ReactorState.PendingEchoMatch</c>
    /// arm for a synchronous-event selection control.</summary>
    internal class ComboBoxProgrammaticDrift(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            int lastIdx = -99;
            var el1 = new ComboBoxElement(
                Items: new[] { "a", "b", "c" }, SelectedIndex: 0,
                OnSelectedIndexChanged: i => { fireCount++; lastIdx = i; });

            if (rec.Mount(el1, _noOp) is WinUI.ComboBox cb)
            {
                parent.Children.Add(cb);
                await Harness.Render();
                H.Check("ValueDiff_ComboBox_MountNoFire", fireCount == 0);
                H.Check("ValueDiff_ComboBox_MountValue", cb.SelectedIndex == 0);

                // Programmatic drift 0 -> 1: synchronous SelectionChanged is the
                // echo of our own write (readback == armed value) and is dropped.
                rec.UpdateChild(el1, el1 with { SelectedIndex = 1 }, cb, _noOp);
                await Harness.Render();
                H.Check("ValueDiff_ComboBox_UpdateAppliedValue", cb.SelectedIndex == 1);
                H.Check("ValueDiff_ComboBox_NoEchoCall", fireCount == 0);

                // Real user selection must still fire (one-shot arm consumed).
                cb.SelectedIndex = 2;
                await Harness.Render();
                H.Check("ValueDiff_ComboBox_RealInputFires", fireCount == 1 && lastIdx == 2);

                rec.UnmountChild(cb);
                parent.Children.Clear();
            }
            else
            {
                H.Check("ValueDiff_ComboBox_Mounted", false);
            }
        }
    }

    /// <summary>ToggleSwitch.IsOn — hand-coded <c>ToggleSwitchHandler</c> value-diff
    /// path (bool readback). Toggled fires synchronously inside the IsOn write, so
    /// the programmatic drift false→true must apply, must NOT echo, and a later real
    /// toggle must still fire.</summary>
    internal class ToggleSwitchProgrammaticDrift(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            bool lastVal = false;
            var el1 = new ToggleSwitchElement(
                IsOn: false, OnIsOnChanged: v => { fireCount++; lastVal = v; });

            if (rec.Mount(el1, _noOp) is WinUI.ToggleSwitch ts)
            {
                parent.Children.Add(ts);
                await Harness.Render();
                H.Check("ValueDiff_ToggleSwitch_MountNoFire", fireCount == 0);
                H.Check("ValueDiff_ToggleSwitch_MountValue", ts.IsOn == false);

                rec.UpdateChild(el1, el1 with { IsOn = true }, ts, _noOp);
                await Harness.Render();
                H.Check("ValueDiff_ToggleSwitch_UpdateAppliedValue", ts.IsOn == true);
                H.Check("ValueDiff_ToggleSwitch_NoEchoCall", fireCount == 0);

                ts.IsOn = false;
                await Harness.Render();
                H.Check("ValueDiff_ToggleSwitch_RealInputFires", fireCount == 1 && lastVal == false);

                rec.UnmountChild(ts);
                parent.Children.Clear();
            }
            else
            {
                H.Check("ValueDiff_ToggleSwitch_Mounted", false);
            }
        }
    }

    /// <summary>Regression guard for the SelectedIndex echo-suppression strand
    /// invariant. Prior to spec 050, GridView's controlled setter had an
    /// `if (v >= 0)` guard that silently dropped negative-value writes. Spec 050
    /// removed that guard so Optional.Of(-1) is the explicit force-clear sentinel
    /// (see GridViewElement.SelectedIndex XML doc + migration guide). This fixture
    /// asserts the post-spec-050 invariant: a programmatic Optional.Of(-1) drift
    /// writes -1 through WriteSuppressed (no echo back to OnSelectedIndexChanged),
    /// and a subsequent genuine user selection still fires the callback (the
    /// suppress token did not strand and swallow it).</summary>
    internal class GridViewGuardedNoOpStrand(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            int lastIdx = -99;
            var items = new Element[]
            {
                new TextBlockElement("i0"), new TextBlockElement("i1"), new TextBlockElement("i2"),
            };
            var el1 = new GridViewElement(items)
            {
                SelectedIndex = 0,
                OnSelectedIndexChanged = i => { fireCount++; lastIdx = i; },
            };

            if (rec.Mount(el1, _noOp) is WinUI.GridView gv)
            {
                parent.Children.Add(gv);
                await Harness.Render();
                H.Check("ValueDiff_GridViewStrand_MountValue", gv.SelectedIndex == 0);

                // GridView's SelectionChanged is deferred to container realization
                // (unlike ComboBox's synchronous event), so the bare mount-time
                // SelectedIndex=0 write echoes once after the trampoline subscribes.
                // That is pre-existing behavior (the counter model wrote mount bare
                // too); baseline-reset so the strand assertions below are clean.
                fireCount = 0;
                lastIdx = -99;

                // Spec 050: Optional.Of(-1) is the explicit force-clear sentinel.
                // The drift writes -1 through WriteSuppressed (the WinUI deselect
                // SelectionChanged that fires is consumed by the echo gate, so
                // OnSelectedIndexChanged does NOT fire).
                rec.UpdateChild(el1, el1 with { SelectedIndex = -1 }, gv, _noOp);
                await Harness.Render();
                H.Check("ValueDiff_GridViewStrand_ForceClearWrote", gv.SelectedIndex == -1);
                H.Check("ValueDiff_GridViewStrand_NoEchoCall", fireCount == 0);

                // Genuine user selection: must NOT be swallowed by a stranded token.
                // (-1 → 1 is a real change; -1 → -1 would be a no-op WinUI ignores.)
                gv.SelectedIndex = 1;
                await Harness.Render();
                H.Check("ValueDiff_GridViewStrand_RealSelectFires", fireCount == 1 && lastIdx == 1);

                rec.UnmountChild(gv);
                parent.Children.Clear();
            }
            else
            {
                H.Check("ValueDiff_GridViewStrand_Mounted", false);
            }
        }
    }

    /// <summary>Probe / regression guard for issue #464 — GridView raised
    /// duplicate <c>OnSelectedIndexChanged</c> echoes for two same-frame
    /// programmatic <c>SelectedIndex</c> writes (no render/drain between
    /// them). Asserts the production-reachable single-write path (1 fire,
    /// suppressed → 0) AND the rapid-double-write path (2 fires →
    /// suppressed → 0). ListBox is included as the negative control —
    /// it never exhibited the regression and must stay clean.</summary>
    internal class GridViewRapidDoubleWriteEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await ProbeAsync<WinUI.GridView>(static items => new GridViewElement(items)
                {
                    SelectedIndex = 0,
                },
                static (el, cb) => ((GridViewElement)el) with { OnSelectedIndexChanged = cb },
                static (el, i) => ((GridViewElement)el) with { SelectedIndex = i },
                static gv => gv.SelectedIndex,
                "GridView");

            await ProbeAsync<WinUI.ListBox>(static items =>
                {
                    // ListBox carries items as string[]; map element labels.
                    var labels = new string[items.Length];
                    for (int i = 0; i < items.Length; i++) labels[i] = $"i{i}";
                    return new ListBoxElement(labels) { SelectedIndex = 0 };
                },
                static (el, cb) => ((ListBoxElement)el) with { OnSelectedIndexChanged = cb },
                static (el, i) => ((ListBoxElement)el) with { SelectedIndex = i },
                static lb => lb.SelectedIndex,
                "ListBox");
        }

        private async Task ProbeAsync<TCtrl>(
            Func<Element[], Element> seedFactory,
            Func<Element, Action<int>, Element> withCallback,
            Func<Element, int, Element> withSelectedIndex,
            Func<TCtrl, int> readIndex,
            string label) where TCtrl : WinUI.Control
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            var items = new Element[]
            {
                new TextBlockElement("i0"), new TextBlockElement("i1"),
                new TextBlockElement("i2"), new TextBlockElement("i3"),
            };
            var elBase = seedFactory(items);
            var el = withCallback(elBase, _ => fireCount++);

            if (rec.Mount(el, _noOp) is not TCtrl ctrl)
            {
                H.Check($"Issue464_{label}_Mounted", false);
                return;
            }
            parent.Children.Add(ctrl);
            await Harness.Render();
            await Harness.Render(); // realize containers

            // ── Single-write per render: must not leak. ──
            fireCount = 0;
            var elNext = withSelectedIndex(el, 2);
            rec.UpdateChild(el, elNext, ctrl, _noOp);
            await Harness.Render();
            H.Check($"Issue464_{label}_SingleWrite_Index", readIndex(ctrl) == 2);
            H.Check($"Issue464_{label}_SingleWrite_NoEcho", fireCount == 0);
            el = elNext;

            // ── Two writes in the same frame (no render between): must not leak. ──
            fireCount = 0;
            var elTwo = withSelectedIndex(el, 1);
            rec.UpdateChild(el, elTwo, ctrl, _noOp);
            var elThree = withSelectedIndex(elTwo, 3);
            rec.UpdateChild(elTwo, elThree, ctrl, _noOp);
            await Harness.Render();
            H.Check($"Issue464_{label}_DoubleWrite_FinalIndex", readIndex(ctrl) == 3);
            H.Check($"Issue464_{label}_DoubleWrite_NoEcho", fireCount == 0);

            // ── Real user interaction must still fire after the suppression
            // tokens have been drained. ──
            fireCount = 0;
            if (ctrl is WinUI.GridView gv) gv.SelectedIndex = 0;
            else if (ctrl is WinUI.ListBox lb) lb.SelectedIndex = 0;
            await Harness.Render();
            H.Check($"Issue464_{label}_RealUserStillFires", fireCount == 1);

            rec.UnmountChild(ctrl);
            parent.Children.Clear();
        }
    }
}
