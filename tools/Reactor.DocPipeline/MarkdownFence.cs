using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Tracks fenced code blocks line by line. A fence closes only on a line of the
/// same character that is at least as long as the opening fence and carries
/// nothing after it, so a four-backtick block can show a three-backtick example.
/// </summary>
internal sealed partial class MarkdownFence
{
    private string? _open;

    /// <summary>True when <paramref name="line"/> is a fence line or inside fenced code.</summary>
    public bool InCode(string line)
    {
        var trimmed = line.TrimEnd('\r').TrimStart();
        var m = FenceRun().Match(trimmed);
        if (_open is null)
        {
            // A backtick fence's info string can't contain a backtick, so a
            // line like ```x``` is an inline code span, not an opening fence.
            if (!m.Success || (m.Value[0] == '`' && trimmed[m.Length..].Contains('`'))) return false;
            _open = m.Value;
            return true;
        }

        if (m.Success && m.Value[0] == _open[0] && m.Length >= _open.Length && trimmed[m.Length..].Trim().Length == 0)
        {
            _open = null;
        }
        return true;
    }

    [GeneratedRegex(@"^(`{3,}|~{3,})")]
    private static partial Regex FenceRun();
}
