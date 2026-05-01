---
name: threatmodel
description: >
  Conduct a security threat-modeling pass on a feature, file, or chunk of
  code. Use this when the user asks for a "security review", "threat model",
  "STRIDE pass", "DREAD scoring", or "find the security issues in X". The
  skill exists to keep the pass focused on real attacker-controllable
  trust-boundary crossings and to actively reject the three failure modes
  that pollute most security reports: misclassifying reliability bugs as
  DoS, treating same-principal access as exfiltration, and treating
  developer/accessibility surfaces as privilege boundaries they aren't.
---

# Threat-modeling skill — what counts and what doesn't

A security threat model identifies cases where **attacker-controllable
input crosses a trust boundary** and could cause harm to a principal that
hasn't consented. Everything else — even if it's a real bug — is
reliability work and belongs in a different report.

This skill exists because past threat-modeling passes on this codebase
ran ~40% false-positive: crashes filed as DoS, leaks filed against
surfaces that already speak with the user's authority, and "exfiltration"
through APIs whose entire job is to expose information. Read this before
filing findings.

## The four questions to ask before filing anything

Before recording a finding, answer all four:

1. **Who is the attacker?** Name them concretely (remote network, same-host
   different-user, same-user different-process, malicious source repo,
   compromised package, hostile drag source, browser tab on another origin).
   "An attacker" without a concrete principal is a red flag.

2. **What is their starting privilege?** What can they already do without
   exploiting the bug? If the answer is "execute code as the user," and
   the bug only lets them do things the user can already do, it isn't
   a security finding — it's a quality-of-implementation finding at most.

3. **What trust boundary is being crossed?** Network, process, user
   account, build-time-vs-runtime, integrity zone (MOTW), origin. If you
   can't name the boundary, there isn't one.

4. **What's the harm to a principal who didn't consent?** Code execution
   they didn't authorize, data they shouldn't see, integrity violation,
   credential theft, denial of a resource they were owed. "The app
   crashes" is harm to the developer running it, not a victim.

If any answer is hand-wavy, the finding is probably not security-shaped.
File it as reliability or correctness instead.

## The three failure modes to actively reject

### 1. Reliability bugs dressed as DoS

> "Caller can pass `int.MaxValue`, parking workers for 24 days each."
> "Recursive walker has no depth cap; deep tree blows the stack."
> "Memory grows unbounded under streaming input."

These are real bugs and should be fixed — but they aren't security
findings unless the caller is an *attacker* who lacks privileges to
trigger the same effect more directly. On a loopback dev surface where
the caller is already running as the user, "they can crash the dev
server" reduces to "the user can crash a tool they're running."

**Test:** would a normal-bug-bash triage catch this without invoking a
threat actor? If yes → reliability. File it, fix it, but don't tag it
security.

**Exceptions that ARE security:**
- The trigger comes from genuinely external input (a network peer, a
  hostile file, a URL, an LLM-generated payload, a signed update).
- The crash undermines a *security* property (audit log eviction,
  authentication-state corruption, releasing a lock).
- The resource exhausted is one the victim can't reclaim by restarting
  (token quota, rate limit, on-disk disk space they share).

### 2. Same-principal "exfiltration"

> "Tool returns absolute paths and env values in error messages."
> "Reflection enumerates all public properties of the args object."
> "ETW event ships exception messages that leak file paths."

If the caller is running as the same user as the data owner, "exposing"
data to them isn't exfiltration. The user already has the bytes on disk;
returning a path or a property value to a process *they* spawned doesn't
cross a boundary.

**Test:** *if we deleted this channel entirely, could the principal still
read the data through other means already available to them?* If yes →
not a leak.

**Exceptions that ARE security:**
- ETW providers, audit logs, telemetry pipelines, crash dumps — these
  can leave the machine via support uploads, sync-to-cloud, or be read
  by other-user contexts. ETW specifically fires in production builds
  for any same-UID consumer; that's a real leakage surface even when the
  immediate caller is benign.
- Logs that survive the dev session and may be reviewed by a different
  principal later (eng-on-call reading another dev's traces).
- Cross-tenant SaaS contexts where same-process != same-customer.
- Channels that bypass MOTW / integrity-zone tagging.

### 3. Accessibility and dev-tool surfaces treated as privilege boundaries

> "Tooltip text broadcasts to UIA clients via HelpText."
> "Dev tool exposes full visual tree including text content."
> "MCP endpoint returns log buffer contents."

UIA's contract is to give assistive-tech consumers access to **everything
the user can see**. A screen-reader user must read what a sighted user
reads. Filing UIA exposure as a leak is a category error: UIA is the
delivery mechanism, not an exfiltration channel. The relevant question is
"is this info already visible on screen?" — if yes, UIA carrying it is
the contract.

Likewise, debug-only dev tools running as the user (devtools MCP, log
capture, screenshot, tree walker) operate inside the user's authority.
The same person could attach a debugger or read process memory. The dev
tool is the same principal acting deliberately.

**Test:** is this info already visible to the same principal through a
sanctioned channel (the screen, a debugger, the file system)? If yes →
not a leak.

**Exceptions that ARE security:**
- Dev tool ships in production builds (it shouldn't, but verify).
- Dev tool's loopback endpoint is not authenticated — *that* is the
  finding (TASK-001 / TASK-018 in the live remediation list), not the
  data the tool returns.
- A11y exposes data the *user* doesn't see (placeholder text from a
  redacted password field, internal IDs that never reach the UI).

## Trust-boundary catalog (where real findings live)

Real security findings cluster around a small set of boundaries. When
threat-modeling a chunk, walk this list:

| Boundary | Examples in this codebase |
|---|---|
| **Network** | Any HTTP listener, any TCP socket, any URL fetcher. Loopback counts when other local processes have a different principal (co-tenant, sandboxed browser). |
| **Process / IPC** | Drag-drop, COM, stdio pipes between processes with different parents, named pipes, shared memory. |
| **User-content** | Files the user opens, URLs they navigate to, markdown they view, JSON deserialized from external storage, `.resw` files in a hostile repo. |
| **Build-time** | Source generators, MSBuild tasks, NuGet content, anything that runs C# during build. Compromise here is RCE-on-CI. |
| **Privilege** | Admin / non-admin transitions, service / user, sandboxed / unsandboxed WebView contexts. |
| **Integrity zone** | MOTW-tagged files, downloaded content, content from network shares. Crossing zones without re-evaluating trust is a finding. |
| **Tenant** | In multi-tenant SaaS, customer-A data reaching customer-B even at the same process privilege. |

A finding that doesn't sit on one of these boundaries — or a recognizable
analogue — should be re-checked against the four questions above before
filing.

## STRIDE-shaped checklist for the boundary you found

Once you've identified a boundary, walk STRIDE deliberately:

- **Spoofing** — can the attacker present as someone else? (token theft,
  PID-reuse, lockfile planting, origin spoofing)
- **Tampering** — can the attacker modify data in transit or at rest the
  victim trusts? (TOCTOU, path traversal writing through symlinks,
  prototype pollution analogues)
- **Repudiation** — can the attacker take an action without leaving an
  audit trail the victim can rely on?
- **Information disclosure** — *to a principal who didn't have it
  before*. Re-read failure mode 2 before filing one of these.
- **Denial of service** — by *the attacker* against *the victim*. Re-read
  failure mode 1 before filing one of these.
- **Elevation of privilege** — does the attacker gain a capability they
  didn't have? This is the most important to find and the easiest to
  miss.

## DREAD scoring discipline

If you score, score honestly:

- **Damage** — concrete blast radius. "Crashes the app" is low; "RCE on
  every CI machine" is critical.
- **Reproducibility** — does it always work, or only under racy conditions
  with a real attacker?
- **Exploitability** — does it need a custom tool, or is it `curl`?
- **Affected users** — one developer, one tenant, every install?
- **Discoverability** — already public, or buried in obscure code?

A finding that scores 1/2/2/1/1 is real-but-tiny; don't dress it as
critical to pad the report.

## The report format

When recording findings:

- **Title** — verb-led ("Authenticate the loopback MCP endpoint", "Escape
  `.resw` keys before C# emission"), not "Issue with X."
- **Concrete attacker** — name them.
- **Trust boundary** — name it.
- **Repro** — minimal external-only sequence; if you can't write one
  without invoking "imagine an attacker who already has X," reconsider.
- **DREAD or equivalent** — honest numbers.
- **Severity** — 🔴 / 🟠 / 🟡, calibrated to actual harm.
- **Category tag** — `Security` for boundary-crossing findings,
  `Reliability` for everything else. The category gates how the eventual
  PR is described in the changelog.

## Common phrases that should pause you

If you find yourself writing one of these, double-check against the four
questions before continuing:

- *"An attacker on the local machine can…"* — name them. Same user?
  Different user? Sandboxed app? Each has a different threat model.
- *"This could leak …"* — to whom? Is the recipient already authorized
  to read it through a different channel?
- *"Unbounded growth allows DoS"* — by whom against whom?
- *"Reflection exposes private state"* — exposure to a same-principal
  caller is not a security finding by itself.
- *"The dev tool returns sensitive data"* — to whom? The developer
  running the dev tool?
- *"UIA / accessibility broadcasts X"* — does the user see X on screen?
  If yes, UIA must broadcast it; that's the contract.

## When in doubt

File it as reliability and move on. A real security finding will survive
re-reading; a misfiled one wastes reviewer time and dilutes the signal of
the report. Better to report 20 well-shaped security findings than 80
mixed with reliability bugs — the high-signal report gets read, the
low-signal one gets skimmed.
