using System.Diagnostics;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 057 §9.3 reactive <see cref="ElementRef"/> cell assertions that require
/// real WinUI <see cref="FrameworkElement"/> instances and therefore run in the
/// selftest host instead of the headless unit-test process.
/// </summary>
internal static class ReactiveElementRefCellFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            SetCurrent_FiresOnce_And_SameReference_IsNoOp();
            SetCurrent_Null_After_NonNull_FiresNull();
            Subscriber_Bookkeeping_Subscribe_Unsubscribe();
            Typed_CurrentChanged_Projects_From_Inner_Cell();
            Reentrant_SameCell_Write_Is_Dropped_Without_StackOverflow();

            return Task.CompletedTask;
        }

        private void SetCurrent_FiresOnce_And_SameReference_IsNoOp()
        {
            var cell = new ElementRef();
            var button = new Button();
            var fireCount = 0;
            FrameworkElement? observed = null;
            cell.CurrentChanged += value =>
            {
                fireCount++;
                observed = value;
            };

            cell.SetCurrent(button);
            H.Check(
                "ReactiveElementRef_SetCurrent_FiresOnce",
                fireCount == 1 && ReferenceEquals(observed, button) && ReferenceEquals(cell.Current, button));

            cell.SetCurrent(button);
            H.Check("ReactiveElementRef_SameReference_NoOp", fireCount == 1);
        }

        private void SetCurrent_Null_After_NonNull_FiresNull()
        {
            var cell = new ElementRef();
            var button = new Button();
            var fireCount = 0;
            FrameworkElement? observed = button;
            cell.CurrentChanged += value =>
            {
                fireCount++;
                observed = value;
            };

            cell.SetCurrent(button);
            cell.SetCurrent(null);

            H.Check(
                "ReactiveElementRef_SetNullAfterNonNull_FiresNull",
                fireCount == 2 && observed is null && cell.Current is null);
        }

        private void Subscriber_Bookkeeping_Subscribe_Unsubscribe()
        {
            var cell = new ElementRef();
            var first = new Button();
            var second = new Grid();
            var firstHandlerCount = 0;
            var secondHandlerCount = 0;
            void FirstHandler(FrameworkElement? _) => firstHandlerCount++;
            void SecondHandler(FrameworkElement? _) => secondHandlerCount++;

            cell.CurrentChanged += FirstHandler;
            cell.CurrentChanged += SecondHandler;
            cell.CurrentChanged -= FirstHandler;
            cell.SetCurrent(first);

            H.Check(
                "ReactiveElementRef_Unsubscribe_Removes_Handler",
                firstHandlerCount == 0 && secondHandlerCount == 1);

            cell.CurrentChanged -= SecondHandler;
            cell.SetCurrent(second);
            H.Check(
                "ReactiveElementRef_AllHandlersRemoved_NoInvocation",
                firstHandlerCount == 0 && secondHandlerCount == 1);
        }

        private void Typed_CurrentChanged_Projects_From_Inner_Cell()
        {
            var typed = TypedElementRef.Create<Button>();
            ElementRef inner = typed;
            var button = new Button();
            var grid = new Grid();
            var fireCount = 0;
            Button? observed = null;
            void Handler(Button? value)
            {
                fireCount++;
                observed = value;
            }

            typed.CurrentChanged += Handler;
            inner.SetCurrent(button);
            H.Check(
                "ReactiveElementRef_TypedCurrentChanged_ProjectsButton",
                fireCount == 1 && ReferenceEquals(observed, button) && ReferenceEquals(typed.Current, button));

            inner.SetCurrent(grid);
            H.Check(
                "ReactiveElementRef_TypedCurrentChanged_ProjectsMismatchToNull",
                fireCount == 2 && observed is null && typed.Current is null);

            typed.CurrentChanged -= Handler;
            inner.SetCurrent(null);
            H.Check("ReactiveElementRef_TypedCurrentChanged_Unsubscribe", fireCount == 2);
        }

        private void Reentrant_SameCell_Write_Is_Dropped_Without_StackOverflow()
        {
            var cell = new ElementRef();
            var first = new Button();
            var second = new Grid();
            var fireCount = 0;
            FrameworkElement? observed = null;
            void Handler(FrameworkElement? value)
            {
                fireCount++;
                observed = value;
                cell.SetCurrent(second);
            }

            cell.CurrentChanged += Handler;
            var assertUi = DisableDebugAssertUi();
            try
            {
                cell.SetCurrent(first);
            }
            finally
            {
                RestoreDebugAssertUi(assertUi);
            }

            H.Check(
                "ReactiveElementRef_ReentrantSameCell_DropsNestedDispatch",
                fireCount == 1 && ReferenceEquals(observed, first) && ReferenceEquals(cell.Current, second));

            cell.SetCurrent(second);
            H.Check("ReactiveElementRef_ReentrantSameCell_SameFinalReferenceNoOp", fireCount == 1);
        }

        private static List<(DefaultTraceListener Listener, bool AssertUiEnabled)> DisableDebugAssertUi()
        {
            var saved = new List<(DefaultTraceListener Listener, bool AssertUiEnabled)>();
            foreach (TraceListener listener in Trace.Listeners)
            {
                if (listener is DefaultTraceListener defaultTraceListener)
                {
                    saved.Add((defaultTraceListener, defaultTraceListener.AssertUiEnabled));
                    defaultTraceListener.AssertUiEnabled = false;
                }
            }

            return saved;
        }

        private static void RestoreDebugAssertUi(List<(DefaultTraceListener Listener, bool AssertUiEnabled)> saved)
        {
            foreach (var (listener, assertUiEnabled) in saved)
            {
                listener.AssertUiEnabled = assertUiEnabled;
            }
        }
    }
}
