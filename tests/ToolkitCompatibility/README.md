# Toolkit wrapper compatibility validation

This is the isolated validation consumer for #1504, not a Toolkit port. It links
the actual `samples/apps/wct-controls/WctControls.cs` declarations. There is no
App.xaml, explicit Toolkit metadata-provider construction, reflection-based
provider enumeration, or blanket trimmer root in the normal consumer.

The original investigation used `6f7f398cdeecee9bf13517044052e6095894ef8a`.
Harness development started at `9de044434abfb09caf8bf371a37f99887b60f2e2`.
Final evidence must record the exact integrated commits, mapping hash, dependency
versions, commands, outputs and process exit codes. Fixes for #1564, #1565 and
#1566 are independently owned; a harness change does not establish their status.

The [2026-11-19 validation report](reports/2026-11-19-validation.md) records four
complete but blocked unpackaged lanes on a pinned revision, separately from
later deployment preparation. It is not a compatibility verdict for current main.

## Required matrix

All 25 controls below run on x64 in these six lanes, first against project
references and then against locally packed Reactor packages.

| Runtime | Unpackaged | Registered loose layout | Installed MSIX |
|---|---|---|---|
| JIT | Dynamic code supported, no package identity | Dynamic code supported, actual identity, development layout | Dynamic code supported, actual identity, installed location |
| NativeAOT | Dynamic code disabled, no package identity | Dynamic code disabled, actual identity, development layout | Dynamic code disabled, actual identity, installed location |

Installed MSIX is not interchangeable with loose-layout registration. The runner
must install the archive, resolve its own per-run alias, and assert that the
running executable is below `Package.Current.InstalledLocation`, not the original
publish directory. AOT is a runtime assertion, not an inference from an MSBuild
argument. ARM64 and additional Toolkit/Labs controls are out of scope.

## Scenario inventory

Every core scenario includes mount, an observable update on the same native
instance, removal and fresh remount. Child scenarios additionally verify keyed
identity, ordering, count and removed logical keys/content. Native child reuse
through Reactor's pool is allowed. Assertions check actual
`CommunityToolkit.WinUI.Controls` types, not Reactor implementations.

| Control | Core operations and oracles | Interaction/lifecycle evidence still required |
|---|---|---|
| SettingsCard | Header, description, icon and content slots; update text in place; clear and restore slots; accessible label | Click enabled/disabled, latest callback, focus, no callback after removal |
| SettingsExpander | Header/content/icon slots; expand/collapse; keyed cards inserted, reordered, removed and remounted with exact count | Keyboard expansion, nested input, label and expand/collapse pattern |
| Segmented | SelectedIndex and selected item; update/reorder choices; reject native drift | Keyboard/pointer selection, accept/reject parent, exactly the changed selection callback |
| RangeSelector | Both endpoints; parent update, reject drift, bounds and ordering | Move each real thumb independently; accept/reject; changed-endpoint callback only; no stale callbacks after remount |
| RadialGauge | Value, bounds and unit; native drift reassertion and realized value text | Pointer/keyboard value changes where supported, range-value UIA and focus |
| ColorPicker | Two distinct colors and alpha; parent update/rejection | Real color input, exact callback color, no echo or duplicate/stale subscription |
| ImageCropper | Deterministic local bitmap, crop rectangle/shape update, exported bitmap dimensions and pixels | Drag crop bounds, save output, release source on removal |
| CameraPreview | Start, stop, remove, remount; unavailable/denied error and released resources | Actual frames required for preview-success claim; unavailable/denied cleanup is separate, never a success substitute |
| TokenizingTextBox | Suggestions/text updates, token creation/removal and exact token values | Type delimiter, select suggestion, remove token, max-token boundary; unsubscribe on removal |
| RichSuggestBox | Prefix/query/filter updates, token insertion/removal and resulting document text | Type query, choose suggestion, delete token, cancel request on removal |
| GridSplitter | Adjacent grid definitions, orientation, resize constraints | Real drag/keyboard changes the intended row/column and not its neighbor; remount |
| ContentSizer | Explicit target, target size and orientation updates | Drag changes target size, not only the sizer property; detach/remount target |
| PropertySizer | Bound value, min/max and actual target size | Real drag changes target; native bounds behavior; property callback released on removal |
| HeaderedContentControl | Header and content replace/update/clear with exact visual text | Label/focus relationships, no orphaned content |
| HeaderedItemsControl | Header and dynamic keyed items, exact order/count and native identity | Selection/focus where applicable, removed-item detachment |
| HeaderedTreeView | Header, hierarchical source replacement, realized node labels/count | Expand/collapse, selection, tree roles and focus after updates |
| LayoutTransformControl | Transform changes measure/arrange (90-degree width/height exchange); child update/remove | Hit testing/focus after transform, no render-only substitute |
| MetadataControl | Replace metadata labels, separators and item count; verify rendered text | Hyperlink/command behavior when configured, readable UIA names |
| ConstrainedBox | Aspect ratio and width/height constraints; actual arranged dimensions | Text scaling/RTL does not invalidate constraints or hide content |
| UniformGrid | Rows, Columns, FirstColumn; cell positions and size before/after update | Keyed reorder, row/column boundary behavior, RTL |
| DockPanel | Per-child attached Dock and LastChildFill; actual remaining-space geometry | Change dock edge, reorder/remove last child, RTL |
| WrapPanel | Width/spacing/orientation; children move to a new line on narrower width | Reflow after keyed insert/remove, RTL/text scaling |
| StaggeredPanel | Column width/spacing and heterogeneous child heights; column count/positions | Reflow after resize and keyed insert/remove |
| SwitchPresenter | Actual SwitchCase collection, matching/default cases and visible case content | Change discriminator, replace/remove case, no hand-written switch substitute |
| TabbedCommandBar | Actual tabs, commands and document content; update/remove/remount | Select each tab, invoke command, latest command callback, UIA roles/focus |

## Extended evidence and honest outcomes

For both JIT and AOT, record the deployment used for real input, UIA
names/roles/patterns/focus, light/dark/high-contrast, RTL and text scaling.
Property assignment that raises no native event is not callback evidence.
Changing font size alone is not evidence of the Windows text-scale setting.
High contrast, permissions and camera hardware must be observed, not simulated
and then described as an OS setting.

Each result is `passed`, `failed`, `blocked` or `inapplicable`, with a reason and
evidence. Not-yet-implemented scenarios are blocked. A missing control, zero
fixtures, duplicate result, crash, timeout, nonzero exit, incomplete TAP plan or
missing completion trailer cannot become green. A filtered diagnostic run is
not the complete matrix. A successful property scenario does not clear a
separately blocked input/accessibility scenario.

`ReasonKind` distinguishes `not-implemented`, `approval`, `tool`, and
`native-contract`; unavailable coverage is never presented as an external
permission failure by default. Local checks exercise native accessible-name
updates, selected value patterns, differential programmatic focus, and actual
Light/Dark/RTL PNG capture. Captures establish theme/direction activation and
nonempty output, not pixel-perfect parity or physical keyboard navigation.
OS high contrast and alternate OS text scaling require the real setting, not a
font-size substitute. No shared desktop setting is changed.

### Coverage aliases, not new native passes

Four legacy `PendingOperations` labels duplicate finite assertions already in
other scenarios. Their inventory keys and raw blocked results are retained for
compatibility with pinned evidence. `Summarize-Matrix.ps1` adds
`coveredByScenario` only to a `blocked/not-implemented` row whose exact counterpart
passed in the **same control process, lane and run**, after complete TAP/JSONL,
exit-code and capture-metadata validation. Missing, failed, blocked, inapplicable,
duplicate/ambiguous or other-lane results cannot supply that annotation.

| Recorded blocked label | Passing counterpart | Bounded assertions represented |
|---|---|---|
| `DockPanel/attached-dock-last-child-fill-geometry` | `DockPanel/update` | [Attached Left -> Top, LastChildFill true -> false, exact edge/last-child sizes and allocated width](Scenarios.cs#L296-L320). Not every dock edge or last-child reorder/removal. |
| `UniformGrid/first-column-keyed-reorder` | `UniformGrid/update` | [FirstColumn=1 offset and cell geometry](Scenarios.cs#L283-L295), [retained keyed instances, inserted tile-6 and removed tile-5](Scenarios.cs#L378-L411). Not arbitrary FirstColumn values or row/column boundaries. |
| `SwitchPresenter/actual-matching-default-case-collection` | `SwitchPresenter/sample-native-case-selection` | [Actual matching/default/no-default case collections, stable identities and fresh remount](SwitchPresenterSampleChecks.cs#L12-L118). Not dynamic case replacement/removal while mounted. |
| `TabbedCommandBar/actual-tabs-command-invocation` | `TabbedCommandBar/sample-native-tabs-commands` | [Actual Home/View tabs, five commands twice, two-command burst, current callbacks and fresh remount](TabbedCommandBarSampleChecks.cs#L15-L100). Uses public UIA Invoke, not physical keyboard/pointer navigation. |

The report lists these same-lane links separately. It never changes raw status,
reason, detail, P/F/B/N totals, process/lane verdicts or the scenario inventory.
An annotated blocked row is **not** a new pass. Read `coveredByScenario` as the
fully qualified scenario key in that row's lane and report run, not as a
cross-run or cross-consumer link. Additional desired edge cases need separately
named scenarios; these four mappings must not clear unrelated compound labels.
The pinned Segmented provider gap stays blocked, and OS text-scale coverage is
unchanged.

For historical archives, generate any new summary from a separate copy of the
recorded metadata/TAP/JSONL and validated inventories, retaining the original
reports and source revision. Never run the summarizer against frozen evidence
in place; it writes its report files into `-EvidenceDirectory`.

### Separate physical-input suite

The following are **source links**, not proof of execution or acceptance in any
compatibility lane. They live in the separate `Reactor.AppTests` E2E suite in
[`ToolkitInputTests.cs`](../Reactor.AppTests/Tests/ToolkitInputTests.cs).
Recorded execution evidence must identify that suite's revision, runtime and
deployment independently. None supplies `coveredByScenario`.

| Named family | Existing E2E source and scope | Not established by those tests |
|---|---|---|
| RangeSelector / `both-real-thumbs-parent-accept-reject-callback-routing` | [Accept and route each thumb callback](../Reactor.AppTests/Tests/ToolkitInputTests.cs#L18-L38), [reject and reassert both endpoints](../Reactor.AppTests/Tests/ToolkitInputTests.cs#L42-L58), [actual Tab/Shift+Tab focus](../Reactor.AppTests/Tests/ToolkitInputTests.cs#L173-L197) | No stale callback after remount; no claim that physical input ran in each compatibility lane. |
| RichSuggestBox / `type-query-insert-delete-token` | [`RichSuggestBox_DownEnter_CommitsFullTokensAcrossRenders`](../Reactor.AppTests/Tests/ToolkitInputTests.cs#L62-L87) types/filters a query and commits two full tokens | Token deletion and request cancellation on removal remain separate. |
| TokenizingTextBox / `type-suggest-add-remove-max-tokens` | [`TokenizingTextBox_TypesConvertsAndRemovesOnlyChosenToken`](../Reactor.AppTests/Tests/ToolkitInputTests.cs#L91-L109) types, filters, converts delimiter input and selectively removes tokens | Maximum-token boundary, physical suggestion choice and unsubscribe-on-removal remain separate. |
| ContentSizer / `real-target-resize` | [`ContentSizer_Drag_ResizesAdvertisedTarget`](../Reactor.AppTests/Tests/ToolkitInputTests.cs#L113-L131) checks two drags against the target height, settled layout and unchanged width | Other orientations and physical input after target detach/remount. |
| PropertySizer / `real-bound-target-resize-bounds` | [`PropertySizer_Drag_UpdatesTargetAndClampsBothBounds`](../Reactor.AppTests/Tests/ToolkitInputTests.cs#L135-L143) checks target/binding at both bounds | Callback release on removal and physical input after remount. |
| GridSplitter / `real-grid-definition-resize` | [`GridSplitter_Drag_ResizesBothColumns`](../Reactor.AppTests/Tests/ToolkitInputTests.cs#L147-L169) checks adjacent columns/panes, preserved total and fixed sibling | Row orientation, keyboard resizing and physical input after remount. |

The dependency-free `Test-ResultProtocol.ps1` regression checks these reporting
rules using bounded synthetic runs, including passing and rejected counterpart
cases. It does not launch the consumer, exercise native controls or establish
new native coverage.

### Bounded OS text-scale check (#1602)

`SettingsCard/os-text-scaling` now captures and checks the current core scenario's
string header and `TextBlock` description when the observed real OS
`UISettings.TextScaleFactor` is greater than 1. At 100% it remains
`blocked/approval`; other controls remain `blocked/not-implemented` even on a
scaled host. Rich headers, non-TextBlock descriptions and explicit clip geometries
are also unimplemented, not native limitations or passes. High-contrast behavior
is unchanged.

The check requires exactly one realized header outside the content/icon/description
slots, nonempty attached text with OS scaling enabled, and no native trimming.
A separate native TextBlock measures the full text with matching typography and
the current OS scale, without trimming, fixed height or a line cap. For wrapped
text, its width preserves the native measured content width within the arranged
slot, accounting for margins and never shrinking below the actual text extent.
This avoids rewrapping a single line when its fractional `ActualWidth` is slightly
smaller than its rounded layout width. The probe retains native layout rounding.
Required and
allocated dimensions must be finite, positive and fit within one DIP of layout
rounding. Each text box must also fit within every ancestor's rectangular bounds
through the window content, with no collapsed or fully transparent ancestor.
This is deliberately conservative, including ancestors that do not clip.
The TAP log records text, typography, wrapping, dimensions, ancestor bounds,
theme, rasterization scale, and OS scale before/after. The PNG is captured before
the assertions so a failed fit can still be inspected.

This does not prove scale-to-scale growth, pixel-perfect visibility, overlapping
sibling behavior, arbitrary rich text, long-content stress, or other controls.
The existing short core strings are the measured scope. Captures require human
review. A build or headless geometry test is **not** WinUI/runtime acceptance.

After the positive checks, the same bounded scenario runs three native negative
controls. It halves each text slot's height in turn, verifies a nonzero native
size reduction, and requires a role-matched trimming, text-overflow or
ancestor-overflow failure. Invalid measurements and unrelated exceptions do not
count. It then applies a SettingsCard ancestor clip cutting through the header
and requires the exact `not-implemented` explicit-clip outcome, not a pass.
Each mutation restores the original dependency-property value (including unset)
in `finally`, checks the original text/card dimensions and reruns the positive
oracle. Both text slots are rechecked after all three rejections. The TAP log
records the perturbations, observed reasons and restored baseline; the PNG is
the unmodified positive capture, not a screenshot of the negative controls.

Headless regression tests compile the same measurement guards without WinUI,
WinRT initialization, or the compatibility consumer. The project is included in
solution builds and runs in the PR CI Unit Tests job:

```powershell
dotnet test tests\ToolkitCompatibility.HeadlessTests --filter-class "*TextScalingMeasurementsTests*"
```

Later, on an explicitly approved dedicated host/VM, run the SettingsCard lane at
100% and confirm `blocked/approval`, then at a real scale above 100% (including
the chosen maximum acceptance scale). Verify nonzero full-text measurements,
the exact two checked slots, ancestor bounds, stable OS scale, and the PNG.
Verify all three native negative-control reasons and the final restored positive
baseline in that same run. Explicit clip geometry must remain blocked, never
silently accepted. Confirm an unsupported control remains `blocked/not-implemented`.
Repeat under each deployment/runtime being claimed. High-contrast captures still
require real OS high contrast. Any setting changes and restoration need their
own authorization; these instructions do not grant it.

Mounts require actual visual-tree attachment and nonzero layout rather than
`IsLoaded` alone. Removal checks detachment and exactly one public `OnUnmount`
notification. `GetElementTag` is diagnostic association, not a guarantee of
active ownership or a cleared-on-unmount contract. Native asynchronous resource
release is separately asserted by the consent-gated camera probe.

Settings header ownership checks also replace or clear native headers before an
unrelated update and inside parent `OnUnmount` callbacks. Stateful descendants
must survive updates, each displaced owned child must be cleaned exactly once,
and externally supplied replacements must retain their identity and children.

`sample-deferred-choice-burst` mounts the actual RichSuggestBox page under a separate
public `Reconciler` whose render requests are recorded, not applied. It commits two
native suggestions through public UIA Invoke, then reconciles once and requires a
selection count of two. This checks callback/state batching, not keyboard input.

Direct-native or explicit-provider positive controls belong in distinct
builds/processes, never a runtime switch that roots providers in the normal AOT
binary. Classify new failures using an equivalent actual Toolkit baseline and
send bounded repros to the coordinator. Do not repair product/sample defects
here or drop the failing scenario.

## Machine safety

Use unique per-run package names and execution aliases. Remove only registrations
created by that exact run. Do not create/trust certificates, enable Developer
Mode, alter accessibility settings or grant camera permission implicitly.
Installation/signing/input blockers go to the coordinator for user decisions.
Coordinate foreground input runs before starting them.

### Segmented UIA comparison (#1599)

`tests\ToolkitSegmentedUiaProbe` is a separate **native-only** WinUI application.
It has no Reactor reference and does not use `NativeBaseline` (which still runs
inside a Reactor host). It uses the centrally pinned Toolkit and WinUI packages,
explicit native XAML metadata registration, and the same `SegmentedUiaDiagnostics`
source as the Segmented arm of `LocalEvidenceChecks`.

The probe first requires a stock ListView positive control to expose every item's
selection identity for indices 0 and 1. It then records direct Toolkit Segmented
instances with the harness's string items and with explicit SegmentedItem
containers (the code-only equivalent of declarative native items). Both use the
harness's labels, width, single-selection mode and accessible root name.
This is not a literal XAML-authored application.

The shared capture records the root, container and public child peers, their
managed types as well as native class names, raw and typed Selection/SelectionItem
patterns, container EventsSource peers, and per-item IsSelected at both indices.
The child walk is bounded at depth 16 / 256 peers and reports truncation; absence
in a truncated tree is not proof of absence. It does not construct a replacement
peer, add an adapter, change the existing depth-4 acceptance search, or upgrade a
blocked `uia-value-patterns` result. Selection is assigned programmatically and
restored, not driven through physical input. The Reactor capture is also saved
as `segmented-uia.txt` beside its `results.jsonl`, independently of console capture.

Headless preparation (no application launch):

```powershell
dotnet build tests\ToolkitSegmentedUiaProbe\ToolkitSegmentedUiaProbe.csproj -p:Platform=x64
dotnet build tests\ToolkitCompatibility\ToolkitCompatibility.csproj -p:Platform=x64 -p:SkipSignaturesGen=true -m:1 -nodereuse:false
```

Only after a coordinated runtime slot, run these **sequentially**, using new
evidence paths. Neither command is a headless test.

```powershell
tests\ToolkitSegmentedUiaProbe\bin\x64\Debug\net10.0-windows10.0.22621.0\win-x64\ToolkitSegmentedUiaProbe.exe --run C:\evidence\segmented-native.txt
pwsh tests\ToolkitCompatibility\Run-Matrix.ps1 -EvidenceDirectory C:\evidence\segmented-reactor -Runtime jit -Deployment unpackaged -Filter Segmented
```

The native probe closes its one window automatically. Exit 0 plus `COMPLETE`
means a complete diagnostic capture, **not** accessibility acceptance. A failed
ListView positive control, timeout, exception or missing completion invalidates
the comparison. The filtered Reactor run additionally executes its existing
property, programmatic-focus and render-capture scenarios; it does not inject
physical input. Exit 2 for blocked coverage remains non-success.

For independent cross-process inspection, use `--inspect <new-log-path>` instead
of `--run`. After the same automatic capture and ListView positive control, it
keeps all three controls visible with distinct automation IDs. Its two buttons
assign index 0 / List or index 1 / Grid to every control and log the actual native
indices. Observe the selection state through the independent UIA client, not the
status label alone. Close the diagnostic window to finish and release the slot.
This mode remains open until closed and is not an unattended matrix lane.

Compare `native-segmented-strings` against `sample-mappings` at both indices.
If only the deeper walk or EventsSource reveals providers, investigate that
specific path before changing the harness. If only Reactor differs, investigate
the mapping/host rather than the Toolkit contract. If both lack providers while
ListView passes, the evidence supports an inherited native accessibility gap,
but still needs a read-only cross-process UIA inspection before an upstream
report. In-process peer enumeration alone does not establish what a screen reader
can observe. Preserve the resolved assets, exact source revision and binary hashes
with both captures. A future runtime result must not be inferred from a build.

The November 18-19, 2026 JIT/unpackaged comparison for #1599 resolved Toolkit
`8.3.260402-preview2` and WinUI `2.2.1` (Windows App SDK `2.2.0`). Stock ListView
exposed `ListViewAutomationPeer` Selection and `ListViewItemDataAutomationPeer`
SelectionItem; all three item identities matched at both indices. Both direct
Segmented forms and the Reactor mapping instead exposed a
`FrameworkElementAutomationPeer` with class `NamedContainerAutomationPeer` /
role Group and `ListViewItemAutomationPeer` children. No Selection or
SelectionItem provider or container EventsSource was present. Each public tree
contained seven peers with no truncation.

An independent Computer Use UIA read then showed ListView's selected row changing
List -> Grid -> List while neither native Segmented form exposed selected-item
state. The diagnostic log confirmed all three native indices changed together.
This identifies a native accessibility gap for those versions, rather than a
Reactor mapping failure. The SDK Inspect executable required elevation and was
not run; no Inspect property export or screen-reader announcement is claimed.
The issue artifact preserves the version-pinned evidence. The compatibility
scenario remains **blocked**, not passed or inapplicable; no replacement peer is
installed. Retest the native comparison after a Toolkit/WinUI upgrade.

## Running

Use a Visual Studio x64 developer PowerShell for NativeAOT publishing. No warning
policy is relaxed. The runner restores separately, then publishes Release.

```powershell
pwsh tests\ToolkitCompatibility\Test-ResultProtocol.ps1
pwsh tests\ToolkitCompatibility\Test-RunnerSafety.ps1
# Headless, no window, after a JIT build of the consumer:
<consumer output>\ToolkitCompatibility.exe --protocol-self-test
pwsh tests\ToolkitCompatibility\Run-Matrix.ps1 -EvidenceDirectory C:\evidence\unique-run
# Narrow diagnostic, not acceptance of the full inventory:
pwsh tests\ToolkitCompatibility\Run-Matrix.ps1 -EvidenceDirectory C:\evidence\settings-run -Runtime jit -Filter SettingsCard
```

Each invocation requires a new evidence directory and owns unique package names
and aliases. Runtime, deployment and control-filter arguments are case-insensitive
and are recorded and passed to the consumer using their canonical spelling.
`run.json`, `commands.txt`, lane restore/publish/deploy logs, TAP,
incrementally flushed `results.jsonl` and `matrix.json` retain the evidence.
Exit 2 from the consumer means complete results with blocked scenarios; it is
not success. The matrix runner returns nonzero for any non-passing lane.
A Reactor render or effect error reported during a scenario fails it, even when
its state assertions are satisfied; `--protocol-self-test` checks that headlessly.

Generate a complete per-control/per-scenario report, including unexecuted
approval-blocked lanes, with:

```powershell
pwsh tests\ToolkitCompatibility\Summarize-Matrix.ps1 -EvidenceDirectory C:\evidence\unique-run
```

The summarizer requires recorded timeout/capture-completion metadata and a
matching process verdict before counting revalidated scenario rows. Archives
from before that metadata was recorded must retain their original versioned
reports; the current summarizer rejects missing metadata rather than inferring
complete capture from TAP alone. It writes `scenario-matrix.json` and `summary.md`. Published binary,
PRI and XBF hashes, effective build properties and restored dependency assets
are retained per lane. Package-consumer runs validate the staged-source/package
hashes and the exact resolved package version; untransformed JIT `Microsoft.UI.Reactor.dll`
must match the assembly inside the prepared nupkg.

For coordinated input diagnostics, launch the same published executable with
`--page <route>` (for example `--page range`). This uses `SamplePages.cs` directly.
Interactive mode does not produce acceptance results and must not be substituted
for a core matrix run.

After project-reference integration works, prepare an external consumer from
the same source and repeat the matrix:

```powershell
pwsh tests\ToolkitCompatibility\Prepare-PackageConsumer.ps1 -Destination C:\evidence\package-consumer
pwsh tests\ToolkitCompatibility\Run-Matrix.ps1 -EvidenceDirectory C:\evidence\package-matrix -ConsumerProject C:\evidence\package-consumer\consumer\ToolkitCompatibility.csproj
```

The staged project has no ProjectReference and is outside repository MSBuild
imports. It retains Release warning enforcement, uses a local Reactor feed,
checks the shipped generator/attributes/XBF assets, and records the package hash.
Runtime template realization and output inventories are still required to prove
PRI/native dependency delivery; a package-entry check alone is not that proof.

The manifest uses Windows 11's documented unsigned-package publisher namespace
(`OID.2.25.311729368913984317654407730594956997722=1`) and installation passes
`Add-AppxPackage -AllowUnsigned`. These executable unsigned packages require
administrator privileges and install for **all users**, not only the current
account ([official documentation](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package)).
The runner does not elevate or change certificate trust; an unapproved
installation remains blocked. Installed-MSIX ownership queries, exact-package
removal and cleanup verification use the same all-users scope; loose-layout
registration and cleanup remain current-user scoped.
Pass `-PrepareMsixOnly` to build final archives without retrying an
approval-blocked installation. Each archive gets an `installation-request.json`
containing its hash, identity, alias, source revision, exact install command,
administrator requirement and all-users installation/cleanup scope. Archive-only
preparation does not perform an all-users package query or installation; it also
writes `blocker.json` with reason `approval`, which is the only way the summary
reports an approval blocker. A lane that installed and then failed is reported
by its actual failure, not as approval-blocked. Any lane exception without a
recorded blocker marks the lane failed, and its unexecuted scenarios are reported
as failed with reason `lane-failure`.

The dependency-free script checks exercise synthetic result streams, mocked Appx
commands and owned console child processes. They do not install packages, elevate,
open a camera or establish live MSIX cleanup behavior.

### Focused TabbedCommandBar sample regression

`-Filter TabbedCommandBar` includes the actual `SamplePages.CreatePage("tcb")`
regression for #1577. A filtered run is not the complete 25-control matrix.

```powershell
pwsh tests\ToolkitCompatibility\Run-Matrix.ps1 -EvidenceDirectory C:\evidence\tabbed-run -Runtime jit -Deployment unpackaged -Filter TabbedCommandBar
```

The regression checks visible external document composition, actual Toolkit
Home/View tabs and all five native commands, a two-command burst issued without
yielding between UIA invokes, exact command feedback across rerenders, retained
tab/button identity and selection, light/dark background
changes, removal and fresh remount. Selection uses native `SelectedItem`, and
commands use the public UIA Invoke provider with a real Click-event check.
Neither is physical keyboard/pointer input. OS high contrast/text scaling,
focus navigation, AOT and packaged deployment are not established by this run.
It does not prove that retained off-tree controls have no event subscriptions.

The initial implementation establishes a subset of property/geometry and
mount/update/remove/remount behavior. Required advanced operations listed above
remain explicit blocked results until implemented and exercised. This is a
reusable harness, not a completed compatibility verdict.

For a bounded camera-only proof, use `-CameraProbeOnly`. Without `-AllowCamera`
this verifies the no-consent path without constructing a preview. Only after
explicit permission, adding `-AllowCamera` executes exactly the dedicated
two-generation frame/cleanup probe, not the generic camera property scenarios.
`--page camera` is deliberately rejected by this harness.

## Opt-in camera lifecycle check

`CameraSampleChecks.RunAsync(root, allowCamera)` rejects a false opt-in before
mounting anything. The harness must pass its explicit `--allow-camera true`
decision, and only after the coordinator has obtained approval for the provisioned
device. Building or listing this check is not permission to open a camera.

The check mounts the actual camera sample page twice. Each generation must
deliver at least three Toolkit `FrameArrived` events, then detach on navigation,
complete the captured adapter cleanup task without `HasErrors`, and stop
delivering frames. Re-entry must use fresh native control and helper instances
and deliver fresh frames. Only event counts are logged. The callback never reads
`FrameEventArgs.VideoFrame`, whose getter copies pixels, so it acquires no disposable
frame and does not copy, encode or retain pixels or screenshots.

Helper/frame waits are bounded to five seconds and cleanup waits to ten seconds.
A timeout is a failure, never proof of release. The silence check samples 350 ms
after a 150 ms callback-drain interval; combined with awaited cleanup and fresh
capture it checks the observed lifecycle, not the camera privacy indicator or
arbitrary future silence. Denied/no-device results remain failures of the
live-preview precondition, not live-preview passes. Headless overlap tests are
separate from this hardware check.
