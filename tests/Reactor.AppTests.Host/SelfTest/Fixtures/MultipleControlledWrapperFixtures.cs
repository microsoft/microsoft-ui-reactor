using Microsoft.UI.Reactor.Core;
using Reactor.External.TestControl;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class MultipleControlledWrapperFixtures
{
    internal sealed class TwoValuesOnly(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<string>();
            using var host = H.CreateHost();
            host.Mount(_ => new BareRangeProbeElement
            {
                RangeStart = 20, RangeEnd = 80,
                OnRangeStartChanged = v => log.Add($"start={v}"),
                OnRangeEndChanged = v => log.Add($"end={v}"),
            });
            await Harness.Render();
            var c = H.FindControl<ControlledRangeProbe>(_ => true)
                ?? throw new InvalidOperationException("Two-value probe did not mount");
            var raw = new List<string>();
            c.ValueChanged += (_, e) => raw.Add(e.Endpoint);
            log.Clear();
            c.MoveStart(30);
            c.MoveEnd(70);
            H.Check("MultipleControlled_TwoValuesOnly_NativePositiveControl",
                raw.SequenceEqual(new[] { "Start", "End" }) && c.RangeStart == 30 && c.RangeEnd == 70);
            H.Check("MultipleControlled_TwoValuesOnly_Callbacks",
                log.SequenceEqual(new[] { "start=30", "end=70" }),
                $"actual [{string.Join(", ", log)}]");
        }
    }

    internal sealed class Shared(Harness h) : Scenario(h, "Shared");
    internal sealed class Mixed(Harness h) : Scenario(h, "Mixed");
    internal sealed class Distinct(Harness h) : Scenario(h, "Distinct");
    internal sealed class Deferred(Harness h) : Scenario(h, "Deferred");

    internal abstract class Scenario(Harness h, string kind) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Optional<double> start = 20, end = 80;
            Optional<int> count = 2;
            var log = new List<string>();
            var raw = new List<string>();
            var generation = 0;
            var accept = false;
            var rerenderOnEdit = false;
            var callbacks = true;
            var visible = true;
            var setters = Array.Empty<Action<ControlledRangeProbe>>();
            Action rerender = () => throw new InvalidOperationException("Not mounted");
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, bump) = ctx.UseReducer(0);
                rerender = () => bump(n => n + 1);
                if (!visible) return Factories.TextBlock("Range removed");
                // Capture this render's generation, so a stale callback is observable.
                var currentGeneration = generation;
                Action<double>? onStart = callbacks ? v =>
                {
                    if (accept) start = v;
                    log.Add($"{currentGeneration}:start={v}");
                    if (rerenderOnEdit) rerender();
                } : null;
                Action<double>? onEnd = callbacks ? v =>
                {
                    if (accept) end = v;
                    log.Add($"{currentGeneration}:end={v}");
                    if (rerenderOnEdit) rerender();
                } : null;
                Action<int>? onCount = callbacks ? v =>
                {
                    if (accept) count = v;
                    log.Add($"{currentGeneration}:count={v}");
                    if (rerenderOnEdit) rerender();
                } : null;
                Action<EventArgs> onPing = _ => log.Add($"{currentGeneration}:ping");
                Element element = kind switch
                {
                    "Shared" => new SharedRangeProbeElement
                    {
                        RangeStart = start, RangeEnd = end, Count = count,
                        OnRangeStartChanged = onStart, OnRangeEndChanged = onEnd,
                        OnCountChanged = onCount, OnPinged = onPing, Setters = setters,
                    },
                    "Distinct" => new DistinctRangeProbeElement
                    {
                        RangeStart = start, RangeEnd = end, Count = count,
                        OnRangeStartChanged = onStart, OnRangeEndChanged = onEnd,
                        OnCountChanged = onCount, OnPinged = onPing, Setters = setters,
                    },
                    "Mixed" => new MixedRangeProbeElement
                    {
                        RangeStart = start, RangeEnd = end, Count = count,
                        OnRangeStartChanged = onStart, OnRangeEndChanged = onEnd,
                        OnCountChanged = onCount, OnPinged = onPing, Setters = setters,
                    },
                    _ => new DeferredRangeProbeElement
                    {
                        RangeStart = start, RangeEnd = end,
                        OnRangeStartChanged = onStart, OnRangeEndChanged = onEnd,
                        OnPinged = onPing, Setters = setters,
                    },
                };
                return element.Width(200 + tick);
            });
            await Harness.Render();
            var c = H.FindControl<ControlledRangeProbe>(_ => true)
                ?? throw new InvalidOperationException("Generated range probe did not mount");
            Check("InitialValues", c.RangeStart == 20 && c.RangeEnd == 80);
            if (kind != "Deferred") Check("InitialCount", c.Count == 2);
            Expect("MountDoesNotNotify");

            EventHandler<RangeProbeChangedEventArgs> observer = (_, e) => raw.Add(e.Endpoint);
            c.ValueChanged += observer;
            c.MoveStart(30);
            c.MoveEnd(70);
            Check("PositiveNativeEventControl", raw.SequenceEqual(new[] { "Start", "End" }));
            c.ValueChanged -= observer;
            Expect("IndependentEndpoints", "0:start=30", "0:end=70");
            c.MoveStart(30);
            c.MoveEnd(70);
            Expect("UnchangedEndpointsDoNotNotify");
            MoveCount(3);
            if (kind != "Deferred") Expect("DifferentValueAndArgsType", "0:count=3");

            generation = 1;
            rerender();
            await Harness.Render();
            Check("UnchangedParentReasserted", c.RangeStart == 20 && c.RangeEnd == 80);
            Check("ControlIdentity", ReferenceEquals(c, H.FindControl<ControlledRangeProbe>(_ => true)));
            Expect("ReassertionDoesNotEcho");
            c.MoveBoth(25, 75);
            c.Ping();
            ExpectTogether("CurrentCallbacksAndOrdinaryEvent", "1:start=25", "1:end=75", "1:ping");

            accept = true;
            rerenderOnEdit = true;
            c.MoveBoth(26, 74);
            MoveCount(5);
            await Harness.Render();
            Check("ParentAccepts", start.Value == 26 && end.Value == 74 &&
                c.RangeStart == 26 && c.RangeEnd == 74);
            if (kind != "Deferred") Check("ParentAcceptsCount", count.Value == 5 && c.Count == 5);
            ExpectValues("AcceptanceDoesNotEcho", "1:start=26", "1:end=74", "1:count=5");

            accept = false;
            c.MoveBoth(35, 65);
            MoveCount(6);
            await Harness.Render();
            Check("ParentRejects", c.RangeStart == 26 && c.RangeEnd == 74);
            if (kind != "Deferred") Check("ParentRejectsCount", c.Count == 5);
            ExpectValues("RejectionDoesNotEcho", "1:start=35", "1:end=65", "1:count=6");

            c.Echo = RangeProbeEcho.Silent;
            c.RangeStart = 100;
            c.Echo = RangeProbeEcho.Deferred;
            start = 150;
            rerender();
            await Harness.Render();
            raw.Clear();
            c.ValueChanged += observer;
            c.FlushEchoes();
            c.ValueChanged -= observer;
            Check("CoercedNoOpNativePositiveControl",
                c.RangeStart == 100 && raw.SequenceEqual(new[] { "Start" }));
            Expect("CoercedNoOpDoesNotEcho");
            start = 26;

            c.Echo = RangeProbeEcho.Silent;
            c.RangeStart = 40;
            c.RangeEnd = 60;
            rerender();
            await Harness.Render();
            Check("SilentNativeDriftReasserted", c.RangeStart == 26 && c.RangeEnd == 74);
            Expect("SilentWriteDoesNotStrandToken");
            c.MoveStart(45);
            await Harness.Render();
            Expect("FirstEditAfterSilentWrite", "1:start=45");

            start = Optional<double>.Unset;
            end = Optional<double>.Unset;
            count = Optional<int>.Unset;
            rerender();
            await Harness.Render();
            c.MoveBoth(32, 68);
            MoveCount(7);
            await Harness.Render();
            Check("OmittedValuesStayNative", c.RangeStart == 32 && c.RangeEnd == 68);
            if (kind != "Deferred") Check("OmittedCountStaysNative", c.Count == 7);
            ExpectValues("OmittedValuesStillNotify", "1:start=32", "1:end=68", "1:count=7");

            callbacks = false;
            rerender();
            await Harness.Render();
            c.MoveBoth(42, 58);
            MoveCount(8);
            Expect("RemovedCallbacksStaySilent");
            callbacks = true;
            generation = 2;
            rerender();
            await Harness.Render();
            c.MoveBoth(43, 57);
            MoveCount(9);
            await Harness.Render();
            ExpectValues("ReenabledCallbacksAreCurrent", "2:start=43", "2:end=57", "2:count=9");

            rerenderOnEdit = false;
            c.QueueStart(44);
            end = 56;
            rerender();
            await Harness.Render();
            Check("PendingEditAndSiblingWriteApplied", c.RangeStart == 44 && c.RangeEnd == 56);
            raw.Clear();
            c.ValueChanged += observer;
            c.FlushEchoes();
            c.ValueChanged -= observer;
            Check("PendingEditNativePositiveControl", raw.SequenceEqual(new[] { "Start" }));
            Expect("PendingEditSurvivesSiblingWrite", "2:start=44");
            end = Optional<double>.Unset;

            c.Echo = RangeProbeEcho.Synchronous;
            c.CoerceSibling = true;
            start = 150;
            count = -5;
            rerender();
            await Harness.Render();
            Check("SetterAndSiblingCoercion", c.RangeStart == 100 && c.RangeEnd == 100);
            if (kind != "Deferred") Check("DifferentTypeCoercion", c.Count == 0);
            Expect("CoercionDoesNotEcho");
            c.MoveBoth(90, 95);
            MoveCount(1);
            ExpectValues("EditAfterCoercion", "2:start=90", "2:end=95", "2:count=1");

            c.CoerceSibling = false;
            c.Echo = RangeProbeEcho.Deferred;
            start = 10;
            end = 90;
            count = 10;
            rerender();
            await Harness.Render();
            c.FlushEchoes();
            Expect("DeferredEchoesDoNotNotify");
            start = 12;
            end = 88;
            rerender();
            await Harness.Render();
            c.MoveStart(13);
            c.FlushEchoes();
            Expect("UserEditBeforeDeferredEcho", "2:start=13");

            if (kind == "Distinct")
            {
                ReactorBinding.WriteSuppressed(c, () => c.MoveStart(17));
                ReactorBinding.WriteSuppressed(c, () => c.MoveEnd(83));
            }
            else
                ReactorBinding.WriteSuppressed(c, () => c.MoveBoth(17, 83));
            Expect("PublicWriteSuppressed");

            setters = new Action<ControlledRangeProbe>[] { p => p.MoveBoth(19, 81) };
            rerender();
            await Harness.Render();
            c.FlushEchoes();
            Expect("ApplySettersScope");
            Check("OneSubscriptionPerEvent", kind switch
            {
                "Distinct" => c.ValueSubscriptions == 0 && c.StartSubscriptions == 1 && c.EndSubscriptions == 1,
                "Deferred" => c.ValueSubscriptions == 1 && c.StartSubscriptions == 1 && c.EndSubscriptions == 1,
                _ => c.ValueSubscriptions == 1 && c.StartSubscriptions == 0 && c.EndSubscriptions == 0,
            });
            Check("OrdinaryEventSubscriptionUnchanged", c.PingSubscriptions == 1);
            if (kind != "Deferred")
                Check("CountSubscriptionUnchanged", c.CountSubscriptions == (kind == "Mixed" ? 0 : 1));
            c.Ping();
            Expect("OrdinaryEventAfterManyRenders", "2:ping");

            visible = false;
            rerender();
            await Harness.Render();
            Check("ControlledSubscriptionsRemoved", c.ValueSubscriptions == 0 &&
                c.StartSubscriptions == 0 && c.EndSubscriptions == 0 && c.CountSubscriptions == 0);
            c.MoveBoth(21, 79);
            Expect("NoCallbacksAfterUnmount");

            visible = true;
            callbacks = false;
            setters = Array.Empty<Action<ControlledRangeProbe>>();
            start = 20;
            end = 80;
            rerender();
            await Harness.Render();
            c = H.FindControl<ControlledRangeProbe>(_ => true)
                ?? throw new InvalidOperationException("Range probe did not remount");
            c.MoveBoth(22, 78);
            Expect("MountedWithoutCallbacks");
            callbacks = true;
            rerender();
            await Harness.Render();
            c.MoveBoth(23, 77);
            ExpectTogether("CallbacksAddedAfterMount", "2:start=23", "2:end=77");

            void MoveCount(int value) { if (kind != "Deferred") c.MoveCount(value); }
            void Check(string name, bool pass) => H.Check($"MultipleControlled_{kind}_{name}", pass);
            void ExpectValues(string name, params string[] expected) =>
                ExpectTogether(name, kind == "Deferred" ? expected.Where(x => !x.Contains(":count=")).ToArray() : expected);
            void ExpectTogether(string name, params string[] expected)
            {
                log.Sort(StringComparer.Ordinal);
                Expect(name, expected.Order(StringComparer.Ordinal).ToArray());
            }
            void Expect(string name, params string[] expected)
            {
                H.Check($"MultipleControlled_{kind}_{name}", log.SequenceEqual(expected),
                    $"expected [{string.Join(", ", expected)}], actual [{string.Join(", ", log)}]");
                log.Clear();
            }
        }
    }

    internal sealed class Reentrant(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await RunCase("NestedValue", 40, ["end=70", "start=40"]);
            await RunCase("RoundTrip", 30, ["end=70", "start=40", "start=30"]);
            await RunCase("OtherProperty", 30, ["end=70", "count=9", "start=30"]);
            await RunCase("QueuedValue", 40, ["end=70", "start=40"]);
            await RunCase("ReplaceCallback", 30, ["end=70", "new-start=30"]);
            await RunCase("Unmount", 20, ["end=70"]);
        }

        private async Task RunCase(string name, double expectedStart, string[] expected)
        {
            using var reconciler = new Reconciler();
            var log = new List<string>();
            double start = 20, end = 80;
            int count = 2;
            ControlledRangeProbe? control = null;
            Element? element = null;
            Action<double> onStart = value => { start = value; log.Add($"start={value}"); };

            Element Create() => name == "OtherProperty"
                ? new MixedRangeProbeElement
                {
                    RangeStart = start, RangeEnd = end, Count = count,
                    OnRangeStartChanged = onStart, OnRangeEndChanged = OnEnd,
                    OnCountChanged = value => { count = value; log.Add($"count={value}"); },
                }
                : new BareRangeProbeElement
                {
                    RangeStart = start, RangeEnd = end,
                    OnRangeStartChanged = onStart, OnRangeEndChanged = OnEnd,
                };

            void OnEnd(double value)
            {
                end = value;
                log.Add($"end={value}");
                var c = control ?? throw new InvalidOperationException("Reentrant callback before mount");
                switch (name)
                {
                    case "NestedValue":
                        c.MoveStart(40);
                        break;
                    case "RoundTrip":
                        c.MoveStart(40);
                        c.MoveStart(30);
                        break;
                    case "OtherProperty":
                        c.MoveCount(9);
                        break;
                    case "QueuedValue":
                        c.QueueStart(40);
                        break;
                    case "ReplaceCallback":
                        start = c.RangeStart;
                        onStart = v => { start = v; log.Add($"new-start={v}"); };
                        var updated = Create();
                        reconciler.Reconcile(element, updated, c, static () => { });
                        element = updated;
                        break;
                    case "Unmount":
                        reconciler.UnmountChild(c);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown reentrant case {name}");
                }
            }

            element = Create();
            control = reconciler.Mount(element, static () => { }) as ControlledRangeProbe
                ?? throw new InvalidOperationException("Reentrant probe did not mount");
            H.SetContent(control);
            await Harness.Render();
            log.Clear();
            control.MoveBoth(30, 70);
            if (name == "QueuedValue") control.FlushEchoes();

            if (name == "Unmount")
                H.Check($"MultipleControlled_Reentrant_{name}_Detached", control.ValueSubscriptions == 0);
            else
            {
                var accepted = Create();
                reconciler.Reconcile(element, accepted, control, static () => { });
                H.Check($"MultipleControlled_Reentrant_{name}_AcceptedValue",
                    start == expectedStart && control.RangeStart == expectedStart && control.RangeEnd == 70 &&
                    (name != "OtherProperty" || count == 9 && control.Count == 9),
                    $"parent={start}, native={control.RangeStart}, expected={expectedStart}");
                reconciler.UnmountChild(control);
            }
            H.Check($"MultipleControlled_Reentrant_{name}_Callbacks", log.SequenceEqual(expected),
                $"expected [{string.Join(", ", expected)}], actual [{string.Join(", ", log)}]");
            H.SetContent(new global::Microsoft.UI.Xaml.Controls.Grid());
        }
    }

    internal sealed class SingleWithEvent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<string>();
            Action rerender = () => throw new InvalidOperationException("Not mounted");
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, bump) = ctx.UseReducer(0);
                rerender = () => bump(n => n + 1);
                return new SingleRangeWithEventElement
                {
                    RangeStart = 20,
                    OnRangeStartChanged = value => log.Add($"{tick}:start={value}"),
                    OnPinged = _ => log.Add($"{tick}:ping"),
                }.Width(200 + tick);
            });
            await Harness.Render();
            var c = H.FindControl<ControlledRangeProbe>(_ => true)
                ?? throw new InvalidOperationException("Single probe did not mount");
            c.MoveStart(30);
            c.Ping();
            rerender();
            await Harness.Render();
            c.MoveStart(40);
            c.Ping();
            H.Check("MultipleControlled_SingleWithEvent_Callbacks",
                log.SequenceEqual(new[] { "0:start=30", "0:ping", "1:start=40", "1:ping" }));
            H.Check("MultipleControlled_SingleWithEvent_Subscriptions",
                c.ValueSubscriptions == 1 && c.PingSubscriptions == 1);
        }
    }
}
