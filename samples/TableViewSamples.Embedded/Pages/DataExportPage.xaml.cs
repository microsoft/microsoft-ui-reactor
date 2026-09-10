// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using TableViewSamples.Data;
using TableViewSamples.Models;
using Windows.ApplicationModel.DataTransfer;

namespace TableViewSamples.Pages;

/// <summary>
/// Rolls a simple data-export workflow on top of TableView. CSV / TSV use
/// <c>TableView.GetDataAsText</c>; JSON demonstrates a custom projection over
/// the page's <c>ItemsSource</c> for formats the built-in text formatter does
/// not cover.
/// </summary>
public sealed partial class DataExportPage : Page
{
    private string? _lastExportedPath;

    public DataExportPage()
    {
        InitializeComponent();
        People = PersonData.Take(50);
    }

    public ObservableCollection<Person> People { get; }

    private string SelectedFormat => FormatRadios?.SelectedIndex switch
    {
        1 => "TSV",
        2 => "JSON",
        _ => "CSV",
    };

    private static string ExtensionFor(string format) => format switch
    {
        "TSV" => ".tsv",
        "JSON" => ".json",
        _ => ".csv",
    };

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var format = SelectedFormat;
        string text;
        try
        {
            text = format switch
            {
                "JSON" => BuildJson(),
                "TSV"  => PeopleTable.GetDataAsText(TableViewDataFormat.TabSeparated, includeHeaders: true),
                _      => PeopleTable.GetDataAsText(TableViewDataFormat.CommaSeparated, includeHeaders: true),
            };
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
            return;
        }

        // Write to %TEMP%\tableview-export.<ext>
        var path = Path.Combine(Path.GetTempPath(), "tableview-export" + ExtensionFor(format));
        string? fileError = null;
        try
        {
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _lastExportedPath = path;
            OpenInExplorerButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            fileError = ex.Message;
            _lastExportedPath = null;
            OpenInExplorerButton.IsEnabled = false;
        }

        // Also stage on clipboard so users can paste into Excel / VS Code.
        var copiedToClipboard = false;
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            copiedToClipboard = true;
        }
        catch
        {
            // Clipboard may briefly fail on rapid clicks; the file copy is the
            // authoritative output so we just swallow.
        }

        ExportPreviewText.Text = FirstLines(text, 30);

        var statusFile = _lastExportedPath is null ? "(file write skipped)" : _lastExportedPath;
        StatusText.Text = (fileError, copiedToClipboard) switch
        {
            (not null, true) => $"Copied {People.Count} rows to clipboard, but file write failed: {fileError}",
            (not null, false) => $"Exported {People.Count} rows as {format}, but file write failed: {fileError} and clipboard copy failed.",
            (null, true) => $"Exported {People.Count} rows as {format}. File: {statusFile}. Also copied to clipboard.",
            _ => $"Exported {People.Count} rows as {format}. File: {statusFile}. Clipboard copy failed.",
        };
    }

    private void OnOpenInExplorerClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastExportedPath) || !File.Exists(_lastExportedPath))
        {
            StatusText.Text = "Nothing exported yet — click Export first.";
            return;
        }

        try
        {
            // /select, highlights the file in a new Explorer window.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastExportedPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open Explorer: {ex.Message}";
        }
    }

    /// <summary>
    /// Builds a JSON representation by walking <see cref="TableView.Columns"/>
    /// and the page's <c>People</c> source. (CSV/TSV are emitted via the
    /// built-in <c>TableView.GetDataAsText</c> formatter, which handles
    /// TemplateColumn/CheckBoxColumn correctly. JSON is not part of the
    /// public formatter set, so we still build it here.)
    /// </summary>
    private string BuildJson()
    {
        var columns = CollectColumns();
        var sb = new StringBuilder();
        sb.Append('[');
        sb.Append('\n');
        bool firstRow = true;
        foreach (var person in People)
        {
            if (!firstRow) sb.Append(",\n");
            firstRow = false;
            sb.Append("  {");
            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(JsonEscape(columns[i].PropertyName)).Append("\": \"")
                  .Append(JsonEscape(GetCellText(person, columns[i].PropertyName)))
                  .Append('"');
            }
            sb.Append('}');
        }
        sb.Append('\n').Append(']').Append('\n');
        return sb.ToString();
    }

    private System.Collections.Generic.List<(string Header, string PropertyName)> CollectColumns()
    {
        var result = new System.Collections.Generic.List<(string, string)>();
        if (PeopleTable is null) return result;
        foreach (var col in PeopleTable.Columns)
        {
            if (col is TableViewTextColumn tc && tc.Binding is Binding b && b.Path is not null && !string.IsNullOrEmpty(b.Path.Path))
            {
                result.Add(((string)tc.Header, b.Path.Path));
            }
        }
        return result;
    }

    private static string GetCellText(Person person, string propertyName)
    {
        var prop = typeof(Person).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop is null) return string.Empty;
        var raw = prop.GetValue(person);
        return raw switch
        {
            null              => string.Empty,
            DateTimeOffset dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f    => f.ToString(null, CultureInfo.InvariantCulture),
            _                 => raw.ToString() ?? string.Empty,
        };
    }

    private static string JsonEscape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static string FirstLines(string text, int maxLines)
    {
        int count = 0;
        int idx = 0;
        while (count < maxLines && idx < text.Length)
        {
            int next = text.IndexOf('\n', idx);
            if (next < 0) return text;
            idx = next + 1;
            count++;
        }
        var truncated = text.Substring(0, idx);
        return truncated + (idx < text.Length ? "…\n" : string.Empty);
    }
}
