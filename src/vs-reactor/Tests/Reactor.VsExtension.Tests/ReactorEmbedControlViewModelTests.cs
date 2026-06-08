#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Microsoft.UI.Reactor.VsExtension.UI;
using Microsoft.VisualStudio.Threading;
using Xunit;

namespace Reactor.VsExtension.Tests
{
    public sealed class HwndHostPlaceholderTests
    {
        [Fact]
        public void HwndHostPlaceholder_RegistersClassOnce()
        {
            var before = PlaceholderClass.RegisterClassCallCount;

            var first = PlaceholderClass.EnsureRegistered();
            var afterFirst = PlaceholderClass.RegisterClassCallCount;
            var second = PlaceholderClass.EnsureRegistered();
            var afterSecond = PlaceholderClass.RegisterClassCallCount;

            Assert.Equal("ReactorEmbedPlaceholder", first);
            Assert.Equal(first, second);
            Assert.InRange(afterFirst - before, 0, 1);
            Assert.Equal(afterFirst, afterSecond);
        }

        [Fact]
        public void HwndHostPlaceholder_RaisesResized_OnPositionChange()
        {
            RunOnStaThread(() =>
            {
                var placeholder = new HwndHostPlaceholder();
                Rect? observed = null;
                placeholder.PlaceholderResized += (_, rect) => observed = rect;

                var expected = new Rect(0, 0, 100, 50);
                placeholder.RaiseResizedForTest(expected);

                Assert.Equal(expected, observed);
            });
        }

        private static void RunOnStaThread(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw failure;
            }
        }
    }

    public sealed class ReactorEmbedControlViewModelTests
    {
        [Fact]
        public void VM_SetComponents_PopulatesDropdown()
        {
            var vm = new ReactorEmbedControlViewModel();

            vm.SetComponents(new[] { "Counter", "Todo" }, selected: "Todo");

            Assert.Equal(new[] { "Counter", "Todo" }, vm.Components.ToArray());
            Assert.Equal("Todo", vm.SelectedComponent);
            Assert.False(vm.IsManuallyPinned);
        }

        [Fact]
        public void VM_SelectComponent_PinsManually()
        {
            var vm = new ReactorEmbedControlViewModel();
            vm.SetComponents(new[] { "Counter", "Todo" });

            vm.SelectedComponent = "Todo";

            Assert.Equal("Todo", vm.SelectedComponent);
            Assert.True(vm.IsManuallyPinned);
        }

        [Fact]
        public void VM_StatusTransitions_Idle_Launching_Embedded_Building_Respawning()
        {
            var vm = new ReactorEmbedControlViewModel();

            AssertStatus(vm, EmbedStatus.Idle, "Idle", Brushes.Gray, buildingVisible: false);
            AssertStatus(vm, EmbedStatus.Launching, "Launching…", Brushes.Gray, buildingVisible: false);
            AssertStatus(vm, EmbedStatus.Embedded, "Live", Brushes.Green, buildingVisible: false);
            AssertStatus(vm, EmbedStatus.Building, "Building…", Brushes.Goldenrod, buildingVisible: true);
            AssertStatus(vm, EmbedStatus.Respawning, "Respawning…", Brushes.Goldenrod, buildingVisible: false);
        }

        [Fact]
        public void VM_PlaceholderVisible_TrueForEveryStateExceptEmbeddedAndBuilding()
        {
            // The placeholder overlay covers the preview area whenever the embedded
            // child can't be expected to be drawing content. Embedded is the obvious
            // "live" state; Building keeps the prior frame visible under a thin
            // "Building…" strip rather than collapsing back to the placeholder, so
            // both states must keep the HwndHost visible. Any other state — including
            // BuildFailed and Crashed where the error overlay also paints on top —
            // must hide the empty HwndHost so the WPF placeholder shows through
            // (otherwise WPF airspace would leave the placeholder HWND's leftover
            // pixels visible, defeating the whole "no void content" fix).
            var vm = new ReactorEmbedControlViewModel();

            foreach (EmbedStatus status in Enum.GetValues(typeof(EmbedStatus)))
            {
                vm.TransitionTo(status);
                var expectPlaceholder = status != EmbedStatus.Embedded && status != EmbedStatus.Building;
                Assert.True(
                    vm.PlaceholderVisible == expectPlaceholder,
                    $"PlaceholderVisible should be {expectPlaceholder} for {status} (got {vm.PlaceholderVisible}).");
                Assert.Equal(!vm.PlaceholderVisible, vm.EmbeddedHostVisible);
            }
        }

        [Fact]
        public void VM_PlaceholderMessage_UpdatesWithStatus()
        {
            var vm = new ReactorEmbedControlViewModel();

            vm.TransitionTo(EmbedStatus.Launching);
            Assert.Equal("Starting Reactor preview", vm.PlaceholderTitle);
            Assert.Contains("launching the preview host", vm.PlaceholderDetail, StringComparison.OrdinalIgnoreCase);

            vm.TransitionTo(EmbedStatus.WaitingForHandshake);
            Assert.Equal("Connecting to preview", vm.PlaceholderTitle);

            vm.TransitionTo(EmbedStatus.Respawning);
            Assert.Equal("Restarting Reactor preview", vm.PlaceholderTitle);

            vm.TransitionTo(EmbedStatus.Crashed);
            Assert.Equal("Preview crashed", vm.PlaceholderTitle);

            vm.TransitionTo(EmbedStatus.BuildFailed);
            Assert.Equal("Build failed", vm.PlaceholderTitle);
        }

        [Fact]
        public void VM_PlaceholderVisible_DefaultsTrueOnConstruction()
        {
            // Before any TransitionTo, the VM is in Idle and the preview hasn't been
            // started — the placeholder must already be the visible layer so the
            // tool window doesn't flash "void" content on first reveal.
            var vm = new ReactorEmbedControlViewModel();

            Assert.True(vm.PlaceholderVisible);
            Assert.False(vm.EmbeddedHostVisible);
            Assert.False(string.IsNullOrEmpty(vm.PlaceholderTitle));
            Assert.False(string.IsNullOrEmpty(vm.PlaceholderDetail));
        }

        [Fact]
        public void VM_ForceReloadCommand_DisabledWhenIdle()
        {
            var vm = new ReactorEmbedControlViewModel();

            vm.TransitionTo(EmbedStatus.Idle);
            Assert.False(vm.ForceReloadCommand.CanExecute(null));

            vm.TransitionTo(EmbedStatus.Embedded);
            Assert.True(vm.ForceReloadCommand.CanExecute(null));
        }

        [Fact]
        public void VM_AutoTrack_DoesNotOverridePinUntilCleared()
        {
            var vm = new ReactorEmbedControlViewModel();
            vm.SetComponents(new[] { "Counter", "Todo" });
            vm.SelectedComponent = "Counter";

            vm.OnActiveDocumentChanged("Todo.cs", new[] { "Todo" });

            Assert.Equal("Counter", vm.SelectedComponent);

            vm.ClearPin();
            vm.OnActiveDocumentChanged("Todo.cs", new[] { "Todo" });

            Assert.Equal("Todo", vm.SelectedComponent);
            Assert.False(vm.IsManuallyPinned);
        }

        [Fact]
        public void VM_OnPlaceholderResized_RaisesEvent()
        {
            var vm = new ReactorEmbedControlViewModel();
            Rect? observed = null;
            vm.PlaceholderRectChanged += (_, rect) => observed = rect;

            var expected = new Rect(1, 2, 300, 200);
            vm.OnPlaceholderResized(expected);

            Assert.Equal(expected, observed);
            Assert.Equal(expected, vm.LastPlaceholderRect);
        }

        [Fact]
        public void VM_ShowError_SetsOverlayFields()
        {
            var vm = new ReactorEmbedControlViewModel();

            vm.ShowError("Bad build", "CS1002");

            Assert.True(vm.ErrorOverlayVisible);
            Assert.Equal("Bad build", vm.ErrorTitle);
            Assert.Equal("CS1002", vm.ErrorDetail);
        }

        [Fact]
        public void VM_ClearError_HidesOverlay()
        {
            var vm = new ReactorEmbedControlViewModel();
            vm.ShowError("Bad build", "CS1002");

            vm.ClearError();

            Assert.False(vm.ErrorOverlayVisible);
            Assert.Equal(string.Empty, vm.ErrorTitle);
            Assert.Equal(string.Empty, vm.ErrorDetail);
        }

        [Fact]
        public void VM_TransitionTo_FromBackgroundThread_DoesNotThrow_WhenWpfDispatcherActive()
        {
            // Regression: TransitionTo called RaiseCanExecuteChanged which
            // touches ButtonBase.Command (a WPF DependencyProperty) — that's
            // legal only on the dispatcher thread. EmbedSession callbacks run
            // on threadpool/JTF threads, so a bg-thread TransitionTo threw
            // InvalidOperationException: "The calling thread cannot access
            // this object because a different thread owns it." The fix
            // sync-dispatches to Application.Current.Dispatcher.
            RunWithDispatcher(vm =>
            {
                Exception? observed = null;
                var worker = new Thread(() =>
                {
                    try { vm.TransitionTo(EmbedStatus.Embedded); }
                    catch (Exception ex) { observed = ex; }
                });
                worker.Start();
                worker.Join();

                Assert.Null(observed);
                Assert.Equal(EmbedStatus.Embedded, vm.CurrentStatus);
            });
        }

        [Fact]
        public void VM_SetComponents_FromBackgroundThread_DoesNotThrow_WhenWpfDispatcherActive()
        {
            // Regression: Components is an ObservableCollection bound to a
            // ComboBox via CollectionView, which throws
            // "This type of CollectionView does not support changes to its
            // SourceCollection from a thread different from the Dispatcher
            // thread" on bg-thread Clear/Add. The fix sync-dispatches.
            RunWithDispatcher(vm =>
            {
                Exception? observed = null;
                var worker = new Thread(() =>
                {
                    try { vm.SetComponents(new[] { "Counter", "Todo" }, selected: "Todo"); }
                    catch (Exception ex) { observed = ex; }
                });
                worker.Start();
                worker.Join();

                Assert.Null(observed);
                Assert.Equal(new[] { "Counter", "Todo" }, vm.Components.ToArray());
                Assert.Equal("Todo", vm.SelectedComponent);
            });
        }

        [Fact]
        public void VM_OnActiveDocumentChanged_FromBackgroundThread_DoesNotThrow_WhenWpfDispatcherActive()
        {
            // Same dispatcher-affinity hazard as SetComponents — auto-tracking
            // mutates Components when a new component first appears in the
            // active document, and that callback historically arrived from a
            // JTF worker thread.
            RunWithDispatcher(vm =>
            {
                vm.SetComponents(new[] { "Counter" });
                vm.ClearPin();
                Exception? observed = null;
                var worker = new Thread(() =>
                {
                    try { vm.OnActiveDocumentChanged("Todo.cs", new[] { "Todo" }); }
                    catch (Exception ex) { observed = ex; }
                });
                worker.Start();
                worker.Join();

                Assert.Null(observed);
                Assert.Equal("Todo", vm.SelectedComponent);
            });
        }

        // Spins up a shared STA-thread WPF Application + Dispatcher and pumps
        // the test body onto it. WPF allows only one Application per AppDomain
        // and xunit runs all tests in the same AppDomain, so we keep one
        // permanent dispatcher thread alive across the test run.
        private static readonly object _dispatcherLock = new object();
        private static System.Windows.Threading.Dispatcher? _sharedDispatcher;

        private static System.Windows.Threading.Dispatcher EnsureSharedDispatcher()
        {
            lock (_dispatcherLock)
            {
                if (_sharedDispatcher != null && !_sharedDispatcher.HasShutdownStarted)
                {
                    return _sharedDispatcher;
                }

                var ready = new ManualResetEventSlim(false);
                System.Windows.Threading.Dispatcher? created = null;
                var thread = new Thread(() =>
                {
                    if (Application.Current == null)
                    {
                        _ = new Application();
                    }
                    created = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(created));
                    ready.Set();
                    System.Windows.Threading.Dispatcher.Run();
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = "ReactorVsExtensionTests.WpfDispatcher";
                thread.Start();
                ready.Wait();
                _sharedDispatcher = created;
                return _sharedDispatcher!;
            }
        }

        private static void RunWithDispatcher(Action<ReactorEmbedControlViewModel> body)
        {
            var dispatcher = EnsureSharedDispatcher();

            // Construct the VM on the dispatcher thread so any DependencyObjects
            // it creates inherit the right affinity. The body itself runs on
            // the calling thread so it can spawn a *different* worker thread
            // to exercise the cross-thread guards.
            ReactorEmbedControlViewModel? vm = null;
            dispatcher.Invoke(() =>
            {
                var context = new JoinableTaskContext();
                vm = new ReactorEmbedControlViewModel(context.Factory);
            });
            body(vm!);
        }

        private static void AssertStatus(ReactorEmbedControlViewModel vm, EmbedStatus status, string text, Brush brush, bool buildingVisible)
        {
            vm.TransitionTo(status);

            Assert.Equal(text, vm.StatusText);
            Assert.Same(brush, vm.StatusBrush);
            Assert.Equal(buildingVisible, vm.BuildingVisible);
        }
    }
}
