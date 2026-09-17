using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Proves the <c>.ApplyStyle(...)</c> mount action actually resolves WinUI named
/// styles against the live application resources, and that each type-ramp factory
/// is pointed at the right key.
///
/// <para>
/// The unit layer cannot cover this: a headless test cannot construct a
/// <c>TextBlock</c> or read <c>Application.Current.Resources</c>, so it can only
/// assert that <i>an</i> OnMount action exists. Everything about whether the key
/// resolves — and to which style — is only observable against a real control.
/// </para>
///
/// <para>
/// This is also the regression guard for the resolution mechanism itself.
/// <c>ApplyStyle</c> resolves through <c>ResourceLookup.TryFind</c>, which walks
/// merged dictionaries; the type-ramp styles live in <c>XamlControlsResources</c>.
/// Because an unresolved key no longer throws, a lookup regression would be
/// invisible without an assertion on the resulting font size — a "mounted without
/// throwing" check would pass either way.
/// </para>
/// </summary>
internal static class NamedStyleResolutionFixture
{
    // WinUI 3 type ramp (Windows App SDK generic.xaml). Size alone does not
    // identify a style — Body and BodyStrong are both 14px — so each row also
    // pins FontWeight. Together the pair is unique per row, which is what makes
    // this a real "right key" guard: swap any two keys and the pair reddens.
    private static readonly (string Label, double FontSize, ushort Weight)[] Ramp =
    [
        ("ramp-Body", 14, 400),
        ("ramp-BodyStrong", 14, 600),
        ("ramp-BodyLarge", 18, 400),
        ("ramp-Subtitle", 20, 600),
        ("ramp-Title", 28, 600),
        ("ramp-TitleLarge", 40, 600),
        ("ramp-Display", 68, 600),
    ];

    /// <summary>
    /// Every type-ramp factory resolves its named style and lands the expected
    /// font size on the real control. Deleting the <c>fe.Style = style</c>
    /// assignment in <c>ApplyNamedStyle</c>, or breaking merged-dictionary
    /// lookup, drops every element to the theme default (14px) and fails all
    /// rows except the two that are legitimately 14px.
    /// </summary>
    internal class TypeRampStylesResolve(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                Body("ramp-Body"),
                BodyStrong("ramp-BodyStrong"),
                BodyLarge("ramp-BodyLarge"),
                Subtitle("ramp-Subtitle"),
                Title("ramp-Title"),
                TitleLarge("ramp-TitleLarge"),
                Display("ramp-Display")));

            await Harness.Render();

            foreach (var (label, expected, expectedWeight) in Ramp)
            {
                var tb = H.FindText(label);
                H.Check($"NamedStyle_{label}_Mounted", tb is not null);

                // A Style must actually be attached — the direct negative
                // control for a lookup that silently found nothing.
                H.Check($"NamedStyle_{label}_HasStyle", tb?.Style is not null);

                H.Check($"NamedStyle_{label}_FontSize{expected}",
                    tb is not null && Math.Abs(tb.FontSize - expected) < 0.01);

                // Distinguishes styles that share a size — without this,
                // swapping the Body and BodyStrong keys stays green.
                H.Check($"NamedStyle_{label}_Weight{expectedWeight}",
                    tb is not null && tb.FontWeight.Weight == expectedWeight);
            }
        }
    }

    /// <summary>
    /// Positive control for the fixture above. The two keys this PR added are
    /// the ones with no prior coverage anywhere, and they are the ones most
    /// likely to be wrong (a typo yields a silent 14px fallback now that
    /// unresolved keys no longer throw). Assert they differ from the default
    /// AND from each other, so neither "both fell back" nor "both point at the
    /// same key" can pass.
    /// </summary>
    internal class NewRampKeysAreDistinctAndNonDefault(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                TextBlock("ramp-plain"),
                TitleLarge("ramp-large"),
                Display("ramp-display")));

            await Harness.Render();

            var plain = H.FindText("ramp-plain");
            var large = H.FindText("ramp-large");
            var display = H.FindText("ramp-display");

            H.Check("NamedStyle_NewKeys_AllMounted",
                plain is not null && large is not null && display is not null);

            // Establishes that an unstyled TextBlock really is the 14px default
            // in this host, so "differs from default" below means something.
            H.Check("NamedStyle_PlainIsThemeDefault",
                plain is not null && Math.Abs(plain.FontSize - 14) < 0.01);

            H.Check("NamedStyle_TitleLarge_DiffersFromDefault",
                large is not null && plain is not null
                    && large.FontSize > plain.FontSize + 1);
            H.Check("NamedStyle_Display_DiffersFromDefault",
                display is not null && plain is not null
                    && display.FontSize > plain.FontSize + 1);
            H.Check("NamedStyle_TitleLarge_DiffersFromDisplay",
                large is not null && display is not null
                    && Math.Abs(large.FontSize - display.FontSize) > 1);
        }
    }

    /// <summary>
    /// An unresolved key degrades instead of throwing: the element mounts, keeps
    /// the theme default, and the surrounding tree is unaffected. Before this
    /// behavior existed the missing key threw out of <c>OnMountAction</c>, which
    /// <c>Reconciler.ApplyModifiers</c> invokes unguarded, so the sibling below
    /// would never have rendered.
    /// </summary>
    internal class UnresolvedStyleKeyDoesNotBreakRender(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                TextBlock("bad-style-target").ApplyStyle("ReactorNoSuchStyleKey_SelfTest"),
                TextBlock("bad-style-sibling")));

            await Harness.Render();

            var target = H.FindText("bad-style-target");
            var sibling = H.FindText("bad-style-sibling");

            H.Check("NamedStyle_Unresolved_TargetStillMounts", target is not null);

            // The load-bearing assertion: the mount action threw before, which
            // aborted the whole subtree. A sibling rendered after the bad key
            // proves the render survived rather than merely that one element exists.
            H.Check("NamedStyle_Unresolved_SiblingStillRenders", sibling is not null);

            H.Check("NamedStyle_Unresolved_NoStyleApplied", target?.Style is null);
            H.Check("NamedStyle_Unresolved_KeepsThemeDefaultSize",
                target is not null && Math.Abs(target.FontSize - 14) < 0.01);
        }
    }

    /// <summary>
    /// The behaviour the PR is named for: an unresolved key is actually
    /// <i>reported</i>, on the release-visible ETW surface, naming the key —
    /// and only once per distinct key however many elements mount it.
    ///
    /// <para>
    /// The sibling fixture above deliberately does not cover this: deleting
    /// <c>WarnUnresolvedStyle</c> (or reverting <c>DiagnosticLog.Warning</c> to
    /// its DEBUG-only form) leaves every assertion there green, because
    /// "degrades instead of throwing" is indistinguishable from "does nothing
    /// at all". Subscribing to the trace is the only oracle that separates them.
    /// </para>
    /// </summary>
    internal class UnresolvedStyleKeyEmitsWarning(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // The once-per-key set is process-wide and never cleared, so a fixed
            // key could already have been consumed by an earlier fixture in this
            // run — which would make the "emitted" check pass or fail for reasons
            // unrelated to the code under test. A fresh key per run removes that.
            var key = "ReactorMissingStyle_" + global::System.Guid.NewGuid().ToString("N");

            var captured = new global::System.Collections.Generic.List<ReactorEvent>();
            var gate = new object();

            // Subscribing also flips ReactorEventSource.IsEnabled on, which is
            // what lets DiagnosticLog.Warning emit at all — the same gate a real
            // consumer (dotnet-trace, an ILogger bridge) trips.
            //
            // Verbose (not Warning) on purpose: reconcile/render events then flow
            // too, so `SubscriptionSawEvents` below is a control that stays green
            // when the product warning is removed. Filtering to Warning here
            // instead would make that control fail for the same reason as the
            // assertion it is supposed to be controlling for, proving nothing.
            using (ReactorTrace.Subscribe(
                e => { lock (gate) { captured.Add(e); } },
                EventLevel.Verbose))
            {
                var host1 = H.CreateHost();
                host1.Mount(_ => TextBlock("warn-first").ApplyStyle(key));
                await Harness.Render();

                // Second mount of the SAME key in a fresh host: proves the
                // dedupe, which is what keeps a virtualized list from emitting
                // one warning per realized item.
                var host2 = H.CreateHost();
                host2.Mount(_ => TextBlock("warn-second").ApplyStyle(key));
                await Harness.Render();
            }

            ReactorEvent[] snapshot;
            lock (gate) { snapshot = captured.ToArray(); }

            // Precondition that holds whether or not ETW is available: the
            // unresolved key degraded instead of throwing, so the product path
            // under test actually ran. Keeps a real red available if the
            // harness or the mount path breaks, rather than skipping blindly.
            var mounted = H.FindText("warn-second");
            H.Check("NamedStyle_Warning_ProductPathRan",
                mounted is not null && mounted.Style is null);

            // NativeAOT defaults the EventSourceSupport feature switch to false
            // (see tests/startup_perf/BlankReactorMsix/BlankReactor.csproj), so
            // the whole provider is inert there and no subscriber can observe
            // anything. Detect that by the absence of *any* event — Verbose is
            // subscribed above, so a live provider always delivers some — and
            // skip rather than redden the AOT run for a platform limitation.
            if (snapshot.Length == 0)
            {
                H.Skip("NamedStyle_Warning_Emitted",
                    "EventSource delivered no events at all; NativeAOT defaults EventSourceSupport=false, so ETW-based assertions cannot run in this configuration.");
                return;
            }

            var matching = new global::System.Collections.Generic.List<ReactorEvent>();
            foreach (var e in snapshot)
            {
                if (!string.Equals(e.EventName, "Warning", StringComparison.Ordinal))
                    continue;
                foreach (var p in e.Payload)
                {
                    if (p is string s && s.Contains(key, StringComparison.Ordinal))
                    {
                        matching.Add(e);
                        break;
                    }
                }
            }

            H.Check("NamedStyle_Warning_Emitted", matching.Count > 0);

            var first = matching.Count > 0 ? matching[0] : default;
            H.Check("NamedStyle_Warning_CategoryIsTheme",
                matching.Count > 0 && first.Payload.Count > 0
                    && first.Payload[0] as string == "Theme");
            H.Check("NamedStyle_Warning_OperationIsApplyStyle",
                matching.Count > 0 && first.Payload.Count > 1
                    && first.Payload[1] as string == "ApplyStyle");

            // Two mounts, one warning.
            H.Check("NamedStyle_Warning_OncePerKey", matching.Count == 1);
        }
    }

    /// <summary>
    /// The two overflow branches of the de-duplication, which the single-key
    /// fixture above cannot reach: past the entry cap warnings must keep coming
    /// rather than go silent, and an overlong key must be truncated on the
    /// payload and never retained (so the entry cap bounds retained memory, not
    /// just retained count).
    ///
    /// <para>
    /// Resets the process-wide style caches around itself — filling either would
    /// otherwise change what every later fixture in this process observes: the
    /// warned-key set changes de-duplication (including
    /// <c>UnresolvedStyleKeyEmitsWarning</c>), and saturating the applier cache
    /// pushes every later mount onto the uncached fallback.
    /// </para>
    /// </summary>
    internal class UnresolvedStyleWarningOverflowBranches(Harness h) : SelfTestFixtureBase(h)
    {
        // Mirrors StyleApplierCacheCap / MaxKeyChars in ElementExtensions.
        private const int Cap = 256;

        public override async Task RunAsync()
        {
            ElementExtensions.ResetStyleCachesForTesting();
            try
            {
                var run = global::System.Guid.NewGuid().ToString("N");
                var captured = new global::System.Collections.Generic.List<ReactorEvent>();
                var gate = new object();

                using (ReactorTrace.Subscribe(
                    e => { lock (gate) { captured.Add(e); } },
                    EventLevel.Verbose))
                {
                    // Fill the dedupe set to capacity with distinct misses.
                    var filler = H.CreateHost();
                    filler.Mount(_ =>
                    {
                        var children = new Element[Cap];
                        for (var i = 0; i < Cap; i++)
                            children[i] = TextBlock($"fill-{run}-{i}").ApplyStyle($"MissKey_{run}_{i}");
                        return VStack(children);
                    });
                    await Harness.Render();

                    // A *new* miss after the cap is full must still warn — going
                    // quiet here would hide a real typo behind 256 earlier ones.
                    var pastCapKey = $"PastCap_{run}";
                    var afterCap = H.CreateHost();
                    afterCap.Mount(_ => TextBlock($"past-cap-{run}").ApplyStyle(pastCapKey));
                    await Harness.Render();

                    // Real precondition, checked while this host is still the
                    // live one (see the sibling fixture): the past-cap miss
                    // degraded instead of throwing, so the branch under test ran
                    // even where ETW cannot be observed.
                    var pastCapEl = H.FindText($"past-cap-{run}");
                    H.Check("NamedStyle_Overflow_ProductPathRan",
                        pastCapEl is not null && pastCapEl.Style is null);

                    // An overlong key: truncated on the payload, and never
                    // retained, so it warns again on a second miss.
                    var longKey = "L" + new string('x', Cap + 64) + $"_{run}";
                    var longA = H.CreateHost();
                    longA.Mount(_ => TextBlock($"long-a-{run}").ApplyStyle(longKey));
                    await Harness.Render();
                    var longB = H.CreateHost();
                    longB.Mount(_ => TextBlock($"long-b-{run}").ApplyStyle(longKey));
                    await Harness.Render();

                    ReactorEvent[] snapshot;
                    lock (gate) { snapshot = captured.ToArray(); }

                    // NativeAOT defaults EventSourceSupport=false, so the
                    // provider is inert and no ETW assertion can run there.
                    if (snapshot.Length == 0)
                    {
                        H.Skip("NamedStyle_Overflow_PastCapStillWarns",
                            "EventSource delivered no events at all; NativeAOT defaults EventSourceSupport=false, so ETW-based assertions cannot run in this configuration.");
                        return;
                    }

                    H.Check("NamedStyle_Overflow_PastCapStillWarns",
                        CountWarningsContaining(snapshot, pastCapKey) >= 1);

                    // The full key must never reach the payload...
                    H.Check("NamedStyle_Overflow_LongKeyTruncated",
                        CountWarningsContaining(snapshot, longKey) == 0);
                    // ...but its bounded prefix must, so the message is still useful.
                    var prefix = longKey.Substring(0, Cap);
                    var longHits = CountWarningsContaining(snapshot, prefix);
                    H.Check("NamedStyle_Overflow_LongKeyPrefixEmitted", longHits >= 1);
                    // Not retained => not de-duplicated => both misses warned.
                    H.Check("NamedStyle_Overflow_LongKeyNotDeduplicated", longHits == 2);
                }
            }
            finally
            {
                ElementExtensions.ResetStyleCachesForTesting();
            }
        }

        private static int CountWarningsContaining(ReactorEvent[] events, string needle)
        {
            var n = 0;
            foreach (var e in events)
            {
                if (!string.Equals(e.EventName, "Warning", StringComparison.Ordinal))
                    continue;
                foreach (var p in e.Payload)
                {
                    if (p is string s && s.Contains(needle, StringComparison.Ordinal))
                    {
                        n++;
                        break;
                    }
                }
            }
            return n;
        }
    }

    /// <summary>
    /// Pins the two facts <c>ResourceLookup.TryFind</c> is built on, against real
    /// <c>ResourceDictionary</c> instances (which a headless unit test cannot
    /// construct).
    ///
    /// <para>
    /// The first is an assumption about WinUI, not about our code:
    /// <c>ResourceDictionary.TryGetValue</c> performs the whole XAML resource
    /// walk — merged dictionaries included, last-merged-wins. <c>TryFind</c>
    /// deliberately does not re-implement that walk, so if WinUI ever stops
    /// doing it these checks fail and say to reinstate a manual reverse walk.
    /// </para>
    ///
    /// <para>
    /// The second is ours: a key that resolves to the wrong type must report
    /// "not found" rather than being skipped in favour of some other entry,
    /// because that is what makes <c>ApplyStyle</c> warn instead of silently
    /// applying an unrelated style.
    /// </para>
    /// </summary>
    internal class ResourceLookupHonoursPrecedence(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            static Style StyleWith(double fontSize)
            {
                var s = new Style(typeof(WinUI.TextBlock));
                s.Setters.Add(new Setter(WinUI.TextBlock.FontSizeProperty, fontSize));
                return s;
            }

            var low = new ResourceDictionary();
            var high = new ResourceDictionary();
            var root = new ResourceDictionary();
            // Declaration order: low first, high last. XAML resolves merged
            // dictionaries in reverse, so `high` outranks `low`.
            root.MergedDictionaries.Add(low);
            root.MergedDictionaries.Add(high);

            low["Dup"] = StyleWith(11);
            high["Dup"] = StyleWith(99);

            // ── Assumption checks: WinUI's own walk ──────────────────────────
            // These are what license TryFind to be a single TryGetValue call.
            H.Check("ResourceLookup_WinUI_TryGetValue_TraversesMerged",
                root.TryGetValue("Dup", out _));
            H.Check("ResourceLookup_WinUI_TryGetValue_LastMergedWins",
                root.TryGetValue("Dup", out var raw) && raw is Style rs && FontSizeOf(rs) == 99);

            // ── Our behaviour on top of it ───────────────────────────────────
            var dupFound = ResourceLookup.TryFind<Style>(root, "Dup", out var dup);
            H.Check("ResourceLookup_Duplicate_Resolves", dupFound);
            H.Check("ResourceLookup_Duplicate_LastMergedWins",
                dupFound && FontSizeOf(dup!) == 99);

            // A key that resolves to a non-Style must report not-found. This is
            // the assertion that covers TryFind's own logic: return `true`
            // regardless of type and it fails.
            low["Shadowed"] = StyleWith(11);
            high["Shadowed"] = "not a style";
            H.Check("ResourceLookup_WrongTypeIsNotFound",
                !ResourceLookup.TryFind<Style>(root, "Shadowed", out var shadowed));
            H.Check("ResourceLookup_WrongType_YieldsNullValue", shadowed is null);

            // A dictionary's own entries outrank everything it merges.
            root["Own"] = StyleWith(42);
            high["Own"] = StyleWith(99);
            var ownFound = ResourceLookup.TryFind<Style>(root, "Own", out var own);
            H.Check("ResourceLookup_OwnEntryOutranksMerged",
                ownFound && FontSizeOf(own!) == 42);

            H.Check("ResourceLookup_Absent_IsNotFound",
                !ResourceLookup.TryFind<Style>(root, "NoSuchKeyAnywhere", out _));

            return Task.CompletedTask;
        }

        private static double FontSizeOf(Style style)
        {
            foreach (var setter in style.Setters)
            {
                if (setter is Setter s && s.Property == WinUI.TextBlock.FontSizeProperty)
                    return (double)s.Value;
            }
            return double.NaN;
        }
    }
}
