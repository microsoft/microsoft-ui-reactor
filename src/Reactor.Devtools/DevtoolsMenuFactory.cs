using System.Text;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

internal static class DevtoolsMenuFactory
{
    internal static Element Build(
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
                ReactorApp.ActiveHostInternal?.RequestRender();
            });

        var warningCount = ReactorDiagnostics.RecentKeyedListWarnings.Count;
        var keyedListItem = MenuItem(
            warningCount == 0
                ? "Keyed-list diagnostics (none)"
                : $"Keyed-list diagnostics ({warningCount})",
            ShowKeyedListDiagnosticsDialog);

        var builtInItems = userItems.Length > 0
            ? new MenuFlyoutItemBase[] { MenuSeparator(), builtInToggle, keyedListItem }
            : new MenuFlyoutItemBase[] { builtInToggle, keyedListItem };

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
