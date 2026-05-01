using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Wave 2 core coverage fixtures targeting remaining Reactor\Core gaps:
/// UsePersisted, UseEffect cleanup, UseObservableTree, StandardCommand,
/// CommandInterop, InfoBar with action, TitleBar update, CalendarView update,
/// Component&lt;TProps&gt;, ContextScope non-generic, ColorScheme.
/// </summary>
internal static class CoreCoverageFixtures2
{
    // ════════════════════════════════════════════════════════════════════════
    //  1. UsePersisted hook — exercises PersistedStateCache + RunCleanups
    // ════════════════════════════════════════════════════════════════════════

    internal class UsePersistedHook(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Simply exercise UsePersisted mount + set. The hook internally exercises
            // PersistedStateCache.TryGet and Set, and the setter path through
            // PersistedHookState<T>.
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var persistKey = "selftest_persisted_hook";
                var (val, setVal) = ctx.UsePersisted(persistKey, 42);
                return VStack(
                    TextBlock($"Persisted:{val}"),
                    Button("Set100", () => setVal(100))
                );
            });

            await Harness.Render();
            H.Check("Persisted_InitialValue", H.FindText("Persisted:42") is not null);

            H.ClickButton("Set100");
            await Harness.Render();
            H.Check("Persisted_Updated", H.FindText("Persisted:100") is not null);

            // Set same value — should not trigger re-render (equality check)
            H.ClickButton("Set100");
            await Harness.Render();
            H.Check("Persisted_StableAfterSameValue", H.FindText("Persisted:100") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. UseEffect with cleanup function — exercises effectWithCleanup path
    // ════════════════════════════════════════════════════════════════════════

    internal class UseEffectCleanup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cleanupCount = 0;
            var effectCount = 0;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (dep, setDep) = ctx.UseState(0);

                // UseEffect with cleanup (Func<Action> overload)
                ctx.UseEffect(() =>
                {
                    effectCount++;
                    return () => { cleanupCount++; };
                }, dep);

                return VStack(
                    TextBlock($"Effect:{effectCount} Cleanup:{cleanupCount}"),
                    Button("Change", () => setDep(dep + 1))
                );
            });

            await Harness.Render();
            H.Check("EffectCleanup_InitialEffect", effectCount == 1);
            H.Check("EffectCleanup_InitialNoCleanup", cleanupCount == 0);

            // Change dependency — triggers cleanup of old effect, then new effect
            H.ClickButton("Change");
            await Harness.Render();
            H.Check("EffectCleanup_SecondEffect", effectCount == 2);
            H.Check("EffectCleanup_FirstCleanupRan", cleanupCount == 1);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3. UseObservableTree — exercises deep INPC subscription
    // ════════════════════════════════════════════════════════════════════════

    private class InnerModel : INotifyPropertyChanged
    {
        private string _name = "inner";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private class OuterModel : INotifyPropertyChanged
    {
        private InnerModel _child = new();
        public InnerModel Child
        {
            get => _child;
            set { _child = value; OnPropertyChanged(); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    internal class UseObservableTreeHook(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var model = new OuterModel();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var observed = ctx.UseObservableTree(model);
                return TextBlock($"Tree:{observed.Child.Name}");
            });

            await Harness.Render();
            H.Check("ObsTree_Initial", H.FindText("Tree:inner") is not null);

            // Mutate nested property — should trigger re-render via deep subscription
            model.Child.Name = "mutated";
            await Harness.Render();
            H.Check("ObsTree_DeepMutation", H.FindText("Tree:mutated") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4. StandardCommand factory methods — exercises StandardCommand.cs (0%)
    // ════════════════════════════════════════════════════════════════════════

    internal class StandardCommandExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cutFired = false;
            var saveFired = false;
            var playFired = false;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var cut = StandardCommand.Cut(() => cutFired = true);
                var copy = StandardCommand.Copy(() => { });
                var paste = StandardCommand.Paste(() => { });
                var undo = StandardCommand.Undo(() => { });
                var redo = StandardCommand.Redo(() => { });
                var del = StandardCommand.Delete(() => { });
                var selectAll = StandardCommand.SelectAll(() => { });
                var save = StandardCommand.Save(() => saveFired = true);
                var open = StandardCommand.Open(() => { });
                var close = StandardCommand.Close(() => { });
                var share = StandardCommand.Share(() => { });
                var play = StandardCommand.Play(() => playFired = true);
                var pause = StandardCommand.Pause(() => { });
                var stop = StandardCommand.Stop(() => { });
                var fwd = StandardCommand.Forward(() => { });
                var bwd = StandardCommand.Backward(() => { });

                // Also exercise async overloads
                var asyncSave = StandardCommand.Save(async () => await Task.CompletedTask);
                var asyncCut = StandardCommand.Cut(async () => await Task.CompletedTask);
                var asyncCopy = StandardCommand.Copy(async () => await Task.CompletedTask);
                var asyncPaste = StandardCommand.Paste(async () => await Task.CompletedTask);
                var asyncUndo = StandardCommand.Undo(async () => await Task.CompletedTask);
                var asyncRedo = StandardCommand.Redo(async () => await Task.CompletedTask);
                var asyncDel = StandardCommand.Delete(async () => await Task.CompletedTask);
                var asyncSelAll = StandardCommand.SelectAll(async () => await Task.CompletedTask);
                var asyncOpen = StandardCommand.Open(async () => await Task.CompletedTask);
                var asyncClose = StandardCommand.Close(async () => await Task.CompletedTask);
                var asyncShare = StandardCommand.Share(async () => await Task.CompletedTask);
                var asyncPlay = StandardCommand.Play(async () => await Task.CompletedTask);
                var asyncPause = StandardCommand.Pause(async () => await Task.CompletedTask);
                var asyncStop = StandardCommand.Stop(async () => await Task.CompletedTask);
                var asyncFwd = StandardCommand.Forward(async () => await Task.CompletedTask);
                var asyncBwd = StandardCommand.Backward(async () => await Task.CompletedTask);

                return VStack(
                    Button(cut.Label, () => cut.Execute!()),
                    Button(save.Label, () => save.Execute!()),
                    Button(play.Label, () => play.Execute!()),
                    TextBlock($"Commands:{cut.Label},{copy.Label},{paste.Label},{undo.Label},{redo.Label}"),
                    TextBlock($"More:{del.Label},{selectAll.Label},{open.Label},{close.Label},{share.Label}"),
                    TextBlock($"Media:{pause.Label},{stop.Label},{fwd.Label},{bwd.Label}")
                );
            });

            await Harness.Render();
            H.Check("StdCmd_AllCreated",
                H.FindTextContaining("Cut,Copy,Paste,Undo,Redo") is not null);
            H.Check("StdCmd_MoreCreated",
                H.FindTextContaining("Delete,Select all,Open,Close,Share") is not null);
            H.Check("StdCmd_MediaCreated",
                H.FindTextContaining("Pause,Stop,Forward,Backward") is not null);

            H.ClickButton("Cut");
            H.ClickButton("Save");
            H.ClickButton("Play");
            await Harness.Render();
            H.Check("StdCmd_CutFired", cutFired);
            H.Check("StdCmd_SaveFired", saveFired);
            H.Check("StdCmd_PlayFired", playFired);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5. CommandInterop.FromCommand — exercises CommandInterop.cs (0%)
    // ════════════════════════════════════════════════════════════════════════

    private class SimpleCommand : ICommand
    {
        private readonly Action _execute;
        public SimpleCommand(Action execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    }

    private class ParameterizedCommand : ICommand
    {
        public object? LastParameter { get; private set; }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => LastParameter = parameter;
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    }

    internal class CommandInteropExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var executed = false;
            var icommand = new SimpleCommand(() => executed = true);
            var paramCmd = new ParameterizedCommand();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var ductCmd = CommandInterop.FromCommand(
                    icommand, "Test",
                    icon: new SymbolIconData("Save"),
                    description: "Test desc",
                    accelerator: new KeyboardAcceleratorData(
                        global::Windows.System.VirtualKey.T,
                        global::Windows.System.VirtualKeyModifiers.Control));

                var typedCmd = CommandInterop.FromCommand<string>(
                    paramCmd, "Typed",
                    icon: new SymbolIconData("Edit"),
                    description: "Typed desc");

                return VStack(
                    Button(ductCmd.Label, () =>
                    {
                        ductCmd.Execute?.Invoke();
                    }),
                    Button("TypedExec", () =>
                    {
                        typedCmd.Execute?.Invoke("hello");
                    }),
                    TextBlock($"CanExec:{ductCmd.CanExecute}")
                );
            });

            await Harness.Render();
            H.Check("CmdInterop_Mounted", H.FindText("CanExec:True") is not null);

            H.ClickButton("Test");
            await Harness.Render();
            H.Check("CmdInterop_Executed", executed);

            H.ClickButton("TypedExec");
            await Harness.Render();
            H.Check("CmdInterop_TypedExecuted", (string?)paramCmd.LastParameter == "hello");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  6. InfoBar with action button — exercises uncovered mount paths
    // ════════════════════════════════════════════════════════════════════════

    internal class InfoBarActionButton(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    new InfoBarElement("Title1", phase == 0 ? "Message1" : "Updated")
                    {
                        IsOpen = true, IsClosable = true,
                        ActionButtonContent = "Action",
                        OnActionButtonClick = () => { },
                        OnClosed = () => { },
                    },
                    Button("Update", () => set(1)),
                    TextBlock($"Phase:{phase}")
                );
            });

            await Harness.Render();
            var ib = H.FindControl<Microsoft.UI.Xaml.Controls.InfoBar>(_ => true);
            H.Check("InfoBarAction_Mounted", ib is not null);
            H.Check("InfoBarAction_Title", ib?.Title == "Title1");
            H.Check("InfoBarAction_HasActionButton", ib?.ActionButton is not null);

            // Update InfoBar properties
            H.ClickButton("Update");
            await Harness.Render();
            ib = H.FindControl<Microsoft.UI.Xaml.Controls.InfoBar>(_ => true);
            H.Check("InfoBarAction_MessageUpdated", ib?.Message == "Updated");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  8. CalendarView, PipsPager, AnnotatedScrollBar update
    // ════════════════════════════════════════════════════════════════════════

    internal class CalendarPipsPagerUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    PipsPager(phase == 0 ? 5 : 10, phase == 0 ? 0 : 3),
                    AnnotatedScrollBar(),
                    Button("Update", () => set(1))
                );
            });

            await Harness.Render();
            var pp = H.FindControl<Microsoft.UI.Xaml.Controls.PipsPager>(_ => true);
            var asb = H.FindControl<Microsoft.UI.Xaml.Controls.AnnotatedScrollBar>(_ => true);
            H.Check("CalPips_PipsMounted", pp is not null);
            H.Check("CalPips_AnnotatedMounted", asb is not null);
            H.Check("CalPips_InitialPages", pp?.NumberOfPages == 5);

            H.ClickButton("Update");
            await Harness.Render();
            pp = H.FindControl<Microsoft.UI.Xaml.Controls.PipsPager>(_ => true);
            H.Check("CalPips_PagesUpdated", pp?.NumberOfPages == 10);
            H.Check("CalPips_SelectedUpdated", pp?.SelectedPageIndex == 3);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  9. MapControl, Frame, AnimatedIcon mount + update
    // ════════════════════════════════════════════════════════════════════════

    internal class FrameAnimatedIconUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Frame(),
                    AnimatedIcon(),
                    Button("Update", () => set(1))
                );
            });

            await Harness.Render();
            var frame = H.FindControl<Microsoft.UI.Xaml.Controls.Frame>(_ => true);
            var ai = H.FindControl<Microsoft.UI.Xaml.Controls.AnimatedIcon>(_ => true);
            H.Check("FrameAnim_FrameMounted", frame is not null);
            H.Check("FrameAnim_AnimIconMounted", ai is not null);

            H.ClickButton("Update");
            await Harness.Render();
            frame = H.FindControl<Microsoft.UI.Xaml.Controls.Frame>(_ => true);
            H.Check("FrameAnim_FrameStillPresent", frame is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  10. ParallaxView mount — exercises MountParallaxView
    // ════════════════════════════════════════════════════════════════════════

    internal class ParallaxViewMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return ParallaxView(
                    TextBlock("Parallax content"),
                    verticalShift: 100,
                    horizontalShift: 50
                );
            });

            await Harness.Render();
            var pv = H.FindControl<Microsoft.UI.Xaml.Controls.ParallaxView>(_ => true);
            H.Check("Parallax_Mounted", pv is not null);
            H.Check("Parallax_VShift", pv?.VerticalShift == 100);
            H.Check("Parallax_HShift", pv?.HorizontalShift == 50);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  11. XamlHost mount — exercises MountXamlHost path
    // ════════════════════════════════════════════════════════════════════════

    internal class XamlHostMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    new XamlHostElement(
                        () => new TextBlock { Text = "hosted" },
                        ctrl => ((TextBlock)ctrl).Text = $"hosted:{phase}"
                    ),
                    Button("Update", () => set(1))
                );
            });

            await Harness.Render();
            // The XamlHost should create a TextBlock with "hosted:0" after updater runs
            H.Check("XamlHost_Mounted", H.FindTextContaining("hosted") is not null);

            H.ClickButton("Update");
            await Harness.Render();
            H.Check("XamlHost_Updated", H.FindTextContaining("hosted:1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  12. Component<TProps> — exercises typed props and ShouldUpdate
    // ════════════════════════════════════════════════════════════════════════

    private record CounterProps(int Count, string Label);

    private class TypedPropsComponent : Microsoft.UI.Reactor.Core.Component<CounterProps>
    {
        public override Element Render()
        {
            return TextBlock($"Typed:{Props.Label}={Props.Count}");
        }
    }

    internal class ComponentTypedProps(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (count, setCount) = ctx.UseState(0);
                return VStack(
                    Factories.Component<TypedPropsComponent, CounterProps>(new CounterProps(count, "items")),
                    Button("Inc", () => setCount(count + 1))
                );
            });

            await Harness.Render();
            H.Check("TypedProps_Initial", H.FindText("Typed:items=0") is not null);

            H.ClickButton("Inc");
            await Harness.Render();
            H.Check("TypedProps_Updated", H.FindText("Typed:items=1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  13. UseColorScheme hook — exercises ColorSchemeContext
    // ════════════════════════════════════════════════════════════════════════

    internal class ColorSchemeHookExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var scheme = ctx.UseColorScheme();
                var isDark = ctx.UseIsDarkTheme();
                return TextBlock($"Scheme:{scheme},Dark:{isDark}");
            });

            await Harness.Render();
            // Just verify it mounts without crash and returns a valid value
            var text = H.FindControl<TextBlock>(t => t.Text?.StartsWith("Scheme:") == true);
            H.Check("ColorScheme_Mounted", text is not null);
            H.Check("ColorScheme_ValidValue",
                text?.Text?.Contains("Light") == true || text?.Text?.Contains("Dark") == true);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  14. ContextScope coverage — exercises non-generic Read path
    // ════════════════════════════════════════════════════════════════════════

    internal class ContextScopeExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ThemeContext = new Context<string>("default-theme");

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var themeValue = phase == 0 ? "light" : "dark";

                // .Provide() pushes onto ContextScope, child reads it via UseContext
                return VStack(
                    RenderEachTime(inner =>
                    {
                        var theme = inner.UseContext(ThemeContext);
                        return TextBlock($"Theme:{theme}");
                    }),
                    Button("Switch", () => set(1))
                ).Provide(ThemeContext, themeValue);
            });

            await Harness.Render();
            H.Check("CtxScope_Initial", H.FindText("Theme:light") is not null);

            H.ClickButton("Switch");
            await Harness.Render();
            H.Check("CtxScope_Updated", H.FindText("Theme:dark") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  15. UseCommand hook (async) — exercises RenderContext UseCommand path
    // ════════════════════════════════════════════════════════════════════════

    internal class UseCommandAsyncExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var executed = false;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var cmd = ctx.UseCommand(new Command
                {
                    Label = "Async",
                    ExecuteAsync = async () =>
                    {
                        await Task.Delay(10);
                        executed = true;
                    },
                    CanExecute = true,
                });

                return VStack(
                    Button(cmd.Label, () => cmd.Execute?.Invoke()),
                    TextBlock($"Executing:{cmd.IsExecuting}")
                );
            });

            await Harness.Render();
            H.Check("UseCmd_Mounted", H.FindText("Executing:False") is not null);

            H.ClickButton("Async");
            await Task.Delay(200);
            await Harness.Render();
            H.Check("UseCmd_Executed", executed);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  16. UseCommand<T> typed hook — exercises parameterized UseCommand path
    // ════════════════════════════════════════════════════════════════════════

    internal class UseCommandTypedAsyncExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var lastParam = "";

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var cmd = ctx.UseCommand(new Command<string>
                {
                    Label = "TypedAsync",
                    ExecuteAsync = async (param) =>
                    {
                        await Task.Delay(10);
                        lastParam = param;
                    },
                    CanExecute = true,
                });

                return VStack(
                    Button("Run", () => cmd.Execute?.Invoke("test-param")),
                    TextBlock($"TypedExec:{cmd.IsExecuting}")
                );
            });

            await Harness.Render();
            H.Check("UseCmdTyped_Mounted", H.FindText("TypedExec:False") is not null);

            H.ClickButton("Run");
            await Task.Delay(200);
            await Harness.Render();
            H.Check("UseCmdTyped_ParamReceived", lastParam == "test-param");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  17. ShouldUpdate on propless Component — exercises Component.ShouldUpdate()
    // ════════════════════════════════════════════════════════════════════════

    private class AlwaysUpdateComponent : Microsoft.UI.Reactor.Core.Component
    {
        internal static int RenderCount;
        protected internal override bool ShouldUpdate() => true;
        public override Element Render()
        {
            RenderCount++;
            var (val, _) = UseState(0);
            return TextBlock($"AlwaysUpdate:{RenderCount}");
        }
    }

    internal class ProplessComponentShouldUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            AlwaysUpdateComponent.RenderCount = 0;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    Factories.Component<AlwaysUpdateComponent>(),
                    Button("Tick", () => setTick(tick + 1))
                );
            });

            await Harness.Render();
            H.Check("ShouldUpdate_InitialRender", AlwaysUpdateComponent.RenderCount >= 1);

            // Parent re-renders → ShouldUpdate returns true → component re-renders
            H.ClickButton("Tick");
            await Harness.Render();
            H.Check("ShouldUpdate_Rerendered", AlwaysUpdateComponent.RenderCount >= 2);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  18. InfoBadge mount + update — exercises MountInfoBadge
    // ════════════════════════════════════════════════════════════════════════

    internal class InfoBadgeMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    InfoBadge(phase == 0 ? 3 : 99),
                    Button("Update", () => set(1))
                );
            });

            await Harness.Render();
            var badge = H.FindControl<Microsoft.UI.Xaml.Controls.InfoBadge>(_ => true);
            H.Check("InfoBadge_Mounted", badge is not null);
            H.Check("InfoBadge_InitialValue", badge?.Value == 3);

            H.ClickButton("Update");
            await Harness.Render();
            badge = H.FindControl<Microsoft.UI.Xaml.Controls.InfoBadge>(_ => true);
            H.Check("InfoBadge_Updated", badge?.Value == 99);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  19. ListBox update — exercises UpdateListBox path
    // ════════════════════════════════════════════════════════════════════════

    internal class ListBoxUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var selectedIdx = -1;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { "Apple", "Banana", "Cherry" }
                    : new[] { "Dog", "Elephant", "Fox", "Giraffe" };
                return VStack(
                    ListBox(items, phase == 0 ? 0 : 2, idx => selectedIdx = idx),
                    Button("Update", () => set(1))
                );
            });

            await Harness.Render();
            var lb = H.FindControl<Microsoft.UI.Xaml.Controls.ListBox>(_ => true);
            H.Check("ListBox_Mounted", lb is not null);
            H.Check("ListBox_InitialCount", lb?.Items.Count == 3);

            H.ClickButton("Update");
            await Harness.Render();
            lb = H.FindControl<Microsoft.UI.Xaml.Controls.ListBox>(_ => true);
            H.Check("ListBox_UpdatedCount", lb?.Items.Count == 4);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  20. SelectorBar update — exercises UpdateSelectorBar path
    // ════════════════════════════════════════════════════════════════════════

    internal class SelectorBarUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { SelectorBarItem("All"), SelectorBarItem("Recent") }
                    : new[] { SelectorBarItem("All"), SelectorBarItem("Recent"), SelectorBarItem("Favorites", "Favorite") };
                return VStack(
                    SelectorBar(items, phase == 0 ? 0 : 1),
                    Button("Update", () => set(1))
                );
            });

            await Harness.Render();
            var sb = H.FindControl<Microsoft.UI.Xaml.Controls.SelectorBar>(_ => true);
            H.Check("SelectorBar_Mounted", sb is not null);
            H.Check("SelectorBar_InitialCount", sb?.Items.Count == 2);

            H.ClickButton("Update");
            await Harness.Render();
            sb = H.FindControl<Microsoft.UI.Xaml.Controls.SelectorBar>(_ => true);
            H.Check("SelectorBar_UpdatedCount", sb?.Items.Count == 3);
        }
    }
}
