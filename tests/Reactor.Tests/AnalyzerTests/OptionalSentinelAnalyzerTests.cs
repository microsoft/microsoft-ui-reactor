using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="OptionalSentinelAnalyzer"/> (<c>REACTOR_OPT_001</c>) and its
/// <see cref="OptionalSentinelCodeFix"/>. Stubs a minimal Reactor-shaped
/// <c>Optional&lt;T&gt;</c> (in the real <c>Microsoft.UI.Reactor</c> namespace, since
/// the analyzer keys off that namespace) plus a handful of element records so the
/// syntactic match against <c>new … { SelectedIndex = -1 }</c> / <c>x with { … }</c>
/// and the single <c>GetTypeInfo</c> confirmation resolve without pulling the
/// framework in.
/// </summary>
public class OptionalSentinelAnalyzerTests
{
    // `Optional<T>` mirrors src/Reactor/Core/Optional.cs: the implicit T -> Optional<T>
    // operator (the whole reason the sentinel silently force-asserts), plus static
    // `Unset`/`Of`. `IsExternalInit` is required for the `init`-only element records.
    private const string Stubs = @"
namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit { }
}

namespace Microsoft.UI.Reactor
{
    public readonly struct Optional<T>
    {
        private readonly bool _hasValue;
        private readonly T _value;
        private Optional(T value) { _hasValue = true; _value = value; }
        public static Optional<T> Unset => default;
        public static Optional<T> Of(T value) => new Optional<T>(value);
        public static implicit operator Optional<T>(T value) => new Optional<T>(value);
    }

    public sealed record ComboBoxElement
    {
        public Optional<int> SelectedIndex { get; init; }
    }

    public sealed record PipsPagerElement
    {
        public Optional<int> SelectedPageIndex { get; init; }
    }

    public sealed record CalendarDatePickerElement
    {
        public Optional<System.DateTimeOffset?> Date { get; init; }
    }

    // Legacy int-typed selection member (mirrors the non-Optional SelectedIndex
    // elements in Element.cs). Passes the syntactic fast path, rejected by the
    // GetTypeInfo gate.
    public sealed record LegacyListElement
    {
        public int SelectedIndex { get; init; }
    }

    // Optional<T> member whose name is NOT a selection sentinel.
    public sealed record MiscElement
    {
        public Optional<int> Offset { get; init; }

        // 'Time' is deliberately NOT in the analyzer allowlist (the real
        // TimePickerElement.Time is Optional<TimeSpan>, which can take neither -1
        // nor null). Typed Optional<int> here only so 'Time = -1' compiles and the
        // negative test can assert the allowlist — not the type — excludes it.
        public Optional<int> Time { get; init; }
    }
}
";

    private static string WithUsing(string body) => Stubs + @"
namespace App
{
    using Microsoft.UI.Reactor;

    class C
    {
" + body + @"
    }
}";

    // ── Positive ────────────────────────────────────────────────────────

    [Fact]
    public async Task Fires_For_SelectedIndex_In_With_Expression()
    {
        var source = WithUsing(@"
        ComboBoxElement M(ComboBoxElement el)
            => el with { {|REACTOR_OPT_001:SelectedIndex = -1|} };");

        await new CSharpAnalyzerTest<OptionalSentinelAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_SelectedIndex_In_Object_Initializer()
    {
        var source = WithUsing(@"
        ComboBoxElement M()
            => new ComboBoxElement { {|REACTOR_OPT_001:SelectedIndex = -1|} };");

        await new CSharpAnalyzerTest<OptionalSentinelAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_SelectedPageIndex()
    {
        var source = WithUsing(@"
        PipsPagerElement M(PipsPagerElement el)
            => el with { {|REACTOR_OPT_001:SelectedPageIndex = -1|} };");

        await new CSharpAnalyzerTest<OptionalSentinelAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Date_Null_Sentinel()
    {
        var source = WithUsing(@"
        CalendarDatePickerElement M(CalendarDatePickerElement el)
            => el with { {|REACTOR_OPT_001:Date = null|} };");

        await new CSharpAnalyzerTest<OptionalSentinelAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative ────────────────────────────────────────────────────────

    [Fact]
    public async Task No_Diagnostic_For_Non_Sentinel_Value()
    {
        // SelectedIndex = 2 is a genuine controlled value, not a sentinel.
        var source = WithUsing(@"
        ComboBoxElement M(ComboBoxElement el)
            => el with { SelectedIndex = 2 };");

        await new CSharpAnalyzerTest<OptionalSentinelAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Member_Outside_Allowlist()
    {
        // Offset is Optional<int> but not a selection sentinel member.
        var source = WithUsing(@"
        MiscElement M(MiscElement el)
            => el with { Offset = -1 };");

        await new CSharpAnalyzerTest<OptionalSentinelAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Time_Excluded_From_Allowlist()
    {
        // 'Time' is intentionally omitted from the allowlist: the real
        // TimePickerElement.Time is Optional<TimeSpan> (non-nullable), so -1/null
        // are never type-compatible there. Even a sentinel-compatible Optional<int>
        // Time must not fire — the exclusion is by member name, not type.
        var source = WithUsing(@"
        MiscElement M(MiscElement el)
            => el with { Time = -1 };");

        await new CSharpAnalyzerTest<OptionalSentinelAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Near-miss (trips the syntactic fast path, rejected by GetTypeInfo) ─

    [Fact]
    public async Task No_Diagnostic_For_Sentinel_On_Non_Optional_Member()
    {
        // LegacyListElement.SelectedIndex is a plain int: the member name and the
        // -1 literal match the fast path, but the semantic type gate rejects it.
        var source = WithUsing(@"
        LegacyListElement M(LegacyListElement el)
            => el with { SelectedIndex = -1 };");

        await new CSharpAnalyzerTest<OptionalSentinelAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code fix round-trips ────────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Unset_Rewrites_SelectedIndex()
    {
        var before = WithUsing(@"
        ComboBoxElement M(ComboBoxElement el)
            => el with { {|REACTOR_OPT_001:SelectedIndex = -1|} };");

        var after = WithUsing(@"
        ComboBoxElement M(ComboBoxElement el)
            => el with { SelectedIndex = Optional<int>.Unset };");

        await new CSharpCodeFixTest<OptionalSentinelAnalyzer, OptionalSentinelCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = OptionalSentinelAnalyzer.DiagnosticId + ":Unset",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Of_Rewrites_SelectedIndex()
    {
        var before = WithUsing(@"
        ComboBoxElement M(ComboBoxElement el)
            => el with { {|REACTOR_OPT_001:SelectedIndex = -1|} };");

        var after = WithUsing(@"
        ComboBoxElement M(ComboBoxElement el)
            => el with { SelectedIndex = Optional<int>.Of(-1) };");

        await new CSharpCodeFixTest<OptionalSentinelAnalyzer, OptionalSentinelCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = OptionalSentinelAnalyzer.DiagnosticId + ":Of",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // No `using Microsoft.UI.Reactor;` at the call site — the fix must emit a
    // qualified Optional reference so the rewrite still compiles.
    [Fact]
    public async Task CodeFix_Unset_Compiles_Without_Using()
    {
        var before = Stubs + @"
namespace App
{
    class C
    {
        Microsoft.UI.Reactor.ComboBoxElement M(Microsoft.UI.Reactor.ComboBoxElement el)
            => el with { {|REACTOR_OPT_001:SelectedIndex = -1|} };
    }
}";

        var after = Stubs + @"
namespace App
{
    class C
    {
        Microsoft.UI.Reactor.ComboBoxElement M(Microsoft.UI.Reactor.ComboBoxElement el)
            => el with { SelectedIndex = Microsoft.UI.Reactor.Optional<int>.Unset };
    }
}";

        await new CSharpCodeFixTest<OptionalSentinelAnalyzer, OptionalSentinelCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = OptionalSentinelAnalyzer.DiagnosticId + ":Unset",
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
