// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView.RowStyleSelector — the conditional row-styling
/// hook shipped in P3.1. The selector is a Microsoft.UI.Xaml.Controls.StyleSelector;
/// TableView invokes its SelectStyleCore(item, container) for every realized
/// row, then applies the returned Style to the TableViewRow container. A null
/// return falls back to the implicit theme style (the row simply ClearValue's
/// its Style DP).
///
/// The page wires three selectors:
///   * <see cref="DepartmentRowStyleSelector"/> — looks up a per-department
///     style from the page's resource dictionary.
///   * <see cref="SalaryTierRowStyleSelector"/> — top earners get a success
///     tint, bottom earners get a caution tint, mid-range falls back to null.
///   * <see cref="InactiveRowStyleSelector"/> — only returns a style for
///     inactive people; everyone else hits the null-fallback branch.
///
/// Each selector also bumps an invocation counter so reviewers can see the
/// selector actually runs per realized row.
/// </summary>
public sealed partial class ConditionalStylingPage : Page
{
    private int _invocationCount;

    public ConditionalStylingPage()
    {
        InitializeComponent();
        People = new ObservableCollection<Person>(PersonData.Take(100));
        Loaded += OnPageLoaded;
    }

    public ObservableCollection<Person> People { get; }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Default: no selector. The picker SelectionChanged handler installs
        // the selector when the user picks a non-default option.
        ActiveSelectorText.Text = "(none) — implicit theme style";
    }

    private void OnSelectorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeopleTable is null)
        {
            return;
        }

        // Reset the counter on every swap so reviewers can see the new
        // selector being invoked across the realized window.
        _invocationCount = 0;
        InvocationCountText.Text = "0";

        switch (SelectorPicker.SelectedIndex)
        {
            case 0:
                PeopleTable.RowStyleSelector = null;
                ActiveSelectorText.Text = "(none) — implicit theme style";
                break;

            case 1:
                PeopleTable.RowStyleSelector = new DepartmentRowStyleSelector(this, OnSelectorInvoked);
                ActiveSelectorText.Text = "DepartmentRowStyleSelector";
                break;

            case 2:
                PeopleTable.RowStyleSelector = new SalaryTierRowStyleSelector(this, OnSelectorInvoked);
                ActiveSelectorText.Text = "SalaryTierRowStyleSelector";
                break;

            case 3:
                PeopleTable.RowStyleSelector = new InactiveRowStyleSelector(this, OnSelectorInvoked);
                ActiveSelectorText.Text = "InactiveRowStyleSelector";
                break;
        }
    }

    private void OnSelectorInvoked()
    {
        _invocationCount++;
        InvocationCountText.Text = _invocationCount.ToString();
    }

    // ----- Selector implementations -----
    //
    // Each selector takes a back-reference to the page so it can resolve the
    // styles defined in Page.Resources. A real consumer would inject style
    // references through a constructor or pull them from Application.Resources;
    // walking back to the page keeps this sample self-contained.

    private sealed class DepartmentRowStyleSelector : StyleSelector
    {
        private readonly ConditionalStylingPage _page;
        private readonly System.Action _onInvoked;

        public DepartmentRowStyleSelector(ConditionalStylingPage page, System.Action onInvoked)
        {
            _page = page;
            _onInvoked = onInvoked;
        }

        protected override Style SelectStyleCore(object item, DependencyObject container)
        {
            _onInvoked();
            if (item is not Person p)
            {
                return null!;
            }

            var key = p.Department switch
            {
                "Engineering" => "EngineeringRowStyle",
                "Sales"       => "SalesRowStyle",
                "Marketing"   => "MarketingRowStyle",
                "HR"          => "HRRowStyle",
                "Operations"  => "OperationsRowStyle",
                "Design"      => "DesignRowStyle",
                "Product"     => "ProductRowStyle",
                "Finance"     => "FinanceRowStyle",
                _             => null,
            };

            if (key is null || !_page.Resources.TryGetValue(key, out var resource) || resource is not Style style)
            {
                return null!;
            }

            return style;
        }
    }

    private sealed class SalaryTierRowStyleSelector : StyleSelector
    {
        private const double HighThreshold = 110_000.0;
        private const double LowThreshold = 60_000.0;

        private readonly ConditionalStylingPage _page;
        private readonly System.Action _onInvoked;

        public SalaryTierRowStyleSelector(ConditionalStylingPage page, System.Action onInvoked)
        {
            _page = page;
            _onInvoked = onInvoked;
        }

        protected override Style SelectStyleCore(object item, DependencyObject container)
        {
            _onInvoked();
            if (item is not Person p)
            {
                return null!;
            }

            string? key = p.Salary switch
            {
                >= HighThreshold => "HighSalaryRowStyle",
                <  LowThreshold  => "LowSalaryRowStyle",
                _                => null,
            };

            if (key is null || !_page.Resources.TryGetValue(key, out var resource) || resource is not Style style)
            {
                return null!;
            }

            return style;
        }
    }

    private sealed class InactiveRowStyleSelector : StyleSelector
    {
        private readonly ConditionalStylingPage _page;
        private readonly System.Action _onInvoked;

        public InactiveRowStyleSelector(ConditionalStylingPage page, System.Action onInvoked)
        {
            _page = page;
            _onInvoked = onInvoked;
        }

        protected override Style SelectStyleCore(object item, DependencyObject container)
        {
            _onInvoked();
            if (item is Person { IsActive: false } &&
                _page.Resources.TryGetValue("InactiveRowStyle", out var resource) &&
                resource is Style style)
            {
                return style;
            }

            return null!;
        }
    }
}
