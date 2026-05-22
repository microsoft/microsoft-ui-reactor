# 025 — Devtools CLI Parity

**Status:** Draft
**Date:** 2026-04-19
**Author:** Chris Anderson
**Supersedes (partial):** [024 §7 "CLI is a launcher, not an API"](024-ai-agent-devtools.md#7-cli-surface)

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Goals and Non-Goals](#2-goals-and-non-goals)
3. [Relationship to Spec 024](#3-relationship-to-spec-024)
4. [Architecture](#4-architecture)
5. [Session Discovery](#5-session-discovery)
6. [Single-Instance Enforcement](#6-single-instance-enforcement)
7. [Session Management Subverbs](#7-session-management-subverbs)
8. [CLI Verb Inventory](#8-cli-verb-inventory)
9. [Output Format and Exit Codes](#9-output-format-and-exit-codes)
10. [Generic Escape Hatch](#10-generic-escape-hatch)
11. [Security](#11-security)
12. [Implementation Phases](#12-implementation-phases)
13. [Experiment and Sunset Criteria](#13-experiment-and-sunset-criteria)
14. [Open Questions](#14-open-questions)

---

## 1. Problem Statement

Spec 024 shipped a full MCP devtools surface (19 tools) and deliberately kept the CLI as a thin launcher, on the argument that agents should talk MCP and the CLI should talk to humans. In practice we have a second hypothesis worth testing: some agents — and some human users pairing with an agent — are better served by a plain CLI, because (a) it composes with shell pipes, `jq`, and file redirection without any MCP plumbing, (b) every invocation is self-contained so a transcript is trivially auditable, and (c) it works in contexts where the caller can't or won't configure an MCP client.

We do not yet know which surface wins. Rather than argue about it, we ship both in parallel for a bounded experiment (§13), instrument usage, and then pick one. This spec designs the CLI parity surface so the experiment is well-formed.

## 2. Goals and Non-Goals

### Goals

- **Feature parity.** Every MCP tool has a CLI equivalent. No capability is reachable only through MCP.
- **Zero-ceremony connection.** `mur devtools tree` works in a terminal where a Microsoft.UI.Reactor (Reactor) devtools session is running, with no configuration, no env vars, no flags. The discovery mechanism is the feature.
- **Auditability.** CLI invocations, their arguments, and their outputs are trivially logged by the shell the agent already uses. No extra protocol.
- **Composability.** Output is structured JSON by default; every verb is pipeable.
- **Stability under the experiment.** We can remove the CLI surface after the experiment without touching the MCP server, and vice versa. No shared state beyond the lockfile contract and the JSON-RPC wire the CLI speaks as a client.

### Non-Goals

- **Not a second server.** The CLI is a *client* of the same in-process MCP server. It does not host its own devtools runtime, does not re-implement the tree walker, does not duplicate selector parsing. Everything the CLI does is a JSON-RPC call over loopback HTTP.
- **Not a REPL or session shell.** Each `mur devtools <verb>` is one invocation, one call, one exit. If session-style interaction wins the experiment, MCP was always going to win it — a CLI pretending to be an MCP client with state is the worst of both.
- **Not multi-session orchestration.** One devtools-enabled process per project (§6). If you want to drive two apps simultaneously, use MCP with two clients.
- **Not a replacement for spec 024.** MCP remains the primary contract. This spec exists to run an experiment, not to deprecate §024.

## 3. Relationship to Spec 024

Spec 024 §7 states:

> No `mur devtools click`, no `mur devtools tree --follow`. An agent driving the app talks MCP; the CLI is a launcher.

This spec reverses that specific decision for the duration of the experiment. Every other commitment in 024 — MCP as the contract, loopback HTTP, in-process server, stable node ids, UIA as the automation bus, the tool inventory itself — is unchanged and unchallenged.

When the experiment concludes (§13), one of two things happens:

- **CLI wins, or wins in some niche:** 024 §7 is rewritten to describe the CLI as a first-class parity surface.
- **MCP wins:** this spec is archived, the CLI verbs added here are removed, and `mur devtools` reverts to the launcher-only role in 024 §7.

Either outcome is acceptable. What is not acceptable is shipping both forever — parity is cheap to maintain only because the CLI is generated from the MCP tool list (§10); once the surfaces drift, the cost compounds.

## 4. Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Agent / shell                                                   │
│   $ mur devtools tree --selector '#btn-inc'                     │
└─────────────────────────────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────────┐
│ mur devtools CLI                                                │
│   1. Resolve endpoint (§5): --endpoint > lockfile > --auto scan │
│   2. JSON-RPC POST to http://127.0.0.1:<port>/mcp               │
│   3. Print result to stdout; map JSON-RPC error to exit code    │
└─────────────────────────────────────────────────────────────────┘
                     │ HTTP
                     ▼
┌─────────────────────────────────────────────────────────────────┐
│ Reactor app process (same MCP server as spec 024)               │
└─────────────────────────────────────────────────────────────────┘
```

**The CLI holds no state.** It does not cache node ids, tree snapshots, windows, or the active component. Every call is a round-trip. This is the point: shell history is the transcript, JSON files are the cache, `jq` is the query engine.

**The CLI owns only three things:**

1. Endpoint discovery (§5).
2. Argument marshalling — argv → JSON arguments object for the target tool.
3. Output shaping — `result` printed as JSON; `error` mapped to exit code and stderr.

**Code layout.**

| File | Role |
|---|---|
| `src/Reactor/Hosting/Devtools/LockfileRegistry.cs` | Writes/removes the session lockfile. Owned by the server so the contract lives with the producer. |
| `src/Reactor.Cli/Devtools/EndpointDiscovery.cs` | Resolves `--endpoint` / lockfile / `--auto` → URL. Single entry point used by every verb. |
| `src/Reactor.Cli/Devtools/McpCliClient.cs` | Thin JSON-RPC client. One `Invoke(tool, args)` method; everything else layers on top. |
| `src/Reactor.Cli/Devtools/DevtoolsVerbs.cs` | One method per verb. Parses argv, builds arguments object, delegates to `McpCliClient`, prints. |
| `src/Reactor.Cli/Devtools/SessionCommands.cs` | `mur devtools session list` and `mur devtools session clean` (§7). |

Nothing in `Reactor.Cli` takes a runtime dependency on `Microsoft.UI.Xaml` beyond what the supervisor already needs. The CLI's only knowledge of "what a tool does" is the MCP tool name and its schema — which the CLI learns at runtime by calling `tools/list`, not by compile-time coupling.

## 5. Session Discovery

**Resolution order.** The CLI resolves the MCP endpoint through exactly these sources, in this precedence:

1. **`--endpoint <url>`** — explicit override. Used as-is, no probe. If it's unreachable, the CLI fails fast with the bare transport error.
2. **Lockfile auto-discovery** — default path. The CLI scans `%TEMP%/reactor-devtools/` for `*.json` lockfiles, pid-probes each, and selects the unique live session. If zero live sessions: error with "no running Reactor devtools session; run `mur devtools <project>` to start one". If multiple live sessions: error listing them with their project paths and endpoints, asking the user to disambiguate with `--endpoint`.
3. **`--auto`** — opts into a loopback port scan over `127.0.0.1:1024-65535` (or a tighter range TBD), issuing `GET /mcp` and filtering responses by `schema: "reactor-devtools-mcp/1"`. Only used when explicitly requested. Noisy, slow, and off by default because most users don't need it; present so the experiment can learn whether lockfile discovery ever fails in practice.

There is **no environment variable**. Explicit decision: a single-terminal flow should need zero config (lockfile handles it) and a cross-terminal flow should be explicit (`--endpoint`). Env vars are the worst of both worlds — invisible state that inherits unpredictably across subshells, CI, and VS Code integrated terminals.

### Lockfile contract

**Path.** `%TEMP%/reactor-devtools/<hash>.json`, where `<hash>` is a stable, path-derived identifier for the project. Specifically: SHA-256 of the canonicalized full path to the `.csproj`, truncated to 16 hex chars. Canonicalization lowercases the drive letter and normalizes path separators so `C:\foo\bar.csproj` and `c:/foo/bar.csproj` collide (deliberately — they address the same project).

**Fields.**

```jsonc
{
  "schema": "reactor-devtools-lockfile/1",
  "endpoint": "http://127.0.0.1:54931/mcp",
  "transport": "http",                      // "http" | "stdio"
  "port": 54931,
  "pid": 18432,
  "buildTag": "2026-04-19T14:22:09Z",
  "project": "C:\\Users\\me\\MyApp\\MyApp.csproj",
  "startedAt": "2026-04-19T14:22:11Z"
}
```

`transport: "stdio"` sessions still write a lockfile (pid + project + buildTag are useful) but `endpoint` is the string `"stdio"` and the CLI refuses to use them — the CLI is HTTP-only (§10 open question). A stdio session shows up in `mur devtools session list` with a `transport: stdio` annotation.

**Lifecycle.**

- Written from `DevtoolsMcpServer.AnnounceReady()` — after the HTTP listener is bound and the first render has happened, so any reader that sees the file can in fact connect.
- Removed from `DevtoolsMcpServer.Dispose()` on clean shutdown. Also removed at the end of the reload sentinel path before the new process writes a fresh one.
- On crash or force-kill, the file is left behind. Readers pid-probe and skip dead entries (§7). The file gets cleaned up lazily by the next successful launch of the same project, or explicitly by `mur devtools session clean`.

**Liveness probe.** A reader considers a lockfile live iff the `pid` corresponds to a running process **and** a `GET <endpoint>` returns `schema: "reactor-devtools-mcp/1"`. Pid-only is not enough — Windows reuses pids, and an unrelated process could collide. The HTTP probe is cheap (loopback, one round-trip) and confirms we're talking to the right server.

## 6. Single-Instance Enforcement

**Rule:** at most one devtools-enabled Reactor process per project path. Two instances of the same exe without devtools are fine; two with devtools are not.

**Mechanism.** On startup, before the MCP listener binds, the server:

1. Computes the project's lockfile path.
2. If a lockfile exists and is live (§5): refuse to start devtools. Emit a single stderr line: `[devtools] another session for this project is active at <endpoint> (pid N); stop it first`. Exit with code `3` (reserved for this condition). The app itself may continue to run if the user wants devtools-less — but the way `--devtools run` is invoked today means the whole process exits; that's fine.
3. If a lockfile exists but is not live: treat as stale, delete, continue.
4. Bind the listener, write a fresh lockfile atomically (write to `<hash>.json.tmp`, fsync, rename).

**Why this limitation?** Two devtools sessions for the same project would fight over the node registry's conceptual model (tree ids are project-scoped in the agent's mental model even though they're process-scoped in implementation), and more importantly, would make the lockfile → endpoint mapping non-unique — destroying the "zero-ceremony connection" goal. If we later find a real use case for parallel sessions, the design space is "lockfile holds an array" plus a CLI disambiguation flag; we will not pre-build for it.

**What about different build configurations?** Debug and Release builds of the same project hash to the same lockfile path. This is deliberate: you should not be running two devtools sessions for the same app. If a user needs a Release-mode devtools session specifically, they stop the Debug one first.

**Stdio transport.** A stdio session also takes the project's lockfile slot (it has a pid and a project). A second launch, stdio or HTTP, is rejected the same way.

## 7. Session Management Subverbs

Nested under `session` to keep the top-level `mur devtools` verb list scoped to per-session operations (`tree`, `click`, …). Rationale: `session list` and `components list` both existed, and nesting the session ones is less confusing than either renaming `components` to `mur devtools components-list` or letting `list` mean two different things depending on whether a positional is supplied.

```
mur devtools session list         # show active sessions
mur devtools session clean        # remove stale lockfiles
```

### `mur devtools session list`

Walks `%TEMP%/reactor-devtools/`, pid-probes each lockfile, emits one JSON line per live session on stdout (JSONL for pipe-friendliness; `--pretty` prints a human table). Stale lockfiles are silently skipped. Example output:

```jsonc
{"project":"C:\\Users\\me\\MyApp\\MyApp.csproj","endpoint":"http://127.0.0.1:54931/mcp","pid":18432,"buildTag":"2026-04-19T14:22:09Z","transport":"http","startedAt":"2026-04-19T14:22:11Z"}
```

Exit code 0 if at least one live session; `4` if none. (`4` is distinct from `3` "another session exists" so scripts can branch.)

### `mur devtools session clean`

Walks `%TEMP%/reactor-devtools/`, pid-probes, removes dead ones, leaves live ones alone. Prints a one-line summary to stderr (`removed N stale entries`) and exits 0. `--dry-run` lists what it would remove without touching disk. Never kills processes, never touches live lockfiles; killing a live session is the user's job (`Ctrl+C` in the terminal that launched it, or `taskkill`).

**Auto-cleanup on read.** Every reader (including `session list`, every verb's endpoint discovery, and the single-instance check) already skips dead entries and may GC the stale file opportunistically on the way through. `session clean` is the belt-and-suspenders explicit tool for the moments when a user sees `%TEMP%/reactor-devtools/` and wants it tidied. We expect most users never to run it.

## 8. CLI Verb Inventory

One verb per MCP tool from spec 024 §8, plus `session list`, `session clean`, and `call` (§10). Verb names match MCP tool names where possible; where the tool name is camelCase, the verb name is the same lowercase word or split on the natural boundary.

| MCP tool | CLI verb | Notes |
|---|---|---|
| `version` | `mur devtools version` | Info. |
| `windows` | `mur devtools windows` | Info. |
| `components` | `mur devtools components` | Renamed from today's `mur devtools list` to free `list` for `session list`. |
| `switchComponent` | `mur devtools switch <component>` | |
| `tree` | `mur devtools tree [--selector S] [--window W] [--view summary\|full] [--include-reactor-source]` | Remains available in `--launch` one-shot form (spec 024 §10). |
| `screenshot` | `mur devtools screenshot [--selector S] [--window W] [--out path] [--wait-idle] [--include-chrome]` | Keeps the existing `--launch`-equivalent one-shot path. |
| `state` | `mur devtools state [--selector S]` | |
| `click` | `mur devtools click <selector>` | |
| `type` | `mur devtools type <selector> <text> [--clear]` | |
| `focus` | `mur devtools focus <selector>` | |
| `invoke` | `mur devtools invoke <selector>` | |
| `toggle` | `mur devtools toggle <selector>` | |
| `select` | `mur devtools select <selector> <item-selector>` | |
| `scroll` | `mur devtools scroll <selector> [--by DX,DY \| --to <selector>]` | |
| `expand` | `mur devtools expand <selector>` | |
| `collapse` | `mur devtools collapse <selector>` | |
| `waitFor` | `mur devtools wait <selector> [--text X \| --text-matches RE \| --visible \| --count N] [--timeout MS]` | |
| `fire` | `mur devtools fire <Component>.<event> [--args JSON]` | |
| `reload` | `mur devtools reload [--component N]` | CLI exits 0 once the server acknowledges; reconnecting to the new build is the user's next invocation. |

Verbs that already exist as launcher subverbs (`run`, `list`, `screenshot`, `tree`) keep their current behavior under `--launch` (one-shot app spawn). Default behavior changes to "attach to the running session via lockfile discovery"; `--launch` opts back into the old spawn-per-invocation mode.

## 9. Output Format and Exit Codes

**stdout** is always the result payload. For tools that return JSON (most), stdout is the raw JSON, compact by default, pretty with `--pretty`. For `screenshot`, stdout is the PNG bytes when `--out -` is passed; otherwise `--out <path>` writes the file and stdout emits the result metadata.

**stderr** is for human-readable diagnostics: discovery errors, stale-lockfile notices, the rendered form of JSON-RPC error messages. Never mixed with structured output.

**Exit codes.**

| Code | Meaning |
|---|---|
| 0 | Success. |
| 1 | Usage error (unknown flag, missing argument, bad selector grammar at the CLI layer). |
| 2 | Transport error (endpoint unreachable, timeout, malformed response). |
| 3 | Another devtools session is already active for this project (single-instance, §6). |
| 4 | No live devtools session found during discovery. |
| 5 | Tool returned a JSON-RPC error. The error body is printed to stderr; stdout emits the full `{error: {...}}` object so scripts can still parse it. |

Codes 2–5 are distinct so `if/elif` shell flows can branch without parsing JSON.

## 10. Generic Escape Hatch

`mur devtools call <tool> [--args JSON]` is a generic passthrough: it resolves the endpoint, POSTs `{jsonrpc:"2.0", method:"tools/call", params:{name:"<tool>", arguments:<args>}}`, and prints the result. No argv parsing for the specific tool, no schema validation at the CLI.

**Why.** It guarantees 100% parity from day one, even before every named verb is implemented. It means a new MCP tool is reachable from the CLI in the same release it ships, without a CLI code change. It gives us an escape hatch during the experiment — if a named verb's argv shape proves wrong, users can fall back to `call` while we fix it. And it's the natural path for tools whose arguments are too structured for convenient argv (e.g. `fire` with complex `args`, `waitFor` with compound predicates later).

`mur devtools call tools/list` also works — any method the dispatcher understands, not just those under `tools/call`. This is the lowest-friction way to introspect what's there.

## 11. Security

Inherits spec 024 §14 entirely. The CLI does not widen the attack surface:

- The MCP server still binds loopback only.
- The lockfile lives under `%TEMP%` with default user permissions. Any local process can read it. This is not a regression — any local process can already probe `127.0.0.1` and hit the MCP server; the lockfile just saves it a port scan.
- The lockfile holds no secrets. The endpoint, pid, and project path are the most sensitive fields, all already discoverable by a local observer.
- `fire` authority via the CLI is identical to `fire` authority via MCP. Devtools mode is the gate; how you reach it doesn't matter.

If spec 024's "localhost-only, devtools opt-in" model is ever weakened (cross-machine MCP, always-on in Release), this spec's security implications need a revisit in the same breath.

## 12. Implementation Phases

### Phase 1 — Foundation

- `LockfileRegistry` written and wired into `DevtoolsMcpServer` (`AnnounceReady` / `Dispose` / reload path).
- `EndpointDiscovery` in `Reactor.Cli` with `--endpoint` + lockfile resolution. `--auto` port scan deferred to Phase 3.
- Single-instance check on server startup (§6).
- `mur devtools session list` and `mur devtools session clean`.
- Tests: lockfile round-trip, pid-liveness on fake dead pid, single-instance rejection, session list under multiple fake lockfiles.

**Exit criteria:** a user can start `mur devtools <project>`, see the lockfile appear, list it with `session list`, kill the process, and confirm `session list` reports empty while the stale file is cleanable.

### Phase 2 — Generic client + passthrough

- `McpCliClient` — JSON-RPC client, HTTP POST, error → exit-code mapping.
- `mur devtools call <tool> [--args JSON]` — the generic passthrough (§10).
- Tests: integration test spawning `--devtools run` and calling every MCP tool via `call`, asserting the result schema matches `tools/list`.

**Exit criteria:** 100% parity is reachable, if awkward. The experiment can start measuring usage.

### Phase 3 — Named verbs

- One named verb per MCP tool (§8). Each is a thin wrapper that argv-parses and delegates to `McpCliClient`.
- `--auto` port scan in `EndpointDiscovery`.
- `components` renamed (old `list` removed or redirected).
- Docs: the devtools sub-skill gets a CLI section paralleling the MCP section, same examples.

**Exit criteria:** every MCP tool has a named CLI verb. Agent transcripts recorded under the experiment should show named-verb usage, `call`-passthrough usage, and raw-MCP usage in comparable shapes.

### Phase 4 — Experiment end

- Decide winner per §13.
- Remove the loser; update spec 024 §7; archive this spec or promote its content into 024.

## 13. Experiment and Sunset Criteria

**Duration.** Two weeks after Phase 3 lands (target: one sprint, one week of buffer).

**Signals we care about.**

- **Task completion rate** on the seeded agent evaluation suite from spec 024 §15, measured separately under "MCP-only" and "CLI-only" profiles. A profile that can't complete a task category is immediate evidence against it.
- **Calls per task.** If the CLI's round-trip-per-invocation overhead pushes median calls-per-task noticeably up, that's a mark against it.
- **Transcript legibility.** Subjective, reviewed by the humans doing agent pairing. Can a developer skim a shell transcript vs. an MCP trace and understand what the agent did?
- **Unprompted agent preference.** When both surfaces are available, which does an agent reach for on its own? (Requires neither surface being presented as "preferred" in the skill docs during the experiment.)

**Decision framing.** At the end of the window we pick exactly one primary surface. The other is removed in the next release. The one non-negotiable: we do not ship both long-term. Surface-area cost compounds; the experiment exists to pay it down.

**Credible outcomes.**

- **CLI wins outright.** Unlikely. Would require round-trip overhead to be invisible and transcript legibility to dominate.
- **MCP wins outright.** Most likely outcome; promotes the CLI pivot back to launcher-only.
- **Mixed, with CLI kept only as a debugging / scripting adjunct.** The honest middle. We keep `session list/clean`, `call`, and `screenshot` in `--launch` mode; drop every other named verb; spec 024 §7 is rewritten to reflect the narrower role.

## 14. Open Questions

### Still open

1. **Should the CLI speak stdio MCP, not just HTTP?** An agent running under a framework that already spawns a stdio MCP child could in theory skip the HTTP hop. But the agent in that world has a direct MCP client already — the CLI buying it nothing. Leaning toward HTTP-only for v1; revisit if the experiment surfaces a concrete case.
2. **Port scan range.** `--auto` over `1024-65535` is slow (~64k attempts, parallelizable). A tighter default (say, `1024-10000` with a `--auto-full`) covers most dev scenarios. Decide after seeing what ports Windows actually hands out for loopback `HttpListener`s on our test machines.
3. **Lockfile placement on non-Windows hosts.** Not immediately relevant (Reactor is WinUI-only), but a future macOS / Linux headless build of the devtools server would need `$XDG_RUNTIME_DIR` or `/tmp` with a per-user subdirectory. Design placeholder only; no code until we need it.
4. **Lockfile content versioning.** `schema: "reactor-devtools-lockfile/1"` is pinned; what happens on v2 is TBD. Probably: readers that don't recognize the schema skip the lockfile and log a notice, same shape as stale-pid handling.
5. **Concurrent reader races on `session clean`.** A reader could be mid-probe when `session clean` deletes the file. The delete is harmless (reader still has the in-memory copy for this invocation), but we should audit the sequence once we have the code.

### Resolved

1. **Env var for endpoint discovery?**
   **Resolved: no.** Explicit decision (not an omission). Invisible state that inherits unpredictably across subshells is worse than either zero-config (lockfile) or explicit (`--endpoint`).
2. **Auto port scan as silent fallback?**
   **Resolved: no, flag-gated.** Port scan is only reached via `--auto`. Silent fallback would mask lockfile bugs (the feature under test).
3. **One process per project, or per exe?**
   **Resolved: per project (csproj path).** Matches the mental model — `dotnet run` against a project is the unit the user thinks about, not the output `.exe` path which differs between configurations.
4. **Session subverb nesting.**
   **Resolved: nested under `session`.** `mur devtools session list/clean` keeps the top-level verb list scoped to per-session operations and avoids the `components list` vs `session list` collision.
5. **Generic `call` escape hatch alongside named verbs?**
   **Resolved: both.** `call` guarantees parity even for future MCP tools; named verbs are the ergonomic surface the experiment actually measures.
