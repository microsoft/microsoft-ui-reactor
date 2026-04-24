using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression test for the dynamic-reorder crash: TypeRegistration.Update and
/// .Unmount used hard casts ((TControl)control) that threw InvalidCastException
/// when the control at a position was a different UIElement subclass than expected.
/// The fix uses pattern matching and falls back to a fresh mount / silent skip.
/// </summary>
internal static class TypeRegistrationMismatchFixtures
{
    private record WidgetElement(string Label) : Element;

    /// <summary>
    /// Calls Reconcile with a control whose concrete type (TextBlock) does not
    /// match the registration's TControl (Button). Before the fix, this threw
    /// InvalidCastException inside TypeRegistration.Update.
    /// </summary>
    internal class UpdateControlTypeMismatch(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var reconciler = host.Reconciler;
            bool mountFallbackCalled = false;

            reconciler.RegisterType<WidgetElement, Button>(
                mount: (r, el, rerender) =>
                {
                    mountFallbackCalled = true;
                    return new Button { Content = el.Label };
                },
                update: (r, oldEl, newEl, ctrl, rerender) =>
                {
                    ctrl.Content = newEl.Label;
                    return null; // in-place update
                });

            var oldEl = new WidgetElement("Old");
            var newEl = new WidgetElement("New");

            // Mount normally — produces a Button
            var initial = reconciler.Reconcile(null, oldEl, null, () => { });
            H.Check("TypeMismatch_Update_InitialMountIsButton",
                initial is Button);

            mountFallbackCalled = false;

            // Now reconcile with a TextBlock standing in for the control.
            // Same element type (WidgetElement) so CanUpdate returns true,
            // but the control is a TextBlock, not a Button → type mismatch.
            // Before the fix: InvalidCastException in (TControl)control.
            // After the fix: falls back to a fresh mount.
            var mismatchedControl = new TextBlock();
            UIElement? result = null;
            bool didNotThrow = true;
            try
            {
                result = reconciler.Reconcile(oldEl, newEl, mismatchedControl, () => { });
            }
            catch (InvalidCastException)
            {
                didNotThrow = false;
            }

            H.Check("TypeMismatch_Update_NoInvalidCastException", didNotThrow);
            H.Check("TypeMismatch_Update_FallsBackToMount", mountFallbackCalled);
            H.Check("TypeMismatch_Update_ResultIsButton", result is Button);
            H.Check("TypeMismatch_Update_ResultHasNewLabel",
                result is Button b && (string)b.Content == "New");

            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Calls Unmount on a TextBlock whose Tag references a WidgetElement,
    /// but the registration's TControl is Button. Before the fix, this threw
    /// InvalidCastException inside TypeRegistration.Unmount.
    /// </summary>
    internal class UnmountControlTypeMismatch(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var reconciler = host.Reconciler;
            bool unmountHandlerCalled = false;

            reconciler.RegisterType<WidgetElement, Button>(
                mount: (r, el, rerender) => new Button { Content = el.Label },
                update: (r, oldEl, newEl, ctrl, rerender) => null,
                unmount: (r, ctrl) => { unmountHandlerCalled = true; });

            // Create a TextBlock and stamp it with a WidgetElement via the
            // reactor attached DP, as the reconciler does during mount. This
            // mimics a control that ended up in the wrong slot after a dynamic reorder.
            var mismatchedControl = new TextBlock();
            Reconciler.SetElementTag(mismatchedControl, new WidgetElement("Stale"));

            // Unmount should silently skip — not throw InvalidCastException
            bool didNotThrow = true;
            try
            {
                reconciler.UnmountChild(mismatchedControl);
            }
            catch (InvalidCastException)
            {
                didNotThrow = false;
            }

            H.Check("TypeMismatch_Unmount_NoInvalidCastException", didNotThrow);
            H.Check("TypeMismatch_Unmount_HandlerSkippedForWrongType", !unmountHandlerCalled);

            await Task.CompletedTask;
        }
    }
}
