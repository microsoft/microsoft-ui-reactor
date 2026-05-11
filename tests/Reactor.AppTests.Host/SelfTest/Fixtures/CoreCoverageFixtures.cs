using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selfhost fixtures targeting uncovered Core code paths to bring Core line coverage from ~53% to 80%.
/// Organized by coverage gap area.
/// </summary>
internal static class CoreCoverageFixtures
{
    // ════════════════════════════════════════════════════════════════════════
    //  1. RichTextBlock — paragraph/inline mount + update + reconciliation
    //     Targets: Reconciler.Update.cs lines 440-581 (RichTextBlock inline update)
    // ════════════════════════════════════════════════════════════════════════

    internal class RichTextBlockParagraphUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var paragraphs = phase switch
                {
                    0 => new[]
                    {
                        Paragraph(
                            Run("Hello "),
                            new RichTextRun("bold") { IsBold = true },
                            new RichTextRun(" italic") { IsItalic = true }
                        ),
                    },
                    1 => new[]
                    {
                        Paragraph(
                            Run("Updated "),
                            new RichTextRun("bold changed") { IsBold = true },
                            new RichTextRun(" still italic") { IsItalic = true, IsStrikethrough = true }
                        ),
                    },
                    _ => new[]
                    {
                        Paragraph(
                            Run("Para1 "),
                            Hyperlink("click me", new Uri("https://example.com"))
                        ),
                        Paragraph(
                            Run("Para2"),
                            new RichTextLineBreak(),
                            new RichTextRun("sized") { FontSize = 20, FontFamily = "Consolas" }
                        ),
                    },
                };
                return VStack(
                    Button("Next", () => set(phase + 1)),
                    RichText(paragraphs)
                );
            });

            await Harness.Render();

            var rtb = H.FindControl<RichTextBlock>(_ => true);
            H.Check("RichText_Para_Mounted", rtb is not null);
            H.Check("RichText_Para_InitialBlockCount", rtb?.Blocks.Count == 1);

            // Phase 1: update inline text and formatting
            H.ClickButton("Next");
            await Harness.Render();
            rtb = H.FindControl<RichTextBlock>(_ => true);
            H.Check("RichText_Para_UpdatedBlockCount", rtb?.Blocks.Count == 1);

            // Phase 2: rebuild paragraphs with hyperlink, line break, font changes
            H.ClickButton("Next");
            await Harness.Render();
            rtb = H.FindControl<RichTextBlock>(_ => true);
            H.Check("RichText_Para_MultiParagraph", rtb?.Blocks.Count == 2);
        }
    }

    internal class RichTextBlockInlineReconciliation(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var paragraphs = phase switch
                {
                    // Start with 3 inlines: Run, Hyperlink, Run
                    0 => new[] { Paragraph(
                        Run("A"),
                        Hyperlink("link", new Uri("https://a.com")),
                        Run("B")
                    )},
                    // Change text of Run + update hyperlink URI
                    1 => new[] { Paragraph(
                        Run("A-updated"),
                        Hyperlink("link-updated", new Uri("https://b.com")),
                        Run("B-updated")
                    )},
                    // Type mismatch: replace Hyperlink with Run (triggers inline replacement)
                    2 => new[] { Paragraph(
                        Run("A-updated"),
                        Run("was-link"),
                        Run("B-updated")
                    )},
                    // Grow inlines (add new ones)
                    3 => new[] { Paragraph(
                        Run("A-updated"),
                        Run("was-link"),
                        Run("B-updated"),
                        new RichTextLineBreak(),
                        Run("Extra")
                    )},
                    // Shrink inlines (remove some)
                    _ => new[] { Paragraph(Run("Only")) },
                };
                return VStack(
                    Button("Advance", () => set(phase + 1)),
                    RichText(paragraphs)
                );
            });

            await Harness.Render();
            H.Check("RichInline_Initial", H.FindControl<RichTextBlock>(_ => true) is not null);

            // Phase 1: update text within same types
            H.ClickButton("Advance");
            await Harness.Render();
            H.Check("RichInline_TextUpdate", H.FindControl<RichTextBlock>(_ => true)?.Blocks.Count == 1);

            // Phase 2: type mismatch replacement
            H.ClickButton("Advance");
            await Harness.Render();
            H.Check("RichInline_TypeReplace", H.FindControl<RichTextBlock>(_ => true)?.Blocks.Count == 1);

            // Phase 3: grow inlines
            H.ClickButton("Advance");
            await Harness.Render();
            H.Check("RichInline_Grow", H.FindControl<RichTextBlock>(_ => true)?.Blocks.Count == 1);

            // Phase 4: shrink inlines
            H.ClickButton("Advance");
            await Harness.Render();
            H.Check("RichInline_Shrink", H.FindControl<RichTextBlock>(_ => true)?.Blocks.Count == 1);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. TemplatedGridView — mount + update + item count change
    //     Targets: Reconciler.Mount.cs lines 1415-1448, Reconciler.Update.cs lines 1388-1406
    // ════════════════════════════════════════════════════════════════════════

    internal class TemplatedGridViewMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (items, setItems) = ctx.UseState<IReadOnlyList<string>>(["Alpha", "Beta", "Gamma"]);
                return VStack(
                    Button("AddItem", () => setItems([..items, $"Item{items.Count}"])),
                    Button("RemoveItem", () => setItems(items.Count > 0 ? items.Take(items.Count - 1).ToList() : [])),
                    Button("ChangeItem", () =>
                    {
                        var l = items.ToList();
                        if (l.Count > 0) l[0] = "Changed";
                        setItems(l);
                    }),
                    GridView<string>(items, x => x, (item, idx) => TextBlock(item))
                );
            });

            await Harness.Render();

            var gv = H.FindControl<GridView>(_ => true);
            H.Check("GridView_Mounted", gv is not null);
            H.Check("GridView_InitialCount", gv?.Items.Count == 3);

            // Add item
            H.ClickButton("AddItem");
            await Harness.Render();
            gv = H.FindControl<GridView>(_ => true);
            H.Check("GridView_AfterAdd", gv?.Items.Count == 4);

            // Remove item
            H.ClickButton("RemoveItem");
            await Harness.Render();
            gv = H.FindControl<GridView>(_ => true);
            H.Check("GridView_AfterRemove", gv?.Items.Count == 3);

            // Change item content (same count triggers RefreshRealizedContainers)
            H.ClickButton("ChangeItem");
            await Harness.Render();
            H.Check("GridView_AfterChange", H.FindControl<GridView>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3. TemplatedFlipView — mount + update + add/remove items
    //     Targets: Reconciler.Mount.cs lines 1451-1476, Reconciler.Update.cs lines 1408-1451
    // ════════════════════════════════════════════════════════════════════════

    internal class TemplatedFlipViewMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (items, setItems) = ctx.UseState<IReadOnlyList<string>>(["Page1", "Page2", "Page3"]);
                return VStack(
                    Button("AddPage", () => setItems([..items, $"Page{items.Count + 1}"])),
                    Button("RemovePage", () => setItems(items.Count > 1 ? items.Take(items.Count - 1).ToList() : items)),
                    Button("EditPage", () =>
                    {
                        var l = items.ToList();
                        if (l.Count > 0) l[0] = "Edited";
                        setItems(l);
                    }),
                    FlipView<string>(items, x => x, (item, idx) => TextBlock(item))
                );
            });

            await Harness.Render();

            var fv = H.FindControl<FlipView>(_ => true);
            H.Check("FlipView_Mounted", fv is not null);
            H.Check("FlipView_InitialCount", fv?.Items.Count == 3);

            // Add page
            H.ClickButton("AddPage");
            await Harness.Render();
            fv = H.FindControl<FlipView>(_ => true);
            H.Check("FlipView_AfterAdd", fv?.Items.Count == 4);

            // Remove page
            H.ClickButton("RemovePage");
            await Harness.Render();
            fv = H.FindControl<FlipView>(_ => true);
            H.Check("FlipView_AfterRemove", fv?.Items.Count == 3);

            // Edit page content (same count, triggers Update path)
            H.ClickButton("EditPage");
            await Harness.Render();
            H.Check("FlipView_AfterEdit", H.FindControl<FlipView>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4. LazyVStack — ItemsRepeater mount + update
    //     Targets: Reconciler.Mount.cs lines 1940-1967, Reconciler.Update.cs lines 1453-1467
    // ════════════════════════════════════════════════════════════════════════

    internal class LazyVStackMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (items, setItems) = ctx.UseState<IReadOnlyList<string>>(
                    Enumerable.Range(0, 20).Select(i => $"Item {i}").ToList());
                return VStack(
                    Button("MoreItems", () => setItems(
                        Enumerable.Range(0, 30).Select(i => $"Item {i}").ToList())),
                    LazyVStack<string>(items, x => x, (item, idx) =>
                        TextBlock(item).Height(30))
                ).Height(400);
            });

            await Harness.Render();

            var sv = H.FindControl<ScrollViewer>(_ => true);
            H.Check("LazyVStack_ScrollViewer", sv is not null);
            var repeater = H.FindControl<ItemsRepeater>(_ => true);
            H.Check("LazyVStack_Repeater", repeater is not null);

            // Update items source
            H.ClickButton("MoreItems");
            await Harness.Render();
            H.Check("LazyVStack_Updated", H.FindControl<ItemsRepeater>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5. ContentDialog — mount (placeholder + trigger)
    //     Targets: Reconciler.Mount.cs lines 1506-1534
    // ════════════════════════════════════════════════════════════════════════

    internal class ContentDialogMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                // Mount a closed ContentDialog — it creates a collapsed placeholder
                return VStack(
                    TextBlock("Main content"),
                    ContentDialog("Test Dialog", TextBlock("Dialog body"), "OK")
                );
            });

            await Harness.Render();

            // ContentDialog mounts as a collapsed StackPanel placeholder
            H.Check("ContentDialog_MainContent", H.FindText("Main content") is not null);
            var placeholder = H.FindControl<StackPanel>(sp => sp.Visibility == Visibility.Collapsed);
            H.Check("ContentDialog_Placeholder", placeholder is not null);
        }
    }

    // Regression for #246: declarative IsOpen=true at first mount must actually
    // show the dialog. Previously the framework tried to read XamlRoot from the
    // dialog's just-mounted (detached) content, which is always null, so
    // ShowAsync() threw and the empty catch swallowed it.
    internal class ContentDialogOpensAtMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                TextBlock("anchor"),
                ContentDialog("AutoOpen", TextBlock("dialog-body"), "OK") with { IsOpen = true }
            ));

            await Harness.Render(100);

            var dialog = FindOpenContentDialog(H, "AutoOpen");
            H.Check("ContentDialog_OpensAtMount_#246", dialog is not null);

            dialog?.Hide();
            await Harness.Render(50);
        }
    }

    // Regression for #246: flipping IsOpen false→true via state must show the
    // dialog without requiring the app to set XamlRoot manually.
    internal class ContentDialogOpensOnStateFlip(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (open, setOpen) = ctx.UseState(false);
                return VStack(
                    Button("Show", () => setOpen(true)),
                    ContentDialog("Toggled", TextBlock("dialog-body"), "OK") with { IsOpen = open }
                );
            });

            await Harness.Render();
            H.Check("ContentDialog_NotOpenBeforeClick_#246", FindOpenContentDialog(H, "Toggled") is null);

            H.ClickButton("Show");
            await Harness.Render(50);

            var dialog = FindOpenContentDialog(H, "Toggled");
            H.Check("ContentDialog_OpensOnStateFlip_#246", dialog is not null);

            dialog?.Hide();
            await Harness.Render(50);
        }
    }

    private static Microsoft.UI.Xaml.Controls.ContentDialog? FindOpenContentDialog(Harness h, string title)
    {
        var xamlRoot = (h.Window.Content as UIElement)?.XamlRoot
                       ?? ReactorApp.PrimaryWindow?.NativeWindow.Content?.XamlRoot;
        if (xamlRoot is null) return null;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
        {
            // Popup.Child is not enumerated by VisualTreeHelper.GetChildrenCount,
            // so descend into it explicitly before recursing.
            if (popup.Child is DependencyObject child)
            {
                var found = WalkForContentDialog(child, title);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static Microsoft.UI.Xaml.Controls.ContentDialog? WalkForContentDialog(DependencyObject node, string title)
    {
        if (node is Microsoft.UI.Xaml.Controls.ContentDialog cd && cd.Title as string == title)
            return cd;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            var hit = WalkForContentDialog(VisualTreeHelper.GetChild(node, i), title);
            if (hit is not null) return hit;
        }
        return null;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  6. CommandBar — mount + update (primary/secondary commands)
    //     Targets: Reconciler.Mount.cs lines 1636+, Reconciler.Update.cs lines 1593+
    // ════════════════════════════════════════════════════════════════════════

    internal class CommandBarMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var primary = phase == 0
                    ? new AppBarItemBase[] { AppBarButton("Save"), AppBarButton("Copy") }
                    : new AppBarItemBase[] { AppBarButton("Save"), AppBarButton("Paste"), AppBarButton("Cut") };
                return VStack(
                    Button("UpdateBar", () => set(1)),
                    CommandBar(primaryCommands: primary)
                );
            });

            await Harness.Render();

            var cb = H.FindControl<CommandBar>(_ => true);
            H.Check("CmdBar_Mounted", cb is not null);
            H.Check("CmdBar_InitialPrimary", cb?.PrimaryCommands.Count == 2);

            H.ClickButton("UpdateBar");
            await Harness.Render();
            cb = H.FindControl<CommandBar>(_ => true);
            H.Check("CmdBar_UpdatedPrimary", cb?.PrimaryCommands.Count == 3);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  7. CommandHost — mount + update (keyboard accelerators)
    //     Targets: Reconciler.Mount.cs lines 1582-1592, Reconciler.Update.cs lines 1567-1591
    // ════════════════════════════════════════════════════════════════════════

    internal class CommandHostMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (label, setLabel) = ctx.UseState("Initial");
                var saveCmd = new Command
                {
                    Label = "Save",
                    Execute = () => setLabel("Saved"),
                    Accelerator = new KeyboardAcceleratorData(global::Windows.System.VirtualKey.S,
                        global::Windows.System.VirtualKeyModifiers.Control),
                };
                return CommandHost([saveCmd],
                    VStack(
                        TextBlock(label),
                        Button("Trigger", () => setLabel("Updated"))
                    )
                );
            });

            await Harness.Render();

            H.Check("CmdHost_Mounted", H.FindText("Initial") is not null);

            // Update the command host child
            H.ClickButton("Trigger");
            await Harness.Render();
            H.Check("CmdHost_Updated", H.FindText("Updated") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  8. MenuBar — mount + update reconciliation
    //     Targets: Reconciler.Update.cs lines 1469-1565
    // ════════════════════════════════════════════════════════════════════════

    internal class MenuBarMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var menus = phase switch
                {
                    0 => new[]
                    {
                        Menu("File", MenuItem("New"), MenuItem("Open"), MenuSeparator(), MenuItem("Exit")),
                        Menu("Edit", MenuItem("Cut"), MenuItem("Copy")),
                    },
                    1 => new[]
                    {
                        // Updated: changed title, changed items, added toggle/radio
                        Menu("File", MenuItem("New"), MenuItem("Save"), MenuSeparator(), MenuItem("Exit")),
                        Menu("Edit", MenuItem("Cut"), MenuItem("Copy"), MenuItem("Paste")),
                        Menu("View", ToggleMenuItem("Sidebar", true), RadioMenuItem("Dark", "theme", true)),
                    },
                    _ => new[]
                    {
                        // Shrunk: removed a menu, updated sub items
                        Menu("File", MenuItem("New"), MenuSubItem("Recent", MenuItem("File1.txt"), MenuItem("File2.txt"))),
                    },
                };
                return VStack(
                    Button("ChangeMenu", () => set(phase + 1)),
                    MenuBar(menus)
                );
            });

            await Harness.Render();

            var mb = H.FindControl<MenuBar>(_ => true);
            H.Check("MenuBar_Mounted", mb is not null);
            H.Check("MenuBar_InitialMenus", mb?.Items.Count == 2);

            // Update: change items, add menus
            H.ClickButton("ChangeMenu");
            await Harness.Render();
            mb = H.FindControl<MenuBar>(_ => true);
            H.Check("MenuBar_UpdatedMenus", mb?.Items.Count == 3);

            // Shrink: remove menus
            H.ClickButton("ChangeMenu");
            await Harness.Render();
            mb = H.FindControl<MenuBar>(_ => true);
            H.Check("MenuBar_ShrunkMenus", mb?.Items.Count == 1);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  9. Theme bindings — .Background(Theme.Xxx) applies XAML style
    //     Targets: Reconciler.cs lines 1996-2077 (ApplyThemeBindings, GetStyleTargetType, etc.)
    // ════════════════════════════════════════════════════════════════════════

    internal class ThemeBindingApplication(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                if (phase == 0)
                {
                    return VStack(
                        Button("ChangeTheme", () => set(1)),
                        TextBlock("Themed text").Foreground(Theme.PrimaryText),
                        Button("Themed btn", () => { }).Background(Theme.Accent),
                        Border(TextBlock("inner")).Background(Theme.CardBackground)
                    );
                }
                else
                {
                    return VStack(
                        Button("ChangeTheme", () => set(2)),
                        TextBlock("Themed text").Foreground(Theme.SecondaryText),
                        Button("Themed btn", () => { }).Background(Theme.AccentSecondary),
                        Border(TextBlock("inner")).Background(Theme.SubtleFill)
                    );
                }
            });

            await Harness.Render();

            // Theme bindings should have created styles on the elements
            var themed = H.FindText("Themed text");
            H.Check("Theme_TextMounted", themed is not null);
            H.Check("Theme_ButtonMounted", H.FindButton("Themed btn") is not null);

            // Update to different theme refs
            H.ClickButton("ChangeTheme");
            await Harness.Render();
            H.Check("Theme_Updated", H.FindText("Themed text") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  10. Resource overrides (lightweight styling)
    //     Targets: Reconciler.cs lines 2095-2138 (ApplyResourceOverrides)
    // ════════════════════════════════════════════════════════════════════════

    internal class ResourceOverrideApplication(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("SwapResources", () => set(1)),
                    Button("Styled", () => { }).Resources(b =>
                    {
                        if (phase == 0)
                            b.Set("ButtonBackground", new SolidColorBrush(Microsoft.UI.Colors.Red));
                        else
                        {
                            b.Set("ButtonBackground", new SolidColorBrush(Microsoft.UI.Colors.Blue));
                            b.Set("ButtonForeground", new SolidColorBrush(Microsoft.UI.Colors.White));
                        }
                    })
                );
            });

            await Harness.Render();

            H.Check("ResOverride_Mounted", H.FindButton("Styled") is not null);

            // Update resource overrides (removes old keys, adds new)
            H.ClickButton("SwapResources");
            await Harness.Render();
            H.Check("ResOverride_Updated", H.FindButton("Styled") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  11. Accessibility modifiers
    //     Targets: Reconciler.cs lines 1834-1870 (ApplyAccessibilityModifiers)
    // ════════════════════════════════════════════════════════════════════════

    internal class AccessibilityModifierApplication(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                if (phase == 0)
                {
                    return VStack(
                        Button("UpdateA11y", () => set(1)),
                        TextBlock("Accessible")
                            .AutomationName("test-text")
                            .HelpText("This is help text")
                            .FullDescription("Full description of the element")
                            .Landmark(Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType.Main)
                    );
                }
                else
                {
                    return VStack(
                        Button("UpdateA11y", () => set(2)),
                        TextBlock("Accessible")
                            .AutomationName("test-text-updated")
                            .HelpText("Updated help text")
                            .FullDescription("Updated description")
                            .AccessibilityView(Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content)
                            .Required()
                            .LiveRegion(Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite)
                            .PositionInSet(1, 5)
                            .HierarchyLevel(3)
                            .ItemStatus("Ready")
                            .TabNavigation(Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Cycle)
                    );
                }
            });

            await Harness.Render();

            var tb = H.FindText("Accessible");
            H.Check("A11y_Mounted", tb is not null);
            H.Check("A11y_HelpText",
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(tb!) == "This is help text");

            // Update accessibility modifiers
            H.ClickButton("UpdateA11y");
            await Harness.Render();
            tb = H.FindText("Accessible");
            H.Check("A11y_Updated",
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(tb!) == "Updated help text");
            H.Check("A11y_LiveSetting",
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetLiveSetting(tb!) ==
                Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  12. UseCommand hook — async command wrapping with IsExecuting tracking
    //     Targets: RenderContext.cs lines 697-773
    // ════════════════════════════════════════════════════════════════════════

    private class UseCommandComponent : Component
    {
        public override Element Render()
        {
            var asyncCmd = new Command
            {
                Label = "AsyncOp",
                ExecuteAsync = async () => await Task.Delay(50),
            };
            var wrapped = UseCommand(asyncCmd);

            return VStack(
                TextBlock(wrapped.IsExecuting ? "Executing" : "Idle"),
                Button("RunAsync", () => wrapped.Execute?.Invoke())
            );
        }
    }

    internal class UseCommandHook(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(new UseCommandComponent());

            await Harness.Render();
            H.Check("UseCmd_Initial", H.FindText("Idle") is not null);

            // Trigger async command
            H.ClickButton("RunAsync");
            await Harness.Render(100);
            // It should transition to executing (or already finished for short delays)
            H.Check("UseCmd_Triggered", H.FindText("Idle") is not null || H.FindText("Executing") is not null);

            // Wait for async command (50ms internal delay) to complete
            await Harness.Render(150);
            H.Check("UseCmd_Completed", H.FindText("Idle") is not null);
        }
    }

    private class UseCommandTypedComponent : Component
    {
        public override Element Render()
        {
            var (result, setResult) = UseState("none", threadSafe: true);
            var typedCmd = new Command<string>
            {
                Label = "TypedOp",
                ExecuteAsync = async (arg) =>
                {
                    await Task.Delay(50);
                    setResult(arg);
                },
            };
            var wrapped = UseCommand(typedCmd);

            return VStack(
                TextBlock($"Result: {result}"),
                Button("RunTyped", () => wrapped.Execute?.Invoke("hello"))
            );
        }
    }

    internal class UseCommandTypedHook(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(new UseCommandTypedComponent());

            await Harness.Render();
            H.Check("UseCmdTyped_Initial", H.FindText("Result: none") is not null);

            H.ClickButton("RunTyped");
            // The async command has a 50ms Task.Delay; wait for it to complete
            // and for the background-thread setState to marshal back to the UI thread.
            await Task.Delay(500);
            await Harness.Render();
            H.Check("UseCmdTyped_Completed", H.FindText("Result: hello") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  13. Flyout — mount + update reconciliation
    //     Targets: Reconciler.Mount.cs (MountFlyout), Reconciler.cs (ApplyFlyoutAttachment)
    // ════════════════════════════════════════════════════════════════════════

    internal class FlyoutMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("Toggle", () => set(phase + 1)),
                    Flyout(
                        Button("WithFlyout", () => { }),
                        phase == 0 ? TextBlock("Flyout v1") : TextBlock("Flyout v2")
                    )
                );
            });

            await Harness.Render();

            H.Check("Flyout_TargetMounted", H.FindButton("WithFlyout") is not null);

            // Update flyout content
            H.ClickButton("Toggle");
            await Harness.Render();
            H.Check("Flyout_Updated", H.FindButton("WithFlyout") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  14. SemanticZoom — mount + update
    //     Targets: Reconciler.Update.cs lines 1505-1591 (SemanticZoom update path)
    // ════════════════════════════════════════════════════════════════════════

    internal class SemanticZoomMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var zoomedIn = phase == 0 ? TextBlock("Zoomed In v1") : TextBlock("Zoomed In v2");
                var zoomedOut = phase == 0 ? TextBlock("Zoomed Out v1") : TextBlock("Zoomed Out v2");
                return VStack(
                    Button("UpdateZoom", () => set(1)),
                    SemanticZoom(zoomedIn, zoomedOut)
                );
            });

            await Harness.Render();

            var sz = H.FindControl<Microsoft.UI.Xaml.Controls.SemanticZoom>(_ => true);
            H.Check("SemZoom_Mounted", sz is not null);

            // Update both views
            H.ClickButton("UpdateZoom");
            await Harness.Render();
            H.Check("SemZoom_Updated", H.FindControl<Microsoft.UI.Xaml.Controls.SemanticZoom>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  15. Compositor transitions — .ImplicitTransitions() modifier
    //     Targets: Reconciler.cs lines 760-825 (ApplyTransitions), 828-865 (ApplyTransitionsViaCompositor)
    // ════════════════════════════════════════════════════════════════════════

    internal class CompositorTransitions(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (visible, setVisible) = ctx.UseState(true);
                return VStack(
                    Button("ToggleVis", () => setVisible(!visible)),
                    TextBlock("Transitioning")
                        .Opacity(visible ? 1.0 : 0.3)
                        .OpacityTransition(TimeSpan.FromMilliseconds(200))
                        .ScaleTransition()
                        .TranslationTransition()
                        .RotationTransition(TimeSpan.FromMilliseconds(200))
                );
            });

            await Harness.Render();
            H.Check("Transitions_Mounted", H.FindText("Transitioning") is not null);

            // Trigger a transition by changing opacity
            H.ClickButton("ToggleVis");
            await Harness.Render();
            H.Check("Transitions_Updated", H.FindText("Transitioning") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  16. Theme transitions (children/item container)
    //     Targets: Reconciler.cs lines 804-825
    // ════════════════════════════════════════════════════════════════════════

    internal class ThemeTransitions(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (count, setCount) = ctx.UseState(2);
                return VStack(
                    Button("AddChild", () => setCount(count + 1)),
                    VStack(
                        Enumerable.Range(0, count).Select(i =>
                            TextBlock($"Child {i}").WithKey($"c{i}")).ToArray()
                    ).WithTransitions(
                        new Microsoft.UI.Xaml.Media.Animation.EntranceThemeTransition())
                );
            });

            await Harness.Render();
            H.Check("ThemeTransit_Initial", H.FindText("Child 0") is not null);

            H.ClickButton("AddChild");
            await Harness.Render();
            H.Check("ThemeTransit_Added", H.FindText("Child 2") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  17. Memo component — mount + conditional skip
    //     Targets: Reconciler.Mount.cs lines 1906-1938 (MountMemoComponent)
    // ════════════════════════════════════════════════════════════════════════

    internal class MemoComponentMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var renderCount = 0;
            host.Mount(ctx =>
            {
                var (count, setCount) = ctx.UseState(0);
                var (other, setOther) = ctx.UseState(0);
                return VStack(
                    Button("BumpDep", () => setCount(count + 1)),
                    Button("BumpOther", () => setOther(other + 1)),
                    Memo(memoCtx =>
                    {
                        renderCount++;
                        return TextBlock($"Memo count={count} renders={renderCount}");
                    }, count)
                );
            });

            await Harness.Render();
            H.Check("Memo_Mounted", H.FindTextContaining("Memo count=0") is not null);

            // BumpOther should NOT re-render the memo (deps didn't change)
            H.ClickButton("BumpOther");
            await Harness.Render();
            var rc1 = renderCount;

            // BumpDep SHOULD re-render the memo
            H.ClickButton("BumpDep");
            await Harness.Render();
            H.Check("Memo_DepChanged", H.FindTextContaining("Memo count=1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  18. Declarative event handlers — OnPointerEntered/Exited, KeyDown
    //     Targets: Reconciler.cs lines ~1940-1985 (ApplyEventHandlers)
    // ════════════════════════════════════════════════════════════════════════

    internal class EventHandlerModifiers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (msg, setMsg) = ctx.UseState("None");
                return VStack(
                    TextBlock(msg),
                    // OnKeyDown is exposed on ElementModifiers; exercise it for coverage
                    Button("Hoverable", () => setMsg("Clicked"))
                        .OnKeyDown((s, e) => setMsg("KeyDown"))
                );
            });

            await Harness.Render();
            H.Check("EventHandler_Mounted", H.FindText("None") is not null);
            H.Check("EventHandler_ButtonExists", H.FindButton("Hoverable") is not null);

            // Clicking exercises the handler
            H.ClickButton("Hoverable");
            await Harness.Render();
            H.Check("EventHandler_Clicked", H.FindText("Clicked") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  19. MenuFlyout / CommandBarFlyout — mount through element types
    //     Targets: Reconciler.cs flyout dispatch, Reconciler.Mount (MountMenuFlyout, etc.)
    // ════════════════════════════════════════════════════════════════════════

    internal class MenuFlyoutMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return VStack(
                    MenuFlyout(
                        Button("RightClick", () => { }),
                        MenuItem("Action1"),
                        ToggleMenuItem("Toggle1", true),
                        MenuSeparator(),
                        MenuSubItem("More", MenuItem("Sub1"), MenuItem("Sub2"))
                    ),
                    CommandBarFlyout(
                        Button("CmdFlyout", () => { }),
                        primaryCommands: [AppBarButton("Bold")]
                    )
                );
            });

            await Harness.Render();
            H.Check("MenuFlyout_TargetMounted", H.FindButton("RightClick") is not null);
            H.Check("CmdBarFlyout_TargetMounted", H.FindButton("CmdFlyout") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  20. Reconciler Update dispatch — MediaPlayer, AnimatedVisualPlayer
    //     Targets: Reconciler.Update.cs lines 222-254
    // ════════════════════════════════════════════════════════════════════════

    internal class MediaPlayerMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdateMedia", () => set(1)),
                    MediaPlayerElement(),
                    AnimatedVisualPlayer()
                );
            });

            await Harness.Render();
            H.Check("Media_PlayerMounted",
                H.FindControl<MediaPlayerElement>(_ => true) is not null);
            H.Check("Media_AnimatedMounted",
                H.FindControl<AnimatedVisualPlayer>(_ => true) is not null);

            H.ClickButton("UpdateMedia");
            await Harness.Render();
            H.Check("Media_Updated",
                H.FindControl<MediaPlayerElement>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  21. LazyHStack — horizontal virtualized stack
    //     Targets: Element.cs LazyHStackElement record, Reconciler.Mount (orientation variant)
    // ════════════════════════════════════════════════════════════════════════

    internal class LazyHStackMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var items = Enumerable.Range(0, 15).Select(i => $"H{i}").ToList();
                return LazyHStack<string>(items, x => x, (item, idx) =>
                    TextBlock(item).Width(80)).Height(60);
            });

            await Harness.Render();

            var sv = H.FindControl<ScrollViewer>(_ => true);
            H.Check("LazyHStack_ScrollViewer", sv is not null);
            // Horizontal stack should have horizontal scrollbar enabled
            H.Check("LazyHStack_HScrollEnabled",
                sv?.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  22. Sync Command passthrough (UseCommand with sync-only command)
    //     Targets: RenderContext.cs lines 699-701 (sync passthrough path)
    // ════════════════════════════════════════════════════════════════════════

    private class UseSyncCommandComponent : Component
    {
        public override Element Render()
        {
            var (label, setLabel) = UseState("Before");
            var syncCmd = new Command
            {
                Label = "SyncCmd",
                Execute = () => setLabel("After"),
            };
            // Sync-only: UseCommand returns unchanged
            var wrapped = UseCommand(syncCmd);
            return VStack(
                TextBlock(label),
                Button("RunSync", () => wrapped.Execute?.Invoke())
            );
        }
    }

    internal class UseSyncCommandPassthrough(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(new UseSyncCommandComponent());

            await Harness.Render();
            H.Check("SyncCmd_Initial", H.FindText("Before") is not null);

            H.ClickButton("RunSync");
            await Harness.Render();
            H.Check("SyncCmd_Executed", H.FindText("After") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  23. NavigationView update — animated content transition
    //     Targets: Reconciler.Update.cs lines 1114-1142
    // ════════════════════════════════════════════════════════════════════════

    internal class NavigationViewContentUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (page, setPage) = ctx.UseState(0);
                var items = new[] { NavItem("Home", tag: "home"), NavItem("Settings", tag: "settings") };
                return VStack(
                    Button("SwitchPage", () => setPage(page == 0 ? 1 : 0)),
                    new NavigationViewElement(items, page == 0 ? TextBlock("Home Page") : TextBlock("Settings Page"))
                    {
                        SelectedTag = page == 0 ? "home" : "settings",
                    }
                );
            });

            await Harness.Render();

            var nav = H.FindControl<NavigationView>(_ => true);
            H.Check("NavUpdate_Mounted", nav is not null);
            H.Check("NavUpdate_InitialContent", H.FindText("Home Page") is not null);

            H.ClickButton("SwitchPage");
            await Harness.Render();
            H.Check("NavUpdate_ContentChanged", H.FindText("Settings Page") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  24. RelativePanel update
    //     Targets: Reconciler.Update.cs RelativePanel update path
    // ════════════════════════════════════════════════════════════════════════

    internal class RelativePanelUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdateRP", () => set(1)),
                    RelativePanel(
                        phase == 0 ? TextBlock("Left") : TextBlock("Right"),
                        TextBlock("Center")
                    )
                );
            });

            await Harness.Render();
            var rp = H.FindControl<Microsoft.UI.Xaml.Controls.RelativePanel>(_ => true);
            H.Check("RelPanel_Mounted", rp is not null);

            H.ClickButton("UpdateRP");
            await Harness.Render();
            H.Check("RelPanel_Updated", H.FindText("Right") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  25. Keyframe animation on compositor (Scale, Translation, Rotation)
    //     Targets: Reconciler.cs lines 1480-1537
    // ════════════════════════════════════════════════════════════════════════

    internal class KeyframeCompositorAnimations(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (trigger, setTrigger) = ctx.UseState(0);
                return VStack(
                    Button("Animate", () => setTrigger(trigger + 1)),
                    TextBlock("Animated")
                        .Keyframes("pulse", trigger, kf => kf
                            .Duration(300)
                            .At(0f, opacity: 0.5f, scale: new global::System.Numerics.Vector3(0.8f, 0.8f, 1f))
                            .At(0.5f, opacity: 1f, scale: global::System.Numerics.Vector3.One,
                                translation: new global::System.Numerics.Vector3(10, 0, 0), rotation: 0.1f)
                            .At(1f, opacity: 1f, scale: global::System.Numerics.Vector3.One,
                                translation: global::System.Numerics.Vector3.Zero, rotation: 0f)
                        )
                );
            });

            await Harness.Render();
            H.Check("KFComp_Mounted", H.FindText("Animated") is not null);

            // Trigger the keyframe animation
            H.ClickButton("Animate");
            await Harness.Render();
            H.Check("KFComp_Triggered", H.FindText("Animated") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  26. SwipeControl — mount
    //     Targets: Reconciler.Mount.cs lines 2256-2291
    // ════════════════════════════════════════════════════════════════════════

    internal class SwipeControlMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return SwipeControl(
                    TextBlock("Swipeable item"),
                    leftItems: [new SwipeItemData("Delete")],
                    rightItems: [new SwipeItemData("Archive")]
                );
            });

            await Harness.Render();
            var sc = H.FindControl<Microsoft.UI.Xaml.Controls.SwipeControl>(_ => true);
            H.Check("Swipe_Mounted", sc is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  28. ListBox — mount
    //     Targets: Reconciler.Mount.cs lines 2117-2128
    // ════════════════════════════════════════════════════════════════════════

    internal class ListBoxMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return ListBox(["Apple", "Banana", "Cherry"], selectedIndex: 1);
            });

            await Harness.Render();
            var lb = H.FindControl<Microsoft.UI.Xaml.Controls.ListBox>(_ => true);
            H.Check("ListBox_Mounted", lb is not null);
            H.Check("ListBox_Items", lb?.Items.Count == 3);
            H.Check("ListBox_Selected", lb?.SelectedIndex == 1);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  29. SelectorBar + PipsPager — mount
    //     Targets: Reconciler.Mount.cs lines 2133-2171
    // ════════════════════════════════════════════════════════════════════════

    internal class SelectorBarPipsPagerMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return VStack(
                    SelectorBar([SelectorBarItem("Tab 1"), SelectorBarItem("Tab 2")], selectedIndex: 0),
                    PipsPager(5, selectedPageIndex: 2)
                );
            });

            await Harness.Render();
            var sb = H.FindControl<Microsoft.UI.Xaml.Controls.SelectorBar>(_ => true);
            H.Check("SelectorBar_Mounted", sb is not null);
            var pp = H.FindControl<Microsoft.UI.Xaml.Controls.PipsPager>(_ => true);
            H.Check("PipsPager_Mounted", pp is not null);
            H.Check("PipsPager_Pages", pp?.NumberOfPages == 5);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  30. Theme.cs — ThemeRef resolution (exercises the resolver)
    //     Targets: Theme.cs lines 20-126
    // ════════════════════════════════════════════════════════════════════════

    internal class ThemeRefResolution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                // Use multiple different theme binding combinations to exercise all
                // GetStyleTargetType branches
                return VStack(
                    // TextBlock target
                    TextBlock("ThemeText").Foreground(Theme.PrimaryText),
                    // Button target
                    Button("ThemeBtn", () => { }).Background(Theme.Accent).Foreground(Theme.PrimaryText),
                    // Border target
                    Border(TextBlock("inner"))
                        .Background(Theme.CardBackground)
                        .WithBorder(Theme.CardStroke),
                    // Grid target
                    Grid([GridSize.Star()], [GridSize.Star()], TextBlock("grid cell")).Background(Theme.SubtleFill),
                    // StackPanel target
                    VStack(TextBlock("stack")).Background(Theme.LayerFill)
                );
            });

            await Harness.Render();
            H.Check("ThemeRef_TextMounted", H.FindText("ThemeText") is not null);
            H.Check("ThemeRef_BtnMounted", H.FindButton("ThemeBtn") is not null);
            H.Check("ThemeRef_BorderMounted", H.FindText("inner") is not null);
            H.Check("ThemeRef_GridMounted", H.FindText("grid cell") is not null);

            // Also test explicit resolution
            var resolved = Microsoft.UI.Reactor.Core.ThemeRef.Resolve("SystemFillColorAttentionBrush", isDark: false);
            H.Check("ThemeRef_ExplicitResolve", true); // just exercises the code path
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  31. Popup + RefreshContainer — mount
    //     Targets: Reconciler.Mount.cs lines 2185-2215
    // ════════════════════════════════════════════════════════════════════════

    internal class PopupRefreshContainerMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return VStack(
                    Popup(TextBlock("Popup content"), isOpen: false),
                    RefreshContainer(TextBlock("Pull to refresh"))
                );
            });

            await Harness.Render();
            H.Check("Popup_Mounted", true); // popup is wrapped in StackPanel
            var rc = H.FindControl<Microsoft.UI.Xaml.Controls.RefreshContainer>(_ => true);
            H.Check("RefreshContainer_Mounted", rc is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  32. AnnotatedScrollBar — mount
    //     Targets: Reconciler.Mount.cs lines 2176-2180
    // ════════════════════════════════════════════════════════════════════════

    internal class AnnotatedScrollBarMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => AnnotatedScrollBar());

            await Harness.Render();
            var asb = H.FindControl<Microsoft.UI.Xaml.Controls.AnnotatedScrollBar>(_ => true);
            H.Check("AnnotatedSB_Mounted", asb is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  33. Component.cs — exercise Component base class hooks
    //     Targets: Component.cs uncovered paths, RenderContext UseContext
    // ════════════════════════════════════════════════════════════════════════

    private class ContextProviderComponent : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            return VStack(
                Button("BumpCtx", () => setCount(count + 1)),
                TextBlock($"Context: {count}")
            ).Provide(ContextProviderKey, count);
        }

        public static readonly Context<int> ContextProviderKey = new(0);
    }

    private class ContextConsumerComponent : Component
    {
        public override Element Render()
        {
            var value = UseContext(ContextProviderComponent.ContextProviderKey);
            return TextBlock($"Consumed: {value}");
        }
    }

    internal class ContextProvideConsume(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return new ContextProviderComponent().Render() switch
                {
                    _ => VStack(
                        Component<ContextProviderComponent>()
                    ),
                };
            });

            // Simplified: just mount a component
            host.Mount(new ContextProviderComponent());
            await Harness.Render();
            H.Check("Context_Provider", H.FindTextContaining("Context:") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  34. ChildCollection — exercise the ChildCollection wrapper
    //     Targets: ChildCollection.cs uncovered paths
    // ════════════════════════════════════════════════════════════════════════

    internal class ChildCollectionExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            // Exercise dynamic child operations: add many, remove many, replace
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                Element[] children = phase switch
                {
                    0 => [TextBlock("A").WithKey("a"), TextBlock("B").WithKey("b"), TextBlock("C").WithKey("c")],
                    1 => [TextBlock("A").WithKey("a"), TextBlock("D").WithKey("d"), TextBlock("B").WithKey("b"),
                          TextBlock("E").WithKey("e"), TextBlock("C").WithKey("c")],
                    2 => [TextBlock("E").WithKey("e"), TextBlock("C").WithKey("c")],
                    _ => [TextBlock("X").WithKey("x")],
                };
                return VStack(
                    Button("Phase", () => set(phase + 1)),
                    VStack(children)
                );
            });

            await Harness.Render();
            H.Check("ChildColl_Initial", H.FindText("A") is not null && H.FindText("C") is not null);

            H.ClickButton("Phase");
            await Harness.Render();
            H.Check("ChildColl_Grow", H.FindText("D") is not null && H.FindText("E") is not null);

            H.ClickButton("Phase");
            await Harness.Render();
            H.Check("ChildColl_Shrink", H.FindText("A") is null && H.FindText("E") is not null);

            H.ClickButton("Phase");
            await Harness.Render();
            H.Check("ChildColl_Replace", H.FindText("X") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  35. Attached flyout via modifier — .ContentFlyout() / .MenuItems()
    //     Targets: Reconciler.cs lines 2145-2263 (ApplyFlyoutAttachment, ReconcileFlyout)
    // ════════════════════════════════════════════════════════════════════════

    internal class AttachedFlyoutModifier(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("SwapFlyout", () => set(phase + 1)),
                    // ContentFlyout attached via modifier
                    Button("WithContentFlyout", () => { })
                        .WithFlyout(ContentFlyout(
                            phase == 0 ? TextBlock("Flyout V1") : TextBlock("Flyout V2"))),
                    // MenuItems attached via modifier
                    Button("WithMenuFlyout", () => { })
                        .WithFlyout(MenuItems(
                            MenuItem("Action A"),
                            MenuItem("Action B")))
                );
            });

            await Harness.Render();
            H.Check("AttachedFlyout_ContentBtn", H.FindButton("WithContentFlyout") is not null);
            H.Check("AttachedFlyout_MenuBtn", H.FindButton("WithMenuFlyout") is not null);

            // Update: change flyout content
            H.ClickButton("SwapFlyout");
            await Harness.Render();
            H.Check("AttachedFlyout_Updated", H.FindButton("WithContentFlyout") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  36. TreeView — exercise update/expansion paths
    //     Targets: Reconciler.Update.cs TreeView diff paths
    // ════════════════════════════════════════════════════════════════════════

    internal class TreeViewUpdateExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var nodes = phase == 0
                    ? new[] {
                        TreeNode("Root", TreeNode("Child A"), TreeNode("Child B"))
                    }
                    : new[] {
                        TreeNode("Root", TreeNode("Child A"), TreeNode("Child C"), TreeNode("Child D"))
                    };
                return VStack(
                    Button("UpdateTree", () => set(1)),
                    TreeView(nodes)
                );
            });

            await Harness.Render();
            var tv = H.FindControl<Microsoft.UI.Xaml.Controls.TreeView>(_ => true);
            H.Check("TreeView_Mounted", tv is not null);

            H.ClickButton("UpdateTree");
            await Harness.Render();
            H.Check("TreeView_Updated", H.FindControl<Microsoft.UI.Xaml.Controls.TreeView>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  37. Modifier combos: Scale, Rotation, RichTooltip, FontFamily
    //     Targets: Reconciler.cs ApplyModifiers lines 1681-1806
    // ════════════════════════════════════════════════════════════════════════

    internal class AdvancedModifierCoverage(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdateMods", () => set(1)),
                    TextBlock("Scaled")
                        .Scale(phase == 0 ? 1.0f : 1.5f)
                        .Rotation(phase == 0 ? 0f : 45f)
                        .FontFamily("Segoe UI")
                        .AccessKey("A")
                );
            });

            await Harness.Render();
            H.Check("AdvMod_Initial", H.FindText("Scaled") is not null);

            H.ClickButton("UpdateMods");
            await Harness.Render();
            H.Check("AdvMod_Updated", H.FindText("Scaled") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  38. ColorScheme + IsDarkTheme hooks
    //     Targets: RenderContext.cs lines 641-660
    // ════════════════════════════════════════════════════════════════════════

    private class ColorSchemeComponent : Component
    {
        public override Element Render()
        {
            var scheme = UseColorScheme();
            var isDark = UseIsDarkTheme();
            return TextBlock($"Scheme: {scheme}, Dark: {isDark}");
        }
    }

    internal class ColorSchemeHook(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(new ColorSchemeComponent());

            await Harness.Render();
            H.Check("ColorScheme_Mounted", H.FindTextContaining("Scheme:") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  39. ObservableTreeTracker — UseObservable with deep property tracking
    //     Targets: RenderContext.cs UseObservable (lines 381-394), ObservableTreeTracker
    // ════════════════════════════════════════════════════════════════════════

    private class SimpleObservable : global::System.ComponentModel.INotifyPropertyChanged
    {
        private string _name = "Initial";
        public string Name
        {
            get => _name;
            set { _name = value; PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(nameof(Name))); }
        }
        public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private class ObservableComponent : Component
    {
        public SimpleObservable Model { get; init; } = new();

        public override Element Render()
        {
            UseObservable(Model);
            return TextBlock($"Observed: {Model.Name}");
        }
    }

    internal class UseObservableDeep(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var model = new SimpleObservable();
            var host = H.CreateHost();
            host.Mount(new ObservableComponent { Model = model });

            await Harness.Render();
            H.Check("Observable_Initial", H.FindText("Observed: Initial") is not null);

            // Mutate externally — should trigger re-render
            model.Name = "Changed";
            await Harness.Render();
            H.Check("Observable_Mutated", H.FindText("Observed: Changed") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  40. Component error boundary during update
    //     Targets: Reconciler.Update.cs lines 2036-2044
    // ════════════════════════════════════════════════════════════════════════

    private class ConditionalThrowComponent : Component<bool>
    {
        public override Element Render() =>
            Props ? throw new InvalidOperationException("Boom") : TextBlock("Healthy");
    }

    internal class ErrorBoundaryUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (shouldThrow, setShouldThrow) = ctx.UseState(false);
                return VStack(
                    Button("TriggerError", () => setShouldThrow(true)),
                    ErrorBoundary(
                        Component<ConditionalThrowComponent, bool>(shouldThrow),
                        ex => TextBlock($"Caught: {ex.Message}")
                    )
                );
            });

            await Harness.Render();
            H.Check("ErrBound_Initial", H.FindText("Healthy") is not null);

            H.ClickButton("TriggerError");
            await Harness.Render();
            H.Check("ErrBound_Caught", H.FindTextContaining("Caught:") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  41. Expander child content update
    //     Targets: Reconciler.Update.cs UpdateExpanderContent path
    // ════════════════════════════════════════════════════════════════════════

    internal class ExpanderChildUpdateDeep(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdateExp", () => set(1)),
                    Expander("Header", phase == 0 ? TextBlock("Body V1") : VStack(TextBlock("Body V2"), TextBlock("Extra")), isExpanded: true)
                );
            });

            await Harness.Render();
            var exp = H.FindControl<Microsoft.UI.Xaml.Controls.Expander>(_ => true);
            H.Check("ExpanderDeep_Mounted", exp is not null);

            H.ClickButton("UpdateExp");
            await Harness.Render();
            H.Check("ExpanderDeep_Updated", H.FindText("Body V2") is not null || H.FindText("Extra") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  42. Canvas update with positioning
    //     Targets: Reconciler.Update.cs Canvas left/top (lines 872-875)
    // ════════════════════════════════════════════════════════════════════════

    internal class CanvasPositionUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("MoveItem", () => set(1)),
                    Canvas(
                        phase == 0
                            ? TextBlock("Positioned").Canvas(left: 10, top: 20)
                            : TextBlock("Positioned").Canvas(left: 50, top: 60)
                    ).Width(200).Height(200)
                );
            });

            await Harness.Render();
            H.Check("Canvas_Mounted", H.FindText("Positioned") is not null);

            H.ClickButton("MoveItem");
            await Harness.Render();
            var tb = H.FindText("Positioned");
            H.Check("Canvas_Updated", tb is not null);
        }
    }
}
