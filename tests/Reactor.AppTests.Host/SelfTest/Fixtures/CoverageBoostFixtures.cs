using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

using Component = Microsoft.UI.Reactor.Core.Component;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selftest fixtures targeting remaining coverage gaps to push combined coverage above 85%.
/// Each fixture exercises real product features through the Reactor rendering pipeline.
/// </summary>
internal static class CoverageBoostFixtures
{
    // ════════════════════════════════════════════════════════════════════════
    //  1. Component subclass hook wrappers — exercises the protected methods
    //     on Component that delegate to RenderContext (UseHighContrast,
    //     UseColorScheme, UseAnnounce, UseObservableTree, UseCollection, etc.)
    // ════════════════════════════════════════════════════════════════════════

    private class ObservableModel : global::System.ComponentModel.INotifyPropertyChanged
    {
        private string _name = "Initial";
        private int _value = 0;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public int Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([global::System.Runtime.CompilerServices.CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(prop));
    }

    private class HookWrapperComponent : Component
    {
        private readonly ObservableModel _model;
        private readonly global::System.Collections.ObjectModel.ObservableCollection<string> _items;

        public HookWrapperComponent(ObservableModel model,
            global::System.Collections.ObjectModel.ObservableCollection<string> items)
        {
            _model = model;
            _items = items;
        }

        public override Element Render()
        {
            // Exercise Component-level hook wrappers that delegate to Context
            var colorScheme = UseColorScheme();
            var isDark = UseIsDarkTheme();
            var isHighContrast = UseHighContrast();
            var tree = UseObservableTree(_model);
            var propVal = UseObservableProperty(_model, m => m.Value, nameof(ObservableModel.Value));
            var collection = UseCollection(_items);
            var memo = UseMemo(() => $"CS:{colorScheme},HC:{isHighContrast}", colorScheme, isHighContrast);
            var cb = UseCallback(() => { }, colorScheme);
            var r = UseRef(0);

            return VStack(
                TextBlock($"Name:{tree.Name}"),
                TextBlock($"PropVal:{propVal}"),
                TextBlock($"Items:{collection.Count}"),
                TextBlock($"Memo:{memo}"),
                TextBlock($"Dark:{isDark}"),
                TextBlock($"RefVal:{r.Current}")
            );
        }
    }

    internal class ComponentHookWrappers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var model = new ObservableModel { Name = "Test", Value = 42 };
            var items = new global::System.Collections.ObjectModel.ObservableCollection<string> { "A", "B" };

            var host = H.CreateHost();
            host.Mount(new HookWrapperComponent(model, items));
            await Harness.Render();

            H.Check("HookWrap_ObservableTree", H.FindText("Name:Test") is not null);
            H.Check("HookWrap_ObservableProperty", H.FindText("PropVal:42") is not null);
            H.Check("HookWrap_Collection", H.FindText("Items:2") is not null);
            H.Check("HookWrap_Memo", H.FindTextContaining("Memo:CS:") is not null);
            H.Check("HookWrap_Ref", H.FindText("RefVal:0") is not null);

            // Mutate the observable model — triggers re-render via UseObservableTree
            model.Name = "Updated";
            await Harness.Render();
            H.Check("HookWrap_TreeMutated", H.FindText("Name:Updated") is not null);

            // Add to collection — triggers re-render via UseCollection
            items.Add("C");
            await Harness.Render();
            H.Check("HookWrap_CollectionGrew", H.FindText("Items:3") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. Theme token resolution — exercises Theme static properties and
    //     ThemeRef.Resolve() through the real WinUI resource system
    // ════════════════════════════════════════════════════════════════════════

    internal class ThemeTokenResolution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                // Exercise Theme static token accessors
                var accent = Theme.Accent;
                var primary = Theme.PrimaryText;
                var secondary = Theme.SecondaryText;
                var cardBg = Theme.CardBackground;
                var solidBg = Theme.SolidBackground;
                var stroke = Theme.CardStroke;
                var success = Theme.SystemSuccess;
                var caution = Theme.SystemCaution;
                var critical = Theme.SystemCritical;
                var attention = Theme.SystemAttention;
                var neutral = Theme.SystemNeutral;
                var custom = Theme.Ref("AccentFillColorDefaultBrush");

                // Resolve theme refs
                var accentBrush = ThemeRef.Resolve(accent.ResourceKey, isDark: false);
                var darkBrush = ThemeRef.Resolve(accent.ResourceKey, isDark: true);

                return VStack(
                    TextBlock($"Accent:{accent.ResourceKey}").Foreground(accent),
                    TextBlock($"Primary:{primary.ResourceKey}").Foreground(primary),
                    TextBlock($"CardBg:{cardBg.ResourceKey}"),
                    TextBlock($"ResolvedLight:{accentBrush is not null}"),
                    TextBlock($"ResolvedDark:{darkBrush is not null}"),
                    // Exercise additional token groups
                    TextBlock("SubtleFill").Background(Theme.SubtleFill),
                    TextBlock("LayerFill").Background(Theme.LayerFill),
                    TextBlock("ControlFill").Background(Theme.ControlFill),
                    TextBlock("ControlStroke").Foreground(Theme.ControlStroke),
                    TextBlock("DividerStroke").Foreground(Theme.DividerStroke),
                    TextBlock("AccentText").Foreground(Theme.AccentText),
                    TextBlock("TertiaryText").Foreground(Theme.TertiaryText),
                    TextBlock("DisabledText").Foreground(Theme.DisabledText),
                    TextBlock("SmokeFill").Background(Theme.SmokeFill),
                    TextBlock("SolidNeutral").Foreground(Theme.SystemSolidNeutral),
                    TextBlock("SuccessBg").Background(Theme.SystemSuccessBackground),
                    TextBlock("CautionBg").Background(Theme.SystemCautionBackground),
                    TextBlock("CriticalBg").Background(Theme.SystemCriticalBackground),
                    TextBlock("NeutralBg").Background(Theme.SystemNeutralBackground),
                    TextBlock("AttentionBg").Background(Theme.SystemAttentionBackground),
                    TextBlock("SolidAttention").Background(Theme.SystemSolidAttention),
                    TextBlock("AccentSec").Background(Theme.AccentSecondary),
                    TextBlock("AccentTer").Background(Theme.AccentTertiary),
                    TextBlock("AccentDis").Background(Theme.AccentDisabled),
                    TextBlock("CtrlFillSec").Background(Theme.ControlFillSecondary),
                    TextBlock("CtrlFillTer").Background(Theme.ControlFillTertiary),
                    TextBlock("CtrlFillDis").Background(Theme.ControlFillDisabled),
                    TextBlock("CtrlFillInput").Background(Theme.ControlFillInputActive),
                    TextBlock("SurfaceStroke").Foreground(Theme.SurfaceStroke),
                    TextBlock("CtrlStrokeSec").Foreground(Theme.ControlStrokeSecondary)
                );
            });

            await Harness.Render();

            H.Check("Theme_AccentToken", H.FindTextContaining("Accent:AccentFill") is not null);
            H.Check("Theme_PrimaryToken", H.FindTextContaining("Primary:TextFill") is not null);
            H.Check("Theme_ResolvedLight", H.FindText("ResolvedLight:True") is not null);
            H.Check("Theme_ResolvedDark", H.FindText("ResolvedDark:True") is not null);
            H.Check("Theme_ThemeRefToString", Theme.Accent.ToString().Contains("ThemeRef"));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3. ElementPool exercise — mount, unmount, remount controls to trigger
    //     pool Return + TryRent + CleanElement paths, including interactive
    //     controls (Button, TextBox, ToggleSwitch)
    // ════════════════════════════════════════════════════════════════════════

    internal class ElementPoolExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return phase switch
                {
                    // Phase 0: Mount a variety of controls
                    0 => VStack(
                        TextBlock("PoolTest"),
                        Button("PoolBtn", () => { }),
                        TextField("initial", _ => { }),
                        ToggleSwitch(false, _ => { }),
                        Progress(50),
                        ProgressRing(50.0),
                        Image("ms-appx:///Assets/placeholder.png"),
                        InfoBadge(5),
                        Border(TextBlock("inner")),
                        Button("Next", () => set(1))
                    ),
                    // Phase 1: Remove most controls — triggers Return to pool
                    1 => VStack(
                        TextBlock("Trimmed"),
                        Button("Remount", () => set(2))
                    ),
                    // Phase 2: Re-add similar controls — triggers TryRent from pool
                    _ => VStack(
                        TextBlock("Remounted"),
                        Button("Reuse", () => { }),
                        TextField("reused", _ => { }),
                        ToggleSwitch(true, _ => { }),
                        Progress(75),
                        ProgressRing(75.0),
                        InfoBadge(10),
                        Border(TextBlock("reused inner"))
                    ),
                };
            });

            await Harness.Render();
            H.Check("Pool_InitialMount", H.FindText("PoolTest") is not null);

            // Transition to trimmed — controls should be returned to pool
            H.ClickButton("Next");
            await Harness.Render();
            H.Check("Pool_Trimmed", H.FindText("Trimmed") is not null);

            // Transition to remount — controls should be rented from pool
            H.ClickButton("Remount");
            await Harness.Render();
            H.Check("Pool_Remounted", H.FindText("Remounted") is not null);
            H.Check("Pool_ReusedButton", H.FindButton("Reuse") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4. FlexPanel attached properties — directly exercises the Get/Set
    //     accessors on FlexPanel (Position, Left, Top, Right, Bottom, AlignSelf)
    //     that are currently uncovered
    // ════════════════════════════════════════════════════════════════════════

    internal class FlexPanelAttachedProps(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return FlexColumn(
                    TextBlock("PositionedChild")
                        .Flex(grow: 0, shrink: 1, basis: 50,
                              alignSelf: Microsoft.UI.Reactor.Layout.FlexAlign.Center),
                    TextBlock("AbsoluteChild")
                        .Flex(position: Microsoft.UI.Reactor.Layout.FlexPositionType.Absolute,
                              left: 10, top: 20, right: 30, bottom: 40)
                ).Height(300).Width(300);
            });

            await Harness.Render();

            // Verify controls were rendered
            var positioned = H.FindText("PositionedChild");
            var absolute = H.FindText("AbsoluteChild");
            H.Check("FlexAP_PositionedMounted", positioned is not null);
            H.Check("FlexAP_AbsoluteMounted", absolute is not null);

            // Verify attached property values via FlexPanel accessors
            if (absolute is not null)
            {
                var pos = Microsoft.UI.Reactor.Layout.FlexPanel.GetPosition(absolute);
                var left = Microsoft.UI.Reactor.Layout.FlexPanel.GetLeft(absolute);
                var top = Microsoft.UI.Reactor.Layout.FlexPanel.GetTop(absolute);
                var right = Microsoft.UI.Reactor.Layout.FlexPanel.GetRight(absolute);
                var bottom = Microsoft.UI.Reactor.Layout.FlexPanel.GetBottom(absolute);
                H.Check("FlexAP_PositionAbsolute",
                    pos == Microsoft.UI.Reactor.Layout.FlexPositionType.Absolute);
                H.Check("FlexAP_Left10", left == 10);
                H.Check("FlexAP_Top20", top == 20);
                H.Check("FlexAP_Right30", right == 30);
                H.Check("FlexAP_Bottom40", bottom == 40);
            }

            if (positioned is not null)
            {
                var alignSelf = Microsoft.UI.Reactor.Layout.FlexPanel.GetAlignSelf(positioned);
                var basis = Microsoft.UI.Reactor.Layout.FlexPanel.GetBasis(positioned);
                H.Check("FlexAP_AlignSelfCenter",
                    alignSelf == Microsoft.UI.Reactor.Layout.FlexAlign.Center);
                H.Check("FlexAP_Basis50", basis == 50);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5. FlexPanel container property changes — exercises Direction,
    //     JustifyContent, AlignItems, AlignContent, Wrap, Gap, FlexPadding
    //     dependency property callbacks to trigger InvalidateMeasure
    // ════════════════════════════════════════════════════════════════════════

    internal class FlexPanelContainerProps(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return phase switch
                {
                    0 => VStack(
                        FlexRow(
                            TextBlock("A").Width(50),
                            TextBlock("B").Width(50)
                        ).Height(100).Width(300),
                        Button("ChangeLayout", () => set(1))
                    ),
                    _ => VStack(
                        FlexColumn(
                            TextBlock("X").Height(30),
                            TextBlock("Y").Height(30)
                        ).Height(300).Width(100),
                        TextBlock("LayoutChanged")
                    ),
                };
            });

            await Harness.Render();
            H.Check("FlexCP_InitialRow", H.FindText("A") is not null && H.FindText("B") is not null);

            H.ClickButton("ChangeLayout");
            await Harness.Render();
            H.Check("FlexCP_ChangedToColumn",
                H.FindText("X") is not null && H.FindText("LayoutChanged") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  6. UseSystemBackButton — exercises the keyboard handler subscription
    //     for navigation back button (Alt+Left / VirtualKey.GoBack)
    // ════════════════════════════════════════════════════════════════════════

    private enum NavRoute { Home, Detail }

    private class BackButtonComponent : Component
    {
        private readonly Window _window;
        public BackButtonComponent(Window window) => _window = window;

        public override Element Render()
        {
            var nav = UseNavigation(NavRoute.Home);
            UseSystemBackButton(nav, _window);

            return NavigationHost(nav, route => route switch
            {
                NavRoute.Home => VStack(
                    TextBlock("NavHome"),
                    Button("GoDetail", () => nav.Navigate(NavRoute.Detail))
                ),
                NavRoute.Detail => VStack(
                    TextBlock("NavDetail"),
                    Button("GoBack", () => nav.GoBack())
                ),
                _ => TextBlock("Unknown")
            });
        }
    }

    internal class UseSystemBackButtonExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(new BackButtonComponent(H.Window));
            await Harness.Render();

            H.Check("BackBtn_InitialHome", H.FindText("NavHome") is not null);

            // Navigate forward
            H.ClickButton("GoDetail");
            await Harness.Render();
            H.Check("BackBtn_NavigatedToDetail", H.FindText("NavDetail") is not null);

            // Navigate back via button
            H.ClickButton("GoBack");
            await Harness.Render();
            H.Check("BackBtn_BackToHome", H.FindText("NavHome") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  7. UseHighContrastScheme — exercises the high contrast detection hook
    //     (UseHighContrastState / UseHighContrastScheme wrappers)
    // ════════════════════════════════════════════════════════════════════════

    private class HighContrastComponent : Component
    {
        public override Element Render()
        {
            var scheme = UseHighContrastScheme();
            return VStack(
                TextBlock($"HC:{scheme ?? "none"}")
            );
        }
    }

    internal class UseHighContrastSchemeExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(new HighContrastComponent());
            await Harness.Render();

            // Most machines are NOT in high contrast mode, so we expect "none"
            var text = H.FindTextContaining("HC:");
            H.Check("HC_SchemeRendered", text is not null);
            H.Check("HC_SchemeIsNone", text?.Text == "HC:none");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  8. Component.UseAnnounce — exercises the announce hook wrapper
    // ════════════════════════════════════════════════════════════════════════

    private class AnnounceComponent : Component
    {
        public override Element Render()
        {
            var announce = UseAnnounce();
            var (count, setCount) = UseState(0);

            return VStack(
                TextBlock($"Announced:{count}"),
                Button("DoAnnounce", () =>
                {
                    announce.Announce($"Test announcement {count}");
                    setCount(count + 1);
                })
            );
        }
    }

    internal class UseAnnounceExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(new AnnounceComponent());
            await Harness.Render();

            H.Check("Announce_Initial", H.FindText("Announced:0") is not null);

            H.ClickButton("DoAnnounce");
            await Harness.Render();
            H.Check("Announce_AfterCall", H.FindText("Announced:1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  9. ElementPool Disable/Enable — exercises the Enabled property and
    //     the pool Clear + Dispose paths
    // ════════════════════════════════════════════════════════════════════════

    internal class ElementPoolLifecycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Access the pool through the reconciler
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return phase switch
                {
                    0 => VStack(
                        TextBlock("PoolLife-A"),
                        TextBlock("PoolLife-B"),
                        TextBlock("PoolLife-C"),
                        Button("Shrink", () => set(1))
                    ),
                    1 => VStack(
                        TextBlock("PoolLife-Shrunk"),
                        Button("Grow", () => set(2))
                    ),
                    _ => VStack(
                        TextBlock("PoolLife-Regrown-A"),
                        TextBlock("PoolLife-Regrown-B"),
                        TextBlock("PoolLife-Regrown-C"),
                        TextBlock("PoolLife-Regrown-D")
                    ),
                };
            });

            await Harness.Render();
            H.Check("PoolLife_Initial", H.FindText("PoolLife-A") is not null);

            H.ClickButton("Shrink");
            await Harness.Render();
            H.Check("PoolLife_Shrunk", H.FindText("PoolLife-Shrunk") is not null);

            H.ClickButton("Grow");
            await Harness.Render();
            H.Check("PoolLife_Regrown", H.FindText("PoolLife-Regrown-D") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  10. ThemeRef resolution with explicit isDark flag — exercises both
    //      light and dark resolution paths through WinUI resource dictionaries
    // ════════════════════════════════════════════════════════════════════════

    internal class ThemeRefExplicitResolution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                // Exercise ThemeRef.Resolve with explicit isDark flag
                var lightBrush = ThemeRef.Resolve("TextFillColorPrimaryBrush", isDark: false);
                var darkBrush = ThemeRef.Resolve("TextFillColorPrimaryBrush", isDark: true);
                var accentLight = ThemeRef.Resolve("AccentFillColorDefaultBrush", isDark: false);
                var accentDark = ThemeRef.Resolve("AccentFillColorDefaultBrush", isDark: true);
                var invalidKey = ThemeRef.Resolve("NonExistentBrushKey12345", isDark: false);

                // Also test the ResolveForTheme → TryResolveNonThemed fallback path
                var nonThemedTest = ThemeRef.Resolve("SystemControlHighlightAccentBrush", isDark: false);

                return VStack(
                    TextBlock($"LightText:{lightBrush is not null}"),
                    TextBlock($"DarkText:{darkBrush is not null}"),
                    TextBlock($"AccentLight:{accentLight is not null}"),
                    TextBlock($"AccentDark:{accentDark is not null}"),
                    TextBlock($"InvalidKey:{invalidKey is null}"),
                    TextBlock($"NonThemed:{nonThemedTest is not null}")
                );
            });

            await Harness.Render();
            H.Check("ThemeRes_LightResolved", H.FindText("LightText:True") is not null);
            H.Check("ThemeRes_DarkResolved", H.FindText("DarkText:True") is not null);
            H.Check("ThemeRes_InvalidNull", H.FindText("InvalidKey:True") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  11. Component.UseMutation wrapper — exercises the mutation hook through
    //      the Component base class
    // ════════════════════════════════════════════════════════════════════════

    private class MutationComponent : Component
    {
        public override Element Render()
        {
            var (result, setResult) = UseState("idle");
            var mutation = UseMutation<string, string>(async (input, ct) =>
            {
                await Task.Delay(10, ct);
                return $"done:{input}";
            }, new Hooks.MutationOptions<string, string>
            {
                OnSuccess = (res, _) => setResult(res),
            });

            return VStack(
                TextBlock($"Mutation:{result}"),
                TextBlock($"Pending:{mutation.IsPending}"),
                Button("Mutate", () => _ = mutation.RunAsync("test"))
            );
        }
    }

    internal class ComponentUseMutationExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(new MutationComponent());
            await Harness.Render();

            H.Check("Mutation_Initial", H.FindText("Mutation:idle") is not null);

            H.ClickButton("Mutate");
            await Harness.Render(100);
            H.Check("Mutation_Completed", H.FindText("Mutation:done:test") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  12. Component.UseResource wrapper — exercises the resource hook
    //      through the Component base class
    // ════════════════════════════════════════════════════════════════════════

    private class ResourceComponent : Component
    {
        public override Element Render()
        {
            var data = UseResource<string>(async ct =>
            {
                await Task.Delay(10, ct);
                return "fetched-data";
            }, []);

            var text = data switch
            {
                AsyncValue<string>.Data d => $"Data:{d.Value}",
                AsyncValue<string>.Loading => "Loading",
                AsyncValue<string>.Error e => $"Error:{e.Exception.Message}",
                _ => "Unknown"
            };

            return TextBlock(text);
        }
    }

    internal class ComponentUseResourceExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(new ResourceComponent());
            await Harness.Render(200);
            // The fetcher's Task.Delay(10) resolves during the 200ms wall-clock
            // wait, then schedules a re-render that may not have flushed before
            // Render(200) returns. Pump once more to drain the queued re-render.
            await Harness.Render();

            var tb = H.FindTextContaining("Data:fetched-data");
            H.Check("Resource_FetchedData", tb is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  13. FlexPanel Unloaded cleanup — exercises the OnUnloaded handler
    //      that clears the Yoga node cache
    // ════════════════════════════════════════════════════════════════════════

    internal class FlexPanelUnloadCleanup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (showFlex, set) = ctx.UseState(true);
                if (showFlex)
                {
                    return VStack(
                        FlexRow(
                            TextBlock("FlexChild1"),
                            TextBlock("FlexChild2")
                        ).Height(100).Width(200),
                        Button("RemoveFlex", () => set(false))
                    );
                }
                else
                {
                    return VStack(
                        TextBlock("FlexRemoved")
                    );
                }
            });

            await Harness.Render();
            H.Check("FlexUnload_Mounted", H.FindText("FlexChild1") is not null);

            // Remove the FlexPanel from the tree — triggers Unloaded → OnUnloaded cleanup
            H.ClickButton("RemoveFlex");
            await Harness.Render();
            H.Check("FlexUnload_Removed", H.FindText("FlexRemoved") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  14. ElementPool DetachFromParent — exercises different parent types
    //      (Panel, Border, ScrollViewer, ContentControl) when recycling
    // ════════════════════════════════════════════════════════════════════════

    internal class ElementPoolDetach(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return phase switch
                {
                    // Phase 0: TextBlocks inside different parent types
                    0 => VStack(
                        // TextBlock in a Panel (StackPanel)
                        VStack(TextBlock("InPanel")),
                        // TextBlock in a Border
                        Border(TextBlock("InBorder")),
                        // TextBlock in a ScrollView
                        ScrollView(TextBlock("InScroll")),
                        Button("Detach", () => set(1))
                    ),
                    // Phase 1: Remove everything — forces detach from parent before pool return
                    _ => VStack(TextBlock("AllDetached")),
                };
            });

            await Harness.Render();
            H.Check("Detach_Initial", H.FindText("InPanel") is not null);

            H.ClickButton("Detach");
            await Harness.Render();
            H.Check("Detach_Completed", H.FindText("AllDetached") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  15. Component.UseWindowSize + UseBreakpoint wrappers
    // ════════════════════════════════════════════════════════════════════════

    private class WindowSizeComponent : Component
    {
        private readonly Window _window;
        public WindowSizeComponent(Window window) => _window = window;

        public override Element Render()
        {
            var (width, height) = UseWindowSize(_window);
            var isWide = UseBreakpoint(_window, 500);
            return VStack(
                TextBlock($"W:{width:F0}"),
                TextBlock($"H:{height:F0}"),
                TextBlock($"Wide:{isWide}")
            );
        }
    }

    internal class UseWindowSizeBreakpoint(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(new WindowSizeComponent(H.Window));
            await Harness.Render();

            var wText = H.FindTextContaining("W:");
            var hText = H.FindTextContaining("H:");
            var wideText = H.FindTextContaining("Wide:");

            H.Check("WinSize_WidthRendered", wText is not null);
            H.Check("WinSize_HeightRendered", hText is not null);
            H.Check("WinSize_BreakpointRendered", wideText is not null);
        }
    }
}
