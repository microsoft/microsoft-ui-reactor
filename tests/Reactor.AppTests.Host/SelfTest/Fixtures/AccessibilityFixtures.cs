using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Accessibility;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selftest fixtures targeting the Accessibility folder coverage gap:
///   * <see cref="SemanticPanel"/> + <see cref="SemanticPanelAutomationPeer"/> —
///     IRangeValueProvider / IValueProvider pattern exposure, role→ControlType
///     mapping, read-only enforcement on SetValue, and the SemanticElement
///     mount + update reconciler paths.
///   * <see cref="FocusTrapHandle"/> + the <c>.FocusTrap()</c> modifier —
///     hook handle identity, IsActive lifecycle, container attach/detach, and
///     OnMount chaining.
///
/// These cover code that's hard to reach from headless unit tests because the
/// AutomationPeer and LosingFocus paths need a real WinUI element.
/// </summary>
internal static class AccessibilityFixtures
{
    // ════════════════════════════════════════════════════════════════════════
    //  SemanticPanel — automation peer surface
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Range slider semantics: peer must expose IRangeValueProvider with the
    /// configured Min/Max/Value and the conventional SmallChange/LargeChange.
    /// </summary>
    internal class SemanticPanelRangeValueProvider(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => new SemanticElement(
                TextBlock("SemRange"),
                new SemanticDescription(Role: "slider", Value: "42 of 100",
                    RangeMin: 0, RangeMax: 100, RangeValue: 42, IsReadOnly: false)));
            await Harness.Render();

            var panel = H.FindControl<SemanticPanel>(_ => true);
            H.Check("A11y_SemRange_PanelMounted", panel is not null);
            if (panel is null) return;

            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(panel);
            var range = peer.GetPattern(PatternInterface.RangeValue) as IRangeValueProvider;
            H.Check("A11y_SemRange_PeerCreated", peer is not null);
            H.Check("A11y_SemRange_RangeProviderReturned", range is not null);
            H.Check("A11y_SemRange_Min", range is not null && global::System.Math.Abs(range.Minimum - 0.0) < 1e-9);
            H.Check("A11y_SemRange_Max", range is not null && global::System.Math.Abs(range.Maximum - 100.0) < 1e-9);
            H.Check("A11y_SemRange_Value", range is not null && global::System.Math.Abs(range.Value - 42.0) < 1e-9);
            H.Check("A11y_SemRange_SmallChange", range is not null && global::System.Math.Abs(range.SmallChange - 1.0) < 1e-9);
            H.Check("A11y_SemRange_LargeChange",
                range is not null && global::System.Math.Abs(range.LargeChange - 10.0) < 1e-9);
            H.Check("A11y_SemRange_NotReadOnly", range is not null && !range.IsReadOnly);
        }
    }

    /// <summary>
    /// Pure value semantics: peer must expose IValueProvider returning the
    /// configured SemanticValue, and the explicit Value/IsReadOnly members.
    /// </summary>
    internal class SemanticPanelValueProvider(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => new SemanticElement(
                TextBlock("SemValue"),
                new SemanticDescription(Role: "custom", Value: "three of five",
                    IsReadOnly: true)));
            await Harness.Render();

            var panel = H.FindControl<SemanticPanel>(_ => true);
            H.Check("A11y_SemValue_PanelMounted", panel is not null);
            if (panel is null) return;

            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(panel);
            var value = peer.GetPattern(PatternInterface.Value) as IValueProvider;
            H.Check("A11y_SemValue_ValueProviderReturned", value is not null);
            H.Check("A11y_SemValue_Value",
                value is not null && value.Value == "three of five");
            H.Check("A11y_SemValue_IsReadOnly", value is not null && value.IsReadOnly);
        }
    }

    /// <summary>
    /// SetValue on IRangeValueProvider must respect IsReadOnly: writes when
    /// editable, no-ops when read-only.
    /// </summary>
    internal class SemanticPanelRangeSetValueRespectsReadOnly(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Editable case
            var hostE = H.CreateHost();
            hostE.Mount(ctx => new SemanticElement(
                TextBlock("RangeRW"),
                new SemanticDescription(Role: "slider",
                    RangeMin: 0, RangeMax: 10, RangeValue: 3, IsReadOnly: false)));
            await Harness.Render();
            var panelE = H.FindControl<SemanticPanel>(p => global::System.Math.Abs(p.RangeValue - 3.0) < 1e-9);
            H.Check("A11y_SemSetRange_EditableMount", panelE is not null);
            if (panelE is not null)
            {
                var rp = FrameworkElementAutomationPeer.CreatePeerForElement(panelE)
                    .GetPattern(PatternInterface.RangeValue) as IRangeValueProvider;
                rp?.SetValue(7);
                H.Check("A11y_SemSetRange_EditableAccepted", global::System.Math.Abs(panelE.RangeValue - 7.0) < 1e-9);
            }

            // Read-only case
            var hostR = H.CreateHost();
            hostR.Mount(ctx => new SemanticElement(
                TextBlock("RangeRO"),
                new SemanticDescription(Role: "progressbar",
                    RangeMin: 0, RangeMax: 10, RangeValue: 3, IsReadOnly: true)));
            await Harness.Render();
            var panelR = H.FindControl<SemanticPanel>(p => global::System.Math.Abs(p.RangeValue - 3.0) < 1e-9 && p.IsReadOnly);
            H.Check("A11y_SemSetRange_ReadOnlyMount", panelR is not null);
            if (panelR is not null)
            {
                var rp = FrameworkElementAutomationPeer.CreatePeerForElement(panelR)
                    .GetPattern(PatternInterface.RangeValue) as IRangeValueProvider;
                rp?.SetValue(9);
                H.Check("A11y_SemSetRange_ReadOnlyIgnored", global::System.Math.Abs(panelR.RangeValue - 3.0) < 1e-9);
            }
        }
    }

    /// <summary>
    /// SetValue on IValueProvider must respect IsReadOnly the same way.
    /// </summary>
    internal class SemanticPanelValueSetValueRespectsReadOnly(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var hostE = H.CreateHost();
            hostE.Mount(ctx => new SemanticElement(
                TextBlock("ValRW"),
                new SemanticDescription(Role: "custom", Value: "alpha", IsReadOnly: false)));
            await Harness.Render();
            var panelE = H.FindControl<SemanticPanel>(p => p.SemanticValue == "alpha");
            H.Check("A11y_SemSetValue_EditableMount", panelE is not null);
            if (panelE is not null)
            {
                var vp = FrameworkElementAutomationPeer.CreatePeerForElement(panelE)
                    .GetPattern(PatternInterface.Value) as IValueProvider;
                vp?.SetValue("beta");
                H.Check("A11y_SemSetValue_EditableAccepted", panelE.SemanticValue == "beta");
            }

            var hostR = H.CreateHost();
            hostR.Mount(ctx => new SemanticElement(
                TextBlock("ValRO"),
                new SemanticDescription(Role: "custom", Value: "gamma", IsReadOnly: true)));
            await Harness.Render();
            var panelR = H.FindControl<SemanticPanel>(p => p.SemanticValue == "gamma");
            H.Check("A11y_SemSetValue_ReadOnlyMount", panelR is not null);
            if (panelR is not null)
            {
                var vp = FrameworkElementAutomationPeer.CreatePeerForElement(panelR)
                    .GetPattern(PatternInterface.Value) as IValueProvider;
                vp?.SetValue("delta");
                H.Check("A11y_SemSetValue_ReadOnlyIgnored", panelR.SemanticValue == "gamma");
            }
        }
    }

    /// <summary>
    /// When RangeMin == RangeMax (no usable range), the peer must NOT report
    /// itself as an IRangeValueProvider — that's what hides the range pattern
    /// from screen readers when it doesn't apply.
    /// </summary>
    internal class SemanticPanelHidesRangePatternWhenNoRange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => new SemanticElement(
                TextBlock("NoRange"),
                new SemanticDescription(Role: "group", Value: "label only")));
            await Harness.Render();

            var panel = H.FindControl<SemanticPanel>(_ => true);
            H.Check("A11y_SemNoRange_PanelMounted", panel is not null);
            if (panel is null) return;

            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(panel);
            var range = peer.GetPattern(PatternInterface.RangeValue);
            H.Check("A11y_SemNoRange_RangeProviderAbsent", range is null);
            // Value provider should still be present because SemanticValue is set
            H.Check("A11y_SemNoRange_ValueProviderPresent",
                peer.GetPattern(PatternInterface.Value) is IValueProvider);
        }
    }

    /// <summary>
    /// When SemanticValue is null, the peer must NOT report itself as an
    /// IValueProvider.
    /// </summary>
    internal class SemanticPanelHidesValuePatternWhenNoValue(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => new SemanticElement(
                TextBlock("NoValue"),
                new SemanticDescription(Role: "slider",
                    RangeMin: 0, RangeMax: 50, RangeValue: 25)));
            await Harness.Render();

            var panel = H.FindControl<SemanticPanel>(_ => true);
            H.Check("A11y_SemNoValue_PanelMounted", panel is not null);
            if (panel is null) return;

            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(panel);
            H.Check("A11y_SemNoValue_ValueProviderAbsent",
                peer.GetPattern(PatternInterface.Value) is null);
            H.Check("A11y_SemNoValue_RangeProviderPresent",
                peer.GetPattern(PatternInterface.RangeValue) is IRangeValueProvider);
        }
    }

    /// <summary>
    /// Exercise the full role → AutomationControlType switch and the
    /// GetClassName / GetLocalizedControlType core overrides in one fixture
    /// (one assertion per role keeps diagnostics readable).
    /// </summary>
    internal class SemanticPanelControlTypeMapping(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var roles = new (string Role, AutomationControlType Expected)[]
            {
                ("slider",      AutomationControlType.Slider),
                ("progressbar", AutomationControlType.ProgressBar),
                ("progress",    AutomationControlType.ProgressBar),
                ("spinbutton",  AutomationControlType.Spinner),
                ("spinner",     AutomationControlType.Spinner),
                ("group",       AutomationControlType.Group),
                ("list",        AutomationControlType.List),
                ("listitem",    AutomationControlType.ListItem),
                ("tab",         AutomationControlType.Tab),
                ("tabitem",     AutomationControlType.TabItem),
                ("tree",        AutomationControlType.Tree),
                ("treeitem",    AutomationControlType.TreeItem),
                ("menu",        AutomationControlType.Menu),
                ("menuitem",    AutomationControlType.MenuItem),
                ("toolbar",     AutomationControlType.ToolBar),
                ("statusbar",   AutomationControlType.StatusBar),
                ("image",       AutomationControlType.Image),
                ("document",    AutomationControlType.Document),
                ("custom",      AutomationControlType.Custom),
                // Unrecognized + null both fall through to Group
                ("UnknownRole", AutomationControlType.Group),
            };

            foreach (var (role, expected) in roles)
            {
                var host = H.CreateHost();
                host.Mount(ctx => new SemanticElement(
                    TextBlock($"Role_{role}"),
                    new SemanticDescription(Role: role)));
                await Harness.Render();

                var panel = H.FindControl<SemanticPanel>(p => p.SemanticRole == role);
                if (panel is null)
                {
                    H.Check($"A11y_Role_{role}_Mounted", false);
                    continue;
                }
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(panel);
                var actual = peer.GetAutomationControlType();
                H.Check($"A11y_Role_{role}_ControlType", actual == expected);
                H.Check($"A11y_Role_{role}_ClassName", peer.GetClassName() == "SemanticPanel");
                H.Check($"A11y_Role_{role}_LocalizedType",
                    peer.GetLocalizedControlType() == role);
            }

            // Role == null path: GetLocalizedControlType falls back to "group"
            // and GetAutomationControlType falls through to Group.
            var nhost = H.CreateHost();
            nhost.Mount(ctx => new SemanticElement(
                TextBlock("Role_null"),
                new SemanticDescription(Role: null, Value: "x")));
            await Harness.Render();
            var npanel = H.FindControl<SemanticPanel>(p => p.SemanticRole is null);
            H.Check("A11y_Role_null_PanelMounted", npanel is not null);
            if (npanel is not null)
            {
                var npeer = FrameworkElementAutomationPeer.CreatePeerForElement(npanel);
                H.Check("A11y_Role_null_LocalizedFallback",
                    npeer.GetLocalizedControlType() == "group");
                H.Check("A11y_Role_null_ControlTypeFallback",
                    npeer.GetAutomationControlType() == AutomationControlType.Group);
            }
        }
    }

    /// <summary>
    /// SemanticElement update path: reconciler must propagate role/value/range/
    /// readonly changes onto the existing SemanticPanel without remounting.
    /// </summary>
    internal class SemanticElementUpdatePathRefreshesPanel(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            SemanticPanel? before = null;
            SemanticPanel? after = null;

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var s = phase == 0
                    ? new SemanticDescription(Role: "slider", Value: "v0",
                        RangeMin: 0, RangeMax: 10, RangeValue: 1, IsReadOnly: false)
                    : new SemanticDescription(Role: "progressbar", Value: "v1",
                        RangeMin: 0, RangeMax: 200, RangeValue: 150, IsReadOnly: true);

                return VStack(
                    new SemanticElement(TextBlock($"Sem:{phase}"), s),
                    Button("AdvanceSem", () => set(1)));
            });
            await Harness.Render();

            before = H.FindControl<SemanticPanel>(_ => true);
            H.Check("A11y_SemUpdate_InitialMount",
                before is not null && before.SemanticRole == "slider");

            H.ClickButton("AdvanceSem");
            await Harness.Render();

            after = H.FindControl<SemanticPanel>(_ => true);
            H.Check("A11y_SemUpdate_SamePanelInstance",
                before is not null && after is not null && ReferenceEquals(before, after));
            H.Check("A11y_SemUpdate_RoleUpdated", after?.SemanticRole == "progressbar");
            H.Check("A11y_SemUpdate_ValueUpdated", after?.SemanticValue == "v1");
            H.Check("A11y_SemUpdate_RangeMaxUpdated", after is not null && global::System.Math.Abs(after.RangeMaximum - 200.0) < 1e-9);
            H.Check("A11y_SemUpdate_RangeValueUpdated", after is not null && global::System.Math.Abs(after.RangeValue - 150.0) < 1e-9);
            H.Check("A11y_SemUpdate_IsReadOnlyUpdated", after?.IsReadOnly == true);
        }
    }

    /// <summary>
    /// The Semantics() fluent extension on Element must materialize a
    /// SemanticElement record carrying the supplied SemanticDescription.
    /// </summary>
    internal class SemanticsFluentModifierBuildsRecord(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.CompletedTask;

            var wrapped = TextBlock("inner")
                .Semantics(role: "slider", value: "n of m",
                    rangeMin: 0, rangeMax: 5, rangeValue: 3, isReadOnly: false);

            H.Check("A11y_Modifier_ReturnsSemanticElement", wrapped is SemanticElement);
            var se = (SemanticElement)wrapped;
            H.Check("A11y_Modifier_Role", se.Semantics.Role == "slider");
            H.Check("A11y_Modifier_Value", se.Semantics.Value == "n of m");
            H.Check("A11y_Modifier_RangeMin", se.Semantics.RangeMin is double rmin && global::System.Math.Abs(rmin - 0.0) < 1e-9);
            H.Check("A11y_Modifier_RangeMax", se.Semantics.RangeMax is double rmax && global::System.Math.Abs(rmax - 5.0) < 1e-9);
            H.Check("A11y_Modifier_RangeValue", se.Semantics.RangeValue is double rval && global::System.Math.Abs(rval - 3.0) < 1e-9);
            H.Check("A11y_Modifier_IsReadOnly", se.Semantics.IsReadOnly == false);
            H.Check("A11y_Modifier_ChildIsOriginal", se.Child is TextBlockElement);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  UseFocusTrap — hook + .FocusTrap() modifier
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// UseFocusTrap is built on UseState so the same FocusTrapHandle must be
    /// returned across re-renders (essential for stable LosingFocus subscription).
    /// </summary>
    internal class FocusTrapHookReturnsStableHandle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var seen = new List<FocusTrapHandle>();
            Action? bump = null;

            host.Mount(ctx =>
            {
                var trap = ctx.UseFocusTrap(isActive: true);
                seen.Add(trap);
                var (_, set) = ctx.UseState(0);
                bump = () => set(seen.Count);
                return Border(TextBlock("trapTarget")).FocusTrap(trap);
            });
            await Harness.Render();
            bump?.Invoke();
            await Harness.Render();
            bump?.Invoke();
            await Harness.Render();

            H.Check("A11y_FocusTrap_MultipleRenders", seen.Count >= 3);
            H.Check("A11y_FocusTrap_HandleStableAcrossRenders",
                seen.Count >= 2 && ReferenceEquals(seen[0], seen[^1]));
            H.Check("A11y_FocusTrap_HandleIsActiveAfterMount", seen[^1].IsActive);
        }
    }

    /// <summary>
    /// Passing isActive: false to UseFocusTrap leaves IsActive false; flipping
    /// the hook arg between renders must propagate to the existing handle.
    /// </summary>
    internal class FocusTrapHookIsActiveTracksHookArg(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            FocusTrapHandle? captured = null;
            bool active = false;
            Action? toggle = null;

            host.Mount(ctx =>
            {
                var (a, set) = ctx.UseState(false);
                active = a;
                toggle = () => set(!a);
                var trap = ctx.UseFocusTrap(isActive: a);
                captured = trap;
                return Border(TextBlock("activeTrap")).FocusTrap(trap);
            });
            await Harness.Render();
            H.Check("A11y_FocusTrap_InitiallyInactive",
                captured is not null && !captured.IsActive);

            toggle?.Invoke();
            await Harness.Render();
            H.Check("A11y_FocusTrap_FlippedActive", captured is not null && captured.IsActive);

            toggle?.Invoke();
            await Harness.Render();
            H.Check("A11y_FocusTrap_FlippedInactive",
                captured is not null && !captured.IsActive);
        }
    }

    /// <summary>
    /// .FocusTrap(handle) must wire the modifier's OnMountAction so the
    /// FocusTrapHandle ends up referencing the mounted FE.
    /// </summary>
    internal class FocusTrapModifierAttachesContainer(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            FocusTrapHandle? trap = null;

            host.Mount(ctx =>
            {
                trap = ctx.UseFocusTrap(isActive: true);
                return Border(TextBlock("container"))
                    .Set(b => b.Name = "trapContainer")
                    .FocusTrap(trap);
            });
            await Harness.Render();

            H.Check("A11y_FocusTrap_HandleCreated", trap is not null);
            H.Check("A11y_FocusTrap_ContainerAttached",
                trap?.Container is Border b && b.Name == "trapContainer");
            H.Check("A11y_FocusTrap_ContainerIsMountedFE",
                trap?.Container is FrameworkElement fe && fe.IsLoaded);
        }
    }

    /// <summary>
    /// .FocusTrap(handle) must chain onto any pre-existing OnMountAction
    /// rather than clobbering it.
    /// </summary>
    internal class FocusTrapModifierChainsExistingOnMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            int otherCount = 0;
            FocusTrapHandle? trap = null;

            host.Mount(ctx =>
            {
                trap = ctx.UseFocusTrap(isActive: true);
                return Border(TextBlock("chainTarget"))
                    .OnMount(_ => otherCount++)
                    .FocusTrap(trap);
            });
            await Harness.Render();

            H.Check("A11y_FocusTrapChain_OtherOnMountFired", otherCount == 1);
            H.Check("A11y_FocusTrapChain_ContainerSet",
                trap?.Container is not null);
        }
    }

    /// <summary>
    /// Toggling IsActive on the handle directly must safely attach / detach
    /// the LosingFocus subscription without throwing, even with a container
    /// already set (covers FocusTrapHandle.IsActive setter both branches and
    /// Attach/Detach idempotency).
    /// </summary>
    internal class FocusTrapHandleAttachDetachIdempotent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            FocusTrapHandle? trap = null;

            host.Mount(ctx =>
            {
                trap = ctx.UseFocusTrap(isActive: false);
                return Border(TextBlock("toggleTarget")).FocusTrap(trap);
            });
            await Harness.Render();

            H.Check("A11y_FocusTrapToggle_InitialContainer",
                trap?.Container is not null);

            // Flip on then off then on — Attach/Detach must be safe even
            // when chained tightly.
            trap!.IsActive = true;
            H.Check("A11y_FocusTrapToggle_TurnedOn", trap.IsActive);
            trap.IsActive = true;  // setting same value must early-out cleanly
            H.Check("A11y_FocusTrapToggle_SameValueNoOp", trap.IsActive);
            trap.IsActive = false;
            H.Check("A11y_FocusTrapToggle_TurnedOff", !trap.IsActive);
            trap.IsActive = false; // detach when already detached must be safe
            H.Check("A11y_FocusTrapToggle_SameValueNoOpOff", !trap.IsActive);
            trap.IsActive = true;
            H.Check("A11y_FocusTrapToggle_TurnedBackOn", trap.IsActive);
        }
    }

    /// <summary>
    /// Calling SetContainer with a new container must detach the previous
    /// subscription, swap in the new container, and re-attach if active.
    /// Exercises both attach branches in <see cref="FocusTrapHandle.SetContainer"/>.
    /// </summary>
    internal class FocusTrapHandleSetContainerSwaps(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            FocusTrapHandle? trap = null;
            Border? first = null;

            host.Mount(ctx =>
            {
                trap = ctx.UseFocusTrap(isActive: true);
                return Border(TextBlock("swap1"))
                    .Set(b => { first = b; })
                    .FocusTrap(trap);
            });
            await Harness.Render();

            H.Check("A11y_FocusTrapSwap_FirstAttached",
                trap?.Container is not null && ReferenceEquals(trap!.Container, first));

            // Detach by direct SetContainer to a fresh, never-mounted FE.
            var orphan = new Border();
            trap!.SetContainer(orphan);
            H.Check("A11y_FocusTrapSwap_SwappedContainer",
                ReferenceEquals(trap.Container, orphan));
            H.Check("A11y_FocusTrapSwap_StillActive", trap.IsActive);

            // Dispose() is the documented teardown — must clear container.
            trap.Dispose();
            H.Check("A11y_FocusTrapSwap_DisposedClearsContainer", trap.Container is null);
        }
    }
}
