using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace ChatSample.Chat.UI;

public record StatusBarProps(
    ChatThread Session,
    string[] AvailableModels,
    Action<string> OnModelChanged,
    Action<bool> OnPermissionsChanged);

/// <summary>
/// Renders per-chat metadata plus model and permission controls at the bottom of the chat pane.
/// </summary>
public class StatusBar : Component<StatusBarProps>
{
    public override Element Render()
    {
        var sb = Props.Session;
        var currentModel = sb.Model ?? "";
        var models = Props.AvailableModels;
        var modelIndex = Array.IndexOf(models, currentModel);
        if (modelIndex < 0 && models.Length > 0) modelIndex = 0;

        var permOptions = new[] { "Auto-approve", "Prompt" };
        var permIndex = 0; // default auto-approve

        var metaParts = new List<string>();
        var shortId = sb.Id.Length > 16 ? sb.Id[..16] : sb.Id;
        metaParts.Add(shortId);
        if (sb.Compute is { } compute) metaParts.Add(compute);
        if (sb.ProfileName is { } profile) metaParts.Add(profile);

        return Border(
            (FlexRow(
                Caption(string.Join("  ·  ", metaParts)).Foreground(TertiaryText)
                    .VAlign(VerticalAlignment.Center).Flex(grow: 1),
                ComboBox(permOptions, permIndex, idx =>
                {
                    Props.OnPermissionsChanged(idx == 0);
                }).Set(cb =>
                {
                    cb.MinWidth = 120;
                    cb.CornerRadius = new CornerRadius(4);
                }).VAlign(VerticalAlignment.Center),
                models.Length > 0
                    ? ComboBox(models, Math.Max(modelIndex, 0), idx =>
                    {
                        if (idx >= 0 && idx < models.Length)
                            Props.OnModelChanged(models[idx]);
                    }).Set(cb =>
                    {
                        cb.MinWidth = 140;
                        cb.CornerRadius = new CornerRadius(4);
                    }).VAlign(VerticalAlignment.Center)
                    : Caption(currentModel).Foreground(TertiaryText).VAlign(VerticalAlignment.Center)
            ) with { ColumnGap = 12 })
        ).Padding(12, 8, 12, 8);
    }
}
