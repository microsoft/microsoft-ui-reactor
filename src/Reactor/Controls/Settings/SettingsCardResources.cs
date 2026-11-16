// Portions of this file are ported from the Windows Community Toolkit
// (https://github.com/CommunityToolkit/Windows, components/SettingsControls/src:
// SettingsCard/SettingsCard.xaml and SettingsExpander/SettingsExpander.xaml).
// Original notice:
//   Licensed to the .NET Foundation under one or more agreements.
//   The .NET Foundation licenses this file to you under the MIT license.
//   See the LICENSE file in the project root for more information.

using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// Spec 065 — the styles and templates of <c>SettingsCard</c> and <c>SettingsExpander</c>,
/// ported from the Toolkit's <c>SettingsCard.xaml</c> and <c>SettingsExpander.xaml</c> and
/// loaded with <see cref="XamlReader"/> on first use.
/// </summary>
/// <remarks>
/// <para>Differences from the Toolkit markup:</para>
/// <list type="bullet">
///   <item>Templates target built-in types (<c>ButtonBase</c>, <c>UserControl</c>), because
///   <see cref="XamlReader"/> cannot resolve Reactor's own types. Values the Toolkit bound to
///   its own dependency properties (header, description, icons) are set from code, and the
///   Toolkit's state triggers (<c>ControlSizeTrigger</c>, <c>IsEqualStateTrigger</c>,
///   <c>IsNullOrEmptyStateTrigger</c>) are replaced by <c>VisualStateManager.GoToState</c>
///   calls in <see cref="SettingsCardControl"/>.</item>
///   <item>The Toolkit's twelve <c>SettingsCard*</c> brush keys are aliases, defined per theme,
///   of WinUI brushes. An alias cannot be ported: an alias dictionary loaded at run time
///   resolves its target in the app's theme, not the element's, so a dark subtree would get
///   light brushes. And reading WinUI's theme dictionaries from code to build them makes it
///   worse: it fixes WinUI's entry for that theme to the app's theme for every control in the
///   app (spec 065). So the markup references the WinUI brushes directly, which WinUI resolves
///   per element theme; <see cref="ThemeBrushes"/> lists the substitution. The Toolkit maps the
///   same keys to other brushes in the <c>HighContrast</c> theme, so a second copy of the
///   markup is loaded with those brushes (<see cref="Contrast"/>), and the controls switch
///   to it while a contrast theme is on.</item>
///   <item>The resource keys carry a <c>Reactor</c> prefix (and the contrast copy a
///   <c>Contrast</c> suffix), so an app that also uses the Toolkit, like the <c>wct-controls</c>
///   reference sample, never sees one control's resources replace the other's. The keys are an
///   implementation detail, not a styling API: apps restyle the cards with Reactor theme tokens
///   and modifiers.</item>
///   <item><see cref="XamlReader"/> cannot resolve the attached <c>AnimatedIcon.State</c> (the
///   Toolkit's compiled XAML gets it from generated type info). The card template drops it:
///   Reactor icons are never animated icons. The expander header's visual states set the
///   chevron's <c>Tag</c> instead, and <see cref="SettingsExpanderControl"/> forwards it to
///   <c>AnimatedIcon.State</c>.</item>
///   <item>The Toolkit puts its content styles (the compact toggle switch, a 120 pixel minimum
///   width for sliders, combo boxes and text boxes) on each card through an attached
///   <c>StyleExtensions.Resources</c> property; <see cref="SettingsCardControl"/> merges a copy
///   of <see cref="ContentStyles"/> into its own resources the same way.</item>
///   <item>Constant resources (sizes, margins, the bitmap-icon disabled opacity) are inlined,
///   and the expander header style is declared before the expander style that uses it.</item>
/// </list>
/// </remarks>
internal static class SettingsCardResources
{
    /// <summary>
    /// The Toolkit's <c>SettingsCard*</c> brush keys (as <c>Reactor</c>-prefixed tokens in the
    /// markup), with the WinUI brush each one stands for in the <c>Default</c> and <c>Light</c>
    /// themes, and in the <c>HighContrast</c> theme. The Toolkit also defines
    /// <c>SettingsCardBackgroundDisabled</c>, which none of its templates use: a disabled card
    /// keeps its background.
    /// </summary>
    internal static readonly (string Token, string Standard, string HighContrast)[] ThemeBrushes =
    [
        ("ReactorSettingsCardBackground", "CardBackgroundFillColorDefaultBrush", "SystemColorButtonFaceColorBrush"),
        ("ReactorSettingsCardBackgroundPointerOver", "ControlFillColorSecondaryBrush", "SystemColorHighlightTextColorBrush"),
        ("ReactorSettingsCardBackgroundPressed", "ControlFillColorTertiaryBrush", "SystemColorHighlightTextColorBrush"),
        ("ReactorSettingsCardForeground", "TextFillColorPrimaryBrush", "SystemColorButtonTextColorBrush"),
        ("ReactorSettingsCardForegroundPointerOver", "TextFillColorPrimaryBrush", "SystemColorHighlightColorBrush"),
        ("ReactorSettingsCardForegroundPressed", "TextFillColorSecondaryBrush", "SystemColorHighlightColorBrush"),
        ("ReactorSettingsCardForegroundDisabled", "TextFillColorDisabledBrush", "SystemControlDisabledBaseMediumLowBrush"),
        ("ReactorSettingsCardBorderBrush", "CardStrokeColorDefaultBrush", "SystemColorButtonTextColorBrush"),
        ("ReactorSettingsCardBorderBrushPointerOver", "ControlElevationBorderBrush", "SystemColorHighlightColorBrush"),
        ("ReactorSettingsCardBorderBrushPressed", "ControlStrokeColorDefaultBrush", "SystemColorHighlightTextColorBrush"),
        ("ReactorSettingsCardBorderBrushDisabled", "ControlStrokeColorDefaultBrush", "SystemControlDisabledTransparentBrush"),
    ];

    private static Variant? s_standard;
    private static Variant? s_contrast;
    private static ResourceDictionary? s_contentStyles;

    /// <summary>The markup with the Light and Dark theme brushes.</summary>
    internal static Variant Standard => s_standard ??= new Variant(highContrast: false);

    /// <summary>The markup with the Toolkit's contrast-theme brushes. Loaded on first use.</summary>
    internal static Variant Contrast => s_contrast ??= new Variant(highContrast: true);

    /// <summary>The variant for the current Windows contrast setting. Re-read on every
    /// reconcile, so the full re-render a contrast change triggers switches every control.</summary>
    internal static Variant Current => HighContrastState.IsActive ? Contrast : Standard;

    /// <summary>The outer expander control's style; the same for both variants.</summary>
    internal static Style ExpanderStyle => (Style)Standard.Resources["ReactorSettingsExpanderStyle"];

    /// <summary>The implicit styles a card gives its content (Toolkit: the card style's
    /// <c>StyleExtensions.Resources</c>), with the compact toggle switch style they build on.</summary>
    internal static ResourceDictionary ContentStyles
        => s_contentStyles ??= (ResourceDictionary)XamlReader.Load(ContentStylesXaml);

    /// <summary>One loaded copy of the markup, merged into the application resources.</summary>
    internal sealed class Variant
    {
        internal Variant(bool highContrast)
        {
            IsHighContrast = highContrast;
            KeySuffix = highContrast ? "Contrast" : string.Empty;
            Resources = (ResourceDictionary)XamlReader.Load(Specialize(Xaml, highContrast));
            Application.Current?.Resources.MergedDictionaries.Add(Resources);
        }

        internal bool IsHighContrast { get; }

        /// <summary>Appended to every resource key of this copy.</summary>
        internal string KeySuffix { get; }

        internal ResourceDictionary Resources { get; }

        internal string CardStyleKey => "ReactorSettingsCardStyle" + KeySuffix;
        internal string ExpanderItemStyleKey => "ReactorSettingsExpanderItemStyle" + KeySuffix;
        internal string ExpanderClickableItemStyleKey => "ReactorSettingsExpanderClickableItemStyle" + KeySuffix;

        internal Style CardStyle => (Style)Resources[CardStyleKey];
        internal Style ExpanderItemStyle => (Style)Resources[ExpanderItemStyleKey];
        internal Style ExpanderClickableItemStyle => (Style)Resources[ExpanderClickableItemStyleKey];
        internal ControlTemplate CardTemplate => (ControlTemplate)Resources["ReactorSettingsCardTemplate" + KeySuffix];
        internal Style ExpanderExpanderStyle => (Style)Resources["ReactorSettingsExpanderExpanderStyle" + KeySuffix];
    }

    /// <summary>
    /// The markup for one variant: each brush token becomes the WinUI brush for that variant,
    /// and the contrast copy suffixes its own resource keys (and the references to them).
    /// </summary>
    internal static string Specialize(string xaml, bool highContrast)
    {
        foreach (var (token, standard, contrast) in ThemeBrushes)
            xaml = xaml.Replace("{ThemeResource " + token + "}", "{ThemeResource " + (highContrast ? contrast : standard) + "}");
        if (highContrast)
        {
            xaml = global::System.Text.RegularExpressions.Regex.Replace(
                xaml, "(x:Key=\"|\\{StaticResource )(Reactor\\w+)", "$1$2Contrast");
        }
        return xaml;
    }
    internal const string ContentStylesXaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:win="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <Style x:Key="ReactorSettingsCardToggleSwitchStyle"
           BasedOn="{StaticResource DefaultToggleSwitchStyle}"
           TargetType="ToggleSwitch">
        <Style.Setters>
            <Setter Property="MinWidth" Value="0" />
            <Setter Property="Height" Value="36" />
            <Setter Property="HorizontalContentAlignment" Value="Right" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ToggleSwitch">
                        <Grid VerticalAlignment="Stretch"
                              Background="{TemplateBinding Background}"
                              BorderBrush="{TemplateBinding BorderBrush}"
                              BorderThickness="{TemplateBinding BorderThickness}"
                              Control.IsTemplateFocusTarget="True"
                              CornerRadius="{TemplateBinding CornerRadius}">
                            <VisualStateManager.VisualStateGroups>
                                <VisualStateGroup x:Name="CommonStates">
                                    <VisualState x:Name="Normal">
                                        <Storyboard>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="OuterBorder"
                                                                           Storyboard.TargetProperty="Stroke">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchStrokeOff}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="OuterBorder"
                                                                           Storyboard.TargetProperty="Fill">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchFillOff}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Fill">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchKnobFillOff}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Background">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchKnobFillOn}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                           Storyboard.TargetProperty="Fill">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchFillOn}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                           Storyboard.TargetProperty="Stroke">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchStrokeOn}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchAreaGrid"
                                                                           Storyboard.TargetProperty="Background">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchContainerBackground}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Width">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="12" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Height">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="12" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Width">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="12" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Height">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="12" />
                                            </DoubleAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState>
                                    <VisualState x:Name="PointerOver">
                                        <Storyboard>
                                            <ColorAnimationUsingKeyFrames Storyboard.TargetName="OuterBorder"
                                                                          Storyboard.TargetProperty="(Shape.Stroke).(SolidColorBrush.Color)">
                                                <LinearColorKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                     Value="{ThemeResource ToggleSwitchStrokeOffPointerOver}" />
                                            </ColorAnimationUsingKeyFrames>
                                            <ColorAnimationUsingKeyFrames Storyboard.TargetName="OuterBorder"
                                                                          Storyboard.TargetProperty="(Shape.Fill).(SolidColorBrush.Color)">
                                                <LinearColorKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                     Value="{ThemeResource ToggleSwitchFillOffPointerOver}" />
                                            </ColorAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Fill">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchKnobFillOffPointerOver}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Background">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchKnobFillOnPointerOver}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                           Storyboard.TargetProperty="Fill">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchFillOnPointerOver}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                           Storyboard.TargetProperty="Stroke">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchStrokeOnPointerOver}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ColorAnimationUsingKeyFrames Storyboard.TargetName="SwitchAreaGrid"
                                                                          Storyboard.TargetProperty="(Panel.Background).(SolidColorBrush.Color)">
                                                <LinearColorKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                     Value="{ThemeResource ToggleSwitchContainerBackgroundPointerOver}" />
                                            </ColorAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Width">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="14" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Height">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="14" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Width">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="14" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Height">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="14" />
                                            </DoubleAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState>
                                    <VisualState x:Name="Pressed">
                                        <VisualState.Setters>
                                            <Setter Target="SwitchKnobOn.HorizontalAlignment" Value="Right" />
                                            <Setter Target="SwitchKnobOn.Margin" Value="0,0,3,0" />
                                            <Setter Target="SwitchKnobOff.HorizontalAlignment" Value="Left" />
                                            <Setter Target="SwitchKnobOff.Margin" Value="3,0,0,0" />
                                        </VisualState.Setters>
                                        <Storyboard>
                                            <ColorAnimationUsingKeyFrames Storyboard.TargetName="OuterBorder"
                                                                          Storyboard.TargetProperty="(Shape.Stroke).(SolidColorBrush.Color)">
                                                <LinearColorKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                     Value="{ThemeResource ToggleSwitchStrokeOffPressed}" />
                                            </ColorAnimationUsingKeyFrames>
                                            <ColorAnimationUsingKeyFrames Storyboard.TargetName="OuterBorder"
                                                                          Storyboard.TargetProperty="(Shape.Fill).(SolidColorBrush.Color)">
                                                <LinearColorKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                     Value="{ThemeResource ToggleSwitchFillOffPressed}" />
                                            </ColorAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                           Storyboard.TargetProperty="Fill">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchFillOnPressed}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                           Storyboard.TargetProperty="Stroke">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchStrokeOnPressed}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Fill">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchKnobFillOffPressed}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Background">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchKnobFillOnPressed}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ColorAnimationUsingKeyFrames Storyboard.TargetName="SwitchAreaGrid"
                                                                          Storyboard.TargetProperty="(Panel.Background).(SolidColorBrush.Color)">
                                                <LinearColorKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                     Value="{ThemeResource ToggleSwitchContainerBackgroundPressed}" />
                                            </ColorAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Width">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="17" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Height">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="14" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Width">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="17" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Height">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="14" />
                                            </DoubleAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState>
                                    <VisualState x:Name="Disabled">
                                        <Storyboard>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="HeaderContentPresenter"
                                                                           Storyboard.TargetProperty="Foreground">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchHeaderForegroundDisabled}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="OffContentPresenter"
                                                                           Storyboard.TargetProperty="Foreground">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchContentForegroundDisabled}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="OnContentPresenter"
                                                                           Storyboard.TargetProperty="Foreground">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchContentForegroundDisabled}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ColorAnimationUsingKeyFrames Storyboard.TargetName="OuterBorder"
                                                                          Storyboard.TargetProperty="(Shape.Stroke).(SolidColorBrush.Color)">
                                                <LinearColorKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                     Value="{ThemeResource ToggleSwitchStrokeOffDisabled}" />
                                            </ColorAnimationUsingKeyFrames>
                                            <ColorAnimationUsingKeyFrames Storyboard.TargetName="OuterBorder"
                                                                          Storyboard.TargetProperty="(Shape.Fill).(SolidColorBrush.Color)">
                                                <LinearColorKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                     Value="{ThemeResource ToggleSwitchFillOffDisabled}" />
                                            </ColorAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                           Storyboard.TargetProperty="Fill">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchFillOnDisabled}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                           Storyboard.TargetProperty="Stroke">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchStrokeOnDisabled}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Fill">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchKnobFillOffDisabled}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Background">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ToggleSwitchKnobFillOnDisabled}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ColorAnimationUsingKeyFrames Storyboard.TargetName="SwitchAreaGrid"
                                                                          Storyboard.TargetProperty="(Panel.Background).(SolidColorBrush.Color)">
                                                <LinearColorKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                     Value="{ThemeResource ToggleSwitchContainerBackgroundDisabled}" />
                                            </ColorAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Width">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlNormalAnimationDuration}"
                                                                      Value="12" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Height">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlNormalAnimationDuration}"
                                                                      Value="12" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Width">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlNormalAnimationDuration}"
                                                                      Value="12" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames EnableDependentAnimation="True"
                                                                           Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Height">
                                                <SplineDoubleKeyFrame KeySpline="{StaticResource ControlFastOutSlowInKeySpline}"
                                                                      KeyTime="{StaticResource ControlNormalAnimationDuration}"
                                                                      Value="12" />
                                            </DoubleAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState>
                                </VisualStateGroup>
                                <VisualStateGroup x:Name="ToggleStates">
                                    <VisualStateGroup.Transitions>
                                        <VisualTransition x:Name="DraggingToOnTransition"
                                                          win:GeneratedDuration="0"
                                                          From="Dragging"
                                                          To="On">
                                            <Storyboard>
                                                <win:RepositionThemeAnimation FromHorizontalOffset="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=TemplateSettings.KnobCurrentToOnOffset}"
                                                                              TargetName="SwitchKnob" />
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                          Value="1" />
                                                </DoubleAnimationUsingKeyFrames>
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="OuterBorder"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                          Value="0" />
                                                </DoubleAnimationUsingKeyFrames>
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOn"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                          Value="1" />
                                                </DoubleAnimationUsingKeyFrames>
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOff"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                          Value="0" />
                                                </DoubleAnimationUsingKeyFrames>
                                            </Storyboard>
                                        </VisualTransition>
                                        <VisualTransition x:Name="OnToDraggingTransition"
                                                          win:GeneratedDuration="0"
                                                          From="On"
                                                          To="Dragging">
                                            <Storyboard>
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="0"
                                                                          Value="1" />
                                                </DoubleAnimationUsingKeyFrames>
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOn"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="0"
                                                                          Value="1" />
                                                </DoubleAnimationUsingKeyFrames>
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOff"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="0"
                                                                          Value="0" />
                                                </DoubleAnimationUsingKeyFrames>
                                            </Storyboard>
                                        </VisualTransition>
                                        <VisualTransition x:Name="DraggingToOffTransition"
                                                          win:GeneratedDuration="0"
                                                          From="Dragging"
                                                          To="Off">
                                            <Storyboard>
                                                <win:RepositionThemeAnimation FromHorizontalOffset="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=TemplateSettings.KnobCurrentToOffOffset}"
                                                                              TargetName="SwitchKnob" />
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                          Value="0" />
                                                </DoubleAnimationUsingKeyFrames>
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOn"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                          Value="0" />
                                                </DoubleAnimationUsingKeyFrames>
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOff"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                          Value="1" />
                                                </DoubleAnimationUsingKeyFrames>
                                            </Storyboard>
                                        </VisualTransition>
                                        <VisualTransition x:Name="OnToOffTransition"
                                                          win:GeneratedDuration="0"
                                                          From="On"
                                                          To="Off">
                                            <Storyboard>
                                                <win:RepositionThemeAnimation FromHorizontalOffset="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=TemplateSettings.KnobOnToOffOffset}"
                                                                              TargetName="SwitchKnob" />
                                            </Storyboard>
                                        </VisualTransition>
                                        <VisualTransition x:Name="OffToOnTransition"
                                                          win:GeneratedDuration="0"
                                                          From="Off"
                                                          To="On">
                                            <Storyboard>
                                                <win:RepositionThemeAnimation FromHorizontalOffset="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=TemplateSettings.KnobOffToOnOffset}"
                                                                              TargetName="SwitchKnob" />
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                          Value="1" />
                                                </DoubleAnimationUsingKeyFrames>
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="OuterBorder"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                          Value="0" />
                                                </DoubleAnimationUsingKeyFrames>
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOn"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                          Value="1" />
                                                </DoubleAnimationUsingKeyFrames>
                                                <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOff"
                                                                               Storyboard.TargetProperty="Opacity">
                                                    <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                          Value="0" />
                                                </DoubleAnimationUsingKeyFrames>
                                            </Storyboard>
                                        </VisualTransition>
                                    </VisualStateGroup.Transitions>
                                    <VisualState x:Name="Dragging" />
                                    <VisualState x:Name="Off" />
                                    <VisualState x:Name="On">
                                        <Storyboard>
                                            <DoubleAnimation Storyboard.TargetName="KnobTranslateTransform"
                                                             Storyboard.TargetProperty="X"
                                                             To="20"
                                                             Duration="0" />
                                            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobBounds"
                                                                           Storyboard.TargetProperty="Opacity">
                                                <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="1" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="OuterBorder"
                                                                           Storyboard.TargetProperty="Opacity">
                                                <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="0" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOn"
                                                                           Storyboard.TargetProperty="Opacity">
                                                <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="1" />
                                            </DoubleAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SwitchKnobOff"
                                                                           Storyboard.TargetProperty="Opacity">
                                                <LinearDoubleKeyFrame KeyTime="{StaticResource ControlFasterAnimationDuration}"
                                                                      Value="0" />
                                            </DoubleAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState>
                                </VisualStateGroup>
                                <VisualStateGroup x:Name="ContentStates">
                                    <VisualState x:Name="OffContent">
                                        <Storyboard>
                                            <DoubleAnimation Storyboard.TargetName="OffContentPresenter"
                                                             Storyboard.TargetProperty="Opacity"
                                                             To="1"
                                                             Duration="0" />
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="OffContentPresenter"
                                                                           Storyboard.TargetProperty="IsHitTestVisible">
                                                <DiscreteObjectKeyFrame KeyTime="0">
                                                    <DiscreteObjectKeyFrame.Value>
                                                        <x:Boolean>True</x:Boolean>
                                                    </DiscreteObjectKeyFrame.Value>
                                                </DiscreteObjectKeyFrame>
                                            </ObjectAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState>
                                    <VisualState x:Name="OnContent">
                                        <Storyboard>
                                            <DoubleAnimation Storyboard.TargetName="OnContentPresenter"
                                                             Storyboard.TargetProperty="Opacity"
                                                             To="1"
                                                             Duration="0" />
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="OnContentPresenter"
                                                                           Storyboard.TargetProperty="IsHitTestVisible">
                                                <DiscreteObjectKeyFrame KeyTime="0">
                                                    <DiscreteObjectKeyFrame.Value>
                                                        <x:Boolean>True</x:Boolean>
                                                    </DiscreteObjectKeyFrame.Value>
                                                </DiscreteObjectKeyFrame>
                                            </ObjectAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState>
                                </VisualStateGroup>
                            </VisualStateManager.VisualStateGroups>
                            <ContentPresenter x:Name="HeaderContentPresenter"
                                              Grid.Row="0"
                                              Margin="{ThemeResource ToggleSwitchTopHeaderMargin}"
                                              VerticalAlignment="Top"
                                              win:AutomationProperties.AccessibilityView="Raw"
                                              x:DeferLoadStrategy="Lazy"
                                              Content="{TemplateBinding Header}"
                                              ContentTemplate="{TemplateBinding HeaderTemplate}"
                                              Foreground="{ThemeResource ToggleSwitchHeaderForeground}"
                                              IsHitTestVisible="False"
                                              TextWrapping="Wrap"
                                              Visibility="Collapsed" />
                            <Grid Grid.Row="1"
                                  HorizontalAlignment="Right"
                                  VerticalAlignment="Center">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="12"
                                                      MaxWidth="12" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <Grid x:Name="SwitchAreaGrid"
                                      Grid.RowSpan="3"
                                      Grid.ColumnSpan="3"
                                      Margin="0,5"
                                      Background="{ThemeResource ToggleSwitchContainerBackground}"
                                      CornerRadius="{TemplateBinding CornerRadius}" />
                                <ContentPresenter x:Name="OffContentPresenter"
                                                  Grid.RowSpan="3"
                                                  HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                                  VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                                  win:AutomationProperties.AccessibilityView="Raw"
                                                  Content="{TemplateBinding OffContent}"
                                                  ContentTemplate="{TemplateBinding OffContentTemplate}"
                                                  Foreground="{TemplateBinding Foreground}"
                                                  IsHitTestVisible="False"
                                                  Opacity="0" />
                                <ContentPresenter x:Name="OnContentPresenter"
                                                  Grid.RowSpan="3"
                                                  HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                                  VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                                  win:AutomationProperties.AccessibilityView="Raw"
                                                  Content="{TemplateBinding OnContent}"
                                                  ContentTemplate="{TemplateBinding OnContentTemplate}"
                                                  Foreground="{TemplateBinding Foreground}"
                                                  IsHitTestVisible="False"
                                                  Opacity="0" />
                                <Rectangle x:Name="OuterBorder"
                                           Grid.Row="1"
                                           Grid.Column="2"
                                           Width="40"
                                           Height="20"
                                           Fill="{ThemeResource ToggleSwitchFillOff}"
                                           RadiusX="10"
                                           RadiusY="10"
                                           Stroke="{ThemeResource ToggleSwitchStrokeOff}"
                                           StrokeThickness="{ThemeResource ToggleSwitchOuterBorderStrokeThickness}" />
                                <Rectangle x:Name="SwitchKnobBounds"
                                           Grid.Row="1"
                                           Grid.Column="2"
                                           Width="40"
                                           Height="20"
                                           Fill="{ThemeResource ToggleSwitchFillOn}"
                                           Opacity="0"
                                           RadiusX="10"
                                           RadiusY="10"
                                           Stroke="{ThemeResource ToggleSwitchStrokeOn}"
                                           StrokeThickness="{ThemeResource ToggleSwitchOnStrokeThickness}" />
                                <Grid x:Name="SwitchKnob"
                                      Grid.Row="1"
                                      Grid.Column="2"
                                      Width="20"
                                      Height="20"
                                      HorizontalAlignment="Left">
                                    <Border x:Name="SwitchKnobOn"
                                            Width="12"
                                            Height="12"
                                            Margin="0,0,3,0"
                                            HorizontalAlignment="Right"
                                            Background="{ThemeResource ToggleSwitchKnobFillOn}"
                                            BackgroundSizing="OuterBorderEdge"
                                            BorderBrush="{ThemeResource ToggleSwitchKnobStrokeOn}"
                                            CornerRadius="7"
                                            Opacity="0"
                                            RenderTransformOrigin="0.5, 0.5">
                                        <Border.RenderTransform>
                                            <CompositeTransform />
                                        </Border.RenderTransform>
                                    </Border>
                                    <Rectangle x:Name="SwitchKnobOff"
                                               Width="12"
                                               Height="12"
                                               Margin="3,0,0,0"
                                               HorizontalAlignment="Left"
                                               Fill="{ThemeResource ToggleSwitchKnobFillOff}"
                                               RadiusX="7"
                                               RadiusY="7"
                                               RenderTransformOrigin="0.5, 0.5">
                                        <Rectangle.RenderTransform>
                                            <CompositeTransform />
                                        </Rectangle.RenderTransform>
                                    </Rectangle>
                                    <Grid.RenderTransform>
                                        <TranslateTransform x:Name="KnobTranslateTransform" />
                                    </Grid.RenderTransform>
                                </Grid>
                                <Thumb x:Name="SwitchThumb"
                                       Grid.RowSpan="3"
                                       Grid.ColumnSpan="3"
                                       win:AutomationProperties.AccessibilityView="Raw">
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Rectangle Fill="Transparent" />
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Grid>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style.Setters>
    </Style>

    <!--  Making sure ToggleSwitches are right-aligned by default  -->
    <Style BasedOn="{StaticResource ReactorSettingsCardToggleSwitchStyle}"
           TargetType="ToggleSwitch" />
    <!--  Overriding the MinWidth of various types of controls so they neatly align  -->
    <Style BasedOn="{StaticResource DefaultSliderStyle}"
           TargetType="Slider">
        <Setter Property="MinWidth" Value="120" />
    </Style>
    <Style BasedOn="{StaticResource DefaultComboBoxStyle}"
           TargetType="ComboBox">
        <Setter Property="MinWidth" Value="120" />
    </Style>
    <Style BasedOn="{StaticResource DefaultTextBoxStyle}"
           TargetType="TextBox">
        <Setter Property="MinWidth" Value="120" />
    </Style>
</ResourceDictionary>
""";

    internal const string Xaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:animatedvisuals="using:Microsoft.UI.Xaml.Controls.AnimatedVisuals"
                    xmlns:muxc="using:Microsoft.UI.Xaml.Controls"
                    xmlns:win="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <!--  Each ReactorSettingsCard* brush token is replaced on load by the WinUI brush it  -->
    <!--  stands for in the Light/Dark or the contrast variant: see SettingsCardResources.ThemeBrushes.  -->
    <!--  The card's content styles are a separate dictionary: see SettingsCardResources.ContentStyles.  -->

    <Style x:Key="ReactorSettingsCardStyle"
           TargetType="ButtonBase">
        <Setter Property="Background" Value="{ThemeResource ReactorSettingsCardBackground}" />
        <Setter Property="Foreground" Value="{ThemeResource ReactorSettingsCardForeground}" />
        <Setter Property="BorderBrush" Value="{ThemeResource ReactorSettingsCardBorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="{ThemeResource ControlCornerRadius}" />
        <Setter Property="MinHeight" Value="68" />
        <Setter Property="MinWidth" Value="148" />
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="HorizontalAlignment" Value="Stretch" />
        <Setter Property="HorizontalContentAlignment" Value="Right" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="BackgroundSizing" Value="InnerBorderEdge" />
        <Setter Property="Padding" Value="16,16,16,16" />
        <Setter Property="FontFamily" Value="{ThemeResource ContentControlThemeFontFamily}" />
        <Setter Property="FontWeight" Value="Normal" />
        <Setter Property="FontSize" Value="{ThemeResource ControlContentThemeFontSize}" />
        <Setter Property="UseSystemFocusVisuals" Value="{StaticResource UseSystemFocusVisuals}" />
        <Setter Property="FocusVisualMargin" Value="-3" />
    </Style>

    <Style x:Key="ReactorSettingsExpanderItemStyle"
           BasedOn="{StaticResource ReactorSettingsCardStyle}"
           TargetType="ButtonBase">
        <Setter Property="BorderThickness" Value="0,1,0,0" />
        <Setter Property="MinHeight" Value="52" />
        <Setter Property="Padding" Value="58,8,44,8" />
        <Setter Property="CornerRadius" Value="0" />
    </Style>

    <Style x:Key="ReactorSettingsExpanderClickableItemStyle"
           BasedOn="{StaticResource ReactorSettingsExpanderItemStyle}"
           TargetType="ButtonBase">
        <Setter Property="Padding" Value="58,8,16,8" />
    </Style>

    <!--  The outer SettingsExpander control. As in the Toolkit, it only sizes and names the inner Expander;  -->
    <!--  the visible chrome comes from the Expander and its header styles below.  -->
    <Style x:Key="ReactorSettingsExpanderStyle"
           TargetType="UserControl">
        <Setter Property="CornerRadius" Value="{ThemeResource ControlCornerRadius}" />
        <Setter Property="MinHeight" Value="68" />
        <Setter Property="MinWidth" Value="148" />
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="HorizontalAlignment" Value="Stretch" />
        <Setter Property="HorizontalContentAlignment" Value="Stretch" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="FontFamily" Value="{ThemeResource ContentControlThemeFontFamily}" />
        <Setter Property="FontWeight" Value="Normal" />
        <Setter Property="FontSize" Value="{ThemeResource ControlContentThemeFontSize}" />
    </Style>

    <ControlTemplate x:Key="ReactorSettingsCardTemplate"
                     TargetType="ButtonBase">
                        <Grid x:Name="PART_RootGrid"
                              MinWidth="{TemplateBinding MinWidth}"
                              MinHeight="{TemplateBinding MinHeight}"
                              MaxWidth="{TemplateBinding MaxWidth}"
                              Padding="{TemplateBinding Padding}"
                              HorizontalAlignment="{TemplateBinding HorizontalAlignment}"
                              VerticalAlignment="{TemplateBinding VerticalAlignment}"
                              Background="{TemplateBinding Background}"
                              BackgroundSizing="{TemplateBinding BackgroundSizing}"
                              BorderBrush="{TemplateBinding BorderBrush}"
                              BorderThickness="{TemplateBinding BorderThickness}"
                              CornerRadius="{TemplateBinding CornerRadius}">
                            <Grid.BackgroundTransition>
                                <BrushTransition Duration="0:0:0.083" />
                            </Grid.BackgroundTransition>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="*" />
                                <RowDefinition Height="Auto" />
                            </Grid.RowDefinitions>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto"
                                                  MinWidth="0" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <VisualStateManager.VisualStateGroups>
                                <VisualStateGroup x:Name="CommonStates">
                                    <VisualState x:Name="Normal" />
                                    <VisualState x:Name="PointerOver">
                                        <Storyboard>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_RootGrid"
                                                                           Storyboard.TargetProperty="Background">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardBackgroundPointerOver}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_RootGrid"
                                                                           Storyboard.TargetProperty="BorderBrush">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardBorderBrushPointerOver}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_HeaderPresenter"
                                                                           Storyboard.TargetProperty="Foreground">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardForegroundPointerOver}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_HeaderIconPresenter"
                                                                           Storyboard.TargetProperty="Foreground">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardForegroundPointerOver}" />
                                            </ObjectAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState>
                                    <VisualState x:Name="Pressed">
                                        <Storyboard>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_RootGrid"
                                                                           Storyboard.TargetProperty="Background">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardBackgroundPressed}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_RootGrid"
                                                                           Storyboard.TargetProperty="BorderBrush">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardBorderBrushPressed}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_HeaderIconPresenter"
                                                                           Storyboard.TargetProperty="Foreground">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardForegroundPressed}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_HeaderPresenter"
                                                                           Storyboard.TargetProperty="Foreground">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardForegroundPressed}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_DescriptionPresenter"
                                                                           Storyboard.TargetProperty="Foreground">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardForegroundPressed}" />
                                            </ObjectAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState>
                                    <VisualState x:Name="Disabled">
                                        <Storyboard>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_ActionIconPresenter"
                                                                           Storyboard.TargetProperty="Foreground">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardForegroundDisabled}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_HeaderIconPresenter"
                                                                           Storyboard.TargetProperty="Foreground">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardForegroundDisabled}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_HeaderPresenter"
                                                                           Storyboard.TargetProperty="Foreground">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardForegroundDisabled}" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="PART_DescriptionPresenter"
                                                                           Storyboard.TargetProperty="Foreground">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="{ThemeResource ReactorSettingsCardForegroundDisabled}" />
                                            </ObjectAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState>
                                </VisualStateGroup>

                                <!--  These states are only relevant if a BitmapIcon is used as HeaderIcon or ActionIcon.  -->
                                <VisualStateGroup x:Name="BitmapHeaderIconStates">
                                    <VisualState x:Name="BitmapHeaderIconEnabled" />
                                    <VisualState x:Name="BitmapHeaderIconDisabled">
                                        <VisualState.Setters>
                                            <Setter Target="PART_HeaderIconPresenterHolder.Opacity" Value="0.4" />
                                        </VisualState.Setters>
                                    </VisualState>
                                </VisualStateGroup>

                                <VisualStateGroup x:Name="ContentAlignmentStates">
                                    <!--  Default  -->
                                    <VisualState x:Name="Right" />

                                    <!--  Whenever the control width is less than SettingsCardWrapThreshold, the Content is below the Header/Description  -->
                                    <VisualState x:Name="RightWrapped">
                                        <VisualState.Setters>
                                            <Setter Target="PART_ContentPresenter.(Grid.Row)" Value="1" />
                                            <Setter Target="PART_ContentPresenter.(Grid.Column)" Value="1" />
                                            <Setter Target="PART_ContentPresenter.HorizontalAlignment" Value="Stretch" />
                                            <Setter Target="PART_ContentPresenter.HorizontalContentAlignment" Value="Left" />
                                            <Setter Target="HeaderPanel.Margin" Value="0" />
                                        </VisualState.Setters>
                                    </VisualState>

                                    <!--  For even smaller widths: the HeaderIcon is collapsed.  -->
                                    <VisualState x:Name="RightWrappedNoIcon">
                                        <VisualState.Setters>
                                            <Setter Target="PART_HeaderIconPresenterHolder.Visibility" Value="Collapsed" />
                                            <Setter Target="PART_ContentPresenter.(Grid.Row)" Value="1" />
                                            <Setter Target="PART_ContentPresenter.(Grid.Column)" Value="1" />
                                            <Setter Target="PART_ContentPresenter.HorizontalAlignment" Value="Stretch" />
                                            <Setter Target="PART_ContentPresenter.HorizontalContentAlignment" Value="Left" />
                                            <Setter Target="HeaderPanel.Margin" Value="0" />
                                        </VisualState.Setters>
                                    </VisualState>

                                    <!--  Header/Description/Icon collapsed, content is to the left. Great for e.g. Checkboxes  -->
                                    <VisualState x:Name="Left">
                                        <VisualState.Setters>
                                            <Setter Target="PART_HeaderIconPresenterHolder.Visibility" Value="Collapsed" />
                                            <Setter Target="PART_DescriptionPresenter.Visibility" Value="Collapsed" />
                                            <Setter Target="PART_HeaderPresenter.Visibility" Value="Collapsed" />
                                            <Setter Target="PART_ContentPresenter.(Grid.Row)" Value="1" />
                                            <Setter Target="PART_ContentPresenter.(Grid.Column)" Value="1" />
                                            <Setter Target="PART_ContentPresenter.HorizontalAlignment" Value="Left" />
                                        </VisualState.Setters>
                                    </VisualState>

                                    <!--  Similiar to Left, but the HeaderIcon/Header/Description is visible  -->
                                    <VisualState x:Name="Vertical">
                                        <VisualState.Setters>
                                            <Setter Target="PART_ContentPresenter.(Grid.Row)" Value="1" />
                                            <Setter Target="PART_ContentPresenter.(Grid.Column)" Value="1" />
                                            <Setter Target="PART_ContentPresenter.HorizontalAlignment" Value="Stretch" />
                                        </VisualState.Setters>
                                    </VisualState>
                                </VisualStateGroup>

                                <!--  Collapsing the Content presenter whenever it's empty  -->
                                <VisualStateGroup x:Name="ContentVisibilityStates">
                                    <VisualState x:Name="Visible" />
                                    <VisualState x:Name="Collapsed">
                                        <VisualState.Setters>
                                            <Setter Target="PART_ContentPresenter.Visibility" Value="Collapsed" />
                                        </VisualState.Setters>
                                    </VisualState>
                                </VisualStateGroup>

                                <VisualStateGroup x:Name="ContentSpacingStates">
                                    <VisualState x:Name="NoContentSpacing" />
                                    <VisualState x:Name="ContentSpacing">
                                        <VisualState.Setters>
                                            <Setter Target="PART_RootGrid.RowSpacing" Value="8" />
                                        </VisualState.Setters>
                                    </VisualState>
                                </VisualStateGroup>
                            </VisualStateManager.VisualStateGroups>

                            <Viewbox x:Name="PART_HeaderIconPresenterHolder"
                                     Grid.RowSpan="1"
                                     MaxWidth="20"
                                     MaxHeight="20"
                                     Margin="2,0,20,0">
                                <ContentPresenter x:Name="PART_HeaderIconPresenter"
                                                  AutomationProperties.AccessibilityView="Raw"
                                                  HighContrastAdjustment="None" />
                            </Viewbox>

                            <StackPanel x:Name="HeaderPanel"
                                        Grid.Column="1"
                                        Margin="0,0,24,0"
                                        VerticalAlignment="Center"
                                        Orientation="Vertical">
                                <ContentPresenter x:Name="PART_HeaderPresenter"
                                                  Grid.Column="1"
                                                  HorizontalAlignment="Left"
                                                  HighContrastAdjustment="None"
                                                  TextWrapping="Wrap" />

                                <ContentPresenter x:Name="PART_DescriptionPresenter"
                                                  FontSize="12"
                                                  Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                                                  TextWrapping="Wrap">
                                    <ContentPresenter.Resources>
                                        <Style BasedOn="{StaticResource CaptionTextBlockStyle}"
                                               TargetType="TextBlock">
                                            <Style.Setters>
                                                <Setter Property="TextWrapping" Value="Wrap" />
                                            </Style.Setters>
                                        </Style>
                                        <Style BasedOn="{StaticResource DefaultHyperlinkButtonStyle}"
                                               TargetType="HyperlinkButton">
                                            <Style.Setters>
                                                <Setter Property="FontSize" Value="12" />
                                                <Setter Property="Padding" Value="0,0,0,-1" />
                                                <Setter Property="FontWeight" Value="SemiBold" />
                                            </Style.Setters>
                                        </Style>
                                        <Style TargetType="Hyperlink">
                                            <Style.Setters>
                                                <Setter Property="UnderlineStyle" Value="None" />
                                                <Setter Property="TextDecorations" Value="None" />
                                                <Setter Property="FontWeight" Value="SemiBold" />
                                            </Style.Setters>
                                        </Style>
                                    </ContentPresenter.Resources>
                                </ContentPresenter>
                            </StackPanel>

                            <ContentPresenter x:Name="PART_ContentPresenter"
                                              Grid.Column="2"
                                              HorizontalAlignment="Right"
                                              VerticalAlignment="Center"
                                              HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}"
                                              Content="{TemplateBinding Content}" />

                            <Viewbox x:Name="PART_ActionIconPresenterHolder"
                                     Grid.RowSpan="2"
                                     Grid.Column="3"
                                     MaxWidth="13"
                                     MaxHeight="13"
                                     Margin="14,0,0,0"
                                     HorizontalAlignment="Center"
                                     VerticalAlignment="Center"
                                     HighContrastAdjustment="None"
                                     Visibility="Collapsed">
                                <ContentPresenter x:Name="PART_ActionIconPresenter"
                                                  AutomationProperties.AccessibilityView="Raw"
                                                  HighContrastAdjustment="None"
                                                  FontFamily="{ThemeResource SymbolThemeFontFamily}" />
                            </Viewbox>
                        </Grid>
    </ControlTemplate>

    <Style x:Key="ReactorSettingsExpanderHeaderDownStyle"
           TargetType="ToggleButton">
        <Setter Property="Padding" Value="0" />
        <Setter Property="HorizontalContentAlignment" Value="Left" />
        <Setter Property="BackgroundSizing" Value="InnerBorderEdge" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                    <Grid x:Name="ToggleButtonGrid"
                          Width="{TemplateBinding Width}"
                          MinWidth="{TemplateBinding MinWidth}"
                          MinHeight="{TemplateBinding MinHeight}"
                          MaxWidth="{TemplateBinding MaxWidth}"
                          HorizontalAlignment="{TemplateBinding HorizontalAlignment}"
                          Background="{TemplateBinding Background}"
                          BackgroundSizing="{TemplateBinding BackgroundSizing}"
                          BorderBrush="{ThemeResource ExpanderHeaderBorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}"
                          CornerRadius="{TemplateBinding CornerRadius}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>

                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="CommonStates">
                                <VisualState x:Name="Normal">
                                    <Storyboard>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ContentPresenter"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderHeaderForeground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ToggleButtonGrid"
                                                                       Storyboard.TargetProperty="BorderBrush">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ReactorSettingsCardBorderBrush}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpandCollapseChevron"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderChevronForeground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ToggleButtonGrid"
                                                                       Storyboard.TargetProperty="Background">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ReactorSettingsCardBackground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                    </Storyboard>
                                </VisualState>
                                <VisualState x:Name="PointerOver">
                                    <Storyboard>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ContentPresenter"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderHeaderForegroundPointerOver}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ToggleButtonGrid"
                                                                       Storyboard.TargetProperty="BorderBrush">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ReactorSettingsCardBorderBrushPointerOver}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpandCollapseChevron"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderChevronPointerOverForeground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ToggleButtonGrid"
                                                                       Storyboard.TargetProperty="Background">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ReactorSettingsCardBackgroundPointerOver}" />
                                        </ObjectAnimationUsingKeyFrames>
                                    </Storyboard>
                                    <VisualState.Setters>
                                        <Setter Target="ExpandCollapseChevron.Tag" Value="PointerOverOff" />
                                    </VisualState.Setters>
                                </VisualState>
                                <VisualState x:Name="Pressed">
                                    <Storyboard>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ContentPresenter"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderHeaderForegroundPressed}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ToggleButtonGrid"
                                                                       Storyboard.TargetProperty="BorderBrush">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ReactorSettingsCardBorderBrushPressed}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpandCollapseChevron"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderChevronPressedForeground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ToggleButtonGrid"
                                                                       Storyboard.TargetProperty="Background">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ReactorSettingsCardBackgroundPressed}" />
                                        </ObjectAnimationUsingKeyFrames>
                                    </Storyboard>
                                    <VisualState.Setters>
                                        <Setter Target="ExpandCollapseChevron.Tag" Value="PressedOff" />
                                    </VisualState.Setters>
                                </VisualState>
                                <VisualState x:Name="Disabled">
                                    <Storyboard>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ContentPresenter"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderHeaderDisabledForeground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ToggleButtonGrid"
                                                                       Storyboard.TargetProperty="BorderBrush">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ReactorSettingsCardBorderBrushDisabled}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpandCollapseChevron"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderHeaderDisabledForeground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                    </Storyboard>
                                </VisualState>
                                <VisualState x:Name="Checked">
                                    <Storyboard>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ContentPresenter"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderHeaderForeground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpandCollapseChevron"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderChevronForeground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpandCollapseChevronBorder"
                                                                       Storyboard.TargetProperty="BorderBrush">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderChevronBorderBrush}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpandCollapseChevronBorder"
                                                                       Storyboard.TargetProperty="Background">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderChevronBackground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                    </Storyboard>
                                    <VisualState.Setters>
                                        <Setter Target="ExpandCollapseChevron.Tag" Value="NormalOn" />
                                    </VisualState.Setters>
                                </VisualState>
                                <VisualState x:Name="CheckedPointerOver">
                                    <Storyboard>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ContentPresenter"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderHeaderForegroundPointerOver}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpandCollapseChevron"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderChevronPointerOverForeground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ToggleButtonGrid"
                                                                       Storyboard.TargetProperty="BorderBrush">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ReactorSettingsCardBorderBrushPointerOver}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ToggleButtonGrid"
                                                                       Storyboard.TargetProperty="Background">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ReactorSettingsCardBackgroundPointerOver}" />
                                        </ObjectAnimationUsingKeyFrames>
                                    </Storyboard>
                                    <VisualState.Setters>
                                        <Setter Target="ExpandCollapseChevron.Tag" Value="PointerOverOn" />
                                    </VisualState.Setters>
                                </VisualState>
                                <VisualState x:Name="CheckedPressed">
                                    <Storyboard>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ContentPresenter"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderHeaderForegroundPressed}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpandCollapseChevron"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderChevronPressedForeground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ToggleButtonGrid"
                                                                       Storyboard.TargetProperty="BorderBrush">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ReactorSettingsCardBorderBrushPressed}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ToggleButtonGrid"
                                                                       Storyboard.TargetProperty="Background">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ReactorSettingsCardBackgroundPressed}" />
                                        </ObjectAnimationUsingKeyFrames>
                                    </Storyboard>
                                    <VisualState.Setters>
                                        <Setter Target="ExpandCollapseChevron.Tag" Value="PressedOn" />
                                    </VisualState.Setters>
                                </VisualState>

                                <VisualState x:Name="CheckedDisabled">
                                    <Storyboard>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ContentPresenter"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderHeaderDisabledForeground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpandCollapseChevron"
                                                                       Storyboard.TargetProperty="Foreground">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ExpanderHeaderDisabledForeground}" />
                                        </ObjectAnimationUsingKeyFrames>
                                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ToggleButtonGrid"
                                                                       Storyboard.TargetProperty="BorderBrush">
                                            <DiscreteObjectKeyFrame KeyTime="0"
                                                                    Value="{ThemeResource ReactorSettingsCardBorderBrushDisabled}" />
                                        </ObjectAnimationUsingKeyFrames>
                                    </Storyboard>
                                    <VisualState.Setters>
                                        <Setter Target="ExpandCollapseChevron.Tag" Value="NormalOn" />
                                    </VisualState.Setters>
                                </VisualState>
                                <VisualState x:Name="Indeterminate" />
                                <VisualState x:Name="IndeterminatePointerOver" />
                                <VisualState x:Name="IndeterminatePressed" />
                                <VisualState x:Name="IndeterminateDisabled" />
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>

                        <ContentPresenter x:Name="ContentPresenter"
                                          HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}"
                                          VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}"
                                          win:AutomationProperties.AccessibilityView="Raw"
                                          Content="{TemplateBinding Content}"
                                          ContentTemplate="{TemplateBinding ContentTemplate}"
                                          ContentTransitions="{TemplateBinding ContentTransitions}"
                                          Foreground="{TemplateBinding Foreground}" />

                        <ContentControl x:Name="ExpandCollapseChevronBorder"
                                        Grid.Column="1"
                                        Width="32"
                                        Height="32"
                                        Margin="0,0,8,0"
                                        HorizontalAlignment="Center"
                                        VerticalAlignment="Center"
                                        HorizontalContentAlignment="Center"
                                        VerticalContentAlignment="Center"
                                        Background="{ThemeResource ExpanderChevronBackground}"
                                        BorderBrush="{ThemeResource ExpanderChevronBorderBrush}"
                                        BorderThickness="{ThemeResource ExpanderChevronBorderThickness}"
                                        CornerRadius="{ThemeResource ControlCornerRadius}"
                                        FocusVisualMargin="-3"
                                        IsTabStop="False"
                                        ToolTipService.ToolTip="Show all settings"
                                        UseSystemFocusVisuals="True">
                            <muxc:AnimatedIcon x:Name="ExpandCollapseChevron"
                                               Width="16"
                                               Height="16"
                                               HorizontalAlignment="Center"
                                               VerticalAlignment="Center"
                                               Tag="NormalOff"
                                               win:AutomationProperties.AccessibilityView="Raw"
                                               Foreground="{ThemeResource ExpanderChevronForeground}"
                                               RenderTransformOrigin="0.5, 0.5">
                                <animatedvisuals:AnimatedChevronUpDownSmallVisualSource />
                                <muxc:AnimatedIcon.FallbackIconSource>
                                    <muxc:FontIconSource FontFamily="{StaticResource SymbolThemeFontFamily}"
                                                         FontSize="16"
                                                         Glyph="{StaticResource ExpanderChevronDownGlyph}"
                                                         IsTextScaleFactorEnabled="False" />
                                </muxc:AnimatedIcon.FallbackIconSource>
                            </muxc:AnimatedIcon>
                        </ContentControl>
                    </Grid>

                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="ReactorSettingsExpanderExpanderStyle"
           TargetType="muxc:Expander">
        <Setter Property="Background" Value="{ThemeResource ExpanderContentBackground}" />
        <Setter Property="BackgroundSizing" Value="InnerBorderEdge" />
        <Setter Property="MinWidth" Value="{ThemeResource FlyoutThemeMinWidth}" />
        <Setter Property="MinHeight" Value="{StaticResource ExpanderMinHeight}" />
        <Setter Property="BorderThickness" Value="{ThemeResource ExpanderContentDownBorderThickness}" />
        <Setter Property="BorderBrush" Value="{ThemeResource ExpanderContentBorderBrush}" />
        <Setter Property="Padding" Value="0" />
        <Setter Property="HorizontalAlignment" Value="Left" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="CornerRadius" Value="{ThemeResource ControlCornerRadius}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="muxc:Expander">
                    <Grid MinWidth="{TemplateBinding MinWidth}"
                          MaxWidth="{TemplateBinding MaxWidth}">
                        <Grid.RowDefinitions>
                            <RowDefinition x:Name="Row0"
                                           Height="Auto" />
                            <RowDefinition x:Name="Row1"
                                           Height="*" />
                        </Grid.RowDefinitions>
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="ExpandStates">
                                <VisualState x:Name="ExpandUp">
                                    <VisualState.Setters>
                                        <Setter Target="ExpanderHeader.CornerRadius" Value="{Binding CornerRadius, RelativeSource={RelativeSource TemplatedParent}, Converter={StaticResource BottomCornerRadiusFilterConverter}}" />
                                    </VisualState.Setters>
                                    <VisualState.Storyboard>
                                        <Storyboard>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpanderContent"
                                                                           Storyboard.TargetProperty="Visibility">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="Visible" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="ExpanderContent"
                                                                           Storyboard.TargetProperty="(UIElement.RenderTransform).(CompositeTransform.TranslateY)">
                                                <DiscreteDoubleKeyFrame KeyTime="0"
                                                                        Value="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=TemplateSettings.ContentHeight}" />
                                                <SplineDoubleKeyFrame KeySpline="0.0, 0.0, 0.0, 1.0"
                                                                      KeyTime="0:0:0.333"
                                                                      Value="0" />
                                            </DoubleAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState.Storyboard>
                                </VisualState>
                                <VisualState x:Name="CollapseDown">
                                    <VisualState.Storyboard>
                                        <Storyboard>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpanderContent"
                                                                           Storyboard.TargetProperty="Visibility">
                                                <DiscreteObjectKeyFrame KeyTime="0:0:0.2"
                                                                        Value="Collapsed" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="ExpanderContent"
                                                                           Storyboard.TargetProperty="(UIElement.RenderTransform).(CompositeTransform.TranslateY)">
                                                <DiscreteDoubleKeyFrame KeyTime="0"
                                                                        Value="0" />
                                                <SplineDoubleKeyFrame KeySpline="1.0, 1.0, 0.0, 1.0"
                                                                      KeyTime="0:0:0.167"
                                                                      Value="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=TemplateSettings.ContentHeight}" />
                                            </DoubleAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState.Storyboard>
                                </VisualState>
                                <VisualState x:Name="ExpandDown">
                                    <VisualState.Setters>
                                        <Setter Target="ExpanderHeader.CornerRadius" Value="{Binding CornerRadius, RelativeSource={RelativeSource TemplatedParent}, Converter={StaticResource TopCornerRadiusFilterConverter}}" />
                                    </VisualState.Setters>
                                    <VisualState.Storyboard>
                                        <Storyboard>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpanderContent"
                                                                           Storyboard.TargetProperty="Visibility">
                                                <DiscreteObjectKeyFrame KeyTime="0"
                                                                        Value="Visible" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="ExpanderContent"
                                                                           Storyboard.TargetProperty="(UIElement.RenderTransform).(CompositeTransform.TranslateY)">
                                                <DiscreteDoubleKeyFrame KeyTime="0"
                                                                        Value="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=TemplateSettings.NegativeContentHeight}" />
                                                <SplineDoubleKeyFrame KeySpline="0.0, 0.0, 0.0, 1.0"
                                                                      KeyTime="0:0:0.333"
                                                                      Value="0" />
                                            </DoubleAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState.Storyboard>
                                </VisualState>
                                <VisualState x:Name="CollapseUp">
                                    <VisualState.Storyboard>
                                        <Storyboard>
                                            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="ExpanderContent"
                                                                           Storyboard.TargetProperty="Visibility">
                                                <DiscreteObjectKeyFrame KeyTime="0:0:0.167"
                                                                        Value="Collapsed" />
                                            </ObjectAnimationUsingKeyFrames>
                                            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="ExpanderContent"
                                                                           Storyboard.TargetProperty="(UIElement.RenderTransform).(CompositeTransform.TranslateY)">
                                                <DiscreteDoubleKeyFrame KeyTime="0"
                                                                        Value="0" />
                                                <SplineDoubleKeyFrame KeySpline="1.0, 1.0, 0.0, 1.0"
                                                                      KeyTime="0:0:0.167"
                                                                      Value="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=TemplateSettings.NegativeContentHeight}" />
                                            </DoubleAnimationUsingKeyFrames>
                                        </Storyboard>
                                    </VisualState.Storyboard>
                                </VisualState>
                            </VisualStateGroup>
                            <VisualStateGroup x:Name="ExpandDirectionStates">
                                <VisualState x:Name="Down" />
                                <VisualState x:Name="Up">
                                    <VisualState.Setters>
                                        <Setter Target="ExpanderHeader.Style" Value="{StaticResource ExpanderHeaderUpStyle}" />
                                        <Setter Target="ExpanderContent.BorderThickness" Value="{StaticResource ExpanderContentUpBorderThickness}" />
                                        <Setter Target="ExpanderContent.CornerRadius" Value="{Binding CornerRadius, RelativeSource={RelativeSource TemplatedParent}, Converter={StaticResource TopCornerRadiusFilterConverter}}" />
                                        <Setter Target="ExpanderHeader.(Grid.Row)" Value="1" />
                                        <Setter Target="ExpanderContentClip.(Grid.Row)" Value="0" />
                                        <Setter Target="Row0.Height" Value="*" />
                                        <Setter Target="Row1.Height" Value="Auto" />
                                    </VisualState.Setters>
                                </VisualState>
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>
                        <ToggleButton x:Name="ExpanderHeader"
                                      MinHeight="{TemplateBinding MinHeight}"
                                      Padding="{StaticResource ExpanderHeaderPadding}"
                                      HorizontalAlignment="Stretch"
                                      HorizontalContentAlignment="{StaticResource ExpanderHeaderHorizontalContentAlignment}"
                                      VerticalContentAlignment="{StaticResource ExpanderHeaderVerticalContentAlignment}"
                                      win:AutomationProperties.AutomationId="ExpanderToggleButton"
                                      win:AutomationProperties.Name="{TemplateBinding AutomationProperties.Name}"
                                      BackgroundSizing="{TemplateBinding BackgroundSizing}"
                                      BorderBrush="{ThemeResource ExpanderHeaderBorderBrush}"
                                      BorderThickness="{ThemeResource ExpanderHeaderBorderThickness}"
                                      Content="{TemplateBinding Header}"
                                      ContentTemplate="{TemplateBinding HeaderTemplate}"
                                      ContentTemplateSelector="{TemplateBinding HeaderTemplateSelector}"
                                      CornerRadius="{TemplateBinding CornerRadius}"
                                      IsChecked="{Binding Path=IsExpanded, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"
                                      IsEnabled="{TemplateBinding IsEnabled}"
                                      IsTabStop="True"
                                      Style="{StaticResource ReactorSettingsExpanderHeaderDownStyle}" />
                        <!--  The clip is a composition clip applied in code  -->
                        <Border x:Name="ExpanderContentClip"
                                Grid.Row="1">
                            <Border x:Name="ExpanderContent"
                                    MinHeight="16"
                                    Padding="{TemplateBinding Padding}"
                                    HorizontalAlignment="Stretch"
                                    VerticalAlignment="Stretch"
                                    BackgroundSizing="{TemplateBinding BackgroundSizing}"
                                    BorderBrush="{TemplateBinding BorderBrush}"
                                    BorderThickness="{StaticResource ExpanderContentDownBorderThickness}"
                                    CornerRadius="{Binding CornerRadius, RelativeSource={RelativeSource TemplatedParent}, Converter={StaticResource BottomCornerRadiusFilterConverter}}"
                                    Visibility="Collapsed">
                                <ContentPresenter Margin="0,-2,0,0"
                                                  HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}"
                                                  VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}"
                                                  Content="{TemplateBinding Content}"
                                                  ContentTemplate="{TemplateBinding ContentTemplate}"
                                                  ContentTemplateSelector="{TemplateBinding ContentTemplateSelector}" />
                                <Border.RenderTransform>
                                    <CompositeTransform />
                                </Border.RenderTransform>
                            </Border>
                        </Border>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>
""";
}