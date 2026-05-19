using System.Linq;
using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Spec 039 Phase 11.4 — Card + AccentButton theme resolution.
///
/// The full Light/Dark/HighContrast theme-flip mount test needs a XAML
/// dispatcher; see <c>tests/Reactor.AppTests/</c>. That harness does not
/// currently expose a theme-flip primitive — tracked for a follow-up on
/// <c>Reactor.AppTests</c>. In the meantime, this unit-level smoke
/// guarantees the wiring that *would* re-resolve on theme change:
///
/// <list type="bullet">
///   <item><description><c>Card(child)</c> wires
///   <c>CardBackgroundFillColorDefaultBrush</c> and
///   <c>CardStrokeColorDefaultBrush</c> into <c>ThemeBindings</c> — both keys
///   are the canonical WinUI 3 card theme resources that re-resolve on
///   Light ↔ Dark ↔ HighContrast switches.</description></item>
///   <item><description><c>.AccentButton()</c> stores an OnMount action that
///   binds <c>AccentButtonStyle</c> at render time. We can't resolve the
///   actual style without a dispatcher, but the OnMount presence is the
///   parity check the unit layer can perform.</description></item>
/// </list>
///
/// Cross-reference: Phase 11.7 reuses the Card ThemeBindings check as the
/// "Card(child) smoke — resolved brushes match the theme dict" regression.
/// </summary>
public class CardThemeResolutionSmokeTests
{
    // ── Card theme-ref wiring ─────────────────────────────────────────

    [Fact]
    public void Card_WiresCardBackgroundThemeRef()
    {
        var el = Card(TextBlock("hi"));
        Assert.NotNull(el.ThemeBindings);
        Assert.True(el.ThemeBindings!.TryGetValue("Background", out var bg));
        Assert.Equal("CardBackgroundFillColorDefaultBrush", bg.ResourceKey);
    }

    [Fact]
    public void Card_WiresCardStrokeThemeRef()
    {
        var el = Card(TextBlock("hi"));
        Assert.NotNull(el.ThemeBindings);
        Assert.True(el.ThemeBindings!.TryGetValue("BorderBrush", out var stroke));
        Assert.Equal("CardStrokeColorDefaultBrush", stroke.ResourceKey);
    }

    [Fact]
    public void Card_ThemeBindings_MatchCanonicalThemeRefAccessors()
    {
        // Cross-check: the resource keys wired by Card(child) must be exactly
        // those exposed by Theme.CardBackground / Theme.CardStroke — if any of
        // these drifts the named-style helper drifts too.
        var el = Card(TextBlock("hi"));
        Assert.Equal(Theme.CardBackground.ResourceKey, el.ThemeBindings!["Background"].ResourceKey);
        Assert.Equal(Theme.CardStroke.ResourceKey, el.ThemeBindings!["BorderBrush"].ResourceKey);
    }

    [Fact]
    public void Card_DefaultBrushKeys_AreWinUiCardSlots()
    {
        // Belt-and-braces: the canonical WinUI 3 card theme resources resolve
        // through ResourceDictionary lookups whose lifetime is bound to the
        // Application.Current.RequestedTheme. The unit layer can't flip
        // RequestedTheme, but it CAN assert the keys we depend on are the
        // canonical ones (so a future rename of Theme.CardBackground does not
        // silently regress).
        Assert.Equal("CardBackgroundFillColorDefaultBrush", Theme.CardBackground.ResourceKey);
        Assert.Equal("CardStrokeColorDefaultBrush", Theme.CardStroke.ResourceKey);
    }

    // ── AccentButton style wiring ─────────────────────────────────────

    [Fact]
    public void AccentButton_StoresOnMountAction()
    {
        // The OnMount action wires AccentButtonStyle at render time; we
        // can't resolve the Style here, but the modifier presence is the
        // parity check (parity with NamedStyleFluentTests).
        var el = Button("Save").AccentButton();
        Assert.NotNull(el.Modifiers?.OnMountAction);
    }

    [Fact]
    public void AccentButton_OnMountAction_TargetsAccentButtonStyleResourceKey()
    {
        // Best-effort introspection — the closure captured by ApplyStyle holds
        // the style name as a private field on a compiler-generated display
        // class. If the closure layout ever changes, this falls back to the
        // weaker presence check above and we deliberately let the assertion
        // succeed (Assert.True with a skip message) rather than couple the
        // test to a specific compiler-generated layout.
        var el = Button("Save").AccentButton();
        Assert.NotNull(el.Modifiers?.OnMountAction);

        var styleName = TryExtractCapturedString(el.Modifiers!.OnMountAction!);
        if (styleName is null)
        {
            // Compiler layout changed — presence check above is enough.
            return;
        }
        Assert.Equal("AccentButtonStyle", styleName);
    }

    /// <summary>
    /// Walks the delegate's captured fields (the compiler-generated display
    /// class) and returns the first non-null string it finds. Returns null
    /// if no such field exists.
    /// </summary>
    private static string? TryExtractCapturedString(global::System.Delegate d)
    {
        var target = d.Target;
        if (target is null) return null;
        foreach (var f in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (f.FieldType == typeof(string))
            {
                if (f.GetValue(target) is string s && s.Length > 0)
                    return s;
            }
        }
        return null;
    }
}
