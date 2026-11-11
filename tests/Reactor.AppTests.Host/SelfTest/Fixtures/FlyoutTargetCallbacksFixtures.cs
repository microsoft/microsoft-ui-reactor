using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Numerics;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Issue #942: decorators must not replace their target's event identity.</summary>
internal static class FlyoutTargetCallbacksFixtures
{
    internal sealed class NestedLifecycleAcrossUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int warnings = 0;
            bool sawWarningControl = false;
            using var subscription = Microsoft.UI.Reactor.Diagnostics.ReactorTrace.Subscribe(
                e =>
                {
                    if (e.EventName == "Warning" && e.Payload.Count > 1)
                    {
                        if (Equals(e.Payload[1], "Flyout.WarningPositiveControl")) sawWarningControl = true;
                        if (Equals(e.Payload[1], "Flyout.NestedTarget")) Interlocked.Increment(ref warnings);
                    }
                }, global::System.Diagnostics.Tracing.EventLevel.Warning,
                Microsoft.UI.Reactor.Core.Diagnostics.ReactorEventSource.Keywords.Errors);
            Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.Warning(
                Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Reactor,
                "Flyout.WarningPositiveControl", "Flyout warning delivery positive control.");
            void CheckWarning(string name, bool condition)
            {
                if (!sawWarningControl && !global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
                    H.Skip(name, "NativeAOT EventSource delivery is disabled: a direct Warning positive control emitted no event. Ownership checks still run; warning delivery runs in JIT lanes.");
                else
                    H.Check(name, sawWarningControl && condition, $"positiveControl={sawWarningControl}; warnings={warnings}");
            }
            for (int outerKind = 0; outerKind < 3; outerKind++)
            for (int innerKind = 0; innerKind < 3; innerKind++)
            {
                var name = $"FlyoutOwnership_{outerKind}_{innerKind}";
                var cleanups = new int[4]; // Inner content/pass-through, outer content/pass-through.
                var onCleanup = Enumerable.Range(0, 4).Select(index => (Action)(() => cleanups[index]++)).ToArray();
                var opened = new List<int>();
                var closed = new List<int>();
                int innerOpened = 0;
                using var reconciler = new Reconciler();
                Element Build(int innerVersion, int outerVersion)
                {
                    Element Wrap(Element target, int kind, bool inner)
                    {
                        string text = inner ? $"inner-{innerVersion}" : $"outer-{outerVersion}";
                        int cleanupIndex = inner ? 0 : 2;
                        return kind switch
                        {
                            0 => Flyout(target, Component<LifecycleContent, (string, Action)>(
                                (text, onCleanup[cleanupIndex]))) with
                            {
                                OverlayInputPassThroughElement = Component<CleanupProbe, Action>(
                                    onCleanup[cleanupIndex + 1]),
                                AreOpenCloseAnimationsEnabled = false,
                                OnOpened = inner
                                    ? (innerVersion == 0 ? null : () => innerOpened++)
                                    : () => opened.Add(outerVersion),
                                OnClosed = inner ? null : () => closed.Add(outerVersion),
                            },
                            1 => MenuFlyout(target, MenuItem(text)),
                            _ => CommandBarFlyout(target, primaryCommands: [AppBarButton(text)]),
                        };
                    }
                    return Wrap(Wrap(Button(name).Size(150, 60), innerKind, inner: true), outerKind, inner: false);
                }

                int warningsBefore = warnings;
                var current = Build(0, 0);
                var button = (Button)reconciler.Mount(current, static () => { })!;
                H.SetContent(button);
                await Harness.Render();
                var originalFlyout = button.Flyout;
                try
                {
                    CheckWarning(name + "_NestedWarning", warnings == warningsBefore + 1);
                    H.Check(name + "_InnerCleanupOnReplacement", cleanups[0] == (innerKind == 0 ? 1 : 0)
                        && cleanups[1] == (innerKind == 0 ? 1 : 0));
                    if (outerKind == 0 && innerKind == 0)
                    {
                        var oldInner = (FlyoutElement)Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.DecoratorTarget(current)!;
                        var nextInner = (FlyoutElement)Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.DecoratorTarget(Build(1, 0))!;
                        new Microsoft.UI.Reactor.Core.V1Protocol.Handlers.FlyoutHandler().Update(
                            new UpdateContext(reconciler, static () => { }), oldInner, nextInner, button);
                        // Observe the inner update before an outer update can repair its content.
                        H.Check(name + "_InnerUpdateLeavesOuterContent", ReferenceEquals(originalFlyout, button.Flyout)
                            && FlyoutText(button.Flyout) == "outer-0");
                        Reconciler.SetDecoratorElementTag(button, current);
                    }
                    for (int phase = 0; phase < 3; phase++)
                    {
                        if (phase > 0)
                        {
                            var next = Build(1, phase == 1 ? 0 : 1);
                            reconciler.Update(current, next, button, static () => { });
                            current = next;
                            await Harness.Render();
                        }
                        var flyout = button.Flyout;
                        H.Check(name + $"_Phase{phase}_SameOuterInstance", ReferenceEquals(originalFlyout, flyout));
                        H.Check(name + $"_Phase{phase}_OuterContent", FlyoutText(flyout) == $"outer-{(phase < 2 ? 0 : 1)}");
                        H.Check(name + $"_Phase{phase}_NoEarlyOuterCleanup", cleanups[2] == 0 && cleanups[3] == 0);
                        if (flyout is null) continue;
                        flyout.AreOpenCloseAnimationsEnabled = false;
                        flyout.ShowAt(button);
                        H.Check(name + $"_Phase{phase}_Opened", await Harness.WaitFor(() => flyout.IsOpen));
                        await Harness.Render(50);
                        if (outerKind == 0)
                            H.Check(name + $"_Phase{phase}_OpenedOnce", opened.SequenceEqual(
                                Enumerable.Range(0, phase + 1).Select(p => p < 2 ? 0 : 1)), $"opened: {string.Join(",", opened)}");
                        flyout.Hide();
                        await Harness.WaitFor(() => !flyout.IsOpen);
                        await Harness.Render(50);
                        if (outerKind == 0)
                            H.Check(name + $"_Phase{phase}_ClosedOnce", closed.SequenceEqual(
                                Enumerable.Range(0, phase + 1).Select(p => p < 2 ? 0 : 1)), $"closed: {string.Join(",", closed)}");
                        H.Check(name + $"_Phase{phase}_InnerNeverOpened", innerOpened == 0);
                    }

                    // Reuse the outer instance so exactly this layer takes the skip path.
                    int skipped = reconciler.DebugElementsSkipped;
                    reconciler.Update(current, current, button, static () => { });
                    H.Check(name + "_ShallowSkipReached", reconciler.DebugElementsSkipped == skipped + 1);
                    H.Check(name + "_ShallowSkipKeepsDecorator", ReferenceEquals(Reconciler.GetMountedElement(button), current));
                    // Re-keying changes the published tag, not the retained virtual tree.
                    reconciler.RefreshItemIdentityOnSkip(button, current.WithKey("recycled-row"));
                    var keyedTag = Reconciler.GetMountedElement(button);
                    skipped = reconciler.DebugElementsSkipped;
                    reconciler.Update(current, current, button, static () => { });
                    H.Check(name + "_SkipAfterRekeyKeepsPublishedTag", reconciler.DebugElementsSkipped == skipped + 1
                        && ReferenceEquals(Reconciler.GetMountedElement(button), keyedTag));
                    if (outerKind == 0)
                    {
                        int opensBefore = opened.Count, closesBefore = closed.Count;
                        button.Flyout!.ShowAt(button);
                        await Harness.Render(50);
                        button.Flyout.Hide();
                        await Harness.Render(50);
                        H.Check(name + "_RekeyKeepsCallbacks", opened.Count == opensBefore + 1 && opened[^1] == 1
                            && closed.Count == closesBefore + 1 && closed[^1] == 1);
                    }
                    var final = Build(2, 2);
                    reconciler.Update(current, final, button, static () => { });
                    current = final;
                    H.Check(name + "_UpdateAfterSkipAndRekey", ReferenceEquals(originalFlyout, button.Flyout)
                        && FlyoutText(button.Flyout) == "outer-2");

                    if (current is FlyoutElement outerFlyout)
                    {
                        var withoutCallbacks = outerFlyout with { OnOpened = null, OnClosed = null };
                        reconciler.Update(current, withoutCallbacks, button, static () => { });
                        var withCallbacks = withoutCallbacks with
                        {
                            OnOpened = () => opened.Add(3),
                            OnClosed = () => closed.Add(3),
                        };
                        reconciler.Update(withoutCallbacks, withCallbacks, button, static () => { });
                        current = withCallbacks;
                        int opensBefore = opened.Count, closesBefore = closed.Count;
                        button.Flyout!.ShowAt(button);
                        await Harness.Render(50);
                        button.Flyout.Hide();
                        await Harness.Render(50);
                        H.Check(name + "_CallbacksReenabledOnce", opened.Count == opensBefore + 1 && opened[^1] == 3
                            && closed.Count == closesBefore + 1 && closed[^1] == 3);
                    }

                    // A stale inner teardown must leave the outer native object untouched.
                    var innerElement = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.DecoratorTarget(current)!;
                    var ctx = new UnmountContext(reconciler);
                    if (innerKind == 0)
                        new Microsoft.UI.Reactor.Core.V1Protocol.Handlers.FlyoutHandler().Unmount(ctx, (FlyoutElement)innerElement, button);
                    else if (innerKind == 1)
                        new Microsoft.UI.Reactor.Core.V1Protocol.Handlers.MenuFlyoutHandler().Unmount(ctx, (MenuFlyoutElement)innerElement, button);
                    else
                        new Microsoft.UI.Reactor.Core.V1Protocol.Handlers.CommandBarFlyoutHandler().Unmount(ctx, (CommandBarFlyoutElement)innerElement, button);
                    H.Check(name + "_InnerUnmountLeavesOuter", ReferenceEquals(originalFlyout, button.Flyout)
                        && FlyoutText(button.Flyout) == "outer-2");

                    reconciler.UnmountChild(button);
                    H.SetContent(null);
                    H.Check(name + "_CleanupsExactlyOnce", cleanups[0] == (innerKind == 0 ? 1 : 0)
                        && cleanups[1] == (innerKind == 0 ? 1 : 0)
                        && cleanups[2] == (outerKind == 0 ? 1 : 0) && cleanups[3] == (outerKind == 0 ? 1 : 0),
                        $"cleanups: {string.Join(",", cleanups)}");
                    H.Check(name + "_Detached", button.Flyout is null);
                    CheckWarning(name + "_WarningNotRepeatedOnUpdate", warnings == warningsBefore + 1);
                }
                finally
                {
                    button.Flyout?.Hide();
                    H.SetContent(null);
                }
            }
        }
    }

    internal sealed class TransparentTargetLifecycle(Harness h, bool registered) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            for (int outerKind = 0; outerKind < 3; outerKind++)
            {
                var name = $"FlyoutTransparent_{(registered ? "RegisterType" : "Memo")}_{outerKind}";
                int factoryCalls = 0, innerCleanups = 0, passThroughCleanups = 0, outerCleanups = 0, innerOpened = 0;
                var opened = new List<int>();
                var closed = new List<int>();
                Action innerCleanup = () => innerCleanups++;
                Action passThroughCleanup = () => passThroughCleanups++;
                Action outerCleanup = () => outerCleanups++;
                using var reconciler = new Reconciler();
                if (registered)
                    reconciler.RegisterType<ForwardedTargetElement, FrameworkElement>(
                        mount: static (r, element, render) => (FrameworkElement)r.Mount(element.Target, render)!,
                        update: static (r, oldEl, newEl, control, render) =>
                        {
                            if (r.CanUpdate(oldEl.Target, newEl.Target))
                                return r.Update(oldEl.Target, newEl.Target, control, render) as FrameworkElement;
                            r.UnmountChild(control);
                            return (FrameworkElement)r.Mount(newEl.Target, render)!;
                        });

                Element Build(int targetVersion, int contentVersion)
                {
                    Element Inner()
                    {
                        factoryCalls++;
                        Element target = targetVersion == 0 ? Button(name) : Border(TextBlock("replacement"));
                        return Flyout(target, Component<CleanupProbe, Action>(innerCleanup)) with
                        {
                            OverlayInputPassThroughElement = Component<CleanupProbe, Action>(passThroughCleanup),
                            OnOpened = () => innerOpened++,
                            AreOpenCloseAnimationsEnabled = false,
                        };
                    }
                    Element wrapped = registered
                        ? new ForwardedTargetElement(Inner().WithKey($"target-{targetVersion}"))
                        : Memo(targetVersion, Inner);
                    return outerKind switch
                    {
                        0 => Flyout(wrapped, Component<LifecycleContent, (string, Action)>(($"outer-{contentVersion}", outerCleanup))) with
                        {
                            OnOpened = () => opened.Add(contentVersion),
                            OnClosed = () => closed.Add(contentVersion),
                            AreOpenCloseAnimationsEnabled = false,
                        },
                        1 => MenuFlyout(wrapped, MenuItem($"outer-{contentVersion}")),
                        _ => CommandBarFlyout(wrapped, primaryCommands: [AppBarButton($"outer-{contentVersion}")]),
                    };
                }

                var current = Build(0, 0);
                var control = (FrameworkElement)reconciler.Mount(current, static () => { })!;
                var firstControl = control;
                var firstFlyout = Reconciler.GetFlyoutOnControl(control);
                H.SetContent(control);
                try
                {
                    for (int phase = 0; phase < 3; phase++)
                    {
                        if (phase > 0)
                        {
                            var next = Build(phase == 2 ? 1 : 0, phase);
                            var replacement = reconciler.Update(current, next, control, static () => { });
                            H.Check(name + $"_Phase{phase}_Replacement", (replacement is not null) == (phase == 2));
                            if (replacement is FrameworkElement updated) control = updated;
                            current = next;
                            H.SetContent(control);
                        }
                        await Harness.Render();
                        var flyout = Reconciler.GetFlyoutOnControl(control);
                        H.Check(name + $"_Phase{phase}_OuterContent", FlyoutText(flyout) == $"outer-{phase}");
                        H.Check(name + $"_Phase{phase}_CurrentDecorator", ReferenceEquals(Reconciler.GetMountedElement(control), current));
                        H.Check(name + $"_Phase{phase}_ControlIdentity", ReferenceEquals(firstControl, control) == (phase < 2));
                        H.Check(name + $"_Phase{phase}_FlyoutIdentity", ReferenceEquals(firstFlyout, flyout) == (phase < 2));
                        H.Check(name + $"_Phase{phase}_DisplacedCleanup", innerCleanups == (phase < 2 ? 1 : 2)
                            && passThroughCleanups == (phase < 2 ? 1 : 2));
                        H.Check(name + $"_Phase{phase}_OuterCleanup", outerCleanups == (outerKind == 0 && phase == 2 ? 1 : 0));
                        if (!registered)
                            H.Check(name + $"_Phase{phase}_FactoryCalls", factoryCalls == (phase < 2 ? 1 : 2));
                        if (flyout is not null)
                        {
                            flyout.AreOpenCloseAnimationsEnabled = false;
                            flyout.ShowAt(control);
                            H.Check(name + $"_Phase{phase}_Opened", await Harness.WaitFor(() => flyout.IsOpen));
                            await Harness.Render(50);
                            flyout.Hide();
                            await Harness.Render(50);
                        }
                        if (outerKind == 0)
                            H.Check(name + $"_Phase{phase}_CurrentCallbacks", opened.SequenceEqual(Enumerable.Range(0, phase + 1))
                                && closed.SequenceEqual(Enumerable.Range(0, phase + 1)) && innerOpened == 0);
                    }
                    H.Check(name + "_OldTargetDetached", Reconciler.GetFlyoutOnControl(firstControl) is null);
                    reconciler.UnmountChild(control);
                    H.Check(name + "_FinalCleanup", innerCleanups == 2 && passThroughCleanups == 2
                        && outerCleanups == (outerKind == 0 ? 2 : 0));
                    H.Check(name + "_FinalDetached", Reconciler.GetFlyoutOnControl(control) is null);
                }
                finally
                {
                    Reconciler.GetFlyoutOnControl(control)?.Hide();
                    H.SetContent(null);
                }
            }
        }
    }

    private sealed record ForwardedTargetElement(Element Target) : Element;

    internal sealed class KeyedFlipViewOwnerIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // An explicitly keyed view is the control: FlipView need not copy it to stamp the item key.
            foreach (bool explicitViewKey in new[] { true, false })
            for (int kind = 0; kind < 3; kind++)
            {
                var name = $"FlyoutFlipOwner_{(explicitViewKey ? "Explicit" : "Stamped")}_{kind}";
                int cleanups = 0, passThroughCleanups = 0;
                var opened = new List<int>();
                var closed = new List<int>();
                Action cleanup = () => cleanups++;
                Action passThroughCleanup = () => passThroughCleanups++;
                Action? advance = null;
                Action? remove = null;
                Element? rawView = null;
                var previousHost = ReactorApp.ActiveHostInternal;
                var target = new Border();
                H.SetContent(target);
                try
                {
                    using var host = new ReactorHost(H.Window) { ContentTarget = target };
                    host.Mount(ctx =>
                    {
                        var (version, setVersion) = ctx.UseState(0);
                        advance = () => setVersion(version + 1);
                        remove = () => setVersion(-1);
                        return FlipView(version < 0 ? Array.Empty<int>() : new[] { version }, _ => "page", (value, _) =>
                        {
                            Element view = kind switch
                            {
                                0 => Flyout(Button("page target"), Component<LifecycleContent, (string, Action)>(($"body-{value}", cleanup))) with
                                {
                                    OverlayInputPassThroughElement = Component<CleanupProbe, Action>(passThroughCleanup),
                                    OnOpened = () => opened.Add(value),
                                    OnClosed = () => closed.Add(value),
                                },
                                1 => MenuFlyout(Button("page target"), MenuItem($"body-{value}")),
                                _ => CommandBarFlyout(Button("page target"), primaryCommands: [AppBarButton($"body-{value}")]),
                            };
                            rawView = explicitViewKey ? view.WithKey("page") : view;
                            return rawView;
                        });
                    });
                    await host.WaitForIdleAsync();
                    await Harness.Render();
                    var flip = (FlipView)target.Child;
                    var button = (Button)flip.Items[0];
                    var flyout = Reconciler.GetFlyoutOnControl(button)!;
                    H.Check(name + "_StampedIdentity", Reconciler.GetMountedElement(button)?.Key == "page"
                        && ReferenceEquals(Reconciler.GetMountedElement(button), rawView) == explicitViewKey);

                    for (int phase = 0; phase < 2; phase++)
                    {
                        if (phase > 0)
                        {
                            advance!();
                            await host.WaitForIdleAsync();
                            await Harness.Render();
                        }
                        H.Check(name + $"_Phase{phase}_SameControlAndFlyout", ReferenceEquals(flip.Items[0], button)
                            && ReferenceEquals(Reconciler.GetFlyoutOnControl(button), flyout));
                        H.Check(name + $"_Phase{phase}_Content", FlyoutText(flyout) == $"body-{phase}",
                            $"content={FlyoutText(flyout)}");
                        H.Check(name + $"_Phase{phase}_CurrentTag", Reconciler.GetMountedElement(button)?.Key == "page"
                            && ReferenceEquals(Reconciler.GetMountedElement(button), rawView) == explicitViewKey);
                        flyout.AreOpenCloseAnimationsEnabled = false;
                        flyout.ShowAt(button);
                        H.Check(name + $"_Phase{phase}_Opens", await Harness.WaitFor(() => flyout.IsOpen));
                        await Harness.Render(50);
                        flyout.Hide();
                        await Harness.Render(50);
                        if (kind == 0)
                            H.Check(name + $"_Phase{phase}_LatestCallbacks", opened.SequenceEqual(Enumerable.Range(0, phase + 1))
                                && closed.SequenceEqual(Enumerable.Range(0, phase + 1)),
                                $"opened={string.Join(',', opened)} closed={string.Join(',', closed)}");
                    }
                    H.Check(name + "_NoEarlyCleanup", cleanups == 0 && passThroughCleanups == 0);
                    // Remove the page through FlipView's item reconciliation, keeping the parent mounted.
                    remove!();
                    await host.WaitForIdleAsync();
                    H.Check(name + "_PageRemoved", flip.Items.Count == 0);
                    H.Check(name + "_Detached", Reconciler.GetFlyoutOnControl(button) is null);
                    if (kind == 0)
                        H.Check(name + "_CleanupOnce", cleanups == 1 && passThroughCleanups == 1,
                            $"content={cleanups} passThrough={passThroughCleanups}");
                }
                finally
                {
                    ReactorApp.ActiveHostInternal = previousHost;
                    H.SetContent(null);
                }
            }
        }
    }

    internal sealed class MemoResourceRefreshOwnerIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string resourceKey = "FlyoutMemoOwnerBrush";
            var resources = new ResourceDictionary();
            Application.Current.Resources.MergedDictionaries.Add(resources);
            var previousHost = ReactorApp.ActiveHostInternal;
            try
            {
                for (int kind = 0; kind < 3; kind++)
                foreach (bool nested in new[] { false, true })
                {
                    resources[resourceKey] = new SolidColorBrush(Microsoft.UI.Colors.Red);
                    ThemeRef.InvalidateResolutionCache();
                    var name = $"FlyoutMemoOwner_{kind}_{(nested ? "Nested" : "Direct")}";
                    int factoryCalls = 0, outerVersion = 0, innerCleanups = 0, passThroughCleanups = 0, outerCleanups = 0;
                    var opened = new List<int>();
                    var closed = new List<int>();
                    var invoked = new List<int>();
                    int innerOpened = 0, innerInvoked = 0;
                    Action innerCleanup = () => innerCleanups++;
                    Action passThroughCleanup = () => passThroughCleanups++;
                    Action outerCleanup = () => outerCleanups++;
                    Element? factoryOutput = null;
                    var target = new Border();
                    H.SetContent(target);
                    using var host = new ReactorHost(H.Window) { ContentTarget = target };
                    using var isolation = ThemeResourceListeners.IsolateForTest(host);
                    Element Wrap(Element child, int generation, bool inner)
                    {
                        string text = inner ? "inner" : "outer";
                        Action invoke = () => { if (inner && nested) innerInvoked++; else invoked.Add(generation); };
                        return kind switch
                        {
                            0 => Flyout(child,
                                Component<ResourceLifecycleContent, (string, string, Action)>((text, resourceKey, inner ? innerCleanup : outerCleanup))) with
                            {
                                OverlayInputPassThroughElement = inner ? Component<CleanupProbe, Action>(passThroughCleanup) : null,
                                OnOpened = () => { if (inner && nested) innerOpened++; else opened.Add(generation); },
                                OnClosed = () => { if (!inner || !nested) closed.Add(generation); },
                            },
                            // Resolve inside the setter so the Memo factory does not read mutable resources.
                            1 => MenuFlyout(child, MenuItem(text, invoke)).Set(menu =>
                                menu.Items[0].Foreground = ThemeRef.Resolve(resourceKey, isDark: false)!),
                            _ => CommandBarFlyout(child, primaryCommands: [AppBarButton(text, invoke)]).Set(commands =>
                                ((AppBarButton)commands.PrimaryCommands[0]).Foreground = ThemeRef.Resolve(resourceKey, isDark: false)!),
                        };
                    }
                    host.Mount(_ =>
                    {
                        int outerGeneration = ++outerVersion;
                        var memo = Memo("stable", () =>
                        {
                            int generation = ++factoryCalls;
                            // Business output stays pure for this Memo key; only the Theme.Ref resource changes.
                            factoryOutput = Wrap(Button("resource target"), generation, inner: true);
                            return factoryOutput;
                        });
                        return nested ? Wrap(memo, outerGeneration, inner: false) : memo;
                    });
                    await host.WaitForIdleAsync();
                    await Harness.Render();
                    var button = (Button)target.Child;
                    var flyout = Reconciler.GetFlyoutOnControl(button)!;
                    var initialOutput = factoryOutput;
                    int initialCalls = factoryCalls;
                    for (int phase = 0; phase < 2; phase++)
                    {
                        if (phase > 0)
                        {
                            resources[resourceKey] = new SolidColorBrush(Microsoft.UI.Colors.Blue);
                            Theme.NotifyResourcesChanged();
                            await host.WaitForIdleAsync();
                            await Harness.Render();
                            H.Check(name + "_Reconstructed", factoryCalls > initialCalls && !ReferenceEquals(initialOutput, factoryOutput));
                        }
                        H.Check(name + $"_Phase{phase}_SameControlAndFlyout", ReferenceEquals(target.Child, button)
                            && ReferenceEquals(Reconciler.GetFlyoutOnControl(button), flyout));
                        H.Check(name + $"_Phase{phase}_OwnerContent", FlyoutText(flyout) == (nested ? "outer" : "inner"));
                        var brush = flyout switch
                        {
                            Flyout { Content: Border { Child: TextBlock text } } => text.Foreground,
                            MenuFlyout menu => menu.Items[0].Foreground,
                            CommandBarFlyout commands => ((AppBarButton)commands.PrimaryCommands[0]).Foreground,
                            _ => null,
                        };
                        var color = (brush as SolidColorBrush)?.Color;
                        H.Check(name + $"_Phase{phase}_ResourceColor", color == (phase == 0 ? Microsoft.UI.Colors.Red : Microsoft.UI.Colors.Blue),
                            $"color={color}");
                        flyout.AreOpenCloseAnimationsEnabled = false;
                        flyout.ShowAt(button);
                        H.Check(name + $"_Phase{phase}_Opens", await Harness.WaitFor(() => flyout.IsOpen));
                        await Harness.Render(50);
                        if (kind != 0)
                        {
                            var command = flyout is MenuFlyout menu ? (UIElement)menu.Items[0]
                                : (UIElement)((CommandBarFlyout)flyout).PrimaryCommands[0];
                            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(command);
                            var invoke = peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
                                as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider;
                            H.Check(name + $"_Phase{phase}_CommandInvokable", invoke is not null);
                            invoke?.Invoke();
                            await Harness.Render(50);
                        }
                        flyout.Hide();
                        await Harness.Render(50);
                        int currentGeneration = nested ? outerVersion : factoryCalls;
                        if (kind == 0)
                            H.Check(name + $"_Phase{phase}_LatestCallbacks", opened.Count == phase + 1 && closed.Count == phase + 1
                                && opened[^1] == currentGeneration && closed[^1] == currentGeneration && innerOpened == 0,
                                $"opened={string.Join(',', opened)} closed={string.Join(',', closed)} current={currentGeneration} innerOpened={innerOpened}");
                        else
                            H.Check(name + $"_Phase{phase}_LatestCommand", invoked.Count == phase + 1
                                && invoked[^1] == currentGeneration && innerInvoked == 0,
                                $"invoked={string.Join(',', invoked)} current={currentGeneration} innerInvoked={innerInvoked}");
                        H.Check(name + $"_Phase{phase}_DisplacedCleanup", innerCleanups == (kind == 0 && nested ? 1 : 0)
                            && passThroughCleanups == (kind == 0 && nested ? 1 : 0) && outerCleanups == 0,
                            $"inner={innerCleanups} passThrough={passThroughCleanups} outer={outerCleanups}");
                    }
                    host.Mount(_ => TextBlock("removed"));
                    await host.WaitForIdleAsync();
                    H.Check(name + "_Detached", Reconciler.GetFlyoutOnControl(button) is null);
                    H.Check(name + "_CleanupOnce", innerCleanups == (kind == 0 ? 1 : 0) && passThroughCleanups == (kind == 0 ? 1 : 0)
                        && outerCleanups == (kind == 0 && nested ? 1 : 0),
                        $"inner={innerCleanups} passThrough={passThroughCleanups} outer={outerCleanups}");
                }
            }
            finally
            {
                ReactorApp.ActiveHostInternal = previousHost;
                H.SetContent(null);
                Application.Current.Resources.MergedDictionaries.Remove(resources);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    internal sealed class MemoDirtyDescendantOwnerIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previousHost = ReactorApp.ActiveHostInternal;
            try
            {
                foreach (bool wrapped in new[] { false, true })
                {
                    var name = $"FlyoutMemoDirty_{(wrapped ? "OuterOnPanel" : "Direct")}";
                    var probe = new MemoDirtyProbe();
                    int factoryCalls = 0, cleanups = 0;
                    var opened = new List<int>();
                    Action cleanup = () => cleanups++;
                    var target = new Border();
                    H.SetContent(target);
                    using var host = new ReactorHost(H.Window) { ContentTarget = target };
                    host.Mount(_ =>
                    {
                        var memo = Memo("stable", () =>
                        {
                            int generation = ++factoryCalls;
                            return VStack(Component<MemoDirtyCounter, MemoDirtyProbe>(probe),
                                Flyout(Button("skip target"), Component<LifecycleContent, (string, Action)>(("unchanged", cleanup))) with
                                {
                                    OnOpened = () => opened.Add(generation),
                                    AreOpenCloseAnimationsEnabled = false,
                                });
                        });
                        // This outer flyout shares the panel, not the sibling Button's ownership.
                        return wrapped ? Flyout(memo, TextBlock("panel flyout")) : memo;
                    });
                    await host.WaitForIdleAsync();
                    await Harness.Render();
                    var button = H.FindButton("skip target")!;
                    var flyout = (Flyout)Reconciler.GetFlyoutOnControl(button)!;
                    for (int phase = 0; phase < 3; phase++)
                    {
                        if (phase > 0)
                        {
                            probe.Advance!();
                            await host.WaitForIdleAsync();
                            await Harness.Render();
                            H.Check(name + $"_Phase{phase}_DirtyCounterRendered", H.FindText($"counter:{phase}") is not null);
                            H.Check(name + $"_Phase{phase}_SkippedSibling", host.Reconciler.DebugElementsSkipped > 0);
                        }
                        H.Check(name + $"_Phase{phase}_FactoryCalls", factoryCalls == phase + 1);
                        H.Check(name + $"_Phase{phase}_SameFlyout", ReferenceEquals(Reconciler.GetFlyoutOnControl(button), flyout));
                        flyout.ShowAt(button);
                        H.Check(name + $"_Phase{phase}_Opens", await Harness.WaitFor(() => flyout.IsOpen));
                        await Harness.Render(50);
                        flyout.Hide();
                        await Harness.Render(50);
                        H.Check(name + $"_Phase{phase}_LatestCallback", opened.SequenceEqual(Enumerable.Range(1, phase + 1)),
                            $"opened={string.Join(',', opened)}");
                        H.Check(name + $"_Phase{phase}_NoEarlyCleanup", cleanups == 0);
                    }
                    host.Mount(_ => TextBlock("removed"));
                    await host.WaitForIdleAsync();
                    H.Check(name + "_CleanupOnce", cleanups == 1 && Reconciler.GetFlyoutOnControl(button) is null);
                }
            }
            finally
            {
                ReactorApp.ActiveHostInternal = previousHost;
                H.SetContent(null);
            }
        }
    }

    private sealed class MemoDirtyProbe { internal Action? Advance; }

    private sealed class MemoDirtyCounter : Component<MemoDirtyProbe>
    {
        public override Element Render()
        {
            var (value, setValue) = UseState(0);
            Props.Advance = () => setValue(value + 1);
            return TextBlock($"counter:{value}");
        }
    }

    internal sealed class MemoOwnerRestoresAfterThrow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string resourceKey = "FlyoutMemoThrowBrush";
            const string expectedError = "expected flyout target update failure";
            var resources = new ResourceDictionary { [resourceKey] = new SolidColorBrush(Microsoft.UI.Colors.Red) };
            Application.Current.Resources.MergedDictionaries.Add(resources);
            ThemeRef.InvalidateResolutionCache();
            try
            {
                using var reconciler = new Reconciler();
                bool throwOnUpdate = false;
                bool beforeRealization = false, insideRealization = true, afterRealization = false;
                int factoryCalls = 0, cleanups = 0;
                var opened = new List<int>();
                Action cleanup = () => cleanups++;
                reconciler.RegisterType<ForwardedTargetElement, FrameworkElement>(
                    mount: static (r, el, render) => (FrameworkElement)r.Mount(el.Target, render)!,
                    update: (r, oldEl, newEl, control, render) =>
                    {
                        if (throwOnUpdate)
                        {
                            beforeRealization = r.ReconstructingKeyedMemo;
                            try
                            {
                                using var realization = r.EnterRealization(null, control);
                                insideRealization = r.ReconstructingKeyedMemo;
                                throw new InvalidOperationException(expectedError);
                            }
                            finally { afterRealization = r.ReconstructingKeyedMemo; }
                        }
                        return r.Update(oldEl.Target, newEl.Target, control, render) as FrameworkElement;
                    });
                FlyoutElement? output = null;
                var memo = Memo("stable", () =>
                {
                    int generation = ++factoryCalls;
                    output = Flyout(new ForwardedTargetElement(Button("throw target")),
                        Component<ResourceLifecycleContent, (string, string, Action)>(("owned", resourceKey, cleanup))) with
                    {
                        OnOpened = () => opened.Add(generation),
                        AreOpenCloseAnimationsEnabled = false,
                    };
                    return output;
                });
                var button = (Button)reconciler.Reconcile(null, memo, null, static () => { })!;
                var original = output!;
                var flyout = (Flyout)Reconciler.GetFlyoutOnControl(button)!;
                H.SetContent(button);
                await Harness.Render();
                resources[resourceKey] = new SolidColorBrush(Microsoft.UI.Colors.Blue);
                ThemeRef.InvalidateResolutionCache();
                throwOnUpdate = true;
                bool caught = false;
                reconciler.RequestResourceRefresh();
                reconciler.BeginRootPass();
                try { reconciler.Reconcile(memo, memo, button, static () => { }); }
                catch (InvalidOperationException ex) when (ex.Message == expectedError) { caught = true; }
                finally { reconciler.EndRootPass(); }
                throwOnUpdate = false;
                H.Check("FlyoutMemoThrow_TargetThrew", caught && factoryCalls == 2);
                H.Check("FlyoutMemoThrow_RealizationContextRestored", beforeRealization && !insideRealization && afterRealization);

                // Outside reconstruction, a self-update of another decorator must still be rejected.
                var foreign = Flyout(original.Target, TextBlock("foreign"));
                new Microsoft.UI.Reactor.Core.V1Protocol.Handlers.FlyoutHandler().Update(
                    new UpdateContext(reconciler, static () => { }), foreign, foreign, button);
                H.Check("FlyoutMemoThrow_ForeignOwnerRejected", FlyoutText(flyout) == "owned"
                    && ReferenceEquals(Reconciler.GetMountedElement(button), original));
                H.Check("FlyoutMemoThrow_NoEarlyCleanup", cleanups == 0);

                reconciler.RequestResourceRefresh();
                reconciler.BeginRootPass();
                try { reconciler.Reconcile(memo, memo, button, static () => { }); }
                finally { reconciler.EndRootPass(); }
                var text = (flyout.Content as Border)?.Child as TextBlock;
                H.Check("FlyoutMemoThrow_RetryReusesFlyout", factoryCalls == 3
                    && ReferenceEquals(Reconciler.GetFlyoutOnControl(button), flyout));
                H.Check("FlyoutMemoThrow_RetryRefreshesResource", (text?.Foreground as SolidColorBrush)?.Color == Microsoft.UI.Colors.Blue);
                flyout.ShowAt(button);
                H.Check("FlyoutMemoThrow_RetryOpens", await Harness.WaitFor(() => flyout.IsOpen));
                await Harness.Render(50);
                flyout.Hide();
                await Harness.Render(50);
                H.Check("FlyoutMemoThrow_RetryUsesCurrentCallback", opened.SequenceEqual(new[] { 3 }));
                reconciler.UnmountChild(button);
                H.Check("FlyoutMemoThrow_CleanupOnce", cleanups == 1 && Reconciler.GetFlyoutOnControl(button) is null);
            }
            finally
            {
                H.SetContent(null);
                Application.Current.Resources.MergedDictionaries.Remove(resources);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    internal sealed class ForwardedUpdateThrowsAfterInner(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ControlRegistry.RegisterDecorator<CustomForwardedTargetElement>(static () => new CustomForwardedTargetHandler());
            foreach (var (innerKind, custom) in new[] { (0, false), (1, false), (2, false), (0, true) })
            foreach (bool skipInner in new[] { false, true })
            foreach (bool pooled in new[] { false, true })
            {
                var name = $"FlyoutForwardedThrow_{(custom ? "Custom_" : "")}{innerKind}_{(skipInner ? "Skip" : "Full")}_{(pooled ? "Pool" : "Unmount")}";
                const string expectedError = "expected failure after inner flyout update";
                using var reconciler = new Reconciler();
                int innerUpdates = 0, innerCleanups = 0, outerCleanups = 0, passThroughCleanups = 0;
                var clicks = new List<int>();
                var opened = new List<int>();
                var customUnmounts = new List<int>();
                Action innerCleanup = () => innerCleanups++;
                Action outerCleanup = () => outerCleanups++;
                Action passThroughCleanup = () => passThroughCleanups++;
                reconciler.RegisterType<ForwardedTargetElement, FrameworkElement>(
                    mount: static (r, el, render) => (FrameworkElement)r.Mount(el.Target, render)!,
                    update: (r, oldEl, newEl, control, render) =>
                    {
                        r.Update(oldEl.Target, newEl.Target, control, render);
                        innerUpdates++;
                        throw new InvalidOperationException(expectedError);
                    });
                Element Inner(int version)
                {
                    var button = Button(name, () => clicks.Add(version));
                    return innerKind switch
                    {
                        0 => Flyout(button, Component<CleanupProbe, Action>(innerCleanup)),
                        1 => MenuFlyout(button, MenuItem("inner")),
                        _ => CommandBarFlyout(button, primaryCommands: [AppBarButton("inner")]),
                    };
                }
                var retainedInner = Inner(0);
                FlyoutElement Build(int version)
                {
                    Element inner = skipInner ? retainedInner : Inner(version);
                    if (custom)
                        inner = new CustomForwardedTargetElement(inner, version, () => customUnmounts.Add(version));
                    return Flyout(new ForwardedTargetElement(inner),
                        Component<LifecycleContent, (string, Action)>(($"outer-{version}", outerCleanup))) with
                    {
                        OverlayInputPassThroughElement = Component<CleanupProbe, Action>(passThroughCleanup),
                        OnOpened = () => opened.Add(version),
                        AreOpenCloseAnimationsEnabled = false,
                    };
                }
                var current = Build(0);
                var button = (Button)reconciler.Reconcile(null, current, null, static () => { })!;
                var flyout = button.Flyout;
                if (pooled) reconciler.RefreshItemIdentityOnSkip(button, current.WithKey("recycled-row"));
                var installedTag = Reconciler.GetMountedElement(button);
                H.SetContent(button);
                await Harness.Render();
                bool caught = false;
                try { reconciler.Reconcile(current, Build(1), button, static () => { }); }
                catch (InvalidOperationException ex) when (ex.Message == expectedError) { caught = true; }
                H.Check(name + "_ThrewAfterInnerUpdate", caught && innerUpdates == 1);
                H.Check(name + "_InstalledOwnerPreserved", ReferenceEquals(button.Flyout, flyout)
                    && ReferenceEquals(Reconciler.GetMountedElement(button), installedTag));
                H.Check(name + "_NoEarlyCleanup", innerCleanups == (innerKind == 0 ? 1 : 0)
                    && outerCleanups == 0 && passThroughCleanups == 0 && customUnmounts.Count == 0);
                if (custom)
                {
                    H.Check(name + "_CustomTagUpdated", Reconciler.GetElementTag(button) is CustomForwardedTargetElement { Version: 1 });
                    // Custom decorators own the event tag, so open the native flyout directly.
                    flyout?.ShowAt(button);
                }
                else
                {
                    H.ClickButton(name);
                    H.Check(name + "_TargetCallbackUpdated", clicks.SequenceEqual(new[] { skipInner ? 0 : 1 }));
                }
                H.Check(name + "_OuterStillOpens", await Harness.WaitFor(() => flyout?.IsOpen == true));
                await Harness.Render(50);
                flyout?.Hide();
                await Harness.Render(50);
                H.Check(name + "_OuterCallbackStillWorks", opened.SequenceEqual(new[] { 0 }));
                if (pooled) reconciler.UnmountAndPool(button);
                else reconciler.UnmountChild(button);
                H.Check(name + "_OwnedSubtreesCleanedOnce", innerCleanups == (innerKind == 0 ? 1 : 0)
                    && outerCleanups == 1 && passThroughCleanups == 1,
                    $"inner={innerCleanups} outer={outerCleanups} passThrough={passThroughCleanups}");
                if (custom)
                    H.Check(name + "_CurrentCustomUnmountOnce", customUnmounts.SequenceEqual(new[] { 1 }));
                H.Check(name + "_Detached", Reconciler.GetFlyoutOnControl(button) is null);
                H.SetContent(null);
                if (pooled)
                {
                    var plain = Button(name + " reused", () => clicks.Add(2));
                    var reused = (Button)reconciler.Mount(plain, static () => { })!;
                    H.SetContent(reused);
                    await Harness.Render();
                    H.ClickButton(name + " reused");
                    int[] expectedClicks = custom ? [2] : [skipInner ? 0 : 1, 2];
                    H.Check(name + "_PlainButtonReuse", ReferenceEquals(reused, button)
                        && ReferenceEquals(Reconciler.GetMountedElement(reused), plain)
                        && Reconciler.GetFlyoutOnControl(reused) is null
                        && clicks.SequenceEqual(expectedClicks));
                    reconciler.UnmountChild(reused);
                    if (custom)
                        H.Check(name + "_CustomUnmountNotRepeated", customUnmounts.SequenceEqual(new[] { 1 }));
                    H.SetContent(null);
                }
            }
        }
    }

    private sealed record CustomForwardedTargetElement(Element Target, int Version, Action OnUnmount) : Element;

    private sealed class CustomForwardedTargetHandler : IDecoratorElementHandler<CustomForwardedTargetElement>
    {
        public UIElement Mount(MountContext ctx, CustomForwardedTargetElement element) => ctx.MountChild(element.Target)!;
        public UIElement Update(UpdateContext ctx, CustomForwardedTargetElement oldElement,
            CustomForwardedTargetElement newElement, UIElement control)
            => ctx.Reconciler.Update(oldElement.Target, newElement.Target, control, ctx.RequestRerender) ?? control;
        public V1UnmountDisposition Unmount(UnmountContext ctx, CustomForwardedTargetElement? element, UIElement control)
        {
            element?.OnUnmount();
            return V1UnmountDisposition.ContinueDefaultTraversal;
        }
        public Element? GetSourceTarget(CustomForwardedTargetElement element) => element.Target;
    }

    internal sealed class TransparentTargetMetadata(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ControlRegistry.RegisterDecorator<CustomForwardedTargetElement>(static () => new CustomForwardedTargetHandler());
            foreach (var (registered, custom) in new[] { (false, false), (true, false), (false, true), (true, true) })
            foreach (bool pooled in new[] { false, true })
            {
                var name = $"FlyoutTransparentMetadata_{(registered ? "RegisterType" : "Memo")}_{(custom ? "Custom_" : "")}{(pooled ? "Pool" : "Unmount")}";
                var targetRef = new ElementRef();
                var customRef = new ElementRef();
                int refClears = 0, customRefClears = 0, customUnmounts = 0, contentCleanups = 0;
                int trackersBefore = Reconciler.InteractionTrackerCountForTests;
                var targetCurve = Curve.Linear(600);
                using var reconciler = new Reconciler();
                if (registered)
                    reconciler.RegisterType<ForwardedTargetElement, FrameworkElement>(
                        mount: static (r, el, render) => (FrameworkElement)r.Mount(el.Target, render)!,
                        update: static (r, oldEl, newEl, control, render) => r.Update(oldEl.Target, newEl.Target, control, render) as FrameworkElement);
                var target = Button(name).Ref(targetRef).ConnectedAnimation(name + "_target")
                    .Transition(Animation.Transition.Exit(Animation.Transition.Fade), targetCurve)
                    .Animate(targetCurve)
                    .InteractionStates(states => states.PointerOver(opacity: 0.8f))
                    .Keyframes("transparent-target", 1, keyframes => keyframes.Duration(1000).Loop()
                        .At(0f, translation: Vector3.Zero).At(1f, translation: new Vector3(8, 0, 0)));
                Element actualTarget = custom
                    ? new CustomForwardedTargetElement(target, 0, () => customUnmounts++).Ref(customRef)
                    : target;
                Element transparent = registered ? new ForwardedTargetElement(actualTarget) : Memo("stable", () => actualTarget);
                var outer = Flyout(transparent, Component<CleanupProbe, Action>(() => contentCleanups++));
                var button = (Button)reconciler.Mount(outer, static () => { })!;
                H.SetContent(button);
                await Harness.Render();
                var visual = ElementCompositionPreview.GetElementVisual(button);
                targetRef.CurrentChanged += current => { if (current is null) refClears++; };
                customRef.CurrentChanged += current => { if (current is null) customRefClears++; };
                try
                {
                    H.Check(name + "_ActualTarget", ReferenceEquals(Reconciler.GetElementTag(button), actualTarget)
                        && ReferenceEquals(targetRef.Current, button)
                        && (!custom || ReferenceEquals(customRef.Current, button)));
                    H.Check(name + "_TargetConnectedKey", Reconciler.FindMountedElementValue(button, static el => el.ConnectedAnimationKey) == name + "_target");
                    H.Check(name + "_TargetExitTransition", ReferenceEquals(
                        Reconciler.FindMountedElementValue(button, static el => el.ElementTransition), target.ElementTransition));
                    H.Check(name + "_TargetCurve", ReferenceEquals(
                        Reconciler.FindMountedElementValue(button, static el => el.AnimationConfig)?.Curve, targetCurve));
                    H.Check(name + "_InteractionRegistered", Reconciler.InteractionTrackerCountForTests == trackersBefore + 1);
                    H.Check(name + "_KeyframesStarted", visual.TryGetAnimationController("Translation") is not null);
                    var configuredOuter = outer.ConnectedAnimation(name + "_outer")
                        .Transition(Animation.Transition.Exit(Animation.Transition.Fade), Curve.Ease(200)).Animate(Curve.Spring());
                    reconciler.Update(outer, configuredOuter, button, static () => { });
                    H.Check(name + "_OuterConfigurationWins", Reconciler.FindMountedElementValue(button, static el => el.ConnectedAnimationKey) == name + "_outer"
                        && ReferenceEquals(Reconciler.FindMountedElementValue(button, static el => el.ElementTransition), configuredOuter.ElementTransition)
                        && ReferenceEquals(Reconciler.FindMountedElementValue(button, static el => el.AnimationConfig), configuredOuter.AnimationConfig));
                    if (pooled) reconciler.UnmountAndPool(button);
                    else reconciler.UnmountChild(button);
                    H.Check(name + "_TargetRefClearedOnce", targetRef.Current is null && refClears == 1);
                    if (custom)
                        H.Check(name + "_CustomTeardownOnce", customRef.Current is null && customRefClears == 1 && customUnmounts == 1);
                    H.Check(name + "_InteractionCleared", Reconciler.InteractionTrackerCountForTests == trackersBefore);
                    H.Check(name + "_KeyframesStopped", visual.TryGetAnimationController("Translation") is null);
                    H.Check(name + "_ContentCleanedOnce", contentCleanups == 1 && Reconciler.GetFlyoutOnControl(button) is null);
                }
                finally
                {
                    Reconciler.ClearInteractionStates(button);
                    visual.StopAnimation("Translation");
                    H.SetContent(null);
                }
            }
        }
    }

    private sealed class ResourceLifecycleContent : Component<(string Text, string ResourceKey, Action Cleanup)>
    {
        public override Element Render()
        {
            UseEffect(() => Props.Cleanup, []);
            return TextBlock(Props.Text).Foreground(Theme.Ref(Props.ResourceKey));
        }
    }

    private static string? FlyoutText(WinPrim.FlyoutBase? flyout) => flyout switch
    {
        Flyout { Content: Border { Child: TextBlock text } } => text.Text,
        MenuFlyout menu when menu.Items.Count > 0 => (menu.Items[0] as MenuFlyoutItem)?.Text,
        CommandBarFlyout commands when commands.PrimaryCommands.Count > 0 => (commands.PrimaryCommands[0] as AppBarButton)?.Label,
        _ => null,
    };

    private sealed class LifecycleContent : Component<(string Text, Action Cleanup)>
    {
        public override Element Render()
        {
            UseEffect(() => Props.Cleanup, []);
            return TextBlock(Props.Text);
        }
    }

    internal sealed class AnimateCurveAcrossNestedFlyouts(Harness h, bool vector) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cases = new (string Name, Curve Target, Curve? Middle, Curve? Decorator, Curve? Scope)[]
            {
                ("Target", Curve.Linear(600), null, null, null),
                ("Middle", Curve.Spring(), Curve.Linear(600), null, null),
                ("Decorator", Curve.Spring(), Curve.Spring(), Curve.Linear(600), null),
                ("Scope", Curve.Spring(), Curve.Spring(), Curve.Spring(), Curve.Linear(600)),
            };
            foreach (var scenario in cases)
            {
                var name = $"FlyoutAnimate_{(vector ? "Vector3" : "Scalar")}_{scenario.Name}";
                var property = vector ? "Scale" : "Opacity";
                using var reconciler = new Reconciler();
                var middle = Flyout(Flyout(
                    Button("curve target").Size(120, 60).Animate(scenario.Target),
                    TextBlock("inner")), TextBlock("middle"));
                if (scenario.Middle is { } middleCurve)
                    middle = middle.Animate(middleCurve);
                var decorated = Flyout(middle, TextBlock("outer"));
                if (scenario.Decorator is { } decoratorCurve)
                    decorated = decorated.Animate(decoratorCurve);
                var button = (Button)reconciler.Mount(decorated, static () => { })!;
                H.SetContent(button);
                await Harness.Render();
                var visual = ElementCompositionPreview.GetElementVisual(button);
                try
                {
                    // Isolate the helper: implicit animations could otherwise hide a missing curve fallback.
                    bool hadImplicitAnimations = visual.ImplicitAnimations is not null;
                    visual.ImplicitAnimations = null;
                    H.Check(name + "_Isolated", hadImplicitAnimations
                        && visual.TryGetAnimationController(property) is null);
                    Action change = () =>
                    {
                        if (vector) AnimationHelper.SetOrAnimateVector3(button, property, new Vector3(1.2f));
                        else AnimationHelper.SetOrAnimate(button, property, 0.4f);
                    };
                    // The selected Linear curve has a keyframe controller; lower-priority Springs do not.
                    H.Check(name + "_SelectedCurve", () =>
                    {
                        if (scenario.Scope is { } scopeCurve)
                            AnimationScope.WithAnimation(scopeCurve, change);
                        else
                            change();
                        return visual.TryGetAnimationController(property) is not null;
                    });
                }
                finally
                {
                    visual.StopAnimation(property);
                    reconciler.UnmountChild(button);
                    H.SetContent(null);
                }
            }
        }
    }

    internal sealed class ConnectedNavigation(Harness h, bool intermediate = false) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            using var host = H.CreateHost();
            var nav = new NavigationHandle<string>(new NavigationStack<string>("list"));
            const string key = "flyout-connected-navigation";
            host.Mount(_ => (NavigationHost(nav, route => Border(VStack(8,
                TextBlock(route),
                ConnectedFlyoutTarget(route == "list" ? "connected source" : "connected destination",
                    key, route == "list" ? 100 : 180, intermediate)))) with
            {
                Transition = NavigationTransition.None,
            }).AutomationId("flyout-connected-host"));
            await Harness.Render();
            var source = H.FindButton("connected source");
            var pages = H.FindControl<Grid>(grid =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(grid) == "flyout-connected-host")!;
            H.Check("FlyoutConnectedNav_SourceMeasured", source is { ActualWidth: > 0, ActualHeight: > 0 });
            H.Check("FlyoutConnectedNav_SourceCollected", source is not null
                && ConnectedNavigationOperation.Collect(host.Reconciler, pages).Contains(source));
            int baseline = host.Reconciler.ConnectedAnimationStartCount;

            nav.Navigate("detail", new() { ConnectedAnimationKey = key, Transition = NavigationTransition.Fade() });
            H.Check("FlyoutConnectedNav_AnimationStarted", await Harness.WaitFor(() =>
                host.Reconciler.ConnectedAnimationStartCount == baseline + 1));
            H.Check("FlyoutConnectedNav_DestinationMeasured",
                H.FindButton("connected destination") is { ActualWidth: > 0, ActualHeight: > 0 });
            H.Check("FlyoutConnectedNav_Settled", await Harness.WaitFor(() => pages.Children.Count == 1, 100));
            H.Check("FlyoutConnectedNav_NoDuplicateStarts", host.Reconciler.ConnectedAnimationStartCount == baseline + 1);
        }
    }

    internal sealed class ConnectedSnapshotAcrossUnmount(Harness h, bool pooled, bool intermediate = false) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var name = pooled ? "FlyoutConnectedPool" : "FlyoutConnectedUnmount";
            var key = pooled ? "flyout-connected-pool" : "flyout-connected-unmount";
            using var reconciler = new Reconciler();
            var panel = (StackPanel)reconciler.Mount(VStack(ConnectedFlyoutTarget("snapshot source", key, 100, intermediate)), static () => { })!;
            H.SetContent(panel);
            await Harness.Render();
            var source = (Button)panel.Children[0];
            try
            {
                reconciler.FlushConnectedAnimations();
                H.Check(name + "_NoSourceNoStart", reconciler.ConnectedAnimationStartCount == 0);
                // Mount queues the destination. Lay it out before preparing the source snapshot.
                var destination = (Button)reconciler.Mount(ConnectedFlyoutTarget("snapshot destination", key, 180, intermediate), static () => { })!;
                panel.Children.Add(destination);
                await Harness.Render();
                H.Check(name + "_BothMeasured", source.ActualWidth > 0 && source.ActualHeight > 0
                    && destination.ActualWidth > 0 && destination.ActualHeight > 0);

                if (pooled) reconciler.UnmountAndPool(source);
                else reconciler.UnmountChild(source);
                panel.Children.Remove(source);

                var animation = Microsoft.UI.Xaml.Media.Animation.ConnectedAnimationService.GetForCurrentView().GetAnimation(key);
                H.Check(name + "_SnapshotPrepared", animation is not null);
                bool completed = false;
                if (animation is not null) animation.Completed += (_, _) => completed = true;
                reconciler.FlushConnectedAnimations();
                bool started = reconciler.ConnectedAnimationStartCount == 1;
                H.Check(name + "_AnimationStarted", started);
                if (started)
                    H.Check(name + "_AnimationCompleted", await Harness.WaitFor(() => completed, 100));
            }
            finally
            {
                await Harness.Render();
                // Dispose the remaining destination without preparing an unused source snapshot.
                ConnectedNavigationOperation.UnmountPage(reconciler, panel);
                H.SetContent(null);
            }
        }
    }

    private static FlyoutElement ConnectedFlyoutTarget(string label, string key, double width, bool intermediate)
    {
        var target = Button(label).Size(width, 60);
        if (!intermediate) target = target.ConnectedAnimation(key);
        var middle = Flyout(Flyout(target, TextBlock("inner")), TextBlock("middle"));
        if (intermediate) middle = middle.ConnectedAnimation(key);
        return Flyout(middle, TextBlock("outer"));
    }

    internal sealed class TargetExitTransition(Harness h, bool replace, int depth = 1, bool intermediate = false) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var name = replace ? "FlyoutExit_Replacement" : "FlyoutExit_Removal";
            int contentCleanups = 0;
            var targetRef = new ElementRef();
            using var reconciler = new Reconciler();
            Element decorated = Button("exit target").Ref(targetRef);
            if (!intermediate)
                decorated = decorated.Transition(Animation.Transition.Exit(Animation.Transition.Fade), Animation.Curve.Ease(600));
            for (var layer = 1; layer < depth; layer++)
            {
                decorated = Flyout(decorated, TextBlock($"inner {layer}"));
                if (intermediate && layer == depth - 1)
                    decorated = decorated.Transition(Animation.Transition.Exit(Animation.Transition.Fade), Animation.Curve.Ease(600));
            }
            var initial = VStack(Flyout(decorated,
                Component<CleanupProbe, Action>(() => contentCleanups++)));
            var panel = (StackPanel)reconciler.Mount(initial, static () => { })!;
            H.SetContent(panel);
            await Harness.Render();
            var target = (Button)panel.Children[0];
            try
            {
                H.Check(name + "_Mounted", ReferenceEquals(targetRef.Current, target) && contentCleanups == 0);
                var next = replace ? VStack(TextBlock("replacement")) : VStack();
                reconciler.Update(initial, next, panel, static () => { });

                H.Check(name + "_ExitInFlight", reconciler.IsExiting(target) && panel.Children.Contains(target));
                H.Check(name + "_CleanupDeferred", ReferenceEquals(targetRef.Current, target) && contentCleanups == 0);
                H.Check(name + "_FadeStarted", reconciler.IsExiting(target)
                    && ElementCompositionPreview.GetElementVisual(target).TryGetAnimationController("Opacity") is not null);
                if (replace)
                    H.Check(name + "_ReplacementVisible", panel.Children.Count == 2
                        && panel.Children[0] is TextBlock { Text: "replacement" }
                        && ReferenceEquals(panel.Children[1], target));

                var completed = await Harness.WaitFor(() => !reconciler.HasExitingControls, maxPasses: 40, perPassMs: 50);
                H.Check(name + "_ExitCompleted", completed && !panel.Children.Contains(target)
                    && panel.Children.Count == (replace ? 1 : 0));
                H.Check(name + "_CleanupOnce", targetRef.Current is null && contentCleanups == 1);
            }
            finally
            {
                H.SetContent(null);
                reconciler.UnmountChild(panel);
            }
        }
    }

    internal sealed class DeepButtonCallbacksAcrossUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            var calls = new List<int>();
            Action? advance = null;
            ButtonElement? currentTarget = null;
            FlyoutElement? currentFlyout = null;
            host.Mount(ctx =>
            {
                var (version, setVersion) = ctx.UseState(0);
                advance = () => setVersion(version + 1);
                // The label stays the same, so each new callback takes the Button's shallow-skip path.
                currentTarget = Button("deep target", () => calls.Add(version));
                currentFlyout = Flyout(
                    Flyout(Flyout(currentTarget, TextBlock("inner")), TextBlock("middle")),
                    TextBlock("outer"));
                return currentFlyout;
            });
            await Harness.Render();
            var first = H.FindButton("deep target");
            H.Check("FlyoutDeep_Mounted", first is not null);
            if (first is null) return;

            for (var version = 0; version < 3; version++)
            {
                if (version > 0)
                {
                    advance!();
                    await Harness.Render();
                }
                var button = H.FindButton("deep target");
                var name = $"FlyoutDeep_Phase{version}";
                H.Check(name + "_SameControl", ReferenceEquals(first, button));
                H.Check(name + "_CurrentTarget", ReferenceEquals(Reconciler.GetElementTag(first), currentTarget));
                H.Check(name + "_CurrentDecorator", ReferenceEquals(Reconciler.GetMountedElement(first), currentFlyout));
                H.ClickButton("deep target");
                H.Check(name + "_CurrentClosure", calls.SequenceEqual(Enumerable.Range(0, version + 1)),
                    $"calls: {string.Join(", ", calls)}");
                first.Flyout?.Hide();
                await Harness.Render();
            }
        }
    }

    internal sealed class RegisterTypeCallbacksAcrossUpdates(Harness h, int depth = 1) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            var calls = new List<int>();
            Action? advance = null;
            RegisteredButtonElement? currentTarget = null;
            FlyoutElement? currentFlyout = null;
            host.Reconciler.RegisterType<RegisteredButtonElement, Button>(
                mount: static (_, element, _) =>
                {
                    var button = new Button { Content = $"registered target {element.Version}" };
                    button.Click += static (sender, _) =>
                        (Reconciler.GetElementTag((Button)sender!) as RegisteredButtonElement)?.OnClick();
                    return button;
                },
                update: static (_, _, element, button, _) =>
                {
                    button.Content = $"registered target {element.Version}";
                    return null;
                });
            host.Mount(ctx =>
            {
                var (version, setVersion) = ctx.UseState(0);
                advance = () => setVersion(1);
                currentTarget = new RegisteredButtonElement(version, () => calls.Add(version))
                    .WithKey("registered-target");
                Element decorated = currentTarget;
                for (var layer = 1; layer < depth; layer++)
                    decorated = Flyout(decorated, TextBlock($"registered inner {layer}"));
                currentFlyout = Flyout(decorated, TextBlock("registered target body"));
                return currentFlyout;
            });
            await Harness.Render();

            var first = H.FindButton("registered target 0");
            H.Check("FlyoutRegisterType_Mounted", first is not null);
            if (first is null) return;
            H.Check("FlyoutRegisterType_InitialTarget", ReferenceEquals(Reconciler.GetElementTag(first), currentTarget));
            H.Check("FlyoutRegisterType_InitialDecorator", ReferenceEquals(Reconciler.GetMountedElement(first), currentFlyout));
            H.ClickButton("registered target 0");
            H.Check("FlyoutRegisterType_InitialClosure", calls.SequenceEqual(new[] { 0 }), $"calls: {string.Join(", ", calls)}");
            first.Flyout?.Hide();

            advance!();
            await Harness.Render();
            var updated = H.FindButton("registered target 1");
            H.Check("FlyoutRegisterType_Updated", updated is not null);
            if (updated is null) return;
            H.Check("FlyoutRegisterType_SameControl", ReferenceEquals(first, updated));
            H.Check("FlyoutRegisterType_CurrentTarget", ReferenceEquals(Reconciler.GetElementTag(updated), currentTarget));
            H.Check("FlyoutRegisterType_CurrentDecorator", ReferenceEquals(Reconciler.GetMountedElement(updated), currentFlyout));
            H.ClickButton("registered target 1");
            H.Check("FlyoutRegisterType_CurrentClosure", calls.SequenceEqual(new[] { 0, 1 }), $"calls: {string.Join(", ", calls)}");
            updated.Flyout?.Hide();
        }
    }

    private sealed record RegisteredButtonElement(int Version, Action OnClick) : Element;

    internal sealed class MiddleStateCleanup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            foreach (bool pooled in new[] { false, true })
            {
                var name = pooled ? "FlyoutMiddleCleanup_Pool" : "FlyoutMiddleCleanup_Ordinary";
                int trackersBefore = Reconciler.InteractionTrackerCountForTests;
                var targetRef = new ElementRef();
                var middleRef = new ElementRef();
                var outerRef = new ElementRef();
                Element middle = Flyout(Button(name).Ref(targetRef), TextBlock("inner")).Ref(middleRef);
                // Keep the pool-reuse case free of compositor taint.
                if (!pooled)
                    middle = middle.InteractionStates(states => states.PointerOver(opacity: 0.8f))
                        .Keyframes("middle", 1, keyframes => keyframes.Duration(1000).Loop()
                            .At(0f, translation: Vector3.Zero)
                            .At(1f, translation: new Vector3(8, 0, 0)));
                using var reconciler = new Reconciler();
                var panel = (StackPanel)reconciler.Mount(
                    VStack(Flyout(middle, TextBlock("outer")).Ref(outerRef)), static () => { })!;
                H.SetContent(panel);
                await Harness.Render();
                var button = (Button)panel.Children[0];
                var visual = !pooled ? ElementCompositionPreview.GetElementVisual(button) : null;
                int middleClears = 0;
                middleRef.CurrentChanged += current => { if (current is null) middleClears++; };
                try
                {
                    H.Check(name + "_RefsSet", ReferenceEquals(targetRef.Current, button)
                        && ReferenceEquals(middleRef.Current, button) && ReferenceEquals(outerRef.Current, button));
                    if (!pooled)
                    {
                        H.Check(name + "_InteractionRegistered", Reconciler.InteractionTrackerCountForTests == trackersBefore + 1);
                        H.Check(name + "_KeyframesStarted", visual!.TryGetAnimationController("Translation") is not null);
                    }

                    if (pooled) reconciler.UnmountAndPool(button);
                    else reconciler.UnmountChild(button);
                    panel.Children.Remove(button);

                    H.Check(name + "_MiddleRefCleared", middleRef.Current is null && middleClears == 1);
                    H.Check(name + "_EndpointRefsCleared", targetRef.Current is null && outerRef.Current is null);
                    if (!pooled)
                    {
                        H.Check(name + "_InteractionCleared", Reconciler.InteractionTrackerCountForTests == trackersBefore);
                        H.Check(name + "_KeyframesStopped", visual!.TryGetAnimationController("Translation") is null);
                    }
                    else
                    {
                        var reused = reconciler.Mount(Button("plain reuse"), static () => { })!;
                        panel.Children.Add(reused);
                        H.Check(name + "_SameControlReused", ReferenceEquals(button, reused));
                        H.Check(name + "_OldRefStaysCleared", middleRef.Current is null && middleClears == 1);
                    }
                }
                finally
                {
                    Reconciler.ClearInteractionStates(button);
                    visual?.StopAnimation("Translation");
                    reconciler.UnmountChild(panel);
                    H.SetContent(null);
                }
            }
        }
    }

    internal sealed class TargetTeardownAcrossPaths(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            foreach (var (registered, pooled) in new[] { (false, false), (false, true), (true, false), (true, true) })
            {
                string name = $"FlyoutTeardown_{(registered ? "RegisterType" : "V1")}_{(pooled ? "Pool" : "Ordinary")}";
                int targetUnmounts = 0, contentCleanups = 0;
                var unmountOrder = new List<string>();
                Action onTargetUnmount = () => { targetUnmounts++; unmountOrder.Add("target"); };
                int trackersBefore = Reconciler.InteractionTrackerCountForTests;
                var targetRef = new ElementRef();
                var decoratorRef = new ElementRef();
                Action? remove = null;
                using var host = H.CreateHost();
                if (registered)
                {
                    host.Reconciler.RegisterType<TeardownButtonElement, Button>(
                        mount: static (_, element, _) => new Button { Content = element.Label },
                        update: static (_, _, element, button, _) => { button.Content = element.Label; return null; },
                        unmount: (_, _) => onTargetUnmount());
                }
                else
                    host.Reconciler.RegisterHandler<TeardownButtonElement, Button>(new TeardownButtonHandler(onTargetUnmount));
                host.Mount(ctx =>
                {
                    var (show, setShow) = ctx.UseState(true);
                    remove = () => setShow(false);
                    if (!show) return VStack();
                    Element target = new TeardownButtonElement(name).Ref(targetRef);
                    // Interaction states and keyframes taint the compositor; keep pool-reuse cases untainted.
                    if (!pooled)
                        target = target.InteractionStates(states => states.PointerOver(opacity: 0.8f))
                            .Keyframes("teardown", 1, keyframes => keyframes.Duration(1000).Loop()
                                .At(0f, translation: Vector3.Zero)
                                .At(1f, translation: new Vector3(8, 0, 0)));
                    var decorated = Flyout(target, Component<CleanupProbe, Action>(() =>
                    {
                        contentCleanups++;
                        unmountOrder.Add("flyout-content");
                    })).Ref(decoratorRef);
                    return pooled ? VStack(decorated) : decorated;
                });
                await Harness.Render();

                var first = H.FindButton(name);
                H.Check(name + "_RefsSetOnMount", first is not null
                    && ReferenceEquals(targetRef.Current, first) && ReferenceEquals(decoratorRef.Current, first));
                if (first is null) continue;
                H.Check(name + "_NoEarlyCleanup", targetUnmounts == 0 && contentCleanups == 0);
                var visual = !pooled ? ElementCompositionPreview.GetElementVisual(first) : null;
                if (!pooled)
                {
                    H.Check(name + "_InteractionRegistered", Reconciler.InteractionTrackerCountForTests == trackersBefore + 1);
                    H.Check(name + "_KeyframesStarted", visual!.TryGetAnimationController("Translation") is not null);
                }
                if (pooled) remove!();
                else host.Mount(_ => TextBlock("removed"));
                await Harness.Render();

                H.Check(name + "_TargetRefCleared", targetRef.Current is null);
                H.Check(name + "_DecoratorRefCleared", decoratorRef.Current is null);
                H.Check(name + "_TargetUnmountOnce", targetUnmounts == 1, $"unmounts: {targetUnmounts}");
                H.Check(name + "_ContentCleanupOnce", contentCleanups == 1, $"cleanups: {contentCleanups}");
                H.Check(name + "_UnmountOrder", unmountOrder.SequenceEqual(new[] { "flyout-content", "target" }),
                    $"order: {string.Join(", ", unmountOrder)}");
                if (!pooled)
                {
                    H.Check(name + "_InteractionCleared", Reconciler.InteractionTrackerCountForTests == trackersBefore);
                    H.Check(name + "_KeyframesStopped", visual!.TryGetAnimationController("Translation") is null);
                    // Stop the loop even if the cleanup assertion failed.
                    visual.StopAnimation("Translation");
                }
                if (pooled)
                {
                    host.Mount(_ => Button(name + " reused"));
                    await Harness.Render();
                    H.Check(name + "_SameControlReused", ReferenceEquals(first, H.FindButton(name + " reused")));
                    H.Check(name + "_OldOwnerStaysCleared", targetRef.Current is null
                        && decoratorRef.Current is null && targetUnmounts == 1 && contentCleanups == 1);
                }
            }
        }
    }

    private sealed record TeardownButtonElement(string Label) : Element;

    private sealed class TeardownButtonHandler(Action onUnmount) : IElementHandler<TeardownButtonElement, Button>
    {
        public Button Mount(MountContext ctx, TeardownButtonElement element) => new() { Content = element.Label };
        public void Update(UpdateContext ctx, TeardownButtonElement oldElement, TeardownButtonElement newElement, Button control)
            => control.Content = newElement.Label;
        public void Unmount(UnmountContext ctx, Button control) => onUnmount();
    }

    internal sealed class MenuFlyoutTargetCallbacks(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int decoratedClicks = 0, plainClicks = 0;
            using var host = H.CreateHost();
            host.Mount(_ => VStack(
                MenuFlyout(Button("menu target", () => decoratedClicks++), MenuItem("item")),
                Button("plain target", () => plainClicks++)));
            await Harness.Render();

            H.ClickButton("menu target");
            await Harness.Render();
            H.Check("Diag_MenuFlyoutTargetOnClick", decoratedClicks == 1);
            H.FindButton("menu target")?.Flyout?.Hide();
            H.ClickButton("plain target");
            H.Check("Diag_PlainButtonOnClick", plainClicks == 1);
        }
    }

    internal sealed class TeardownAndPlainPoolReuse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int contentCleanups = 0, passThroughCleanups = 0, plainClicks = 0;
            using var host = H.CreateHost();
            host.Mount(_ => Flyout(Button("cleanup target", () => { }),
                Component<CleanupProbe, Action>(() => contentCleanups++)) with
            {
                OverlayInputPassThroughElement = Component<CleanupProbe, Action>(() => passThroughCleanups++),
            });
            await Harness.Render();
            var target = H.FindButton("cleanup target");
            H.Check("FlyoutTarget_CleanupTargetMounted", target is not null);
            if (target is null) return;
            H.Check("FlyoutTarget_CleanupsNotRunBeforeUnmount", contentCleanups == 0 && passThroughCleanups == 0);

            host.Mount(_ => TextBlock("removed"));
            await Harness.Render();
            H.Check("FlyoutTarget_ContentCleanupRuns", contentCleanups == 1);
            H.Check("FlyoutTarget_PassThroughCleanupRuns", passThroughCleanups == 1);

            // Root replacement unmounts without pooling; return this target explicitly.
            host.Reconciler.Pool.Return(target);
            H.Check("FlyoutTarget_PoolClearsBothIdentities", Reconciler.GetMountedElement(target) is null);
            host.Mount(_ => Button("reused plain target", () => plainClicks++));
            await Harness.Render();
            var plain = H.FindButton("reused plain target");
            H.Check("FlyoutTarget_PlainButtonReusesTarget", ReferenceEquals(target, plain));
            H.Check("FlyoutTarget_PlainButtonHasNoDecorator",
                plain is not null && Reconciler.GetMountedElement(plain) is ButtonElement);
            H.Check("FlyoutTarget_PlainButtonHasNoFlyout", plain?.Flyout is null);
            H.ClickButton("reused plain target");
            H.Check("FlyoutTarget_PlainButtonCallbackWorks", plainClicks == 1);

            host.Mount(_ => TextBlock("finished"));
            await Harness.Render();
            H.Check("FlyoutTarget_PlainUnmountDoesNotRepeatCleanup", contentCleanups == 1 && passThroughCleanups == 1);
        }
    }

    private sealed class CleanupProbe : Component<Action>
    {
        public override Element Render()
        {
            UseEffect(() => Props, []);
            return TextBlock("cleanup probe");
        }
    }

    internal sealed class CallbacksAcrossUpdates(Harness h, int kind) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probe = new Probe(kind);
            using var host = H.CreateHost();
            host.Mount(probe);
            await Harness.Render();
            var first = H.FindButton("target");
            H.Check("FlyoutTarget_InitialButtonMounted", first is not null);
            if (first is null) return;

            for (var phase = 0; phase < 4; phase++)
            {
                if (phase > 0)
                {
                    H.ClickButton("advance");
                    await Harness.Render();
                }
                H.Check($"FlyoutTarget_Phase{phase}", probe.Phase == phase);
                if (phase == 2)
                {
                    var replacement = H.FindControl<CheckBox>(c => c.Content as string == "replacement");
                    H.Check("FlyoutTarget_ReplacedWithCheckBox", replacement is not null);
                    H.ToggleCheckBox("replacement");
                    // Target replacement unmounts without pooling. Explicitly return the
                    // removed button to exercise both identity cleanup and its next rent.
                    host.Reconciler.ReturnControl(first);
                    H.Check("FlyoutTarget_OldIdentityCleared", Reconciler.GetMountedElement(first) is null);
                }
                else
                {
                    var button = H.FindButton("target");
                    H.Check($"FlyoutTarget_Phase{phase}_ButtonIdentity",
                        button is not null && Reconciler.GetElementTag(button) is ButtonElement);
                    H.Check($"FlyoutTarget_Phase{phase}_SameButton", ReferenceEquals(first, button));
                    H.ClickButton("target");
                    H.Check($"FlyoutTarget_Phase{phase}_Opens",
                        await Harness.WaitFor(() => button?.Flyout?.IsOpen == true));
                    // IsOpen becomes true before the opening animation completes.
                    // Closing immediately can cancel Opened even on a native Flyout.
                    if (kind == 0)
                        await Harness.WaitFor(() => probe.Opened[phase] == 1);
                    button?.Flyout?.Hide();
                    await Harness.WaitFor(() => button?.Flyout?.IsOpen == false);
                    await Harness.Render(50);
                }
                H.ToggleCheckBox("check");
                await Harness.Render();
                H.Check($"FlyoutTarget_Phase{phase}_TargetCallback", probe.Clicks[phase] == 1);
                H.Check($"FlyoutTarget_Phase{phase}_CheckCallback", probe.Checks[phase] == 1);
                for (var previous = 0; previous < phase; previous++)
                    H.Check($"FlyoutTarget_Phase{phase}_Previous{previous}Unchanged",
                        probe.Clicks[previous] == 1 && probe.Checks[previous] == 1);
                if (kind == 0 && phase != 2)
                    H.Check($"FlyoutTarget_Phase{phase}_CurrentDecoratorCallbacks",
                        await Harness.WaitFor(() => probe.Opened[phase] == 1 && probe.Closed[phase] == 1),
                        $"opened={probe.Opened[phase]}, closed={probe.Closed[phase]}");
            }
        }
    }

    internal sealed class NestedCallbacksAcrossUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            for (var kind = 0; kind < 3; kind++)
            {
                using var host = H.CreateHost();
                var probe = new NestedProbe(kind);
                host.Mount(probe);
                await Harness.Render();
                var first = H.FindButton("nested target");
                for (var phase = 0; phase < 2; phase++)
                {
                    if (phase > 0)
                    {
                        H.ClickButton("nested advance");
                        await Harness.Render();
                    }
                    var button = H.FindButton("nested target");
                    var name = $"FlyoutTarget_Nested{kind}_Phase{phase}";
                    H.Check(name + "_SameTarget", button is not null && ReferenceEquals(first, button));
                    if (button is null) continue;
                    H.Check(name + "_TargetIdentity", Reconciler.GetElementTag(button) is ButtonElement);
                    H.Check(name + "_DecoratorIdentity", ReferenceEquals(Reconciler.GetMountedElement(button), probe.Outer));
                    H.ClickButton("nested target");
                    H.Check(name + "_Opens", await Harness.WaitFor(() => button.Flyout?.IsOpen == true));
                    if (kind == 0)
                        H.Check(name + "_CurrentOpened", await Harness.WaitFor(() => probe.Opened[phase] == 1));
                    button.Flyout?.Hide();
                    await Harness.WaitFor(() => button.Flyout?.IsOpen == false);
                    await Harness.Render(50);
                    H.Check(name + "_TargetCallback", probe.Clicks == phase + 1);
                }
            }
        }
    }

    private sealed class NestedProbe : Component
    {
        private readonly int _kind;
        private readonly Element _inner;
        internal int Clicks;
        internal readonly int[] Opened = new int[2];
        internal Element? Outer;

        internal NestedProbe(int kind)
        {
            _kind = kind;
            // Reusing this exact element takes the inner decorator's shallow-skip path.
            // Its callback must refresh the decorator identity, not the button's tag.
            _inner = Flyout(Button("nested target", () => Clicks++), TextBlock("inner")) with
            {
                OnOpened = static () => { },
            };
        }

        public override Element Render()
        {
            var (phase, setPhase) = UseState(0);
            Outer = _kind switch
            {
                0 => Flyout(_inner, TextBlock($"outer-{phase}")) with
                {
                    OnOpened = () => Opened[phase]++,
                    AreOpenCloseAnimationsEnabled = false,
                },
                1 => MenuFlyout(_inner, MenuItem($"outer-{phase}")),
                _ => CommandBarFlyout(_inner, primaryCommands: [AppBarButton($"outer-{phase}")]),
            };
            return VStack(Button("nested advance", () => setPhase(phase + 1)), Outer);
        }
    }

    private sealed class Probe(int kind) : Component
    {
        internal int Phase;
        internal readonly int[] Clicks = new int[4];
        internal readonly int[] Checks = new int[4];
        internal readonly int[] Opened = new int[4];
        internal readonly int[] Closed = new int[4];

        public override Element Render()
        {
            var (phase, setPhase) = UseState(0);
            Phase = phase;
            Element Wrap(Element target) => kind switch
            {
                0 => Flyout(target, TextBlock("body")) with
                {
                    Placement = WinPrim.FlyoutPlacementMode.Top,
                    AreOpenCloseAnimationsEnabled = false,
                    OnOpened = () => Opened[phase]++,
                    OnClosed = () => Closed[phase]++,
                },
                1 => MenuFlyout(target, MenuItem("item")),
                _ => CommandBarFlyout(target, primaryCommands: [AppBarButton("command")]) with
                {
                    Placement = WinPrim.FlyoutPlacementMode.Top,
                },
            };
            Element target = phase == 2
                ? CheckBox(false, _ => Clicks[phase]++, label: "replacement")
                : Button("target", () => Clicks[phase]++);
            return VStack(
                Button("advance", () => setPhase(phase + 1)),
                Wrap(target),
                Wrap(CheckBox(false, _ => Checks[phase]++, label: "check")));
        }
    }
}
