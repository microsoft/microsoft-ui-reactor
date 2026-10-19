using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Formatting and parsing rules of <see cref="DiagnosticText"/> — the text every
/// <see cref="ReactorDiagnostics"/> value goes through. These pin the winapp DevTools agent's
/// behaviour, which this code replaces.
/// </summary>
public class DiagnosticTextTests
{
    // ── Type names ──────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(int), "int")]
    [InlineData(typeof(int?), "int?")]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(object), "object")]
    [InlineData(typeof(string[]), "string[]")]
    [InlineData(typeof(List<int>), "List<int>")]
    [InlineData(typeof(Dictionary<string, List<bool?>>), "Dictionary<string, List<bool?>>")]
    [InlineData(typeof(DayOfWeek), "DayOfWeek")]
    [InlineData(typeof(DayOfWeek?), "DayOfWeek?")]
    [InlineData(typeof(int[,]), "int[,]")]
    [InlineData(typeof(nint), "nint")]
    [InlineData(typeof(nuint?), "nuint?")]
    [InlineData(typeof(string[,,]), "string[,,]")]
    public void FriendlyTypeName_SpellsTypesAsCSharp(Type type, string expected)
        => Assert.Equal(expected, DiagnosticText.FriendlyTypeName(type));

    // ── Formatting ──────────────────────────────────────────────────

    [Fact]
    public void Format_QuotesAndEscapesStrings()
    {
        Assert.Equal("\"a \\\"b\\\"\\n\\\\c\"", DiagnosticText.Format(typeof(string), "a \"b\"\n\\c"));
    }

    // A comma-decimal culture discriminates: invariant output ("1.5") differs from de-DE ("1,5").
    [CulturedFact(new[] { "de-DE" })]
    public void Format_PrimitivesUseInvariantCulture()
    {
        Assert.Equal("1.5", DiagnosticText.Format(typeof(double), 1.5));
        Assert.Equal("True", DiagnosticText.Format(typeof(bool), true));
        Assert.Equal("null", DiagnosticText.Format(typeof(string), null));
    }

    [Fact]
    public void TryCount_ReadsOnlyAPublicGetter()
    {
        Assert.Null(DiagnosticText.TryCount(new PrivateCountGetter()));
        Assert.Equal("PrivateCountGetter (count unknown)", DiagnosticText.Format(typeof(PrivateCountGetter), new PrivateCountGetter()));
    }

    private sealed class PrivateCountGetter : IEnumerable<int>
    {
        private int _count;
        public int Count { private get => throw new InvalidOperationException("private getter ran " + _count); set => _count = value; }
        public IEnumerator<int> GetEnumerator() { yield break; }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void Format_DatesRoundTripThroughTryParse()
    {
        var utc = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        var local = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(-7)).AddTicks(89);
        foreach (var (type, value) in new (Type, object)[] { (typeof(DateTime), utc), (typeof(DateTimeOffset), local) })
        {
            var text = DiagnosticText.Format(type, value);
            Assert.True(DiagnosticText.TryParse(text, type, out var parsed, out var error), error);
            Assert.Equal(value, parsed);
            if (parsed is DateTime d) Assert.Equal(DateTimeKind.Utc, d.Kind);
            if (parsed is DateTimeOffset o) Assert.Equal(local.Offset, o.Offset);
        }
    }

    [Fact]
    public void Format_SummarisesCollectionsByTheCountTheyAdvertise()
    {
        Assert.Equal("List<int> (3 items)", DiagnosticText.Format(typeof(List<int>), new List<int> { 1, 2, 3 }));
        Assert.Equal("int[] (2 items)", DiagnosticText.Format(typeof(int[]), new[] { 1, 2 }));
        // HashSet<T> has no non-generic ICollection; its public Count is read instead.
        Assert.Equal("HashSet<string> (1 item)", DiagnosticText.Format(typeof(HashSet<string>), new HashSet<string> { "a" }));
    }

    [Fact]
    public void Format_NeverEnumeratesALazySequence()
    {
        var lazy = new CountingSequence();
        Assert.Equal("IEnumerable<int> (count unknown)", DiagnosticText.Format(typeof(IEnumerable<int>), Lazy(5)));
        Assert.Equal($"{nameof(CountingSequence)} (count unknown)", DiagnosticText.Format(typeof(CountingSequence), lazy));
        Assert.Equal(0, lazy.Enumerations);
    }

    private static IEnumerable<int> Lazy(int n)
    {
        for (int i = 0; i < n; i++) yield return i;
    }

    [Fact]
    public void Format_CompilerGeneratedCollectionType_IsNamedAfterTheDeclaredType()
    {
        IReadOnlyList<int> value = [1, 2];
        Assert.Equal("IReadOnlyList<int> (2 items)", DiagnosticText.Format(typeof(IReadOnlyList<int>), value));
    }

    [Fact]
    public void Format_ACollectionIsSummarised_ItsOwnToStringIsNeverCalled()
    {
        var bag = new SelfFormatting();
        Assert.Equal($"{nameof(SelfFormatting)} (count unknown)", DiagnosticText.Format(typeof(SelfFormatting), bag));
        Assert.Equal(0, bag.ToStringCalls);
        Assert.Equal(0, bag.Enumerations);
    }

    [Fact]
    public void Format_TruncatesLongValues()
    {
        var text = DiagnosticText.Format(typeof(string), new string('x', 500));
        // The payload is cut, and both quote delimiters survive.
        Assert.Equal("\"" + new string('x', DiagnosticText.MaxValueLength) + "...\"", text);
    }

    [Fact]
    public void Format_TruncationNeverSplitsASurrogatePair()
    {
        // 199 x's then an emoji (two UTF-16 units): the cut would land between its halves.
        var value = new string('x', DiagnosticText.MaxValueLength - 1) + "\U0001F600tail";
        var quoted = DiagnosticText.Format(typeof(string), value);
        Assert.Equal("\"" + new string('x', DiagnosticText.MaxValueLength - 1) + "...\"", quoted);
        var plain = DiagnosticText.FormatPlain(value);
        Assert.Equal(new string('x', DiagnosticText.MaxValueLength - 1) + "...", plain);
    }

    [Fact]
    public void Format_TruncationNeverSplitsAnEscapeSequence()
    {
        // 199 x's then a backslash: escaped, the cut would land between "\" and "\".
        var text = DiagnosticText.Format(typeof(string), new string('x', DiagnosticText.MaxValueLength - 1) + "\\tail");
        Assert.Equal("\"" + new string('x', DiagnosticText.MaxValueLength - 1) + "...\"", text);
    }

    // ── Values are shown as they are ───────────────────────────────

    private sealed record LoginForm(string User, string Password);

    [Fact]
    public void Format_ShowsValuesUnredacted_HidingIsTheInspectorsCall()
    {
        Assert.Equal("\"AccessToken=hunter2\"", DiagnosticText.Format(typeof(string), "AccessToken=hunter2"));
        Assert.Equal("LoginForm { User = me, Password = hunter2 }", DiagnosticText.Format(typeof(LoginForm), new LoginForm("me", "hunter2")));
        Assert.Equal("hunter2", DiagnosticText.FormatPlain("hunter2"));
    }

    private sealed class ThrowsOnToString
    {
        public override string ToString() => throw new InvalidOperationException("boom");
    }

    private sealed class FormattableThrows : IFormattable
    {
        public string ToString(string? format, IFormatProvider? provider) => throw new FormatException("boom");
    }

    private sealed class CountThrows : global::System.Collections.ICollection
    {
        public int Count => throw new InvalidOperationException("boom");
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public void CopyTo(Array array, int index) { }
        public global::System.Collections.IEnumerator GetEnumerator() { yield break; }
    }

    [Fact]
    public void Format_AThrowingCountIsReportedUnknown_InsteadOfFailing()
    {
        Assert.Equal("CountThrows (count unknown)", DiagnosticText.Format(typeof(CountThrows), new CountThrows()));
    }

    [Fact]
    public void Format_AThrowingToStringShowsTheType_InsteadOfFailing()
    {
        Assert.Equal("ThrowsOnToString (ToString() threw InvalidOperationException)",
            DiagnosticText.Format(typeof(ThrowsOnToString), new ThrowsOnToString()));
        Assert.Equal("FormattableThrows (ToString() threw FormatException)",
            DiagnosticText.FormatPlain(new FormattableThrows()));
    }

    [Fact]
    public void FormatPlain_DoesNotQuote()
    {
        Assert.Equal("Save", DiagnosticText.FormatPlain("Save"));
    }

    private sealed class SelfFormattingRefs : IReadOnlyList<global::Microsoft.UI.Reactor.Input.ElementRef>
    {
        public bool Touched;
        public int Count => 2;
        public global::Microsoft.UI.Reactor.Input.ElementRef this[int index] { get { Touched = true; throw new InvalidOperationException(); } }
        public IEnumerator<global::Microsoft.UI.Reactor.Input.ElementRef> GetEnumerator() { Touched = true; throw new InvalidOperationException(); }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public override string ToString() { Touched = true; return "refs"; }
    }

    [Fact]
    public void FormatPlain_SummarisesCollectionsByCount_WithoutCallingTheirToStringOrEnumerator()
    {
        var refs = new SelfFormattingRefs();
        Assert.Equal("SelfFormattingRefs (2 items)", DiagnosticText.FormatPlain(refs));
        Assert.False(refs.Touched);
        var array = new[] { new global::Microsoft.UI.Reactor.Input.ElementRef() };
        Assert.Equal(" (1 item)", DiagnosticText.FormatPlain(array)[^9..]);
    }

    // ── Parsing ─────────────────────────────────────────────────────

    public static IEnumerable<object?[]> Parsed() =>
    [
        ["42", typeof(int), 42],
        ["-7", typeof(long), -7L],
        ["1.5", typeof(double), 1.5],
        ["2.25", typeof(decimal), 2.25m],
        ["true", typeof(bool), true],
        ["False", typeof(bool), false],
        ["x", typeof(char), 'x'],
        ["hello world", typeof(string), "hello world"],
        ["friday", typeof(DayOfWeek), DayOfWeek.Friday],
        ["Read, Write", typeof(Access), Access.Read | Access.Write],
        ["3", typeof(Access), Access.Read | Access.Write],
        ["01:02:03", typeof(TimeSpan), new TimeSpan(1, 2, 3)],
        ["2026-10-05T12:00:00Z", typeof(DateTime), new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc)],
        ["2026-10-05T12:00:00+02:00", typeof(DateTimeOffset), new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(2))],
        ["0f8fad5b-d9cb-469f-a165-70867728950e", typeof(Guid), Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")],
        ["5", typeof(int?), 5],
        ["null", typeof(int?), null],
        ["null", typeof(string), null],
    ];

    [Theory]
    [MemberData(nameof(Parsed))]
    public void TryParse_ParsesTextTypes(string text, Type type, object? expected)
    {
        Assert.True(DiagnosticText.TryParse(text, type, out var value, out var error), error);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("abc", typeof(int), "'abc' is not a valid int")]
    [InlineData("99999999999", typeof(int), "'99999999999' is not a valid int")]
    [InlineData("null", typeof(int), "'null' is not a valid int")]
    [InlineData("maybe", typeof(bool), "'maybe' is not true or false")]
    [InlineData("xy", typeof(char), "'xy' is not a single character")]
    [InlineData("Funday", typeof(DayOfWeek), "use one of Sunday, Monday")]
    [InlineData("8", typeof(DayOfWeek), "'8' is not a valid DayOfWeek")]
    [InlineData("8", typeof(Access), "'8' is not a valid Access")]
    [InlineData("Read, 4", typeof(Access), "is not a valid Access; use one of None, Read, Write")]
    [InlineData("x", typeof(List<int>), "List<int> cannot be set from text")]
    [InlineData("null", typeof(List<int>), "List<int> cannot be set from text")]
    [InlineData("null", typeof(object), "object cannot be set from text")]
    [InlineData("x", typeof(UserCredential), "UserCredential cannot be set from text")]
    public void TryParse_RefusesWithAReason(string text, Type type, string reason)
    {
        Assert.False(DiagnosticText.TryParse(text, type, out var value, out var error));
        Assert.Null(value);
        Assert.Contains(reason, error);
    }

    [Theory]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(Guid?), true)]
    [InlineData(typeof(DayOfWeek), true)]
    [InlineData(typeof(string), true)]
    [InlineData(typeof(object), false)]
    [InlineData(typeof(nint), false)]
    [InlineData(typeof(nuint?), false)]
    [InlineData(typeof(List<int>), false)]
    public void IsEditable(Type type, bool expected) => Assert.Equal(expected, DiagnosticText.IsEditable(type));

    [Flags]
    private enum Access { None = 0, Read = 1, Write = 2 }

    private sealed class CountingSequence : IEnumerable<int>
    {
        public int Enumerations;
        public IEnumerator<int> GetEnumerator() { Enumerations++; while (true) yield return 0; }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SelfFormatting : IEnumerable<string>
    {
        public int ToStringCalls, Enumerations;
        public IEnumerator<string> GetEnumerator() { Enumerations++; yield return "x"; }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        // A ToString that enumerates, as a custom collection's might.
        public override string ToString() { ToStringCalls++; return "Bag[" + string.Join(",", this) + "]"; }
    }
}
