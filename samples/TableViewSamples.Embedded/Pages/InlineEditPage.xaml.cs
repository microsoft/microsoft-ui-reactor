// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView's P2.7 inline-editing pipeline:
///
///   * Each text cell is wrapped in an EditableContentPresenter (the P8
///     primitive shipped in Phase 1). The display content is a TextBlock
///     bound to the column's Binding; the edit content is a TextBox bound
///     TwoWay to the same path with UpdateSourceTrigger=Explicit so typing
///     doesn't touch the model until commit.
///
///   * F2 / programmatic BeginEdit() transitions the focused cell into edit
///     mode. Enter / programmatic CommitEdit() pushes the typed value back
///     to the model. Esc / programmatic CancelEdit() discards it.
///
///   * The cancellable EditEnding event lets consumers veto a commit
///     (e.g. validation failure) — set args.Cancel = true and the cell
///     stays in edit mode.
///
///   * Per-table TableView.IsReadOnly and per-column
///     TableViewColumn.IsReadOnly compose with OR. Either being true blocks
///     the transition. Flipping table.IsReadOnly to true while a cell is
///     editing COMMITS the in-flight edit (it doesn't silently abort) so
///     the user's typed value is preserved.
/// </summary>
public sealed partial class InlineEditPage : Page
{
    public InlineEditPage()
    {
        InitializeComponent();

        // Materialize a snapshot — InlineEdit needs mutable rows the user can
        // type into and see persist. PersonData implements INPC so the display
        // TextBlock's binding refreshes after CommitEdit pushes a new value.
        People = new ObservableCollection<Person>(PersonData.Take(25));

        // N9 row-level edit lifecycle: subscribed in code-behind so the demo
        // table's pre-existing XAML event surface stays minimal. RowEditEnding
        // is cancelable (mirrors WPF DataGrid / WCT). RowEditEnded fires after
        // the row leaves edit mode (commit or cancel).
        PeopleTable.RowEditEnding += OnTableRowEditEnding;
        PeopleTable.RowEditEnded += OnTableRowEditEnded;
    }

    public ObservableCollection<Person> People { get; }

    // ----- Toolbar buttons -----

    private void OnBeginEditClick(object sender, RoutedEventArgs e)
    {
        var ok = PeopleTable.BeginEdit();
        LastResultText.Text = $"BeginEdit() = {ok}";
    }

    private void OnCommitEditClick(object sender, RoutedEventArgs e)
    {
        var ok = PeopleTable.CommitEdit();
        LastResultText.Text = $"CommitEdit() = {ok}";
    }

    private void OnCancelEditClick(object sender, RoutedEventArgs e)
    {
        var ok = PeopleTable.CancelEdit();
        LastResultText.Text = $"CancelEdit() = {ok}";
    }

    // ----- ToggleSwitches -----

    private void OnReadOnlyToggled(object sender, RoutedEventArgs e)
    {
        // Per the control's contract: setting IsReadOnly=true while editing
        // COMMITS the in-flight edit (we want the user's typed value
        // preserved even though we're locking the table down).
        PeopleTable.IsReadOnly = ReadOnlyToggle.IsOn;
    }

    private void OnEmailReadOnlyToggled(object sender, RoutedEventArgs e)
    {
        EmailColumn.IsReadOnly = EmailReadOnlyToggle.IsOn;
        EmailColumn.Header = EmailReadOnlyToggle.IsOn ? "Email (read-only)" : "Email (editable)";
    }

    private void OnLastNameReadOnlyToggled(object sender, RoutedEventArgs e)
    {
        LastNameColumn.IsReadOnly = LastNameReadOnlyToggle.IsOn;
        LastNameColumn.Header = LastNameReadOnlyToggle.IsOn ? "Last name (read-only)" : "Last name (editable)";
    }

    // ----- Edit lifecycle event handlers -----

    private int _editStartedCount;
    private int _editEndedCount;

    private void OnTableEditStarted(TableView sender, TableViewEditStartedEventArgs args)
    {
        _editStartedCount++;
        EditStartedCountText.Text = _editStartedCount.ToString();
        IsEditingText.Text = sender.IsEditing.ToString();
        EditingItemText.Text = (sender.EditingItem as Person)?.FullName ?? "(none)";
        EditingColumnText.Text = (sender.EditingColumn as TableViewTextColumn)?.Header?.ToString() ?? "(none)";
        LastResultText.Text = $"EditStarted on '{EditingColumnText.Text}'";
    }

    private void OnTableEditEnding(TableView sender, TableViewEditEndingEventArgs args)
    {
        // Validation hook: when the toggle is on, reject empty values.
        // EditEnding fires AFTER CommitEdit's UpdateSource call, so by the
        // time we see this event the model already holds the typed value.
        // The veto restores edit mode and keeps the cell in the editor.
        if (ValidateToggle.IsOn && args.Kind == TableViewEditEndKind.Commit)
        {
            // Read the prospective new value via the column's Binding source.
            var newValue = ReadCurrentNameOf(args);
            if (string.IsNullOrWhiteSpace(newValue))
            {
                args.Cancel = true;
                LastResultText.Text = "EditEnding vetoed (empty value rejected)";
                return;
            }
        }
        LastResultText.Text = $"EditEnding (Kind={args.Kind})";
    }

    private void OnTableEditEnded(TableView sender, TableViewEditEndedEventArgs args)
    {
        _editEndedCount++;
        EditEndedCountText.Text = _editEndedCount.ToString();
        IsEditingText.Text = sender.IsEditing.ToString();
        EditingItemText.Text = "(none)";
        EditingColumnText.Text = "(none)";
        LastResultText.Text = $"EditEnded (Kind={args.Kind})";
    }

    // Read the most-recently-committed value off the model for an editable
    // text column (first name / last name / email), matching the column the
    // edit ended on. The validation toggle uses this to reject empty strings
    // AFTER UpdateSource has run.
    private static string? ReadCurrentNameOf(TableViewEditEndingEventArgs args)
    {
        if (args.Item is Person p)
        {
            if (args.Column is TableViewTextColumn tc)
            {
                return tc.EffectiveSortMemberPath switch
                {
                    nameof(Person.FirstName) => p.FirstName,
                    nameof(Person.LastName) => p.LastName,
                    nameof(Person.Email) => p.Email,
                    _ => null,
                };
            }
        }
        return null;
    }

    // ----- N9 row-level edit lifecycle -----

    private int _rowEditEndingCount;

    private void OnTableRowEditEnding(TableView sender, TableViewRowEditEndingEventArgs args)
    {
        _rowEditEndingCount++;
        RowEditEndingCountText.Text = _rowEditEndingCount.ToString();
        var name = (args.Row as Person)?.FullName ?? "(none)";
        LastRowEditEndingText.Text = $"row='{name}', EditAction={args.EditAction}, Cancel={args.Cancel}";
    }

    private void OnTableRowEditEnded(TableView sender, TableViewRowEditEndedEventArgs args)
    {
        var name = (args.Row as Person)?.FullName ?? "(none)";
        LastRowEditEndedText.Text = $"row='{name}', EditAction={args.EditAction}";
    }
}
