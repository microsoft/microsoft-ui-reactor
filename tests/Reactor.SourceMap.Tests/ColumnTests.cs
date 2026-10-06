using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — <see cref="SourceLocation.ColumnNumber"/> through the live interceptors.
///
/// <para>There is no <c>[CallerColumnNumber]</c>, so the oracle is this file's own text:
/// the line the stamp names (itself pinned by a <c>[CallerLineNumber]</c> probe) is read
/// back from disk and the expected column is where the factory's name appears in it. A
/// hard-coded column would silently re-baseline on a reformat; the text cannot.</para>
///
/// <para>The file is located by walking up from the test binaries rather than through
/// <c>CallSite.FilePath</c>, because under <c>CI=true</c> that path is the
/// deterministic <c>/_/tests/...</c> form, which is not a real path on disk.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class ColumnTests : IDisposable
{
    public ColumnTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int Line([CallerLineNumber] int line = 0) => line;

    private static string SourceLine(int lineNumber)
    {
        string relative = global::System.IO.Path.Join("tests", "Reactor.SourceMap.Tests", "ColumnTests.cs");
        var dir = new global::System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = global::System.IO.Path.Join(dir.FullName, relative);
            if (global::System.IO.File.Exists(candidate))
                return global::System.IO.File.ReadAllLines(candidate)[lineNumber - 1];
            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not locate ColumnTests.cs above " + AppContext.BaseDirectory);
    }

    /// <summary>1-based column of the <paramref name="occurrence"/>-th match, or -1.</summary>
    private static int ColumnOf(string text, string needle, int occurrence = 1)
    {
        int index = -1;
        for (int i = 0; i < occurrence; i++)
        {
            index = text.IndexOf(needle, index + 1, StringComparison.Ordinal);
            if (index < 0) return -1;
        }
        return index + 1;
    }

    [Fact]
    public void TwoCallsOnOneLine_GetDistinctColumnsAtTheirNames()
    {
        var a = TextBlock("a"); var b = TextBlock("b"); var line = Line();

        Assert.Equal(line, a.CallSite!.Value.LineNumber);
        Assert.Equal(line, b.CallSite!.Value.LineNumber);

        var text = SourceLine(line);
        // Positive control: the oracle has to find both names, or the equality below
        // would be comparing against -1.
        Assert.True(ColumnOf(text, "TextBlock(\"a\")") > 0, text);
        Assert.True(ColumnOf(text, "TextBlock(\"b\")") > 0, text);

        Assert.Equal(ColumnOf(text, "TextBlock(\"a\")"), a.CallSite.Value.ColumnNumber);
        Assert.Equal(ColumnOf(text, "TextBlock(\"b\")"), b.CallSite.Value.ColumnNumber);
        Assert.NotEqual(a.CallSite, b.CallSite);
    }

    [Fact]
    public void NestedParamsCall_StampsContainerAndChildrenAtTheirOwnNames()
    {
        var stack = VStack(TextBlock("left"), TextBlock("right")); var line = Line();
        var children = global::System.Linq.Enumerable.ToArray(stack.Children);

        var text = SourceLine(line);
        Assert.Equal(ColumnOf(text, "VStack("), stack.CallSite!.Value.ColumnNumber);
        Assert.Equal(ColumnOf(text, "TextBlock(\"left\")"), children[0]!.CallSite!.Value.ColumnNumber);
        Assert.Equal(ColumnOf(text, "TextBlock(\"right\")"), children[1]!.CallSite!.Value.ColumnNumber);
    }

    [Fact]
    public void QualifiedCall_ColumnIsTheMethodNameNotTheReceiver()
    {
        var el = Factories.TextBlock("qualified"); var line = Line();

        var text = SourceLine(line);
        Assert.Equal(ColumnOf(text, "TextBlock(\"qualified\")"), el.CallSite!.Value.ColumnNumber);
        Assert.NotEqual(ColumnOf(text, "Factories."), el.CallSite.Value.ColumnNumber);
    }

    [Fact]
    public void ConvertedArgument_ColumnIsTheArgumentExpression()
    {
        // `"converted"` reaches VStack's Element parameter through Element's implicit
        // string conversion, so it is stamped in argument position (mechanism 2).
        var stack = VStack("converted"); var line = Line();
        var child = global::System.Linq.Enumerable.Single(stack.Children)!;

        var text = SourceLine(line);
        Assert.Equal(line, child.CallSite!.Value.LineNumber);
        Assert.Equal(ColumnOf(text, "\"converted\""), child.CallSite.Value.ColumnNumber);
    }

    [Fact]
    public void GenericFactories_ColumnIsTheMethodName()
    {
        // A generic name is a GenericNameSyntax, not an IdentifierNameSyntax; the column
        // must still land on the name's first character, for explicit and inferred calls.
        var explicitCall = Component<ColumnProbeComponent>(); var inferred = ForEach(new[] { 1 }, i => TextBlock(i.ToString())); var line = Line();

        var text = SourceLine(line);
        Assert.True(ColumnOf(text, "Component<") > 0 && ColumnOf(text, "ForEach(") > 0, text);
        Assert.Equal(line, explicitCall.CallSite!.Value.LineNumber);
        Assert.Equal(ColumnOf(text, "Component<"), explicitCall.CallSite.Value.ColumnNumber);
        Assert.Equal(ColumnOf(text, "ForEach("), inferred.CallSite!.Value.ColumnNumber);
    }

    private sealed class ColumnProbeComponent : Component
    {
        public override Element Render() => TextBlock("probe");
    }

    [Fact]
    public void TwoArgumentConstructor_LeavesColumnUnknown()
    {
        var location = new SourceLocation("F.cs", 3);

        Assert.Equal(0, location.ColumnNumber);
        Assert.Equal(new SourceLocation("F.cs", 3, 0), location);
        Assert.NotEqual(new SourceLocation("F.cs", 3, 7), location);
        Assert.Equal("F.cs:3", new SourceLocation("F.cs", 3, 7).ToString());
    }

    [Fact]
    public void ColumnDoesNotWidenTheStructOn64Bit()
    {
        // On 64-bit the column packs into the padding after LineNumber, so ElementExtras
        // (which stores SourceLocation? inline) keeps its measured size. On x86 there is
        // no padding to fill: reference (4) + two ints = 12, up from 8.
        Assert.Equal(IntPtr.Size == 8 ? 16 : 12, Unsafe.SizeOf<SourceLocation>());
    }
}
