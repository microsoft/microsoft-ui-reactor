namespace DemoScriptTool.App.Services;

/// <summary>
/// Result of attempting to parse <c>demo-script.md</c>. The UI surfaces this
/// in an inline banner (spec §Error Surfacing).
/// </summary>
public sealed record DemoScriptParseError(int Line, int Column, string Message)
{
    public override string ToString() => $"line {Line}: {Message}";
}
