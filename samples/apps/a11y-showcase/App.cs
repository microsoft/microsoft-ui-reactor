// ════════════════════════════════════════════════════════════════════════════
//  A11y Showcase — accessibility best-practices demo
//
//  PURPOSE: This sample is a realistic task tracker that demonstrates the
//  Reactor accessibility surface in use. Every screen-reader-relevant
//  element carries the appropriate metadata (AutomationName, HeadingLevel,
//  Landmark, LiveRegion, etc.) so that the runtime AccessibilityScanner
//  and the REACTOR_A11Y_* Roslyn analyzers produce zero findings on a
//  clean build.
//
//  What's demonstrated here:
//    • Icon-only buttons name themselves via .AutomationName()
//    • Decorative images opt out with .AccessibilityHidden()
//    • Form fields use the `header:` argument so screen readers announce
//      a label alongside the value
//    • The main content region is tagged with .Landmark(Main) and the
//      app title carries .HeadingLevel(Level1)
//    • Status messages use .LiveRegion(Polite) so screen readers
//      announce updates without interrupting
//    • List rows use .WithKey() so the reconciler keeps focus/state
//      stable across re-renders
//
//  To run the scanner at runtime, call AccessibilityScanner.Scan() on
//  the rendered element tree and inspect the structured JSON output.
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace A11yShowcase;

record TaskItem(string Id, string Title, string Assignee, string Priority, bool Done);

sealed class App : Component
{
    // Decorative icon — the surrounding button/label carries the accessible name,
    // so the image itself is hidden from screen readers.
    static Element CreateIcon(Symbol icon) => Icon(icon).AccessibilityHidden();

    public override Element Render()
    {
        var (tasks, updateTasks) = UseReducer(new List<TaskItem>
        {
            new("1", "Set up CI pipeline", "Alice", "High", false),
            new("2", "Write unit tests", "Bob", "Medium", true),
            new("3", "Design landing page", "Carol", "High", false),
            new("4", "Fix login timeout", "Dave", "Low", true),
            new("5", "Review PR #247", "Alice", "Medium", false),
        });

        var (newTitle, setNewTitle) = UseState("");
        var (filter, setFilter) = UseState("All");
        var (statusMsg, setStatusMsg) = UseState("");

        var filtered = UseMemo(() => filter switch
        {
            "Active" => tasks.Where(t => !t.Done).ToList(),
            "Done" => tasks.Where(t => t.Done).ToList(),
            _ => tasks,
        }, tasks, filter);

        var doneCount = tasks.Count(t => t.Done);
        var totalCount = tasks.Count;
        var progress = totalCount > 0 ? (double)doneCount / totalCount * 100 : 0;

        var tree = VStack(0,
            // ── Header ──────────────────────────────────────────────
            Border(
                HStack(12,
                    // Logo is decorative — the heading next to it conveys the app identity.
                    Image("ms-appx:///Assets/logo.png").Size(28, 28).AccessibilityHidden(),

                    // App title is the document heading.
                    TextBlock("Task Tracker").Bold().FontSize(24).HeadingLevel(AutomationHeadingLevel.Level1),

                    HStack(4,
                        Button(CreateIcon(Symbol.Setting), () => { }).AutomationName("Settings"),
                        Button(CreateIcon(Symbol.People), () => { }).AutomationName("Manage team"),
                        Button(CreateIcon(Symbol.Refresh), () =>
                        {
                            setStatusMsg("Tasks refreshed");
                        }).AutomationName("Refresh tasks")
                    ).HAlign(HorizontalAlignment.Right)
                ).Padding(16, 12, 16, 12)
            ).Background(Theme.CardBackground),

            // ── Toolbar ─────────────────────────────────────────────
            Border(
                HStack(12,
                    HStack(4,
                        FilterBtn("All", filter, setFilter),
                        FilterBtn("Active", filter, setFilter),
                        FilterBtn("Done", filter, setFilter)
                    ),

                    // header: associates the visible label with the input for
                    // screen readers and meets REACTOR_A11Y_003.
                    TextBox(newTitle, setNewTitle, placeholderText: "New task...", header: "New task")
                        .Width(280),

                    Button(CreateIcon(Symbol.Add), () =>
                    {
                        if (!string.IsNullOrWhiteSpace(newTitle))
                        {
                            var id = Guid.NewGuid().ToString("N")[..8];
                            updateTasks(list => [.. list, new TaskItem(id, newTitle.Trim(), "Unassigned", "Medium", false)]);
                            setNewTitle("");
                            setStatusMsg($"Added: {newTitle.Trim()}");
                        }
                    }).AutomationName("Add task"),

                    // LiveRegion so updates ("Added: ...", "Deleted: ...") are
                    // announced by the screen reader without stealing focus.
                    TextBlock(statusMsg).Opacity(0.6).HAlign(HorizontalAlignment.Right)
                        .LiveRegion(AutomationLiveSetting.Polite)
                ).Padding(12, 8, 12, 8)
            ).Background(Theme.LayerFill).WithBorder(Theme.Ref("DividerStrokeColorDefaultBrush"), 1),

            // ── Progress bar ────────────────────────────────────────
            HStack(8,
                Caption($"{doneCount}/{totalCount} complete"),
                Progress(progress).HAlign(HorizontalAlignment.Stretch)
                    .AutomationName($"{doneCount} of {totalCount} tasks complete")
            ).Margin(16, 8, 16, 8),

            // ── Task list ── Landmark(Main) lets screen-reader users jump
            // straight to the list with a navigation shortcut.
            ScrollView(
                VStack(4,
                    filtered.Select(task =>
                        TaskRow(task, updateTasks, setStatusMsg).WithKey(task.Id)
                    ).ToArray()
                ).Padding(16, 4, 16, 16)
            ).Landmark(AutomationLandmarkType.Main),

            // ── Footer ──────────────────────────────────────────────
            Border(
                HStack(12,
                    TextBox("", _ => { }, header: "Quick note")
                        .Width(300),

                    Caption("v1.0.0 — A11y Showcase").Opacity(0.5)
                        .HAlign(HorizontalAlignment.Right)
                ).Padding(12, 8, 12, 8)
            ).Background(Theme.CardBackground)
        );

        // ── Runtime accessibility scan (DEBUG only) ─────────────────
        // The scanner should now find zero issues — run it on first render
        // to confirm and print the (empty) report.
        var scanRan = UseRef(false);
        if (!scanRan.Current)
        {
            scanRan.Current = true;
            var findings = AccessibilityScanner.Scan(tree);
            global::System.Diagnostics.Debug.WriteLine($"");
            global::System.Diagnostics.Debug.WriteLine($"═══ AccessibilityScanner: {findings.Count} finding(s) ═══");
            foreach (var f in findings)
                global::System.Diagnostics.Debug.WriteLine($"  [{f.Id}] {f.Severity.ToUpper()}: {f.Message}");
            global::System.Diagnostics.Debug.WriteLine($"");

            var jsonPath = global::System.IO.Path.Combine(
                global::System.IO.Path.GetTempPath(), "a11y-showcase-diagnostics.json");
            AccessibilityScanner.ExportJson(findings, jsonPath);
            global::System.Diagnostics.Debug.WriteLine($"  JSON report: {jsonPath}");
            global::System.Diagnostics.Debug.WriteLine($"═══════════════════════════════════════════════════════");
        }

        return tree;
    }

    // ── Task row ────────────────────────────────────────────────────
    static Element TaskRow(
        TaskItem task,
        Action<Func<List<TaskItem>, List<TaskItem>>> updateTasks,
        Action<string> setStatusMsg)
    {
        return Border(
            HStack(12,
                CheckBox(task.Done, done =>
                {
                    updateTasks(list =>
                    {
                        var copy = new List<TaskItem>(list);
                        var idx = copy.FindIndex(t => t.Id == task.Id);
                        if (idx >= 0) copy[idx] = task with { Done = done };
                        return copy;
                    });
                    setStatusMsg(done ? $"Completed: {task.Title}" : $"Reopened: {task.Title}");
                }).AutomationName($"Mark '{task.Title}' as done"),

                VStack(2,
                    TextBlock(task.Title).SemiBold()
                        .Opacity(task.Done ? 0.5 : 1.0),
                    Caption($"{task.Assignee} · {task.Priority}")
                        .Opacity(0.6)
                ),

                HStack(4,
                    PriorityBadge(task.Priority),

                    Button(CreateIcon(Symbol.Edit), () =>
                    {
                        setStatusMsg($"Editing: {task.Title}");
                    }).AutomationName($"Edit {task.Title}"),
                    Button(CreateIcon(Symbol.Delete), () =>
                    {
                        updateTasks(list => list.Where(t => t.Id != task.Id).ToList());
                        setStatusMsg($"Deleted: {task.Title}");
                    }).AutomationName($"Delete {task.Title}")
                ).HAlign(HorizontalAlignment.Right)
            ).Padding(8, 6, 8, 6)
        )
        .CornerRadius(4)
        .Background(Theme.ControlFill);
    }

    // ── Filter button — label argument is the text *and* the accessible name.
    static Element FilterBtn(string label, string current, Action<string> set) =>
        Button(label, () => set(label))
            .AutomationName(label)
            .Background(label == current ? Theme.Ref("AccentFillColorDefaultBrush") : Theme.Ref("SubtleFillColorTransparentBrush"));

    // ── Priority badge — image is decorative; the Caption provides the text.
    static Element PriorityBadge(string priority)
    {
        var (color, icon) = priority switch
        {
            "High" => ("#d13438", "Important"),
            "Low" => ("#498205", "Download"),
            _ => ("#8a8886", "Remove"),
        };

        return Border(
            HStack(4,
                Image($"ms-appx:///Assets/{icon}.png").Size(14, 14).AccessibilityHidden(),
                Caption(priority)
            ).Padding(4, 2, 8, 2)
        ).CornerRadius(4).Background(color).Opacity(0.85);
    }
}
