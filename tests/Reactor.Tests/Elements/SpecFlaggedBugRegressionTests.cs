using System;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Spec 039 Phase 11.7 — regression checks for the spec-flagged bugs.
///
/// Three deliberate cross-references rather than new behavior, so that a
/// future drift on any of these surfaces fails *here* (in the regression
/// suite) and not just in the broader coverage tests:
///
/// <list type="bullet">
///   <item><description>Spec §3.4 — <c>AutoSuggestBox.OnSuggestionChosen</c>
///   must be reachable via both the record-constructor (factory) path AND
///   the <c>.SuggestionChosen(...)</c> fluent.</description></item>
///   <item><description>Spec §14 #5 — <c>HyperlinkButton.NavigateUri</c>
///   fluent exists AND the <c>HyperlinkButton(Command)</c> doc-comment
///   promise (mix-and-match with <c>.NavigateUri(...)</c>) is fulfilled.
///   See <see cref="Phase4InitFluentTests"/> for the canonical value-set
///   tests; this file is the named regression checkpoint.</description></item>
///   <item><description><c>Card(child)</c> resolved brushes — see
///   <see cref="CardThemeResolutionSmokeTests"/> for the smoke; this file
///   names the cross-reference.</description></item>
/// </list>
/// </summary>
public class SpecFlaggedBugRegressionTests
{
    // ── §3.4 AutoSuggestBox.OnSuggestionChosen — factory + fluent ────────

    [Fact]
    public void AutoSuggestBox_OnSuggestionChosen_Reachable_Via_RecordConstructor()
    {
        // The factory-equivalent path is the record's positional constructor —
        // OnSuggestionChosen is the 4th positional parameter so a caller can
        // wire it without resorting to property-init syntax.
        Action<string> h = _ => { };
        var el = new AutoSuggestBoxElement(
            Text: "",
            OnTextChanged: null,
            OnQuerySubmitted: null,
            OnSuggestionChosen: h);

        Assert.Same(h, el.OnSuggestionChosen);
    }

    [Fact]
    public void AutoSuggestBox_OnSuggestionChosen_Reachable_Via_Fluent()
    {
        Action<string> h = _ => { };
        var el = AutoSuggestBox("").SuggestionChosen(h);
        Assert.Same(h, el.OnSuggestionChosen);
    }

    [Fact]
    public void AutoSuggestBox_OnSuggestionChosen_FluentOverridesConstructorArg()
    {
        // Last-write-wins on the fluent path: constructing with a handler and
        // then calling .SuggestionChosen(null) clears it.
        Action<string> h = _ => { };
        var ctorEl = new AutoSuggestBoxElement("", null, null, h);
        Assert.Null(ctorEl.SuggestionChosen(null).OnSuggestionChosen);
    }

    // ── §14 #5 HyperlinkButton.NavigateUri — cross-reference ─────────────

    [Fact]
    public void HyperlinkButton_NavigateUri_Fluent_RoundTrips()
    {
        // Phase 4.8 added .NavigateUri(uri); Phase 4.8 tests cover the value-set.
        // This is the named Phase 11.7 regression checkpoint — if the fluent is
        // ever removed or renamed, this test fails alongside the Phase 4.8 set.
        var uri = new Uri("https://example.com");
        var el = HyperlinkButton("Click").NavigateUri(uri);
        Assert.Equal(uri, el.NavigateUri);
    }

    [Fact]
    public void HyperlinkButton_Command_Plus_NavigateUri_DocPromise()
    {
        // Spec §14 #5: the HyperlinkButton(Command) doc comment promises a
        // mix-and-match `.NavigateUri(...)` chain. Verify the promise holds.
        var uri = new Uri("https://example.com");
        var cmd = new Command { Label = "Go", Execute = () => { } };
        var el = HyperlinkButton(cmd).NavigateUri(uri);
        Assert.Equal(uri, el.NavigateUri);
    }

    // ── §17.5 Card(child) — cross-reference to Phase 11.4 smoke ──────────

    [Fact]
    public void Card_Child_ResolvesCanonicalThemeBrushKeys()
    {
        // Cross-reference: full smoke lives in CardThemeResolutionSmokeTests.
        // This test ensures the regression is named in the Phase 11.7 bucket
        // so a removal of the Card factory's theme wiring fails *here* too.
        var el = Card(TextBlock("hi"));
        Assert.NotNull(el.ThemeBindings);
        Assert.Equal("CardBackgroundFillColorDefaultBrush",
            el.ThemeBindings!["Background"].ResourceKey);
        Assert.Equal("CardStrokeColorDefaultBrush",
            el.ThemeBindings!["BorderBrush"].ResourceKey);
    }
}
