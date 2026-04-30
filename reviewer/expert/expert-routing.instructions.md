---
description: "C# expert skill routing matrix, risk-trigger decision tables, escalation models, and feedback methodology for expert C# code review"
title: "C# Expert Routing and Reference Data"
version: "1.0.0"
owner: "Agentic Engineering System"
---

# C# Expert Routing and Reference Data

## Purpose

Reference data and guidelines for routing C# code changes to the right expert review skills, calibrating review depth, and managing the feedback loop. Used by the expert code review orchestrator during Stage 2 (Route) and post-review calibration.

## Pattern Sources

The C# expert review skills encode patterns sourced from established .NET authorities, framework design guidelines, and .NET team guidance. The C# skills are pattern-sourced from published expertise rather than calibrated from observed reviewer behavior on internal PRs. The patterns are authoritative, but they are derived from public guidance.

| Source | Domain Coverage | Key Contributions |
|--------|----------------|-------------------|
| .NET Runtime team design guidelines | api-design, error-handling, performance | Framework Design Guidelines, API review process |
| .NET async/await guidance | concurrency, error-handling | Async/await best practices, `ConfigureAwait`, `ValueTask` usage, `Task.Run` guidance, `async void` avoidance, `SynchronizationContext`, deadlock prevention |
| Windows UI / native interop guidance | memory-lifecycle, concurrency, general correctness | COM ref counting, Windows API patterns, threading gotchas, P/Invoke correctness |
| ASP.NET Core security / DI guidance | security, build-packaging, api-design | Configuration patterns, dependency injection, security headers, middleware pipeline |
| ASP.NET Core architecture guidance | api-design, performance, concurrency | High-performance API design, Kestrel patterns, `System.IO.Pipelines`, connection abstractions |
| .NET API review guidance | api-design, error-handling | API design reviews, nullability annotations, API compatibility, platform compatibility |
| .NET GC / performance guidance | performance, memory-lifecycle | GC tuning, allocation reduction, working set optimization, performance measurement methodology |
| High-performance .NET patterns | performance, concurrency | Zero-allocation techniques, `Span<T>` adoption, server performance |
| C# language design notes | api-design, error-handling | Nullable reference types, pattern matching, record types |
| .NET Framework Design Guidelines | api-design, error-handling | Type design, member design, exception design, naming conventions, extensibility patterns |

## Skill Routing Matrix

```
detected_domain → applicable_skills

C# Skills:
  cs-memory-lifecycle   → GC/finalizer patterns, IDisposable/using, unsafe code,
                          SafeHandle, pooling, P/Invoke memory management,
                          GCHandle, weak references, ConditionalWeakTable

  cs-concurrency        → async/await correctness, Task and ValueTask usage,
                          synchronization primitives (lock, SemaphoreSlim, Monitor),
                          dispatcher/UI thread marshaling, CancellationToken,
                          Channel<T>, sync-over-async detection, deadlock patterns,
                          SynchronizationContext, ConfigureAwait

  cs-error-handling     → exception hierarchy design, catch specificity,
                          nullable reference types and annotations,
                          null-forgiving operator usage, guard clauses,
                          ObjectDisposedException patterns, validation patterns,
                          [DoesNotReturn], [NotNull], [MaybeNull]

  cs-security           → SQL injection (string concat in queries),
                          command injection (Process.Start with user input),
                          authentication/authorization attributes,
                          cryptography usage, insecure deserialization
                          (BinaryFormatter, TypeNameHandling.All),
                          P/Invoke security, assembly loading,
                          serialization attack surface

  cs-performance        → allocation reduction, LINQ in hot paths,
                          boxing/unboxing, Span<T>/Memory<T> usage,
                          string handling (StringBuilder, interpolation, concat),
                          ValueTask vs Task, pooling (ArrayPool, ObjectPool),
                          struct vs class, ref struct, collection choice,
                          IAsyncEnumerable, BenchmarkDotNet patterns

  cs-api-design         → public type/member design, nullability contracts,
                          dependency injection patterns, interface vs abstract,
                          sealed vs open, generic constraints,
                          operator overloads, implicit/explicit conversions,
                          extension methods, record design,
                          [Obsolete] usage, EditorBrowsable,
                          Framework Design Guidelines compliance

  cs-ui-framework       → XAML correctness, DependencyProperty registration,
                          data binding ({Binding}, {x:Bind}),
                          INotifyPropertyChanged implementation,
                          ICommand patterns, DataTemplate/ControlTemplate,
                          ResourceDictionary, VisualStateManager,
                          visual tree management, DispatcherQueue usage,
                          UI thread correctness, memory leaks from event handlers

  cs-build-packaging    → MSBuild project files (.csproj, .props, .targets),
                          NuGet PackageReference management,
                          target framework multitargeting (TFM),
                          Roslyn analyzer configuration,
                          Directory.Build.props/Directory.Packages.props,
                          global.json SDK pinning, Central Package Management,
                          source generators, .editorconfig analyzer rules

  cs-test-infrastructure → unit test patterns (xUnit, MSTest, NUnit),
                          mocking (Moq, NSubstitute),
                          integration testing (WebApplicationFactory, TestServer),
                          UI testing patterns, test fixture lifecycle,
                          test data management, assertion patterns,
                          flaky test prevention
```

## Risk-Trigger Decision Table (C#)

Risk triggers are signals in the diff that mandate specific skill routing and tool escalation.

| Trigger ID | Diff Signal | Route To Skills | Tool Escalation | Default Gate |
|-----------|-------------|-----------------|-----------------|--------------|
| `t_unsafe_code` | `unsafe` block/method, `fixed` statement, `stackalloc`, pointer arithmetic (`*`, `->` on pointer types) | cs-memory-lifecycle, cs-security | Roslyn analyzers (CA rules), PVS-Studio (if configured), `dotnet build -warnaserror` | Block without SAFETY justification comment explaining why managed alternatives are insufficient |
| `t_idisposable` | New `IDisposable` implementation, `Dispose(bool)` pattern, missing `using` statement on disposable, finalizer (`~ClassName()`) without `IDisposable` | cs-memory-lifecycle | CA2000 (Dispose objects before losing scope), CA1816 (Call GC.SuppressFinalize), CA2213 (Disposable fields should be disposed) | Block on undisposed resources or missing Dispose pattern |
| `t_async_sync_mix` | `Task.Result`, `.GetAwaiter().GetResult()`, `Task.Wait()` in async context, `async void` (non-event-handler) | cs-concurrency | Async analyzers (VSTHRD series, CA2007, CA2012), deadlock detection | Block on sync-over-async in library code; block on `async void` outside event handlers |
| `t_dispatcher_access` | UI element access from non-UI thread, missing `Dispatcher.Invoke`/`DispatcherQueue.TryEnqueue`, cross-thread `ObservableCollection` modification | cs-concurrency, cs-ui-framework | UI thread analyzer, runtime thread-affinity checks | Block on unmarshaled UI access |
| `t_security_boundary` | SQL string concatenation with user input, `Process.Start` with unsanitized arguments, `BinaryFormatter` usage, `TypeNameHandling.All`/`Auto`, `Assembly.Load` with user-controlled path | cs-security | Security analyzers (CA3001-CA3012, CA2300-CA2315), SAST scanner, Roslyn security rules | Block on unvalidated user input crossing trust boundary |
| `t_dependency_change` | `.csproj` `<PackageReference>` additions/updates, `Directory.Packages.props` changes, NuGet source changes, `global.json` SDK version change | cs-build-packaging | `dotnet list package --vulnerable`, NuGet audit (`dotnet restore --audit`), component governance scan | Block on packages with known CVEs or deprecated status |
| `t_public_api` | New `public` types, new `public` members on existing types, parameter type/count changes on public methods, removal of `public` members, interface changes | cs-api-design | API compatibility analyzer, `Microsoft.CodeAnalysis.PublicApiAnalyzers`, nullability completeness check | Block on undocumented public API (missing XML doc comments on public members) |
| `t_binding_pattern` | New `DependencyProperty.Register`/`RegisterAttached`, `PropertyChanged` handler modifications, `{Binding}` or `{x:Bind}` changes in XAML, `DataTemplate` or `ControlTemplate` modifications | cs-ui-framework | XAML compiler diagnostics, binding failure detection at build time, runtime binding error logging | Block on incorrect property metadata (wrong owner type, missing PropertyChanged callback, type mismatch) |
| `t_serialization` | `[Serializable]` attribute, JSON/XML serialization attributes (`[JsonPropertyName]`, `[XmlElement]`), custom `JsonConverter<T>`, `TypeNameHandling` configuration, `BinaryFormatter`/`NetDataContractSerializer` usage | cs-security, cs-api-design | Serialization security analyzers (CA2300-CA2315, SYSLIB0011), JSON schema validation | Block on `TypeNameHandling` values other than `None`, block on `BinaryFormatter` usage (SYSLIB0011), require justification for custom converters on public API types |
| `t_perf_sensitive` | Hot path code modifications (identified by profiler attributes, benchmark references, or call frequency comments), `.ToList()` in loops, LINQ chains in tight loops, string concatenation in loops, new allocations on UI thread, large collection operations without capacity hints | cs-performance | BenchmarkDotNet (if benchmarks exist), memory profiler allocation analysis, `dotnet-counters` for GC pressure | Block on measurable regressions in existing benchmarks; require allocation justification in identified hot paths |

## Three-Tier Escalation Model (C#)

| Tier | When | Actions |
|------|------|---------|
| **Baseline** | All C# PRs | `dotnet build -warnaserror` + Roslyn analyzers (CA/IDE rules at configured `AnalysisLevel`) + `dotnet test` (all test projects) + nullable reference type warnings enforced |
| **Escalation** | Risk triggers `t_unsafe_code`, `t_async_sync_mix`, `t_perf_sensitive`, `t_idisposable` | Baseline + security analyzers (CA security rules enabled) + BenchmarkDotNet regression check (if benchmarks exist) + memory profiler allocation snapshot for `t_perf_sensitive` + VSTHRD analyzer suite for `t_async_sync_mix` |
| **Restricted Mode** | Risk triggers `t_security_boundary`, `t_serialization` on untrusted PRs (external contributors or fork PRs) | Baseline + SAST scanner (full scan) + manual review required for security-boundary changes + serialization configuration audit + no auto-merge permitted |

## Routing Thresholds

Risk score determines the review path. These are the default thresholds:

| Risk Score | Path | Review Depth |
|-----------|------|--------------|
| **< 0.4** | **Fast Path** | Single most-relevant domain skill. Quick review. |
| **0.4 - 0.7** | **Standard Path** | Top 2-3 domain skills review in parallel. |
| **>= 0.7** | **Deep Path** | All relevant domain skills + risk assessment + HITL recommendation. |

### Per-Domain Threshold Overrides

Some domains carry higher blast radius. These overrides shift toward deeper review earlier:

```json
{
  "default": { "fast_ceiling": 0.40, "deep_floor": 0.70 },
  "overrides": {
    "memory-lifecycle":  { "fast_ceiling": 0.30, "deep_floor": 0.60 },
    "security":          { "fast_ceiling": 0.25, "deep_floor": 0.55 },
    "concurrency":       { "fast_ceiling": 0.30, "deep_floor": 0.60 },
    "ui-framework":      { "fast_ceiling": 0.30, "deep_floor": 0.60 },
    "build-packaging":   { "fast_ceiling": 0.35, "deep_floor": 0.65 }
  }
}
```

**Calibration**: Feed classified PR outcomes (post-merge incidents, reverted PRs, missed bugs) back into threshold calibration. Lower `fast_ceiling` when fast-path reviews miss findings. Raise `deep_floor` when deep reviews consistently find nothing actionable.

## Domain-Specific Review Calibration (C#)

When reviewing C# code, calibrate analysis based on the project type and context:

| Project Context | Heightened Checks | Reduced Checks |
|----------------|-------------------|----------------|
| WinUI/WPF/MAUI application | Dispatcher thread safety, binding correctness, DependencyProperty metadata, event handler memory leaks, visual tree lifetime | Raw performance micro-optimizations (UI is rarely the hot path for CPU) |
| ASP.NET Core service | Request pipeline thread safety, DI lifetime (Singleton vs Scoped vs Transient), middleware ordering, authentication/authorization | UI framework signals (not applicable) |
| Shared library / NuGet package | Public API design, nullability completeness, binary compatibility, XML doc comments, `ConfigureAwait(false)` usage | UI framework signals (unless it is a UI library) |
| Console tool / CLI | Argument validation, error reporting, exit codes | API design depth (internal surface only), UI framework signals |
| Test project | Test isolation, mock verification, flaky test indicators, test naming | Performance micro-optimizations, API design strictness |

### Common C# Anti-Patterns to Flag

These cross-cutting patterns should be detected regardless of domain routing:

| Anti-Pattern | Detection Signal | Severity | Domain |
|-------------|-----------------|----------|--------|
| `async void` (non-event-handler) | `async void MethodName` where method is not an event handler (`object sender, EventArgs e`) | High | concurrency, error-handling |
| Sync-over-async | `Task.Result`, `.Wait()`, `.GetAwaiter().GetResult()` in async call chain | High | concurrency |
| Captured `this` in long-lived delegate | Lambda or delegate capturing `this` stored in static/singleton, event subscription without unsubscription | Medium | memory-lifecycle |
| `catch (Exception) { }` (empty) | Empty catch block swallowing all exceptions | High | error-handling |
| `GC.Collect()` without justification | Direct GC invocation outside test/benchmark code | Medium | memory-lifecycle, performance |
| Missing `ConfigureAwait(false)` in library | Library code (non-app, non-test) awaiting without `ConfigureAwait(false)` | Low | concurrency |
| `BinaryFormatter` usage | Any use of `BinaryFormatter` | Critical | security |
| Disposable field not disposed | Class contains `IDisposable` field but does not implement `IDisposable` | High | memory-lifecycle |

## Feedback Loop

The system improves through three feedback signals, each targeting a different component.

### Signal 1: Post-Merge Incidents

When a bug escapes to production that the agent should have caught:

1. **Classify the miss**: Which domain and pattern should have flagged it? Was the skill missing the pattern, or did the orchestrator route incorrectly?
2. **Add or update the pattern**: If the skill lacked the pattern, add it to the relevant skill definition with a concrete before/after example from the incident. For C# skills, include the Roslyn diagnostic code (e.g., CA2000) if one exists for the pattern.
3. **Adjust thresholds**: If the orchestrator fast-pathed a PR that needed deep review, lower the `fast_ceiling` for that domain. Each threshold adjustment must include the incident ID that motivated it.

### Signal 2: Reviewer Feedback on Agent Findings

When a human reviewer marks an agent finding as unhelpful, incorrect, or a nitpick:

1. **Track the rejection**: Record the finding ID, domain, pattern, and rejection reason.
2. **Pattern-level analysis**: If a specific pattern accumulates 3+ rejections across different PRs, review the pattern for over-triggering. Either tighten the pattern's preconditions or add a suppression rule. Common over-triggers in C# include: `ConfigureAwait` in application code (not library), nullable warnings in test code, minor LINQ performance in non-hot paths.
3. **Domain-level analysis**: If a domain's rejection rate exceeds 30% over a rolling window, review the domain's scope boundaries for over-breadth.

### Signal 3: False Negative Discovery

When a human reviewer catches an issue that the agent missed (not a pattern gap, but a routing or gating failure):

1. **Routing miss**: If the PR was classified to the wrong domain(s), update the classification signals in the change classifier. Example: a `DependencyProperty` registration error routed only to api-design but not ui-framework.
2. **Gating miss**: If the signal-to-noise gate suppressed a valid finding, tighten the suppression rules — add an exception or raise the confidence threshold for that pattern category.
3. **Severity miss**: If the agent rated a finding too low and it was escalated by a human, check whether the severity auto-escalation tables should include a new entry.

### Feedback Cadence

- **Incident-driven**: Pattern and threshold updates happen within one sprint of the incident.
- **Periodic review**: Aggregate reviewer feedback monthly. Compute rejection rates per domain and per pattern. Adjust scope boundaries and suppression rules.
- **After changes**: After any pattern or threshold change, review the affected skill on a sample of recent PRs to verify the change behaves as intended.

## Repository Configuration

Repositories may override default behavior by placing a `.code-review.json` file at the repository root. All fields are optional — omitted fields use the defaults defined in the agent.

```json
{
  "schema_version": "1.0",
  "language": "csharp",
  "domains": {
    "disabled": ["performance"],
    "severity_overrides": {
      "api-design": { "max_severity": "medium" }
    }
  },
  "noise_tolerance": "strict | default | lenient",
  "hitl": {
    "always_require": ["security", "memory-lifecycle"],
    "risk_threshold_override": 0.80
  },
  "suppression": {
    "patterns": ["CS-PERF-03"],
    "domains_in_test_code": ["performance", "api-design"]
  },
  "project_context": "winui | aspnet | library | console | test"
}
```

**Field semantics**:

| Field | Default | Effect |
|-------|---------|--------|
| `language` | auto-detect | Hints the classifier to use C# domain signals. Useful for mixed-language repos. |
| `domains.disabled` | `[]` | Skills in these domains are never routed to. Use when a domain is not relevant (e.g., no `ui-framework` in a headless service). |
| `domains.severity_overrides` | none | Caps the maximum severity for findings in a domain. Findings above the cap are downgraded, not suppressed. |
| `noise_tolerance` | `"default"` | `"strict"`: suppress all Low findings and findings with confidence < 0.80. `"lenient"`: emit all findings including Low severity. `"default"`: apply signal-to-noise gate rules as defined. |
| `hitl.always_require` | `[]` | Domains where human review is always recommended regardless of risk score. |
| `hitl.risk_threshold_override` | `0.85` | Override the global HITL threshold. |
| `suppression.patterns` | `[]` | Pattern IDs to suppress (e.g., team has decided a specific pattern is not relevant to their codebase). |
| `suppression.domains_in_test_code` | `[]` | Domains to suppress entirely when the finding is in test code (`*Tests.cs`, `*Test.cs`, `*.Tests.csproj`, `tests/` directory). |
| `project_context` | auto-detect | Selects the project-type calibration profile from the Domain-Specific Review Calibration table. |

**Precedence**: Repository configuration overrides agent defaults. Agent defaults override skill defaults. Suppressed findings are not emitted in the output — they are silently dropped.
