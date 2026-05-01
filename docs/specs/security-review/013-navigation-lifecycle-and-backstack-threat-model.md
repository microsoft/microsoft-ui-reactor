# Chunk 13 — Navigation lifecycle and back-stack persistence

**Status:** Phase 2 — review complete
**Reviewed commit SHA:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Reviewer scope:** STRIDE + code review with focus on tampering / type-confusion on persisted nav state, info disclosure of route params, and lifecycle-guard correctness.

---

## 1. Scope

| File | LOC | Role |
|---|---|---|
| `src/Reactor/Core/Navigation/NavigationStack.cs` | 257 | Pure data structure: back/forward stacks + lifecycle guards. |
| `src/Reactor/Core/Navigation/NavigationLifecycle.cs` | 56 | Guard / lifecycle-event context records (`NavigatingToContext`, `NavigatedToContext`, `NavigatedFromContext`). |
| `src/Reactor/Core/Navigation/NavigationCache.cs` | 132 | In-memory LRU page cache keyed by route. |
| `src/Reactor/Core/Navigation/NavigationContext.cs` | 15 | Per-`TRoute` static `Context<NavigationHandle<T>?>` instance. |
| `src/Reactor/Core/Navigation/NavigationHandle.cs` | 298 | Public navigation API + `GetState`/`SetState` JSON serialization. |
| `src/Reactor/Core/Navigation/NavigationTransition.cs` | 118 | Transition records (Slide/Fade/DrillIn/Spring/Connected/Suppress). |
| `src/Reactor/Core/Navigation/TransitionEngine.cs` | 287 | Composition-thread animation runner. |
| `src/Reactor/Core/Navigation/NavigationDiagnostics.cs` | 118 | Diagnostic events + `Debug.WriteLine` logging. |
| `src/Reactor/Core/Navigation/DeepLinkMap.cs` | 259 | URI-pattern → route resolution (regex compile, query-string parse). |
| `src/Reactor/Core/PersistedStateCache.cs` | 43 | Process-lifetime `ConcurrentDictionary<string, object?>` for `UsePersisted` hook. |
| **Total** | **1583** | |

**Out-of-band but referenced for context:** `src/Reactor/Core/RenderContext.cs:354–379, 1140–1150` (only consumer of `PersistedStateCache`).

> The chunk-plan (`000-chunking-and-threat-model.md`) frames the primary threat as "tampering on persisted nav state … deserialization gadgets, type confusion … info disclosure of params in persisted state". A central finding of this review is that **the framework does not, in the in-tree code as of `4623474`, persist navigation state to disk anywhere**. The threat surface contemplated in the plan is therefore latent — it materializes only when an *application* opts in to `NavigationHandle.GetState`/`SetState` and stores the resulting JSON somewhere.

---

## 2. Data-flow diagram

```
                       ┌─────────────────────────────────────────────┐
                       │                Application code              │
                       │  (Navigate / GoBack / GoForward / Reset)     │
                       └─────────────────┬───────────────────────────┘
                                         │
                                         ▼
                ┌──────────────────────────────────────────────┐
                │  NavigationHandle<TRoute>                    │
                │  (handle.cs)                                 │
                │   ├─ Fires Navigated event (in-proc)         │
                │   ├─ Sets _pendingTransitionOverride         │
                │   └─ Delegates to NavigationStack<TRoute>    │
                └────────┬─────────────────────────────────────┘
                         │
        ┌────────────────┼─────────────────┐
        │                │                 │
        ▼                ▼                 ▼
 ┌────────────┐   ┌───────────────┐   ┌──────────────────┐
 │ Stack push │   │ Lifecycle     │   │ Diagnostics      │
 │  / pop     │──▶│  guards       │──▶│ events +         │
 │ (in-proc   │   │ (in-proc      │   │ Debug.WriteLine  │
 │  list ops) │   │  callbacks)   │   │ (in-proc + ETW   │
 └────────────┘   └───────────────┘   │  attached debug) │
                                      └──────────────────┘
                                              │
                                              ▼
                  ┌────────────────────────────────────────────┐
                  │  NavigationHost (out-of-scope, Tier-4)      │
                  │   ├─ Reads CurrentRoute, mounts page        │
                  │   ├─ Calls NavigationCache.Add / TryGet     │
                  │   └─ Calls TransitionEngine.RunTransition   │
                  └────────────────────────────────────────────┘

  Optional opt-in flows (NOT wired by the framework):
  ────────────────────────────────────────────────────
  1) State export:  app calls handle.GetState() → JSON string → app's
                    own storage (file, settings, etc.).
  2) State import:  app reads JSON → handle.SetState(json) →
                    JsonSerializer.Deserialize<NavigationState<TRoute>>
                    → stack.RestoreState (no guards re-run).
  3) Deep link:     app receives URI → DeepLinkMap.Resolve(uri) →
                    RouteArgs (string→typed parse) → factory(args) → route.

  In-memory only (no disk):
  ─────────────────────────
  • NavigationCache  — UIElement instances, process lifetime, LRU evict.
  • PersistedStateCache — string→object?, process lifetime, MaxEntries=4096.
```

The chunk has **no I/O of its own**: no `File.*`, no `FileStream`, no `ApplicationData.LocalFolder`, no socket. (Verified by grep: no `File.WriteAllText|File.ReadAllText|FileStream` in `src/Reactor/Core/Navigation`.) All "persistence" semantics are **process-lifetime in-memory**.

---

## 3. Trust boundaries crossed

| # | Boundary | Direction | Trust assumption made |
|---|---|---|---|
| **B1** | Application code ↔ navigation API | bidirectional | App is trusted (Tier-0 of the plan). Routes are constructed by app code; `Navigate(route)` is just a typed call. |
| **B2** | `NavigationHandle.SetState(string json)` ↔ caller-supplied JSON | inbound | The JSON string is treated as **caller-controlled**. Provenance is the app's responsibility — Reactor offers no built-in storage and therefore no integrity check. If the app reads the JSON from disk, the disk file is semi-trusted (other local processes / users may mutate it). |
| **B3** | `DeepLinkMap.Resolve(Uri)` ↔ external URI | inbound | Deep-link URIs may originate from OS protocol-handler activation (i.e. another process / a clicked link in a browser). Treat as untrusted text. The chunk only converts text → route; **what the host *does* with the route** (e.g. auto-push it onto the stack) determines actual reach. |
| **B4** | Diagnostic events | outbound | Subscribers may include arbitrary in-proc consumers (devtools — Chunk 02; user telemetry). Diagnostic payloads contain `object From`, `object To` — the route values themselves. |
| **B5** | `Debug.WriteLine` in `NavigationDiagnostics.cs` | outbound | Goes to attached debugger / `OutputDebugString`, which is readable by **any process with `SeDebugPrivilege` or via DbgPrint sessions**. Logged content is `from {from} → {to}` with raw `ToString()` of the route. |
| **B6** | `Detach()` | internal | Lifecycle guard delegate is cleared on unmount — boundary between component-instance lifetime and the long-lived `NavigationHandle`. Mis-use leaks delegates that pin component graph. |

The plan's framing assumed a B2 with "persisted state on disk written by Reactor"; in the current code that boundary is **deferred to the application** — the framework just exports a JSON string and accepts one back. Findings call this out so the review's scope can be revisited if disk persistence is later added (e.g. a `WithPersistedNavigationState(IStorage)` host extension).

---

## 4. Asset inventory

| ID | Asset | Why it's worth attacking |
|---|---|---|
| **A1** | Currently active route (`_stack.Current`) | Determines which screen the user sees — overwriting it can phish (e.g. fake login page at app start). |
| **A2** | Back stack contents | Affects `GoBack` destination; tampering can route the user to a hostile route after a back press. Also discloses where the user has been. |
| **A3** | Route param values held in routes | If `TRoute` is a record like `OrderDetailsRoute(int OrderId, string SessionToken)`, those fields land in any `GetState` JSON the app persists. **Any secret embedded in a route is at risk.** |
| **A4** | Navigation lifecycle guards (`onNavigatingFrom`) | A guard might enforce "unsaved-changes" prompts, license-check, or auth-redirect. `RestoreState` (NavigationStack.cs:186) **bypasses guards** by design — exposes a "forge a stack state past the guard" attack if the JSON path is untrusted. |
| **A5** | `NavigationCache` mounted controls | Hold live `UIElement`s with full UI state (input values, scroll positions). LRU keying is by `route` *object*, so structural-equality misuse (mutable routes) can confuse cache lookup. |
| **A6** | `PersistedStateCache` entries | In-memory only, but content can include arbitrary developer-passed values (`UsePersisted<T>`) — review-relevant for ETW / log inspection rather than disk threats. |
| **A7** | Compiled regexes in `DeepLinkMap` | Each `Map(pattern, …)` call produces a `Regex(.., RegexOptions.Compiled)` (DeepLinkMap.cs:225). Patterns are app-author authored at startup, not attacker-supplied. |
| **A8** | `NavigationDiagnostics` event subscribers | A static-event sink — once subscribed, leaks across hosts; payload contains route values. |

---

## 5. STRIDE table

| # | Cat. | Threat | Attacker | Impact | Likelihood | Current mitigation | Finding |
|---|---|---|---|---|---|---|---|
| T1 | **Tampering** | Modified persisted JSON given to `SetState` causes the back stack to be replaced with attacker-controlled routes; lifecycle guards are skipped. | Local user with write access to app's saved state file (if app persists `GetState` to disk). | App resumes on attacker-chosen page; `onNavigatingFrom` prompts ("unsaved changes") bypassed; phishing screens at startup. | Med (depends on app opting in) | None at framework layer. `RestoreState` (`NavigationStack.cs:184–194`) explicitly does **not** invoke guards. | **F-1** below. |
| T2 | **Tampering / Type confusion** | Polymorphic route hierarchy uses `[JsonPolymorphic]` + `[JsonDerivedType]`; attacker mutates the JSON's `$type` discriminator to a registered-but-unintended derived type. | Same as T1. | App reaches a route the user didn't navigate to; `Navigated` event fires with a real-typed payload that confuses listener logic. | Med | None — `JsonSerializer.Deserialize` only honors derived types the app *declared*, so this is bounded by the app's declared type set, but any declared type is reachable. | **F-2** below. |
| T3 | **Tampering / RCE via deserializer gadgets** | Attacker crafts JSON to instantiate a gadget type during deserialization. | Same as T1. | RCE if a known `System.Text.Json` gadget existed. | Low (no `BinaryFormatter`; `System.Text.Json` requires explicit `[JsonDerivedType]`/`TypeInfoResolver` opt-in). | `JsonSerializer.Deserialize<NavigationState<TRoute>>` (`NavigationHandle.cs:271`) — closed generic, no `object`/dynamic, no `TypeNameHandling`. STJ default refuses unknown discriminators. | **No finding** — see Finding F-3 (informational). |
| T4 | **Info disclosure** (persisted) | Route values containing secrets (auth tokens, OAuth `code`, account IDs, deep-link URLs with credentials) end up in app-stored JSON. | Anyone with read access to wherever the app puts the JSON. | Credential theft / PII leak. | Med-High in real apps (developer ergonomics push tokens into routes). | None — `GetState` blindly serializes whatever the route record exposes. No filter, no "marked-secret" attribute. | **F-4** below. |
| T5 | **Info disclosure** (in-process) | `Debug.WriteLine` in `NavigationDiagnostics.cs:41,47,53,59,65,71,77,83,89` emits route `ToString()` to `OutputDebugString`, which is readable by any process with the right privilege. | Local malware / sysmon. | Leakage of route params (which may contain tokens). | Low–Med (only in attached-debugger or DbgView scenarios). | None. | **F-5** below. |
| T6 | **Info disclosure** (memory) | `PersistedStateCache` keeps `object?` values for the life of the process; when the user logs out the values aren't zeroed, they sit in `ConcurrentDictionary` until GC. | Memory dump / debugger. | Stale secrets recoverable from a process dump. | Low | `Clear()` exists but is never called automatically. | **F-6** below. |
| T7 | **DoS / Memory** | An app that uses dynamic `UsePersisted` keys grows `PersistedStateCache` to `MaxEntries = 4096` (`PersistedStateCache.cs:13`); after that, *new* keys are silently ignored. | In-app pattern (no external attacker required). | Silent feature breakage — `UsePersisted` returns the developer-passed `initialValue` because the `Set` no-op'd, with no diagnostic. | Med (footgun). | The 4096 cap exists but the rejection is silent (`if (_cache.Count >= MaxEntries && !_cache.ContainsKey(key)) return;`). | **F-7** below. |
| T8 | **DoS / Memory** | `NavigationCache` keys are `object` (`NavigationCache.cs:23`). Equality of mutable route values changes after insertion → entries become unfindable, never evict by key, only by LRU. | Application footgun (mutable route objects). | Cache fills with orphans up to `MaxSize`; `_onEvict` runs but UI state is lost. | Low–Med | Records (default in `NavigationTransition` and most app routes) are immutable, so OK if devs follow guidance. | **F-8** below. |
| T9 | **DoS** | `DeepLinkMap.Resolve` runs the URI through every compiled regex in registration order (`DeepLinkMap.cs:158`). `(.+)` for `**` wildcard and `[^/]+` for default params are not catastrophic, but `RegexOptions.IgnoreCase` + Unicode default may be slower than necessary on hostile-but-valid URIs. | Caller of deep-link entry. | Slow matching at startup if many patterns. | Low | Regexes are pre-compiled. No timeout. | **F-9** below. |
| T10 | **Tampering / EoP** | `RestoreState` (`NavigationStack.cs:184`) takes `IList<TRoute>` and stores them as-is, then fires `OnChanged` (which re-renders). If `TRoute` is a reference type whose handlers fire side-effects on construction, **deserialization itself is the side-effect**, not `RestoreState`. So this isn't a new risk on top of T1. | — | — | — | Same as T1. | (Folded into F-1.) |
| T11 | **Spoofing** | `NavigationDiagnostics` events are static — first subscriber leaks to second host. A malicious in-proc actor (Chunk 02 devtools tool, Chunk 14 component) could subscribe and observe nav. | In-proc only. | Info disclosure of route values. | Low (in-proc adversary already wins). | None. | **F-10** below. |
| T12 | **Repudiation** | No persistent audit log of nav events. `Debug.WriteLine` only goes to debugger. `NavigationDiagnostics` only fires if subscribed. | — | "Did the user navigate to X?" cannot be answered post-hoc. | n/a — outside threat model for a UI framework. | (Out of scope; users own audit if needed.) | No finding. |
| T13 | **EoP** (deep link) | `DeepLinkMap.ParseQueryString` uses `Uri.UnescapeDataString` (`DeepLinkMap.cs:200,202`). An attacker-supplied query value can contain `\0`, control chars, RTL-override codepoints — those land verbatim in `RouteArgs`. If the app concatenates them into another URL or shell, downstream injection is possible. | OS protocol handler / browser. | Depends on app; framework-side: data only. | Med (against careless apps) | None. | **F-11** below. |
| T14 | **Tampering** | `NavigationStack.PopTo(predicate)` runs an app-supplied delegate during traversal (`NavigationStack.cs:200–236`). If the predicate mutates the stack (re-entrancy through `OnChanged` indirectly), iteration semantics are undefined. | App bug. | Inconsistent stack. | Low | Implementation iterates a `List<>` while not modifying it inside the predicate scan loop, only afterwards — currently OK, but fragile. | **F-12** below. |
| T15 | **Type confusion / Casting** | `NavigationCache` keys/values are `object` (`NavigationCache.cs:23, 41, 58, 113`). A reconciler bug that calls `Add(routeA, page)` then `Remove(otherEqualsButDifferentType)` would silently miss. Same hashing/`Equals` traps as any `Dictionary<object,_>`. | App / reconciler bug. | UI inconsistency. | Low | None. | **F-13** below. |

---

## 6. Findings

> Severity scale: **Critical / High / Medium / Low / Informational**.
> Severity reflects *intrinsic* impact assuming an app exercises the feature. Many findings are conditional ("if the app opts into persistence…"), and the conditional is called out per finding.

### F-1 — `SetState` is unauthenticated state restore that bypasses guards (Medium, conditional)

**File:** `src/Reactor/Core/Navigation/NavigationHandle.cs:269–282` and `NavigationStack.cs:184–194`.

`NavigationHandle.SetState(string json, JsonSerializerOptions?)` calls `JsonSerializer.Deserialize<NavigationState<TRoute>>` and then `_stack.RestoreState(...)`, which by design **does not invoke `LifecycleGuard` or `Guard`** (`NavigationStack.cs:184` doc: *"Replaces the entire stack state. Does NOT invoke guards. Fires OnChanged."*). It then fires `Navigated` with `NavigationMode.Reset`.

Consequences if an app persists `GetState()` output to disk and calls `SetState` on startup:
- An attacker who can write the file replaces the entire stack with their own routes.
- `onNavigatingFrom` (intended for "unsaved-changes" prompts) is skipped — but only meaningful at restore time, since there's nothing to navigate *from* yet. So this is mostly a non-issue *at app startup*; it becomes one if `SetState` is used for "import a session from URL" or similar runtime flows.
- `Navigated` event handlers run with attacker-controlled `Route` value. Any handler that takes route-derived action (analytics ID, telemetry, fetch initial data) is now driven by the attacker.

**Mitigation** — three options, listed by intrusiveness:
1. **Doc-only fix:** call out in `GetState`/`SetState` XML doc that the JSON is *unauthenticated* and the caller is responsible for integrity (HMAC, DPAPI, signed envelope). Currently the doc just says *"The route type must support `System.Text.Json` serialization"* — no security note.
2. **Helper API:** ship a `NavigationStateProtector` that wraps `GetState` with `ProtectedData.Protect` (DPAPI, user scope) and a paired `Unprotect` for `SetState`. Default app-data persistence to that helper. DPAPI binds the file to the user account and stops other-local-user mutation.
3. **Schema validation:** `NavigationHandle.SetState` already throws `JsonException` if `state.Current is null` (line 274). Consider also rejecting unreasonably large stacks (e.g. cap `BackStack.Count + ForwardStack.Count` at 1024) to neutralize "tampered with a 10MB file" DoS at deserialization time.

**Severity rationale:** Medium because:
- The framework does not auto-persist (so this is gated on app opt-in), and
- The realistic impact is "phish the user on resume", not RCE.
But the doc deficit is real: a developer reading the current docstring would not know guards are skipped on import.

### F-2 — Polymorphic `TRoute` makes `SetState` a "navigate to any registered route" primitive (Medium, conditional)

**File:** `src/Reactor/Core/Navigation/NavigationHandle.cs:243–282`.

The doc string at lines 246–249 actively recommends: *"For polymorphic route hierarchies, use `[JsonPolymorphic]` and `[JsonDerivedType]` attributes on the base route type."* If a developer follows that advice, `SetState` will instantiate **any derived type that the app has declared**, not just the routes the user previously navigated to.

Threat scenario: an app declares routes `LoginRoute`, `DashboardRoute`, `AdminPanelRoute`, `DebugConsoleRoute`. UI flow only ever pushes `AdminPanelRoute` after a privilege check. A tampered persisted state file substitutes `AdminPanelRoute` (or `DebugConsoleRoute`) for the saved `LoginRoute`. After `SetState`, the app renders the admin panel — not because of an STJ vulnerability but because *any declared derived type is reachable*.

This is effectively an authorization-context bypass: route-construction was assumed to imply prior-auth.

**Mitigation:** Add to the doc: *"If you persist navigation state to durable storage, treat the persisted file as untrusted: any `[JsonDerivedType]` you declare is reachable on restore. Re-run authorization on `Navigated` handlers, do not assume the route would only be present if it had previously been authorized."*

Optionally, surface a `NavigationStateValidator<TRoute>` callback on `SetState` that the app can use to reject untrusted route types per-entry.

### F-3 — Deserializer gadget surface is bounded but not zero (Informational)

**File:** `src/Reactor/Core/Navigation/NavigationHandle.cs:271`.

`JsonSerializer.Deserialize<NavigationState<TRoute>>(json, options)` is closed-generic into a sealed type. There is no `BinaryFormatter`, no `TypeNameHandling`, and the JSON contract resolver chain does not include the unsafe paths (`DefaultJsonTypeInfoResolver` is referenced only in `DevtoolsMcpServer.cs`, not here).

`System.Text.Json` *does* allow custom `JsonConverter`s that run user code on deserialize, and the caller can pass arbitrary `JsonSerializerOptions` (including converters and a custom `TypeInfoResolver`). The framework's posture here is "trust the caller's options". That is appropriate, but the implication is: **if a downstream framework provides an opinionated `JsonSerializerOptions` to `SetState` that allows reflection-based type resolution, all bets are off.** There's no realistic gadget path with `System.Text.Json` defaults today.

**Action:** none. Note this in design rationale so that a future "auto-persist" feature inherits the right defaults (e.g. *do not* let app pass arbitrary `JsonSerializerOptions` if the framework owns the storage).

### F-4 — `GetState` serializes route values verbatim; secrets in routes leak to whatever storage the app picks (High, conditional)

**File:** `src/Reactor/Core/Navigation/NavigationHandle.cs:252–261` and `NavigationHandle.cs:288–298` (`NavigationState<TRoute>`).

`GetState()`:
```csharp
var state = new NavigationState<TRoute> {
    BackStack = _stack.BackStack.ToList(),
    Current = _stack.Current,
    ForwardStack = _stack.ForwardStack.ToList(),
};
return JsonSerializer.Serialize(state, options);
```

The framework has no concept of "this route field is a secret". Whatever the developer puts on the route record is serialized. In practice, route records frequently carry:
- selected entity IDs (`OrderId`, `UserId`) — usually fine,
- session/refresh tokens passed through deep-link auth flows,
- OAuth `code` / `state` query-string values from a deep-link callback (`DeepLinkMap.cs:62–65`),
- file system paths.

If the app then writes the JSON to `%LOCALAPPDATA%\<app>\nav.json` with default ACLs, **other local users on the same machine cannot read it (NTFS default per-user ACLs on `%LOCALAPPDATA%`), but malware running as the same user can.** The bigger concern is roaming/sync providers (OneDrive, profile-roaming) that may copy `%LOCALAPPDATA%`-adjacent paths off-box.

**Mitigation:**
1. Document strongly that `GetState` output is plaintext and includes everything the app put on the route. Recommend filtering before persistence (e.g. omit auth context from the route record, store it in a separate DPAPI-encrypted store).
2. Provide a `[NavStateIgnore]`-style attribute (or just refer to `[JsonIgnore]`, which already works) and document it explicitly.
3. Consider ship a sample / helper that wraps `GetState` with DPAPI before disk write.

### F-5 — `Debug.WriteLine` in `NavigationDiagnostics` leaks route `ToString()` to `OutputDebugString` (Low)

**File:** `src/Reactor/Core/Navigation/NavigationDiagnostics.cs:41, 47, 53, 59, 65, 71, 77, 83, 89`.

Each diagnostic entry-point starts with a `Debug.WriteLine($"[Reactor.Nav] ... {from} → {to}")`. `Debug.WriteLine` is `[Conditional("DEBUG")]`-style — but on .NET the call goes to all registered `TraceListener`s, and on Windows the `DefaultTraceListener` calls `OutputDebugString`, **including in `Release` builds** because `Debug.WriteLine` in `System.Diagnostics` is gated by the `DEBUG` define **of the assembly that calls it**, which for a NuGet-shipped Reactor.dll is the build configuration of Reactor itself, not the consumer.

Implication: any process that opens a `DbgView` session (or attaches as debugger) on the user's box reads every navigation event — including the `ToString()` of the route, which by record-default in C# expands to `RouteName { Field1 = value1, Field2 = value2 }` (i.e. *all* fields, including any secret carried on the route).

**Mitigation:**
1. Build Reactor with `DEBUG` undefined for `Release` shipped binaries (the standard `dotnet build -c Release` already does this for `Debug.WriteLine` semantics — verify in the publish pipeline). Confirm the Reactor.dll on NuGet is built `-c Release` so these calls compile out.
2. If diagnostics-style tracing in Release is desired, route through `EventSource` / ETW (Reactor already has `Core/Diagnostics/ReactorEventSource.cs`) **with the route value as an *opaque identifier* (e.g. `route.GetType().Name + hash`) rather than the full `ToString()`**.
3. As a defense-in-depth, replace `Debug.WriteLine($"... {to}")` with `Debug.WriteLine($"... {to.GetType().Name}")` to avoid leaking field values to debug output even if `DEBUG` is on.

### F-6 — `PersistedStateCache.Clear()` is never invoked automatically; secrets linger in process memory (Low)

**File:** `src/Reactor/Core/PersistedStateCache.cs:39–42`.

The cache is a `static ConcurrentDictionary<string, object?>` — values live for the lifetime of the AppDomain. There is no logout / lock-screen / app-suspended hook that clears it. If a developer puts a secret in `UsePersisted("authToken", token)`, the secret stays in the dictionary until the app exits.

**Mitigation:** Add a public `ReactorApp.ClearPersistedState()` or an `OnUserSwitch` lifecycle hook. Document that `UsePersisted` should not hold secrets.

### F-7 — `PersistedStateCache.Set` silently ignores writes after 4096 entries (Low)

**File:** `src/Reactor/Core/PersistedStateCache.cs:27–32`.

```csharp
internal static void Set<T>(string key, T value)
{
    if (_cache.Count >= MaxEntries && !_cache.ContainsKey(key))
        return;
    _cache[key] = value;
}
```

When the cap is hit, *new* keys' `Set` calls are no-ops with no exception, no log, no diagnostic. A developer who keys `UsePersisted` by something they thought was bounded but actually grows (e.g. `UsePersisted($"row-{rowId}", …)`) will silently lose persistence after the first 4096 distinct rows.

**Severity:** correctness/footgun, not a security issue per se, but it can mask security-relevant state being lost (e.g. "remember-this-device" toggle never persists past the cap).

**Mitigation:** Log to `Debug.WriteLine` (or better, the Reactor `EventSource`) on first overflow per key prefix; consider adopting an LRU eviction policy similar to `NavigationCache`.

### F-8 — `NavigationCache` keys by `object` with structural equality; mutable routes break LRU (Low)

**File:** `src/Reactor/Core/Navigation/NavigationCache.cs:23, 41, 58, 113`.

Cache uses `Dictionary<object, CachedPage>` with whatever `Equals`/`GetHashCode` the developer's `TRoute` provides. Records are fine (immutable, value-based). A class-typed `TRoute` with reference equality is also fine but defeats the "navigating back restores the exact visual state" contract. A *mutable* route (struct with mutable fields, or class with overridden equality based on mutable fields) hashes one way at insert and another way at lookup → lookup misses, the cache fills until LRU evicts.

**Mitigation:** XML doc on `NavigationCache` and on `NavigationHandle.Navigate` already advise route immutability indirectly. Make it explicit: *"Route values used as cache keys must be immutable; mutating a route after navigation will cause cache lookups to miss and pages to be re-mounted."*

### F-9 — `DeepLinkMap` regex compile has no per-pattern timeout (Informational)

**File:** `src/Reactor/Core/Navigation/DeepLinkMap.cs:225`.

```csharp
return (new Regex($"^{regexPattern}$", RegexOptions.Compiled | RegexOptions.IgnoreCase), …);
```

No `MatchTimeout`. Patterns are author-controlled (developer registers them at startup), so the threat is bounded. URI inputs hit `(.+)` for `**` and `[^/]+` for default params — both linear-time. No catastrophic backtracking pattern observed in the framework-level grammar. (Still, the absence of a timeout means any future pattern source — e.g. plug-in–supplied — would inherit unbounded match cost.)

**Mitigation:** consider passing `TimeSpan.FromMilliseconds(100)` as `Regex` matchTimeout. Cheap defense in depth.

### F-10 — `NavigationDiagnostics` events are static — leaky in multi-host scenarios (Low)

**File:** `src/Reactor/Core/Navigation/NavigationDiagnostics.cs:13–37`.

All diagnostic events are `public static event Action<…>?`. In a process that hosts multiple Reactor windows / multiple `NavigationHost` instances, any subscriber receives events from *all* of them. There's also no unsubscribe ceremony — a careless `+=` from a component that re-mounts will leak handler references and pin the component graph.

**Mitigation:** doc the lifetime semantics (subscribe at process bootstrap, unsubscribe at app shutdown). Or move events to per-`NavigationHandle` instance events (`Navigated` already exists per-handle; consider exposing the cache/transition events the same way).

### F-11 — `RouteArgs` exposes raw decoded query-string values (Low)

**File:** `src/Reactor/Core/Navigation/DeepLinkMap.cs:189–205`.

`Uri.UnescapeDataString` produces a `string` that may contain `\0`, RTL-override codepoints (`U+202E`), control characters, embedded newlines. `RouteArgs.GetString` / `Query` return these verbatim. The framework does no further sanitization — appropriate for a string-typed contract, but worth documenting.

For typed accessors (`int`, `long`, `bool`, `Guid`) the `Parse` step rejects junk (DeepLinkMap.cs:80–84). For `string` the developer must validate.

**Mitigation:** doc note on `RouteArgs.GetString` / `Query`: *"The returned value is the raw URL-decoded string; it may contain control characters and bidirectional-override codepoints. Validate before logging, displaying in trust-relevant UI, or interpolating into another URL."*

### F-12 — `PopTo` evaluates predicate during traversal (Informational)

**File:** `src/Reactor/Core/Navigation/NavigationStack.cs:200–236`.

The predicate runs in a scan loop *before* any mutation; the mutation block follows. So a re-entrant predicate that mutates the stack would currently work, but the design is fragile: a future refactor that interleaves scan and mutate would break. Since the predicate is app-provided, also document the expectation: *"the predicate must be pure"*.

**Mitigation:** doc the contract; optionally guard with a re-entrancy flag.

### F-13 — `NavigationCache` uses `Dictionary` with `object` keys; default equality may be hash-collision sensitive (Informational)

**File:** `src/Reactor/Core/Navigation/NavigationCache.cs:23`.

Standard concern with `Dictionary<object, T>`: an attacker who controls many distinct routes that hash to the same bucket can degrade insertion/lookup to O(n). In this chunk routes are app-author-typed (not attacker-typed), and the cache is bounded by `MaxSize` (a NavigationHost-set value). Not an external attack surface.

**Mitigation:** none required.

---

## 7. Open questions

1. **Will Reactor ever ship a built-in nav-state persister?** The plan and chunk description imply yes, but no such code exists in tree at `4623474`. If the answer is "yes, soon", design with DPAPI-encrypted blobs from day one and refer F-1 / F-2 / F-4 mitigations into that design.
2. **Is `Reactor.dll` built with `DEBUG` undefined when shipped via NuGet?** F-5 depends on this. Confirm the NuGet pack pipeline uses `-c Release` and that `Debug.WriteLine` calls in `NavigationDiagnostics` are elided. (Spot-check by `ildasm` or `dotnet-symbol` on a NuGet artifact.)
3. **What is the trust model around `JsonSerializerOptions` passed to `SetState`?** Today the caller can pass arbitrary options (custom converters, custom `TypeInfoResolver`). If the app's policy is "any plug-in can call `SetState`", a malicious plug-in could pass an unsafe options bag. Should the framework whitelist or freeze options for nav serialization?
4. **Is `NavigationCache.Add` expected to be reentrant safe with `_onEvict`?** `_onEvict` is invoked while holding `_lock` (line 102, inside `EvictLocked` called from inside the outer lock at line 70). If `_onEvict` calls back into `NavigationCache` or schedules a UI-thread continuation that does, a deadlock or re-entrancy bug is possible. Verify with the `NavigationHost` author.
5. **Should diagnostic events redact route values when a "release" build flag is set?** F-5 is bounded but easy to overlook. A `NavigationDiagnostics.IncludeRouteValues = false` toggle would cap the leak.
6. **Is `PersistedStateCache.MaxEntries = 4096` a hard cap or guidance?** F-7's silent-no-op behavior is worse than a tracked metric. Worth a design pass.

---

## 8. Out-of-scope referrals

- **NavigationHost** (mounts `UIElement`s, owns `NavigationCache`, calls `TransitionEngine.RunTransition`) — this chunk reviewed only the cache and engine in isolation. The host wiring (`Reactor/Core/Reconciler*.cs` for navigation, `Reactor/Hosting/*` for app bootstrap) belongs in **Chunks 14 and 15**. Specifically:
  - Whether `NavigationHost` ever calls `_onEvict` while still holding refs to the unmounted `UIElement` (memory-safety / GC root).
  - Whether `NavigationHost` is expected to call `NavigationStack.Detach()` at the right time (referenced in `NavigationStack.cs:94–99` and `NavigationHandle.cs:55, 100`).

- **Deep-link entry from OS protocol handler** — how Reactor wires `DeepLinkMap.Resolve(uri)` into the app's startup is host-level, not chunk-level. Belongs in **Chunk 15** (Hosting). The relevant security question — *is the resolved route auto-pushed onto the stack on cold start?* — has direct interaction with F-2.

- **JSON serialization options & AOT compatibility** — `[UnconditionalSuppressMessage]` on `GetState`/`SetState` (NavigationHandle.cs:250–251, 267–268) suppresses trimming/AOT warnings. The trim-safety review is a separate workstream. For security purposes, note that an AOT build with no reflection metadata cannot deserialize unanticipated derived types — which actually *helps* F-2 in AOT scenarios.

- **`UsePersisted` hook semantics** — `RenderContext.cs:354–379` is the only consumer of `PersistedStateCache`. Hook ergonomics belong to **Chunk 23** (hooks). This chunk only flagged the dictionary-level findings (F-6, F-7).

- **Devtools state inspection of nav** — the `NavigationDiagnostics` events are likely consumed by **Chunk 02** (`DevtoolsStateTool.cs`) for nav inspection. The "what does devtools expose to a hostile loopback caller" question is owned there; this chunk only flags that diagnostics carry full route values.

- **ETW source for navigation** — `ReactorEventSource.cs` lives in **Chunk 15**. F-5's "redirect Debug.WriteLine to ETW" mitigation should be evaluated against that EventSource's existing schema.
