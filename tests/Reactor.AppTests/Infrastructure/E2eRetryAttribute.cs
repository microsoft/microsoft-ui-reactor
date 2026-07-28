using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

// MSTest's retry extension surface (RetryContext / RetryResult / RetryBaseAttribute.ExecuteAsync)
// is gated behind the MSTESTEXP experimental diagnostic. Deriving a custom retry policy is the
// documented, supported use of RetryBaseAttribute, so opt in explicitly for this whole file.
#pragma warning disable MSTESTEXP

/// <summary>
/// Drop-in replacement for MSTest's built-in <c>[Retry(n)]</c> on the winapp-ui E2E tests, adding
/// two behaviours the built-in attribute lacks:
///
/// <list type="number">
/// <item><description><b>Env-gated attempt count.</b> Reads <see cref="RetriesEnvVar"/>
/// (<c>REACTOR_E2E_RETRIES</c>) at run time, falling back to the compile-time default. Set it to
/// <c>0</c> to disable retries entirely — the "retries-OFF" diagnostic lane that measures the true
/// per-attempt flake rate the production <c>[Retry(3)]</c> otherwise masks.</description></item>
///
/// <item><description><b>Anti-masking of a real failure.</b> MSTest stops retrying as soon as an
/// attempt is "acceptable", counts <c>Inconclusive</c> as acceptable, and reports the <i>last</i>
/// attempt's outcome. So a genuine first-run <c>Failed</c> followed by an environmental
/// <c>Inconclusive</c> on a retry (screen locks / input injection lost mid-method, or a
/// false-positive lock verdict) is reported as <c>Inconclusive</c> and the failure is silently
/// erased — and <c>dotnet test</c> then exits 0. MSTest only enters the retry path when the first
/// normal run already produced <c>Failed</c>/<c>Timeout</c> (a real failure the interactivity guard
/// did <b>not</b> reclassify), so this policy never lets a later <c>Inconclusive</c> become the
/// outcome: it heals only on a genuine <c>Passed</c>, otherwise it surfaces the hard failure.
/// </description></item>
/// </list>
///
/// See the E2E Inconclusive audit (Finding 1) for the full masking mechanism.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class E2eRetryAttribute : RetryBaseAttribute
{
    /// <summary>Environment variable that overrides the retry attempt count at run time.</summary>
    public const string RetriesEnvVar = "REACTOR_E2E_RETRIES";

    /// <summary>
    /// Initializes the policy with the compile-time default number of retry attempts (after the
    /// first normal run). <see cref="RetriesEnvVar"/> can override this at run time.
    /// </summary>
    /// <param name="maxRetryAttempts">Default retry attempts; must be &gt;= 1.</param>
    public E2eRetryAttribute(int maxRetryAttempts)
    {
        if (maxRetryAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxRetryAttempts));

        MaxRetryAttempts = maxRetryAttempts;
    }

    /// <summary>Compile-time default number of retry attempts after the first normal run.</summary>
    public int MaxRetryAttempts { get; }

    /// <summary>Delay in milliseconds between attempts (also before the first retry). Default 0.</summary>
    public int MillisecondsDelayBetweenRetries { get; set; }

    /// <summary>
    /// Effective attempt count: <see cref="RetriesEnvVar"/> when it parses to a non-negative integer,
    /// otherwise <see cref="MaxRetryAttempts"/>. A value of <c>0</c> disables retries.
    /// </summary>
    internal int EffectiveRetryAttempts
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable(RetriesEnvVar);
            return raw is not null && int.TryParse(raw, out var n) && n >= 0
                ? n
                : MaxRetryAttempts;
        }
    }

    /// <inheritdoc />
    protected override async Task<RetryResult> ExecuteAsync(RetryContext retryContext)
    {
        // Contract: MSTest consumes only RetryResult.TryGetLast() as the reported outcome
        // (UnitTestRunner: `result = retryResult.TryGetLast()`); every other AddResult entry is
        // discarded and never reaches the TRX. So this method's whole job is to make the *last*
        // added result the authoritative one — a genuine pass heals, otherwise a hard failure is
        // surfaced, and a retry-attempt Inconclusive is never allowed to become the last result.
        var result = new RetryResult();
        int attempts = EffectiveRetryAttempts;

        // Retries disabled (diagnostic lane): let the real first-run outcome stand so the report
        // sees the true per-attempt result rather than a retry-healed one. MSTest only calls this
        // method when the first normal run was Failed/Timeout, so FirstRunResults is that failure.
        if (attempts <= 0)
        {
            result.AddResult(retryContext.FirstRunResults);
            return result;
        }

        int currentDelay = MillisecondsDelayBetweenRetries;
        TestResult[]? lastHardFailure = null;
        for (int i = 0; i < attempts; i++)
        {
            if (currentDelay > 0)
                await Task.Delay(currentDelay).ConfigureAwait(false);

            TestResult[] attemptResults = await retryContext.ExecuteTaskGetter().ConfigureAwait(false);

            if (AllPassed(attemptResults))
            {
                // Genuine flake heal — Passed is the last-added result, so it becomes the outcome.
                result.AddResult(attemptResults);
                return result;
            }

            if (ContainsHardFailure(attemptResults))
                lastHardFailure = attemptResults;

            // An Inconclusive retry attempt (environmental / lock verdict) is deliberately NOT
            // added as the outcome: the first normal run already failed for real, so keep retrying
            // for a genuine heal instead of letting the Inconclusive erase the failure.
        }

        // Exhausted without a pass. Surface a hard failure — a failed retry attempt if we saw one,
        // else the first normal run's failure — never a masking Inconclusive.
        result.AddResult(lastHardFailure ?? retryContext.FirstRunResults);
        return result;
    }

    private static bool AllPassed(TestResult[] results)
        => results.Length > 0 && Array.TrueForAll(results, static r => r.Outcome == UnitTestOutcome.Passed);

    private static bool ContainsHardFailure(TestResult[] results)
        => Array.Exists(results, static r => r.Outcome is UnitTestOutcome.Failed or UnitTestOutcome.Timeout);
}
#pragma warning restore MSTESTEXP
