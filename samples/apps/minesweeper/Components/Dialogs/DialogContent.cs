using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Minesweeper.Game;
using Minesweeper.Persistence;
using static Microsoft.UI.Reactor.Factories;

namespace Minesweeper.Components.Dialogs;

/// <summary>
/// Pure renderers for the three dialogs. Each returns the Element that goes
/// inside <see cref="ModalOverlay"/>'s body slot. The overlay itself owns
/// the title bar, button row, and backdrop.
/// </summary>
internal static class DialogContent
{
    /// <summary>High-scores list — three rows, one per classic difficulty.</summary>
    public static Element HighScores(HighScores scores)
    {
        Element Row(string label, HighScoreEntry? entry) =>
            HStack(12,
                TextBlock(label).Width(140).SemiBold(),
                entry is null
                    ? TextBlock("(no record yet)").Foreground(Theme.SecondaryText)
                    : TextBlock($"{entry.Seconds}s").Foreground(Theme.PrimaryText)
            );

        return VStack(8,
            Row("Beginner", scores.Beginner),
            Row("Intermediate", scores.Intermediate),
            Row("Expert", scores.Expert)
        );
    }

    /// <summary>New-best celebration. The score is auto-saved when this
    /// dialog opens — there's no input to fill in.</summary>
    public static Element NewBest(DifficultyKind kind, int seconds)
    {
        return VStack(8,
            TextBlock($"You set a new best time on {kind}: {seconds} seconds!")
                .FontSize(16).SemiBold(),
            TextBlock("Saved to your high scores.").Foreground(Theme.SecondaryText)
        );
    }

    /// <summary>
    /// Custom-board form. Reports validation errors inline so the player
    /// sees why the Start button is disabled.
    /// </summary>
    public static Element CustomBoard(
        string rows, Action<string> onRowsChanged,
        string columns, Action<string> onColumnsChanged,
        string mines, Action<string> onMinesChanged,
        string? error)
    {
        return VStack(10,
            HStack(8, TextBlock("Rows (4–24):").Width(140), TextBox(rows, onRowsChanged).Width(80)),
            HStack(8, TextBlock("Columns (4–30):").Width(140), TextBox(columns, onColumnsChanged).Width(80)),
            HStack(8, TextBlock("Mines:").Width(140), TextBox(mines, onMinesChanged).Width(80)),
            error is null
                ? TextBlock("").Height(0)
                : TextBlock(error).Foreground(Theme.SystemCritical)
        );
    }
}
