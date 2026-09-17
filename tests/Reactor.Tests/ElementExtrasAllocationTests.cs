using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 010 — pins the allocation cost of bucketing <c>CallSite</c> into
/// <see cref="ElementExtras"/>.
///
/// <para><b>Why this exists.</b> The M12 control-model benchmark shows flag-off as
/// byte-identical to baseline, and that measurement is real but narrow: M12's leaves are
/// extras-free, so their bucket is <c>null</c> and the new field costs nothing. An
/// element that already carries a behavioral extra — attached properties, theme
/// bindings, animations, resource overrides, context values — allocates the bucket
/// regardless, and that bucket is now 24 bytes wider whether or not source mapping is
/// enabled and whether or not the consumer has the generator at all.</para>
///
/// <para>That cost is accepted rather than hidden: it is bounded (one field on an object
/// that already exists), proportional to elements carrying extras rather than to all
/// elements, and it buys not widening EVERY element by 24 bytes, which measured at
/// +24.0 B/op on M12 when <c>CallSite</c> was declared inline on the record. These tests
/// make the tax visible so it cannot grow unnoticed.</para>
/// </summary>
public class ElementExtrasAllocationTests
{
    /// <summary>
    /// The inline width a nullable <see cref="SourceLocation"/> contributes: an
    /// 8-byte string reference, a 4-byte line, and the nullable flag, padded to 24 on
    /// x64. This is the exact tax added to every extras bucket.
    /// </summary>
    [Fact]
    public void CallSiteContributesTwentyFourBytesToTheBucket()
    {
        Assert.Equal(24, Unsafe.SizeOf<SourceLocation?>());
    }

    /// <summary>
    /// Regression guard starting from a NON-NULL behavioral bucket, which is the case
    /// the M12 benchmark cannot reach. If someone adds another bucketed field, this
    /// fails and forces a deliberate decision rather than silent growth.
    /// </summary>
    [Fact]
    public void BehavioralExtrasBucketStaysAtItsMeasuredSize()
    {
        const int N = 50_000;
        const double ExpectedBytes = 152.0;

        // Warm up so the first-touch allocations of the type handle are not counted.
        for (int i = 0; i < 1_000; i++)
        {
            var warm = new ElementExtras();
            GC.KeepAlive(warm);
        }

        var sink = new ElementExtras[N];
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < N; i++)
        {
            sink[i] = new ElementExtras();
        }
        var after = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(sink);

        var perInstance = (after - before) / (double)N;

        Assert.True(
            Math.Abs(perInstance - ExpectedBytes) < 0.5,
            $"ElementExtras is {perInstance:F2} B/instance, expected {ExpectedBytes:F2}. " +
            "Spec 010 bucketed CallSite here, which cost 24 of those bytes and is paid by " +
            "every element carrying a behavioral extra even when source mapping is off. " +
            "If this grew, another bucketed field was added: measure the new tax and update " +
            "both this number and the cost note in docs/guide/source-mapping.md, rather than " +
            "just relaxing the assertion.");
    }

    /// <summary>
    /// The other half of the trade-off, and the reason the bucket was chosen: an element
    /// with no extras must still allocate NO bucket, so the common leaf pays nothing.
    /// Without this, "we bucketed it to keep leaves free" would be an unverified claim.
    /// </summary>
    [Fact]
    public void AnUnstampedLeafAllocatesNoBucketAtAll()
    {
        var bare = new TextBlockElement("hi");

        Assert.Null(bare.Extensions);
        Assert.Null(bare.CallSite);
    }

    /// <summary>
    /// And a stamped one does allocate the bucket — so the test above is pinning a real
    /// distinction rather than a type that never carries extras in the first place.
    /// </summary>
    [Fact]
    public void AStampedLeafDoesAllocateTheBucket()
    {
        var stamped = new TextBlockElement("hi") with { CallSite = new SourceLocation("F.cs", 1) };

        Assert.NotNull(stamped.Extensions);
        Assert.Equal(1, stamped.CallSite!.Value.LineNumber);
    }
}
