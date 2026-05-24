using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Coverage fixtures for Reactor/Hooks — FocusManager, UseFocus hook.
/// </summary>
internal static class HooksCoverageFixtures
{
    // ── FocusManager: registration, ordering, navigation ──

    internal class FocusManagerRegistration(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var fm = new FocusManager();

            // Initially empty
            H.Check("FM_InitialEmpty", fm.Fields.Count == 0);

            // Register fields
            fm.Register("name");
            fm.Register("email");
            fm.Register("phone");
            H.Check("FM_ThreeFields", fm.Fields.Count == 3);
            H.Check("FM_Order0", fm.Fields[0] == "name");
            H.Check("FM_Order1", fm.Fields[1] == "email");
            H.Check("FM_Order2", fm.Fields[2] == "phone");

            // Duplicate registration is no-op
            fm.Register("name");
            H.Check("FM_NoDuplicate", fm.Fields.Count == 3);

            // IsFirstField / IsLastField
            H.Check("FM_IsFirst_Name", fm.IsFirstField("name"));
            H.Check("FM_NotFirst_Email", !fm.IsFirstField("email"));
            H.Check("FM_IsLast_Phone", fm.IsLastField("phone"));
            H.Check("FM_NotLast_Email", !fm.IsLastField("email"));

            // Clear
            fm.Clear();
            H.Check("FM_ClearEmpty", fm.Fields.Count == 0);

            return Task.CompletedTask;
        }
    }

    internal class FocusManagerNavigation(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var fm = new FocusManager();
            fm.Register("a");
            fm.Register("b");
            fm.Register("c");

            // FocusNext with null focuses first
            // (We can't verify actual focus without real controls, but we exercise the code paths)
            fm.FocusNext(null);    // should attempt to focus "a"
            fm.FocusNext("a");     // should attempt to focus "b"
            fm.FocusNext("b");     // should attempt to focus "c"

            // FocusNext on last field triggers submit
            var submitCalled = false;
            fm.SetSubmitHandler(() => submitCalled = true);
            fm.FocusNext("c");     // last field → submit
            H.Check("FM_SubmitCalled", submitCalled);

            // FocusPrevious with null focuses last
            fm.FocusPrevious(null); // should attempt to focus "c"
            fm.FocusPrevious("c");  // should attempt to focus "b"
            fm.FocusPrevious("b");  // should attempt to focus "a"

            // FocusPrevious on first field is no-op
            fm.FocusPrevious("a");  // no-op (index 0, nothing before)

            // FocusNext/FocusPrevious with unknown field
            fm.FocusNext("unknown");     // index < 0, returns
            fm.FocusPrevious("unknown"); // index < 0, returns

            // FocusField with unregistered control — no crash
            fm.FocusField("nonexistent");

            // FocusFirst with field list
            fm.FocusFirst(new[] { "missing", "b", "a" }); // should focus "b" (first match)

            // FocusFirst with all missing — no crash
            fm.FocusFirst(new[] { "x", "y", "z" });

            // Empty manager edge cases
            var empty = new FocusManager();
            empty.FocusNext(null);     // count == 0, returns
            empty.FocusPrevious(null); // count == 0, returns

            H.Check("FM_NavComplete", true); // If we got here, no exceptions

            return Task.CompletedTask;
        }
    }

    internal class FocusManagerWithControls(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var fm = new FocusManager();

            // Mount a form with text fields that register with the FocusManager
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                fm.Clear(); // Clear on each render to re-register in order
                fm.Register("first");
                fm.Register("second");

                return VStack(
                    TextBox("", placeholderText: "First").Set(tb => fm.SetControl("first", tb)),
                    TextBox("", placeholderText: "Second").Set(tb => fm.SetControl("second", tb))
                );
            });

            await Harness.Render();

            // Now controls are registered — exercise focus with real controls
            H.Check("FMC_FieldCount", fm.Fields.Count == 2);

            fm.FocusField("first");
            await Harness.Render();
            H.Check("FMC_FocusFirst", true); // no crash

            fm.FocusNext("first");
            await Harness.Render();
            H.Check("FMC_FocusNext", true); // no crash

            fm.FocusPrevious("second");
            await Harness.Render();
            H.Check("FMC_FocusPrev", true); // no crash

            return;
        }
    }

    internal class UseFocusHookIntegration(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            FocusManager? capturedFm = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var fm = ctx.UseFocus();
                capturedFm = fm;
                fm.Clear();
                fm.Register("username");
                fm.Register("password");

                return VStack(
                    TextBox("", placeholderText: "Username").Set(tb => fm.SetControl("username", tb)),
                    TextBox("", placeholderText: "Password").Set(tb => fm.SetControl("password", tb))
                );
            });

            await Harness.Render();

            H.Check("Hook_FmCaptured", capturedFm is not null);
            H.Check("Hook_Fields", capturedFm!.Fields.Count == 2);
            H.Check("Hook_IsFirst", capturedFm.IsFirstField("username"));
            H.Check("Hook_IsLast", capturedFm.IsLastField("password"));

            // FocusAttached record
            var attached = new FocusAttached("test", capturedFm, AutoFocus: true);
            H.Check("Attached_Name", attached.FieldName == "test");
            H.Check("Attached_Manager", attached.Manager == capturedFm);
            H.Check("Attached_AutoFocus", attached.AutoFocus);

            return;
        }
    }
}
