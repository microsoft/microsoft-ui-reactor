// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TableViewSamples.Data;
using TableViewSamples.Models;
using Windows.UI;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates the public per-row brush DPs:
///   * RowBackground               — even-row background brush
///   * AlternatingRowBackground    — odd-row background brush
///   * RowForeground               — even-row text brush
///   * AlternatingRowForeground    — odd-row text brush
///
/// All four DPs default to null. With nothing set, TableView writes no
/// per-row Background — banding is OPT-IN so the rest of the sample (and any
/// host that hasn't asked for banding) renders cleanly. Set both background
/// DPs to enable zebra striping; set the matching foreground DPs to swap
/// text color on alternating rows.
/// </summary>
public sealed partial class RowColorsPage : Page
{
    public RowColorsPage()
    {
        InitializeComponent();
        People = new ObservableCollection<Person>(PersonData.Take(60));
    }

    public ObservableCollection<Person> People { get; }

    private void ApplyDefaultBanding()
    {
        if (PeopleTable is null)
        {
            return;
        }

        // Apply the control-shipped Style. The Style's Setter VALUEs use
        // {ThemeResource TableViewDefault*Brush}, so the framework re-resolves
        // brushes against PeopleTable.ActualTheme on Light↔Dark switches —
        // tracking the live root-theme flip from the Theme & settings page.
        // (Application.Current.Resources brush lookup would NOT track theme
        // because it resolves against Application.RequestedTheme, which is
        // immutable post-startup. See TableViewCellWrapperStyle precedent
        // comment in controls/dev/TableView/TableView_themeresources.xaml.)
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(
                "TableViewDefaultBandingStyle", out var styleObj) &&
            styleObj is Style style)
        {
            PeopleTable.Style = style;
        }
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeopleTable is null)
        {
            return;
        }

        ClearAllRowColorDPs();

        switch (PresetPicker.SelectedIndex)
        {
            case 0:
                // Default banding — theme-aware: white/light-grey + dark text in Light,
                // charcoal/lighter-charcoal + white text in Dark. Resolved at apply time
                // off ActualTheme; ActualThemeChanged (wired in the ctor) re-runs this
                // case so the table tracks live theme switches from the Theme page.
                ApplyDefaultBanding();
                break;

            case 1:
                // No banding — DPs already cleared above; nothing else to do.
                break;

            case 2:
                // Sky blue + cloud white banding. Pair a dark foreground so the
                // light bands stay legible in Dark theme, where the default cell
                // foreground is near-white and would vanish on these backgrounds.
                PeopleTable.RowBackground = MakeBrush(245, 249, 252);
                PeopleTable.AlternatingRowBackground = MakeBrush(214, 234, 248);
                PeopleTable.RowForeground = MakeBrush(32, 32, 32);
                PeopleTable.AlternatingRowForeground = MakeBrush(32, 32, 32);
                break;

            case 3:
                // Disable banding — both backgrounds resolve to the same brush.
                // Pair a dark foreground so the single light band stays legible
                // in Dark theme (default cell foreground is near-white there).
                var solid = MakeBrush(235, 235, 235);
                PeopleTable.RowBackground = solid;
                PeopleTable.AlternatingRowBackground = solid;
                PeopleTable.RowForeground = MakeBrush(32, 32, 32);
                PeopleTable.AlternatingRowForeground = MakeBrush(32, 32, 32);
                break;

            case 4:
                // Salary-emphasis: gold text on even, white text on odd, with
                // a charcoal banding so both reads stay legible.
                PeopleTable.RowBackground = MakeBrush(40, 40, 50);
                PeopleTable.AlternatingRowBackground = MakeBrush(55, 55, 65);
                PeopleTable.RowForeground = MakeBrush(255, 215, 0);
                PeopleTable.AlternatingRowForeground = MakeBrush(255, 255, 255);
                break;

            case 5:
                // Dark contrast theme.
                PeopleTable.RowBackground = new SolidColorBrush(Colors.Black);
                PeopleTable.AlternatingRowBackground = MakeBrush(33, 33, 33);
                PeopleTable.RowForeground = new SolidColorBrush(Colors.White);
                PeopleTable.AlternatingRowForeground = new SolidColorBrush(Colors.White);
                break;
        }
    }

    private void ClearAllRowColorDPs()
    {
        // ClearValue (NOT `= null`) is required so the Default banding Style's
        // Setters can take effect. Setting the property to null sets a LOCAL
        // value of null, which trumps Style.Setter per WinUI DP precedence.
        PeopleTable.Style = null;
        PeopleTable.ClearValue(TableView.RowBackgroundProperty);
        PeopleTable.ClearValue(TableView.AlternatingRowBackgroundProperty);
        PeopleTable.ClearValue(TableView.RowForegroundProperty);
        PeopleTable.ClearValue(TableView.AlternatingRowForegroundProperty);
    }

    private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
    {
        return new SolidColorBrush(Color.FromArgb(0xFF, r, g, b));
    }
}
