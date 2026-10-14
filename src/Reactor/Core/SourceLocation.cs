namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Spec 010 — the C# source location that produced an <see cref="Element"/>.
///
/// <para><see cref="ToString"/> / <see cref="ToShortString"/> keep their
/// <c>file:line</c> forms; read <see cref="ColumnNumber"/> directly.</para>
/// </summary>
/// <param name="FilePath">
/// Absolute path of the source file at compile time. Under a deterministic
/// build (<c>DeterministicSourcePaths</c>, which this repo enables when
/// <c>CI=true</c>) this is the <em>mapped</em> path — e.g.
/// <c>/_/src/Reactor/Elements/Dsl.cs</c> — not a local disk path.
/// </param>
/// <param name="LineNumber">1-based line number of the DSL call site.</param>
/// <param name="ColumnNumber">
/// 1-based column of the DSL call site on <paramref name="LineNumber"/>, or <c>0</c>
/// when the provider did not record one.
///
/// <para>The source-map generator reports the first character of the invoked
/// method's name — the <c>B</c> of <c>Button</c> in <c>Row(Button("a"), Button("b"))</c>,
/// the <c>T</c> of <c>TextBlock</c> in <c>Factories.TextBlock("x")</c> — which is
/// what tells several factory calls on one line apart. In the rare layout where the
/// name and its <c>(</c> sit on different lines, <see cref="LineNumber"/> follows the
/// paren (matching <c>[CallerLineNumber]</c>), so the column is the paren's too and
/// the pair always names a position on the reported line. For an element stamped
/// in argument position (a <c>string</c> converted to an element) it is the first
/// character of the argument expression.</para>
///
/// <para>Counted in UTF-16 code units with a tab counting as one, the convention
/// Roslyn diagnostics and VS Code's <c>file:line:column</c> links use.</para>
/// </param>
public readonly record struct SourceLocation(string FilePath, int LineNumber, int ColumnNumber)
{
    /// <summary>
    /// Full form: <c>C:\src\MainPage.cs:34</c>. Deliberately omits
    /// <see cref="ColumnNumber"/>, so consumers that split this string on its last
    /// <c>:</c> keep working; read <see cref="ColumnNumber"/> directly.
    /// </summary>
    public override string ToString() => $"{FilePath}:{LineNumber}";

    /// <summary>Short display form: filename + line only (<c>MainPage.cs:34</c>).</summary>
    public string ToShortString()
    {
        if (string.IsNullOrEmpty(FilePath)) return LineNumber.ToString(global::System.Globalization.CultureInfo.InvariantCulture);

        // Deliberately not Path.GetFileName: a deterministic-build path uses '/'
        // separators even on Windows, and a Windows-authored path uses '\'.
        // Scan for either so both round-trip to a bare file name.
        int slash = FilePath.LastIndexOfAny(new[] { '/', '\\' });
        string name = slash >= 0 ? FilePath.Substring(slash + 1) : FilePath;
        return $"{name}:{LineNumber}";
    }
}
