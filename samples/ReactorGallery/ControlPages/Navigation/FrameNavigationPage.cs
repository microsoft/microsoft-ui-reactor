using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Navigation;

/// <summary>
/// Phase 8.3 — exercises Frame navigation events (spec 039 §3.2):
/// <c>.Navigated(...)</c>, <c>.Navigating(...)</c>, <c>.NavigationFailed(...)</c>.
/// Deferred from Phase 3.2 because a meaningful sample requires
/// Page-derived navigation targets; the two trivial XAML pages live
/// alongside this file.
/// </summary>
class FrameNavigationPage : Component
{
    record LogEntry(DateTimeOffset At, string Kind, string Detail);

    public override Element Render()
    {
        var (log, updateLog) = UseReducer<IReadOnlyList<LogEntry>>(Array.Empty<LogEntry>());
        var (target, setTarget) = UseState<Type>(typeof(Pages.FrameSampleHomePage));
        // Bump on each Navigate request so the Frame re-resolves SourcePageType
        // even when target equals the current page (Frame coalesces identical
        // navigations otherwise).
        var (navTick, updateNavTick) = UseReducer<int>(0);

        void Append(string kind, string detail) =>
            updateLog(prev =>
            {
                var next = new List<LogEntry>(prev) { new(DateTimeOffset.Now, kind, detail) };
                // Keep only the most recent 20 entries.
                if (next.Count > 20) next.RemoveRange(0, next.Count - 20);
                return next;
            });

        void NavigateTo(Type t)
        {
            setTarget(t);
            updateNavTick(n => n + 1);
        }

        return PageContent("Frame navigation events",
            "Wire .Navigated / .Navigating / .NavigationFailed on FrameElement to observe Frame navigation transitions. Each fluent drops the leading 'On' per Phase 1's convention; the underlying record properties remain OnNavigated / OnNavigating / OnNavigationFailed.",

            SampleCard("Live demo",
                VStack(12,
                    HStack(8,
                        Button("Go Home").Click(() => NavigateTo(typeof(Pages.FrameSampleHomePage))).AccentButton(),
                        Button("Go Details").Click(() => NavigateTo(typeof(Pages.FrameSampleDetailsPage))),
                        Button("Force failure").Click(() => NavigateTo(typeof(Pages.FrameSampleBrokenPage))).SubtleButton(),
                        Button("Clear log").Click(() => updateLog(_ => Array.Empty<LogEntry>())).SubtleButton()
                    ),

                    // The Frame itself. Re-keyed on navTick so identical-target
                    // re-navigations still fire Navigating + Navigated.
                    Border(
                        Frame(target)
                            .Navigating(t => Append("Navigating", t.Name))
                            .Navigated(t => Append("Navigated", t.Name))
                            .NavigationFailed((t, ex) => Append("NavigationFailed", $"{t.Name}: {ex.Message}"))
                            .WithKey($"frame-{navTick}")
                    )
                    .WithBorder(Theme.DividerStroke)
                    .CornerRadius(8)
                    .Height(160),

                    // Event log.
                    BodyStrong("Event log"),
                    Border(
                        log.Count == 0
                            ? Body("(no events yet — click a button above)").Foreground(Theme.SecondaryText)
                            : VStack(2, log
                                .Reverse()
                                .Select(e =>
                                    HStack(8,
                                        TextBlock(e.At.ToString("HH:mm:ss.fff"))
                                            .FontSize(12)
                                            .Foreground(Theme.TertiaryText)
                                            .Set(tb => tb.FontFamily = new FontFamily("Cascadia Code, Consolas, monospace")),
                                        TextBlock(e.Kind)
                                            .FontSize(12)
                                            .SemiBold()
                                            .Foreground(e.Kind == "NavigationFailed" ? Theme.AccentText : Theme.PrimaryText)
                                            .Width(140),
                                        TextBlock(e.Detail)
                                            .FontSize(12)
                                            .Foreground(Theme.SecondaryText)) as Element)
                                .ToArray())
                    )
                    .Background(Theme.SubtleFill)
                    .WithBorder(Theme.DividerStroke)
                    .CornerRadius(6)
                    .Padding(12)
                    .Height(180)
                ),
                @"Frame(target)
    .Navigating(t => log.Add(""Navigating "" + t.Name))
    .Navigated(t => log.Add(""Navigated ""  + t.Name))
    .NavigationFailed((t, ex) => log.Add($""Failed {t.Name}: {ex.Message}""))")
        );
    }
}
