---
title: "0.1.0-preview.16"
type: changelog
date: 2026-09-22
changelog:
  version: "0.1.0-preview.16"
description: "2 added, 49 changed, 1 removed and 7 fixed."
---

## Added

- **`REACTOR_DOCS_CAPTURE_ORIGIN` for regenerating screenshots when 150% is not
  your primary display.** Capture is `PrintWindow` in physical pixels, so a PNG
  takes the DPI of the monitor its window opens on — the primary. Setting the
  variable to a virtual-desktop `X,Y` origin forwards `--x` / `--y` to the
  devtools preview host, which opens the capture window there instead. Needed on
  remote sessions, where the primary display cannot be changed. See
  [`docs/contributing/doc-pipeline.md`](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/contributing/doc-pipeline.md).

- **`UnpackagedAppDataStore` — window placement now survives renaming your executable
  (spec 063 §4).** The unpackaged persistence store keys saved window placement on
  the entry **process name**, so shipping the same app under a renamed `.exe` silently stranded
  every user's saved layout — it was still on disk, under the old name, and never read again. Two
  unrelated apps sharing an exe name collided the same way.

  The new store keys on an explicit publisher/product pair instead, via the Windows App SDK's
  first-class unpackaged app-data root
  (`Microsoft.Windows.Storage.ApplicationData.GetForUnpackaged(publisher, product)`), persisting to
  `<LocalPath>/reactor-windows.json`. Opt in by assigning it to
  `ReactorApp.WindowPersistenceStore` before the first `OpenWindow`:

  ```csharp
  ReactorApp.WindowPersistenceStore = new UnpackagedAppDataStore("Contoso", "TimeTracker");
  ```

  It is opt-in rather than the new default because the two stores key their data differently, so
  switching automatically would strand exactly the layouts it is meant to protect. `JsonFileStore`
  remains the auto-detected default (spec 063 §6, D2).

  It uses the SDK's `LocalPath` surface and deliberately **not** `LocalSettings`. On Windows App SDK
  2.2.0, `GetForUnpackaged().LocalSettings` opens `HKCU\SOFTWARE\<publisher>\<product>` — a
  *roaming* hive — instead of the machine-local
  `HKCU\SOFTWARE\Classes\Local Settings\Software\…` it is contracted to use
  ([WindowsAppSDK#6559](https://github.com/microsoft/WindowsAppSDK/issues/6559)).
  Window placement is monitor-topology and DPI dependent, so roaming it would restore windows onto
  monitors that do not exist on the current machine. `LocalPath` resolves under `%LOCALAPPDATA%`,
  which does not roam, and is verified correct on 2.2.0 (spec 063 §3.1). Note that the
  `LocalSettings` behaviour is a property of the *installed* 2.x runtime, which services in place,
  rather than of the SDK version an app pins — so it can vary machine to machine for the same build
  (spec 063 §3.2).

## Changed

- **Windows App SDK bumped 2.1.3 → 2.2.0 (spec 063 §3).** `WindowsAppSDKWinUIVersion` moves
  2.1.0 → **2.2.1** — the WinUI sub-package version is neither equal to nor a fixed offset from the
  metapackage version, and must be read from the metapackage's own nuspec rather than inferred.
  This remains an in-place servicing bump within the same side-by-side runtime family
  (`Microsoft.WindowsAppRuntime.2`), so consumers do not need a new runtime generation.
  `Microsoft.UI.Reactor` is framework-dependent and flows only the lean
  `Microsoft.WindowsAppSDK.WinUI` sub-package, so the transitive floor consumers actually
  inherit is **`Microsoft.WindowsAppSDK.WinUI >= 2.2.1`**; projects that reference the full
  metapackage themselves (self-contained or MSIX) move to `Microsoft.WindowsAppSDK 2.2.0`.
- E2E tests now run as one named winapp workflow: `WinAppUi` stamps `WINAPP_UI_WORKFLOW_ID` onto
  every `winapp ui` child and `AppTestBase` yields the UI turn after any test that used winapp.
  This keeps a concurrent agent from interleaving between a click and the assertion that reads its
  result, which anonymous one-shot commands allow. An ambient `WINAPP_UI_WORKFLOW_ID` is inherited
  so a harness can group the run with its own calls. **Requires a winapp carrying winappCli#767**,
  which introduced interactive-desktop coordination wholesale (the lock, scheduler, participant
  registry and `ui yield`); it merged 2026-09-09 and the newest published release, v0.6.1
  (2026-08-19), predates it. Against an earlier build the stamp is an unread environment variable
  — inert and harmless — and this wiring becomes effective the day a release carries [#767](https://github.com/microsoft/microsoft-ui-reactor/issues/767), with no
  further change here. See `TESTING.md` §4.
- The packaged selftest tier registers its host under an identity derived from the layout
  directory (`<name>.w<hash>`, with a matching execution alias) instead of the literal name in
  `Package.appxmanifest`, so concurrent checkouts can run it without evicting each other's
  registration or contending for one alias stub. Cleanup is scoped to the derived name, to
  packages installed from the same directory, and to derived packages whose directory is provably
  gone and whose layout lock no run still holds — including those registered by a superseded
  revision of the derivation, so bumping it does not strand the identities the previous one
  created. A path that merely cannot be read is not treated as gone.
  Sweeps by hand or in CI must now match `Microsoft.UI.Reactor.PackagedTests.Host*`. See
  `TESTING.md` §3 and [microsoft/winappCli#763](https://github.com/microsoft/winappCli/issues/763).
- The E2E suite's start-up sweep of orphaned test hosts is now scoped to the host executable in
  the current checkout. It previously killed every process with a matching name anywhere on the
  machine, so starting the suite in one worktree terminated another worktree's live host. A
  candidate whose image path cannot be read is left alone rather than killed. Two runs of the
  *same* checkout share an executable path, so the sweep is additionally gated on claiming that
  build output: every live run holds its own lease, and a run that finds another run's lease still
  held skips the sweep instead of killing the live sibling. A single-owner claim was not enough —
  a refused run still launched a host, and once the owner exited a third run could acquire the
  freed claim and sweep that host away. Leases are tracked per host executable, since the suite
  sweeps both the WinUI and WinForms hosts.
- The packaged tier serializes runs that share one checkout behind a per-user lock on the derived
  identity, since registration and the alias stub are per-user rather than per-session. Runs in
  different checkouts still proceed in parallel. The lock is taken over every supported identity
  algorithm version rather than just the current one, so a run on a revision that derives a
  different name still blocks — locking only the current version would let two runs holding
  different names both register over one layout.
- Reclaiming an abandoned packaged registration now holds the layout lock across the removal
  instead of sampling it first. Sampling only reports whether a run held the lock at that
  instant, which leaves a window for a run to start and register between the check and the
  `RemovePackage` that then unregisters it.
- The packaged layout lock is now taken under every spelling a layout could canonicalize to,
  not just the one its path reduces to today. Canonicalization asks the filesystem what a path
  really points at, so an aliased directory — a `subst`'d drive, a junction, an extended-length
  `\\?\` path — reduces to its physical location while it exists and to its textual spelling
  once it is gone. A run therefore locked one name while a later run asking whether that
  registration could be reclaimed derived another, took a different file, found it free, and was
  granted a lease over a live run. That is the exact eviction the lease documents as impossible
  for a run whose directory was deleted out from under it. Locking the union is fail-closed, and
  for an ordinary path the spellings collapse to one, so the common case takes no extra files.
- The E2E continuity tests now report whether the resolved `winapp` carries the `ui yield` verb
  instead of skipping silently, and honour `REACTOR_E2E_REQUIRE_UI_YIELD` to turn its absence
  into a failure. Cooperative UI turns landed in winappCli#767 (merged 2026-09-09) and no
  published `winapp` contains it yet, so the verb cannot be required by default — but
  Microsoft.Testing.Platform reports a skip as *passed with zero skipped*, which made a run that
  measured nothing indistinguishable from one that measured the continuity. CI records the
  capability in its step summary for the same reason.
- The E2E suite's start-up sweep admits one run at a time per host executable, holding a gate
  across registering its own lease, querying for live siblings, and snapshotting processes.
  Those three steps were separately correct but interleaved: two runs starting together could
  each register after the other's query and both conclude they were alone. A run that cannot
  record its lease at all now aborts rather than continuing unregistered, since an unregistered
  run is invisible to the next one to start and its host would be killed as an orphan.
- The packaged tier now aborts rather than acting when a package holding this layout's derived
  name records some other install path, or none. Name equality is not an ownership proof: the
  suffix is a 40-bit hash, and a registration left pointing elsewhere reaches the same state with
  no collision at all. Removing it could evict another checkout's live host through our own name;
  the abort names the package and the manual `Remove-AppxPackage` recovery.
- Packaged-tier layout locks are deleted when released, instead of accumulating one file per
  layout directory the machine has ever locked. The delete is race-safe because the lock handle
  does not share `FileShare.Delete`, so a file another run has already reacquired refuses to be
  unlinked.
- The E2E sweep's live-sibling check no longer pre-checks the claim directory with
  `Directory.Exists`, which answered false for an unlistable directory exactly as for a missing
  one and so read as "no siblings" — the verdict that licenses killing hosts. Listing failures
  now fail closed.
- The packaged tier's `.resw` tripwire scans the packaged host's transitive `ProjectReference`
  graph rather than two hardcoded directories, so a string resource added to a referenced runtime
  project is caught instead of silently entering the packaged PRI.
- The E2E suite's startup gates and run leases are removed once the runs that created them are
  gone, instead of accumulating one file per build output forever. Both live in a single per-user
  directory shared by every checkout, and a lease was only ever pruned by a later run of that same
  executable — so a worktree deleted after its last run left its files behind permanently.
  Staleness is proven by an exclusive open rather than by a name, an age or a recorded pid, which
  is what makes it safe to reclaim other checkouts' leftovers: a live run keeps its files whoever
  started it, and a file reacquired between the open and the unlink refuses to be removed rather
  than being pulled out from under its owner.
- The packaged tier's recovery message for a conflicting registration now names the exact package
  to remove (`Remove-AppxPackage -Package <full name>`) instead of a `Get-AppxPackage -Name`
  pipeline, which is not publisher-unique and could unregister an unrelated package that happens
  to share the name.
- The packaged tier's manifest drift guard now accepts the derived name produced by *any*
  supported identity-algorithm version, not only the current one. The guard runs before the
  rewrite that migrates a stale name, so after a version bump it would previously abort on
  exactly the manifest the rewrite was about to heal, leaving the tier unrunnable until a
  rebuild. Derivations are still compared exactly rather than by suffix shape, so a name
  belonging to a different layout remains drift.
- The packaged tier's layout-lock wait is sized for two host process budgets instead of one. A
  batch whose filter excludes the identity guard runs the host a second time to fetch it, so an
  owner can legitimately occupy two consecutive budgets, and a contender could time out and
  report a collision against a perfectly healthy run.
- The packaged tier's layout locks now share one wait deadline across the whole set rather than
  restarting the timeout for each supported algorithm version, so a contender's worst case stays
  the single bounded wait that is documented instead of growing with every version added.
- A packaged-tier lock file that opens but cannot be stamped with its owner record now fails
  immediately as a storage error. It was previously indistinguishable from contention, so the
  run retried until the full layout timeout elapsed and then blamed a competing owner that did
  not exist.
- The packaged tier's name-only registration lookup — the fallback used when broad package
  enumeration is unavailable — now probes every supported algorithm version's derived name, not
  just the current one, so this layout's own registration from before a version bump is still
  found and migrated rather than being registered on top of.
- A packaged-tier run that fails partway through taking its layout locks now releases the ones it
  already holds instead of only doing so when it is refused. Making stamp failures surface as
  errors introduced a path out of acquisition that skipped the release, so a storage fault on one
  version's lock would hold another version's open until the process exited — wedging every later
  run over that layout behind the run that correctly reported a failure.
- A packaged-tier run whose lock directory cannot be prepared — an unavailable or read-only
  `%LOCALAPPDATA%` — now reports the storage fault itself instead of a collision. Setup failure
  and contention were both signalled the same way, so the run named an owner that never existed,
  claimed a wait that never happened, and discarded the error that explained it. The reclamation
  probe still reads an unusable lock directory as "leave the registration alone".
- The packaged `ms-appx:`/MRT resolution fixture now reports success only once the resource value
  has actually been read. It previously decided from the lookup alone, so a failure while reading
  the value was recorded in the detail text while the fixture still passed — a PRI compatibility
  check that could go green without resolving anything.
- The packaged-tier reclamation probe now treats a stamp failure the same way it treats a setup
  failure: as "this registration is in use, leave it alone". Only setup failures were caught, so a
  storage fault while stamping a lock taken over someone else's registration escaped an advisory
  liveness probe and aborted the whole run — the opposite of the fail-closed behaviour the probe
  documents.
- The packaged identity guard now checks the publisher as well as the name. A package family name
  is `<name>_<publisherHash>`, so matching the name prefix alone accepted a same-named package
  from any publisher — precisely the case the guard exists to tell apart.
- Per-checkout identity derivation now resolves a layout directory through the filesystem rather
  than by string handling alone, so mapped or `subst`'d drive letters and the extended-length
  `\\?\` spelling all reach one identity. The override accepts any rooted path that exists and
  preserves the spelling it was handed, so two runs pointed at one physical layout through
  different spellings previously took different locks and then rewrote and registered the same
  generated manifest concurrently.
- The orphaned-host sweep tests now record every lease they stage and delete it at class cleanup.
  A staged lease is not a claim the process took, so the release seam could not see it, and
  disposing deliberately leaves the file behind — every run added another permanent file to the
  real per-user claim directory that later runs then paid to probe.
- The orphaned-host sweep now resolves an executable path through the filesystem rather than by
  string handling alone, so a junction, a `subst`'d drive or the extended-length `\\?\` spelling
  all reach one path. Both the key that names a run's lease and gate and the check that decides
  whether a live process is a sibling derive from it, so one build output reachable by two
  spellings previously took two lease files, left each run seeing no sibling, and admitted both
  to sweep — each then killing the other's running host.
- The orphaned-host sweep now only considers processes in this run's own Windows session, and
  refuses to run at all when this checkout's own executable cannot be resolved. Process
  enumeration is machine-wide while the liveness leases are per-user files, so another user
  running the same build output had no lease this run could see and so presented exactly as an
  orphan: right image, no live sibling. Path scoping cannot separate them, because it is the
  same path. Separately, the resolution fallback that keeps the sibling check safe is not safe
  for the key naming the gate and lease: falling back to the caller's spelling makes the key a
  function of how the path was written, so two runs of one build output could again derive
  different keys, each see no sibling, and each kill the other's host.
- The E2E suite's session reference count is now taken only once bootstrap has succeeded.
  MSTest does not run a class's cleanup when its initialize threw, so a count taken before the
  work leaked on every failed bootstrap: the count never returned to zero, and the host process
  and its liveness lease outlived the run — leaving the next run to defer to a sibling that was
  no longer there.
- The packaged tier's `.resw` tripwire judges build output by the path below each scanned
  project root rather than by the absolute path. A checkout beneath any directory named `bin` or
  `obj` — `C:\bin\reactor`, a build agent's `obj` workspace — put that component in every path,
  so every source `.resw` was discarded and the scan reported zero. For a tripwire whose only
  possible finding is "none found", that is indistinguishable from a clean tree. The existing
  positive control could not catch it, because it stages its tree under `%TEMP%`.
- The documented by-hand packaged cleanup in `TESTING.md` now filters on the package publisher,
  matching the runtime sweep in `AppxLooseLayoutDeployment` and the `Unregister packaged host`
  step in CI. A name wildcard alone can match a current-user package from another publisher that
  happens to share the prefix, and the documented command would have unregistered it.
- The packaged tier's lock-path set now snapshots the layout's canonical spellings once and
  crosses that snapshot with the algorithm versions, instead of recomputing them inside the
  cross. Canonicalisation consults the filesystem, so passing it as the cross's collection
  selector re-asked per version; a layout deleted midway made the later versions contribute only
  the textual spelling, producing a ragged cross rather than the complete one the lock set is
  documented to be. Exclusion itself was never at risk — one candidate spelling is derived by
  string handling alone and so appears whatever the directory's state, meaning any two sets for
  one layout still overlap — but the set otherwise depended on when during its own construction
  the filesystem happened to be asked.
- The orphaned-host sweep now also requires a candidate's launching process to be gone before it
  will kill it. Admission proves only that no other *participant* in the lease protocol is live,
  which is strictly weaker than "nothing is live": a run started from a revision predating the
  lease writes no lease, so it is invisible to admission, and the sweep would then find its
  perfectly healthy host sharing our image path and matching every other rule. No handshake can
  close that, because the other side is already running code that does not implement it. A host
  is started as a direct child of the run that owns it, so launcher liveness is a fact about the
  process table rather than about any file this protocol writes, and it is therefore decidable
  for a non-participant. Both conditions are kept: the launcher check alone would admit killing
  a host whose own run exited but which a sibling is still driving.
- Ownership comparison of two install paths is now case-exact whenever both resolve, which is
  deliberately stricter than identity derivation. Derivation lowercases, so under a
  case-sensitive parent directory — which `fsutil file setCaseSensitiveInfo` and WSL both
  enable — `Repo` and `repo` derive one package name. That collision alone is survivable, but
  the ownership check canonicalised the same way, so a registration belonging to one checkout
  was judged to be the other's own and unregistered while it was still running. The final-path
  query reports a directory's on-disk case whatever case it is asked with, so comparing the
  resolved spellings exactly still treats two spellings of one directory as equal while
  correctly separating two directories, turning a silent eviction into the conflict refusal the
  deployment already reports. Two paths are only judged the same directory when the filesystem
  confirms both spellings; see the fail-closed note below for why an unresolvable path proves
  nothing here.
- The E2E orphan sweep applies the same rule to executables, in both the kill predicate and the
  claim key. Two checkouts differing only in case hold two different host binaries, but the
  predicate folded case and so judged one checkout's live host to be the other's own image, and
  the gate and lease key folded case and so put two unrelated runs in one claim namespace. Both
  now preserve the resolved spelling, so the two stop sharing a startup gate and stop reading
  each other's lease as a sibling. Unresolved paths are still folded, where the spelling is the
  caller's own, and an unresolved candidate is no longer matched against a resolved image at
  all — declining costs a skipped sweep, which is the harmless half of that predicate's error
  space.
- The E2E teardown no longer spawns a `winapp.exe` per UI test to invoke a verb the installed CLI
  may not have. Cooperative UI turns landed whole in winappCli#767, so a build without `ui yield`
  has no turn to release and the handoff could only exit on an unknown verb. The verb is now
  probed once per test process with `winapp ui yield --help` and cached, and the yield is skipped
  when it is absent. `--help` is the probe rather than a trial yield because a yield exits
  non-zero both for "no such verb" and for "verb present, no workflow id reached me", and caching
  the second as the first would disable continuity for the rest of the run precisely when the
  wiring it exists to exercise had broken. The strict switch is unchanged: with
  `REACTOR_E2E_REQUIRE_UI_YIELD` set, a missing verb is still a failure rather than a skip.
- The E2E orphan sweep can no longer kill a process that merely inherited a doomed one's process
  id. Every candidate was classified from a snapshot and killed afterwards by id, but the objects
  `Process.GetProcessesByName` returns hold no handle, so the kill re-opened the id: a host that
  exited in between, and whose id Windows then reassigned, meant terminating a stranger. The
  sweep now pins a handle to each process before reading anything about it, which keeps the id
  reserved for as long as the decision is in flight, and a candidate that cannot be pinned is
  excluded rather than killed on an id that no longer proves anything — the same "never act on
  don't-know" rule the path and session checks already follow, at a cost of one stale process.
- The E2E assembly declares `DoNotParallelize`. Its tests share one desktop, one winapp workflow
  id and one process-wide invocation counter, so running them concurrently would let a headless
  test release the UI turn a neighbour was mid-interaction on, and let the wiring probe's real
  `ui yield` release someone else's turn. MSTest is sequential by default, so nothing changes
  today; the declaration exists so a `.runsettings` or a `--parallel` cannot flip that assumption
  silently, since the resulting failures would be indistinguishable from ordinary UI flake.
- A failed E2E session bootstrap no longer leaves a half-published session behind. The host
  process, the winapp wrapper and the UIA reader were assigned to statics as each was built, but
  the reuse path keys on the wrapper alone, so a throw between the two - a `COMException` out of
  the reader, say, which the narrow `WinAppException`/`TimeoutException` filter did not even
  catch - left the next test class incrementing the ref count on a session whose reader was never
  built, then failing on a null field far from the cause. Publication now happens only once all
  three have been constructed, and a launch abandoned by a failure is killed rather than dropped:
  nothing else would ever have reaped it, and while it lived it held the liveness lease that
  defers the orphan sweep of every concurrent run of the checkout.
- A storage fault that stops the startup gate being addressed is now reported as itself rather
  than as contention. Setup failure and a genuinely held gate were both a `null` return, so an
  unwritable claim directory spent the full timeout and then blamed a competing run that never
  existed, discarding the storage error that was the only actionable fact. The same applies to
  the gate file itself: only a sharing or lock violation establishes another holder, so an ACL
  denial or a directory occupying the gate's name is now reported as the fault it is.
- Two install paths are the same directory only when the filesystem confirms both spellings.
  Where neither path resolved, the comparison fell back to a case-insensitive string match, and
  the previous release note justified that by saying both spellings are then the caller's own.
  That claim was wrong: every production caller is a destructive ownership check, and each turns
  a match into an unregistration. So an unresolvable recorded path that merely looked like ours
  proved ownership of a registration that might belong to another checkout - and the paths most
  likely to be unresolvable are exactly the foreign ones, since this run's own layout is the one
  guaranteed to exist. The comparison now fails closed unless both sides resolve, which costs a
  reported conflict the caller can act on in place of a silent eviction it cannot.
- A killed `winapp.exe` is now waited for. `Process.Kill` only requests termination, and every
  caller of the timeout reaper returned or threw the instant it came back, so the next test's
  first `winapp ui` call raced a child that still held the UI turn - the orphan symptom the
  reaper's own warning describes, reached through success rather than failure. It now waits a
  bounded five seconds and reports a process that outlives that, which is one stuck in the kernel
  where a further kill would not help either.
- The cleanup tests' own reaper no longer re-opens a bare process id. They asserted on a probe by
  id after the `Process` that started it had been disposed, so between the assertion and the kill
  Windows was free to reassign that id and the helper could have reported on, or terminated, an
  unrelated process - the same defect the orphan sweep was fixed for, reintroduced in the test
  infrastructure that exists to check it. The helper now opens and holds a handle for the whole
  decision. This one is deliberately not backed by a new test: the window it closes needs Windows
  to actually recycle an id, which will not happen inside a run, so any test written for it would
  pass identically with the fix removed.
- Session statics are published only after the bootstrap has nothing left that can throw. The
  three fields were assigned before the launch was logged, so a failing write to a redirected or
  closed stdout would have left a session visible to the next test class with its process already
  killed - a narrower instance of the half-published session above, through the one statement that
  looked too inert to matter.
- A packaged-tier lock file this run cannot open is likewise no longer reported as a lock
  another run holds. The failure is recorded and raised only once it has outlasted the whole
  wait, so a genuinely transient denial — a lock left delete-pending by a third party holding it
  with delete sharing — still clears on its own within the ordinary poll. Classification is by
  Win32 error code rather than exception type, because `DirectoryNotFoundException` is an
  `IOException` and a missing lock directory is not a competing run.
- Localization extraction now converts recognized count-based singular/plural ternaries
  into ICU plural messages (spec 005 §10.4, [#1131](https://github.com/microsoft/microsoft-ui-reactor/issues/1131)).
- Localization extraction normalizes boolean select arguments to the string keys expected
  by ICU MessageFormat when rewriting source (spec 005 §10.4, [#1131](https://github.com/microsoft/microsoft-ui-reactor/issues/1131)).
- **Doc apps no longer hardcode a window size.** Every `docs/_pipeline/apps/*`
  launch line dropped its `width:` / `height:` arguments, so the guide teaches
  the OS-default shape introduced in [#924](https://github.com/microsoft/microsoft-ui-reactor/issues/924). The screenshot harness now forwards
  `app.width` / `app.height` from each `doc-manifest.yaml` to the devtools
  preview host via the new `--width` / `--height` switches, which keeps captures
  deterministic — the manifest is now the single declaration of a doc app's
  capture size.

## Removed

- **Dead `doc-manifest.yaml` fields.** `app.title`, `app.theme`,
  `screenshots[].region`, `screenshots[].theme`, `screenshots[].bounds` and the
  `snippets:` block were parsed but never read by the doc pipeline — `region:`
  alone appeared 224 times. They are removed from the schema and from all 53
  manifests. Two manifests had silently drifted from the sizes actually in
  effect (`animation`, `charting`), which is what being unread allowed.

## Fixed

- **`mur loc extract` no longer offers icon glyphs as localizable strings
  (spec 005 §10.2, [#1182](https://github.com/microsoft/microsoft-ui-reactor/issues/1182)).** Segoe Fluent / MDL2 glyphs reach the DSL as ordinary string
  literals — `Button("\uE74D", onDelete)` — so the scanner treated them like any other bare
  string: a `.resw` key whose value is a private-use code point, and an `extract --dry-run`
  CI gate that could only be satisfied by sending that code point to translators.

  Literals whose code points are *all* in a Unicode private-use area are now skipped. The
  filter sits on the two sinks every extraction funnels through, so it covers each shape the
  scanner reads: plain literals, interpolated strings, both branches of a ternary, and the
  literal side of a `??` fallback. The test is *all*, not *any*, so a mixed string such as
  `"\uE74D Delete"` still contains prose and is still extracted, as is the text branch of
  `isOpen ? "\uE70D" : "Visible"`.

- **Window placement no longer vanishes when two app instances save at once
  (spec 063 §5).** `JsonFileStore` — the default unpackaged persistence store — merged
  its document under a per-*instance* lock and committed through a shared temp file, so
  two instances or processes saving placement concurrently each wrote a document missing
  the other's entries and the last writer won. Layouts disappeared even when the two
  windows had *different* `PersistenceId`s. Measured: with 8 concurrent writers, 1 entry
  survived.

  Writes now take a named cross-process guard, stage through a per-process temp file, and
  commit with `File.Replace`; reads grant delete sharing. Both halves matter — on Windows
  a plain `File.Move(overwrite: true)` cannot replace a file another process has open even
  when that reader grants delete sharing, so a concurrent reader could previously kill a
  write outright (silently, since writes are best-effort). This affects every existing
  unpackaged app, not only users of the new opt-in store.

- **Stale `Microsoft.WindowsAppSDK` version pins in shipped agent-kit recipes and docs
  (spec 063 §3.0).** **22 literal occurrences across 20 files** used a
  `#:package Microsoft.WindowsAppSDK@2.0.1` file-based-app header, which the 2.1.3 bump
  (spec 059) missed entirely because that shape does not match the
  `Microsoft.WindowsAppSDK" Version=` grep that spec prescribed. They had been advertising a
  version *below* the framework's own floor — an `NU1605` downgrade for anyone running them — and
  the **18 recipe files** under `skills/recipes/` and
  `plugins/reactor/skills/reactor-recipes/references/` are packed into the shipped NuGet agent
  kit, so this was user-facing rather than internal-only. All now track the pinned version, and a
  new guard (`WinAppSDKReferenceGuardTests.No_literal_SDK_pin_sits_below_the_central_pinned_version`)
  sweeps the tree for both pin shapes so the class cannot recur.

- **The DIP migration notice no longer fires for apps that declare no window
  size (spec 036 §12.1 / §12.2a).** `ReactorApp.Run` emitted the
  `[reactor] … now DIPs …` stderr line on every launch, including the size-less
  overloads the same notice recommends. It is now emitted only when the caller
  actually supplies a width or height, and a size-less call no longer consumes
  the one-shot latch.

- **Content-collapse parking for recycled ItemsView rows (issue [#1213](https://github.com/microsoft/microsoft-ui-reactor/issues/1213)).**
  Parking collapses template content instead of the outer container and restores
  its original visibility value source on reuse. Recycled rows are excluded from
  scroll anchoring, preventing invalid-anchor and layout-cycle failures.

- **Tray icon `Click` and `RightClick` no longer fire twice per interaction
  (spec 036 §11.4, issue [#1180](https://github.com/microsoft/microsoft-ui-reactor/issues/1180)).** Under `NOTIFYICON_VERSION_4` the shell
  forwards both the legacy mouse message and the version-4 semantic
  notification for a single physical interaction — a left click arrives as
  `WM_LBUTTONUP` *and* `NIN_SELECT`, a right click as `WM_RBUTTONUP` *and*
  `WM_CONTEXTMENU`. `TrayHiddenWindow` routed both arms of each pair, so
  `ReactorTrayIcon.Click` and `ReactorTrayIcon.RightClick` raised twice per
  click. Only the version-4 notifications are routed now. `DoubleClick` is
  unaffected: `WM_LBUTTONDBLCLK` has no version-4 counterpart, so a physical
  double click still raises `Click` once (from the first click's `NIN_SELECT`)
  followed by `DoubleClick` once.

- `ShutdownPolicy.Explicit` and `ShutdownPolicy.OnLastSurfaceClosed` now actually keep the process
  running. Reactor takes ownership of the WinUI dispatcher loop at startup
  (`Application.DispatcherShutdownMode = OnExplicitShutdown`), so the platform no longer ends the
  process on last-window-close without consulting `EvaluateShutdownPolicy`. Closing the last window
  of a tray-backed app used to exit with code 0 as if the policy were `OnPrimaryWindowClosed`.
  Ownership is unconditional, which also makes the documented issue-[#647](https://github.com/microsoft/microsoft-ui-reactor/issues/647) guarantee real: under the
  default policy, closing an auxiliary window that opted out via `ExcludeFromShutdownPolicy` no
  longer ends the app even when it was the last window on screen. Apps that embed
  `ReactorHostControl` in their own `Application` are left alone. (spec 036 §6.2, issue [#1204](https://github.com/microsoft/microsoft-ui-reactor/issues/1204))
