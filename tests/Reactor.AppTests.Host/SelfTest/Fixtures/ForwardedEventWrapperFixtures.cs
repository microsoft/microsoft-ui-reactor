using Microsoft.UI.Reactor.Core;
using Reactor.External.TestControl;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ForwardedEventWrapperFixtures
{
    internal sealed class Single(Harness h) : Scenario(h, "Single");
    internal sealed class Deferred(Harness h) : Scenario(h, "Deferred");
    internal sealed class Ordinary(Harness h) : Scenario(h, "Ordinary");
    internal sealed class Combined(Harness h) : Scenario(h, "Combined");

    internal abstract class Scenario(Harness h, string kind) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<string>();
            ForwardedEventArgs? received = null;
            var generation = 0;
            var callbacks = true;
            var visible = true;
            Action rerender = () => throw new InvalidOperationException("Not mounted");
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, bump) = ctx.UseReducer(0);
                rerender = () => bump(n => n + 1);
                if (!visible) return Factories.TextBlock("Removed");
                var current = generation;
                Action<int>? value = callbacks ? v => log.Add($"{current}:value={v}") : null;
                Action<int>? other = callbacks ? v => log.Add($"{current}:other={v}") : null;
                Action<ForwardedEventArgs>? ping = callbacks ? e =>
                {
                    received = e;
                    log.Add($"{current}:ping={e.Sequence}");
                } : null;
                Element element = kind switch
                {
                    "Single" => new ForwardedSingleElement { Value = 10, OnValueChanged = value },
                    "Deferred" => new ForwardedDeferredElement { Value = 10, OnValueChanged = value },
                    "Ordinary" => new ForwardedOrdinaryElement { OnPinged = ping },
                    _ => new ForwardedCombinedElement
                    {
                        Value = 10, Other = 110, OnValueChanged = value,
                        OnOtherChanged = other, OnPinged = ping,
                    },
                };
                return element.Width(200 + tick);
            });
            await Harness.Render();
            var c = Find();
            Expect("MountDoesNotNotify");
            Check("ChildIsUntagged", Reconciler.GetElementTag((Xaml.FrameworkElement)c.Child) is null);

            var raw = 0;
            object? nativeSender = null;
            ForwardedEventArgs? nativeArgs = null;
            EventHandler<ForwardedEventArgs> observer = (s, e) =>
            {
                raw++;
                nativeSender = s;
                nativeArgs = e;
            };
            if (kind == "Ordinary") c.Pinged += observer;
            else c.Changed += observer;
            var first = new ForwardedEventArgs(1);
            Fire(c.Child, first, 20);
            Check("NativePositiveControl", raw == 1 && ReferenceEquals(nativeSender, c.Child) &&
                ReferenceEquals(nativeArgs, first) && (kind == "Ordinary" || c.Value == 20));
            if (kind == "Ordinary") c.Pinged -= observer;
            else c.Changed -= observer;
            ExpectEvent("ForwardedChild", 20, 1);

            generation = 1;
            rerender();
            await Harness.Render();
            Check("SameControl", ReferenceEquals(c, Find()));
            if (kind != "Ordinary") Check("ParentReasserted", c.Value == 10);
            Expect("ParentWriteDoesNotEcho");
            foreach (var sender in new object?[] { null, new object(), c.Child })
            {
                var args = new ForwardedEventArgs(2);
                Fire(sender, args, 30);
                ExpectEvent("CurrentCallback_" + (sender?.GetType().Name ?? "null"), 30, 2);
                if (kind == "Ordinary" || kind == "Combined")
                    Check("OriginalArgs", ReferenceEquals(received, args));
                // Combined dispatch only notifies changed values.
                if (kind == "Combined") c.Edit(10, c.Child, new(0));
                log.Clear();
            }

            if (kind != "Ordinary")
            {
                ReactorBinding.WriteSuppressed(c, () => c.Edit(40, null, new(3)));
                Expect("OwnerSuppression");
                c.Edit(41, new object(), new(4));
                ExpectValues("NextEditIsNotSuppressed", 41);
            }

            callbacks = false;
            rerender();
            await Harness.Render();
            log.Clear();
            Fire(c.Child, new(5), 50);
            Expect("RemovedCallbacks");
            callbacks = true;
            generation = 2;
            rerender();
            await Harness.Render();
            Expect("ReenableDoesNotEcho");
            Fire(c.Child, new(6), 60);
            ExpectEvent("ReenabledCurrentCallbacks", 60, 6);
            Check("OneSubscription", c.ChangedSubscriptions == (kind == "Ordinary" ? 0 : 1) &&
                c.PingSubscriptions == (kind == "Ordinary" || kind == "Combined" ? 1 : 0));

            visible = false;
            rerender();
            await Harness.Render();
            if (kind == "Combined") Check("CombinedUnsubscribes", c.ChangedSubscriptions == 0);
            Fire(c.Child, new(7), 70);
            Expect("NoCallbacksAfterUnmount");
            visible = true;
            rerender();
            await Harness.Render();
            c = Find();
            Fire(c.Child, new(8), 80);
            ExpectEvent("RemountedCallbacks", 80, 8);
            Check("RemountSubscriptionCount", c.ChangedSubscriptions == (kind == "Ordinary" ? 0 : 1) &&
                c.PingSubscriptions == (kind == "Ordinary" || kind == "Combined" ? 1 : 0));

            ForwardedEventProbe Find() => H.FindControl<ForwardedEventProbe>(_ => true)
                ?? throw new InvalidOperationException("Forwarding probe did not mount");
            void Fire(object? sender, ForwardedEventArgs args, int value)
            {
                if (kind != "Ordinary") c.Edit(value, sender, args);
                if (kind == "Ordinary" || kind == "Combined") c.Ping(sender, args);
            }
            void Check(string name, bool pass) => H.Check($"ForwardedEvent_{kind}_{name}", pass);
            void ExpectEvent(string name, int value, int sequence)
            {
                var expected = new List<string>();
                if (kind != "Ordinary") expected.Add($"{generation}:value={value}");
                if (kind == "Combined") expected.Add($"{generation}:other={value + 100}");
                if (kind == "Ordinary" || kind == "Combined") expected.Add($"{generation}:ping={sequence}");
                Expect(name, expected.ToArray());
            }
            void ExpectValues(string name, int value) =>
                Expect(name, kind == "Combined"
                    ? new[] { $"{generation}:value={value}", $"{generation}:other={value + 100}" }
                    : new[] { $"{generation}:value={value}" });
            void Expect(string name, params string[] expected)
            {
                H.Check($"ForwardedEvent_{kind}_{name}",
                    log.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)),
                    $"expected [{string.Join(", ", expected)}], actual [{string.Join(", ", log)}]");
                log.Clear();
            }
        }
    }
}
