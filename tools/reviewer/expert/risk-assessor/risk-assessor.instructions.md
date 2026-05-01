---
id: "expert-cs:risk-assessor/v1"
title: Risk Assessment for Expert C# Code Review Orchestration
version: 1.0
workflow: expert-cs-code-review-orchestration
dri: Agentic Engineering System
---

# Risk Assessment Instructions

## Purpose

This document provides instructions for assessing the risk of C# code changes and determining whether human-in-the-loop (HITL) review is required. The risk assessor takes the classifier output and expert review findings, then produces a final risk assessment with an explicit HITL recommendation.

**FORBIDDEN**: Defaulting to "requires human review" for all changes; using lines-of-code as sole risk determinant
**MANDATORY**: Evaluate multiple risk dimensions; provide specific rationale for HITL decisions; consider finding severity and confidence together

## Risk Dimensions

### Dimension 1: Code Safety Risk

**Weight**: 0.30

Evaluates patterns that can cause memory corruption, access violations, or bypass the managed runtime's safety guarantees.

**Critical Risk Factors** (any one triggers HITL):
- `unsafe` blocks added or modified
- `fixed` statements pinning managed objects
- `stackalloc` without bounds checking or span safety
- P/Invoke declarations with raw pointer parameters (`IntPtr`, `void*`)
- `GCHandle` allocation without guaranteed release
- `Unsafe.As<T>` or `Unsafe.AsRef<T>` reinterpret casts
- Direct memory manipulation via `Marshal.Copy`, `Marshal.PtrToStructure`, `Buffer.MemoryCopy`
- `NativeMemory.Alloc`/`AllocZeroed` without corresponding `Free`
- `Span<T>` or `Memory<T>` created from raw pointers with incorrect length

**High Risk Factors** (2+ triggers HITL):
- Types holding unmanaged resources without implementing `IDisposable`
- Finalizers (`~ClassName()`) without the full Dispose pattern (`Dispose(bool)`)
- `WeakReference` or `WeakReference<T>` used for caching without null-check after `TryGetTarget`
- `GC.SuppressFinalize` called without a corresponding `Dispose` method
- `ConditionalWeakTable` misuse (retaining strong references via values)
- `ref struct` returned from methods without proper lifetime tracking
- `MemoryMarshal.GetReference` on empty spans
- Custom `IMemoryOwner<T>` implementations without proper disposal semantics

**Assessment**: Count critical and high factors. Score 0.0-1.0.

### Dimension 2: Concurrency Risk

**Weight**: 0.25

Evaluates patterns that can cause deadlocks, race conditions, thread-safety violations, or async/await misuse.

**Critical Risk Factors** (any one triggers HITL):
- `Task.Result` or `Task.Wait()` called on a thread with a `SynchronizationContext` (UI thread, ASP.NET classic) -- deadlock
- `lock(this)`, `lock(typeof(T))`, or `lock("string literal")` -- external code can take the same lock
- Cross-thread UI access without `Dispatcher.Invoke`/`BeginInvoke` or `Control.Invoke`/`BeginInvoke`
- `async void` methods that are not event handlers -- unobserved exceptions crash the process
- Nested `lock` statements with inconsistent ordering across call sites -- deadlock

**High Risk Factors** (2+ triggers HITL):
- Missing `ConfigureAwait(false)` in library code (non-app, non-test assemblies)
- `CancellationToken` not threaded through async call chains
- `SemaphoreSlim.WaitAsync` without `try/finally` ensuring `Release`
- Fire-and-forget `Task` (discarded or assigned to `_`) without error handling
- `Lazy<T>` constructed with `LazyThreadSafetyMode.None` in a type used concurrently
- `ConcurrentDictionary.GetOrAdd` with a factory that has side effects
- `ReaderWriterLockSlim` without `try/finally` on every acquisition
- `Timer` callback accessing shared state without synchronization
- `Parallel.ForEach` or `PLINQ` mutating shared collection
- `volatile` field used as a synchronization mechanism beyond simple flags

**Assessment**: Count factors, weight by interaction complexity. Score 0.0-1.0.

### Dimension 3: API Surface Risk

**Weight**: 0.15

Evaluates changes that affect public API contracts, binary compatibility, and downstream consumers.

**Critical Risk Factors** (any one triggers HITL):
- Breaking change to public API -- removed or renamed `public`/`protected` members
- Changed nullability annotations (`?`, `[NotNull]`, `[MaybeNull]`) on public method signatures
- Behavioral changes to `virtual` or `abstract` members that downstream types may override
- Sealed a previously unsealed public class (breaks inheritance)
- Changed `params` array to non-params or vice versa
- Changed default parameter values on public methods

**High Risk Factors** (2+ triggers HITL):
- New public API surface without XML documentation comments
- Unsealed public classes that expose inheritance surface to external consumers
- Missing `[Obsolete]` attribute before planned member removal
- Generic constraint changes on public types or methods (`where T :`)
- Changed exception types thrown from public methods (callers may catch specific types)
- Interface member additions without default implementation (breaks implementors on older TFMs)
- `[EditorBrowsable(EditorBrowsableState.Never)]` removed from internal-use members

**Medium Risk Factors**:
- New public API with complete XML documentation
- Internal API restructuring (`internal`, `private protected`)
- Module/namespace reorganization
- New extension methods in commonly-imported namespaces (potential ambiguity)

**Assessment**: Score based on API stability impact. Score 0.0-1.0.

### Dimension 4: Security Boundary Risk

**Weight**: 0.20

Evaluates patterns that can introduce exploitable vulnerabilities, data exposure, or privilege escalation.

**Critical Risk Factors** (any one triggers HITL):
- SQL string concatenation or interpolation with user input (SQL injection)
- `BinaryFormatter` usage anywhere -- remote code execution via deserialization
- `TypeNameHandling.All` or `TypeNameHandling.Auto` in Newtonsoft.Json -- deserialization attack
- `Process.Start` with unsanitized user input -- command injection
- Certificate validation disabled (`ServerCertificateCustomValidationCallback` returning `true`)
- Hardcoded secrets, connection strings, API keys, or passwords in source
- `Assembly.Load` or `Activator.CreateInstance` with user-controlled type names
- XAML/BAML loading from untrusted sources
- `DataContractSerializer` or `NetDataContractSerializer` with untrusted input

**High Risk Factors** (2+ triggers HITL):
- `HttpClient` created without explicit `Timeout` (default is infinite)
- Custom authentication or authorization logic (roll-your-own auth)
- XML parsing without `DtdProcessing.Prohibit` (XXE injection)
- `Regex` constructed without `RegexOptions.None` and no timeout -- ReDoS on user input
- Deserialization of untrusted data with any serializer not configured for known types only
- Path concatenation with user input without `Path.GetFullPath` + validation (path traversal)
- Cryptographic operations using obsolete algorithms (`MD5`, `SHA1` for security, `DES`, `3DES`)
- `dynamic` keyword used with user-controlled data
- CORS policy set to allow all origins (`AllowAnyOrigin`) with credentials
- Logging of sensitive data (passwords, tokens, PII) without redaction

**Assessment**: Security risk is binary for critical factors. Score 0.0-1.0.

### Dimension 5: Reviewer Confidence

**Weight**: 0.10

After expert reviews are complete, assess the confidence level across all skill findings.

**Low Confidence Indicators** (suggest HITL):
- Expert review findings contradict each other
- Multiple hedging statements ("might cause", "possibly leads to") in findings
- Findings reference unfamiliar patterns or unusual code structures (Roslyn source generators, custom awaitable types, IL emit)
- Domain is outside the primary expertise of available reviewers
- Code relies heavily on runtime behavior that cannot be statically verified (reflection, `dynamic`, COM interop)

**Medium Confidence Indicators**:
- Clear findings but some edge cases not fully explored
- Patterns partially match known categories
- Some uncertainty about framework-level interactions (DI container lifetime, middleware pipeline ordering)

**High Confidence Indicators**:
- Findings clearly match catalogued patterns
- Before/after code examples are well-established
- Similar patterns have been successfully reviewed in prior PRs

**Assessment**: Aggregate reviewer confidence. Score 0.0-1.0.

## Composite Risk Scoring

```
composite_risk = weighted_average([
    (code_safety_risk,              0.30),
    (concurrency_risk,              0.25),
    (api_surface_risk,              0.15),
    (security_boundary_risk,        0.20),
    (1.0 - reviewer_confidence,     0.10)   // low confidence = higher risk
])

composite_risk = clamp(composite_risk, 0.0, 1.0)
```

### Risk Labels

| Label | Score Range |
|-------|-------------|
| `low` | < 0.3 |
| `medium` | 0.3 - 0.6 |
| `high` | 0.6 - 0.85 |
| `critical` | >= 0.85 |

## HITL Decision Matrix

### Always Require HITL

These conditions bypass the scoring model and always recommend human review:

1. **Any critical safety factor** -- `unsafe` added/modified, `fixed` statements, P/Invoke with raw pointers, `GCHandle` manipulation
2. **Any critical security factor** -- `BinaryFormatter`, SQL injection, `TypeNameHandling.All`, disabled certificate validation, hardcoded secrets
3. **Breaking public API change** -- removed/renamed public members, changed nullability annotations on public signatures, behavioral changes to virtual/abstract members
4. **Reviewer confidence < 0.5** on any Critical or High severity finding
5. **Composite risk >= 0.85**

### Recommend HITL

Suggest human review but don't block merge:

1. **Composite risk 0.7-0.85** when concurrency or memory-lifecycle domains are involved
2. **3+ High-severity findings** from expert reviewers
3. **Cross-project changes** touching > 5 projects/assemblies
4. **Complex UI framework interactions** -- changes spanning WPF/WinForms dispatcher, data binding, visual tree manipulation, or custom controls with dependency properties
5. **Mixed async/sync code paths** -- changes that bridge synchronous and asynchronous call chains (e.g., wrapping async in sync or vice versa)

### Skip HITL

Human review adds minimal value:

1. **Composite risk < 0.4**
2. **Pure refactor** -- renamed symbols, moved code, extracted methods, tests pass
3. **Dependency version bump** -- `.csproj` `<PackageReference>` version changes only, `Directory.Packages.props`, `global.json`
4. **Documentation-only changes** -- XML doc comments, README, markdown files
5. **Test-only changes** -- new or modified test cases with no production code impact
6. **All findings are Low/Info severity** with high confidence

## Output Format

```json
{
  "risk_assessment": {
    "dimensions": {
      "code_safety": {
        "score": 0.7,
        "critical_factors": ["unsafe block added in NativeInterop.cs"],
        "high_factors": ["GCHandle without try/finally release"]
      },
      "concurrency": {
        "score": 0.3,
        "critical_factors": [],
        "high_factors": ["fire-and-forget Task without error handling"]
      },
      "api_surface": {
        "score": 0.1,
        "critical_factors": [],
        "high_factors": []
      },
      "security_boundary": {
        "score": 0.0,
        "critical_factors": [],
        "high_factors": []
      },
      "reviewer_confidence": {
        "score": 0.8,
        "notes": "Well-known P/Invoke pattern with established safe wrappers"
      }
    },
    "composite_risk": 0.42,
    "overall_risk": "medium",
    "risk_label_mapping": {
      "low": "< 0.3",
      "medium": "0.3 - 0.6",
      "high": "0.6 - 0.85",
      "critical": ">= 0.85"
    }
  },
  "hitl_decision": {
    "recommended": true,
    "required": true,
    "reasons": [
      "unsafe block added in NativeInterop.cs (critical safety factor triggers HITL -- always required)"
    ],
    "bypass_reasons": [],
    "confidence": 0.85
  },
  "review_summary": {
    "total_findings": 5,
    "by_severity": { "critical": 0, "high": 1, "medium": 2, "low": 2 },
    "primary_concerns": ["GCHandle allocated in unsafe block without guaranteed Free on exception path"],
    "positive_notes": ["Clean async/await usage, proper CancellationToken threading, IDisposable pattern correctly implemented on wrapper type"]
  }
}
```

## Calibration Notes

These thresholds are initial values adapted from the Rust/C++ risk assessor (calibrated on 6,000+ Rust PRs across Microsoft ADO) and adjusted for C#-specific risk patterns. They should be calibrated over time based on:

1. **False positive rate**: How often HITL is recommended but human reviewer finds nothing actionable
2. **False negative rate**: How often HITL is skipped but a defect ships
3. **Domain-specific tuning**: Some codebases may need adjusted domain weights (e.g., a security library should weight security higher; a high-performance networking library should weight concurrency and safety higher)
4. **Framework-specific tuning**: WPF/WinForms applications may need higher weight on UI-framework concurrency risk; ASP.NET applications may need higher weight on security boundary risk

The debugging orchestrator's Strategy C validated this approach: start with conservative thresholds, then tune from experiment data. The same principle applies here.
