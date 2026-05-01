---
description: "Quality filtering guidelines for expert C# code review findings -- Team Lead Test, severity auto-escalation, confidence rubric, deduplication"
title: "Signal-to-Noise Gate (C#)"
version: "1.0.0"
owner: "Agentic Engineering System"
---

# Signal-to-Noise Gate

## Purpose

Guidelines for filtering candidate findings to ensure high signal-to-noise ratio in C# code review output. Applied after expert skills produce candidate findings (Stage 2.5) and during synthesis (Stage 3).

## The Team Lead Test

Before emitting any finding, apply this test:

> **"Would a senior team lead on this codebase keep this comment in their PR review, or would they delete it as noise that distracts from the important issues?"**

A team lead would **DELETE** a finding that:
- Is a pure style preference with no correctness impact (`var` vs explicit type, expression-bodied members vs block bodies, `_` discard naming)
- Suggests changes to generated code (`.designer.cs`, `.g.cs`, `.g.i.cs`, `AssemblyInfo.cs` from build)
- Flags analyzer suppressions (`#pragma warning disable`, `[SuppressMessage]`) that are properly justified with adjacent comments
- Applies to test or mock code where readability and intent clarity trump production patterns (e.g., inline magic numbers in test assertions, `new Mock<T>()` without strict mode)
- Suggests LINQ where a `for`/`foreach` loop is clearer and the code is not on a hot path
- Flags missing `ConfigureAwait(false)` in application code -- this only matters in library assemblies
- Points out a technically true inefficiency that is unmeasurable in context (e.g., string concatenation with 2-3 literals vs `StringBuilder`)
- Flags a pattern that is idiomatic C# in context (e.g., `!` null-forgiving operator on a value known to be non-null from prior check, `default!` in `[required]` property initializers)
- Would require significant refactoring for negligible benefit
- Flags issues in code being deleted (the deletion is the fix)
- Recommends making a class `sealed` purely for micro-optimization when there is no design concern

A team lead would **KEEP** a finding that:
- Identifies a potential deadlock (sync-over-async, `Task.Result`/`.Wait()` with `SynchronizationContext`, lock ordering)
- Reveals a disposed object access path (`ObjectDisposedException` reachable through normal control flow)
- Catches a UI thread violation (cross-thread property access on WPF/WinForms controls without dispatcher/invoke)
- Points out null reference paths (missing null checks on nullable-annotated code in nullable-enabled projects)
- Identifies a security boundary violation (SQL injection, deserialization of untrusted data, path traversal)
- Catches a memory leak (event handler not unsubscribed, `IDisposable` resource not disposed, delegate preventing GC)
- Reveals a correctness bug (wrong error handling, logic error, exception swallowed silently)
- Points out a measurable performance issue in a hot path
- Catches an API design issue that will be costly to fix after release (breaking change, missing nullability annotation)
- Identifies a pattern that will cause problems at scale (unbounded cache, connection exhaustion, thread pool starvation)

**If the finding would be deleted, suppress it. If uncertain, emit it with `severity: low` and `confidence: <0.75`.**

## Severity Auto-Escalation

The following findings MUST be escalated to the specified minimum severity regardless of the reviewer's initial rating:

### C#-Specific

| Pattern | Minimum Severity | Rationale |
|---------|-----------------|-----------|
| `BinaryFormatter` usage (serialize or deserialize) | **critical** | Remote code execution via deserialization (CVE-rich, deprecated since .NET 5) |
| `Task.Result` or `.Wait()` on thread with `SynchronizationContext` (UI thread, ASP.NET classic) | **critical** | Guaranteed deadlock under standard configuration |
| `async void` method that is not an event handler | **high** | Unobserved exceptions crash the process; cannot be awaited or caught by caller |
| `lock(this)` or `lock(typeof(T))` | **high** | External code can acquire the same lock, causing deadlock |
| SQL string concatenation or interpolation with user-derived input | **high** | SQL injection (CWE-89) |
| Missing `Dispose()` call on `IDisposable` resource (not in `using` statement or block) | **high** | Resource leak -- file handles, database connections, unmanaged memory |
| Cross-thread UI property access without `Dispatcher.Invoke`/`BeginInvoke` or `Control.Invoke`/`BeginInvoke` | **high** | `InvalidOperationException` at runtime or silent UI corruption |
| `TypeNameHandling.All` or `TypeNameHandling.Auto` in Newtonsoft.Json settings | **high** | Deserialization attack vector -- attacker-controlled type instantiation |
| Missing null check on parameter annotated `[NotNull]` or in nullable-enabled context without `?` | **high** | `NullReferenceException` in production; nullability contract violated |
| `GCHandle.Alloc` without guaranteed `Free` in `finally` block | **high** | Pinned memory leak, GC heap fragmentation |
| `Process.Start` with string arguments derived from user input | **high** | Command injection (CWE-78) |
| `HttpClient` created per-request instead of reused (outside of `IHttpClientFactory`) | **high** | Socket exhaustion under load (`SocketException`) |

### Language-Neutral

| Pattern | Minimum Severity | Rationale |
|---------|-----------------|-----------|
| Security bypass (reclassifying errors to skip auth, SSRF redirect following) | **high** | Exploitable vulnerability |
| Hardcoded credentials, API keys, or connection strings with passwords in source | **high** | Credential exposure |

## Confidence Rubric

Confidence is NOT a self-reported feeling. It is an **evidence grade** that describes how completely the cited pattern materialized in the code under review. Every finding MUST cite the pattern it matched (by ID or description) and justify the confidence level.

| Level | Value | Meaning | Evidence Required | Example |
|-------|-------|---------|-------------------|---------|
| **High** | >= 0.85 | Pattern is **fully materialized** in the code. All elements of the pattern are present and verifiable in the diff or fetched context. | Cite the specific lines where each element of the pattern appears. The reviewer could verify the finding by reading only those lines. | `Task.Result` called at L42 inside a method invoked from `Button_Click` (UI thread with `SynchronizationContext`). Both the `.Result` call and the UI-thread entry point are visible in the diff. Deadlock is certain. |
| **Medium** | 0.70-0.84 | Pattern is **partially materialized**. Some elements are present but others require inference (e.g., caller behavior, code not in the diff, runtime conditions). | Cite what IS visible. State explicitly what is inferred and why. | "`SqlCommand` at L55 uses string interpolation for the query. The interpolated variable `userName` comes from a parameter, but the caller is not in the diff. If the caller passes user input, this is SQL injection." The interpolation is visible; the input source is inferred. |
| **Low** | < 0.70 | **Conceptual resemblance** to a pattern but cannot be validated from available context. The code shares structural similarity with a known bug pattern but key confirming evidence is missing. | State the pattern, state what matches, state what's missing. | "This `HttpClient` is created in a method body (L30), which resembles the socket-exhaustion pattern, but I cannot confirm whether this method is called per-request or once at startup without seeing the caller." |

**Rules:**
1. **Every finding MUST cite a pattern** -- either a pattern ID from a skill file (e.g., `CS-CONC-001`) or a named pattern description (e.g., "sync-over-async deadlock via Task.Result on SynchronizationContext thread").
2. **No uncited confidence scores.** If you cannot name the pattern, you cannot assign confidence.
3. **Low confidence + low/medium severity = suppressed** (per Low-Confidence Suppression below), UNLESS the domain is memory-lifecycle, security, concurrency, or UI-framework.
4. **Confidence is about evidence, not severity.** A finding can be high-confidence + low-severity (e.g., "this naming inconsistency is definitely present but doesn't matter") or low-confidence + critical-severity (e.g., "this might be a deadlock but I can't see the threading context").
5. **Findings MUST trace to a catalogued pattern.** The orchestrator applies patterns from skill files -- it does not independently analyze code for novel defects. If no catalogued pattern matches, the correct output is zero findings for that skill, not a novel observation. Novel observations belong in the feedback loop (Signal 3: False Negative Discovery) where they are evaluated and, if validated, added to the catalogue by the pattern maintainers. This boundary ensures consistency: every finding from this system has the same provenance (expert pattern -> skill -> orchestrator -> output), regardless of which model runs the orchestrator.

## Low-Confidence Suppression

If the finding's confidence_level is **low** AND the severity is **low or medium**, suppress the finding. Low-confidence + low-severity = noise.

**Exception**: Never suppress findings related to the following domains regardless of confidence:
- **memory-lifecycle** -- `IDisposable` leaks, `GCHandle` leaks, finalizer issues
- **security** -- injection, deserialization, credential exposure, privilege escalation
- **concurrency** -- deadlocks, race conditions, `async void`, thread safety violations
- **ui-framework** -- cross-thread access violations, dispatcher misuse

These domains have disproportionate blast radius; even uncertain signals warrant human attention.

## Deduplication

When multiple skills analyze the same diff, they may independently flag the same issue. Deduplicate before emitting findings.

Two findings are **duplicates** when they satisfy ALL of:
1. **Same file** -- identical file path
2. **Overlapping line range** -- line ranges overlap or are within 3 lines of each other
3. **Same root cause** -- both describe the same underlying issue (e.g., "missing Dispose on SqlConnection" and "IDisposable resource not in using block for SqlConnection" are the same root cause; "missing Dispose on SqlConnection" and "missing Dispose on SqlCommand" are not)

Two findings are **NOT duplicates** even if they share a line range when:
- They describe different root causes (e.g., one flags a missing null check, the other flags a performance issue on the same line)
- One is about the code as written, the other is about a missing test for that code
- One flags a concurrency issue and the other flags an API design issue on the same method signature

When duplicates are found:
- **Keep the finding with the highest severity.** If severity is equal, keep the one with the higher confidence score.
- **Preserve the domain attribution from the kept finding.** Do not merge domains -- the finding belongs to whichever skill identified it most precisely.
- **Note the duplicate in the kept finding's detail field** (e.g., "Also identified by cs-error-handling skill") so the reviewer knows multiple skills converged on the same issue. Convergence increases confidence.

## Ordering and Grouping

- Rank by severity: Critical > High > Medium > Low
- Within the same severity, rank by confidence (highest first)
- Group findings by file, ordered by line number, for coherent PR comments
- **Apply the Team Lead Test: remove findings that fail the test**
