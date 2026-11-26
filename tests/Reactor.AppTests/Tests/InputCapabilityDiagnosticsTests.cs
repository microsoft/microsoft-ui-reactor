using Microsoft.UI.Reactor.AppTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Tests;

[TestClass]
public sealed class InputCapabilityDiagnosticsTests
{
    private static readonly DateTimeOffset ProbeTime = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(false, 5)]
    [DataRow(false, 87)]
    [DataRow(true, 5)]
    public void Capture_PreservesImmediateErrorBeforeClockAndMetadata(bool succeeded, int error)
    {
        int nativeError = error;
        var calls = new List<string>();
        var cursor = SessionInteractivityGuard.ObserveCursor(
            () => { calls.Add("cursor"); return succeeded; },
            () => { calls.Add("error"); return nativeError; },
            () => { calls.Add("clock"); nativeError = 123; return ProbeTime; });
        var snapshot = SessionInteractivityGuard.CompleteCursorObservation(cursor, () =>
        {
            calls.Add("context");
            nativeError = 999;
            return Context();
        });

        CollectionAssert.AreEqual(new[] { "cursor", "error", "clock", "context" }, calls);
        Assert.AreEqual(succeeded, snapshot.Succeeded);
        Assert.AreEqual(error, snapshot.NativeError);
        Assert.AreEqual(ProbeTime, snapshot.ProbeCompletedAtUtc);
        Assert.AreEqual(ProbeTime.AddMilliseconds(10), snapshot.ContextStartedAtUtc);
        Assert.AreEqual(42, snapshot.CallerProcessId);
        Assert.AreEqual(7u, snapshot.CallerNativeThreadId);
        Assert.AreEqual(3, snapshot.CallerSessionId);
        StringAssert.Contains(snapshot.Context, "after cursor probe; not atomic");
        StringAssert.Contains(snapshot.Context, "wtsConnectState=query-failed");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(5)]
    [DataRow(87)]
    public void Failure_RemainsInconclusiveWithOneProbeAndNoDispatch(int error)
    {
        int probes = 0, contextReads = 0;
        bool dispatched = false;
        var failure = Assert.Throws<AssertInconclusiveException>(() =>
        {
            SessionInteractivityGuard.EnsureInputInjectable("fake input",
                () => SessionInteractivityGuard.ObserveCursor(
                    () => { probes++; return false; }, () => error, () => ProbeTime),
                cursor => SessionInteractivityGuard.CompleteCursorObservation(cursor, () =>
                {
                    contextReads++;
                    return Context();
                }));
            dispatched = true;
        });

        Assert.AreEqual(1, probes);
        Assert.AreEqual(1, contextReads);
        Assert.IsFalse(dispatched);
        StringAssert.Contains(failure.Message, SessionInteractivityGuard.InputBlockedMarker);
        StringAssert.Contains(failure.Message, $"nativeError={error}");
        Assert.AreEqual(error == 5, failure.Message.Contains("ERROR_ACCESS_DENIED", StringComparison.Ordinal));
        if (error == 0) StringAssert.Contains(failure.Message, "no extended error code was reported");
    }

    [TestMethod]
    public void Success_UsesTheBooleanPredicateAndDoesNotQueryContext()
    {
        int probes = 0, contextReads = 0;
        SessionInteractivityGuard.EnsureInputInjectable("fake input",
            () => { probes++; return new CursorObservation(true, 5, ProbeTime); },
            cursor =>
            {
                contextReads++;
                return SessionInteractivityGuard.CompleteCursorObservation(cursor, Context);
            });
        Assert.AreEqual(1, probes);
        Assert.AreEqual(0, contextReads);
    }

    [TestMethod]
    public void UnavailableMetadata_DoesNotReplaceProbeErrorOrFailureClassification()
    {
        var cursor = new CursorObservation(false, 0, ProbeTime);
        InputCapabilitySnapshot Complete(CursorObservation observed) =>
            SessionInteractivityGuard.CompleteCursorObservation(observed, () =>
                throw new global::System.ComponentModel.Win32Exception(5, "Metadata query failed"));
        var snapshot = Complete(cursor);
        Assert.AreEqual(0, snapshot.NativeError);
        Assert.IsFalse(snapshot.Succeeded);
        Assert.IsNull(snapshot.CallerSessionId);
        Assert.IsNull(snapshot.ContextStartedAtUtc);
        StringAssert.Contains(snapshot.Context, "metadata unavailable");
        StringAssert.Contains(snapshot.Context, "Win32Exception");

        var failure = Assert.Throws<AssertInconclusiveException>(() =>
            SessionInteractivityGuard.EnsureInputInjectable("fake input", () => cursor, Complete));
        StringAssert.Contains(failure.Message, "nativeError=0");
        StringAssert.Contains(failure.Message, "Caller PID=<unavailable>");
        Assert.IsFalse(failure.Message.Contains("ERROR_ACCESS_DENIED", StringComparison.Ordinal));
    }

    private static InputCapabilityContext Context() => new(
        ProbeTime.AddMilliseconds(10), 42, 7, 3,
        "threadDesktop=Default; winsta=WinSta0; inputDesktop=Default; wtsConnectState=query-failed");
}
