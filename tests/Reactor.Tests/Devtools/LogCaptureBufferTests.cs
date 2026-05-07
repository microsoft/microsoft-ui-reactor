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
}
