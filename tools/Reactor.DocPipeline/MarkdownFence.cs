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
        // Fence markers count only with zero to three spaces of indent; deeper is code content.
        line = line.TrimEnd('\r');
        var m = FenceRun().Match(line);
        var rest = m.Success ? line[(m.Index + m.Length)..] : "";
        if (_open is null)
        {
            // A backtick fence's info string can't contain a backtick, so a
            // line like ```x``` is an inline code span, not an opening fence.
            if (!m.Success || (m.Groups[1].Value[0] == '`' && rest.Contains('`'))) return false;
            _open = m.Groups[1].Value;
            return true;
        }

        var run = m.Groups[1].Value;
        if (m.Success && run[0] == _open[0] && run.Length >= _open.Length && rest.Trim().Length == 0)
        {
            _open = null;
        }
        return true;
    }

    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})")]
    private static partial Regex FenceRun();
}
