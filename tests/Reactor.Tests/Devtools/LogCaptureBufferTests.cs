using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Contract tests for <see cref="LogCaptureBuffer"/>: seq monotonicity, ring
/// drop semantics, filter shapes, and the long-poll wake-up path.
/// </summary>
public class LogCaptureBufferTests
{
    [Fact]
    public void Append_AssignsMonotonicSeq_StartingAtOne()
    {
        var buf = new LogCaptureBuffer();
        buf.Append(LogSource.Stdout, null, "a");
        buf.Append(LogSource.Stdout, null, "b");

        var result = buf.Query();
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(1, result.Entries[0].Seq);
        Assert.Equal(2, result.Entries[1].Seq);
        Assert.Equal(3, result.NextSeq);
        Assert.Equal(0, result.Dropped);
    }

    [Fact]
    public void Query_SinceSeq_IsInclusive()
    {
        var buf = new LogCaptureBuffer();
        for (int i = 0; i < 5; i++) buf.Append(LogSource.Stdout, null, $"line-{i}");

        var page1 = buf.Query(sinceSeq: 0, tail: 2);
        // tail keeps the last 2 of the 5, so seqs 4 and 5.
        Assert.Equal(2, page1.Entries.Count);
        Assert.Equal(4, page1.Entries[0].Seq);

        // Pass nextSeq directly as the next `since` — with inclusive semantics
        // and no new entries, the window is empty.
        var page2 = buf.Query(sinceSeq: page1.NextSeq);
        Assert.Empty(page2.Entries);

        // But the very next appended entry should come back on the same cursor.
        buf.Append(LogSource.Stdout, null, "after");
        var page3 = buf.Query(sinceSeq: page1.NextSeq);
        Assert.Single(page3.Entries);
        Assert.Equal("after", page3.Entries[0].Text);
    }

    [Fact]
    public void Capacity_DropsOldestAndCountsDropped()
    {
        // Cap is 2 KB. Entry overhead is ~64 bytes + 2*text. 200-char strings
        // take ~464 bytes each, so ~4 fit — the 5th append evicts the first.
        var buf = new LogCaptureBuffer(capacityBytes: 2 * 1024);
        for (int i = 0; i < 20; i++) buf.Append(LogSource.Stdout, null, new string('x', 200));

        var result = buf.Query();
        Assert.True(result.Entries.Count < 20);
        Assert.True(result.Dropped > 0);
        // Seq is still monotonic even though we dropped old entries.
        Assert.Equal(21, result.NextSeq);
        Assert.Equal(20 - result.Entries.Count, result.Dropped);
    }

    [Fact]
    public void Query_FiltersBySource()
    {
        var buf = new LogCaptureBuffer();
        buf.Append(LogSource.Stdout, null, "out");
        buf.Append(LogSource.Stderr, null, "err");
        buf.Append(LogSource.Debug, null, "dbg");

        var errs = buf.Query(source: LogSource.Stderr);
        Assert.Single(errs.Entries);
        Assert.Equal("err", errs.Entries[0].Text);
    }

    [Fact]
    public void Query_FiltersByRegex_FallsBackToSubstring()
    {
        var buf = new LogCaptureBuffer();
        buf.Append(LogSource.Stdout, null, "nav: cache hit");
        buf.Append(LogSource.Stdout, null, "render: 2ms");
        buf.Append(LogSource.Stdout, null, "nav: cache miss");

        var regex = buf.Query(filterRegex: "nav.*cache");
        Assert.Equal(2, regex.Entries.Count);

        // A malformed regex becomes a substring match — no exception.
        var substring = buf.Query(filterRegex: "[unclosed");
        Assert.Empty(substring.Entries); // no line contains literal "[unclosed"
    }

    [Fact]
    public async Task WaitForNewAsync_ReturnsImmediately_WhenDataIsAlreadyNewer()
    {
        var buf = new LogCaptureBuffer();
        buf.Append(LogSource.Stdout, null, "a");
        buf.Append(LogSource.Stdout, null, "b");

        // sinceSeq=0, buffer has seq 1 & 2 → fast path, no blocking.
        // WaitAsync provides the outer budget; on the fast path it should be near-instant.
        await buf.WaitForNewAsync(0, 5_000).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task WaitForNewAsync_WakesOnAppend()
    {
        var buf = new LogCaptureBuffer();
        // Use an effectively-infinite internal timeout so the only completion
        // path is the wake from Append — eliminates the "did it fall through to
        // the internal timeout?" ambiguity that a Task.WhenAny+Task.Delay race
        // can't disambiguate, especially on contended CI runners where the
        // post-signal continuation can be delayed past a tight outer race.
        var wait = buf.WaitForNewAsync(1, timeoutMs: int.MaxValue);

        Assert.False(wait.IsCompleted);

        buf.Append(LogSource.Stdout, null, "first");
        // 30s outer budget is generous for thread-pool starvation; if the wake
        // never propagates, WaitAsync throws TimeoutException with a clear stack.
        await wait.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task WaitForNewAsync_RespectsTimeout()
    {
        var buf = new LogCaptureBuffer();
        var sw = global::System.Diagnostics.Stopwatch.StartNew();
        // Wait for seq >= 1 on an empty buffer — exercises the timeout path.
        await buf.WaitForNewAsync(1, 150);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 100, $"WaitForNewAsync returned after only {sw.ElapsedMilliseconds}ms (expected ≥ ~150ms)");
        // Upper bound is a "didn't get stuck forever" sanity check — generous so
        // thread-pool starvation on a contended CI runner doesn't fail it.
        Assert.True(sw.ElapsedMilliseconds < 30_000, $"WaitForNewAsync blocked for {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void LongEntry_IsTruncated_NotDropped()
    {
        var buf = new LogCaptureBuffer();
        var huge = new string('z', 2 * 1024 * 1024); // 2 MB, above 1 MB cap
        buf.Append(LogSource.Stdout, null, huge);
        var result = buf.Query();
        Assert.Single(result.Entries);
        Assert.True(result.Entries[0].Text.Length <= 1 << 20);
    }

    [Fact]
    public void TeeTextWriter_AppendsLineOnNewline()
    {
        var buf = new LogCaptureBuffer();
        var tee = new TeeTextWriter(forward: null, buffer: buf, source: LogSource.Stdout);
        tee.WriteLine("hello");
        tee.Write("partial");
        // Partial line has not been flushed as a log entry yet.
        Assert.Single(buf.Query().Entries);
        Assert.Equal("hello", buf.Query().Entries[0].Text);

        tee.WriteLine(" world");
        var entries = buf.Query().Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal("partial world", entries[1].Text);
    }

    [Fact]
    public void BufferTraceListener_CapturesDebugWriteLine()
    {
        // Isolate the listener — don't pollute global Debug.Listeners.
        var buf = new LogCaptureBuffer();
        var listener = new BufferTraceListener(buf);
        listener.WriteLine("hello from debug");
        listener.Write("half-");
        listener.WriteLine("line");

        var entries = buf.Query().Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal(LogSource.Debug, entries[0].Source);
        Assert.Equal("hello from debug", entries[0].Text);
        Assert.Equal("half-line", entries[1].Text);
    }

    // ────────────────────────────────────────────────────────────────────
    // TeeTextWriter — additional branches (Write overloads, \r handling,
    // forward stream, Flush, Encoding fallback). Each test pins behavior
    // tied to the stdio MCP contract: no corruption of the JSON-RPC frame
    // on the wire, no entries silently dropped, no \r leaking into entries.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void TeeTextWriter_WriteChar_AccumulatesUntilNewline()
    {
        // Bug this catches: a regression where Write(char) bypasses the
        // newline detector and either emits one entry per char or never emits.
        var buf = new LogCaptureBuffer();
        var tee = new TeeTextWriter(forward: null, buffer: buf, source: LogSource.Stdout);
        tee.Write('h');
        tee.Write('i');
        Assert.Empty(buf.Query().Entries);
        tee.Write('\n');
        var entries = buf.Query().Entries;
        Assert.Single(entries);
        Assert.Equal("hi", entries[0].Text);
    }

    [Fact]
    public void TeeTextWriter_WriteCharArraySlice_RespectsIndexAndCount()
    {
        // Bug this catches: regression where Write(char[], int, int) ignores
        // the slice window and dumps the whole buffer into the log — caller
        // expects only data[index..index+count] to be captured.
        var buf = new LogCaptureBuffer();
        var tee = new TeeTextWriter(forward: null, buffer: buf, source: LogSource.Stdout);
        var data = "XX_keep_YY".ToCharArray();
        tee.Write(data, 3, 4);   // "keep"
        tee.WriteLine();
        var entries = buf.Query().Entries;
        Assert.Single(entries);
        Assert.Equal("keep", entries[0].Text);
    }

    [Fact]
    public void TeeTextWriter_WriteCharArray_SplitsOnEmbeddedNewline()
    {
        // Bug this catches: AppendString in the char[] overload not detecting
        // embedded '\n', producing one fused entry "first\nsecond" instead of
        // two distinct lines.
        var buf = new LogCaptureBuffer();
        var tee = new TeeTextWriter(forward: null, buffer: buf, source: LogSource.Stdout);
        var data = "first\nsecond\n".ToCharArray();
        tee.Write(data, 0, data.Length);
        var entries = buf.Query().Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal("first", entries[0].Text);
        Assert.Equal("second", entries[1].Text);
    }

    [Fact]
    public void TeeTextWriter_WriteString_StripsCarriageReturn()
    {
        // Bug this catches: regression that stops dropping '\r', so Windows
        // CRLF logs surface with trailing '\r' in entries — corrupts grep
        // filters and the agent-visible log view.
        var buf = new LogCaptureBuffer();
        var tee = new TeeTextWriter(forward: null, buffer: buf, source: LogSource.Stdout);
        tee.Write("alpha\r\nbeta\r\n");
        var entries = buf.Query().Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal("alpha", entries[0].Text);
        Assert.Equal("beta", entries[1].Text);
    }

    [Fact]
    public void TeeTextWriter_WriteString_HandlesMultipleEmbeddedNewlines()
    {
        // Bug this catches: AppendString only splits on the first '\n' — would
        // collapse multi-line writes (common with WriteLine of pre-formatted
        // multi-line strings) into a single entry.
        var buf = new LogCaptureBuffer();
        var tee = new TeeTextWriter(forward: null, buffer: buf, source: LogSource.Stdout);
        tee.Write("a\nb\nc\n");
        var entries = buf.Query().Entries;
        Assert.Equal(3, entries.Count);
        Assert.Equal(new[] { "a", "b", "c" }, entries.Select(e => e.Text).ToArray());
    }

    [Fact]
    public void TeeTextWriter_WriteNullString_IsNoOp()
    {
        // Bug this catches: Write((string?)null) producing an entry or NRE.
        // Some TraceListener pipelines emit null when no message is provided.
        var buf = new LogCaptureBuffer();
        var tee = new TeeTextWriter(forward: null, buffer: buf, source: LogSource.Stdout);
        tee.Write((string?)null);
        Assert.Empty(buf.Query().Entries);
    }

    [Fact]
    public void TeeTextWriter_Flush_ForcesPendingPartialLine()
    {
        // Bug this catches: an app calls Console.Out.Flush() expecting
        // diagnostics to be visible to the agent, but the partial line sits
        // in _lineBuf forever. The contract is "Flush makes pending work
        // observable" — without it, MCP `logs` polls miss in-progress lines.
        var buf = new LogCaptureBuffer();
        var tee = new TeeTextWriter(forward: null, buffer: buf, source: LogSource.Stdout);
        tee.Write("no-newline-yet");
        Assert.Empty(buf.Query().Entries);
        tee.Flush();
        var entries = buf.Query().Entries;
        Assert.Single(entries);
        Assert.Equal("no-newline-yet", entries[0].Text);
    }

    [Fact]
    public void TeeTextWriter_Flush_NoPending_DoesNotEmitEmptyEntry()
    {
        // Bug this catches: Flush() emitting an empty "" entry when nothing
        // is pending, polluting the log with phantom blank lines after every
        // pipeline checkpoint.
        var buf = new LogCaptureBuffer();
        var tee = new TeeTextWriter(forward: null, buffer: buf, source: LogSource.Stdout);
        tee.Flush();
        Assert.Empty(buf.Query().Entries);
    }

    [Fact]
    public void TeeTextWriter_WriteLineNoArg_FlushesPendingPartial()
    {
        // Bug this catches: WriteLine() with no arg writes only '\n' but
        // doesn't flush already-accumulated text — would lose "partial"
        // (the trailing line of a multi-call sequence).
        var buf = new LogCaptureBuffer();
        var tee = new TeeTextWriter(forward: null, buffer: buf, source: LogSource.Stdout);
        tee.Write("partial");
        tee.WriteLine();
        var entries = buf.Query().Entries;
        Assert.Single(entries);
        Assert.Equal("partial", entries[0].Text);
    }

    [Fact]
    public void TeeTextWriter_Forward_ReceivesWrites()
    {
        // Bug this catches: regression where forward is dropped silently,
        // so the underlying console / parent capture pipeline goes dark when
        // a tee is installed (e.g. xunit test logger loses all output).
        var buf = new LogCaptureBuffer();
        var sink = new StringWriter();
        var tee = new TeeTextWriter(forward: sink, buffer: buf, source: LogSource.Stdout);
        tee.WriteLine("hello");
        tee.Write("more");
        tee.Flush();

        Assert.Equal("hello" + Environment.NewLine + "more", sink.ToString());
        // Buffer still sees both entries — capture is in addition to forward.
        var entries = buf.Query().Entries;
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void TeeTextWriter_Encoding_FollowsForwardWhenPresent()
    {
        // Bug this catches: Encoding always returning UTF8 ignoring inner
        // writer — breaks consumers (StreamWriter/process redirection) that
        // pick byte width from Encoding.GetBytes.
        var buf = new LogCaptureBuffer();
        using var inner = new StreamWriter(new MemoryStream(), Encoding.Unicode);
        var tee = new TeeTextWriter(forward: inner, buffer: buf, source: LogSource.Stdout);
        Assert.Equal(Encoding.Unicode, tee.Encoding);
    }

    [Fact]
    public void TeeTextWriter_Encoding_DefaultsToUtf8WhenNoForward()
    {
        // Bug this catches: NullReferenceException on .Encoding when forward
        // is null (stdio MCP transport always passes null).
        var buf = new LogCaptureBuffer();
        var tee = new TeeTextWriter(forward: null, buffer: buf, source: LogSource.Stdout);
        Assert.Equal(Encoding.UTF8, tee.Encoding);
    }

    // ────────────────────────────────────────────────────────────────────
    // BufferTraceListener — additional branches (null/empty no-op, Flush
    // semantics, CR stripping, embedded-newline splitting).
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void BufferTraceListener_WriteNullOrEmpty_IsNoOp()
    {
        // Bug this catches: regression where Write(null) emits a null entry
        // or NRE. Trace plumbing routinely passes null messages from
        // TraceSource overloads with no args.
        var buf = new LogCaptureBuffer();
        var listener = new BufferTraceListener(buf);
        listener.Write((string?)null);
        listener.Write("");
        Assert.Empty(buf.Query().Entries);
    }

    [Fact]
    public void BufferTraceListener_FlushEmitsPendingPartialLine()
    {
        // Bug this catches: Debug.Flush after a partial Debug.Write doesn't
        // surface the line — diagnostics swallowed on graceful shutdown.
        var buf = new LogCaptureBuffer();
        var listener = new BufferTraceListener(buf);
        listener.Write("partial");
        Assert.Empty(buf.Query().Entries);
        listener.Flush();
        Assert.Single(buf.Query().Entries);
        Assert.Equal("partial", buf.Query().Entries[0].Text);
    }

    [Fact]
    public void BufferTraceListener_Write_StripsCRAndSplitsOnLF()
    {
        // Bug this catches: a CRLF-terminated Trace.Write payload produces
        // an entry with trailing '\r' (corrupts grep), or fuses multiple
        // lines into one entry when '\n' is embedded mid-string.
        var buf = new LogCaptureBuffer();
        var listener = new BufferTraceListener(buf);
        listener.Write("one\r\ntwo\r\n");
        var entries = buf.Query().Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal("one", entries[0].Text);
        Assert.Equal("two", entries[1].Text);
    }

    [Fact]
    public void BufferTraceListener_WriteLineNullPayload_EmitsBlankLine()
    {
        // Bug this catches: WriteLine(null) NRE'ing or dropping the line
        // terminator. The Trace contract is "WriteLine emits a line regardless".
        var buf = new LogCaptureBuffer();
        var listener = new BufferTraceListener(buf);
        listener.Write("seed");
        listener.WriteLine((string?)null);
        var entries = buf.Query().Entries;
        Assert.Single(entries);
        Assert.Equal("seed", entries[0].Text);
    }
}

/// <summary>
/// Tests that mutate process-wide Console.Out / Console.Error / Trace.Listeners
/// must run on the ConsoleTests collection so concurrent tests don't see a
/// partly-replaced Console. All tests here save and restore Console state.
/// </summary>
[Collection("ConsoleTests")]
public class LogCaptureInstallTests
{
    [Fact]
    public void Install_IsIdempotent_ReturnsSameBufferOnRepeatCalls()
    {
        // Bug this catches: a second Install() creating a fresh buffer (which
        // disconnects the live stream from agents holding the prior cursor)
        // and/or adding a second BufferTraceListener (unbounded growth of
        // Trace.Listeners across host restarts in long-lived test runs).
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var listenersBefore = Trace.Listeners.Count;
        LogCaptureInstall.ResetForTests();
        try
        {
            var first = LogCaptureInstall.Install(capacityBytes: 64 * 1024, forwardConsole: false);
            var second = LogCaptureInstall.Install(capacityBytes: 64 * 1024, forwardConsole: false);
            Assert.Same(first, second);
            Assert.Same(first, LogCaptureInstall.Shared);
            // Only one BufferTraceListener should have been added across both calls.
            var added = Trace.Listeners.Count - listenersBefore;
            Assert.Equal(1, added);
        }
        finally
        {
            // Restore Console + remove our listener so no other test sees the tee.
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            for (int i = Trace.Listeners.Count - 1; i >= 0; i--)
            {
                if (Trace.Listeners[i] is BufferTraceListener)
                    Trace.Listeners.RemoveAt(i);
            }
            LogCaptureInstall.ResetForTests();
        }
    }

    [Fact]
    public void Install_CapturesConsoleWrites_IntoBuffer()
    {
        // Bug this catches: Install() reporting "buffer ready" without
        // actually swapping Console.Out — agent's `logs` MCP tool returns
        // nothing despite the host emitting via Console.WriteLine.
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var listenersBefore = Trace.Listeners.Count;
        LogCaptureInstall.ResetForTests();
        try
        {
            var buf = LogCaptureInstall.Install(capacityBytes: 64 * 1024, forwardConsole: false);
            Console.WriteLine("captured stdout");
            Console.Error.WriteLine("captured stderr");

            var entries = buf.Query().Entries;
            // Exactly two entries — one stdout, one stderr — in the order written.
            Assert.Equal(2, entries.Count);
            Assert.Equal("captured stdout", entries[0].Text);
            Assert.Equal(LogSource.Stdout, entries[0].Source);
            Assert.Equal("captured stderr", entries[1].Text);
            Assert.Equal(LogSource.Stderr, entries[1].Source);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            for (int i = Trace.Listeners.Count - 1; i >= 0; i--)
            {
                if (Trace.Listeners[i] is BufferTraceListener)
                    Trace.Listeners.RemoveAt(i);
            }
            LogCaptureInstall.ResetForTests();
        }
    }

    [Fact]
    public void Install_ForwardConsoleFalse_DoesNotWriteToOriginalOutStream()
    {
        // Bug this catches: stdio MCP transport corruption — if forward=false
        // is ignored, the host's debug Console.WriteLine bleeds into the
        // JSON-RPC frame and the agent disconnects mid-session. This was
        // the original motivation for the forward flag.
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var capturedOriginalOut = new StringWriter();
        Console.SetOut(capturedOriginalOut);
        LogCaptureInstall.ResetForTests();
        try
        {
            var buf = LogCaptureInstall.Install(capacityBytes: 64 * 1024, forwardConsole: false);
            Console.WriteLine("must-not-reach-original-out");
            // The buffer must have it; the original stream must NOT.
            Assert.Contains(buf.Query().Entries, e => e.Text == "must-not-reach-original-out");
            Assert.DoesNotContain("must-not-reach-original-out", capturedOriginalOut.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            for (int i = Trace.Listeners.Count - 1; i >= 0; i--)
            {
                if (Trace.Listeners[i] is BufferTraceListener)
                    Trace.Listeners.RemoveAt(i);
            }
            LogCaptureInstall.ResetForTests();
        }
    }

    [Fact]
    public void Install_ForwardConsoleTrue_AlsoWritesToOriginalOutStream()
    {
        // Bug this catches: regression where the forward path is dropped
        // unconditionally — non-stdio hosts (interactive shells, log tails)
        // lose their console output entirely.
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var capturedOriginalOut = new StringWriter();
        Console.SetOut(capturedOriginalOut);
        LogCaptureInstall.ResetForTests();
        try
        {
            var buf = LogCaptureInstall.Install(capacityBytes: 64 * 1024, forwardConsole: true);
            Console.WriteLine("must-reach-both");
            Assert.Contains(buf.Query().Entries, e => e.Text == "must-reach-both");
            Assert.Contains("must-reach-both", capturedOriginalOut.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            for (int i = Trace.Listeners.Count - 1; i >= 0; i--)
            {
                if (Trace.Listeners[i] is BufferTraceListener)
                    Trace.Listeners.RemoveAt(i);
            }
            LogCaptureInstall.ResetForTests();
        }
    }

    [Fact]
    public void Install_CapturesTraceWriteLine_AsDebugSource()
    {
        // Bug this catches: BufferTraceListener not actually added to
        // Trace.Listeners (or added with the wrong source tag), so
        // Debug.WriteLine output never reaches the agent.
        var originalOut = Console.Out;
        var originalError = Console.Error;
        LogCaptureInstall.ResetForTests();
        try
        {
            var buf = LogCaptureInstall.Install(capacityBytes: 64 * 1024, forwardConsole: false);
            Trace.WriteLine("trace-line");
            Trace.Flush();
            var entries = buf.Query().Entries;
            Assert.Contains(entries, e => e.Source == LogSource.Debug && e.Text == "trace-line");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            for (int i = Trace.Listeners.Count - 1; i >= 0; i--)
            {
                if (Trace.Listeners[i] is BufferTraceListener)
                    Trace.Listeners.RemoveAt(i);
            }
            LogCaptureInstall.ResetForTests();
        }
    }

    [Fact]
    public void Shared_IsNullBeforeInstall_AndAfterResetForTests()
    {
        // Bug this catches: ResetForTests not clearing the static reference,
        // so subsequent tests reuse a buffer they didn't install.
        var originalOut = Console.Out;
        var originalError = Console.Error;
        LogCaptureInstall.ResetForTests();
        Assert.Null(LogCaptureInstall.Shared);
        try
        {
            LogCaptureInstall.Install(capacityBytes: 64 * 1024, forwardConsole: false);
            Assert.NotNull(LogCaptureInstall.Shared);
            LogCaptureInstall.ResetForTests();
            Assert.Null(LogCaptureInstall.Shared);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            for (int i = Trace.Listeners.Count - 1; i >= 0; i--)
            {
                if (Trace.Listeners[i] is BufferTraceListener)
                    Trace.Listeners.RemoveAt(i);
            }
            LogCaptureInstall.ResetForTests();
        }
    }
}
