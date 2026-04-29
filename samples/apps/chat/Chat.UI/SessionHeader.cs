using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace ChatSample.Chat.UI;

public record SessionHeaderProps(ChatThread? Session, ChatTimelineState Timeline);

/// <summary>
/// Displays the selected chat title, contextual metadata, message count, and current intent.
/// </summary>
public class SessionHeader : Component<SessionHeaderProps>
{
    public override Element Render()
    {
        if (Props.Session is not { } ss)
        {
            return VStack(8,
                TextBlock("Chat Sample").FontSize(16).SemiBold().Foreground(SecondaryText),
                Caption("Select a chat to get started").Foreground(TertiaryText)
            ).Padding(20, 12, 20, 12);
        }

        var tl = Props.Timeline;

        // Breadcrumb path: 📁 C:\path · repo/name · 🔀 branch
        var pathParts = new List<Element>();
        if (ss.Cwd is { } cwd)
            pathParts.Add(Caption($"📁 {cwd}").Foreground(TertiaryText));
        if (ss.Repository is { } repo)
        {
            if (pathParts.Count > 0) pathParts.Add(Caption("·").Foreground(TertiaryText));
            pathParts.Add(Caption(repo).Foreground(TertiaryText));
        }
        if (ss.Branch is { } br)
        {
            if (pathParts.Count > 0) pathParts.Add(Caption("·").Foreground(TertiaryText));
            pathParts.Add(Caption($"🔀 {br}").Foreground(TertiaryText));
        }

        var breadcrumb = pathParts.Count > 0
            ? (Element)(FlexRow([.. pathParts]) with { ColumnGap = 6 })
            : Empty();

        var entryCountBadge = Border(
            Caption($"💬 {tl.Entries.Count(e => e.Kind is ChatTimelineItemKind.User or ChatTimelineItemKind.Assistant)}").Foreground(TertiaryText)
        ).Padding(4, 2, 4, 2);

        var intentEl = tl.CurrentIntent is { } intent
            ? Caption($"⚡ {intent}").Foreground(AccentText)
                .Set(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxLines = 1; })
            : Empty();

        return VStack(4,
            FlexRow(
                TextBlock(ss.DisplayTitle).SemiBold().FontSize(16).Flex(grow: 1)
                    .Set(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxLines = 1; }),
                entryCountBadge
            ) with { ColumnGap = 8 },
            breadcrumb,
            intentEl
        ).Padding(20, 12, 20, 12);
    }
}
