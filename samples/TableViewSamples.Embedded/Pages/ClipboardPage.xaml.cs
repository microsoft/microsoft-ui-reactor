// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;
using Windows.ApplicationModel.DataTransfer;

namespace TableViewSamples.Pages;

/// <summary>
/// Clipboard / Excel-style operations sample (P3.10). Demonstrates the
/// Ctrl+C / Ctrl+V / Ctrl+X / Ctrl+D shortcut surface plus the gating DPs
/// and the cancellable Copying / Cutting / Pasting events.
/// </summary>
public sealed partial class ClipboardPage : Page
{
    private readonly List<Person> _master;
    private int _copyingCount;
    private int _cuttingCount;
    private int _pastingCount;

    public ClipboardPage()
    {
        InitializeComponent();
        _master = PersonData.Take(40).ToList();
        People = new ObservableCollection<Person>(_master);
    }

    public ObservableCollection<Person> People { get; }

    // --- Toggles --------------------------------------------------------

    private void OnCanUserCopyToggled(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is not null)
        {
            PeopleTable.CanUserCopy = CanUserCopySwitch.IsOn;
        }
    }

    private void OnCanUserPasteToggled(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is not null)
        {
            PeopleTable.CanUserPaste = CanUserPasteSwitch.IsOn;
        }
    }

    private void OnCanUserCutToggled(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is not null)
        {
            PeopleTable.CanUserCut = CanUserCutSwitch.IsOn;
        }
    }

    // --- Buttons (also exercise the API surface programmatically) ------

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var ok = PeopleTable.CopyToClipboard();
        LastActionText.Text = ok
            ? "CopyToClipboard() — true"
            : "CopyToClipboard() — false (gated or empty selection)";
    }

    private void OnCutClick(object sender, RoutedEventArgs e)
    {
        var ok = PeopleTable.CutToClipboard();
        LastActionText.Text = ok
            ? "CutToClipboard() — true"
            : "CutToClipboard() — false (gated or empty selection)";
    }

    private async void OnPasteClick(object sender, RoutedEventArgs e)
    {
        var ok = await PeopleTable.PasteFromClipboardAsync();
        LastActionText.Text = ok
            ? "PasteFromClipboardAsync() — true"
            : "PasteFromClipboardAsync() — false (gated, empty clipboard, or clipboard is not text)";
    }

    private void OnFillDownClick(object sender, RoutedEventArgs e)
    {
        var ok = PeopleTable.FillDown();
        LastActionText.Text = ok
            ? "FillDown() — true"
            : "FillDown() — false (gated or fewer than two selected rows)";
    }

    // --- Events ---------------------------------------------------------

    private void OnCopying(TableView sender, TableViewClipboardEventArgs args)
    {
        _copyingCount++;
        CopyingCountText.Text = _copyingCount.ToString();

        if (args.DataPackage?.GetView() is { } view && view.Contains(StandardDataFormats.Text))
        {
            // GetView+Contains is sync but GetTextAsync is the documented way to read the
            // payload; keep this side-effect-free so the real clipboard receives what we
            // built (see Pasting handler for the full async-safe pattern). We just preview
            // the first row in the readout.
            var snapshot = SnapshotPackageText(args.DataPackage);
            LastClipboardText.Text = snapshot;
        }
    }

    private void OnCutting(TableView sender, TableViewClipboardEventArgs args)
    {
        _cuttingCount++;
        CuttingCountText.Text = _cuttingCount.ToString();

        var snapshot = SnapshotPackageText(args.DataPackage);
        LastClipboardText.Text = snapshot;
    }

    private void OnPasting(TableView sender, TableViewPasteEventArgs args)
    {
        _pastingCount++;
        PastingCountText.Text = _pastingCount.ToString();
        LastClipboardText.Text = args.Text ?? string.Empty;

        // Demonstrate args.Text rewrite: trim leading/trailing whitespace per cell so a
        // user pasting from Word doesn't get NBSPs into the table. Real consumers would
        // do schema-aware coercion here.
        var trimmed = TrimEachCell(args.Text);
        if (!string.Equals(trimmed, args.Text, StringComparison.Ordinal))
        {
            args.Text = trimmed;
        }
    }

    private static string SnapshotPackageText(DataPackage? package)
    {
        if (package is null) return string.Empty;
        try
        {
            var view = package.GetView();
            if (view.Contains(StandardDataFormats.Text))
            {
                return view.GetTextAsync().AsTask().GetAwaiter().GetResult() ?? string.Empty;
            }
        }
        catch
        {
            // Best-effort preview only — the actual clipboard write is not affected.
        }
        return string.Empty;
    }

    private static string TrimEachCell(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var rows = text.Replace("\r\n", "\n").Split('\n');
        for (var r = 0; r < rows.Length; r++)
        {
            var cells = rows[r].Split('\t');
            for (var c = 0; c < cells.Length; c++)
            {
                cells[c] = cells[c].Trim();
            }
            rows[r] = string.Join('\t', cells);
        }
        return string.Join("\r\n", rows);
    }
}
