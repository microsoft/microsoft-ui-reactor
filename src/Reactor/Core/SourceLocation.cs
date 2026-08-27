namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Spec 010 — the C# source location that produced an <see cref="Element"/>.
///
/// <para>The shape is deliberately <c>(FilePath, LineNumber)</c> and nothing
/// more: that is the greatest common denominator of the two candidate
/// providers. <c>[CallerFilePath]</c> / <c>[CallerLineNumber]</c> cannot supply
/// a column, so carrying one here would make the two routes produce different
/// public API and stop them being drop-in alternatives. A Roslyn-interceptor
/// provider does know the column and can add it later as a purely additive
/// member.</para>
/// </summary>
/// <param name="FilePath">
/// Absolute path of the source file at compile time. Under a deterministic
/// build (<c>DeterministicSourcePaths</c>, which this repo enables when
/// <c>CI=true</c>) this is the <em>mapped</em> path — e.g.
/// <c>/_/src/Reactor/Elements/Dsl.cs</c> — not a local disk path.
/// </param>
/// <param name="LineNumber">1-based line number of the DSL call site.</param>
public readonly record struct SourceLocation(string FilePath, int LineNumber)
{
    /// <summary>Full form: <c>C:\src\MainPage.cs:34</c>.</summary>
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
