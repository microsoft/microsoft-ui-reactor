using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static partial class MarkdownSelectionFixtures
{
    private sealed class SelectionWarningListener : global::System.Diagnostics.Tracing.EventListener
    {
        internal bool SawMissingScope;
        internal bool SawPositiveControl;
        protected override void OnEventSourceCreated(global::System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "Microsoft-UI-Reactor")
                EnableEvents(source, global::System.Diagnostics.Tracing.EventLevel.Warning);
        }
        protected override void OnEventWritten(global::System.Diagnostics.Tracing.EventWrittenEventArgs args)
        {
            if (args.EventName != "Warning" || args.Payload is not { Count: >= 3 }) return;
            if (Equals(args.Payload[0], "Markdown") && Equals(args.Payload[1], "TextSelection"))
                SawMissingScope = true;
            if (Equals(args.Payload[0], "Markdown") && Equals(args.Payload[1], "WarningPositiveControl"))
                SawPositiveControl = true;
        }
    }

    internal sealed class EqualOptionsProbe
    {
        internal int ParentRenders;
        internal int ChildRenders;
        internal Action<int> Update = null!;
        internal MarkdownTextSelectionOptions? FirstOptions;
        internal MarkdownTextSelectionOptions? LatestOptions;
    }
    internal sealed record EqualOptionsProps(MarkdownTextSelectionOptions Options, EqualOptionsProbe Probe);
    internal sealed class EqualOptionsChild : Component<EqualOptionsProps>
    {
        public override Element Render()
        {
            Props.Probe.ChildRenders++;
            return Md("retained child", new MarkdownOptions { TextSelection = Props.Options });
        }
    }
    internal sealed class EqualOptionsParent : Component<EqualOptionsProbe>
    {
        public override Element Render()
        {
            var (revision, setRevision) = UseState(0);
            Props.Update = value => setRevision(value);
            Props.ParentRenders++;
            var options = new MarkdownTextSelectionOptions();
            Props.FirstOptions ??= options;
            Props.LatestOptions = options;
            return MarkdownTextSelection.Scope(options, VStack(
                TextBlock($"parent revision {revision}"),
                Component<EqualOptionsChild, EqualOptionsProps>(new(options, Props))));
        }
    }
    internal sealed record MembershipProps(MarkdownTextSelectionOptions Options, Action<Action<bool>> SetToggle);
    internal sealed class MembershipToggle : Component<MembershipProps>
    {
        public override Element Render()
        {
            var (show, setShow) = UseState(false);
            Props.SetToggle(value => setShow(value));
            return show
                ? Md("details", new MarkdownOptions { TextSelection = Props.Options })
                : TextBlock("not selectable");
        }
    }
    private sealed partial class ProviderProbe(Microsoft.UI.Xaml.FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
    {
        internal Microsoft.UI.Xaml.UIElement? OwnerOf(IRawElementProviderSimple provider) =>
            (PeerFromProvider(provider) as FrameworkElementAutomationPeer)?.Owner;
    }
    private static Element Md(string text, MarkdownOptions options) =>
        Microsoft.UI.Reactor.Advanced.Factories.Markdown(text, options);
    internal class SemanticMessage(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var options = new MarkdownTextSelectionOptions();
            var markdownOptions = new MarkdownOptions { TextSelection = options };
            var host = H.CreateHost();
            Element Body(bool expanded, bool inner) => VStack(
                TextBlock("outside before"),
                MarkdownTextSelection.Scope(options, VStack(
                    Md("# Title\n\nfirst\n\n- one\n- two\n\n```cs\nvar x = 1;\n```", markdownOptions),
                    MarkdownTextSelection.Surface("Summary", TextBlock("Summary")),
                    Expander("Disclosure", MarkdownTextSelection.Visibility(expanded, VStack(
                        Md("body", markdownOptions),
                        MarkdownTextSelection.Visibility(inner, Md("nested", markdownOptions))))
                    ) with { IsExpanded = expanded },
                    Md("> quote\n\n|a|b|\n|-|-|\n|c|d|", markdownOptions),
                    Button("toolbar outside text"))),
                TextBlock("outside after"));
            host.Mount(_ => Body(false, true));
            await Harness.Render();
            var selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            H.Check("MdSelection_SingleMessageHost", H.FindAllControls<MarkdownSelectionHost>(_ => true).Count == 1);
            H.Check("MdSelection_ExactVisibleSemanticText",
                selection.Document.Text == "Title\nfirst\n• one\n• two\nvar x = 1;\nSummary\nquote\na\tb\nc\td");
            host.Mount(_ => Body(true, false));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            H.Check("MdSelection_ExpandedBody_NotNested",
                selection.Document.Text.Contains("Summary\nbody\nquote", StringComparison.Ordinal)
                && !selection.Document.Text.Contains("nested", StringComparison.Ordinal));
            host.Mount(_ => Body(true, true));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            H.Check("MdSelection_NestedGatesCompose",
                selection.Document.Text.Contains("Summary\nbody\nnested\nquote", StringComparison.Ordinal));
            host.Mount(_ => Body(false, true));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            H.Check("MdSelection_CollapseRemovesDescendants",
                !selection.Document.Text.Contains("body", StringComparison.Ordinal)
                && !selection.Document.Text.Contains("nested", StringComparison.Ordinal));
            using var warning = new SelectionWarningListener();
            Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.Warning(
                Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Markdown,
                "WarningPositiveControl", "Markdown warning delivery positive control.");
            host.Mount(_ => Md("missing scope", markdownOptions));
            bool missingScopeLoaded = await Harness.WaitFor(() =>
                H.FindControl<MarkdownSelectionMarker>(marker => marker.IsLoaded) is { HasSelectionScope: false });
            H.Check("MdSelection_MissingScopeProductPath", missingScopeLoaded);
            if (!warning.SawPositiveControl && !global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
                H.Skip("MdSelection_MissingScopeWarns",
                    "NativeAOT EventSource delivery is disabled: a direct Warning positive control emitted no event. Missing-scope detection was checked; warning delivery runs in JIT lanes.");
            else
                H.Check("MdSelection_MissingScopeWarns", warning.SawPositiveControl && warning.SawMissingScope,
                    $"positiveControl={warning.SawPositiveControl}; missingScope={warning.SawMissingScope}");
        }
    }

    internal class LifecycleAndStreaming(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var options = new MarkdownTextSelectionOptions();
            var host = H.CreateHost();
            Element Message(string text) => MarkdownTextSelection.Scope(options,
                Md(text, new MarkdownOptions { TextSelection = options }));
            host.Mount(_ => Message("partial"));
            await Harness.Render();
            var selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            selection.Select(1, 7);
            host.Mount(_ => Message("partial streamed"));
            await Harness.Render();
            var updated = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            updated.Refresh();
            H.Check("MdSelection_ReconciliationKeepsHost", ReferenceEquals(selection, updated));
            H.Check("MdSelection_StreamKeepsSelection", updated.Document.SelectedText == "artial");
            var originalSurface = updated.Document.Entries.Single().Identity;
            host.Mount(_ => Message("new partial streamed"));
            await Harness.Render();
            H.Check("MdSelection_StreamRemapsAfterPrefixInsertion",
                ReferenceEquals(originalSurface, updated.Document.Entries.Single().Identity)
                && updated.Document.Anchor == 5 && updated.Document.Caret == 11
                && updated.Document.SelectedText == "artial");
            H.Check("MdSelection_NoDuplicateRegistration", updated.Document.Entries.Count == 1);
            var provider = (ITextProvider)FrameworkElementAutomationPeer.CreatePeerForElement(updated)
                .GetPattern(PatternInterface.Text);
            var oldRange = provider.DocumentRange;
            host.Mount(_ => TextBlock("replacement"));
            await Harness.Render();
            H.Check("MdSelection_UnmountInvalidatesRegistration", updated.Document.Text == "");
            H.Check("MdSelection_OldUiaRangeIsEmpty", oldRange.GetText(-1) == "");
            host.Mount(_ => Message("fresh message"));
            await Harness.Render();
            var fresh = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            fresh.Refresh();
            H.Check("MdSelection_RemountHasNoOldSelection",
                !ReferenceEquals(updated, fresh) && fresh.Document.SelectedText == ""
                && fresh.Document.Text == "fresh message");
            var innerOptions = new MarkdownTextSelectionOptions();
            host.Mount(_ => MarkdownTextSelection.Scope(options, VStack(
                Md("outer", new MarkdownOptions { TextSelection = options }),
                MarkdownTextSelection.Scope(innerOptions, Md("inner", new MarkdownOptions { TextSelection = innerOptions })))));
            await Harness.Render();
            var scopes = H.FindAllControls<MarkdownSelectionHost>(_ => true);
            foreach (var scope in scopes) scope.Refresh();
            var outer = scopes.Single(scope => scope.Document.Text == "outer");
            var inner = scopes.Single(scope => scope.Document.Text == "inner");
            var innerText = MarkdownSelectionHost.Descendants(inner).OfType<RichTextBlock>().Single();
            H.Check("MdSelection_NestedInputOwnership", inner.OwnsInput(innerText) && !outer.OwnsInput(innerText));
            inner.Select(0, 5);
            H.Check("MdSelection_NestedSelectionIndependent", outer.Document.SelectedText == "" && inner.Document.SelectedText == "inner");
            H.Check("MdSelection_HandledDescendantKeyIgnored",
                !inner.CanHandleKey(innerText, true) && inner.CanHandleKey(innerText, false)
                && !outer.CanHandleKey(innerText, false));
            H.Check("MdSelection_ScopeKeyOwned", inner.CanHandleKey(inner, true));
            inner.Select(1, 1);
            inner.HandleKey(global::Windows.System.VirtualKey.Right, true, false);
            H.Check("MdSelection_ShiftArrowExtends", inner.Document.SelectedText == "n");
            inner.HandleKey(global::Windows.System.VirtualKey.Left, false, false);
            H.Check("MdSelection_ArrowCollapses", inner.Document.Start == 1 && inner.Document.End == 1);
            inner.HandleKey(global::Windows.System.VirtualKey.End, true, false);
            H.Check("MdSelection_ShiftEnd", inner.Document.SelectedText == "nner");
            inner.HandleKey(global::Windows.System.VirtualKey.Home, false, false);
            H.Check("MdSelection_Home", inner.Document.Caret == 0 && inner.Document.SelectedText == "");
            inner.HandleKey(global::Windows.System.VirtualKey.A, false, true);
            H.Check("MdSelection_SelectAllKey", inner.Document.SelectedText == "inner");
            var copyEvents = new List<string>();
            inner.Options = new MarkdownTextSelectionOptions
            { OnCopyResult = (value, success) => copyEvents.Add($"result:{value}:{success}") };
            inner.CopySelection(value => copyEvents.Add($"write:{value}"));
            H.Check("MdSelection_HostCopyFeedback", copyEvents.SequenceEqual(["write:inner", "result:inner:True"]));
            inner.HandleKey(global::Windows.System.VirtualKey.Escape, false, false);
            H.Check("MdSelection_EscapeClears", inner.Document.SelectedText == "");

            host.Mount(_ => MarkdownTextSelection.Scope(options, VStack(
                Md("owned", new MarkdownOptions { TextSelection = options }),
                RichEditBox("editor"), Slider(5), ToggleSwitch(false), RichTextBlock("native custom"))));
            await Harness.Render();
            var keyScope = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            H.Check("MdSelection_UnregisteredControlsKeepKeys",
                !keyScope.CanHandleKey(H.FindControl<RichEditBox>(_ => true)!, false)
                && !keyScope.CanHandleKey(H.FindControl<Slider>(_ => true)!, false)
                && !keyScope.CanHandleKey(H.FindControl<ToggleSwitch>(_ => true)!, false)
                && !keyScope.CanHandleKey(H.FindAllControls<RichTextBlock>(_ => true).Last(), false));

            Action<bool> toggle = null!;
            host.Mount(_ => MarkdownTextSelection.Scope(options, VStack(
                Md("intro", new MarkdownOptions { TextSelection = options }),
                Border(Component<MembershipToggle, MembershipProps>(new(options, setter => toggle = setter)))
                    .AutomationId("MembershipAncestor"))));
            await Harness.Render();
            var membershipHost = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            toggle(true);
            await Harness.Render();
            H.Check("MdSelection_ChildMountUpdatesMembership", membershipHost.Document.Text == "intro\ndetails");
            toggle(false);
            await Harness.Render();
            H.Check("MdSelection_ChildUnmountUpdatesMembership", membershipHost.Document.Text == "intro");
            toggle(true);
            await Harness.Render();
            var ancestor = H.FindControl<Border>(border =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(border) == "MembershipAncestor")!;
            ancestor.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            await Harness.Render();
            H.Check("MdSelection_NativeCollapseUpdatesMembership", membershipHost.Document.Text == "intro");
            ancestor.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            await Harness.Render();
            H.Check("MdSelection_NativeVisibleRestoresMembership", membershipHost.Document.Text == "intro\ndetails");

            var equalOptions = new EqualOptionsProbe();
            host.Mount(_ => Component<EqualOptionsParent, EqualOptionsProbe>(equalOptions));
            await Harness.Render();
            int parentRenders = equalOptions.ParentRenders;
            int childRenders = equalOptions.ChildRenders;
            equalOptions.Update(1);
            await Harness.Render();
            var equalScope = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            H.Check("MdSelection_ValueEqualOptionsRetainSkippedChild",
                childRenders > 0 && equalOptions.ChildRenders == childRenders
                && equalOptions.ParentRenders > parentRenders
                && !ReferenceEquals(equalOptions.FirstOptions, equalOptions.LatestOptions)
                && Equals(equalOptions.FirstOptions, equalOptions.LatestOptions)
                && H.FindTextContaining("parent revision 1") is not null
                && equalScope.Document.Text == "retained child");

            host.Mount(_ => Component<Microsoft.UI.Reactor.AppTests.Host.Fixtures.MarkdownSelectionInputFixture>());
            await Harness.Render();
            outer = H.FindAllControls<MarkdownSelectionHost>(_ => true)
                .Single(scope => scope.Document.Text.Contains("native link", StringComparison.Ordinal));
            outer.Refresh();
            var richLink = MarkdownSelectionHost.Descendants(outer).OfType<RichTextBlock>()
                .Single(text => text.Blocks.OfType<Paragraph>().Any(p => p.Inlines.OfType<Hyperlink>().Any()));
            var nativeLink = richLink.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Hyperlink>().Single();
            int linkStart = outer.Document.Text.IndexOf("native link", StringComparison.Ordinal);
            void CheckLink(string name, Hyperlink expected)
            {
                var nativeProvider = (ITextProvider)FrameworkElementAutomationPeer.CreatePeerForElement(richLink)
                    .GetPattern(PatternInterface.Text);
                var glyph = nativeProvider.DocumentRange.Clone();
                glyph.MoveEndpointByUnit(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start,
                    Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, 2);
                glyph.MoveEndpointByRange(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.End,
                    glyph, Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start);
                glyph.MoveEndpointByUnit(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.End,
                    Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, 1);
                glyph.GetBoundingRectangles(out var rects);
                var origin = FrameworkElementAutomationPeer.CreatePeerForElement(outer).GetBoundingRectangle();
                double scale = outer.XamlRoot.RasterizationScale;
                var point = new Point((rects[0] + rects[2] / 2 - origin.X) / scale,
                    (rects[1] + rects[3] / 2 - origin.Y) / scale);
                int hit = outer.HitTest(point);
                Console.WriteLine($"# Link {name}: native={string.Join(",", rects)} hostPoint={point} hit={hit} linkStart={linkStart}");
                H.Check(name, ReferenceEquals(outer.LinkAt(linkStart + 2), expected)
                    && outer.LinkAtPoint(point, hit));
            }
            CheckLink("MdSelection_SetNativeHyperlinkDiscovered", nativeLink);
            var clicks = H.FindControl<TextBlock>(text =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(text) == "SelectionClicks")!;
            var clicksPeer = FrameworkElementAutomationPeer.CreatePeerForElement(clicks);
            H.Check("MdSelection_HyperlinkFeedbackStartsAtZero",
                clicks.Text == "Clicks 0" && clicksPeer.GetName() == "Clicks 0");
            var linkPeer = FrameworkElementAutomationPeer.CreatePeerForElement(richLink).GetChildren()
                .Single(child => child.GetName() == "native link");
            ((IInvokeProvider)linkPeer.GetPattern(PatternInterface.Invoke)).Invoke();
            bool clickDelivered = await Harness.WaitFor(() => H.FindText("Clicks 1") is not null, perPassMs: 20);
            H.Check("MdSelection_HyperlinkFeedbackUpdatesAutomationName",
                clickDelivered && clicksPeer.GetName() == "Clicks 1",
                $"text={clicks.Text}; automationName={clicksPeer.GetName()}");
            richLink.Blocks.Clear();
            outer.Refresh();
            H.Check("MdSelection_EmptyNativeTreeHasNoLink", outer.LinkAt(linkStart + 2) is null);
            var replacementLink = new Hyperlink();
            replacementLink.Inlines.Add(new Run { Text = "native link" });
            var replacementParagraph = new Paragraph();
            replacementParagraph.Inlines.Add(replacementLink);
            richLink.Blocks.Add(replacementParagraph);
            await Harness.Render();
            outer.Refresh();
            CheckLink("MdSelection_NativeTreeReplacementInvalidatesSegments", replacementLink);

            host.Mount(_ => Message("native reload").AutomationId("NativeReloadScope"));
            bool loaded = await Harness.WaitFor(() =>
                H.FindControl<MarkdownSelectionHost>(scope => scope.IsLoaded &&
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(scope) == "NativeReloadScope") is not null);
            await Harness.Render();
            var retained = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            H.Check("MdSelection_NativeUnloadPositiveControl",
                loaded && retained.IsLoaded && retained.Document.Text == "native reload",
                $"loaded={retained.IsLoaded}; text={retained.Document.Text}");
            H.SetContent(new Grid());
            await Harness.Render();
            H.Check("MdSelection_NativeUnloadKeepsDocumentEmptyAfterLayout",
                !retained.IsLoaded && retained.Document.Text == "");
            retained.Refresh();
            H.Check("MdSelection_NativeUnloadRefreshCannotResurrectText", retained.Document.Text == "");
            H.SetContent(retained);
            await Harness.Render();
            H.Check("MdSelection_NativeReloadRestoresDocument",
                retained.IsLoaded && retained.Document.Text == "native reload");
            host.Dispose();
        }
    }

    internal class RangeChildren(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var options = new MarkdownTextSelectionOptions();
            var host = H.CreateHost();
            host.Mount(_ => MarkdownTextSelection.Scope(options, VStack(
                Md("first [alpha](https://example.com/a) tail\n\nsecond [beta](https://example.com/b) end",
                    new MarkdownOptions { TextSelection = options, LinkBuilder = link => Button(link.Text) }),
                Button("toolbar"))));
            await Harness.Render();
            var selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            var peer = (MarkdownSelectionAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(selection);
            var document = peer.DocumentRange;
            H.Check("MdSelection_UiaDocumentText",
                document.GetText(-1) == "first alpha tail\nsecond beta end");
            var alpha = document.FindText("alpha", false, false);
            var beta = document.FindText("beta", false, false);
            H.Check("MdSelection_AlphaHasOneChild", alpha.GetChildren().Length == 1);
            H.Check("MdSelection_BetaHasOneDifferentChild",
                beta.GetChildren().Length == 1 &&
                !Equals(alpha.GetChildren()[0], beta.GetChildren()[0]));
            H.Check("MdSelection_ProseHasNoEmbeddedChild",
                document.FindText("tail", false, false).GetChildren().Length == 0);
            H.Check("MdSelection_ToolbarExcluded", document.GetChildren().Length == 2);
            var fromAlpha = peer.RangeFromChild(alpha.GetChildren()[0]);
            H.Check("MdSelection_RangeFromChildMapsExactLabel", fromAlpha.GetText(-1) == "alpha");
            H.Check("MdSelection_RangeFromChildMatchesPosition", fromAlpha.Compare(alpha));
            H.Check("MdSelection_BetaMapsExactLabel", peer.RangeFromChild(beta.GetChildren()[0]).Compare(beta));
            Element Disclosure(bool expanded) => MarkdownTextSelection.Scope(options, VStack(
                Expander("", MarkdownTextSelection.Visibility(expanded,
                    Md("disclosure body", new MarkdownOptions { TextSelection = options })),
                    isExpanded: expanded).HeaderTemplate(
                        VStack(MarkdownTextSelection.Surface("Disclosure", TextBlock("Disclosure"), ""),
                            MarkdownTextSelection.Surface(" summary",
                                TextBlock(" summary").TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap)))),
                Md("neighbor prose", new MarkdownOptions { TextSelection = options })));
            host.Mount(_ => VStack(Disclosure(false), Expander("outside scope", TextBlock("outside"))));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            peer = (MarkdownSelectionAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(selection);
            document = peer.DocumentRange;
            var summary = document.FindText("Disclosure summary", false, false);
            var expander = MarkdownSelectionHost.Descendants(selection).OfType<Expander>().Single();
            var children = summary.GetChildren();
            H.Check("MdSelection_HeaderSurfaceExposesAncestorExpander",
                children.Length == 1 && new ProviderProbe(selection).OwnerOf(children[0]) == expander);
            H.Check("MdSelection_HeaderRangeMapsExactSummary",
                children.Length == 1 && peer.RangeFromChild(children[0]).Compare(summary));
            H.Check("MdSelection_HeaderCollapsedBodyExcluded",
                document.GetText(-1) == "Disclosure summary\nneighbor prose");
            H.Check("MdSelection_HeaderHostContainmentAndDedup", document.GetChildren().Length == 1);
            host.Mount(_ => VStack(Disclosure(true), Expander("outside scope", TextBlock("outside"))));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            peer = (MarkdownSelectionAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(selection);
            document = peer.DocumentRange;
            summary = document.FindText("Disclosure summary", false, false);
            children = summary.GetChildren();
            H.Check("MdSelection_HeaderExpandedRangeUnchanged",
                children.Length == 1 && peer.RangeFromChild(children[0]).Compare(summary));
            H.Check("MdSelection_HeaderExpandedBodyHasNoAncestorChild",
                document.FindText("disclosure body", false, false).GetChildren().Length == 0);
            H.Check("MdSelection_HeaderExpandedDedup", document.GetChildren().Length == 1);
            var insertion = peer.Range(2, 2);
            bool unsupported = false;
            try { _ = insertion.GetAttributeValue(40005); }
            catch (NotSupportedException e) { unsupported = e.HResult == unchecked((int)0x80131515); }
            H.Check("MdSelection_UnsupportedAttributeSignalsNativeSentinel", unsupported);
            H.Check("MdSelection_ReadOnlyAttributeSupported", Equals(insertion.GetAttributeValue(40015), true));
            H.Check("MdSelection_UiaMoveKeepsInsertionPoint",
                insertion.Move(Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, 1) == 1 &&
                insertion.GetText(-1) == "" &&
                insertion.CompareEndpoints(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start,
                    peer.Range(3, 3), Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start) == 0);
            var first = peer.Range(0, 5);
            H.Check("MdSelection_UiaMoveBoundaryPreservesRange",
                first.Move(Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, -1) == 0 && first.GetText(-1) == "Discl");

            host.Mount(_ => MarkdownTextSelection.Scope(options,
                Md("one two three four five six seven eight nine ten", new MarkdownOptions { TextSelection = options }))
                .Width(130));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            peer = (MarkdownSelectionAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(selection);
            var wrapped = MarkdownSelectionHost.Descendants(selection).OfType<RichTextBlock>().Single();
            var nativeLines = (ITextProvider)FrameworkElementAutomationPeer.CreatePeerForElement(wrapped).GetPattern(PatternInterface.Text);
            var nativeLine = nativeLines.DocumentRange.Clone();
            nativeLine.MoveEndpointByRange(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.End,
                nativeLine, Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start);
            nativeLine.ExpandToEnclosingUnit(Microsoft.UI.Xaml.Automation.Text.TextUnit.Line);
            var line = peer.Range(0, 0);
            line.ExpandToEnclosingUnit(Microsoft.UI.Xaml.Automation.Text.TextUnit.Line);
            H.Check("MdSelection_VisualLineMatchesNative",
                line.GetText(-1).Length > 0 && line.GetText(-1).Length < selection.Document.Text.Length
                && line.GetText(-1) == nativeLine.GetText(-1));
            H.Check("MdSelection_VisualLineMove",
                line.Move(Microsoft.UI.Xaml.Automation.Text.TextUnit.Line, 1) == 1
                && line.CompareEndpoints(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start,
                    peer.Range(nativeLine.GetText(-1).Length, nativeLine.GetText(-1).Length),
                    Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start) == 0);
            var word = peer.Range(1, 1);
            word.ExpandToEnclosingUnit(Microsoft.UI.Xaml.Automation.Text.TextUnit.Word);
            H.Check("MdSelection_UiaWordBoundary", word.GetText(-1) == "one");
            word.ExpandToEnclosingUnit(Microsoft.UI.Xaml.Automation.Text.TextUnit.Paragraph);
            H.Check("MdSelection_UiaParagraphBoundary", word.GetText(-1) == selection.Document.Text);
            word.Select();
            word.RemoveFromSelection();
            H.Check("MdSelection_UiaRemoveSelection", selection.Document.SelectedText == "");
            var customBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 33, 99, 77));
            wrapped.SelectionHighlightColor = customBrush;
            H.Check("MdSelection_UsesNativeHighlightBrush", ReferenceEquals(customBrush, selection.SelectionBrush(0)));
            var caretRect = selection.CharacterBounds(1);
            var screenRect = peer.GetBoundingRectangle();
            var pointRange = peer.RangeFromPoint(new Point(screenRect.X + caretRect.X * selection.XamlRoot.RasterizationScale,
                screenRect.Y + (caretRect.Y + caretRect.Height / 2) * selection.XamlRoot.RasterizationScale));
            H.Check("MdSelection_UiaRangeFromPoint",
                pointRange.CompareEndpoints(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start,
                    peer.Range(1, 1), Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start) == 0);

            const string bidi = "abc \u05d0\u05d1\u05d2 xyz";
            host.Mount(_ => MarkdownTextSelection.Scope(options,
                Md("**abc** \u05d0\u05d1\u05d2 _xyz_", new MarkdownOptions { TextSelection = options })));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            var nativeText = MarkdownSelectionHost.Descendants(selection).OfType<RichTextBlock>().Single();
            nativeText.FlowDirection = Microsoft.UI.Xaml.FlowDirection.RightToLeft;
            await Harness.Render();
            selection.Refresh();
            var nativeProvider = (ITextProvider)FrameworkElementAutomationPeer.CreatePeerForElement(nativeText)
                .GetPattern(PatternInterface.Text);
            var origin = FrameworkElementAutomationPeer.CreatePeerForElement(selection).GetBoundingRectangle();
            double scale = selection.XamlRoot.RasterizationScale;
            bool rectanglesMatch = true;
            for (int i = 0; i < bidi.Length; i++)
            {
                if (bidi[i] == ' ') continue;
                var glyph = nativeProvider.DocumentRange.Clone();
                glyph.MoveEndpointByUnit(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start,
                    Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, i);
                glyph.MoveEndpointByRange(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.End,
                    glyph, Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start);
                glyph.MoveEndpointByUnit(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.End,
                    Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, 1);
                glyph.GetBoundingRectangles(out var nativeRects);
                var actual = selection.GlyphBounds(i, i + 1);
                Console.WriteLine($"# Bidi offset={i} text={glyph.GetText(-1)} native={string.Join(",", nativeRects)} actual={actual}; caret={selection.CharacterBounds(i)} next={selection.CharacterBounds(i + 1)}");
                rectanglesMatch &= nativeRects.Length >= 4 &&
                    Math.Abs(origin.X + actual.X * scale - nativeRects[0]) < 1 &&
                    Math.Abs(actual.Width * scale - nativeRects[2]) < 1;
            }
            H.Check("MdSelection_RtlMixedGlyphsMatchNativeTextPattern", rectanglesMatch);
        }
    }

    internal class ItemsViewClippedNavigation(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var options = new MarkdownTextSelectionOptions();
            string longMessage = string.Join("\n\n", Enumerable.Range(0, 35).Select(i => $"paragraph {i:D2}."));
            var host = H.CreateHost();
            host.Mount(_ => ItemsView(new[] { "message", "sibling" }, row => row, (row, _) =>
                ItemContainer(row == "message"
                    ? MarkdownTextSelection.Scope(options, Md(longMessage,
                        new MarkdownOptions { TextSelection = options }))
                    : TextBlock("timeline sibling excluded"))).Height(180).Width(400));
            await Harness.Render();
            var items = H.FindControl<ItemsView>(_ => true)!;
            var modern = MarkdownSelectionHost.Descendants(items).OfType<ScrollView>().First();
            var selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            H.Check("MdSelection_ItemsViewHasRealScrollView", modern.ActualHeight > 0);
            H.Check("MdSelection_ItemsViewViewportClipsMessage",
                selection.Viewport().Height > 0 && selection.Viewport().Height < selection.ActualHeight);
            H.Check("MdSelection_FullyClippedTailStillRegistered",
                selection.Document.Entries.Count == 35 && selection.Document.Text.EndsWith("paragraph 34.", StringComparison.Ordinal)
                && selection.CharacterBounds(selection.Document.Entries[^1].Start).Top > selection.Viewport().Bottom);
            var visiblePeer = (MarkdownSelectionAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(selection);
            var visible = visiblePeer.GetVisibleRanges();
            H.Check("MdSelection_UiaVisibleRangeExcludesClippedTail",
                visible.Length > 0 && visible.Any(range => range.GetText(-1).Contains("paragraph 00.", StringComparison.Ordinal))
                && visible.All(range => !range.GetText(-1).Contains("paragraph 34.", StringComparison.Ordinal)));
            H.Check("MdSelection_VisibleGlyphsSkipClippedEntries",
                selection.VisibleGlyphs(0, selection.Document.Text.Length).Any()
                && selection.VisibleGlyphs(0, selection.Document.Text.Length)
                    .All(glyph => glyph.Offset < selection.Document.Entries[^1].Start));
            _ = selection.CharacterBounds(selection.Document.Entries[^1].Start + 2);
            int cached = selection.CachedCharacterCount;
            _ = visiblePeer.DocumentRange.GetText(-1);
            H.Check("MdSelection_UiaReadPreservesGeometryCache", cached > 0 && selection.CachedCharacterCount >= cached);
            selection.Select(0, 0);
            for (int i = 0; i < 40; i++) selection.MoveVertical(1, false, true);
            H.Check("MdSelection_DownReachesClippedTail",
                selection.Document.Caret >= selection.Document.Entries[^1].Start);
            await Harness.Render();
            H.Check("MdSelection_BringIntoViewUsesInternalScrollView", modern.VerticalOffset > 0);
            modern.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
            await Harness.Render();
            selection.Refresh();
            selection.Select(0, 0);
            for (int i = 0; i < 10; i++) selection.MoveVertical(1, true, true);
            H.Check("MdSelection_PageDownReachesClippedTail",
                selection.Document.Caret >= selection.Document.Entries[^1].Start);
            H.Check("MdSelection_MessageScopeExcludesTimelineSibling",
                !selection.Document.Text.Contains("timeline", StringComparison.Ordinal));
            selection.Select(0, selection.Document.Text.Length);
            selection.BringCaretIntoView();
            await Harness.Render();
            selection.Refresh();
            var tail = (MarkdownSelectionMarker)selection.Document.Entries[^1].Identity;
            var text = MarkdownSelectionHost.Descendants(tail).OfType<RichTextBlock>().Single();
            var run = text.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Run>().Single();
            var glyph = run.ContentStart.GetPositionAtOffset(run.Text.Length - 1, LogicalDirection.Forward)
                .GetCharacterRect(LogicalDirection.Forward);
            var endForward = run.ContentEnd.GetCharacterRect(LogicalDirection.Forward);
            var endBackward = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            var mappedEnd = selection.CharacterBounds(selection.Document.Text.Length);
            Console.WriteLine($"# Terminal glyph={glyph}; end forward={endForward}; end backward={endBackward}; mapped end={mappedEnd}");
            var target = text.TransformToVisual(selection).TransformPoint(
                new Point(endForward.X - .25, glyph.Top + glyph.Height / 2));
            int offset = selection.HitTest(target);
            Console.WriteLine($"# Target={target}; hit={offset}; document end={selection.Document.Text.Length}");
            selection.Select(0, offset);
            H.Check("MdSelection_TerminalGlyphRightHalfIncludesLastCharacter",
                offset == selection.Document.Text.Length && selection.Document.SelectedText == longMessage.Replace("\n\n", "\n"));
            host.Mount(_ => MarkdownTextSelection.Scope(options, Md(
                "| Fixture selection table | State |\n| --- | --- |\n| FIXTURE SELECTION table cell. | visible |",
                new MarkdownOptions { TextSelection = options })).Width(400));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            var cell = selection.Document.Entries.Single(e => e.Text == "FIXTURE SELECTION table cell.");
            int beforePeriod = cell.End - 1;
            var periodStart = selection.CharacterBounds(beforePeriod);
            var periodEnd = selection.CharacterBounds(cell.End);
            double scale = selection.XamlRoot.RasterizationScale;
            var screenOrigin = FrameworkElementAutomationPeer.CreatePeerForElement(selection).GetBoundingRectangle();
            double rightScreen = screenOrigin.X + periodEnd.X * scale;
            double insideInteger = Math.Ceiling(rightScreen) - 1;
            var inside = new Point((insideInteger - screenOrigin.X) / scale,
                periodStart.Y + periodStart.Height / 2);
            int cellHit = selection.HitTest(inside);
            Console.WriteLine($"# Table period start={periodStart}; end={periodEnd}; scale={scale}; target={inside}; hit={cellHit}; expected={cell.End}");
            H.Check("MdSelection_TableTerminalIntegerPointIncludesPeriod", cellHit == cell.End);
            var cellMarker = (MarkdownSelectionMarker)cell.Identity;
            var cellNative = MarkdownSelectionHost.Descendants(cellMarker).OfType<RichTextBlock>().Single();
            var nativeProvider = (ITextProvider)FrameworkElementAutomationPeer.CreatePeerForElement(cellNative)
                .GetPattern(PatternInterface.Text);
            var nativeRange = nativeProvider.DocumentRange.Clone();
            nativeRange.MoveEndpointByUnit(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start,
                Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, cell.Text.Length - 1);
            nativeRange.GetBoundingRectangles(out var nativeRects);
            Console.WriteLine($"# Native period range text={nativeRange.GetText(-1)}; rectangles={string.Join(",", nativeRects)}");
            var nativeInside = new Point(
                (Math.Ceiling(nativeRects[0] + nativeRects[2]) - 1 - screenOrigin.X) / scale,
                (nativeRects[1] + nativeRects[3] / 2 - screenOrigin.Y) / scale);
            int nativeHit = selection.HitTest(nativeInside);
            Console.WriteLine($"# Native range target={nativeInside}; hit={nativeHit}; expected={cell.End}");
            H.Check("MdSelection_NativeTextRangeTerminalPointIncludesPeriod", nativeHit == cell.End);
            cellNative.FontSize = 16;
            cellNative.LineHeight = 24;
            await Harness.Render();
            selection.Refresh();
            periodStart = selection.CharacterBounds(beforePeriod);
            periodEnd = selection.CharacterBounds(cell.End);
            screenOrigin = FrameworkElementAutomationPeer.CreatePeerForElement(selection).GetBoundingRectangle();
            rightScreen = screenOrigin.X + periodEnd.X * scale;
            insideInteger = Math.Ceiling(rightScreen) - 1;
            inside = new Point((insideInteger - screenOrigin.X) / scale, periodStart.Y + periodStart.Height / 2);
            cellHit = selection.HitTest(inside);
            Console.WriteLine($"# Font16 period start={periodStart}; end={periodEnd}; target={inside}; hit={cellHit}; expected={cell.End}");
            H.Check("MdSelection_Font16TerminalIntegerPointIncludesPeriod", cellHit == cell.End);
            string tallText = string.Join(" ", Enumerable.Range(0, 100).Select(i => $"word{i:D3}"));
            host.Mount(_ => ScrollViewer(MarkdownTextSelection.Scope(options,
                Md(tallText, new MarkdownOptions { TextSelection = options })).Width(220)).Height(150));
            await Harness.Render();
            selection = H.FindControl<MarkdownSelectionHost>(_ => true)!;
            selection.Refresh();
            var scroller = H.FindControl<ScrollViewer>(_ => true)!;
            var stationaryRoot = selection.RootPoint(new Point(30, 100));
            var oldLocal = selection.LocalPoint(stationaryRoot);
            scroller.ChangeView(null, 100, null, true);
            await Harness.Render();
            var newLocal = selection.LocalPoint(stationaryRoot);
            H.Check("MdSelection_StationaryRootPointerTracksScroll",
                scroller.VerticalOffset > 0 && Math.Abs(newLocal.Y - oldLocal.Y - scroller.VerticalOffset) < 1);
            var selectionPeer = (MarkdownSelectionAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(selection);
            selectionPeer.Range(tallText.Length - 7, tallText.Length).ScrollIntoView(false);
            await Harness.Render();
            selection.Refresh();
            H.Check("MdSelection_UiaScrollTargetsRangeInsideTallSurface",
                selection.Document.Entries.Count == 1 &&
                MarkdownSelectionHost.Intersect(selection.CharacterBounds(tallText.Length - 1), selection.Viewport()).Height > 0);

            Element ReusedRow(string message) => ItemsView(new[] { message }, _ => "same-row-slot", (value, _) =>
                ItemContainer(MarkdownTextSelection.Scope(options,
                    Md(value, new MarkdownOptions { TextSelection = options })).WithKey(value))).Height(150);
            host.Mount(_ => ReusedRow("message A"));
            bool firstRealized = await Harness.WaitFor(() => H.FindControl<MarkdownSelectionHost>(
                scope => scope.IsLoaded && scope.Document.Text == "message A") is not null);
            var reusedItems = H.FindControl<ItemsView>(_ => true)!;
            var previousMessage = H.FindControl<MarkdownSelectionHost>(scope => scope.Document.Text == "message A")!;
            H.Check("MdSelection_ReusedRowInitialRealization", firstRealized);
            previousMessage.Select(0, previousMessage.Document.Text.Length);
            H.Check("MdSelection_ReusedRowInitialSelection", previousMessage.Document.SelectedText == "message A");
            host.Mount(_ => ReusedRow("message B"));
            bool nextRealized = await Harness.WaitFor(() => H.FindControl<MarkdownSelectionHost>(
                scope => scope.IsLoaded && scope.Document.Text == "message B") is not null);
            var currentMessage = H.FindControl<MarkdownSelectionHost>(scope => scope.Document.Text == "message B");
            H.Check("MdSelection_ReusedItemsViewRowResetsKeyedScope",
                nextRealized && currentMessage is not null && ReferenceEquals(reusedItems, H.FindControl<ItemsView>(_ => true))
                && !ReferenceEquals(previousMessage, currentMessage)
                && currentMessage.Document.Text == "message B" && currentMessage.Document.SelectedText == ""
                && previousMessage.Document.Text == "",
                $"realized={nextRealized}; sameItems={ReferenceEquals(reusedItems, H.FindControl<ItemsView>(_ => true))}; " +
                $"sameScope={ReferenceEquals(previousMessage, currentMessage)}; oldText={previousMessage.Document.Text}; " +
                $"newText={currentMessage?.Document.Text}; newSelection={currentMessage?.Document.SelectedText}");
        }
    }
}
