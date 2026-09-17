using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — <c>#line</c> parity.
///
/// <para>Tooling that emits C# on a developer's behalf (T4, Razor, and any custom
/// codegen) marks its output with <c>#line</c> so that positions resolve back to the
/// file the developer actually edits. <c>[CallerFilePath]</c>, <c>[CallerLineNumber]</c>
/// and the debugger all honour that remapping. <c>SyntaxTree.GetLineSpan</c> does NOT —
/// it reports the physical position in the generated <c>.cs</c>.</para>
///
/// <para>So a generator that reads the unmapped span reports a line in generated
/// <c>.cs</c> that nobody edits, and silently diverges from the CallerInfo route this
/// design claims parity with.</para>
///
/// <para>The path has a further trap: a directive names its file relatively
/// (<c>#line 5000 "virtual-source.cs"</c>) and <c>[CallerFilePath]</c> reports it
/// RESOLVED against the physical file's directory, so emitting the mapped span's bare
/// <c>Path</c> gives a different string for the same file. Both halves are pinned here
/// against live <c>[CallerLineNumber]</c>/<c>[CallerFilePath]</c> probes subject to the
/// same directive — the only oracle that can tell correct mapping from either the
/// physical position or an unresolved relative name.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class LineDirectiveParityTests : IDisposable
{
    public LineDirectiveParityTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int Line([CallerLineNumber] int line = 0) => line;

    private static string File([CallerFilePath] string path = "") => path;

    /// <summary>
    /// Under a <c>#line</c> directive the stamped line must equal what
    /// <c>[CallerLineNumber]</c> reports on the same physical line. Reading the
    /// unmapped span yields this file's real line number instead, which differs from
    /// the remapped 5000 by hundreds — so the assertion cannot pass by coincidence.
    /// </summary>
    [Fact]
    public void StampedLineFollowsLineDirective()
    {
#line 5000 "virtual-source.cs"
        var el = TextBlock("hi"); int expected = Line();
#line default

        Assert.NotNull(el.CallSite);
        Assert.Equal(expected, el.CallSite!.Value.LineNumber);
        Assert.Equal(5000, el.CallSite.Value.LineNumber);
    }

    /// <summary>
    /// The path follows the directive too, matching <c>[CallerFilePath]</c>. Note what
    /// that means concretely: the directive names its file relatively, and CallerInfo
    /// reports it RESOLVED against the physical file's directory — so emitting the
    /// mapped span's bare <c>Path</c> would produce "virtual-source.cs" where the other
    /// route produces an absolute path. Asserted against the probe, which is the only
    /// oracle that captures that resolution.
    /// </summary>
    [Fact]
    public void StampedPathFollowsLineDirective()
    {
#line 6000 "virtual-source.cs"
        var el = TextBlock("hi"); string expected = File();
#line default

        Assert.NotNull(el.CallSite);
        Assert.Equal(expected, el.CallSite!.Value.FilePath);
        Assert.EndsWith("virtual-source.cs", el.CallSite.Value.FilePath, global::System.StringComparison.Ordinal);
        Assert.DoesNotContain("LineDirectiveParityTests.cs", el.CallSite.Value.FilePath, global::System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Positive control: without a directive the two routes still agree, so a failure
    /// above is specifically about <c>#line</c> handling and not about the generator
    /// being off or the probe being broken in this file.
    /// </summary>
    [Fact]
    public void WithoutADirectiveBothRoutesStillAgree()
    {
        var el = TextBlock("hi"); int expectedLine = Line(); string expectedPath = File();

        Assert.NotNull(el.CallSite);
        Assert.Equal(expectedLine, el.CallSite!.Value.LineNumber);
        Assert.Equal(expectedPath, el.CallSite.Value.FilePath);
    }
}

