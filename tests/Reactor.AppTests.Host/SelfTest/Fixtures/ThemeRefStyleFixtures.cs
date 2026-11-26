using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;
using Color = global::Windows.UI.Color;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed partial class ThemeFixtureFontIcon : WinUI.FontIcon { }

/// <summary>
/// <see cref="ThemeRef"/> modifiers applied through the synthesized <c>{ThemeResource}</c> Style:
/// <c>SplitView.PaneBackground</c>, InteractionStates restoring the themed value rather than
/// pinning a brush, an app's own Style kept as the themed Style's <c>BasedOn</c>, and resource
/// keys that need quoting in the markup extension. Each scenario uses its own brush key with
/// distinct Light and Dark values, so a color names exactly which source produced it, and a
/// native <c>RequestedTheme</c> flip (no Reactor render) proves the value is still live.
/// </summary>
internal static class ThemeRefStyleFixtures
{
    private static readonly Color LightColor = Color.FromArgb(255, 210, 220, 230);
    private static readonly Color DarkColor = Color.FromArgb(255, 30, 40, 50);
    private static readonly Color HoverColor = Color.FromArgb(255, 0, 160, 0);
    private static readonly Color ExplicitColor = Color.FromArgb(255, 200, 30, 40);
    private static readonly Color OtherExplicitColor = Color.FromArgb(255, 40, 30, 200);

    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static Color? ColorOf(object? brush) => (brush as SolidColorBrush)?.Color;

    private static UIElement? UpdateObservingForeground(Reconciler reconciler, Element initial, Element next,
        WinUI.RichTextBlock text, Action<Brush?> observe)
    {
        var observer = text.RegisterPropertyChangedCallback(WinUI.RichTextBlock.ForegroundProperty,
            (sender, _) => observe(((WinUI.RichTextBlock)sender).Foreground));
        try { return reconciler.Update(initial, next, text, static () => { }); }
        finally { text.UnregisterPropertyChangedCallback(WinUI.RichTextBlock.ForegroundProperty, observer); }
    }

    private static bool HasLocal(DependencyObject? d, DependencyProperty p) =>
        d is not null && !ReferenceEquals(d.ReadLocalValue(p), DependencyProperty.UnsetValue);

    /// <summary>
    /// Runs <paramref name="body"/> with each of <paramref name="keys"/> defined in the app's theme
    /// dictionaries (Light/Dark colors). The body receives the merged dictionary so it can add
    /// other resources, such as a keyed Style.
    /// </summary>
    private static async Task WithThemeKeys(Harness h, string[] keys, Func<ReactorHost, ResourceDictionary, Task> body)
    {
        var resources = new ResourceDictionary();
        var lightDict = new ResourceDictionary();
        var darkDict = new ResourceDictionary();
        foreach (var key in keys)
        {
            lightDict[key] = new SolidColorBrush(LightColor);
            darkDict[key] = new SolidColorBrush(DarkColor);
        }
        resources.ThemeDictionaries["Light"] = lightDict;
        resources.ThemeDictionaries["Dark"] = darkDict;
        Application.Current.Resources.MergedDictionaries.Add(resources);
        using var host = h.CreateHost();
        try
        {
            await body(host, resources);
        }
        finally
        {
            h.SetContent(null);
            Application.Current.Resources.MergedDictionaries.Remove(resources);
            await Harness.Render();
        }
    }

    private static Task WithThemeKey(Harness h, string key, Func<ReactorHost, Task> body)
        => WithThemeKeys(h, [key], (host, _) => body(host));

    private static async Task<bool> FlipToDark(FrameworkElement control, Func<bool> isDark)
    {
        ((FrameworkElement)VisualTreeHelper.GetParent(control))!.RequestedTheme = ElementTheme.Dark;
        return await Harness.WaitFor(isDark);
    }

    internal sealed class ForegroundAppliedAfterBackgroundCallback(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var background = new SolidColorBrush(DarkColor);
            var foreground = new SolidColorBrush(ExplicitColor);
            var callbackForeground = new SolidColorBrush(OtherExplicitColor);
            var initial = Button("contrast").Background(new SolidColorBrush(LightColor))
                .Foreground(new SolidColorBrush(HoverColor));
            var button = (WinUI.Button)reconciler.Mount(initial, static () => { })!;
            bool callbackRan = false;
            var token = button.RegisterPropertyChangedCallback(WinUI.Control.BackgroundProperty, (sender, _) =>
            {
                ((WinUI.Button)sender).Foreground = callbackForeground;
                callbackRan = true;
            });
            try
            {
                var next = initial.Background(background).Foreground(foreground);
                var replacement = reconciler.Update(initial, next, button, static () => { });
                H.Check("ForegroundOrder_BackgroundCallbackRanOnSameControl",
                    replacement is null && callbackRan && ReferenceEquals(button.Background, background));
                H.Check("ForegroundOrder_DeclaredBrushWins", ReferenceEquals(button.Foreground, foreground),
                    $"foreground={ColorOf(button.Foreground)}");
            }
            finally
            {
                button.UnregisterPropertyChangedCallback(WinUI.Control.BackgroundProperty, token);
                reconciler.UnmountChild(button);
            }
            return Task.CompletedTask;
        }
    }

    internal sealed class SharedForegroundLookup(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsSharedForeground", async host =>
        {
            var modifierBrush = new SolidColorBrush(HoverColor);
            var typedBrush = new SolidColorBrush(ExplicitColor);
            var nextTypedBrush = new SolidColorBrush(OtherExplicitColor);
            var setterBrush = new SolidColorBrush(DarkColor);
            var source = new WinUI.AnimatedVisuals.AnimatedBackVisualSource();
            var theme = Theme.Ref("TrsSharedForeground");
            Action<int>? setPhase = null;
            Brush? foregroundBeforeSameBrushSetter = null;

            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                var rich = RichTextBlock("shared foreground");
                var icon = AnimatedIcon(source).Size(32, 32);
                return VStack(
                    phase switch
                    {
                        0 => rich.Foreground(typedBrush).Foreground<RichTextBlockElement>(modifierBrush),
                        1 or 3 => rich.Foreground(nextTypedBrush).Foreground<RichTextBlockElement>(modifierBrush),
                        2 => rich.Foreground(nextTypedBrush),
                        4 => rich.Foreground(nextTypedBrush).Set(control => control.Foreground = setterBrush),
                        5 => rich.Foreground(nextTypedBrush).Set(control =>
                        {
                            foregroundBeforeSameBrushSetter = control.Foreground;
                            control.Foreground = modifierBrush;
                        }),
                        _ => rich.Foreground(theme),
                    },
                    phase switch
                    {
                        0 or 1 => icon.Foreground(modifierBrush),
                        2 => icon.Foreground(theme),
                        _ => icon,
                    }).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var rich = H.FindControl<WinUI.RichTextBlock>(_ => true);
            var icon = H.FindControl<WinUI.AnimatedIcon>(_ => true);
            H.Check("SharedForeground_ControlsPresent", rich is not null && icon is not null);
            if (rich is null || icon is null) return;

            H.Check("SharedForeground_RichTextModifierApplied", ReferenceEquals(rich.Foreground, modifierBrush));
            H.Check("SharedForeground_IconBrushApplied", ReferenceEquals(icon.Foreground, modifierBrush));

            setPhase!(1);
            await Harness.Render();
            H.Check("SharedForeground_ModifierKeepsPrecedence", ReferenceEquals(rich.Foreground, modifierBrush));

            setPhase!(2);
            await Harness.Render();
            H.Check("SharedForeground_RichTextRestoresTypedBrush", ReferenceEquals(rich.Foreground, nextTypedBrush));
            H.Check("SharedForeground_IconLocalBrushCleared", !HasLocal(icon, WinUI.IconElement.ForegroundProperty));
            H.Check("SharedForeground_IconThemeApplied", ColorOf(icon.Foreground) == LightColor,
                $"foreground={ColorOf(icon.Foreground)}");

            setPhase!(3);
            await Harness.Render();
            H.Check("SharedForeground_RichTextModifierReapplied", ReferenceEquals(rich.Foreground, modifierBrush));
            H.Check("SharedForeground_IconThemeRemoved", ColorOf(icon.Foreground) != LightColor);

            setPhase!(4);
            await Harness.Render();
            H.Check("SharedForeground_RichTextRemovalKeepsSetterBrush", ReferenceEquals(rich.Foreground, setterBrush),
                $"foreground={ColorOf(rich.Foreground)}");

            setPhase!(3);
            await Harness.Render();
            setPhase!(5);
            await Harness.Render();
            H.Check("SharedForeground_RichTextRemovalKeepsSameSetterBrush",
                ReferenceEquals(foregroundBeforeSameBrushSetter, nextTypedBrush)
                && ReferenceEquals(rich.Foreground, modifierBrush),
                $"before setter={ColorOf(foregroundBeforeSameBrushSetter)}, after={ColorOf(rich.Foreground)}");

            setPhase!(6);
            await Harness.Render();
            H.Check("SharedForeground_RichTextThemeApplied", ColorOf(rich.Foreground) == LightColor,
                $"foreground={ColorOf(rich.Foreground)}");
            H.Check("SharedForeground_SameControls",
                ReferenceEquals(rich, H.FindControl<WinUI.RichTextBlock>(_ => true))
                && ReferenceEquals(icon, H.FindControl<WinUI.AnimatedIcon>(_ => true)));
        });
    }

    internal sealed class RichTextForegroundRestoredAfterSkippedRender(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var black = new SolidColorBrush(Microsoft.UI.Colors.Black);
            Brush? declaredRed = null;
            Action<int>? setPhase = null;
            using var host = H.CreateHost();
            try
            {
                host.Mount(ctx =>
                {
                    var (phase, set) = ctx.UseState(0);
                    setPhase = set;
                    var rich = RichTextBlock("foreground").Foreground(black);
                    if (phase == 2) return rich;
                    var covered = rich.Foreground<RichTextBlockElement>("#FF0000");
                    declaredRed = covered.Modifiers!.Foreground;
                    return covered;
                });
                await Harness.Render();

                var text = H.FindControl<WinUI.RichTextBlock>(_ => true);
                var firstRed = declaredRed;
                H.Check("SkippedRichForeground_RedApplied", ColorOf(firstRed) == Microsoft.UI.Colors.Red
                    && ReferenceEquals(text?.Foreground, firstRed));
                if (text is null) return;

                setPhase!(1);
                await Harness.Render();
                H.Check("SkippedRichForeground_EqualBrushSkipped", !ReferenceEquals(declaredRed, firstRed)
                    && ColorOf(declaredRed) == Microsoft.UI.Colors.Red && host.Reconciler.DebugElementsSkipped > 0
                    && ReferenceEquals(text.Foreground, firstRed));

                setPhase!(2);
                await Harness.Render();
                H.Check("SkippedRichForeground_ExplicitBlackRestored",
                    ReferenceEquals(text, H.FindControl<WinUI.RichTextBlock>(_ => true))
                    && ReferenceEquals(text.Foreground, black), $"foreground={ColorOf(text.Foreground)}");
            }
            finally
            {
                H.SetContent(null);
                await Harness.Render();
            }
        }
    }

    internal sealed class RichTextForegroundRemovedDuringHover(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var black = new SolidColorBrush(Microsoft.UI.Colors.Black);
            var red = new SolidColorBrush(Microsoft.UI.Colors.Red);
            var green = new SolidColorBrush(Microsoft.UI.Colors.Green);
            var next = RichTextBlock("foreground").Foreground(black)
                .InteractionStates(s => s.PointerOver(foreground: green));
            var initial = next.Foreground<RichTextBlockElement>(red);
            var text = (WinUI.RichTextBlock)reconciler.Mount(initial, static () => { })!;
            try
            {
                H.Check("RichTextHoverRemoval_CommonRedApplied", ReferenceEquals(text.Foreground, red));
                InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
                H.Check("RichTextHoverRemoval_HoverGreenApplied", ReferenceEquals(text.Foreground, green));

                var replacement = reconciler.Update(initial, next, text, static () => { });
                H.Check("RichTextHoverRemoval_SameControlStaysGreen", replacement is null && ReferenceEquals(text.Foreground, green));

                InteractionStateApplier.SimulatePointerOverForTests(text, isOver: false);
                H.Check("RichTextHoverRemoval_ExplicitBlackRestored", ReferenceEquals(text.Foreground, black),
                    $"foreground={ColorOf(text.Foreground)}");

                var withoutStates = RichTextBlock("foreground").Foreground(black);
                reconciler.Update(next, initial, text, static () => { });
                InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
                H.Check("RichTextHoverRemoval_GreenBeforeDroppingStates", ReferenceEquals(text.Foreground, green));

                replacement = reconciler.Update(initial, withoutStates, text, static () => { });
                H.Check("RichTextHoverRemoval_BothRemovedRestoresBlackImmediately",
                    replacement is null && ReferenceEquals(text.Foreground, black), $"foreground={ColorOf(text.Foreground)}");
                InteractionStateApplier.SimulatePointerOverForTests(text, isOver: false);
                H.Check("RichTextHoverRemoval_BlackRemainsAfterExit", ReferenceEquals(text.Foreground, black));

                reconciler.Update(withoutStates, initial, text, static () => { });
                InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
                H.Check("RichTextHoverRemoval_GreenBeforeSetterUpdate", ReferenceEquals(text.Foreground, green));

                var blue = new SolidColorBrush(Microsoft.UI.Colors.Blue);
                Brush? beforeSetter = null;
                var withSetter = withoutStates.Set(control =>
                {
                    beforeSetter = control.Foreground;
                    control.Foreground = blue;
                });
                bool wroteRemovedBrush = false;
                replacement = UpdateObservingForeground(reconciler, initial, withSetter, text,
                    brush => wroteRemovedBrush |= ReferenceEquals(brush, red));
                H.Check("RichTextHoverRemoval_BlackRestoredBeforeSetter", ReferenceEquals(beforeSetter, black),
                    $"foreground={ColorOf(beforeSetter)}");
                H.Check("RichTextHoverRemoval_SetterBrushWins", replacement is null && ReferenceEquals(text.Foreground, blue));
                H.Check("RichTextHoverRemoval_RemovedBrushWasNeverWritten", !wroteRemovedBrush);
            }
            finally { reconciler.UnmountChild(text); }
            return Task.CompletedTask;
        }
    }

    internal sealed class AnimatedIconInteractionStatesRemovedDuringHover(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var white = new SolidColorBrush(Microsoft.UI.Colors.White);
            var green = new SolidColorBrush(Microsoft.UI.Colors.Green);
            var withoutStates = AnimatedIcon(new WinUI.AnimatedVisuals.AnimatedBackVisualSource()).Foreground(white);
            var initial = withoutStates.InteractionStates(s => s.PointerOver(foreground: green));
            var icon = (WinUI.AnimatedIcon)reconciler.Mount(initial, static () => { })!;
            try
            {
                H.Check("AnimatedIconHoverRemoval_NormalWhiteApplied", ReferenceEquals(icon.Foreground, white));
                InteractionStateApplier.SimulatePointerOverForTests(icon, isOver: true);
                H.Check("AnimatedIconHoverRemoval_HoverGreenApplied", ReferenceEquals(icon.Foreground, green));

                var replacement = reconciler.Update(initial, withoutStates, icon, static () => { });
                H.Check("AnimatedIconHoverRemoval_SameIconRestoresWhiteImmediately",
                    replacement is null && ReferenceEquals(icon.Foreground, white), $"foreground={ColorOf(icon.Foreground)}");

                reconciler.Update(withoutStates, initial, icon, static () => { });
                InteractionStateApplier.SimulatePointerOverForTests(icon, isOver: true);
                H.Check("AnimatedIconHoverRemoval_GreenBeforeSetterUpdate", ReferenceEquals(icon.Foreground, green));

                Brush? beforeSetter = null;
                var withSetter = withoutStates.Set(control =>
                {
                    beforeSetter = control.Foreground;
                    // A setter may intentionally keep the very brush the state wrote.
                    control.Foreground = green;
                });
                replacement = reconciler.Update(initial, withSetter, icon, static () => { });
                H.Check("AnimatedIconHoverRemoval_WhiteRestoredBeforeSetter", ReferenceEquals(beforeSetter, white),
                    $"foreground={ColorOf(beforeSetter)}");
                H.Check("AnimatedIconHoverRemoval_SameInstanceSetterBrushWins",
                    replacement is null && ReferenceEquals(icon.Foreground, green));
            }
            finally { reconciler.UnmountChild(icon); }
            return Task.CompletedTask;
        }
    }

    internal sealed class FlyoutKeepsHoverForegroundWhenTargetDropsInteractionStates(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var white = new SolidColorBrush(Microsoft.UI.Colors.White);
            var green = new SolidColorBrush(Microsoft.UI.Colors.Green);
            var purple = new SolidColorBrush(Microsoft.UI.Colors.Purple);
            var target = AnimatedIcon(new WinUI.AnimatedVisuals.AnimatedBackVisualSource()).Foreground(white);
            var targetWithStates = target.InteractionStates(s => s.PointerOver(opacity: 0.8f, foreground: purple));
            (string Name, Func<Element, FlyoutElement> Wrap)[] cases =
            {
                ("FlyoutHoverForeground", WithHover),
                ("MiddleFlyoutHoverForeground", child => Flyout(WithHover(child), TextBlock("outer flyout"))),
                ("OutermostFlyoutHoverForeground", child => WithHover(Flyout(child, TextBlock("middle flyout")))),
            };
            foreach (var (name, wrap) in cases)
            {
                var initial = wrap(targetWithStates);
                var next = wrap(target);
                var icon = (WinUI.AnimatedIcon)reconciler.Mount(initial, static () => { })!;
                try
                {
                    H.Check(name + "_NormalWhiteApplied", ReferenceEquals(icon.Foreground, white));
                    InteractionStateApplier.SimulatePointerOverForTests(icon, isOver: true);
                    H.Check(name + "_OuterGreenApplied", ReferenceEquals(icon.Foreground, green));

                    var replacement = reconciler.Update(initial, next, icon, static () => { });
                    H.Check(name + "_TargetDropsStatesOuterStaysGreen",
                        replacement is null && ReferenceEquals(icon.Foreground, green), $"foreground={ColorOf(icon.Foreground)}");

                    InteractionStateApplier.SimulatePointerOverForTests(icon, isOver: false);
                    H.Check(name + "_ExitRestoresWhite", ReferenceEquals(icon.Foreground, white),
                        $"foreground={ColorOf(icon.Foreground)}");
                }
                finally { reconciler.UnmountChild(icon); }
            }

            var firstNormal = new SolidColorBrush(Microsoft.UI.Colors.Black);
            var rebuiltNormal = new SolidColorBrush(Microsoft.UI.Colors.Black);
            var latestNormal = new SolidColorBrush(Microsoft.UI.Colors.Black);
            var targetText = TextBlock("rebuilt normal foreground").Foreground(firstNormal);
            var hoveredFlyout = Flyout(targetText.InteractionStates(s => s.PointerOver(opacity: 0.8f)), TextBlock("flyout"))
                .InteractionStates(s => s.PointerOver(foreground: green));
            var rebuiltFlyout = hoveredFlyout with { Target = targetText.Foreground(rebuiltNormal) };
            // A text change forces the second update even though the latest brush is also black.
            const string UpdatedText = "rebuilt again";
            var latestFlyout = rebuiltFlyout with { Target = TextBlock(UpdatedText).Foreground(latestNormal) };
            foreach (var (name, rebuildAgain) in new[]
            {
                ("FlyoutRebuiltForeground", false),
                ("FlyoutRepeatedRebuildForeground", true),
            })
            {
                var textBlock = (WinUI.TextBlock)reconciler.Mount(hoveredFlyout, static () => { })!;
                try
                {
                    H.Check($"{name}_FirstNormalApplied", ReferenceEquals(textBlock.Foreground, firstNormal));
                    InteractionStateApplier.SimulatePointerOverForTests(textBlock, isOver: true);
                    H.Check($"{name}_OuterHoverApplied", ReferenceEquals(textBlock.Foreground, green));

                    var replacement = reconciler.Update(hoveredFlyout, rebuiltFlyout, textBlock, static () => { });
                    H.Check($"{name}_SameTargetStaysHovered",
                        replacement is null && ReferenceEquals(textBlock.Foreground, green), $"foreground={ColorOf(textBlock.Foreground)}");

                    if (rebuildAgain)
                    {
                        replacement = reconciler.Update(rebuiltFlyout, latestFlyout, textBlock, static () => { });
                        H.Check($"{name}_SecondUpdateStaysHovered",
                            replacement is null && textBlock.Text == UpdatedText && ReferenceEquals(textBlock.Foreground, green),
                            $"text={textBlock.Text}, foreground={ColorOf(textBlock.Foreground)}");
                    }

                    var expectedNormal = rebuildAgain ? latestNormal : rebuiltNormal;
                    InteractionStateApplier.SimulatePointerOverForTests(textBlock, isOver: false);
                    H.Check($"{name}_ExitRestoresRebuiltBrush", ReferenceEquals(textBlock.Foreground, expectedNormal),
                        $"foreground={ColorOf(textBlock.Foreground)}");
                }
                finally { reconciler.UnmountChild(textBlock); }
            }

            var black = new SolidColorBrush(Microsoft.UI.Colors.Black);
            var red = new SolidColorBrush(Microsoft.UI.Colors.Red);
            var blue = new SolidColorBrush(Microsoft.UI.Colors.Blue);
            var noFallback = RichTextBlock("foreground");
            var textTarget = noFallback.Foreground(black);
            var hoveredTarget = textTarget.InteractionStates(s => s.PointerOver(foreground: blue));
            foreach (var (name, oldTarget, nextTarget, normal, restored) in new[]
            {
                ("KeepsBrush", hoveredTarget, textTarget, (Brush?)black, (Brush?)black),
                ("DropsCommonBrush", hoveredTarget.Foreground<RichTextBlockElement>(red), textTarget, red, black),
                ("DropsCommonBrushWithoutFallback", noFallback.Foreground<RichTextBlockElement>(red)
                    .InteractionStates(s => s.PointerOver(foreground: blue)), noFallback, red, null),
                ("AddsFirstCommonBrush", noFallback.InteractionStates(s => s.PointerOver(opacity: 0.8f)),
                    noFallback.Foreground<RichTextBlockElement>(blue), null, blue),
            })
            foreach (var (layer, wrap) in new (string, Func<Element, Element>)[]
            {
                ("Direct", static child => child),
                ("Menu", static child => MenuFlyout(child)),
                ("CommandBar", static child => CommandBarFlyout(child)),
            })
            {
                var caseName = $"FlyoutHoverForeground_{layer}_{name}";
                var oldFlyout = Flyout(wrap(oldTarget), TextBlock("flyout"))
                    .InteractionStates(s => s.PointerOver(foreground: green));
                var newFlyout = oldFlyout with { Target = wrap(nextTarget) };
                var text = (WinUI.RichTextBlock)reconciler.Mount(oldFlyout, static () => { })!;
                try
                {
                    H.Check($"{caseName}_NormalApplied", normal is null
                        ? !HasLocal(text, WinUI.RichTextBlock.ForegroundProperty) : ReferenceEquals(text.Foreground, normal));
                    InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
                    H.Check($"{caseName}_OuterGreenApplied", ReferenceEquals(text.Foreground, green));

                    var replacement = reconciler.Update(oldFlyout, newFlyout, text, static () => { });
                    H.Check($"{caseName}_OuterStaysGreen",
                        replacement is null && ReferenceEquals(text.Foreground, green), $"foreground={ColorOf(text.Foreground)}");

                    InteractionStateApplier.SimulatePointerOverForTests(text, isOver: false);
                    H.Check($"{caseName}_ExitRestoresNormal", restored is null
                        ? !HasLocal(text, WinUI.RichTextBlock.ForegroundProperty)
                        : ReferenceEquals(text.Foreground, restored),
                        $"foreground={ColorOf(text.Foreground)}");
                }
                finally { reconciler.UnmountChild(text); }
            }
            return Task.CompletedTask;

            FlyoutElement WithHover(Element child) => Flyout(child, TextBlock("flyout"))
                .InteractionStates(s => s.PointerOver(foreground: green));
        }
    }

    internal sealed class FlyoutReleasesRemovedHoverForeground(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var normal = new SolidColorBrush(Microsoft.UI.Colors.Black);
            var common = new SolidColorBrush(ExplicitColor);
            var hover = new SolidColorBrush(HoverColor);
            var target = RichTextBlock("foreground").Foreground(normal);
            var initial = Flyout(target.Foreground<RichTextBlockElement>(common)
                    .InteractionStates(s => s.PointerOver(opacity: 0.9f)), TextBlock("flyout"))
                .InteractionStates(s => s.PointerOver(foreground: hover));
            var innerRemoved = initial with { Target = target };

            var text = (WinUI.RichTextBlock)reconciler.Mount(initial, static () => { })!;
            try
            {
                InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
                reconciler.Update(initial, innerRemoved, text, static () => { });
                H.Check("FlyoutReleaseForeground_OuterStillHovered", ReferenceEquals(text.Foreground, hover));

                Brush? beforeFlyoutSetter = null;
                var withoutStates = (innerRemoved with { InteractionStates = null }).Set(_ =>
                {
                    beforeFlyoutSetter = text.Foreground;
                    text.Foreground = hover;
                });
                var replacement = reconciler.Update(innerRemoved, withoutStates, text, static () => { });
                H.Check("FlyoutReleaseForeground_UnchangedTargetRestoresNormalBeforeFlyoutSetter",
                    ReferenceEquals(beforeFlyoutSetter, normal), $"before setter={ColorOf(beforeFlyoutSetter)}");
                H.Check("FlyoutReleaseForeground_FlyoutSetterCanReuseHoverBrush",
                    replacement is null && ReferenceEquals(text.Foreground, hover), $"foreground={ColorOf(text.Foreground)}");
            }
            finally { reconciler.UnmountChild(text); }

            text = (WinUI.RichTextBlock)reconciler.Mount(initial, static () => { })!;
            try
            {
                InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
                H.Check("FlyoutReleaseForeground_OuterHoverBeforeChannelRemoval", ReferenceEquals(text.Foreground, hover));
                var setterBrush = new SolidColorBrush(OtherExplicitColor);
                Brush? beforeSetter = null;
                var withSetter = innerRemoved with
                {
                    Target = target.Set(control =>
                    {
                        beforeSetter = control.Foreground;
                        control.Foreground = setterBrush;
                    }),
                };
                var opacityOnly = withSetter.InteractionStates(s => s.PointerOver(opacity: 0.8f));
                bool wroteRemovedBrush = false;
                UpdateObservingForeground(reconciler, initial, opacityOnly, text,
                    brush => wroteRemovedBrush |= ReferenceEquals(brush, common));
                H.Check("FlyoutReleaseForeground_OpacityOnlyRestoresNormalBeforeSetter",
                    ReferenceEquals(beforeSetter, normal), $"before setter={ColorOf(beforeSetter)}");
                H.Check("FlyoutReleaseForeground_OpacityOnlyKeepsSetterBrush", ReferenceEquals(text.Foreground, setterBrush));
                H.Check("FlyoutReleaseForeground_RemovedBrushWasNeverWritten", !wroteRemovedBrush);
            }
            finally { reconciler.UnmountChild(text); }

            return Task.CompletedTask;
        }
    }

    internal sealed class FlyoutRestoresThemeForegroundWhenTargetDropsInteractionStates(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsFlyoutHoverForeground", async host =>
        {
            var theme = Theme.Ref("TrsFlyoutHoverForeground");
            var hover = new SolidColorBrush(HoverColor);
            var innerHover = new SolidColorBrush(ExplicitColor);
            const string Caption = "themed flyout target";
            Action<bool>? dropTargetStates = null;
            host.Mount(ctx =>
            {
                var (dropped, setDropped) = ctx.UseState(false);
                dropTargetStates = setDropped;
                var target = TextBlock(Caption).Foreground(theme);
                if (!dropped)
                    target = target.InteractionStates(s => s.PointerOver(foreground: innerHover));
                return VStack(Flyout(target, TextBlock("flyout"))
                    .InteractionStates(s => s.PointerOver(foreground: hover))).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var text = H.FindText(Caption);
            H.Check("FlyoutHoverTheme_NormalIsLight", ColorOf(text?.Foreground) == LightColor);
            if (text is null) return;
            InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
            H.Check("FlyoutHoverTheme_OuterHoverApplied", ReferenceEquals(text.Foreground, hover));

            dropTargetStates!(true);
            await Harness.Render();
            H.Check("FlyoutHoverTheme_TargetDropsStatesOuterStaysHovered",
                ReferenceEquals(H.FindText(Caption), text) && ReferenceEquals(text.Foreground, hover));

            InteractionStateApplier.SimulatePointerOverForTests(text, isOver: false);
            H.Check("FlyoutHoverTheme_ExitReleasesLocalBrush",
                !HasLocal(text, WinUI.TextBlock.ForegroundProperty) && ColorOf(text.Foreground) == LightColor,
                $"foreground={ColorOf(text.Foreground)}");
            H.Check("FlyoutHoverTheme_FollowsDarkAfterExit",
                await FlipToDark(text, () => ColorOf(text.Foreground) == DarkColor),
                $"foreground={ColorOf(text.Foreground)}");
        });
    }

    internal sealed class DerivedIconThemeForeground(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsDerivedIcon", async host =>
        {
            host.Mount(_ => VStack(new XamlHostElement(() => new ThemeFixtureFontIcon()).Foreground(Theme.Ref("TrsDerivedIcon")))
                .RequestedTheme(ElementTheme.Light));
            await Harness.Render();
            var icon = H.FindControl<ThemeFixtureFontIcon>(_ => true);
            H.Check("DerivedIconForeground_Mounted", icon is not null);
            if (icon is null) return;
            H.Check("DerivedIconForeground_Light", ColorOf(icon.Foreground) == LightColor);
            H.Check("DerivedIconForeground_Dark", await FlipToDark(icon, () => ColorOf(icon.Foreground) == DarkColor));
        });
    }

    internal sealed class RichTextForegroundResetOutranksSetter(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var common = new SolidColorBrush(ExplicitColor);
            var setterBrush = new SolidColorBrush(OtherExplicitColor);
            var initial = RichTextBlock("reset").Foreground<RichTextBlockElement>(common);
            var text = (WinUI.RichTextBlock)reconciler.Mount(initial, static () => { })!;
            try
            {
                H.Check("RichTextReset_CommonApplied", ReferenceEquals(text.Foreground, common));
                bool setterRan = false;
                var next = RichTextBlock("reset").Set(control =>
                {
                    control.Foreground = setterBrush;
                    setterRan = ReferenceEquals(control.Foreground, setterBrush);
                });
                var replacement = reconciler.Update(initial, next, text, static () => { });
                H.Check("RichTextReset_SetterRanOnSameControl", replacement is null && setterRan);
                H.Check("RichTextReset_LocalValueCleared", !HasLocal(text, WinUI.RichTextBlock.ForegroundProperty));
            }
            finally { reconciler.UnmountChild(text); }
            return Task.CompletedTask;
        }
    }

    internal sealed class RichTextHoverForegroundWithoutTypedFallback(Harness h, bool sharedFlyout = false,
        bool replaceCommon = false, bool commonOnFlyout = false)
        : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var common = new SolidColorBrush(Microsoft.UI.Colors.Red);
            var hover = new SolidColorBrush(HoverColor);
            var setterBrush = new SolidColorBrush(OtherExplicitColor);
            var replacementBrush = replaceCommon ? new SolidColorBrush(Microsoft.UI.Colors.Blue) : null;
            var target = RichTextBlock("reset");
            if (!commonOnFlyout) target = target.Foreground<RichTextBlockElement>(common);
            Element initial = target.InteractionStates(s => s.PointerOver(foreground: hover));
            if (sharedFlyout)
            {
                var flyout = Flyout(target.InteractionStates(s => s.PointerOver(foreground: setterBrush)), TextBlock("flyout"))
                    .InteractionStates(s => s.PointerOver(foreground: hover));
                initial = commonOnFlyout ? ElementExtensions.Foreground(flyout, common) : flyout;
            }
            var name = commonOnFlyout ? "OuterFlyoutCommon" : sharedFlyout ? "FlyoutTargetCommon" : "RichTextCommon";
            name += replaceCommon ? "Replaced" : "Removed";
            var text = (WinUI.RichTextBlock)reconciler.Mount(initial, static () => { })!;
            try
            {
                H.Check($"{name}_NormalBrushApplied", ReferenceEquals(text.Foreground, common));
                InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
                H.Check($"{name}_HoverApplied", ReferenceEquals(text.Foreground, hover));
                Brush? beforeSetter = null;
                bool setterFinished = false;
                var nextTarget = RichTextBlock("reset").Set(control =>
                {
                    beforeSetter = control.Foreground;
                    control.Foreground = setterBrush;
                    setterFinished = true;
                });
                if (replacementBrush is not null && !commonOnFlyout)
                    nextTarget = nextTarget.Foreground<RichTextBlockElement>(replacementBrush);
                Element next = initial is FlyoutElement flyout
                    ? (flyout with { Target = nextTarget }).InteractionStates(s => s.PointerOver(opacity: 0.8f))
                    : nextTarget;
                if (commonOnFlyout) next = next with { Modifiers = initial.Modifiers! with { Foreground = replacementBrush } };
                bool wroteRemovedBrush = false;
                bool wroteReplacementBeforeSetter = false;
                bool wroteReplacementAfterSetter = false;
                var replacement = UpdateObservingForeground(reconciler, initial, next, text, value =>
                {
                    wroteRemovedBrush |= ReferenceEquals(value, common);
                    if (replacementBrush is not null && ReferenceEquals(value, replacementBrush))
                    {
                        wroteReplacementBeforeSetter |= !setterFinished;
                        wroteReplacementAfterSetter |= setterFinished;
                    }
                });
                H.Check($"{name}_SetterRunsBeforeCommonModifier",
                    ReferenceEquals(beforeSetter, hover), $"before setter={ColorOf(beforeSetter)}");
                H.Check($"{name}_RemovedBrushWasNeverWritten", !wroteRemovedBrush);
                if (replacementBrush is null)
                    H.Check($"{name}_LocalValueCleared",
                        replacement is null && !HasLocal(text, WinUI.RichTextBlock.ForegroundProperty));
                else
                    H.Check($"{name}_ReplacementAppliedAfterSetter",
                        replacement is null && !wroteReplacementBeforeSetter && wroteReplacementAfterSetter
                        && ReferenceEquals(text.Foreground, replacementBrush), $"foreground={ColorOf(text.Foreground)}");

                if (sharedFlyout)
                {
                    text.Foreground = hover;
                    InteractionStateApplier.SimulatePointerOverForTests(text, isOver: false);
                    H.Check($"{name}_ExitKeepsLaterBrushWrite", ReferenceEquals(text.Foreground, hover));
                }
            }
            finally { reconciler.UnmountChild(text); }
            return Task.CompletedTask;
        }
    }

    internal sealed class MenuOverlaysRestoreRichTextForegroundBeforeSetter(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var normal = new SolidColorBrush(Microsoft.UI.Colors.Black);
            var common = new SolidColorBrush(Microsoft.UI.Colors.Red);
            var hover = new SolidColorBrush(Microsoft.UI.Colors.Green);
            var setterBrush = new SolidColorBrush(Microsoft.UI.Colors.Blue);
            var target = RichTextBlock("foreground").Foreground(normal);
            (string Name, Func<Element, Element> Wrap)[] cases =
            {
                ("MenuFlyout", static child => MenuFlyout(child)),
                ("CommandBarFlyout", static child => CommandBarFlyout(child)),
            };
            foreach (var (name, wrap) in cases)
            {
                var initial = wrap(target.Foreground<RichTextBlockElement>(common)
                    .InteractionStates(s => s.PointerOver(opacity: 0.9f)))
                    .InteractionStates(s => s.PointerOver(foreground: hover));
                var text = (WinUI.RichTextBlock)reconciler.Mount(initial, static () => { })!;
                try
                {
                    InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
                    H.Check($"{name}_HoverBrushApplied", ReferenceEquals(text.Foreground, hover));
                    Brush? beforeSetter = null;
                    var next = wrap(target.Set(control =>
                    {
                        beforeSetter = control.Foreground;
                        control.Foreground = setterBrush;
                    })).InteractionStates(s => s.PointerOver(opacity: 0.8f));
                    var replacement = reconciler.Update(initial, next, text, static () => { });
                    H.Check($"{name}_ExplicitBrushRestoredBeforeSetter", ReferenceEquals(beforeSetter, normal),
                        $"before setter={ColorOf(beforeSetter)}");
                    H.Check($"{name}_SetterBrushWins", replacement is null && ReferenceEquals(text.Foreground, setterBrush));
                }
                finally { reconciler.UnmountChild(text); }

                var leaf = TextBlock("before").Foreground(normal);
                var inner = Flyout(leaf, TextBlock("inner flyout"))
                    .InteractionStates(s => s.PointerOver(opacity: 0.8f));
                var mixed = wrap(inner).InteractionStates(s => s.PointerOver(foreground: hover));
                var mixedNext = wrap(inner with { Target = leaf with { Content = "after" } })
                    .InteractionStates(s => s.PointerOver(foreground: hover));
                var textBlock = (WinUI.TextBlock)reconciler.Mount(mixed, static () => { })!;
                try
                {
                    InteractionStateApplier.SimulatePointerOverForTests(textBlock, isOver: true);
                    H.Check($"{name}_NestedHoverApplied", ReferenceEquals(textBlock.Foreground, hover));
                    var replacement = reconciler.Update(mixed, mixedNext, textBlock, static () => { });
                    H.Check($"{name}_NestedTextUpdateKeepsHover", replacement is null
                        && textBlock.Text == "after" && ReferenceEquals(textBlock.Foreground, hover));
                    InteractionStateApplier.SimulatePointerOverForTests(textBlock, isOver: false);
                    H.Check($"{name}_NestedExitRestoresNormal", ReferenceEquals(textBlock.Foreground, normal));
                }
                finally { reconciler.UnmountChild(textBlock); }
            }
            return Task.CompletedTask;
        }
    }

    internal sealed class CustomFlyoutRestoresForegroundBeforeSetter(Harness h) : SelfTestFixtureBase(h)
    {
        private sealed class Forwarder : IDecoratorElementHandler<FlyoutElement>
        {
            public UIElement Mount(MountContext ctx, FlyoutElement element) => ctx.MountChild(element.Target)!;
            public UIElement Update(UpdateContext ctx, FlyoutElement previous, FlyoutElement current, UIElement control)
                => ctx.Reconciler.Update(previous.Target, current.Target, control, ctx.RequestRerender) ?? control;
            public V1UnmountDisposition Unmount(UnmountContext ctx, FlyoutElement? element, UIElement control)
            {
                if (element is not null && control is FrameworkElement target)
                    Reconciler.SetElementTag(target, element.Target);
                ctx.Reconciler.UnmountChild(control);
                return V1UnmountDisposition.SkipPool;
            }
            public Element? GetSourceTarget(FlyoutElement element) => element.Target;
        }

        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            // Use the host-local registration so the built-in stays available to other fixtures.
            reconciler.RegisterDecoratorHandler(new Forwarder());
            var normal = new SolidColorBrush(Microsoft.UI.Colors.Black);
            var common = new SolidColorBrush(Microsoft.UI.Colors.Red);
            var hover = new SolidColorBrush(Microsoft.UI.Colors.Green);
            var setterBrush = new SolidColorBrush(Microsoft.UI.Colors.Blue);
            var target = RichTextBlock("foreground").Foreground(normal);
            var initial = Flyout(target.Foreground<RichTextBlockElement>(common)
                .InteractionStates(s => s.PointerOver(opacity: 0.9f)), TextBlock("flyout"))
                .InteractionStates(s => s.PointerOver(foreground: hover));
            var text = (WinUI.RichTextBlock)reconciler.Mount(initial, static () => { })!;
            try
            {
                InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
                H.Check("CustomFlyout_HoverApplied", ReferenceEquals(text.Foreground, hover));
                Brush? beforeSetter = null;
                var next = (initial with { Target = target.Set(control =>
                {
                    beforeSetter = control.Foreground;
                    control.Foreground = setterBrush;
                }) }).InteractionStates(s => s.PointerOver(opacity: 0.8f));
                var replacement = reconciler.Update(initial, next, text, static () => { });
                H.Check("CustomFlyout_RestoresNormalBeforeSetter", ReferenceEquals(beforeSetter, normal));
                H.Check("CustomFlyout_SetterWins", replacement is null && ReferenceEquals(text.Foreground, setterBrush));
            }
            finally { reconciler.UnmountChild(text); }
            return Task.CompletedTask;
        }
    }

    internal sealed class FlyoutThroughTransparentDecorator(Harness h) : SelfTestFixtureBase(h)
    {
        private sealed record Forwarder(Element Target, Action<UIElement>? AfterUpdate = null) : Element;

        private sealed class ForwarderHandler : IDecoratorElementHandler<Forwarder>
        {
            public UIElement Mount(MountContext ctx, Forwarder element) => ctx.MountChild(element.Target)!;
            public UIElement Update(UpdateContext ctx, Forwarder previous, Forwarder current, UIElement control)
            {
                var next = ctx.ReconcileChild(previous.Target, current.Target, control)!;
                current.AfterUpdate?.Invoke(next);
                return next;
            }
            public V1UnmountDisposition Unmount(UnmountContext ctx, Forwarder? element, UIElement control)
            {
                if (element is not null && control is FrameworkElement target)
                    Reconciler.SetElementTag(target, element.Target);
                ctx.Reconciler.UnmountChild(control);
                return V1UnmountDisposition.SkipPool;
            }
            public Element? GetSourceTarget(Forwarder element) => element.Target;
        }

        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            reconciler.RegisterDecoratorHandler(new ForwarderHandler());
            var normal = new SolidColorBrush(Microsoft.UI.Colors.Black);
            var common = new SolidColorBrush(Microsoft.UI.Colors.Red);
            var hover = new SolidColorBrush(Microsoft.UI.Colors.Green);
            var setterBrush = new SolidColorBrush(Microsoft.UI.Colors.Purple);
            var replacementBrush = new SolidColorBrush(Microsoft.UI.Colors.Blue);
            foreach (var (name, typedFallback, replaceCommon) in new[]
            {
                ("TypedFallback", true, false), ("ClearCommon", false, false), ("ReplaceCommon", false, true),
            })
            {
                var target = RichTextBlock("forwarded");
                if (typedFallback) target = target.Foreground(normal);
                var initial = Flyout(new Forwarder(target.Foreground<RichTextBlockElement>(common)
                    .InteractionStates(s => s.PointerOver(opacity: 0.9f))), TextBlock("flyout"))
                    .InteractionStates(s => s.PointerOver(foreground: hover));
                Brush? beforeSetter = null;
                var nextTarget = target.Set(text =>
                {
                    beforeSetter = text.Foreground;
                    text.Foreground = setterBrush;
                });
                if (replaceCommon) nextTarget = nextTarget.Foreground<RichTextBlockElement>(replacementBrush);
                var next = (initial with { Target = new Forwarder(nextTarget) })
                    .InteractionStates(s => s.PointerOver(opacity: 0.8f));
                var text = (WinUI.RichTextBlock)reconciler.Mount(initial, static () => { })!;
                try
                {
                    H.Check($"Forwarded_{name}_CommonApplied", ReferenceEquals(text.Foreground, common));
                    InteractionStateApplier.SimulatePointerOverForTests(text, isOver: true);
                    H.Check($"Forwarded_{name}_HoverApplied", ReferenceEquals(text.Foreground, hover));
                    bool wroteRemovedBrush = false;
                    var replacement = UpdateObservingForeground(reconciler, initial, next, text,
                        brush => wroteRemovedBrush |= ReferenceEquals(brush, common));
                    H.Check($"Forwarded_{name}_NoRemovedBrushWrite", !wroteRemovedBrush);
                    H.Check($"Forwarded_{name}_SetterOrder", ReferenceEquals(beforeSetter, typedFallback ? normal : hover));
                    var expectedBrush = typedFallback ? setterBrush : replacementBrush;
                    H.Check($"Forwarded_{name}_FinalBrush", replacement is null && (typedFallback || replaceCommon
                        ? ReferenceEquals(text.Foreground, expectedBrush)
                        : !HasLocal(text, WinUI.RichTextBlock.ForegroundProperty)));
                }
                finally { reconciler.UnmountChild(text); }
            }

            var unchanged = RichTextBlock("unchanged").Foreground(normal).Foreground<RichTextBlockElement>(common);
            foreach (bool replaceTarget in new[] { false, true })
            {
                var initial = Flyout(new Forwarder(unchanged), TextBlock("flyout"))
                    .InteractionStates(s => s.PointerOver(foreground: hover));
                Element nextTarget = replaceTarget ? Icon(FontIcon("A")).Foreground(normal) : unchanged;
                Brush? beforeSetter = null;
                var next = (initial with { Target = new Forwarder(nextTarget, control =>
                {
                    beforeSetter = control is WinUI.RichTextBlock text ? text.Foreground : ((WinUI.FontIcon)control).Foreground;
                }) }).InteractionStates(s => s.PointerOver(opacity: 0.8f));
                var control = reconciler.Mount(initial, static () => { })!;
                try
                {
                    InteractionStateApplier.SimulatePointerOverForTests(control, isOver: true);
                    H.Check($"Forwarded_SkipOrReplace_{replaceTarget}_HoverApplied", ReferenceEquals(((WinUI.RichTextBlock)control).Foreground, hover));
                    var replacement = reconciler.Update(initial, next, control, static () => { });
                    if (replacement is not null) control = replacement;
                    H.Check($"Forwarded_SkipOrReplace_{replaceTarget}_ControlIdentity", replaceTarget == (replacement is WinUI.FontIcon));
                    H.Check($"Forwarded_SkipOrReplace_{replaceTarget}_RestoredBeforeCallback",
                        ReferenceEquals(beforeSetter, replaceTarget ? normal : common));
                }
                finally { reconciler.UnmountChild(control); }
            }
            return Task.CompletedTask;
        }
    }

    internal sealed class PooledRichTextDoesNotKeepForeground(Harness h) : SelfTestFixtureBase(h)
    {
        private sealed class PooledHandler : IElementHandler<RichTextBlockElement, WinUI.RichTextBlock>
        {
            public WinUI.RichTextBlock Mount(MountContext ctx, RichTextBlockElement element)
            {
                var control = ctx.RentControl<WinUI.RichTextBlock>();
                Reconciler.SetElementTag(control, element);
                return control;
            }

            public void Update(UpdateContext ctx, RichTextBlockElement oldEl, RichTextBlockElement newEl, WinUI.RichTextBlock control) { }

            public void Unmount(UnmountContext ctx, WinUI.RichTextBlock control) => ctx.ReturnControl(control);
        }

        public override Task RunAsync()
        {
            using var reconciler = new Reconciler();
            reconciler.RegisterHandler(new PooledHandler());
            var brush = new SolidColorBrush(ExplicitColor);
            var first = (WinUI.RichTextBlock)reconciler.Mount(
                RichTextBlock("first").Foreground<RichTextBlockElement>(brush), static () => { })!;
            try
            {
                H.Check("PooledRichText_FirstBrushApplied", ReferenceEquals(first.Foreground, brush));
            }
            finally { reconciler.UnmountChild(first); }

            var second = (WinUI.RichTextBlock)reconciler.Mount(RichTextBlock("second"), static () => { })!;
            try
            {
                H.Check("PooledRichText_SameControl", ReferenceEquals(first, second));
                H.Check("PooledRichText_NoLocalForeground",
                    !HasLocal(second, WinUI.RichTextBlock.ForegroundProperty), $"foreground={ColorOf(second.Foreground)}");
            }
            finally { reconciler.UnmountChild(second); }
            return Task.CompletedTask;
        }
    }

    internal sealed class PolymorphicIconKeepsForeground(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var brush = new SolidColorBrush(ExplicitColor);
            Action<bool>? setFontIcon = null;
            using var host = H.CreateHost();
            try
            {
                host.Mount(ctx =>
                {
                    var (fontIcon, set) = ctx.UseState(false);
                    setFontIcon = set;
                    return VStack(Icon(fontIcon ? FontIcon("\uE8A5") : SymbolIcon("Save")).Foreground(brush));
                });
                await Harness.Render();

                var symbol = H.FindControl<WinUI.SymbolIcon>(_ => true);
                H.Check("PolymorphicIcon_SymbolHasForeground", ReferenceEquals(symbol?.Foreground, brush));

                setFontIcon!(true);
                await Harness.Render();
                var font = H.FindControl<WinUI.FontIcon>(_ => true);
                H.Check("PolymorphicIcon_FontHasSameForeground", ReferenceEquals(font?.Foreground, brush),
                    $"foreground={ColorOf(font?.Foreground)}");
            }
            finally
            {
                H.SetContent(null);
                await Harness.Render();
            }
        }
    }

    internal sealed class RichTextBlockForeground(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsRichTextForeground", async host =>
        {
            var brush = new SolidColorBrush(ExplicitColor);
            Action<int>? setPhase = null;
            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                var text = RichTextBlock("themed rich text");
                return VStack(phase switch
                {
                    0 or 2 => text.Foreground(Theme.Ref("TrsRichTextForeground")),
                    1 => text.Foreground(brush),
                    _ => text,
                }).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var text = H.FindControl<WinUI.RichTextBlock>(_ => true);
            H.Check("ThemeRefStyle_RichText_ForegroundMounted", ColorOf(text?.Foreground) == LightColor,
                $"foreground={ColorOf(text?.Foreground)}");
            if (text is null) return;

            H.Check("ThemeRefStyle_RichText_FollowsTheme",
                await FlipToDark(text, () => ColorOf(text.Foreground) == DarkColor),
                $"foreground={ColorOf(text.Foreground)}");

            setPhase!(1);
            await Harness.Render();
            H.Check("ThemeRefStyle_RichText_ExplicitBrush", ReferenceEquals(text.Foreground, brush));

            setPhase!(2);
            await Harness.Render();
            var expected = text.ActualTheme == ElementTheme.Dark ? DarkColor : LightColor;
            H.Check("ThemeRefStyle_RichText_ExplicitToThemed", ColorOf(text.Foreground) == expected,
                $"foreground={ColorOf(text.Foreground)}");

            setPhase!(3);
            await Harness.Render();
            H.Check("ThemeRefStyle_RichText_BindingRemoved",
                ColorOf(text.Foreground) != LightColor && ColorOf(text.Foreground) != DarkColor,
                $"foreground={ColorOf(text.Foreground)}");
            H.Check("ThemeRefStyle_RichText_SameControl",
                ReferenceEquals(H.FindControl<WinUI.RichTextBlock>(_ => true), text));
        });
    }

    /// <summary>
    /// <c>SplitView.PaneBackground(ThemeRef)</c> themes the pane background (it had no Style
    /// mapping and silently did nothing), including when it replaces an explicit
    /// <c>.PaneBackground(brush)</c> on the same control, whose stale local value would hide it.
    /// Writing the themed Style to the already-loaded SplitView, and taking it off again, must
    /// not fail: a loaded SplitView rejects a Style not built on its default style.
    /// </summary>
    internal sealed class SplitViewPaneBackground(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsPaneBrush", async host =>
        {
            var explicitBrush = new SolidColorBrush(ExplicitColor);
            Action<int>? setPhase = null;
            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                // Phase 2 changes the SplitView itself (update arm); phase 3 changes only a sibling,
                // so the unchanged SplitView takes the skip arm. Both re-apply the themed Style to
                // the loaded control.
                var sv = SplitView(TextBlock("trs-pane"), TextBlock("trs-content")).Height(phase >= 2 ? 81 : 80);
                return VStack(
                    TextBlock(phase == 3 ? "trs-pane-sibling-3" : "trs-pane-sibling"),
                    phase switch
                    {
                        0 => sv.PaneBackground(explicitBrush),
                        1 or 2 or 3 => sv.PaneBackground(new ThemeRef("TrsPaneBrush")),
                        _ => sv,
                    }).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var sv = H.FindControl<WinUI.SplitView>(_ => true);
            H.Check("ThemeRefStyle_Pane_ExplicitBrush", ReferenceEquals(sv?.PaneBackground, explicitBrush));

            setPhase!(1);
            await Harness.Render();
            H.Check("ThemeRefStyle_Pane_SameControl", ReferenceEquals(H.FindControl<WinUI.SplitView>(_ => true), sv));
            H.Check("ThemeRefStyle_Pane_ExplicitCleared", !HasLocal(sv, WinUI.SplitView.PaneBackgroundProperty));
            H.Check("ThemeRefStyle_Pane_Themed", ColorOf(sv?.PaneBackground) == LightColor,
                $"pane={ColorOf(sv?.PaneBackground)}");
            if (sv is null) return;
            H.Check("ThemeRefStyle_Pane_FollowsTheme",
                await FlipToDark(sv, () => ColorOf(sv.PaneBackground) == DarkColor),
                $"pane={ColorOf(sv.PaneBackground)}");

            foreach (var (phase, name) in new[] { (2, "Update"), (3, "Skip") })
            {
                setPhase!(phase);
                await Harness.Render();
                H.Check($"ThemeRefStyle_Pane_{name}_SameControl", ReferenceEquals(H.FindControl<WinUI.SplitView>(_ => true), sv));
                H.Check($"ThemeRefStyle_Pane_{name}_StillThemed", ColorOf(sv.PaneBackground) is { } c && (c == DarkColor || c == LightColor),
                    $"pane={ColorOf(sv.PaneBackground)} height={sv.Height}");
            }
            H.Check("ThemeRefStyle_Pane_UpdateApplied", Math.Abs(sv.Height - 81) < 0.01 && H.FindText("trs-pane-sibling-3") is not null);

            setPhase!(4);
            await Harness.Render();
            H.Check("ThemeRefStyle_Pane_Removed_SameControl", ReferenceEquals(H.FindControl<WinUI.SplitView>(_ => true), sv));
            H.Check("ThemeRefStyle_Pane_Removed_NotThemed",
                ColorOf(sv.PaneBackground) != LightColor && ColorOf(sv.PaneBackground) != DarkColor,
                $"pane={ColorOf(sv.PaneBackground)}");
        });
    }    /// <summary>
    /// InteractionStates writes its hover brush over a ThemeRef Background/Foreground; leaving the
    /// hover must drop that local value so the {ThemeResource} Style setter applies again, rather
    /// than write back a brush captured earlier, which would pin the theme it had then.
    /// </summary>
    internal sealed class InteractionStatesRestoreKeepsThemeLive(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsHoverBrush", async host =>
        {
            var hover = new SolidColorBrush(HoverColor);
            var pressed = new SolidColorBrush(ExplicitColor);
            var key = new ThemeRef("TrsHoverBrush");
            var iconSource = new WinUI.AnimatedVisuals.AnimatedBackVisualSource();
            host.Mount(_ => VStack(
                VStack(Border(TextBlock("trs-hover-bg")).Background(key)
                    .InteractionStates(s => s.PointerOver(background: hover).Pressed(background: pressed))),
                VStack(TextBlock("trs-hover-fg").Foreground(key)
                    .InteractionStates(s => s.PointerOver(foreground: hover).Pressed(foreground: pressed))),
                VStack(Button("trs-hover-button").Foreground(key)
                    .InteractionStates(s => s.PointerOver(foreground: hover).Pressed(foreground: pressed))),
                VStack(RichTextBlock("trs-hover-rich-text").Foreground(key)
                    .InteractionStates(s => s.PointerOver(foreground: hover).Pressed(foreground: pressed))),
                VStack(AnimatedIcon(iconSource).Size(32, 32).Foreground(key)
                    .InteractionStates(s => s.PointerOver(foreground: hover).Pressed(foreground: pressed)))).RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trs-hover-bg" });
            var text = H.FindText("trs-hover-fg");
            var button = H.FindControl<WinUI.Button>(b => b.Content is "trs-hover-button");
            var rich = H.FindControl<WinUI.RichTextBlock>(_ => true);
            var icon = H.FindControl<WinUI.AnimatedIcon>(_ => true);
            H.Check("ThemeRefStyle_Hover_Present",
                border is not null && text is not null && button is not null && rich is not null && icon is not null);
            if (border is null || text is null || button is null || rich is null || icon is null) return;

            (FrameworkElement Control, DependencyProperty Property, string Name)[] cases =
            [
                (border, WinUI.Border.BackgroundProperty, "BorderBackground"),
                (text, WinUI.TextBlock.ForegroundProperty, "TextForeground"),
                (button, WinUI.Control.ForegroundProperty, "ButtonForeground"),
                (rich, WinUI.RichTextBlock.ForegroundProperty, "RichTextForeground"),
                (icon, WinUI.IconElement.ForegroundProperty, "IconForeground"),
            ];
            foreach (var (control, property, name) in cases)
            {
                H.Check($"ThemeRefStyle_Hover_{name}_NormalIsTheme", ColorOf(control.GetValue(property)) == LightColor,
                    $"value={ColorOf(control.GetValue(property))}");
                InteractionStateApplier.SimulatePointerOverForTests(control, isOver: true);
                H.Check($"ThemeRefStyle_Hover_{name}_HoverBrush", ReferenceEquals(control.GetValue(property), hover));
                // Pressed sets its own brush: the press must not capture the hover brush as the
                // value to restore.
                InteractionStateApplier.SimulatePressedForTests(control, isPressed: true);
                H.Check($"ThemeRefStyle_Hover_{name}_PressedBrush", ReferenceEquals(control.GetValue(property), pressed));
                InteractionStateApplier.SimulatePressedForTests(control, isPressed: false);
                InteractionStateApplier.SimulatePointerOverForTests(control, isOver: false);
                H.Check($"ThemeRefStyle_Hover_{name}_LocalReleased", !HasLocal(control, property),
                    $"local={control.ReadLocalValue(property)}");
                H.Check($"ThemeRefStyle_Hover_{name}_NormalRestored", ColorOf(control.GetValue(property)) == LightColor);
                H.Check($"ThemeRefStyle_Hover_{name}_ThemeLiveAfterRestore",
                    await FlipToDark(control, () => ColorOf(control.GetValue(property)) == DarkColor),
                    $"value={ColorOf(control.GetValue(property))}");
            }
        });
    }

    /// <summary>
    /// A state that sets no brush (opacity only) leaves Background alone: a transition used to
    /// write the brush captured at setup back as a local value, pinning the ThemeRef.
    /// </summary>
    internal sealed class InteractionStatesOpacityOnlyDoesNotPin(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsOpacityBrush", async host =>
        {
            host.Mount(_ => VStack(
                VStack(Border(TextBlock("trs-opacity")).Background(new ThemeRef("TrsOpacityBrush"))
                    .InteractionStates(s => s.PointerOver(opacity: 0.5f)))).RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trs-opacity" });
            H.Check("ThemeRefStyle_Opacity_NormalIsTheme", ColorOf(border?.Background) == LightColor);
            if (border is null) return;
            InteractionStateApplier.SimulatePointerOverForTests(border, isOver: true);
            InteractionStateApplier.SimulatePointerOverForTests(border, isOver: false);
            H.Check("ThemeRefStyle_Opacity_NoLocalBackground", !HasLocal(border, WinUI.Border.BackgroundProperty));
            H.Check("ThemeRefStyle_Opacity_ThemeLive",
                await FlipToDark(border, () => ColorOf(border.Background) == DarkColor),
                $"bg={ColorOf(border.Background)}");
        });
    }

    /// <summary>
    /// An explicit <c>.Background(brush)</c> is put back after a hover, and it is the brush the
    /// element carries when the states end: not the one it had when InteractionStates was first
    /// set up, not the hover brush when a press follows the hover, and the brush a re-render set
    /// while the pointer was over it.
    /// </summary>
    internal sealed class InteractionStatesRestoresCurrentExplicitBrush(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var hover = new SolidColorBrush(HoverColor);
            var pressed = new SolidColorBrush(DarkColor);
            var first = new SolidColorBrush(ExplicitColor);
            var second = new SolidColorBrush(OtherExplicitColor);
            var third = new SolidColorBrush(LightColor);
            Action<int>? setPhase = null;
            using var host = H.CreateHost();
            try
            {
                host.Mount(ctx =>
                {
                    var (phase, setter) = ctx.UseState(0);
                    setPhase = setter;
                    var brush = phase switch { 0 => first, 1 => second, _ => third };
                    return VStack(Border(TextBlock("trs-explicit")).Background(brush)
                        .InteractionStates(s => s.PointerOver(background: hover).Pressed(background: pressed)));
                });
                await Harness.Render();

                var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trs-explicit" });
                H.Check("ThemeRefStyle_Explicit_Present", border is not null);
                if (border is null) return;
                object Local() => border.ReadLocalValue(WinUI.Border.BackgroundProperty);

                // Over → Pressed → Over → Normal: the press must not recapture the hover brush.
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: true);
                InteractionStateApplier.SimulatePressedForTests(border, isPressed: true);
                H.Check("ThemeRefStyle_Explicit_PressedBrush", ReferenceEquals(border.Background, pressed));
                InteractionStateApplier.SimulatePressedForTests(border, isPressed: false);
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: false);
                H.Check("ThemeRefStyle_Explicit_FirstRestoredAfterPress", ReferenceEquals(Local(), first),
                    $"bg={ColorOf(border.Background)}");

                // A brush set between hovers is the one the next hover restores.
                setPhase!(1);
                await Harness.Render();
                H.Check("ThemeRefStyle_Explicit_SecondApplied", ReferenceEquals(border.Background, second));
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: true);
                H.Check("ThemeRefStyle_Explicit_HoverBrush", ReferenceEquals(border.Background, hover));

                // A re-render during the hover writes a new brush: it stands, and leaving the hover
                // keeps it instead of writing back the brush from when the hover started.
                setPhase!(2);
                await Harness.Render();
                H.Check("ThemeRefStyle_Explicit_ThirdAppliedDuringHover", ReferenceEquals(Local(), third));
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: false);
                H.Check("ThemeRefStyle_Explicit_ThirdKeptOnExit", ReferenceEquals(Local(), third),
                    $"bg={ColorOf(border.Background)}");

                // And once the hover starts over that brush, a press and release still come back to it.
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: true);
                setPhase!(1);
                await Harness.Render();
                InteractionStateApplier.SimulatePressedForTests(border, isPressed: true);
                InteractionStateApplier.SimulatePressedForTests(border, isPressed: false);
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: false);
                H.Check("ThemeRefStyle_Explicit_MidHoverBrushRestoredAfterPress", ReferenceEquals(Local(), second),
                    $"bg={ColorOf(border.Background)}");
            }
            finally
            {
                H.SetContent(null);
                await Harness.Render();
            }
        }
    }

    /// <summary>
    /// A Background set by a binding comes back as that binding after a hover and a press, not as
    /// a cleared property, and keeps following its source afterwards.
    /// </summary>
    internal sealed class InteractionStatesRestoresBinding(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var hover = new SolidColorBrush(HoverColor);
            var pressed = new SolidColorBrush(DarkColor);
            var first = new SolidColorBrush(ExplicitColor);
            var second = new SolidColorBrush(OtherExplicitColor);
            var source = new WinUI.Border { Background = first };
            using var host = H.CreateHost();
            try
            {
                host.Mount(_ => VStack(Border(TextBlock("trs-bound"))
                    .Set(b => b.SetBinding(WinUI.Border.BackgroundProperty,
                        new Microsoft.UI.Xaml.Data.Binding { Source = source, Path = new PropertyPath("Background") }))
                    .InteractionStates(s => s.PointerOver(background: hover).Pressed(background: pressed))));
                await Harness.Render();

                var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trs-bound" });
                H.Check("ThemeRefStyle_Bound_Initial", ReferenceEquals(border?.Background, first));
                if (border is null) return;

                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: true);
                InteractionStateApplier.SimulatePressedForTests(border, isPressed: true);
                H.Check("ThemeRefStyle_Bound_PressedBrush", ReferenceEquals(border.Background, pressed));
                InteractionStateApplier.SimulatePressedForTests(border, isPressed: false);
                InteractionStateApplier.SimulatePointerOverForTests(border, isOver: false);
                H.Check("ThemeRefStyle_Bound_Restored", ReferenceEquals(border.Background, first),
                    $"bg={ColorOf(border.Background)}");

                source.Background = second;
                H.Check("ThemeRefStyle_Bound_FollowsSource",
                    await Harness.WaitFor(() => ReferenceEquals(border.Background, second)),
                    $"bg={ColorOf(border.Background)}");
            }
            finally
            {
                H.SetContent(null);
                await Harness.Render();
            }
        }
    }

    /// <summary>
    /// A ThemeRef keeps a Style the app set (<c>.Set</c> on every render, <c>.ApplyStyle</c> once at
    /// mount): the themed Style is built BasedOn it, re-renders don't chain another level, and
    /// dropping the ThemeRef hands the control back to the app's Style.
    /// </summary>
    internal sealed class AuthorStyleKept(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKeys(H, ["TrsStyleBrush"], async (host, resources) =>
        {
            var setStyle = new Style(typeof(WinUI.TextBlock));
            setStyle.Setters.Add(new Setter(WinUI.TextBlock.FontSizeProperty, 31d));
            var namedStyle = new Style(typeof(WinUI.TextBlock));
            namedStyle.Setters.Add(new Setter(WinUI.TextBlock.FontSizeProperty, 33d));
            resources["TrsNamedTextStyle"] = namedStyle;
            var key = new ThemeRef("TrsStyleBrush");
            Action<int>? setPhase = null;
            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                // Phase 1 re-renders with a changed text, so both elements take the update path.
                var suffix = phase == 1 ? "!" : "";
                var viaSet = TextBlock("trs-style-set" + suffix).Set(t => t.Style = setStyle);
                var viaNamed = TextBlock("trs-style-named" + suffix).ApplyStyle("TrsNamedTextStyle");
                return VStack(
                    phase < 2 ? viaSet.Foreground(key) : viaSet,
                    phase < 2 ? viaNamed.Foreground(key) : viaNamed).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var bySet = H.FindText("trs-style-set");
            var byNamed = H.FindText("trs-style-named");
            H.Check("ThemeRefStyle_Author_Present", bySet is not null && byNamed is not null);
            if (bySet is null || byNamed is null) return;

            void CheckThemed(string phase)
            {
                foreach (var (tb, style, size, name) in new[] { (bySet, setStyle, 31d, "Set"), (byNamed, namedStyle, 33d, "Named") })
                {
                    H.Check($"ThemeRefStyle_Author_{name}_{phase}_StyleKept", Math.Abs(tb.FontSize - size) < 0.01,
                        $"fontSize={tb.FontSize}");
                    H.Check($"ThemeRefStyle_Author_{name}_{phase}_Themed", ColorOf(tb.Foreground) == LightColor,
                        $"fg={ColorOf(tb.Foreground)}");
                    H.Check($"ThemeRefStyle_Author_{name}_{phase}_BasedOnAuthor",
                        tb.Style is { } themed && !ReferenceEquals(themed, style)
                        && ReferenceEquals(themed.BasedOn, style) && themed.BasedOn?.BasedOn is null);
                }
            }

            CheckThemed("Mount");
            var mountedThemed = (bySet.Style, byNamed.Style);
            setPhase!(1);
            await Harness.Render();
            H.Check("ThemeRefStyle_Author_Update_SameControls",
                ReferenceEquals(H.FindText("trs-style-set!"), bySet) && ReferenceEquals(H.FindText("trs-style-named!"), byNamed));
            CheckThemed("Update");
            // The themed Style built on a base is cached per base: a re-render reuses it rather than
            // parsing a new one.
            H.Check("ThemeRefStyle_Author_Update_ThemedStyleReused",
                ReferenceEquals(bySet.Style, mountedThemed.Item1) && ReferenceEquals(byNamed.Style, mountedThemed.Item2));

            setPhase!(2);
            await Harness.Render();
            foreach (var (tb, style, size, name) in new[] { (bySet, setStyle, 31d, "Set"), (byNamed, namedStyle, 33d, "Named") })
            {
                H.Check($"ThemeRefStyle_Author_{name}_Removed_AuthorStyleRestored", ReferenceEquals(tb.Style, style)
                    && Math.Abs(tb.FontSize - size) < 0.01, $"fontSize={tb.FontSize}");
                H.Check($"ThemeRefStyle_Author_{name}_Removed_NotThemed", ColorOf(tb.Foreground) != LightColor
                    && !HasLocal(tb, WinUI.TextBlock.ForegroundProperty), $"fg={ColorOf(tb.Foreground)}");
            }
        });
    }

    /// <summary>
    /// The named-style fluents and typography factories keep their style under a ThemeRef:
    /// <c>Title(...)</c> keeps the title font size and <c>.AccentButton()</c> its accent background.
    /// </summary>
    internal sealed class NamedStyleKeptWithThemeRef(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsNamedBrush", async host =>
        {
            var key = new ThemeRef("TrsNamedBrush");
            host.Mount(_ => VStack(
                Title("trs-title-themed").Foreground(key),
                Title("trs-title-plain"),
                TextBlock("trs-text-plain"),
                Button("trs-accent-themed").AccentButton().Foreground(key),
                Button("trs-accent-plain").AccentButton(),
                Button("trs-button-plain")).RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            var titleThemed = H.FindText("trs-title-themed");
            var titlePlain = H.FindText("trs-title-plain");
            var textPlain = H.FindText("trs-text-plain");
            H.Check("ThemeRefStyle_Named_TitleDiffersFromText",
                titlePlain is not null && textPlain is not null && Math.Abs(titlePlain.FontSize - textPlain.FontSize) > 1,
                $"title={titlePlain?.FontSize} text={textPlain?.FontSize}");
            H.Check("ThemeRefStyle_Named_TitleKept",
                titleThemed is not null && titlePlain is not null && Math.Abs(titleThemed.FontSize - titlePlain.FontSize) < 0.01,
                $"themed={titleThemed?.FontSize} plain={titlePlain?.FontSize}");
            H.Check("ThemeRefStyle_Named_TitleThemed", ColorOf(titleThemed?.Foreground) == LightColor);

            WinUI.Button? FindButton(string label) => H.FindControl<WinUI.Button>(b => b.Content is string s && s == label);
            var accentThemed = FindButton("trs-accent-themed");
            var accentPlain = FindButton("trs-accent-plain");
            var buttonPlain = FindButton("trs-button-plain");
            H.Check("ThemeRefStyle_Named_AccentDiffersFromDefault",
                accentPlain is not null && buttonPlain is not null && ColorOf(accentPlain.Background) != ColorOf(buttonPlain.Background),
                $"accent={ColorOf(accentPlain?.Background)} default={ColorOf(buttonPlain?.Background)}");
            H.Check("ThemeRefStyle_Named_AccentKept",
                accentThemed is not null && accentPlain is not null && ColorOf(accentThemed.Background) == ColorOf(accentPlain.Background),
                $"themed={ColorOf(accentThemed?.Background)} plain={ColorOf(accentPlain?.Background)}");
            H.Check("ThemeRefStyle_Named_AccentThemed", ColorOf(accentThemed?.Foreground) == LightColor,
                $"fg={ColorOf(accentThemed?.Foreground)}");
        });
    }

    /// <summary>
    /// BasedOn needs the base's TargetType to be the derived Style's type or a base of it. A
    /// <c>TargetType=CheckBox</c> author Style must still get its ThemeRef, which the loose
    /// target mapping (CheckBox → ContentControl) cannot build on.
    /// </summary>
    internal sealed class AuthorStyleDerivedTargetType(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsCheckBrush", async host =>
        {
            var style = new Style(typeof(WinUI.CheckBox));
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 177d));
            host.Mount(_ => VStack(
                CheckBox(label: "trs-check").Set(c => c.Style = style).Background(new ThemeRef("TrsCheckBrush")))
                .RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            var check = H.FindControl<WinUI.CheckBox>(c => c.Content is "trs-check");
            H.Check("ThemeRefStyle_CheckBox_StyleKept", check is not null && Math.Abs(check.MinWidth - 177) < 0.01,
                $"minWidth={check?.MinWidth}");
            H.Check("ThemeRefStyle_CheckBox_Themed", ColorOf(check?.Background) == LightColor,
                $"bg={ColorOf(check?.Background)}");
        });
    }

    /// <summary>
    /// Re-applying the themed Style on a re-render must not rebuild a Template the author Style
    /// sets: stepping through no Style at all would swap the template to the default and back.
    /// </summary>
    internal sealed class AuthorTemplateNotRebuilt(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsTemplateBrush", async host =>
        {
            var style = (Style)XamlReader.Load(
                $"<Style xmlns='{XamlNs}' TargetType='Button'><Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}'>" +
                "<ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter></Style>");
            Action<int>? setCount = null;
            host.Mount(ctx =>
            {
                var (count, setter) = ctx.UseState(0);
                setCount = setter;
                return VStack(Button($"trs-template-{count}").Set(b => b.Style = style)
                    .Foreground(new ThemeRef("TrsTemplateBrush"))).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var button = H.FindControl<WinUI.Button>(b => b.Content is "trs-template-0");
            var root = button is not null && VisualTreeHelper.GetChildrenCount(button) > 0 ? VisualTreeHelper.GetChild(button, 0) : null;
            H.Check("ThemeRefStyle_Template_Applied", root is WinUI.Border && ReferenceEquals(button!.Style?.BasedOn, style),
                $"root={root?.GetType().Name}");
            H.Check("ThemeRefStyle_Template_Themed", ColorOf(button?.Foreground) == LightColor);

            setCount!(1);
            await Harness.Render();
            var after = button is not null && VisualTreeHelper.GetChildrenCount(button) > 0 ? VisualTreeHelper.GetChild(button, 0) : null;
            H.Check("ThemeRefStyle_Template_SameControl", ReferenceEquals(H.FindControl<WinUI.Button>(b => b.Content is "trs-template-1"), button));
            H.Check("ThemeRefStyle_Template_RootKept", root is not null && ReferenceEquals(after, root),
                $"after={after?.GetType().Name}");
        });
    }

    /// <summary>
    /// Resource keys with characters that are special in a markup extension (comma, brace, '=',
    /// quote, space, backslash) are quoted in the <c>{ThemeResource}</c> setter, so they apply and
    /// follow the theme instead of failing to parse.
    /// </summary>
    internal sealed class ThemeRefKeyNeedsQuoting(Harness h) : SelfTestFixtureBase(h)
    {
        private static readonly string[] Keys =
            ["Trs,Comma", "Trs}Brace", "Trs=Equals", "Trs'Quote", "Trs Space", "Trs\\Slash", "Trs\"DoubleQuote"];

        public override Task RunAsync() => WithThemeKeys(H, Keys, async (host, _) =>
        {
            host.Mount(_ => VStack(Keys.Select((key, i) =>
                (Element)VStack(Border(TextBlock($"trs-key-{i}")).Background(new ThemeRef(key)))).ToArray())
                .RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            for (int i = 0; i < Keys.Length; i++)
            {
                var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock t && t.Text == $"trs-key-{i}");
                H.Check($"ThemeRefStyle_Key{i}_Themed", ColorOf(border?.Background) == LightColor,
                    $"key={Keys[i]} bg={ColorOf(border?.Background)}");
                if (border is null) continue;
                H.Check($"ThemeRefStyle_Key{i}_FollowsTheme",
                    await FlipToDark(border, () => ColorOf(border.Background) == DarkColor),
                    $"key={Keys[i]} bg={ColorOf(border.Background)}");
            }
        });
    }

    private sealed class ThemeWarningListener : global::System.Diagnostics.Tracing.EventListener
    {
        private readonly List<string> _messages = new();
        internal string[] Messages { get { lock (_messages) return _messages.ToArray(); } }
        protected override void OnEventSourceCreated(global::System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "Microsoft-UI-Reactor")
                EnableEvents(source, global::System.Diagnostics.Tracing.EventLevel.Warning);
        }
        protected override void OnEventWritten(global::System.Diagnostics.Tracing.EventWrittenEventArgs args)
        {
            if (args.EventName != "Warning" || args.Payload is not { Count: >= 3 }) return;
            if (Equals(args.Payload[0], "Theme") && Equals(args.Payload[1], "ApplyThemeBindings"))
                lock (_messages) _messages.Add(args.Payload[2] as string ?? "");
        }
    }

    /// <summary>
    /// A ThemeRef key that cannot be written as a <c>{ThemeResource}</c> reference skips only its
    /// own property (the element's other ThemeRefs still apply) and is reported once, not on every
    /// render.
    /// </summary>
    internal sealed class UnrepresentableKeySkipsOnlyItsProperty(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsGoodBrush", async host =>
        {
            const string BadKey = "TrsBad\tKey";
            using var listener = new ThemeWarningListener();
            Action<int>? setCount = null;
            host.Mount(ctx =>
            {
                var (count, setter) = ctx.UseState(0);
                setCount = setter;
                return VStack(
                    Button($"trs-bad-key-{count}")
                        .Background(new ThemeRef(BadKey))
                        .Foreground(new ThemeRef("TrsGoodBrush")),
                    Button("trs-bad-switch").Background(new ThemeRef(count == 0 ? "TrsGoodBrush" : BadKey)))
                    .RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var button = H.FindControl<WinUI.Button>(b => b.Content is "trs-bad-key-0");
            H.Check("ThemeRefStyle_BadKey_Switch_GoodKeyThemed",
                ColorOf(H.FindControl<WinUI.Button>(b => b.Content is "trs-bad-switch")?.Background) == LightColor);
            H.Check("ThemeRefStyle_BadKey_OtherPropertyThemed", ColorOf(button?.Foreground) == LightColor,
                $"fg={ColorOf(button?.Foreground)}");
            H.Check("ThemeRefStyle_BadKey_OwnPropertyNotSet",
                button is not null && ColorOf(button.Background) != LightColor && !HasLocal(button, WinUI.Control.BackgroundProperty),
                $"bg={ColorOf(button?.Background)}");

            setCount!(1);
            await Harness.Render();
            H.Check("ThemeRefStyle_BadKey_ReRenderStillThemed", ColorOf(button?.Foreground) == LightColor);

            // A key that becomes unrepresentable on a later render takes its brush with it: the
            // themed Style the earlier render applied doesn't keep the old binding alive.
            var switched = H.FindControl<WinUI.Button>(b => b.Content is "trs-bad-switch");
            H.Check("ThemeRefStyle_BadKey_Switch_BadKeyDropsOldBrush",
                switched is not null && ColorOf(switched.Background) != LightColor,
                $"bg={ColorOf(switched?.Background)}");

            // EventListener callbacks do not flow under NativeAOT publish; the JIT run covers it.
            if (global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            {
                var mine = listener.Messages.Where(m => m.Contains("TrsBad\\u0009Key", StringComparison.Ordinal)).ToArray();
                H.Check("ThemeRefStyle_BadKey_WarnedOnce", mine.Length == 1 && mine[0].Contains("Background", StringComparison.Ordinal),
                    $"count={mine.Length} all={string.Join(" || ", listener.Messages)}");
            }
            else
            {
                H.Skip("ThemeRefStyle_BadKey_WarnedOnce", "EventListener callbacks do not flow under NativeAOT publish");
            }
        });
    }

    /// <summary>
    /// A ThemeRef key WinUI can't find fails to build, which says nothing about the app's Style.
    /// Once the key is corrected the themed Style must still be built on that Style, not replace it.
    /// </summary>
    internal sealed class MissingKeyKeepsAuthorBase(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsMissingFixedBrush", async host =>
        {
            var style = new Style(typeof(WinUI.TextBlock));
            style.Setters.Add(new Setter(WinUI.TextBlock.FontSizeProperty, 29d));
            Action<bool>? setFixed = null;
            host.Mount(ctx =>
            {
                var (isFixed, setter) = ctx.UseState(false);
                setFixed = setter;
                return VStack(TextBlock("trs-missing").Set(t => t.Style = style)
                    .Foreground(new ThemeRef(isFixed ? "TrsMissingFixedBrush" : "TrsNoSuchBrushAnywhere")))
                    .RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var tb = H.FindText("trs-missing");
            H.Check("ThemeRefStyle_MissingKey_AuthorStyleKept", tb is not null && Math.Abs(tb.FontSize - 29) < 0.01,
                $"fontSize={tb?.FontSize}");

            setFixed!(true);
            await Harness.Render();
            H.Check("ThemeRefStyle_MissingKey_Fixed_Themed", ColorOf(tb?.Foreground) == LightColor,
                $"fg={ColorOf(tb?.Foreground)}");
            H.Check("ThemeRefStyle_MissingKey_Fixed_AuthorStyleKept",
                tb is not null && Math.Abs(tb.FontSize - 29) < 0.01 && ReferenceEquals(tb.Style?.BasedOn, style),
                $"fontSize={tb?.FontSize}");
        });
    }

    /// <summary>
    /// A themed control returned to the pool goes back without the themed Style or Reactor's
    /// ownership marker for it, so the next renter starts clean and nothing keeps the previous
    /// renter's Style alive.
    /// </summary>
    internal sealed class PoolReturnDropsThemedStyle(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsPoolBrush", async host =>
        {
            Action<int>? setPhase = null;
            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                return phase switch
                {
                    0 => VStack(Title("trs-pool-themed").Foreground(new ThemeRef("TrsPoolBrush"))).RequestedTheme(ElementTheme.Light),
                    1 => VStack().RequestedTheme(ElementTheme.Light),
                    _ => VStack(TextBlock("trs-pool-plain")).RequestedTheme(ElementTheme.Light),
                };
            });
            await Harness.Render();
            var themed = H.FindText("trs-pool-themed");
            H.Check("ThemeRefStyle_Pool_Themed", themed is not null && ColorOf(themed.Foreground) == LightColor
                && ThemeBindingApplier.HasThemeStyleMarkerForTests(themed));

            setPhase!(1);
            await Harness.Render();
            setPhase!(2);
            await Harness.Render();

            var plain = H.FindText("trs-pool-plain");
            H.Check("ThemeRefStyle_Pool_SameInstanceRented", plain is not null && ReferenceEquals(plain, themed));
            H.Check("ThemeRefStyle_Pool_StyleCleared", plain is not null && plain.Style is null && ColorOf(plain.Foreground) != LightColor,
                $"fg={ColorOf(plain?.Foreground)}");
            H.Check("ThemeRefStyle_Pool_MarkerCleared", plain is not null && !ThemeBindingApplier.HasThemeStyleMarkerForTests(plain));
        });
    }

    /// <summary>
    /// The themed-Style cache key keeps a resource key containing '|' or '=' apart from a
    /// different binding set that would otherwise spell the same key, so the second element does
    /// not get the first element's cached Style.
    /// </summary>
    internal sealed class CacheKeyDoesNotCollide(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKeys(H, ["TrsColA|Foreground=TrsColB", "TrsColA", "TrsColB"], async (host, _) =>
        {
            host.Mount(_ => VStack(
                Button("trs-collide-one").Background(new ThemeRef("TrsColA|Foreground=TrsColB")),
                Button("trs-collide-two").Background(new ThemeRef("TrsColA")).Foreground(new ThemeRef("TrsColB")))
                .RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            var one = H.FindControl<WinUI.Button>(b => b.Content is "trs-collide-one");
            var two = H.FindControl<WinUI.Button>(b => b.Content is "trs-collide-two");
            H.Check("ThemeRefStyle_Collide_FirstThemed", ColorOf(one?.Background) == LightColor);
            H.Check("ThemeRefStyle_Collide_SecondHasOwnStyle",
                two is not null && !ReferenceEquals(two.Style, one?.Style)
                && ColorOf(two.Background) == LightColor && ColorOf(two.Foreground) == LightColor,
                $"fg={ColorOf(two?.Foreground)} sameStyle={ReferenceEquals(two?.Style, one?.Style)}");
        });
    }

    /// <summary>
    /// A Style for a third-party control type (one that ships XAML metadata) cannot be the
    /// BasedOn of a themed Style targeting a WinUI type. The ThemeRef must still apply (the themed
    /// Style replaces the app's, as before Styles were kept), and later renders must reuse that
    /// result rather than build on the incompatible base again.
    /// </summary>
    internal sealed class ThirdPartyStyleFallsBack(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsThirdPartyBrush", async host =>
        {
            ReactorApp.RegisterControlAssembly(typeof(global::Reactor.AppTests.ThirdPartyControls.ThirdPartyControlWithPrivateDp).Assembly);
            var style = (Style)XamlReader.Load(
                $"<Style xmlns='{XamlNs}' xmlns:tp='using:Reactor.AppTests.ThirdPartyControls' " +
                "TargetType='tp:ThirdPartyControlWithPrivateDp'><Setter Property='MinWidth' Value='177'/></Style>");

            Action<int>? setCount = null;
            host.Mount(ctx =>
            {
                var (count, setter) = ctx.UseState(0);
                setCount = setter;
                return VStack(
                    TextBlock($"trs-third-party-{count}"),
                    new XamlHostElement(() => new global::Reactor.AppTests.ThirdPartyControls.ThirdPartyControlWithPrivateDp { Style = style })
                        .Background(new ThemeRef("TrsThirdPartyBrush"))).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var control = H.FindControl<global::Reactor.AppTests.ThirdPartyControls.ThirdPartyControlWithPrivateDp>(_ => true);
            var errorText = H.FindControl<WinUI.TextBlock>(t => t.Text?.Contains("xception", StringComparison.Ordinal) == true)?.Text;
            H.Check("ThemeRefStyle_ThirdParty_Mounted", control is not null && errorText is null, $"error={errorText}");
            H.Check("ThemeRefStyle_ThirdParty_Themed", ColorOf(control?.Background) == LightColor,
                $"bg={ColorOf(control?.Background)} styleIsAuthor={ReferenceEquals(control?.Style, style)}");
            var themed = control?.Style;

            setCount!(1);
            await Harness.Render();
            H.Check("ThemeRefStyle_ThirdParty_ReRender_SameControl",
                ReferenceEquals(H.FindControl<global::Reactor.AppTests.ThirdPartyControls.ThirdPartyControlWithPrivateDp>(_ => true), control)
                && H.FindText("trs-third-party-1") is not null);
            H.Check("ThemeRefStyle_ThirdParty_ReRender_Themed", ColorOf(control?.Background) == LightColor);
            H.Check("ThemeRefStyle_ThirdParty_ReRender_StyleReused", control is not null && ReferenceEquals(control.Style, themed));
        });
    }
}
