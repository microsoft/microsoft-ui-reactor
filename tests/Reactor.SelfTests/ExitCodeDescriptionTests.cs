using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.SelfTests;

/// <summary>
/// Guards <see cref="SelfTestBatch.DescribeExitCode"/>, the diagnostic that tells a triager
/// whether a truncated selftest run means "the host faulted" or "something killed it".
///
/// <para>Worth testing rather than eyeballing: the mapping is the thing a human acts on, the
/// NTSTATUS values arrive as negative <see cref="int"/> and must be compared as
/// <see cref="uint"/> (a signed comparison silently matches nothing), and the hedged wording is
/// deliberate — the rule is a strong prior, not a proof, because TerminateProcess lets a caller
/// choose any exit code.</para>
/// </summary>
[TestClass]
public class ExitCodeDescriptionTests
{
    // The four codes that actually occur in this harness. 0xC000027B is the WinUI/WinRT one;
    // 0xC0000409 has repo precedent in the SwipeControl stress crash.
    [TestMethod]
    [DataRow(unchecked((int)0xC0000005u), "STATUS_ACCESS_VIOLATION")]
    [DataRow(unchecked((int)0xC000027Bu), "STATUS_STOWED_EXCEPTION")]
    [DataRow(unchecked((int)0xC0000409u), "STATUS_STACK_BUFFER_OVERRUN")]
    [DataRow(unchecked((int)0xC00000FDu), "STATUS_STACK_OVERFLOW")]
    public void NtStatus_codes_are_named_and_attributed_to_a_self_crash(int exitCode, string expectedName)
    {
        var text = SelfTestBatch.DescribeExitCode(exitCode);

        StringAssert.Contains(text, expectedName,
            "the symbolic name is what makes the code searchable");
        StringAssert.Contains(text, "crashed on its own",
            "an NTSTATUS must point the triager at the faulting fixture, not an external killer");
        StringAssert.Contains(text, "strong prior",
            "the verdict must stay hedged — TerminateProcess can carry any exit code, so a " +
            "triager who reads this as certain would stop looking too early");
    }

    /// <summary>
    /// An NTSTATUS this method does not name individually must STILL be attributed to a crash —
    /// the 0xC0000000 shape is the signal, not membership of the known-code list. Without this,
    /// adding the shape check would be indistinguishable from a four-entry lookup table.
    /// </summary>
    [TestMethod]
    public void Unnamed_NtStatus_still_reads_as_a_self_crash()
    {
        var text = SelfTestBatch.DescribeExitCode(unchecked((int)0xC0000374u)); // heap corruption

        StringAssert.Contains(text, "crashed on its own");
        StringAssert.Contains(text, "0xC0000374", "the raw value must survive so nobody has to trust the mapping");
    }

    /// <summary>
    /// The killer codes must NOT be described as a crash, must carry the trailer caveat (exit 1
    /// is ambiguous — a genuine fixture failure also exits 1), and must NOT name an agent. -1 is
    /// reachable from this harness's own synthesized watchdog value, an external kill, a parent
    /// reap and a CI job-object teardown, so claiming "external kill" would be a false verdict.
    /// </summary>
    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    public void Killer_codes_are_not_called_a_crash_and_name_the_disambiguator(int exitCode)
    {
        var text = SelfTestBatch.DescribeExitCode(exitCode);

        Assert.IsFalse(text.Contains("crashed on its own"),
            $"exit {exitCode} does not indicate a fault");
        StringAssert.Contains(text, "cause is NOT",
            "the message must refuse to name an agent — -1 has at least four sources here");
        StringAssert.Contains(text, "trailer",
            "exit 1 is ambiguous, so the message must point at the TAP trailer to resolve it");
    }

    /// <summary>
    /// -1 specifically must warn that this harness fabricates it. <c>RunProcess</c> returns
    /// <c>timedOut ? -1 : process.ExitCode</c>, discarding the real code on its own watchdog kill
    /// — so a reader who takes -1 as evidence of an *external* killer would be chasing a ghost.
    /// This is the assertion that fails if the wording ever regresses to "external kill".
    /// </summary>
    [TestMethod]
    public void MinusOne_warns_that_the_harness_synthesizes_it()
    {
        var text = SelfTestBatch.DescribeExitCode(-1);

        StringAssert.Contains(text, "SYNTHESIZES",
            "RunProcess returns -1 for its own watchdog kill, which is the most likely source " +
            "of -1 in this harness and must not be mistaken for an external agent");
    }

    /// <summary>
    /// The CLR's unhandled-managed-exception tag must be attributed to a self-crash even though
    /// it is NOT NTSTATUS-shaped: <c>0xE0434352 &amp; 0xF0000000</c> is <c>0xE0000000</c>, so the
    /// <c>0xC0000000</c> mask does not catch it and it would otherwise emit a bare raw value with
    /// no verdict. For a .NET host that is arguably the likeliest crash mode of all, and it is
    /// already recognised elsewhere in the repo (MxcSandbox).
    /// </summary>
    [TestMethod]
    public void Clr_managed_exception_is_attributed_to_a_self_crash()
    {
        var text = SelfTestBatch.DescribeExitCode(unchecked((int)0xE0434352u));

        StringAssert.Contains(text, "0xE0434352", "the raw value must survive");
        StringAssert.Contains(text, "MANAGED",
            "a managed crash must be distinguished from a native fault — the triager needs the " +
            "stack trace, not a faulting-fixture hunt");
        StringAssert.Contains(text, "strong prior",
            "the managed branch must hedge exactly as the NTSTATUS branch does: an exit code is " +
            "chosen by whoever terminates the process, so it can never prove the absence of an " +
            "external terminator. Asserting certainty here would contradict this method's own " +
            "documented discipline.");
        Assert.IsFalse(text.Contains("NTSTATUS"),
            "0xE0434352 is not NTSTATUS-shaped; labelling it as such would send the reader to " +
            "the wrong diagnosis");
        Assert.IsFalse(text.Contains("external kill"),
            "a managed exception is a self-crash, not a kill");
    }

    /// <summary>
    /// Guards the mask arithmetic that makes the previous test necessary. <c>0xE0434352 &amp;
    /// 0xF0000000</c> is <c>0xE0000000</c>, not <c>0xC0000000</c>, so the NTSTATUS shape mask
    /// cannot reach the CLR tag. If someone widens that mask to swallow it, the dedicated
    /// managed-exception wording becomes unreachable and the two classes collapse to identical
    /// guidance — which is what this asserts. (The mask arithmetic itself is a compile-time
    /// constant and is deliberately stated in prose rather than asserted; asserting a constant
    /// proves nothing at run time.)
    /// </summary>
    [TestMethod]
    public void Clr_tag_is_not_matched_by_the_NtStatus_shape_mask()
    {
        var clr = SelfTestBatch.DescribeExitCode(unchecked((int)0xE0434352u));
        var nt = SelfTestBatch.DescribeExitCode(unchecked((int)0xC0000005u));

        // Both classifications must actually be PRESENT. Asserting only that `clr` lacks
        // "NTSTATUS" would pass if the CLR branch were deleted and `clr` fell through to the bare
        // raw line — a negative assertion satisfied by the feature's absence, which is precisely
        // the vacuous shape this suite exists to avoid.
        StringAssert.Contains(clr, "MANAGED", "the CLR branch must be reached at all");
        StringAssert.Contains(nt, "NTSTATUS", "the NTSTATUS branch must be reached at all");

        // ...and they must be DIFFERENT classifications, not merely different raw codes.
        Assert.IsFalse(clr.Contains("NTSTATUS"),
            "widening the 0xC mask to cover the CLR tag would route it to the NTSTATUS wording");
        Assert.IsFalse(nt.Contains("MANAGED"),
            "the native-fault branch must not claim a managed exception");
    }

    /// <summary>
    /// A plain code with no story attached gets the raw value and nothing invented. Asserted as
    /// EXACT equality rather than as a pair of "does not contain" guards: those would pass for
    /// any newly-invented default verdict that merely avoided the two phrases they name, so they
    /// could not actually detect the thing this test claims to detect.
    /// </summary>
    [TestMethod]
    public void Ordinary_exit_code_gets_no_invented_verdict()
    {
        var text = SelfTestBatch.DescribeExitCode(3);

        Assert.AreEqual("Exit code: 3 (0x00000003)", text,
            "an ordinary code must produce the raw line and nothing else — no verdict, no arrow, " +
            "no guidance. Exact equality is what makes this non-vacuous.");
    }

    /// <summary>
    /// Signed-vs-unsigned regression guard. NTSTATUS values arrive as a negative <see cref="int"/>
    /// (<c>0xC0000005</c> is <c>-1073741819</c>), so a signed comparison against the hex literal
    /// matches nothing and every crash would silently fall through to the "no verdict" branch.
    /// The negativity is a compile-time fact stated here rather than asserted; what is asserted
    /// is the observable consequence — the named text is only reachable via the uint cast.
    /// </summary>
    [TestMethod]
    public void NtStatus_is_matched_unsigned_not_signed()
    {
        var text = SelfTestBatch.DescribeExitCode(unchecked((int)0xC0000005u));

        StringAssert.Contains(text, "STATUS_ACCESS_VIOLATION",
            "a signed comparison would never match, so this text is only reachable via the uint cast");
        StringAssert.Contains(text, "crashed on its own",
            "the shape mask is also uint-based; a signed mask would leave this unclassified");
    }
}
