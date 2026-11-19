using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI.ViewManagement;
using Native = Microsoft.UI.Xaml.Controls;
using Wct = CommunityToolkit.WinUI.Controls;

namespace ToolkitCompatibility;

internal static class LocalEvidenceChecks
{
    internal static async Task NameRoleAsync(CompatibilityRoot root, Scenario scenario)
    {
        await root.ShowScenarioAsync(scenario, 0, "Compatibility label A");
        var control = root.CurrentSubject;
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(control);
        if (peer is null)
            throw new NativeContractException("This native root has no automation peer; child accessibility is a separate contract.");
        var role = peer.GetAutomationControlType();
        var className = peer.GetClassName();
        if (control is Wct.RadialGauge gauge)
        {
            Program.Check(peer.GetName() == $"radial gauge. unit {gauge.Unit}, {gauge.Value}",
                "Gauge peer name does not reflect its native unit/value contract.");
            await root.ShowScenarioAsync(scenario, 1, "Compatibility label B");
            Program.Check(ReferenceEquals(control, root.CurrentSubject) &&
                peer.GetName() == $"radial gauge. unit {gauge.Unit}, {gauge.Value}" &&
                peer.GetAutomationControlType() == role,
                "Gauge native value-derived accessible name did not update.");
            Program.Results.Note($"UIA gauge role={role}; name={peer.GetName()}; pinned native peer overrides supplied Name.");
            throw new NativeContractException(
                "Pinned native RadialGauge peer overrides an explicit accessible name with unit/value text. Native-derived name updates passed; arbitrary caller names are an upstream limitation.");
        }
        Program.Check(peer.GetName() == "Compatibility label A", "Native peer did not expose the supplied accessible name.");
        var patterns = Enum.GetValues<PatternInterface>().Where(p => peer.GetPattern(p) is not null).ToArray();
        await root.ShowScenarioAsync(scenario, 0, "Compatibility label B");
        Program.Check(ReferenceEquals(control, root.CurrentSubject) && peer.GetName() == "Compatibility label B" &&
            peer.GetAutomationControlType() == role && peer.GetClassName() == className,
            "Accessible-name update changed native identity/role or retained a stale name.");
        Program.Results.Note($"UIA role={role}; class={className}; name={peer.GetName()}; patterns={string.Join(",", patterns)}");
    }

    internal static async Task PatternValuesAsync(FrameworkElement control)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(control);
        switch (control)
        {
            case Wct.RadialGauge gauge when peer?.GetPattern(PatternInterface.RangeValue) is IRangeValueProvider range:
                Program.Check(range.Value == gauge.Value && range.Minimum == gauge.Minimum && range.Maximum == gauge.Maximum,
                    "Gauge UIA range values differ from actual native values.");
                Program.Results.Note($"UIA gauge Value={range.Value}; Minimum={range.Minimum}; Maximum={range.Maximum}");
                break;
            case Wct.Segmented segmented:
                // Diagnostic parity with the standalone native probe does not clear this scenario.
                await SegmentedUiaDiagnostics.CaptureAsync(segmented, "sample-mappings",
                    Program.Results.Note, CompatibilityRoot.WaitAsync);
                // The root peer can be a named container without a Selection pattern, so selection
                // identity is read from each item's SelectionItem provider.
                var selection = peer?.GetPattern(PatternInterface.Selection) as ISelectionProvider;
                Program.Results.Note($"UIA Segmented root peer={peer?.GetClassName() ?? "(none)"}; Selection pattern={(selection is null ? "absent" : "present")}");
                Program.Check(segmented.SelectedIndex == 0 && segmented.Items.Count > 1 && selection?.CanSelectMultiple != true,
                    "Segmented UIA selection does not describe a single-selection native control.");
                try
                {
                    // Check which item UIA reports, before and after a native selection change.
                    foreach (int index in new[] { 0, 1 })
                    {
                        segmented.SelectedIndex = index;
                        await CompatibilityRoot.WaitAsync(() => segmented.SelectedIndex == index &&
                            Enumerable.Range(0, segmented.Items.Count).All(i => segmented.ContainerFromIndex(i) is UIElement),
                            $"Segmented selection {index} realized");
                        if (selection is not null)
                            Program.Check(selection.GetSelection().Length == 1, "UIA selection does not contain exactly one item.");
                        var itemPeers = SelectionItemPeers(peer);
                        int selected = 0;
                        for (int i = 0; i < segmented.Items.Count; i++)
                        {
                            var container = (UIElement)segmented.ContainerFromIndex(i);
                            string label = segmented.Items[i]?.ToString() ?? "";
                            // List selection items are usually item data peers under the parent peer,
                            // not the container element's own peer, so check both.
                            var item = FrameworkElementAutomationPeer.CreatePeerForElement(container)?
                                .GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider
                                ?? itemPeers.Where(p => p.Peer.GetName() == label).Select(p => p.Provider).SingleOrDefault()
                                ?? throw new ScenarioBlockedException(
                                    $"No UIA SelectionItem provider found for Segmented item {i} ('{label}') on its container or under the root peer " +
                                    $"(providers seen: {string.Join(", ", itemPeers.Select(p => $"{p.Peer.GetClassName()}:'{p.Peer.GetName()}'"))}). " +
                                    "This is unresolved harness coverage, not a proven native limitation.");
                            Program.Check(item.IsSelected == (i == index),
                                $"UIA SelectionItem {i} ('{segmented.Items[i]}') IsSelected={item.IsSelected}, native SelectedIndex={index}.");
                            if (item.IsSelected) selected++;
                        }
                        Program.Check(selected == 1, $"UIA reports {selected} selected Segmented items.");
                        Program.Results.Note($"UIA Segmented per-item SelectionItem checked for {segmented.Items.Count} items; providers under root={itemPeers.Count}");
                        Program.Results.Note($"UIA Segmented selection matches native SelectedItem '{segmented.SelectedItem}'");
                    }
                }
                finally { segmented.SelectedIndex = 0; }
                break;
            case Wct.SettingsExpander expander:
                var nativeExpander = CompatibilityRoot.Descendants(expander).OfType<Native.Expander>().SingleOrDefault();
                var expanderPeer = nativeExpander is null ? peer : FrameworkElementAutomationPeer.CreatePeerForElement(nativeExpander);
                if (expanderPeer?.GetPattern(PatternInterface.ExpandCollapse) is not IExpandCollapseProvider expansion)
                    throw new NativeContractException("No native expand/collapse provider is exposed by this template.");
                Program.Check(expander.IsExpanded && expansion.ExpandCollapseState == ExpandCollapseState.Expanded,
                    "Expander UIA state does not match the expanded native control.");
                break;
            case Wct.RangeSelector selector:
                var providers = (peer?.GetChildren() ?? []).Select(p => p.GetPattern(PatternInterface.RangeValue))
                    .OfType<IRangeValueProvider>().ToArray();
                if (providers.Length != 2)
                    throw new NativeContractException("Native root does not expose two range-value child providers; physical-thumb coverage remains separate.");
                Program.Check(providers.Select(p => p.Value).Order().SequenceEqual(new[] { selector.RangeStart, selector.RangeEnd }.Order()),
                    "UIA endpoint values differ from the actual native range.");
                break;
            default:
                throw new ScenarioBlockedException(
                    "No numeric, selection or expansion value contract is selected by this harness for this native root; this is unimplemented coverage, not a proven native limitation.",
                    "not-implemented");
        }
    }

    static List<(AutomationPeer Peer, ISelectionItemProvider Provider)> SelectionItemPeers(AutomationPeer? root)
    {
        var found = new List<(AutomationPeer, ISelectionItemProvider)>();
        var pending = new Queue<(AutomationPeer Peer, int Depth)>();
        foreach (var child in root?.GetChildren() ?? []) pending.Enqueue((child, 1));
        while (pending.Count > 0)
        {
            var (current, depth) = pending.Dequeue();
            if (current.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider provider)
                found.Add((current, provider));
            else if (depth < 4)
                foreach (var child in current.GetChildren() ?? []) pending.Enqueue((child, depth + 1));
        }
        return found;
    }

    internal static async Task FocusAsync(CompatibilityRoot root, Scenario scenario)
    {
        await root.ShowScenarioAsync(scenario, 0, focusSentinel: true);
        var control = root.CurrentSubject;
        var sentinel = CompatibilityRoot.Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content)
            .OfType<Native.Button>().Single(b => AutomationProperties.GetAutomationId(b) == "focus-sentinel");
        var target = CompatibilityRoot.Descendants(control).OfType<Native.Control>()
            .FirstOrDefault(c => c.IsEnabled && c.IsTabStop && c.Visibility == Visibility.Visible &&
                c.ActualWidth > 0 && c.ActualHeight > 0);
        if (target is null)
            throw new NativeContractException("This configured native subtree has no enabled tab-stop control.");
        if (!sentinel.Focus(FocusState.Programmatic))
            throw new ScenarioBlockedException("The window could not establish sentinel focus; no focus transition is claimed.", "tool");
        await CompatibilityRoot.WaitAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(sentinel.XamlRoot), sentinel),
            "focus positive control");
        if (!target.Focus(FocusState.Programmatic))
            throw new ScenarioBlockedException("The native target rejected programmatic focus in this desktop state.", "tool");
        await CompatibilityRoot.WaitAsync(() => CompatibilityRoot.Descendants(target)
            .Any(c => ReferenceEquals(c, FocusManager.GetFocusedElement(target.XamlRoot))), "focus moved into native target");
        Program.Results.Note($"programmatic focus target={target.GetType().FullName}; not keyboard navigation");
    }

    internal static async Task ThemesAsync(FrameworkElement control, string name)
    {
        var original = control.RequestedTheme;
        try
        {
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                control.RequestedTheme = theme;
                await CompatibilityRoot.WaitAsync(() => control.ActualTheme == theme, $"native {theme} theme");
                await CaptureAsync(control, $"{name}-{theme}");
            }
        }
        finally { control.RequestedTheme = original; }
    }

    internal static async Task RtlAsync(FrameworkElement control, string name)
    {
        var original = control.FlowDirection;
        try
        {
            control.FlowDirection = FlowDirection.LeftToRight;
            control.UpdateLayout();
            await CaptureAsync(control, $"{name}-LTR");
            var inheritedText = CompatibilityRoot.Descendants(control).OfType<Native.TextBlock>()
                .Where(t => Reconciler.GetElementTag(t) is TextBlockElement &&
                    t.ReadLocalValue(FrameworkElement.FlowDirectionProperty) == DependencyProperty.UnsetValue).ToArray();
            control.FlowDirection = FlowDirection.RightToLeft;
            control.UpdateLayout();
            Program.Check(control.FlowDirection == FlowDirection.RightToLeft &&
                inheritedText.All(t => t.FlowDirection == FlowDirection.RightToLeft),
                "RTL did not propagate to declaratively authored text.");
            await CaptureAsync(control, $"{name}-RTL");
            Program.Results.Note($"RTL inherited authored text count={inheritedText.Length}; captures require visual review.");
        }
        finally { control.FlowDirection = original; }
    }

    internal static async Task HighContrastAsync(FrameworkElement control, string name)
    {
        bool active = new AccessibilitySettings().HighContrast;
        Program.Results.Note($"OS HighContrast={active}");
        if (!active)
            throw new ScenarioBlockedException("OS high contrast is off; changing the shared desktop setting is not authorized.", "approval");
        await CaptureAsync(control, $"{name}-HighContrast");
    }

    internal static Task TextScalingAsync()
    {
        double scale = new UISettings().TextScaleFactor;
        Program.Results.Note($"OS TextScaleFactor={scale}");
        throw new ScenarioBlockedException(
            $"Observed OS text scale {scale}; alternate global settings were not authorized or exercised.", "approval");
    }

    private static async Task CaptureAsync(FrameworkElement control, string name)
    {
        Program.Check(CompatibilityRoot.IsAttached(control) && control.ActualWidth > 0 && control.ActualHeight > 0,
            "Capture target is not attached with nonzero layout.");
        await Task.Delay(100);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(control).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        Program.Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0 &&
            pixels.Length == bitmap.PixelWidth * bitmap.PixelHeight * 4, "Incomplete native pixel capture.");
        int visible = 0;
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) visible++;
        if (visible == 0)
            throw new ScenarioBlockedException("Native capture is entirely transparent; appearance cannot be validated from this capture.", "tool");
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        output.Seek(0);
        string path = Path.Combine(Path.GetDirectoryName(Program.Options.Output)!, name + ".png");
        using (var file = File.Create(path))
            await output.AsStreamForRead().CopyToAsync(file);
        Program.Results.Note($"capture={path}; pixels={bitmap.PixelWidth}x{bitmap.PixelHeight}; nontransparent={visible}; pixel-sha256={Convert.ToHexString(SHA256.HashData(pixels))}");
    }
}
