using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Reactor.Controls.Factories;
using TVGrid = Microsoft.UI.Xaml.Controls.TableViewGridLinesVisibility;
using TVHeaders = Microsoft.UI.Xaml.Controls.TableViewHeadersVisibility;
using TVSel = Microsoft.UI.Xaml.Controls.TableViewSelectionMode;

// FIRST-CLASS Reactor TableView gallery — the native TableViewSamples gallery rebuilt as pure-C# Reactor
// (MVU) pages that consume the consumable TableView(items, columns) control, instead of hosting the
// compiled WinUI gallery via XamlHostElement interop. Each page is a Reactor component; the signature
// Showcase visuals (Department pills, Status chips, stoplight Salary tints) render via template columns.
class TableViewFirstClassGallery : Component
{
    public sealed record Person(
        string FirstName, string LastName, string Email, string Department,
        string Role, DateTimeOffset JoinDate, double Salary, bool IsActive)
    {
        public string JoinDateText => JoinDate.ToString("yyyy-MM-dd");
    }

    static readonly List<Person> People = new()
    {
        new("Ava","Chen","ava.chen@contoso.com","Engineering","Software Engineer", new(2021,3,15,0,0,0,TimeSpan.Zero), 112500, true),
        new("Noah","Patel","noah.patel@contoso.com","Design","Designer", new(2022,1,22,0,0,0,TimeSpan.Zero), 84000, true),
        new("Mia","Garcia","mia.garcia@contoso.com","Product","Product Manager", new(2020,11,9,0,0,0,TimeSpan.Zero), 167800, true),
        new("Ethan","Nguyen","ethan.nguyen@contoso.com","Sales","Account Executive", new(2023,5,2,0,0,0,TimeSpan.Zero), 73500, false),
        new("Sophia","Jones","sophia.jones@contoso.com","Marketing","Brand Manager", new(2019,8,18,0,0,0,TimeSpan.Zero), 124200, true),
        new("Liam","Wright","liam.wright@contoso.com","Operations","Ops Manager", new(2018,4,11,0,0,0,TimeSpan.Zero), 158600, true),
        new("Olivia","Smith","olivia.smith@contoso.com","Finance","Financial Analyst", new(2021,9,27,0,0,0,TimeSpan.Zero), 98200, true),
        new("James","Lopez","james.lopez@contoso.com","HR","Recruiter", new(2024,2,14,0,0,0,TimeSpan.Zero), 68100, true),
        new("Emma","Brown","emma.brown@contoso.com","Engineering","Senior Engineer", new(2017,6,5,0,0,0,TimeSpan.Zero), 198400, true),
        new("Lucas","Taylor","lucas.taylor@contoso.com","Design","Researcher", new(2022,10,31,0,0,0,TimeSpan.Zero), 62000, false),
        new("Zoe","Hernandez","zoe.hernandez@contoso.com","Product","Group PM", new(2016,12,20,0,0,0,TimeSpan.Zero), 221700, true),
        new("Henry","Wilson","henry.wilson@contoso.com","Sales","Sales Manager", new(2020,7,13,0,0,0,TimeSpan.Zero), 171300, false),
    };

    // Rich Showcase columns (text + pill/chip/tint template columns).
    static readonly List<TableColumn> ShowcaseColumns = new()
    {
        new("First name", nameof(Person.FirstName), Width: 110),
        new("Department", nameof(Person.Department), CellStyle.Pill, Width: 150),
        new("Status", nameof(Person.IsActive), CellStyle.Chip, Width: 100),
        new("Salary", nameof(Person.Salary), CellStyle.Tint, Width: 120),
        new("Join date", nameof(Person.JoinDateText), Width: 110),
        new("Role", nameof(Person.Role), Width: 170),
        new("Email", nameof(Person.Email)),
    };

    // Plain text columns for the feature-focused pages.
    static readonly List<TableColumn> TextColumns = new()
    {
        new("First name", nameof(Person.FirstName), Width: 120),
        new("Last name", nameof(Person.LastName), Width: 120),
        new("Department", nameof(Person.Department), Width: 130),
        new("Role", nameof(Person.Role), Width: 170),
        new("Join date", nameof(Person.JoinDateText), Width: 110),
        new("Email", nameof(Person.Email)),
    };

    enum Page { Showcase, Selection, Sorting, GridLines, Frozen, Headers }

    static readonly (Page Page, string Label)[] Pages =
    {
        (Page.Showcase, "Showcase"),
        (Page.Selection, "Selection"),
        (Page.Sorting, "Sort + filter"),
        (Page.GridLines, "Grid lines"),
        (Page.Frozen, "Frozen columns"),
        (Page.Headers, "Headers"),
    };

    public override Element Render()
    {
        var (page, setPage) = UseState(Page.Showcase);

        var nav = HStack(8,
            System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(Pages, t =>
                Button(t.Label, () => setPage(t.Page)).IsEnabled(page != t.Page) as Element)));

        return VStack(12,
            Heading("TableView — first-class Reactor gallery"),
            TextBlock(
                "The native TableViewSamples gallery, rebuilt as pure-C# Reactor pages that consume the " +
                "TableView(items, columns) control — no XamlHostElement interop. Pick a page:"),
            nav,
            RenderPage(page)
        );
    }

    Element RenderPage(Page p) => p switch
    {
        Page.Showcase => PageBody(
            "Showcase — the full feature set: colored Department pills, Active/Inactive Status chips, and " +
            "stoplight Salary tints (template columns), a frozen first column, sortable + resizable columns.",
            TableView(People, ShowcaseColumns, height: 440) with
            {
                GridLinesVisibility = TVGrid.Horizontal,
                CanSortColumns = true,
                CanResizeColumns = true,
                FrozenColumnCount = 1,
            }),

        Page.Selection => PageBody(
            "Multiple-row selection with the leading selection gutter (checkbox column). Click rows or the " +
            "header checkbox to select.",
            TableView(People, TextColumns, height: 440) with
            {
                SelectionMode = TVSel.Multiple,
                IsSelectionGutterVisible = true,
            }),

        Page.Sorting => PageBody(
            "Sortable + filterable columns — click a header to sort, the funnel to filter.",
            TableView(People, TextColumns, height: 440) with
            {
                CanSortColumns = true,
                CanFilterColumns = true,
                CanResizeColumns = true,
            }),

        Page.GridLines => PageBody(
            "Grid lines — both horizontal and vertical (TableViewGridLinesVisibility.All).",
            TableView(People, TextColumns, height: 440) with
            {
                GridLinesVisibility = TVGrid.All,
            }),

        Page.Frozen => PageBody(
            "Frozen leading columns — the first two columns stay pinned during horizontal scroll " +
            "(FrozenColumnCount = 2).",
            TableView(People, TextColumns, height: 440) with
            {
                FrozenColumnCount = 2,
                CanResizeColumns = true,
            }),

        Page.Headers => PageBody(
            "Column-only headers (TableViewHeadersVisibility.Column), with grid lines.",
            TableView(People, TextColumns, height: 440) with
            {
                HeadersVisibility = TVHeaders.Column,
                GridLinesVisibility = TVGrid.Horizontal,
            }),

        _ => TextBlock("Select a page"),
    };

    static Element PageBody(string description, Element table) =>
        VStack(8, TextBlock(description), table);
}
