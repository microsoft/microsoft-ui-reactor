using Microsoft.UI.Xaml;

namespace ReactorRegedit.Native;

/// <summary>App-wide resource setup.</summary>
internal static class AppTheme
{
    /// <summary>
    /// F01 — WinUI compact sizing for every control (menus, text boxes, buttons,
    /// dialogs). Must run before the first control is created so styles pick it up.
    /// </summary>
    public static void ApplyCompactDensity()
    {
        var resources = Application.Current.Resources;
        try
        {
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("ms-appx:///Microsoft.UI.Xaml/DensityStyles/Compact.xaml"),
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Compact density dictionary unavailable: {ex.Message}");
        }

        // A few sizes Compact.xaml leaves at their touch-friendly defaults. The app
        // dictionary has a Source, so overrides go in their own merged dictionary.
        var overrides = new ResourceDictionary
        {
            ["MenuBarHeight"] = 30.0,
            ["MenuBarItemButtonPadding"] = new Thickness(8, 0, 8, 0),
            ["MenuFlyoutItemThemePaddingNarrow"] = new Thickness(11, 4, 11, 5),
            ["MenuFlyoutItemThemePadding"] = new Thickness(11, 5, 11, 6),
            ["TextControlThemeMinHeight"] = 24.0,
            ["ContentDialogPadding"] = new Thickness(20),
        };
        resources.MergedDictionaries.Add(overrides);
    }

    private static Style? s_compactRadio;

    /// <summary>
    /// A 24-epx RadioButton. The stock template pins its glyph area to 32 epx, which no resource
    /// override can shrink, so dialogs use this small template instead (same theme brushes,
    /// keyboard behaviour and system focus visuals).
    /// </summary>
    public static Style CompactRadioButtonStyle => s_compactRadio ??= (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(
        """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="RadioButton">
          <Setter Property="MinWidth" Value="0" />
          <Setter Property="MinHeight" Value="0" />
          <Setter Property="Height" Value="24" />
          <Setter Property="Padding" Value="6,0,0,0" />
          <Setter Property="Background" Value="Transparent" />
          <Setter Property="Foreground" Value="{ThemeResource RadioButtonForeground}" />
          <Setter Property="UseSystemFocusVisuals" Value="True" />
          <Setter Property="FocusVisualMargin" Value="-4,0" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="RadioButton">
                <Grid x:Name="Root" Background="{TemplateBinding Background}">
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="18" />
                    <ColumnDefinition Width="*" />
                  </Grid.ColumnDefinitions>
                  <VisualStateManager.VisualStateGroups>
                    <VisualStateGroup x:Name="CommonStates">
                      <VisualState x:Name="Normal" />
                      <VisualState x:Name="PointerOver">
                        <VisualState.Setters>
                          <Setter Target="Outer.Fill" Value="{ThemeResource RadioButtonOuterEllipseFillPointerOver}" />
                        </VisualState.Setters>
                      </VisualState>
                      <VisualState x:Name="Pressed">
                        <VisualState.Setters>
                          <Setter Target="Outer.Fill" Value="{ThemeResource RadioButtonOuterEllipseFillPressed}" />
                        </VisualState.Setters>
                      </VisualState>
                      <VisualState x:Name="Disabled">
                        <VisualState.Setters>
                          <Setter Target="Root.Opacity" Value="0.45" />
                        </VisualState.Setters>
                      </VisualState>
                    </VisualStateGroup>
                    <VisualStateGroup x:Name="CheckStates">
                      <VisualState x:Name="Checked">
                        <VisualState.Setters>
                          <Setter Target="Outer.Fill" Value="{ThemeResource RadioButtonOuterEllipseCheckedFill}" />
                          <Setter Target="Outer.Stroke" Value="{ThemeResource RadioButtonOuterEllipseCheckedStroke}" />
                          <Setter Target="Dot.Opacity" Value="1" />
                        </VisualState.Setters>
                      </VisualState>
                      <VisualState x:Name="Unchecked" />
                      <VisualState x:Name="Indeterminate" />
                    </VisualStateGroup>
                  </VisualStateManager.VisualStateGroups>
                  <Ellipse x:Name="Outer" Width="16" Height="16" StrokeThickness="1" VerticalAlignment="Center"
                           Fill="{ThemeResource RadioButtonOuterEllipseFill}" Stroke="{ThemeResource RadioButtonOuterEllipseStroke}" />
                  <Ellipse x:Name="Dot" Width="6" Height="6" Opacity="0" VerticalAlignment="Center"
                           Fill="{ThemeResource RadioButtonCheckGlyphFill}" />
                  <ContentPresenter Grid.Column="1" Margin="{TemplateBinding Padding}" VerticalAlignment="Center"
                                    Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}"
                                    Foreground="{TemplateBinding Foreground}" TextWrapping="NoWrap" />
                </Grid>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);}
