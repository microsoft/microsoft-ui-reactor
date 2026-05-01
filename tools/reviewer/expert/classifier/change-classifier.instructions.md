---
id: "expert-cs:change-classifier/v1"
title: Change Classification for C# Expert Code Review Routing
version: 1.0
workflow: expert-cs-code-review-orchestration
dri: Agentic Engineering System
---

# Change Classification Instructions (C#)

## Purpose

This document provides instructions for classifying C# code changes to enable intelligent routing to the right expert review skills. The classifier examines a diff and produces a structured classification that drives the orchestrator's routing decisions.

**FORBIDDEN**: Classifying based on file count alone; assuming all `.cs` files are equal risk; ignoring cross-file dependencies; treating XAML as cosmetic without inspecting bindings
**MANDATORY**: Inspect actual diff content; detect domain signals from code patterns; compute risk score from multiple factors; evaluate XAML bindings and templates at the same depth as code-behind

## Pre-Classification Guards

Before classifying, check for signals in the diff or PR metadata that could cause false routing:

- **Ignore merge conflict artifacts**: If the diff contains conflict resolution markers (`<<<<<<<`, `=======`, `>>>>>>>`) or the PR description references automated conflict resolution, do not treat the conflicting content as intentional code changes for routing purposes.
- **Deletion-only diffs**: If the diff contains only removed lines (no additions or modifications), emit NO FINDINGS. Pre-existing bugs in removed code are not actionable — the deletion is the fix. Exception: if the PR description explicitly states the code is being moved or refactored to another location, flag bugs that would carry forward.
- **XAML-only changes receive reduced weight**: If the diff modifies only `.xaml` or `.axaml` files and the changes are limited to layout, styling, or visual properties (margins, colors, font sizes, grid definitions), apply a 0.5 weight reduction to the final risk score. **Exception**: XAML changes that affect `{Binding}`, `{x:Bind}`, `DataTemplate`, `ControlTemplate`, `Style.Triggers`, `VisualStateManager`, event handlers, or `DependencyProperty` declarations receive full weight — these changes have behavioral impact.
- **Multi-signal requirement for dependency routing**: A keyword like "package" or "reference" appearing in code alone is NOT sufficient for build-packaging routing. Require that the **PR modifies `.csproj`, `Directory.Build.props`, `Directory.Packages.props`, `NuGet.config`, or `global.json`** AND the changes involve versioning, target framework, or package references.
- **Designer-generated code**: Files matching patterns like `*.Designer.cs`, `*.g.cs`, `*.g.i.cs` (generated XAML code-behind) should be excluded from domain signal detection unless the PR description indicates intentional hand-editing. Auto-generated code is not authored code.

## Routing Disambiguation

When a diff signal could match multiple domains, use these precedence rules:

| Ambiguous Signal | Correct Routing | Wrong Routing |
|-----------------|-----------------|---------------|
| `using` (namespace import) | general (skip) | ~~memory-lifecycle~~ |
| `using` (IDisposable pattern) | memory-lifecycle | ~~general~~ |
| `Task.Delay` in test code | test-infrastructure | ~~concurrency~~ |
| `lock` keyword in property getter | concurrency | ~~performance~~ |
| `async void` | concurrency (high priority) | ~~error-handling only~~ |
| `[Obsolete]` on internal member | general (low priority) | ~~api-design~~ |
| `[Obsolete]` on public member | api-design | ~~general~~ |
| `.csproj` TFM change only | build-packaging | ~~api-design~~ |
| `string.Format` vs interpolation | performance (if hot path) | ~~error-handling~~ |
| XAML `Margin`/`Padding` only | style/nit (skip) | ~~ui-framework~~ |
| XAML `{Binding}` or `{x:Bind}` | ui-framework | ~~style/nit~~ |
| `DependencyProperty.Register` | ui-framework + api-design | ~~general~~ |
| Merge conflict resolution message | skip entirely | ~~any domain~~ |

## Classification Framework

### Phase 1: Language Detection

Examine file extensions in the diff to determine primary language(s):

```
.cs                                → C#
.xaml, .axaml                      → C# (UI markup)
.csproj, .props, .targets          → C# (build/project)
.razor, .cshtml                    → C# (web UI)
.resx, .resw                       → C# (resources)
.json (appsettings*.json)          → C# (configuration)
.sln, .slnx                       → C# (solution)
Directory.Build.props              → C# (build)
Directory.Packages.props           → C# (build/dependency)
global.json                        → C# (SDK/toolchain)
nuget.config                       → C# (package source)
.editorconfig                      → C# (analyzer/style config)
```

Mixed-language PRs receive multi-domain routing. The primary language is determined by the majority of changed lines.

### Phase 2: Domain Signal Detection

Scan the diff content for domain signals. Each signal maps to one or more review domains with an associated weight representing the signal's strength for triggering that domain.

#### Memory/Lifecycle Signals
```
IDisposable (impl or usage)        → memory-lifecycle (weight: 0.7)
using statement/declaration        → memory-lifecycle (weight: 0.4)
~ClassName() (finalizer)           → memory-lifecycle (weight: 0.9)
GC.SuppressFinalize                → memory-lifecycle (weight: 0.7)
GC.Collect                         → memory-lifecycle (weight: 0.8)
GC.KeepAlive                       → memory-lifecycle (weight: 0.6)
unsafe { ... }                     → memory-lifecycle + security (weight: 0.9)
fixed ( ... )                      → memory-lifecycle (weight: 0.8)
stackalloc                         → memory-lifecycle + performance (weight: 0.7)
Span<T> / ReadOnlySpan<T>         → memory-lifecycle + performance (weight: 0.5)
Memory<T> / ReadOnlyMemory<T>     → memory-lifecycle + performance (weight: 0.5)
WeakReference / WeakReference<T>  → memory-lifecycle (weight: 0.7)
ConditionalWeakTable               → memory-lifecycle (weight: 0.8)
SafeHandle / CriticalHandle        → memory-lifecycle (weight: 0.7)
Marshal.AllocHGlobal               → memory-lifecycle + security (weight: 0.85)
Marshal.FreeHGlobal                → memory-lifecycle (weight: 0.6)
GCHandle                           → memory-lifecycle (weight: 0.8)
```

#### Concurrency Signals
```
async / await                      → concurrency (weight: 0.4)
async void                         → concurrency + error-handling (weight: 0.85)
Task.Run                           → concurrency (weight: 0.6)
Task.WhenAll / Task.WhenAny       → concurrency (weight: 0.5)
Task.Result / Task.Wait()         → concurrency (weight: 0.8)  # sync-over-async anti-pattern
.GetAwaiter().GetResult()          → concurrency (weight: 0.8)  # sync-over-async anti-pattern
lock ( ... )                       → concurrency (weight: 0.6)
SemaphoreSlim                      → concurrency (weight: 0.7)
Monitor.Enter / Monitor.Exit      → concurrency (weight: 0.7)
Interlocked.                       → concurrency (weight: 0.7)
CancellationToken                  → concurrency (weight: 0.4)
CancellationTokenSource            → concurrency (weight: 0.5)
Dispatcher.Invoke/BeginInvoke     → concurrency + ui-framework (weight: 0.7)
DispatcherQueue                    → concurrency + ui-framework (weight: 0.7)
SynchronizationContext              → concurrency (weight: 0.75)
ConfigureAwait(false)              → concurrency (weight: 0.5)
Channel<T>                         → concurrency (weight: 0.6)
BlockingCollection                 → concurrency (weight: 0.65)
Parallel.ForEach / Parallel.For   → concurrency + performance (weight: 0.6)
volatile                           → concurrency (weight: 0.7)
ReaderWriterLockSlim               → concurrency (weight: 0.7)
```

#### Error Handling Signals
```
try / catch / finally              → error-handling (weight: 0.2)
throw new                          → error-handling (weight: 0.3)
throw; (rethrow)                   → error-handling (weight: 0.2)
catch (Exception)                  → error-handling (weight: 0.5)  # overly broad catch
catch { } (empty catch)            → error-handling (weight: 0.6)  # swallowed exception
ArgumentException                  → error-handling (weight: 0.3)
ArgumentNullException              → error-handling (weight: 0.3)
InvalidOperationException          → error-handling (weight: 0.3)
ObjectDisposedException            → error-handling + memory-lifecycle (weight: 0.5)
NotImplementedException            → error-handling (weight: 0.4)  # incomplete implementation
? (nullable reference type)        → error-handling (weight: 0.2)
! (null-forgiving operator)        → error-handling (weight: 0.5)  # suppresses compiler warning
[NotNull]                          → error-handling (weight: 0.3)
[MaybeNull]                        → error-handling (weight: 0.3)
[NotNullWhen] / [MaybeNullWhen]   → error-handling (weight: 0.3)
[DoesNotReturn]                    → error-handling (weight: 0.4)
```

#### Security Signals
```
SqlCommand / SqlConnection         → security (weight: 0.7)
string concat in SQL               → security (weight: 0.95)  # SQL injection
Process.Start                      → security (weight: 0.8)
ProcessStartInfo                   → security (weight: 0.7)
[Authorize] / [AllowAnonymous]    → security (weight: 0.6)
HttpClient (new instantiation)     → security (weight: 0.6)
X509Certificate                    → security (weight: 0.7)
SecureString                       → security (weight: 0.7)
System.Security.Cryptography       → security (weight: 0.8)
[JsonConstructor]                  → security (weight: 0.6)
TypeNameHandling                   → security (weight: 0.95)  # deserialization attack vector
BinaryFormatter                    → security (weight: 0.95)  # insecure deserialization
[Serializable]                     → security (weight: 0.6)
DataContractSerializer             → security (weight: 0.6)
Assembly.Load / Assembly.LoadFrom → security (weight: 0.85)
Reflection.Emit                    → security (weight: 0.8)
Marshal.PtrToStructure             → security + memory-lifecycle (weight: 0.8)
P/Invoke ([DllImport])            → security + memory-lifecycle (weight: 0.7)
[LibraryImport]                    → security + memory-lifecycle (weight: 0.7)
```

#### Performance Signals
```
Span<T> / ReadOnlySpan<T>         → performance (weight: 0.4)
stackalloc                         → performance (weight: 0.5)
ArrayPool<T>.Shared                → performance (weight: 0.5)
ObjectPool<T>                      → performance (weight: 0.5)
.ToList() (in loop or hot path)   → performance (weight: 0.5)  # unnecessary allocation
string.Concat vs interpolation     → performance (weight: 0.3)
StringBuilder                      → performance (weight: 0.4)
ReadOnlySpan<char>                 → performance (weight: 0.4)
LINQ in tight loop                 → performance (weight: 0.6)  # allocation per iteration
ValueTask / ValueTask<T>          → performance (weight: 0.5)
IAsyncEnumerable                   → performance (weight: 0.4)
(object)value (boxing)             → performance (weight: 0.5)
struct vs class choice             → performance (weight: 0.4)
string.Create                      → performance (weight: 0.4)
[MethodImpl(AggressiveInlining)]   → performance (weight: 0.3)
[SkipLocalsInit]                   → performance + security (weight: 0.7)
ref struct / ref return            → performance (weight: 0.5)
CollectionsMarshal.GetValueRefOrAddDefault → performance (weight: 0.5)
FrozenDictionary / FrozenSet      → performance (weight: 0.4)
SearchValues<T>                    → performance (weight: 0.4)
```

#### API Design Signals
```
public class / public interface    → api-design (weight: 0.4)
public record / public struct     → api-design (weight: 0.4)
[Obsolete] on public member        → api-design (weight: 0.5)
sealed class                       → api-design (weight: 0.3)
abstract class / abstract method  → api-design (weight: 0.4)
nullable annotation on public API  → api-design (weight: 0.4)
DependencyProperty.Register        → api-design + ui-framework (weight: 0.5)
[DependencyProperty] attribute    → api-design + ui-framework (weight: 0.5)
generic constraint (where T : )   → api-design (weight: 0.4)
interface default method           → api-design (weight: 0.5)
init-only setter                   → api-design (weight: 0.3)
required modifier                  → api-design (weight: 0.4)
primary constructor               → api-design (weight: 0.3)
extension method (this parameter) → api-design (weight: 0.4)
IServiceCollection extensions     → api-design (weight: 0.5)
[EditorBrowsable(Never)]          → api-design (weight: 0.3)
public operator overload           → api-design (weight: 0.5)
implicit/explicit conversion      → api-design (weight: 0.6)
```

#### UI Framework Signals
```
DependencyProperty                 → ui-framework (weight: 0.7)
DependencyObject                   → ui-framework (weight: 0.6)
INotifyPropertyChanged             → ui-framework (weight: 0.6)
ICommand / RelayCommand           → ui-framework (weight: 0.5)
{Binding} / {x:Bind}             → ui-framework (weight: 0.7)
DataTemplate                       → ui-framework (weight: 0.6)
ControlTemplate                    → ui-framework (weight: 0.7)
Style / ResourceDictionary        → ui-framework (weight: 0.4)
VisualStateManager                 → ui-framework (weight: 0.6)
DispatcherQueue.TryEnqueue        → ui-framework + concurrency (weight: 0.7)
UserControl / Page                → ui-framework (weight: 0.5)
ContentControl / ItemsControl     → ui-framework (weight: 0.5)
ItemsRepeater / ListView          → ui-framework (weight: 0.5)
ObservableCollection               → ui-framework (weight: 0.5)
PropertyChanged event              → ui-framework (weight: 0.6)
XAML markup extensions             → ui-framework (weight: 0.6)
x:DataType                         → ui-framework (weight: 0.5)
Visual / VisualTreeHelper         → ui-framework (weight: 0.6)
Loaded / Unloaded events          → ui-framework + memory-lifecycle (weight: 0.5)
```

#### Build/Packaging Signals
```
<PackageReference>                 → build-packaging (weight: 0.5)
<ProjectReference>                 → build-packaging (weight: 0.3)
<TargetFramework(s)>              → build-packaging (weight: 0.5)
Directory.Build.props              → build-packaging (weight: 0.6)
Directory.Packages.props           → build-packaging (weight: 0.6)
global.json (SDK version)         → build-packaging (weight: 0.5)
<Nullable>enable</Nullable>       → build-packaging + error-handling (weight: 0.4)
<TreatWarningsAsErrors>           → build-packaging (weight: 0.3)
<AnalysisLevel>                    → build-packaging (weight: 0.4)
<EnforceCodeStyleInBuild>         → build-packaging (weight: 0.3)
.editorconfig analyzer rules      → build-packaging (weight: 0.3)
NuGet.config changes              → build-packaging (weight: 0.5)
```

#### Testing Signals
```
[TestMethod] / [Fact] / [Theory] → testing (weight: 0.3)
[TestClass] / [Collection]        → testing (weight: 0.3)
Mock<T> / Substitute.For<T>      → testing (weight: 0.4)
Assert. / Should. / Expect(       → testing (weight: 0.2)
[SetUp] / [TearDown]             → testing (weight: 0.3)
[ClassInitialize]                  → testing (weight: 0.3)
TestServer / WebApplicationFactory → testing (weight: 0.5)
UITestMethodAttribute              → testing + ui-framework (weight: 0.5)
```

### Phase 3: Change Type Classification

Analyze the diff structure to determine change type:

| Change Type | Detection Signals |
|-------------|-------------------|
| **new-feature** | New files created, new `public` types/members, new project references, new NuGet packages added |
| **bugfix** | Small targeted changes (< 50 lines), references to issues/bugs in commit message, changes to existing logic without new public APIs, null checks added |
| **refactor** | Renamed symbols, moved code between files/namespaces, `sealed` added, `readonly` added, no new public API surface, tests unchanged or adapted |
| **perf-optimization** | `Span<T>` adoption, pooling introduced, LINQ-to-loop conversion, `ValueTask` replacement, benchmark additions, `StringBuilder` adoption |
| **security-fix** | Changes to auth/crypto/validation code, parameterized queries replacing string concat, `BinaryFormatter` removal, security advisory references |
| **dependency-update** | `.csproj` PackageReference changes only, version bumps, TFM changes, `global.json` SDK updates |
| **test-only** | Changes only in test projects or files matching `*Tests.cs`, `*Test.cs`, `*.Tests.csproj`, new test cases |
| **docs-only** | Changes only in XML doc comments (`///`), README, or `.md` files |
| **deletion-only** | Diff contains only removed lines with no additions or modifications |

### Phase 4: Risk Score Computation

```
For each detected domain:
    domain_score = max(signal_weights for signals detected in that domain)

base_risk = weighted_average(domain_scores, domain_risk_weights)

domain_risk_weights:
    memory-lifecycle:  0.8
    security:          0.95
    concurrency:       0.85
    error-handling:    0.5
    performance:       0.4
    api-design:        0.6
    ui-framework:      0.7
    build-packaging:   0.3
    testing:           0.3

change_type_multiplier:
    new-feature:       1.0
    bugfix:            0.8
    refactor:          0.7
    perf-optimization: 1.2
    security-fix:      1.5
    dependency-update: 0.6
    test-only:         0.3
    docs-only:         0.1
    deletion-only:     0.0

size_factor:
    < 50 lines:   0.8
    50-200 lines:  1.0
    200-500 lines: 1.2
    > 500 lines:   1.5

cross_file_factor:
    1 file:      1.0
    2-5 files:   1.1
    5-10 files:  1.2
    > 10 files:  1.4

risk_score = base_risk * change_type_multiplier * size_factor * cross_file_factor
risk_score = clamp(risk_score, 0.0, 1.0)
```

## Output Format

The classifier output feeds directly into the schema's `classification` object. The shape must match exactly.

```json
{
  "classification": {
    "language": "csharp",
    "domains": ["concurrency", "ui-framework"],
    "change_type": "new-feature",
    "risk_score": 0.72
  }
}
```

**Field mapping from internal computation to output:**
- `language`: The primary language detected in Phase 1. Use `"csharp"` for C# projects. If mixed with other languages, use `"mixed"`.
- `domains`: Flat string array of domain IDs from Phase 2 (e.g., `"memory-lifecycle"`, `"concurrency"`, `"ui-framework"`). Only domains with detected signals. Ordered by signal strength (strongest first).
- `change_type`: From Phase 3.
- `risk_score`: The final clamped score from Phase 4.

## Validation

After classification:
1. Verify at least one domain was detected (if none: default to `error-handling` with risk 0.3)
2. Verify change type is consistent with diff structure
3. Cross-check: if `unsafe` is detected but memory-lifecycle domain was not flagged, add it
4. Cross-check: if new `public` types/members exist but api-design was not flagged, add it
5. Cross-check: if `DependencyProperty` or XAML binding changes exist but ui-framework was not flagged, add it
6. Cross-check: if `async void` is detected, both concurrency and error-handling must be flagged
7. Cross-check: if `Task.Result` or `.GetAwaiter().GetResult()` is detected in async context, concurrency must be flagged with elevated weight
