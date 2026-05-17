using System.Text;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    /// <summary>
    /// Renders a minimal, icon-only trigger (a lightning bolt "⚡" by default)
    /// whose click opens a flyout containing the menu items returned by
    /// <paramref name="items"/>. Deliberately distinct from normal chrome —
    /// an amber glyph with no drop-down indicator — so "this session is in
    /// dev mode" reads at a glance.
    ///
    /// When the in-app devtools UI is disabled for the session
    /// (<see cref="ReactorApp.DevtoolsEnabled"/> is false), this returns
    /// <see cref="Empty"/> and the <paramref name="items"/> lambda is not
    /// invoked — so any element construction inside the lambda is skipped
    /// at retail cost of one bool check.
    ///
    /// A built-in "Highlight reconcile changes" toggle is always appended
    /// (separated from user items) to flip
    /// <see cref="ReactorFeatureFlags.HighlightReconcileChanges"/>.
    ///
    /// Typical placement is a titlebar:
    /// <code>
    /// HStack(
    ///     Text("My App"), Spacer(),
    ///     DevtoolsMenu(() => new MenuFlyoutItemBase[]
    ///     {
    ///         ToggleMenuItem("Debug UI", AppFlags.DebugUI.Value,
    ///             v => AppFlags.DebugUI.Value = v),
    ///         MenuSeparator(),
    ///         MenuItem("Clear cache", () => CacheService.Clear()),
    ///     })
    /// )
    /// </code>
    ///
    /// Pass a different <paramref name="glyph"/> to customize — e.g. the
    /// radioactive sign "☢" (U+2622), a bug "🐛", or any Unicode/Segoe Fluent
    /// glyph you prefer. For toggle items to reflect fresh state when a
    /// backing <see cref="Observable{T}"/> changes, subscribe the enclosing
    /// component via <c>ctx.UseObservable(flag)</c>.
    /// </summary>
    public static Element DevtoolsMenu(
        Func<IEnumerable<MenuFlyoutItemBase>>? items = null,
        string glyph = "⚡",
        string toolTip = "Devtools",
        string? automationId = null)
    {
        if (!ReactorApp.DevtoolsEnabled) return Empty();

        var userItems = items?.Invoke()?.ToArray() ?? Array.Empty<MenuFlyoutItemBase>();

        var builtInToggle = ToggleMenuItem("Highlight reconcile changes",
            ReactorFeatureFlags.HighlightReconcileChanges,
            v =>
            {
                ReactorFeatureFlags.HighlightReconcileChanges = v;
                // Nudge a render so wrapper install / teardown happens
                // immediately, mirroring the layout-cost toggle.
                ReactorApp.ActiveHostInternal?.RequestRender();
            });

        // Layout-cost overlay toggle (spec 032). The overlay wiring builds
        // lazily on the next render pass after the flag flips, so we nudge
        // the active host to re-render immediately — otherwise the overlay
        // wouldn't appear until the next unrelated state change. The ETW
        // leg stays init-dependent (see ShowLayoutCost doc comment).
        var layoutCostToggle = ToggleMenuItem("Show layout cost overlay",
            ReactorFeatureFlags.ShowLayoutCost,
            v =>
            {
                ReactorFeatureFlags.ShowLayoutCost = v;
                ReactorApp.ActiveHostInternal?.RequestRender();
            });

        // Keyed-list diagnostics viewer (spec 042 Phase 6.2). Surfaces the
        // duplicate-key / null-key bailout warnings captured by
        // ReactorDiagnostics — first occurrence per (control, kind, sample-set)
        // logs through ILogger; subsequent occurrences bump the count in the
        // collector. The label refreshes only when the parent component
        // re-renders, so the click handler nudges a render after closing.
        var warningCount = ReactorDiagnostics.RecentKeyedListWarnings.Count;
        var keyedListItem = MenuItem(
            warningCount == 0
                ? "Keyed-list diagnostics (none)"
                : $"Keyed-list diagnostics ({warningCount})",
            ShowKeyedListDiagnosticsDialog);

        // Separator only makes sense when there are user items to separate from.
        var builtInItems = userItems.Length > 0
            ? new MenuFlyoutItemBase[] { MenuSeparator(), builtInToggle, layoutCostToggle, keyedListItem }
            : new MenuFlyoutItemBase[] { builtInToggle, layoutCostToggle, keyedListItem };

        var materialized = userItems.Concat(builtInItems).ToArray();

        var trigger = Button(glyph)
            .Foreground("#F59E0B")
            .Background("#00000000")
            .WithBorder("#00000000", 0)
            .Padding(horizontal: 8, vertical: 4)
            .FontSize(16)
            .ToolTip(toolTip)
            .AutomationName(toolTip);

        if (automationId is not null)
            trigger = trigger.AutomationId(automationId);

        return MenuFlyout(trigger, materialized);
    }

    private static void ShowKeyedListDiagnosticsDialog()
    {
        // Capture a snapshot up front — the producer side keeps writing, but
        // the dialog only shows what was visible at click time.
        var warnings = ReactorDiagnostics.RecentKeyedListWarnings;
        var host = ReactorApp.ActiveHostInternal;
        var xamlRoot = host?.Window?.Content?.XamlRoot;
        if (xamlRoot is null) return;

        string body;
        if (warnings.Count == 0)
        {
            body = "No keyed-list bailouts captured this session.\n\n" +
                   "When ListView<T> / GridView<T> / LazyVStack<T> / LazyHStack<T> " +
                   "see a duplicate or null key, the diff falls back to a full " +
                   "Reset and one entry lands here. Spec 042 §4.3.";
        }
        else
        {
            var sb = new StringBuilder();
            for (int i = 0; i < warnings.Count; i++)
            {
                var w = warnings[i];
                var ts = w.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
                var kind = w.Kind == KeyedListDiagnosticKind.NullKey ? "null key" : "duplicate keys";
                var times = w.Count == 1 ? "1×" : $"{w.Count}×";
                sb.Append('[').Append(ts).Append("] ")
                  .Append(w.ControlContext ?? "<unknown>")
                  .Append(" — ").Append(kind)
                  .Append(" (").Append(times).AppendLine(")");
                if (w.SampleKeys.Count > 0)
                    sb.Append("    keys: ").AppendLine(string.Join(", ", w.SampleKeys));
                if (i < warnings.Count - 1) sb.AppendLine();
            }
            body = sb.ToString();
        }

        var bodyText = new global::Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = body,
            IsTextSelectionEnabled = true,
            TextWrapping = global::Microsoft.UI.Xaml.TextWrapping.Wrap,
            FontFamily = new global::Microsoft.UI.Xaml.Media.FontFamily(
                "Cascadia Code, Consolas, Courier New, monospace"),
            FontSize = 12,
        };
        var scroll = new global::Microsoft.UI.Xaml.Controls.ScrollViewer
        {
            VerticalScrollBarVisibility = global::Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
            MaxHeight = 420,
            Content = bodyText,
        };

        var dialog = new global::Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = warnings.Count == 0
                ? "Keyed-list diagnostics"
                : $"Keyed-list diagnostics ({warnings.Count})",
            Content = scroll,
            CloseButtonText = "Close",
            DefaultButton = global::Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };

        // Already on the UI thread (the menu's click handler dispatches
        // there). Fire and forget — ContentDialog manages its own lifetime;
        // wrap in try so a re-entrant click (dialog already open) doesn't
        // crash the host.
        try
        {
            var op = dialog.ShowAsync();
            op.Completed = (_, _) => ReactorApp.ActiveHostInternal?.RequestRender();
        }
        catch (global::System.Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine(
                $"[Reactor.Devtools] Keyed-list diagnostics dialog failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
