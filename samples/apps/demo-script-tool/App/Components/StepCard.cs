using System;
using DemoScriptTool.App.Models;
using Microsoft.UI.Reactor.Animation;
using static Microsoft.UI.Reactor.Factories;

namespace DemoScriptTool.App.Components;

public sealed record StepCardProps(
    StepModel Step,
    StepModel? PriorStep,
    int TotalSteps,
    bool IsGenerating,
    Action<int, string> OnPromptChanged,
    Action<int, string> OnTitleChanged,
    Action<StepModel> OnRun,
    Action<StepModel> OnCopyDelta,
    Action<StepModel> OnDelete,
    Action<StepModel> OnRerunFromHere);

/// <summary>
/// One step rendered as a three-column card: prompt | code | actions
/// (spec §Steps Panel / §Card Surface). The card subscribes directly to the
/// step model so streaming token updates re-render only this card, not the
/// parent panel.
/// </summary>
public sealed class StepCard : Component<StepCardProps>
{
    public override Element Render()
    {
        var step = Props.Step;
        var (_, setRevision) = UseState(0, threadSafe: true);
        var counterRef = UseRef(0);

        // Local typing buffers for the editable text fields. The card subscribes
        // to step.Changed so streaming code / build-state mutations re-render the
        // card, but we must NOT round-trip the typed value back through setState
        // here — doing so resets WinUI's TextBox selection mid-keystroke. The
        // typed value flows to the model via Props.OnPromptChanged / OnTitleChanged
        // and persists via the shell's debounced save; on Props.Step swap (e.g.
        // file-watcher reload) we sync from the new step instance below.
        var (localTitle, setLocalTitle) = UseState(step.Title);
        var (localPrompt, setLocalPrompt) = UseState(step.Prompt);

        UseEffect(() =>
        {
            setLocalTitle(step.Title);
            setLocalPrompt(step.Prompt);
            void Handler() { counterRef.Current++; setRevision(counterRef.Current); }
            step.Changed += Handler;
            return () => step.Changed -= Handler;
        }, step);

        // Subscribe to the prior step's Changed so the bolded-diff view reflects
        // late updates to the prior step's code (manual edits, regenerate, fix
        // attempt that lands after we mounted). Separate effect so subscription
        // identity tracks PriorStep, not Step — swapping the prior reference
        // (e.g. after Add/Remove resequences) cleanly tears down + re-attaches.
        UseEffect(() =>
        {
            var prior = Props.PriorStep;
            if (prior is null) return () => { };
            void Handler() { counterRef.Current++; setRevision(counterRef.Current); }
            prior.Changed += Handler;
            return () => prior.Changed -= Handler;
            // Explicit single-element array because Props.PriorStep is nullable
            // and bare `params object[]` would mistake `null` for "no deps".
        }, new object[] { Props.PriorStep! });

        var hasCode = !string.IsNullOrEmpty(step.Code);
        var hasDelta = !string.IsNullOrWhiteSpace(step.Delta);
        var canRun = step.OutputPath is not null;

        // Default the body to the delta (presenter notes) when present —
        // the delta is what the demo author iterates on most. The Toggle
        // flips to the raw code when they want to inspect what got generated.
        var (showCode, setShowCode) = UseState(!hasDelta);
        // If the delta arrives later (streaming) we shouldn't have to keep
        // the user manually flipping back. UseEffect with a hasDelta dep
        // pulls them back to delta-view exactly once when the first delta
        // becomes available.
        UseEffect(() =>
        {
            if (hasDelta && !showCode) return;
            if (hasDelta) setShowCode(false);
        }, hasDelta);

        // .Set runs the configure delegate on every reconcile, allocating a
        // fresh FontFamily / scrollbar / MinHeight each time AND defeating the
        // OwnPropsEqual short-circuit (it requires Setters.Length == 0). We
        // reach for typed modifiers and `with { … }` everywhere we can — the
        // values diff structurally and the WinUI control isn't re-poked.
        var promptField = (TextField(localPrompt,
                v =>
                {
                    setLocalPrompt(v);
                    Props.OnPromptChanged(step.Number, v);
                },
                placeholder: "What should this step do?")
                with { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap })
            .MinHeight(140)
            .AutomationName($"Prompt for step {step.Number}");

        // Body content. Delta is rendered as Markdown when in delta-view
        // (the model emits delta as natural-language presenter notes with
        // inline code spans / fenced blocks); code is rendered monospace
        // with horizontal scroll.
        Element bodyContent = (showCode, hasCode, hasDelta) switch
        {
            (true, true, _) => Border(
                (ScrollView(
                    BuildCodeRichText(step.Code, Props.PriorStep?.Code)
                        .Foreground(Theme.PrimaryText)
                        .Padding(12))
                with { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }))
                .Background(Theme.ControlFill)
                .CornerRadius(4)
                .WithBorder(Theme.ControlStrokeSecondary, 1),

            (false, _, true) => Border(
                (ScrollView(Markdown(step.Delta!)
                    .Foreground(Theme.PrimaryText)
                    .Padding(12))
                with { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }))
                .Background(Theme.LayerFill)
                .CornerRadius(4)
                .WithBorder(Theme.ControlStrokeSecondary, 1),

            (true, false, _) => Border(TextBlock("No code generated yet.")
                    .Foreground(Theme.SecondaryText)
                    .Padding(12))
                .Background(Theme.SubtleFill)
                .CornerRadius(4),

            _ /* (false, _, false) */ => Border(TextBlock(hasCode
                    ? "No presenter notes yet — click Show code to view the generated source."
                    : "No content yet — generate this step to populate code and notes.")
                    .Foreground(Theme.SecondaryText)
                    .TextWrapping(TextWrapping.Wrap)
                    .Padding(12))
                .Background(Theme.SubtleFill)
                .CornerRadius(4),
        };

        var bodyLabel = showCode ? "GENERATED CODE" : "PRESENTER NOTES";

        // Provenance footer: tiny caption under the body that names the model
        // and the relative time-since-generation. Stale flag fires when the
        // disk file's hash at load time didn't match the hash captured at
        // generate time — indicates the artifact was edited externally.
        // All three signals (model, time, stale) round-trip via the delta
        // sidecar's YAML frontmatter so they survive an app restart.
        Element provenance = (step.GeneratedBy, step.GeneratedAt, step.StaleSinceLoad) switch
        {
            (null, null, false) => Empty(),
            (_, _, true) => HStack(8,
                Caption(BuildProvenanceString(step.GeneratedBy, step.GeneratedAt))
                    .Foreground(Theme.SecondaryText)
                    .FontSize(11),
                Caption("⚠ modified outside the app").Foreground(Theme.SystemCaution).FontSize(11))
                .Margin(0, 4, 0, 0),
            _ => Caption(BuildProvenanceString(step.GeneratedBy, step.GeneratedAt))
                    .Foreground(Theme.SecondaryText)
                    .FontSize(11)
                    .Margin(0, 4, 0, 0),
        };

        var stateBadge = BuildStateBadge(step);

        // Action buttons: SVG icon left + label right, all stretched to the
        // column's full width so they read as a uniform button stack.
        // The Button(content, onClick) overload takes a custom content
        // Element so we layout an HStack(icon, label) ourselves.
        var actions = VStack(6,
            ActionButton(IconAsset("run"), "Run", canRun, () => Props.OnRun(step), $"Run step {step.Number}"),
            ActionButton(IconAsset(showCode ? "notes" : "code"), showCode ? "Show notes" : "Show code", hasCode || hasDelta, () => setShowCode(!showCode), $"Toggle code/notes view for step {step.Number}"),
            ActionButton(IconAsset("copy"), "Copy notes", hasDelta, () => Props.OnCopyDelta(step), $"Copy speaker notes for step {step.Number}"),
            // Re-run-from-here regenerates this step + every step that follows.
            // Disabled while a generation pass is already in flight to keep
            // the cancellation token / status state consistent.
            ActionButton(IconAsset("rerun"), "Re-run from here", !Props.IsGenerating, () => Props.OnRerunFromHere(step), $"Re-run step {step.Number} and every following step"),
            ActionButton(IconAsset("delete"), "Delete", true, () => Props.OnDelete(step), $"Delete step {step.Number}"));

        var failureOutput = (step.BuildState == BuildState.Failed && !string.IsNullOrEmpty(step.BuildOutput))
            ? Border(
                (ScrollView(TextBlock(step.BuildOutput!)
                    .FontFamily("Cascadia Code, Consolas, Courier New")
                    .Foreground(Theme.PrimaryText)
                    .Padding(8))
                with { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto })
                .MaxHeight(140))
                .Background(Theme.SystemCriticalBackground)
                .WithBorder(Theme.SystemCritical, 1)
                .CornerRadius(4)
                .Margin(0, 8, 0, 0)
                .AutomationName("Compiler output")
            : Empty();

        // Pin the TextBox height so its inner ContentPresenter stops re-
        // measuring during window resize. Without an explicit MinHeight the
        // TextBox's measured height drifts 1-2px as the row width changes —
        // the FlexRow's center baseline then shifts with it, which reads
        // as a "bobbling" title. 36px = FontSize 18 + default vertical
        // padding (~9px each side); the explicit value freezes the metric.
        var titleField = (TextField(localTitle,
                v =>
                {
                    setLocalTitle(v);
                    Props.OnTitleChanged(step.Number, v);
                },
                placeholder: "Step title")
                with { AcceptsReturn = false })
            .FontSize(18)
            .FontWeight(Microsoft.UI.Text.FontWeights.SemiBold)
            .Height(36)
            .MinHeight(36)
            .AutomationName($"Title for step {step.Number}")
            .HeadingLevel(Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2)
            .Flex(grow: 1);

        // FlexAlign.Center on the row aligns every child to the row's vertical
        // centerline; per-child VAlign was redundant and could conflict with
        // the flex-row policy. The explicit row Height matches the TextBox so
        // the centerline is stable and the small Caption / state badge don't
        // get vertically jittered as the TextBox tries to grow/shrink.
        var headerRow = (FlexRow(
                Caption($"STEP {step.Number}")
                    .Foreground(Theme.SecondaryText)
                    .Width(64),
                titleField,
                stateBadge)
            with { ColumnGap = 12, AlignItems = FlexAlign.Center })
            .Height(36);

        var grid = Grid(
            columns: [GridSize.Px(280), GridSize.Star(), GridSize.Px(140)],
            rows: [GridSize.Auto, GridSize.Auto, GridSize.Auto],
            headerRow.Grid(row: 0, columnSpan: 3),
            VStack(6,
                Caption("PROMPT").Foreground(Theme.SecondaryText),
                promptField).Grid(row: 1, column: 0).Margin(0, 12, 16, 0),
            VStack(6,
                Caption(bodyLabel).Foreground(Theme.SecondaryText),
                bodyContent,
                provenance).Grid(row: 1, column: 1).Margin(0, 12, 16, 0),
            VStack(6,
                Caption("ACTIONS").Foreground(Theme.SecondaryText),
                actions).Grid(row: 1, column: 2).Margin(0, 12, 0, 0),
            failureOutput.Grid(row: 2, columnSpan: 3));

        return Border(grid)
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke, 1)
            .CornerRadius(8)
            .Padding(16)
            // BackgroundSizing is a one-shot — apply on mount instead of every
            // reconcile via .Set so the inner-border-edge sizing doesn't get
            // re-poked + visualized on every keystroke.
            .OnMount(b => ((Microsoft.UI.Xaml.Controls.Border)b).BackgroundSizing = Microsoft.UI.Xaml.Controls.BackgroundSizing.InnerBorderEdge)
            .Margin(0, 0, 0, 12)
            .AutomationName($"Step {step.Number} — {step.Title}")
            .PositionInSet(step.Number, Props.TotalSteps)
            .Transition(Transition.Fade + Transition.Slide(Edge.Bottom))
            .WithKey($"step-{step.Number}");
    }

    static Element BuildStateBadge(StepModel step) => step.BuildState switch
    {
        BuildState.NotBuilt => Empty(),
        BuildState.Building => HStack(6,
                ProgressRing().Width(14).Height(14),
                Caption("Building…").Foreground(Theme.SecondaryText)),
        BuildState.Succeeded => HStack(6,
                TextBlock("✓").Foreground(Theme.SystemSuccess).FontSize(14).VAlign(VerticalAlignment.Center),
                Caption("Build succeeded").Foreground(Theme.SystemSuccess)),
        BuildState.Fixing => HStack(6,
                ProgressRing().Width(14).Height(14),
                Caption($"Fixing… (attempt {step.FixAttempts + 1})").Foreground(Theme.SystemCaution)),
        BuildState.Failed => HStack(6,
                TextBlock("✕").Foreground(Theme.SystemCritical).FontSize(14).VAlign(VerticalAlignment.Center),
                Caption("Build failed").Foreground(Theme.SystemCritical)),
        _ => Empty(),
    };

    /// <summary>
    /// Format the per-step provenance line: <c>"✨ generated by claude-sonnet-4.5 · 2 min ago"</c>.
    /// Either field may be missing — we only render what we have.
    /// </summary>
    static string BuildProvenanceString(string? generatedBy, DateTimeOffset? generatedAt)
    {
        var parts = new System.Collections.Generic.List<string>(2);
        if (!string.IsNullOrEmpty(generatedBy))
            parts.Add($"generated by {generatedBy}");
        if (generatedAt is { } ts)
            parts.Add(RelativeTime(ts));
        return parts.Count == 0 ? "" : "✨ " + string.Join(" · ", parts);
    }

    /// <summary>
    /// Resolve <c>Assets/Icons/&lt;name&gt;.svg</c> relative to the running
    /// assembly's base directory. The csproj has a <c>&lt;Content Include=…&gt;</c>
    /// rule that copies these into bin on each build.
    /// </summary>
    static string IconAsset(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", $"{name}.svg");

    /// <summary>
    /// Action-row button factory: SVG icon (16×16) on the left, label on the
    /// right, stretched to the column's full width so the four buttons line
    /// up perfectly. Disabled state and AutomationName are wired in one place.
    /// </summary>
    static Element ActionButton(string iconPath, string label, bool isEnabled, Action onClick, string automationName) =>
        Button(
            (FlexRow(
                Image(iconPath).Width(16).Height(16),
                TextBlock(label).VAlign(VerticalAlignment.Center))
            with { ColumnGap = 8, AlignItems = FlexAlign.Center }),
            onClick)
        .Disabled(!isEnabled)
        .HAlign(HorizontalAlignment.Stretch)
        .AutomationName(automationName);

    /// <summary>
    /// Render <paramref name="code"/> as a monospace RichTextBlock with each
    /// line that is NOT present in <paramref name="priorCode"/> rendered bold.
    /// Diff is line-set difference, not a true LCS — cheap to compute on every
    /// render, and "good enough" for the demo authoring use case where the
    /// reader just wants to see what got added since the previous step.
    /// Empty / whitespace lines are never bolded so blank-line padding doesn't
    /// flag as new content.
    /// </summary>
    static RichTextBlockElement BuildCodeRichText(string code, string? priorCode)
    {
        System.Collections.Generic.HashSet<string>? prior = null;
        if (!string.IsNullOrEmpty(priorCode))
        {
            prior = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in priorCode.Split('\n'))
                prior.Add(raw.TrimEnd('\r'));
        }

        var lines = code.Split('\n');
        var paragraphs = new RichTextParagraph[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            bool isNew = prior is not null
                      && !string.IsNullOrWhiteSpace(line)
                      && !prior.Contains(line);
            paragraphs[i] = new RichTextParagraph([
                new RichTextRun(line)
                {
                    IsBold = isNew,
                    FontFamily = "Cascadia Code, Consolas, Courier New",
                }
            ]);
        }
        return new RichTextBlockElement("")
        {
            Paragraphs = paragraphs,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.NoWrap,
        };
    }

    static string RelativeTime(DateTimeOffset when)
    {
        var delta = DateTimeOffset.Now - when;
        if (delta.TotalSeconds < 0) return "just now"; // clock skew
        if (delta.TotalSeconds < 45) return "just now";
        if (delta.TotalMinutes < 1.5) return "1 min ago";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} min ago";
        if (delta.TotalHours < 1.5) return "1 hour ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} hours ago";
        if (delta.TotalDays < 1.5) return "yesterday";
        if (delta.TotalDays < 30) return $"{(int)delta.TotalDays} days ago";
        return when.LocalDateTime.ToString("yyyy-MM-dd");
    }
}
