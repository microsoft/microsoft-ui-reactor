#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Microsoft.UI.Reactor.VsExtension.UI;
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

        private static void AssertStatus(ReactorEmbedControlViewModel vm, EmbedStatus status, string text, Brush brush, bool buildingVisible)
        {
            vm.TransitionTo(status);

            Assert.Equal(text, vm.StatusText);
            Assert.Same(brush, vm.StatusBrush);
            Assert.Equal(buildingVisible, vm.BuildingVisible);
        }
    }
}
