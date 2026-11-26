using Microsoft.UI.Reactor.AppTests.Host.SelfTest.ModifierMatrix;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 067 §10.2: every row A–D modifier against a frozen expected matrix
/// (<see cref="ModifierMatrixExpectations"/>), written against the hand-written
/// <c>ApplyModifiers</c> so the later layers that generate those arms have a characterization
/// test to pass. For each (slot, receiver) the fixture mounts a real carrier control and checks:
/// set, change, unset releases the local value, a style-provided value shows again after unset, what
/// <c>CleanElement</c> leaves on the carrier in the pool, and that a bare renter of the same instance
/// does not get the previous value. <c>ModifierMatrix_Negatives</c> checks gated slots on
/// a control that declares the DP but is not a receiver.
/// </summary>
/// <remarks>
/// The typed probes come from the schema (<c>ModifierProbes.Generated.cs</c>), the expectations do
/// not. A wrong target in the schema would be written and read back from the same wrong DP, so each
/// case first checks the probe's DP is the matrix's literal one.
/// </remarks>
internal static class ModifierMatrixFixtures
{
    internal sealed class Execution(Harness h, string group) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cases = ModifierMatrixExpectations.Cases.Where(c => c.Group == group).ToList();
            H.Check($"ModifierMatrix_{group}_HasCases", cases.Count > 0);
            foreach (var c in cases)
                await RunCaseAsync(c);
        }

        private async Task RunCaseAsync(MatrixCase c)
        {
            var id = $"ModifierMatrix_{c.Slot}_{c.Carrier}";
            var probe = ModifierProbes.All.SingleOrDefault(p => p.Slot == c.Slot && p.ReceiverType.Name == c.Receiver);
            H.Check(id + "_ProbeExists", probe is not null);
            if (probe is null) return;
            H.Check(id + "_ProbeDpIsTheMatrixDp", probe.DpName == c.Dp, $"probe {probe.DpName}, matrix {c.Dp}");
            if (probe.DpName != c.Dp) return;
            H.Check(id + "_CarrierIsAReceiver", probe.ReceiverType.IsAssignableFrom(CarrierType(c.Carrier)));

            var dp = probe.Dp();
            var (v1, v2, styled) = SampleValues(c.Slot);
            var style = new Style(CarrierType(c.Carrier));
            style.Setters.Add(new Setter(dp, ToDp(probe, styled)));

            FrameworkElement? mounted = null;
            Action<int>? setPhase = null;
            var styledBase = new ElementModifiers { OnMountAction = fe => { mounted = fe; fe.Style = style; } };
            var bareBase = new ElementModifiers { OnMountAction = fe => mounted = fe };

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                setPhase = set;
                Element? carrier = phase switch
                {
                    0 => Carrier(c.Carrier) with { Modifiers = probe.With(styledBase, v1) },
                    1 => Carrier(c.Carrier) with { Modifiers = probe.With(styledBase, v2) },
                    2 => Carrier(c.Carrier) with { Modifiers = styledBase },
                    3 => Carrier(c.Carrier) with { Modifiers = probe.With(styledBase, v1) },
                    4 => null,
                    _ => Carrier(c.Carrier) with { Modifiers = bareBase },
                };
                return VStack(carrier);
            });

            try
            {
                await Harness.Render();
                var first = mounted;
                H.Check(id + "_Mounted", first is not null && CarrierType(c.Carrier).IsInstanceOfType(first));
                if (first is null) return;

                // 1. set → the control holds the value as a local value.
                H.Check(id + "_Set", Same(first.ReadLocalValue(dp), ToDp(probe, v1)), Describe(first.ReadLocalValue(dp)));

                // 2. change → the new value.
                setPhase!(1);
                await Harness.Render();
                H.Check(id + "_Change", Same(first.ReadLocalValue(dp), ToDp(probe, v2)), Describe(first.ReadLocalValue(dp)));

                // 3–4. unset → the local value is released (#952), and the Style's value shows again.
                setPhase!(2);
                await Harness.Render();
                H.Check(id + "_UnsetClearsLocalValue", first.ReadLocalValue(dp) == DependencyProperty.UnsetValue, Describe(first.ReadLocalValue(dp)));
                H.Check(id + "_UnsetRestoresStyle", Same(first.GetValue(dp), ToDp(probe, styled)), Describe(first.GetValue(dp)));

                // 5. set again and return the carrier to the pool. While it sits there, nothing but
                //    CleanElement has touched it, so this is the pool reset itself.
                setPhase!(3);
                await Harness.Render();
                setPhase!(4);
                await Harness.Render();
                if (c.Pool != MatrixPool.NotPoolable)
                {
                    var inPool = first.ReadLocalValue(dp);
                    var pooled = c.Pool switch
                    {
                        MatrixPool.ClearValue => inPool == DependencyProperty.UnsetValue,
                        MatrixPool.WriteDefault => Same(inPool, PoolDefault(c)),
                        MatrixPool.NotReset => Same(inPool, ToDp(probe, v1)),
                        _ => false,
                    };
                    H.Check(id + "_Pool" + c.Pool, pooled, Describe(inPool));
                }

                // 6. mount a bare carrier. A poolable one rents the same instance; its own mount may
                //    write the DP (Button's IsEnabled, a TextBlock's caption-derived name), so this
                //    checks only that the previous renter's value did not reach the new one, except
                //    where the matrix pins the leak.
                setPhase!(5);
                await Harness.Render();
                var second = mounted;
                // Load-bearing: without instance reuse the rent check passes trivially.
                H.Check(id + (c.Pool == MatrixPool.NotPoolable ? "_NotPooled" : "_PoolReusedInstance"),
                    ReferenceEquals(first, second) == (c.Pool != MatrixPool.NotPoolable));
                if (second is null || c.Pool == MatrixPool.NotPoolable) return;
                var rented = second.ReadLocalValue(dp);
                H.Check(id + (c.Pool == MatrixPool.NotReset ? "_RentKeepsPreviousValue" : "_RentDropsPreviousValue"),
                    Same(rented, ToDp(probe, v1)) == (c.Pool == MatrixPool.NotReset), Describe(rented));
            }
            finally
            {
                host.Dispose();
            }
        }
    }

    internal sealed class Negatives(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Every gated slot having a negative or a reason is checked headlessly
            // (ModifierMatrixSchemaAgreementTests).
            foreach (var n in ModifierMatrixExpectations.Negatives)
            {
                var id = $"ModifierMatrix_{n.Slot}_NotWrittenTo{n.Carrier}";
                var probe = ModifierProbes.All.First(p => p.Slot == n.Slot);
                var dp = NegativeDp(n.Dp);
                H.Check(id + "_CarrierIsNotAReceiver",
                    !ModifierProbes.All.Any(p => p.Slot == n.Slot && p.ReceiverType.IsAssignableFrom(CarrierType(n.Carrier))));

                FrameworkElement? mounted = null;
                var mods = probe.With(new ElementModifiers { OnMountAction = fe => mounted = fe }, SampleValues(n.Slot).V1);
                var host = H.CreateHost();
                host.Mount(_ => VStack(Carrier(n.Carrier) with { Modifiers = mods }));
                try
                {
                    await Harness.Render();
                    H.Check(id + "_Mounted", mounted is not null && CarrierType(n.Carrier).IsInstanceOfType(mounted));
                    H.Check(id + "_NoLocalValue", mounted?.ReadLocalValue(dp) == DependencyProperty.UnsetValue,
                        Describe(mounted?.ReadLocalValue(dp)));
                }
                finally
                {
                    host.Dispose();
                }
            }
        }

        private static DependencyProperty NegativeDp(string name) => name switch
        {
            "RichTextBlock.FontFamilyProperty" => WinUI.RichTextBlock.FontFamilyProperty,
            "RichTextBlock.FontSizeProperty" => WinUI.RichTextBlock.FontSizeProperty,
            "RichTextBlock.FontWeightProperty" => WinUI.RichTextBlock.FontWeightProperty,
            "UIElement.TabIndexProperty" => UIElement.TabIndexProperty,
            _ => throw new InvalidOperationException($"No DP mapped for {name}."),
        };
    }

    // ── Carriers ────────────────────────────────────────────────────────

    private static Element Carrier(string name) => name switch
    {
        "TextBlock" => TextBlock("matrix-carrier"),
        "Button" => Button("matrix-carrier", () => { }),
        "Border" => Border(null),
        "Grid" => Grid([], []),
        "StackPanel" => VStack(),
        "RelativePanel" => RelativePanel(),
        "RichTextBlock" => RichTextBlock("matrix-carrier"),
        _ => throw new InvalidOperationException($"No carrier named {name}."),
    };

    private static Type CarrierType(string name) => name switch
    {
        "TextBlock" => typeof(WinUI.TextBlock),
        "Button" => typeof(WinUI.Button),
        "Border" => typeof(WinUI.Border),
        "Grid" => typeof(WinUI.Grid),
        "StackPanel" => typeof(WinUI.StackPanel),
        "RelativePanel" => typeof(WinUI.RelativePanel),
        "RichTextBlock" => typeof(WinUI.RichTextBlock),
        _ => throw new InvalidOperationException($"No carrier named {name}."),
    };

    private static object PoolDefault(MatrixCase c) => c switch
    {
        { Slot: "FontSize", Carrier: "TextBlock" } => 14.0,
        _ => throw new InvalidOperationException($"No pool default recorded for {c.Slot} on {c.Carrier}."),
    };

    // ── Values ──────────────────────────────────────────────────────────

    /// <summary>
    /// Two modifier values and a style value per slot. The style value differs from the second
    /// modifier value and from the DP default, so "the Style shows again" is distinguishable from
    /// both "the modifier value stuck" and "the default was written".
    /// </summary>
    private static (object V1, object V2, object Style) SampleValues(string slot) => slot switch
    {
        "RequestedTheme" => (ElementTheme.Dark, ElementTheme.Default, ElementTheme.Light),
        "Width" => (120.0, 130.0, 150.0),
        "Height" => (44.0, 46.0, 55.0),
        "MinWidth" => (80.0, 82.0, 90.0),
        "MinHeight" => (30.0, 32.0, 35.0),
        "MaxWidth" => (240.0, 242.0, 260.0),
        "MaxHeight" => (90.0, 92.0, 95.0),
        "HorizontalAlignment" => (HorizontalAlignment.Right, HorizontalAlignment.Left, HorizontalAlignment.Center),
        "VerticalAlignment" => (VerticalAlignment.Bottom, VerticalAlignment.Top, VerticalAlignment.Center),
        "HorizontalContentAlignment" => (HorizontalAlignment.Right, HorizontalAlignment.Left, HorizontalAlignment.Center),
        "VerticalContentAlignment" => (VerticalAlignment.Bottom, VerticalAlignment.Top, VerticalAlignment.Center),
        "IsVisible" => (false, true, false),
        "ToolTipPlacement" => (PlacementMode.Bottom, PlacementMode.Left, PlacementMode.Right),
        "IsEnabled" => (false, true, false),
        "CornerRadius" => (new CornerRadius(3), new CornerRadius(4), new CornerRadius(9)),
        "AutomationName" => ("matrix-name-1", "matrix-name-2", "matrix-name-style"),
        "AutomationId" => ("matrix-id-1", "matrix-id-2", "matrix-id-style"),
        "IsDragRegion" => (true, false, true),
        "ElementSoundMode" => (ElementSoundMode.Off, ElementSoundMode.FocusOnly, ElementSoundMode.Off),
        "HeadingLevel" => (AutomationHeadingLevel.Level2, AutomationHeadingLevel.Level3, AutomationHeadingLevel.Level4),
        "IsTabStop" => (true, false, true),
        "IsHitTestVisible" => (false, true, false),
        "TabIndex" => (3, 4, 7),
        "AccessKey" => ("C", "D", "S"),
        "XYFocusKeyboardNavigation" => (XYFocusKeyboardNavigationMode.Enabled, XYFocusKeyboardNavigationMode.Disabled, XYFocusKeyboardNavigationMode.Enabled),
        "HelpText" => ("help-1", "help-2", "help-style"),
        "FullDescription" => ("full-1", "full-2", "full-style"),
        "LandmarkType" => (AutomationLandmarkType.Main, AutomationLandmarkType.Navigation, AutomationLandmarkType.Search),
        "AccessibilityView" => (AccessibilityView.Control, AccessibilityView.Content, AccessibilityView.Raw),
        "IsRequiredForForm" => (true, false, true),
        "LiveSetting" => (AutomationLiveSetting.Polite, AutomationLiveSetting.Assertive, AutomationLiveSetting.Polite),
        "PositionInSet" => (2, 3, 5),
        "SizeOfSet" => (4, 5, 8),
        "Level" => (2, 3, 6),
        "ItemStatus" => ("status-1", "status-2", "status-style"),
        "TabFocusNavigation" => (KeyboardNavigationMode.Cycle, KeyboardNavigationMode.Once, KeyboardNavigationMode.Cycle),
        "FontFamily" => (new FontFamily("Consolas"), new FontFamily("Courier New"), new FontFamily("Georgia")),
        "FontSize" => (20.0, 22.0, 17.0),
        "FontWeight" => (new global::Windows.UI.Text.FontWeight(700), new global::Windows.UI.Text.FontWeight(600), new global::Windows.UI.Text.FontWeight(300)),
        _ => throw new InvalidOperationException($"No sample values for {slot}. Add them to SampleValues."),
    };

    // Row C: the modifier's value as the DP stores it.
    private static object ToDp(ModifierProbe probe, object value) => probe.Converter switch
    {
        null => value,
        "ToVisibility" => (bool)value ? Visibility.Visible : Visibility.Collapsed,
        _ => throw new InvalidOperationException($"Unknown converter {probe.Converter}."),
    };

    private static bool Same(object? actual, object expected) => expected switch
    {
        FontFamily family => actual is FontFamily a && a.Source == family.Source,
        _ => Equals(actual, expected),
    };

    private static string Describe(object? value) => value switch
    {
        null => "null",
        _ when value == DependencyProperty.UnsetValue => "UnsetValue",
        FontFamily family => "FontFamily(" + family.Source + ")",
        _ => value.ToString() ?? "?",
    };
}
