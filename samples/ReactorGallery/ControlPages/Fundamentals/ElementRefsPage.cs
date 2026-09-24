using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Fundamentals;

class ElementRefsPage : Component
{
    public override Element Render()
    {
        var (focusName, setFocusName) = UseState("");
        var (inputRef, requestFocus) = this.UseElementFocus();

        var (query, setQuery) = UseState("Select all of this text");
        var queryRef = this.UseElementRef<TextBox>();

        var (showTip, setShowTip) = UseState(false);
        var tipTarget = this.UseElementRef<FrameworkElement>();

        return ScrollView(VStack(16,
            PageHeader("Element refs",
                "An ElementRef is the bridge from the declarative tree to a realized WinUI element. Attach it with .Ref(...); .Current is null until the element mounts and clears again on unmount."),

            SampleCard("Imperative focus with UseElementFocus",
                VStack(8,
                    TextBox(focusName, setFocusName, placeholderText: "Focus lands here")
                        .AutomationName("Focus target")
                        .Width(280)
                        .Ref(inputRef),
                    Button("Focus the field", requestFocus),
                    Caption("RequestFocus schedules the focus on the UI dispatcher, so it is safe to call from an effect that runs before the element has mounted.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (inputRef, requestFocus) = this.UseElementFocus();

// Focus on mount: UseEffect(() => requestFocus(), Array.Empty<object>());
VStack(8,
    TextBox(focusName, setFocusName, placeholderText: ""Focus lands here"").Ref(inputRef),
    Button(""Focus the field"", requestFocus))
"),

            SampleCard("Typed refs call methods on the control",
                VStack(8,
                    TextBox(query, setQuery)
                        .AutomationName("Query")
                        .Width(280)
                        .Ref(queryRef),
                    Button("Select all", () => queryRef.Current?.SelectAll()),
                    Caption("UseElementRef<T> types .Current as T, so there is no cast at the call site. The constraint is T : FrameworkElement.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var queryRef = this.UseElementRef<TextBox>();

VStack(8,
    TextBox(query, setQuery).Ref(queryRef),
    Button(""Select all"", () => queryRef.Current?.SelectAll()))
"),

            SampleCard("Reference props take the ref, not .Current",
                VStack(8,
                    Button("Show a tip anchored here", () => setShowTip(true)).Ref(tipTarget),
                    TeachingTip(
                        "Anchored",
                        "The tip resolves its target through the ref, so it survives late mount and recreation.",
                        target: tipTarget) with
                    {
                        IsOpen = showTip,
                        OnClosed = () => setShowTip(false),
                    },
                    Caption("Reading .Current in a handler to assign a relationship property takes a non-reactive snapshot; REACTOR_REF_001 flags it. The same holds for .LabeledBy, .DescribedBy, .FlowsTo and .XYFocus*.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var tipTarget = this.UseElementRef<FrameworkElement>();

VStack(8,
    Button(""Show a tip anchored here"", () => setShowTip(true)).Ref(tipTarget),
    TeachingTip(""Anchored"", ""Resolved through the ref."", target: tipTarget) with
    {
        IsOpen = showTip,
        OnClosed = () => setShowTip(false),
    })
")
        ).Margin(36, 24, 36, 36));
    }
}
