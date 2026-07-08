using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

// Consumes MSTest's experimental retry surface (RetryContext / RetryResult) via reflection to
// drive E2eRetryAttribute.ExecuteAsync directly. Same opt-in as the attribute under test.
#pragma warning disable MSTESTEXP

/// <summary>
/// Headless unit coverage for <see cref="E2eRetryAttribute"/>'s env-gating and anti-masking
/// decision logic. Lives in the AppTests assembly (not Reactor.Tests) because
/// <see cref="E2eRetryAttribute.EffectiveRetryAttempts"/> is internal and the retry API types are
/// internal to MSTest — no winapp/Host session is started, so these run at unit speed.
/// </summary>
[TestClass]
public class E2eRetryAttributeTests
{
    private string? _savedEnv;

    [TestInitialize]
    public void SaveEnv() => _savedEnv = Environment.GetEnvironmentVariable(E2eRetryAttribute.RetriesEnvVar);

    // Always restore the process env var so a retry-count override can never leak into another
    // test's [E2eRetry] orchestration.
    [TestCleanup]
    public void RestoreEnv() => Environment.SetEnvironmentVariable(E2eRetryAttribute.RetriesEnvVar, _savedEnv);

    // ── EffectiveRetryAttempts: env parsing ─────────────────────────────────

    [TestMethod]
    [DataRow(null, 3)]   // unset → compile-time default
    [DataRow("", 3)]     // empty → default
    [DataRow("abc", 3)]  // non-numeric → default
    [DataRow("-1", 3)]   // negative → default (n >= 0 required)
    [DataRow("0", 0)]    // disable retries (diagnostic lane)
    [DataRow("1", 1)]
    [DataRow("5", 5)]
    public void EffectiveRetryAttempts_HonorsEnvOverride(string? env, int expected)
    {
        Environment.SetEnvironmentVariable(E2eRetryAttribute.RetriesEnvVar, env);
        Assert.AreEqual(expected, new E2eRetryAttribute(3).EffectiveRetryAttempts);
    }

    // ── ExecuteAsync: anti-masking decision ─────────────────────────────────
    // MSTest only invokes ExecuteAsync after the first normal run already produced Failed/Timeout,
    // so FirstRunResults always represents a real failure in these scenarios.

    [TestMethod]
    public async Task RetriesOff_ReportsTrueFirstRunOutcome()
    {
        // attempts <= 0: no retries, the real first-run failure stands (the retries-OFF lane).
        Environment.SetEnvironmentVariable(E2eRetryAttribute.RetriesEnvVar, "0");
        var outcome = await RunRetryAsync(new E2eRetryAttribute(3), firstRun: Failed());
        Assert.AreEqual(UnitTestOutcome.Failed, outcome);
    }

    [TestMethod]
    public async Task RealFailure_HealedByRetryPass_ReportsPassed()
    {
        Environment.SetEnvironmentVariable(E2eRetryAttribute.RetriesEnvVar, null); // default 3
        var outcome = await RunRetryAsync(new E2eRetryAttribute(3), firstRun: Failed(), Passed());
        Assert.AreEqual(UnitTestOutcome.Passed, outcome);
    }

    [TestMethod]
    public async Task RealFailure_ThenInconclusiveRetries_ReportsFailure_NotInconclusive()
    {
        // The core anti-masking guarantee: an environmental Inconclusive on a retry must never
        // overwrite a genuine first-run failure (that is the bug this attribute exists to close).
        Environment.SetEnvironmentVariable(E2eRetryAttribute.RetriesEnvVar, "3");
        var outcome = await RunRetryAsync(
            new E2eRetryAttribute(3), firstRun: Failed(), Inconclusive(), Inconclusive(), Inconclusive());
        Assert.AreNotEqual(UnitTestOutcome.Inconclusive, outcome,
            "A retry Inconclusive must not mask the real first-run failure.");
        Assert.AreEqual(UnitTestOutcome.Failed, outcome);
    }

    [TestMethod]
    public async Task RealFailure_AllRetriesFail_ReportsFailure()
    {
        Environment.SetEnvironmentVariable(E2eRetryAttribute.RetriesEnvVar, "2");
        var outcome = await RunRetryAsync(new E2eRetryAttribute(3), firstRun: Failed(), Failed(), Failed());
        Assert.AreEqual(UnitTestOutcome.Failed, outcome);
    }

    [TestMethod]
    public async Task RealFailure_FailedRetryThenInconclusive_ReportsHardFailure()
    {
        // A failed retry attempt must win over a later Inconclusive (lastHardFailure is preferred).
        Environment.SetEnvironmentVariable(E2eRetryAttribute.RetriesEnvVar, "3");
        var outcome = await RunRetryAsync(
            new E2eRetryAttribute(3), firstRun: Failed(), Failed(), Inconclusive(), Inconclusive());
        Assert.AreEqual(UnitTestOutcome.Failed, outcome);
    }

    [TestMethod]
    public async Task RealFailure_HealsOnLaterAttemptAfterInconclusive_ReportsPassed()
    {
        // Inconclusive retries do not stop the loop; a genuine pass later still heals.
        Environment.SetEnvironmentVariable(E2eRetryAttribute.RetriesEnvVar, "3");
        var outcome = await RunRetryAsync(
            new E2eRetryAttribute(3), firstRun: Failed(), Inconclusive(), Passed());
        Assert.AreEqual(UnitTestOutcome.Passed, outcome);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static TestResult[] Result(UnitTestOutcome outcome) => new[] { new TestResult { Outcome = outcome } };
    private static TestResult[] Failed() => Result(UnitTestOutcome.Failed);
    private static TestResult[] Passed() => Result(UnitTestOutcome.Passed);
    private static TestResult[] Inconclusive() => Result(UnitTestOutcome.Inconclusive);

    /// <summary>
    /// Drives <see cref="E2eRetryAttribute.ExecuteAsync"/> with a scripted sequence of retry-attempt
    /// results and returns the outcome MSTest would report (the last element added to the
    /// <c>RetryResult</c>). Reflection is required because MSTest's <c>RetryContext</c> constructor
    /// and <c>RetryResult.TryGetLast()</c> are internal to the framework assembly.
    /// </summary>
    private static async Task<UnitTestOutcome> RunRetryAsync(
        E2eRetryAttribute attr, TestResult[] firstRun, params TestResult[][] retryAttempts)
    {
        var queue = new Queue<TestResult[]>(retryAttempts);
        Func<Task<TestResult[]>> getter = () => Task.FromResult(queue.Dequeue());

        var ctxCtor = typeof(RetryContext).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
            new[] { typeof(Func<Task<TestResult[]>>), typeof(TestResult[]) }, modifiers: null)
            ?? throw new InvalidOperationException("RetryContext(internal) constructor not found.");
        var ctx = ctxCtor.Invoke(new object[] { getter, firstRun });

        var exec = typeof(E2eRetryAttribute).GetMethod(
            "ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("E2eRetryAttribute.ExecuteAsync not found.");
        var result = await (Task<RetryResult>)exec.Invoke(attr, new[] { ctx })!;

        // Read the outcome the same way MSTest's adapter does — RetryResult.TryGetLast() — rather
        // than the private _testResults field, so this stays valid as long as the public contract
        // (the adapter uses `result = retryResult.TryGetLast()`) holds.
        var tryGetLast = typeof(RetryResult).GetMethod(
            "TryGetLast", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RetryResult.TryGetLast not found.");
        var last = (TestResult[]?)tryGetLast.Invoke(result, null);
        Assert.IsNotNull(last, "ExecuteAsync must add at least one result (TryGetLast returned null).");
        return last![0].Outcome;
    }
}
#pragma warning restore MSTESTEXP
