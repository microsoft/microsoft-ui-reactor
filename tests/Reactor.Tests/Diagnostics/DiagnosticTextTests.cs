using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Formatting, redaction and parsing rules of <see cref="DiagnosticText"/> — the text every
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
    public void FriendlyTypeName_SpellsTypesAsCSharp(Type type, string expected)
        => Assert.Equal(expected, DiagnosticText.FriendlyTypeName(type));

    // ── Formatting ──────────────────────────────────────────────────

    [Fact]
    public void Format_QuotesAndEscapesStrings()
    {
        Assert.Equal("\"a \\\"b\\\"\\n\\\\c\"", DiagnosticText.Format("", typeof(string), "a \"b\"\n\\c").Text);
    }

    [Fact]
    public void Format_PrimitivesUseInvariantCulture()
    {
        var previous = global::System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            global::System.Globalization.CultureInfo.CurrentCulture = new global::System.Globalization.CultureInfo("de-DE");
            Assert.Equal("1.5", DiagnosticText.Format("", typeof(double), 1.5).Text);
            Assert.Equal("True", DiagnosticText.Format("", typeof(bool), true).Text);
            Assert.Equal("null", DiagnosticText.Format("", typeof(string), null).Text);
        }
        finally { global::System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Format_SummarisesCollectionsByTheCountTheyAdvertise()
    {
        Assert.Equal("List<int> (3 items)", DiagnosticText.Format("", typeof(List<int>), new List<int> { 1, 2, 3 }).Text);
        Assert.Equal("int[] (2 items)", DiagnosticText.Format("", typeof(int[]), new[] { 1, 2 }).Text);
        // HashSet<T> has no non-generic ICollection; its public Count is read instead.
        Assert.Equal("HashSet<string> (1 items)", DiagnosticText.Format("", typeof(HashSet<string>), new HashSet<string> { "a" }).Text);
    }

    [Fact]
    public void Format_NeverEnumeratesALazySequence()
    {
        var lazy = new CountingSequence();
        Assert.Equal("IEnumerable<int> (count unknown)", DiagnosticText.Format("", typeof(IEnumerable<int>), Lazy(5)).Text);
        Assert.Equal($"{nameof(CountingSequence)} (count unknown)", DiagnosticText.Format("", typeof(CountingSequence), lazy).Text);
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
        Assert.Equal("IReadOnlyList<int> (2 items)", DiagnosticText.Format("", typeof(IReadOnlyList<int>), value).Text);
    }

    [Fact]
    public void Format_ACollectionThatFormatsItself_KeepsItsText()
    {
        Assert.Equal("Bag[x]", DiagnosticText.Format("", typeof(SelfFormatting), new SelfFormatting()).Text);
    }

    [Fact]
    public void Format_TruncatesLongValues()
    {
        var text = DiagnosticText.Format("", typeof(string), new string('x', 500)).Text;
        Assert.Equal(DiagnosticText.MaxValueLength + 3, text.Length);
        Assert.EndsWith("...", text);
    }

    // ── Redaction ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Password")]
    [InlineData("adminPassword")]
    [InlineData("ApiSecret")]
    [InlineData("UserCredential")]
    [InlineData("AccessToken")]
    [InlineData("RefreshToken")]
    [InlineData("ApiKey")]
    [InlineData("PrivateKey")]
    [InlineData("ConnectionString")]
    public void Format_RedactsBySecretName(string name)
    {
        var (text, redacted) = DiagnosticText.Format(name, typeof(string), "hunter2");
        Assert.True(redacted);
        Assert.Equal("<redacted>", text);
    }

    [Fact]
    public void Format_RedactsBySecretType_DeclaredOrRuntime()
    {
        Assert.True(DiagnosticText.Format("", typeof(UserCredential), new UserCredential("u", "p")).Redacted);
        Assert.True(DiagnosticText.Format("", typeof(object), new UserCredential("u", "p")).Redacted);
        using var secure = new global::System.Security.SecureString();
        Assert.True(DiagnosticText.Format("", typeof(global::System.Security.SecureString), secure).Redacted);
    }

    [Fact]
    public void Format_DoesNotRedactNullOrOrdinaryNames()
    {
        Assert.False(DiagnosticText.Format("Password", typeof(string), null).Redacted);
        Assert.False(DiagnosticText.Format("PasswordHint", typeof(string), "h").Redacted);
        Assert.False(DiagnosticText.Format("Name", typeof(string), "n").Redacted);
        // A bare "Key" is not a secret: WinUI's AccessKey is an ordinary modifier.
        Assert.Equal(("S", false), DiagnosticText.FormatPlain("AccessKey", "S"));
    }

    [Fact]
    public void Format_RedactsSecretNamedOrSecretTypedCollections_RatherThanSummarisingThem()
    {
        Assert.Equal(("<redacted>", true), DiagnosticText.Format("ApiSecret", typeof(List<string>), new List<string> { "k" }));
        Assert.True(DiagnosticText.Format("", typeof(List<UserCredential>), new List<UserCredential>()).Redacted);
        Assert.True(DiagnosticText.Format("", typeof(UserCredential[]), new UserCredential[1]).Redacted);
        Assert.True(DiagnosticText.Format("", typeof(object), new Dictionary<string, UserCredential>()).Redacted);
    }

    private sealed record LoginForm(string User, string Password);
    private sealed record Hints(string User, string PasswordHint);

    [Fact]
    public void Format_RedactsAnObjectWhoseOwnTextShowsASecretMember()
    {
        // A record's ToString() prints its members — Password included.
        var (text, redacted) = DiagnosticText.Format("Form", typeof(LoginForm), new LoginForm("me", "hunter2"));
        Assert.True(redacted);
        Assert.DoesNotContain("hunter2", text);

        Assert.Equal(("Hints { User = me, PasswordHint = pet }", false), DiagnosticText.Format("", typeof(Hints), new Hints("me", "pet")));
    }

    private sealed class HandWritten(string text)
    {
        public override string ToString() => text;
    }

    private sealed class ThrowsOnToString : IEnumerable<int>
    {
        public override string ToString() => throw new InvalidOperationException("boom");
        public IEnumerator<int> GetEnumerator() { yield break; }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
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
        Assert.Equal(("CountThrows (count unknown)", false), DiagnosticText.Format("", typeof(CountThrows), new CountThrows()));
    }

    [Fact]
    public void Format_AThrowingToStringShowsTheType_InsteadOfFailing()
    {
        Assert.Equal(("ThrowsOnToString (ToString() threw InvalidOperationException)", false),
            DiagnosticText.Format("", typeof(ThrowsOnToString), new ThrowsOnToString()));
        Assert.Equal(("FormattableThrows (ToString() threw FormatException)", false),
            DiagnosticText.FormatPlain("", new FormattableThrows()));
    }

    [Theory]
    [InlineData("ApiSecret: hunter2")]
    [InlineData("user=me; AccessToken=hunter2")]
    public void Format_RedactsHandWrittenTextNamingASecretMember(string text)
    {
        var (shown, redacted) = DiagnosticText.Format("", typeof(HandWritten), new HandWritten(text));
        Assert.True(redacted);
        Assert.DoesNotContain("hunter2", shown);
    }

    [Theory]
    [InlineData("AccessToken=hunter2")]
    [InlineData("Server=db;User Id=me;Password=hunter2;")]
    [InlineData("apiKey: hunter2")]
    [InlineData("{\"Password\":\"hunter2\"}")]
    [InlineData("{ 'accessToken': 'hunter2' }")]
    public void Format_RedactsAStringCarryingALabelledSecret(string text)
    {
        var (shown, redacted) = DiagnosticText.Format("", typeof(string), text);
        Assert.True(redacted);
        Assert.DoesNotContain("hunter2", shown);
    }

    [Theory]
    [InlineData("Password:")]
    [InlineData("Enter your Password: ")]
    [InlineData("Forgot password?")]
    public void Format_KeepsABareSecretLabel(string text)
        => Assert.False(DiagnosticText.Format("", typeof(string), text).Redacted);

    [Fact]
    public void FormatPlain_DoesNotQuote_ButStillRedacts()
    {
        Assert.Equal(("Save", false), DiagnosticText.FormatPlain("AutomationName", "Save"));
        Assert.Equal(("<redacted>", true), DiagnosticText.FormatPlain("Password", "x"));
    }

    private sealed class FormattableSecret : IFormattable
    {
        public string ToString(string? format, IFormatProvider? provider) => "ApiKey: hunter2";
    }

    [Fact]
    public void FormatPlain_RedactsLabelledSecretText_InEveryValueShape()
    {
        Assert.Equal(("<redacted>", true), DiagnosticText.FormatPlain("ToolTip", "AccessToken=hunter2"));
        Assert.Equal(("<redacted>", true), DiagnosticText.FormatPlain("", new FormattableSecret()));
        Assert.Equal(("<redacted>", true), DiagnosticText.FormatPlain("", new HandWritten("Password = hunter2")));
        // A bare label is ordinary UI text, as is a JSON member with an empty value.
        Assert.Equal(("Password:", false), DiagnosticText.FormatPlain("ToolTip", "Password:"));
        Assert.Equal(("{\"Password\":\"\"}", false), DiagnosticText.FormatPlain("ToolTip", "{\"Password\":\"\"}"));
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
    [InlineData("8", typeof(DayOfWeek), "'8' is not a DayOfWeek")]
    [InlineData("8", typeof(Access), "'8' is not a Access")]
    [InlineData("Read, 4", typeof(Access), "is not a Access; use one of None, Read, Write")]
    [InlineData("x", typeof(List<int>), "a List<int> value cannot be typed as text")]
    [InlineData("null", typeof(List<int>), "a List<int> value cannot be typed as text")]
    [InlineData("null", typeof(object), "a object value cannot be typed as text")]
    [InlineData("x", typeof(UserCredential), "a UserCredential value cannot be typed as text")]
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
        public IEnumerator<string> GetEnumerator() { yield return "x"; }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public override string ToString() => "Bag[x]";
    }
}
