// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView's P3.7 persisted layout state — SaveLayoutToJson /
/// LoadLayoutFromJson.
///
///   * The Save button captures the visible TableView's column order, width,
///     frozen edge and per-column sort state into a versioned JSON token,
///     written to the round-trip TextBox. An app would persist this string
///     in its app-data store so the user's customizations survive across
///     sessions.
///
///   * The Load button takes the current TextBox content and applies it
///     back onto the TableView. Match key is Header.ToString() (with
///     SortMemberPath as a fallback). Saved entries with no live counterpart
///     are silently skipped; live columns whose key isn't in the saved set
///     are left untouched at the tail of Columns.
///
///   * The Reset button re-creates the column collection from scratch so we
///     have a clean baseline to verify the round-trip against.
///
///   * Filter expression is intentionally NOT serialized in v1 — hasFilter
///     is a placeholder so consumers can re-prompt for the filter UI on
///     restore.
///
///   * Throwing-on-bad-input behaviour: malformed JSON or an unknown schema
///     version raises hresult_invalid_argument (surfaced here as
///     ArgumentException). Edit the token to "version":99 to see the gate
///     fire.
/// </summary>
public sealed partial class LayoutPage : Page
{
    public LayoutPage()
    {
        People = new ObservableCollection<PersonRow>();
        InitializeComponent();
        SeedRows();
        UpdateSortStateText();
    }

    public ObservableCollection<PersonRow> People { get; }

    // Seed order, used as the stable source for the consumer-owned re-sort so
    // clearing the sort restores the original ordering.
    private readonly List<PersonRow> _master = new();

    private static readonly Dictionary<string, Func<PersonRow, IComparable?>> s_keySelectors =
        new(StringComparer.Ordinal)
        {
            ["Name"]     = r => r.Name,
            ["Role"]     = r => r.Role,
            ["Team"]     = r => r.Team,
            ["Location"] = r => r.Location,
        };

    private void SeedRows()
    {
        People.Clear();
        People.Add(new PersonRow { Name = "Ada Lovelace",   Role = "Lead",       Team = "Algorithms",    Location = "London" });
        People.Add(new PersonRow { Name = "Alan Turing",    Role = "Cryptographer", Team = "Cipher",      Location = "Bletchley Park" });
        People.Add(new PersonRow { Name = "Grace Hopper",   Role = "Compiler PM", Team = "Tools",         Location = "New York" });
        People.Add(new PersonRow { Name = "Linus Torvalds", Role = "Kernel dev",  Team = "Platform",      Location = "Portland" });
        People.Add(new PersonRow { Name = "Margaret Hamilton", Role = "Software lead", Team = "Apollo",   Location = "Cambridge" });
        People.Add(new PersonRow { Name = "Donald Knuth",   Role = "Author",      Team = "Theory",        Location = "Stanford" });
        People.Add(new PersonRow { Name = "Barbara Liskov", Role = "Researcher",  Team = "Languages",     Location = "Cambridge" });
        People.Add(new PersonRow { Name = "Hedy Lamarr",    Role = "Inventor",    Team = "Spectrum",      Location = "Vienna" });
        People.Add(new PersonRow { Name = "Tim Berners-Lee", Role = "Architect",  Team = "Web",           Location = "Geneva" });
        People.Add(new PersonRow { Name = "Dennis Ritchie", Role = "Language dev", Team = "Systems",      Location = "Murray Hill" });
        People.Add(new PersonRow { Name = "Ken Thompson",   Role = "Systems dev", Team = "Unix",          Location = "Murray Hill" });
        People.Add(new PersonRow { Name = "Edsger Dijkstra", Role = "Researcher", Team = "Algorithms",    Location = "Eindhoven" });
        People.Add(new PersonRow { Name = "Katherine Johnson", Role = "Mathematician", Team = "Trajectory", Location = "Hampton" });
        People.Add(new PersonRow { Name = "Claude Shannon", Role = "Theorist",    Team = "Information",    Location = "Murray Hill" });
        People.Add(new PersonRow { Name = "John McCarthy",  Role = "Researcher",  Team = "AI",            Location = "Stanford" });
        People.Add(new PersonRow { Name = "Radia Perlman",  Role = "Network eng", Team = "Protocols",     Location = "Boston" });
        People.Add(new PersonRow { Name = "Vint Cerf",      Role = "Architect",   Team = "Internet",      Location = "Los Angeles" });
        People.Add(new PersonRow { Name = "Frances Allen",  Role = "Researcher",  Team = "Optimization",  Location = "Yorktown" });
        People.Add(new PersonRow { Name = "Guido van Rossum", Role = "Language dev", Team = "Python",     Location = "Amsterdam" });
        People.Add(new PersonRow { Name = "Bjarne Stroustrup", Role = "Language dev", Team = "C++",       Location = "Murray Hill" });

        _master.Clear();
        _master.AddRange(People);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string token = LayoutTable.SaveLayoutToJson();
            JsonText.Text = token;
            TokenLengthText.Text = $"{token.Length} chars";
            LastActionText.Text = "SaveLayoutToJson — token written to round-trip TextBox.";
        }
        catch (Exception ex)
        {
            LastActionText.Text = $"SaveLayoutToJson threw {ex.GetType().Name}: {ex.Message}";
        }
        UpdateSortStateText();
    }

    private void OnLoadClick(object sender, RoutedEventArgs e)
    {
        string token = JsonText.Text ?? string.Empty;
        try
        {
            LayoutTable.LoadLayoutFromJson(token);
            LastActionText.Text = "LoadLayoutFromJson — token applied. Columns reordered, widths/frozen/sort restored.";
        }
        catch (ArgumentException ex)
        {
            LastActionText.Text = $"LoadLayoutFromJson rejected the token: {ex.Message}";
        }
        catch (Exception ex)
        {
            LastActionText.Text = $"LoadLayoutFromJson threw {ex.GetType().Name}: {ex.Message}";
        }
        ApplySort();
        UpdateSortStateText();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        // 2026-06-06 — replaced rebuild-from-scratch fallback with the three
        // public Reset methods shipped in fix-n5. Snapshot is captured inside
        // the control at Loaded time, so consumers no longer need to keep
        // their own template of the column definitions.
        LayoutTable.ClearSort();
        LayoutTable.ResetColumnOrder();
        LayoutTable.ResetColumnWidths();
        // ResetColumnVisibility removed v1.0 — see Pending-Items.md (N-5);
        // re-enable when TableViewColumn.Visibility lands.
        // LayoutTable.ResetColumnVisibility();
        LastActionText.Text = "Reset — column order/widths restored, sort cleared.";
        ApplySort();
        UpdateSortStateText();
    }

    private void OnToggleFrozenClick(object sender, RoutedEventArgs e)
    {
        if (LayoutTable.Columns.Count == 0)
        {
            return;
        }
        var first = LayoutTable.Columns[0];
        var newEdge = first.FrozenEdge == TableViewFrozenEdge.Leading ? TableViewFrozenEdge.None : TableViewFrozenEdge.Leading;
        first.FrozenEdge = newEdge;
        ToggleFrozenButton.Content = newEdge == TableViewFrozenEdge.Leading
            ? "Unfreeze leading column"
            : "Freeze leading column";
        LastActionText.Text = $"Toggled FrozenEdge on '{first.Header}' to {newEdge}.";
    }

    // The control owns sort STATE (SortDirection / SortIndex + header glyphs);
    // the consumer owns the DATA. When Sorted fires, walk SortedColumns in
    // priority order and re-shape People so the rows visibly reorder — the same
    // consumer-owned re-shape model demonstrated on SortPage.
    private void OnTableSorted(TableView sender, TableViewSortedEventArgs args)
    {
        ApplySort();
        UpdateSortStateText();
    }

    private void ApplySort()
    {
        var sortedColumns = LayoutTable.SortedColumns
            .OrderBy(c => c.SortIndex)
            .ToList();

        IEnumerable<PersonRow> view = _master;
        if (sortedColumns.Count > 0)
        {
            IOrderedEnumerable<PersonRow>? ordered = null;
            foreach (var column in sortedColumns)
            {
                var path = column.SortMemberPath;
                if (string.IsNullOrEmpty(path) ||
                    !s_keySelectors.TryGetValue(path, out var keySelector))
                {
                    continue;
                }

                var dir = column.SortDirection;
                ordered = ordered is null
                    ? (dir == SortDirection.Descending ? view.OrderByDescending(keySelector) : view.OrderBy(keySelector))
                    : (dir == SortDirection.Descending ? ordered.ThenByDescending(keySelector) : ordered.ThenBy(keySelector));
            }
            if (ordered is not null) view = ordered;
        }

        var snapshot = view.ToList();
        People.Clear();
        foreach (var r in snapshot) People.Add(r);
    }

    private void UpdateSortStateText()
    {
        var sb = new StringBuilder();
        foreach (var col in LayoutTable.SortedColumns)
        {
            if (sb.Length > 0)
            {
                sb.Append(", ");
            }
            sb.Append($"{col.Header} {col.SortDirection} (#{col.SortIndex})");
        }
        SortStateText.Text = sb.Length == 0 ? "(none)" : sb.ToString();
    }

    public sealed class PersonRow
    {
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Team { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }
}
