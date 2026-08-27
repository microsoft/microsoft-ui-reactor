using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.SourceMap.Spike;

/// <summary>
/// Spec 010 Route B — measurement 2 supplement.
///
/// <para>The M12 control-model bench barely touches <c>Factories</c> (3 call
/// sites), so it measures the reconciler-side cost of the source-map flag but
/// NOT the cost of the stamp itself. This suite measures the stamp directly:
/// how many extra bytes an intercepted DSL call site allocates when the flag is
/// on, versus the same call site with the flag off.</para>
///
/// <para>The number matters because the interceptor stamps via a record
/// <c>with</c> expression, which allocates a full copy of the element. That is
/// the price Route B pays for not changing any factory signature — Route A
/// writes the location during construction and copies nothing.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class SourceMapAllocationTests
{
    private readonly ITestOutputHelper _out;

    public SourceMapAllocationTests(ITestOutputHelper output) => _out = output;

    private static long Measure(Func<Element> make, int iterations)
    {
        // Warm up so first-call JIT / type-init allocation is not attributed to
        // the measured body.
        for (int i = 0; i < 200; i++) _ = make();

        var before = global::System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) _ = make();
        var after = global::System.GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / iterations;
    }

    [Fact]
    public void StampCost_IsOneExtraElementCopyPerCallSite()
    {
        const int Iterations = 20_000;

        ReactorSourceMap.Enabled = false;
        long off = Measure(static () => TextBlock("probe"), Iterations);

        ReactorSourceMap.Enabled = true;
        long on = Measure(static () => TextBlock("probe"), Iterations);

        ReactorSourceMap.Enabled = false;
        long offAgain = Measure(static () => TextBlock("probe"), Iterations);

        _out.WriteLine($"TextBlock  flag-off = {off} B/call");
        _out.WriteLine($"TextBlock  flag-on  = {on} B/call");
        _out.WriteLine($"TextBlock  delta    = {on - off} B/call");

        // The interceptor is present in BOTH measurements — only the runtime
        // branch differs — so the delta isolates the `with` copy and nothing
        // else.
        Assert.True(on > off, $"expected the stamp to cost something; off={off} on={on}");

        // Symmetry check: turning the flag back off must return to the original
        // cost. Without this, a one-way ratchet (e.g. a cached stamp) would look
        // like a cheap stamp on the second pass.
        Assert.Equal(off, offAgain);
    }

    [Fact]
    public void StampCost_ParamsFactory()
    {
        const int Iterations = 20_000;

        ReactorSourceMap.Enabled = false;
        long off = Measure(static () => VStack(TextBlock("a"), TextBlock("b")), Iterations);

        ReactorSourceMap.Enabled = true;
        long on = Measure(static () => VStack(TextBlock("a"), TextBlock("b")), Iterations);

        ReactorSourceMap.Enabled = false;

        _out.WriteLine($"VStack(2)  flag-off = {off} B/call");
        _out.WriteLine($"VStack(2)  flag-on  = {on} B/call");
        _out.WriteLine($"VStack(2)  delta    = {on - off} B/call  (3 intercepted call sites: VStack + 2 TextBlock)");

        Assert.True(on > off, $"expected the stamp to cost something; off={off} on={on}");
    }
}
