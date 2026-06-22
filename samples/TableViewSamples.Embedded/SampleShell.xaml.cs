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
    static bool s_closureMerged;

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
        // Provide the satellite TableView's implicit Style + theme brushes + primitive styles to every
        // declarative TableView in the pages (scoped to this control's subtree). Strings are merged into
        // the app PRI by the host's post-build step.
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "AdvancedStyles");
            if (Directory.Exists(dir))
            {
                var sb = new StringBuilder();
                sb.Append("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><ResourceDictionary.MergedDictionaries>");
                foreach (var f in Directory.GetFiles(dir, "*.xaml").OrderBy(f => f)) sb.Append(File.ReadAllText(f));
                sb.Append("</ResourceDictionary.MergedDictionaries></ResourceDictionary>");
                var closure = (ResourceDictionary)XamlReader.Load(sb.ToString());
                // Brushes + primitive styles available app-wide (for {ThemeResource} in templates).
                if (!s_closureMerged) { Application.Current.Resources.MergedDictionaries.Add(closure); s_closureMerged = true; }
                // The satellite control's default Style is not found by the implicit-style lookup for a
                // loose declarative <muxc:TableView>, so capture it and assign it explicitly to every
                // TableView in each page after navigation (mirrors what works for code-created instances).
                s_tvStyle ??= FindControlStyle(closure);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[SampleShell] closure merge failed: " + ex); }
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

    static Style? s_tvStyle;

    // Walk a page's visual tree and assign the satellite control's default Style to every TableView that
    // doesn't already have an explicit one, so its template inflates and rows/headers render.
    static void ApplyTableViewStyles(DependencyObject root)
    {
        if (s_tvStyle == null) return;
        if (root is Microsoft.UI.Xaml.Controls.TableView tv)
        {
            // Force-(re)apply the satellite control's default Style so the template inflates even if an
            // implicit style was already resolved (which otherwise renders blank for some pages). Clearing
            // first makes WinUI re-run template application. Guarantee a height for "*"-sized hosts.
            tv.Style = null;
            tv.Style = s_tvStyle;
            try { tv.ApplyTemplate(); } catch { }
            if (double.IsNaN(tv.Height) && tv.MinHeight < 1) tv.MinHeight = 420;
        }
        int n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            ApplyTableViewStyles(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i));
    }

    static Style? FindControlStyle(ResourceDictionary d)
    {
        foreach (var kv in d)
            if (kv.Value is Style s && s.TargetType == typeof(Microsoft.UI.Xaml.Controls.TableView)
                && s.Setters.Any(st => st is Setter se && se.Property == Control.TemplateProperty))
                return s;
        foreach (var md in d.MergedDictionaries)
        {
            var r = FindControlStyle(md);
            if (r != null) return r;
        }
        return null;
    }

    void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag && s_pageMap.TryGetValue(tag, out var pageType))
            ContentFrame.Navigate(pageType, null, new Microsoft.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo());
    }

    void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args) { }
    void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) { }
}


