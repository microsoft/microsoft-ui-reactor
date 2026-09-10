using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using TableViewSamples.Pages;

namespace TableViewSamples;

public sealed partial class SampleShell : UserControl
{
    static readonly Dictionary<string, Type> s_pageMap = new()
    {
        ["Home"] = typeof(HomePage), ["Showcase"] = typeof(ShowcasePage), ["DynamicColumns"] = typeof(DynamicColumnsPage),
        ["Selection"] = typeof(SelectionPage), ["CellSelection"] = typeof(CellSelectionPage), ["ColumnResize"] = typeof(ColumnResizePage),
        ["Sort"] = typeof(SortPage), ["Filter"] = typeof(FilterPage), ["StickyHeaders"] = typeof(StickyHeadersPage),
        ["HeadersVisibility"] = typeof(HeadersVisibilityPage), ["InlineEdit"] = typeof(InlineEditPage), ["Groups"] = typeof(GroupsPage),
        ["Virtualization"] = typeof(VirtualizationPage), ["KeyboardNav"] = typeof(KeyboardNavPage), ["ColumnReorder"] = typeof(ColumnReorderPage),
        ["About"] = typeof(AboutPage), ["FrozenColumns"] = typeof(FrozenColumnsPage), ["FrozenTrailingColumns"] = typeof(FrozenTrailingColumnsPage),
        ["MixedControls"] = typeof(MixedControlsPage), ["ConditionalStyling"] = typeof(ConditionalStylingPage), ["CellStyling"] = typeof(CellStylingPage),
        ["RowTemplate"] = typeof(RowTemplatePage), ["RowDetails"] = typeof(RowDetailsPage), ["RowColors"] = typeof(RowColorsPage),
        ["GridLines"] = typeof(GridLinesVisibilityPage), ["Marquee"] = typeof(MarqueePage), ["RowReorder"] = typeof(RowReorderPage),
        ["Hierarchy"] = typeof(HierarchyPage), ["Layout"] = typeof(LayoutPage), ["AdvancedFilter"] = typeof(AdvancedFilterPage),
        ["Clipboard"] = typeof(ClipboardPage), ["ColumnReorderGesture"] = typeof(ColumnReorderGesturePage), ["RTLPlayground"] = typeof(RTLPlaygroundPage),
        ["Performance"] = typeof(PerformancePage), ["Pagination"] = typeof(PaginationPage), ["DataExport"] = typeof(DataExportPage),
    };

    public SampleShell()
    {
        // Shared page resources (ColWidth*, section styles, custom banding brushes) must live in
        // Application.Resources so each page's {StaticResource ...} resolves at page-parse time (before
        // the page is attached to this shell's subtree). Mirrors the sample's App.xaml.
        var appRes = Application.Current.Resources;
        if (!appRes.MergedDictionaries.OfType<SharedResources>().Any())
            appRes.MergedDictionaries.Add(new SharedResources());
        this.InitializeComponent();
        // The satellite TableView's implicit Style + theme brushes + primitive styles are provided by the
        // SINGLE shared enablement in Reactor.Controls.TableViewStyles (idempotent, process-wide). It
        // registers the advanced XAML metadata provider and merges the control's Style/theme closure into
        // Application.Resources EXACTLY ONCE — shared with the consumable TableView control tab. Merging a
        // second copy of the same advanced theme dictionaries here corrupts native resource lookup and
        // access-violates the next Frame.Navigate, so the gallery defers to the one authority.
        try { Reactor.Controls.TableViewStyles.EnsureInitialized(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[SampleShell] enablement failed: " + ex); }
        ContentFrame.Navigated += (_, e) =>
        {
            if (e.Content is FrameworkElement page)
                page.Loaded += (_, __) =>
                {
                    ApplyTableViewStyles(page);
                    // Some pages (Showcase, Row colors) realize their TableView inside a SamplePresenter
                    // template slightly after Loaded; re-apply on a couple of dispatcher ticks to catch them.
                    var dq = page.DispatcherQueue;
                    foreach (var ms in new[] { 120, 400, 900 })
                    {
                        var t = dq.CreateTimer();
                        t.Interval = TimeSpan.FromMilliseconds(ms);
                        t.IsRepeating = false;
                        t.Tick += (s, _) => { ApplyTableViewStyles(page); ((Microsoft.UI.Dispatching.DispatcherQueueTimer)s).Stop(); };
                        t.Start();
                    }
                };
        };
        NavView.SelectedItem = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
        ContentFrame.Navigate(typeof(HomePage));
    }

    // Walk a page's visual tree and apply the satellite control's default Style (via the single shared
    // enablement) to every TableView so its template inflates and rows/headers render.
    static void ApplyTableViewStyles(DependencyObject root)
    {
        if (root is Microsoft.UI.Xaml.Controls.TableView tv)
            Reactor.Controls.TableViewStyles.EnsureLoadedAndApply(tv);
        int n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            ApplyTableViewStyles(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i));
    }

    void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag && s_pageMap.TryGetValue(tag, out var pageType))
            ContentFrame.Navigate(pageType, null, new Microsoft.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo());
    }

    void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args) { }
    void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) { }
}


