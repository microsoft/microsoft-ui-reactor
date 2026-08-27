using System.Numerics;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Navigation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Pins <see cref="TransitionEngine"/>'s motion constants to the values WinUI actually uses,
/// with a citation for every one.
///
/// <para>
/// Reactor renders navigation transitions on the Composition layer rather than through a WinUI
/// <c>Frame</c> — it has no <c>Frame</c> to hand a <c>NavigationTransitionInfo</c> to, it needs
/// both pages alive at once, and it needs a completion callback to sequence caching and the
/// <c>onNavigatedTo</c>/<c>onNavigatedFrom</c> lifecycle (spec 011, Appendix C). The cost of that
/// choice is that WinUI's motion values are *copied* here rather than consumed, so they can drift.
/// </para>
///
/// <para>
/// Every constant below was read out of <c>microsoft/microsoft-ui-xaml</c>. The WinUI-side value
/// is written as a literal next to the Reactor constant it backs so a reviewer can check both
/// against the cited source without running anything:
/// </para>
///
/// <list type="bullet">
/// <item><description>
/// <c>dxaml/phone/lib/ThemeTransitions.cpp</c> —
/// <c>EntranceNavigationTransitionInfo::CreateStoryboards</c>,
/// <c>SlideNavigationTransitionInfo::CreateStoryboards</c>,
/// <c>DrillInNavigationTransitionInfo::CreateStoryboards</c>
/// </description></item>
/// <item><description>
/// <c>dxaml/phone/lib/ThemeTransitions.h</c> — <c>DrillInNavigationTransitionInfo::s_*Duration</c>
/// </description></item>
/// <item><description>
/// <c>dxaml/phone/lib/NavigateTransitionHelper.h</c> — <c>SLIDE_*</c>
/// </description></item>
/// </list>
///
/// <para>
/// These assertions catch <b>Reactor-side</b> drift: editing a constant here without updating the
/// recorded WinUI value fails. They cannot catch <b>WinUI-side</b> drift on their own — nothing in
/// this process can read WinUI's C++ sources. That is what
/// <see cref="Verified_Against_The_Windows_App_Sdk_The_Repo_Actually_Builds_Against"/> is for: it
/// couples this file to the pinned Windows App SDK version, so bumping the SDK reddens the suite
/// and forces someone to re-read the sources above before re-pinning.
/// </para>
/// </summary>
public class WinUiNavigationMotionParityTests
{
    /// <summary>
    /// The Windows App SDK release whose WinUI sources the constants below were read from.
    /// Bumping <c>WindowsAppSDKVersion</c> without re-verifying is exactly the drift this
    /// suite exists to catch — see the guard test at the bottom of this file.
    /// </summary>
    private const string VerifiedAgainstWindowsAppSdkVersion = "2.1.3";

    // ════════════════════════════════════════════════════════════════
    //  Entrance — ThemeTransitions.cpp,
    //  EntranceNavigationTransitionInfo::CreateStoryboards
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Entrance_Matches_WinUI()
    {
        // const DOUBLE translationOffset = 140;
        Assert.Equal(140f, TransitionEngine.EntranceTranslationOffset);

        // const UINT64 outDuration = 150;
        Assert.Equal(TimeSpan.FromMilliseconds(150), TransitionEngine.EntranceExitDuration);

        // const UINT64 inDuration = 300;
        Assert.Equal(TimeSpan.FromMilliseconds(300), TransitionEngine.EntranceDuration);

        // const wf::Point inControlPoint1  = { 0.1f, 0.9f };
        // const wf::Point inControlPoint2  = { 0.2f, 1.0f };
        Assert.Equal(new Vector2(0.1f, 0.9f), TransitionEngine.EntranceInEasingControlPoint1);
        Assert.Equal(new Vector2(0.2f, 1.0f), TransitionEngine.EntranceInEasingControlPoint2);

        // const wf::Point outControlPoint1 = { 0.7f, 0.0f };
        // const wf::Point outControlPoint2 = { 1.0f, .5f  };
        Assert.Equal(new Vector2(0.7f, 0.0f), TransitionEngine.EntranceOutEasingControlPoint1);
        Assert.Equal(new Vector2(1.0f, 0.5f), TransitionEngine.EntranceOutEasingControlPoint2);
    }

    // ════════════════════════════════════════════════════════════════
    //  Horizontal slide — ThemeTransitions.cpp,
    //  SlideNavigationTransitionInfo::CreateStoryboards (FromLeft / FromRight branch)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void HorizontalSlide_Matches_WinUI()
    {
        // const DOUBLE translationExitOffset = 150;
        Assert.Equal(150f, TransitionEngine.HorizontalSlideExitOffset);

        // const DOUBLE translationEntranceOffset = -200;
        // Reactor stores the magnitude and applies the sign via its direction factor, mirroring
        // WinUI's `reverseTranslationFactor = FromLeft ? 1 : -1`.
        Assert.Equal(200f, TransitionEngine.HorizontalSlideEntranceOffset);

        // const UINT64 outDuration = 150;  const UINT64 inDuration = 300;
        Assert.Equal(TimeSpan.FromMilliseconds(150), TransitionEngine.HorizontalSlideExitDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(300), TransitionEngine.HorizontalSlideEntranceDuration);

        // Same four control points as Entrance, per the same source.
        Assert.Equal(new Vector2(0.1f, 0.9f), TransitionEngine.SlideInEasingControlPoint1);
        Assert.Equal(new Vector2(0.2f, 1.0f), TransitionEngine.SlideInEasingControlPoint2);
        Assert.Equal(new Vector2(0.7f, 0.0f), TransitionEngine.SlideOutEasingControlPoint1);
        Assert.Equal(new Vector2(1.0f, 0.5f), TransitionEngine.SlideOutEasingControlPoint2);
    }

    // ════════════════════════════════════════════════════════════════
    //  Vertical slide — NavigateTransitionHelper.h, SLIDE_*
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void VerticalSlide_Matches_WinUI()
    {
        // static const INT SLIDE_OFFSET_IN = 200;  (SLIDE_OFFSET_OUT is also 200)
        Assert.Equal(200f, TransitionEngine.VerticalSlideOffset);

        // static const INT SLIDE_EASE = 6;  — the exponent passed to the exponential ease.
        Assert.Equal(6f, TransitionEngine.VerticalSlideExponent);

        // static const INT64 SLIDE_MID_TIME = WARP_FACTOR * 250;
        Assert.Equal(TimeSpan.FromMilliseconds(250), TransitionEngine.VerticalSlideHandoffTime);

        // static const INT64 SLIDE_END_TIME = SLIDE_MID_TIME + WARP_FACTOR * 350;  → 250 + 350
        Assert.Equal(TimeSpan.FromMilliseconds(600), TransitionEngine.VerticalSlideDuration);
    }

    // ════════════════════════════════════════════════════════════════
    //  Drill-in — ThemeTransitions.h (durations) + ThemeTransitions.cpp (scales, curves)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DrillIn_Durations_Match_WinUI()
    {
        // s_NavigatingAwayScaleDuration = 100;  s_NavigatingAwayOpacityDuration = 100;
        // (s_BackNavigatingAway* are also 100, and Reactor shares one pair of constants.)
        Assert.Equal(TimeSpan.FromMilliseconds(100), TransitionEngine.DrillInOutScaleDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(100), TransitionEngine.DrillInOutOpacityDuration);

        // s_NavigatingToScaleDuration = 783;  s_NavigatingToOpacityDuration = 333;
        Assert.Equal(TimeSpan.FromMilliseconds(783), TransitionEngine.DrillInForwardInScaleDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(333), TransitionEngine.DrillInForwardInOpacityDuration);

        // s_BackNavigatingToScaleDuration = 333;  s_BackNavigatingToOpacityDuration = 333;
        Assert.Equal(TimeSpan.FromMilliseconds(333), TransitionEngine.DrillInBackInScaleDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(333), TransitionEngine.DrillInBackInOpacityDuration);
    }

    [Fact]
    public void DrillIn_Scales_Match_WinUI()
    {
        // NavigatingAway:     const DOUBLE scaleFactor = 1.04;
        // NavigatingTo:       const DOUBLE scaleFactor = 0.94;
        // BackNavigatingAway: const DOUBLE scaleFactor = 0.96;
        // BackNavigatingTo:   const DOUBLE scaleFactor = 1.06;
        Assert.Equal(1.04f, TransitionEngine.DrillInForwardOutScale);
        Assert.Equal(0.94f, TransitionEngine.DrillInForwardInScale);
        Assert.Equal(0.96f, TransitionEngine.DrillInBackOutScale);
        Assert.Equal(1.06f, TransitionEngine.DrillInBackInScale);
    }

    [Fact]
    public void DrillIn_Easing_Curves_Match_WinUI()
    {
        // Every branch except BackNavigatingTo:
        //   scaleCurveControlPoint1 = { 0.1f, 0.9f };  scaleCurveControlPoint2 = { 0.2f, 1.0f };
        Assert.Equal(new Vector2(0.1f, 0.9f), TransitionEngine.DrillInScaleEasingControlPoint1);
        Assert.Equal(new Vector2(0.2f, 1.0f), TransitionEngine.DrillInScaleEasingControlPoint2);

        // BackNavigatingTo:
        //   scaleCurveControlPoint1 = { 0.12f, 0.0f }; scaleCurveControlPoint2 = { 0.0f, 1.0f };
        Assert.Equal(new Vector2(0.12f, 0.0f), TransitionEngine.DrillInBackScaleEasingControlPoint1);
        Assert.Equal(new Vector2(0.0f, 1.0f), TransitionEngine.DrillInBackScaleEasingControlPoint2);

        // All four branches:
        //   opacityCurveControlPoint1 = { 0.17f, 0.17f }; opacityCurveControlPoint2 = { 0.0f, 1.0f };
        Assert.Equal(new Vector2(0.17f, 0.17f), TransitionEngine.DrillInOpacityEasingControlPoint1);
        Assert.Equal(new Vector2(0.0f, 1.0f), TransitionEngine.DrillInOpacityEasingControlPoint2);
    }

    // ════════════════════════════════════════════════════════════════
    //  The drift guard
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// The assertions above are copies, not observations — they cannot notice WinUI retuning a
    /// value. This test is the trigger that forces a human to look: it fails when the repo starts
    /// building against a different Windows App SDK than the one whose sources were read.
    /// </summary>
    [Fact]
    public void Verified_Against_The_Windows_App_Sdk_The_Repo_Actually_Builds_Against()
    {
        var root = global::Microsoft.UI.Reactor.Cli.Pack.RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);

        var propsPath = global::System.IO.Path.Combine(root!, "Directory.Build.props");
        Assert.True(global::System.IO.File.Exists(propsPath), $"Missing {propsPath}");

        var props = global::System.IO.File.ReadAllText(propsPath);
        var match = Regex.Match(props, @"<WindowsAppSDKVersion>\s*([^<\s]+)\s*</WindowsAppSDKVersion>");

        Assert.True(
            match.Success,
            "Could not read <WindowsAppSDKVersion> from Directory.Build.props. If the property was "
            + "renamed, update this guard — it is the only thing tying Reactor's copied WinUI motion "
            + "constants to the SDK they were copied from.");

        var actual = match.Groups[1].Value;

        Assert.True(
            actual == VerifiedAgainstWindowsAppSdkVersion,
            $"""
            Windows App SDK moved from {VerifiedAgainstWindowsAppSdkVersion} to {actual}, but
            TransitionEngine's WinUI motion constants are still pinned to the values read from
            {VerifiedAgainstWindowsAppSdkVersion}.

            Reactor copies these values rather than consuming WinUI's NavigationTransitionInfo
            (it renders transitions on the Composition layer — spec 011, Appendix C), so an SDK
            bump can silently desynchronise Reactor's navigation motion from the platform's.

            Re-read the sources in microsoft/microsoft-ui-xaml at the tag for {actual}:
              dxaml/phone/lib/ThemeTransitions.cpp
                EntranceNavigationTransitionInfo::CreateStoryboards   (translationOffset, in/outDuration, control points)
                SlideNavigationTransitionInfo::CreateStoryboards      (translationExit/EntranceOffset, durations, control points)
                DrillInNavigationTransitionInfo::CreateStoryboards    (scaleFactor per trigger, scale/opacity curves)
              dxaml/phone/lib/ThemeTransitions.h
                DrillInNavigationTransitionInfo::s_*Duration
              dxaml/phone/lib/NavigateTransitionHelper.h
                SLIDE_EASE, SLIDE_OFFSET_IN/OUT, SLIDE_MID_TIME, SLIDE_END_TIME

            Then update the constants and the recorded values in this file together, and set
            VerifiedAgainstWindowsAppSdkVersion to {actual}.
            """);
    }
}
