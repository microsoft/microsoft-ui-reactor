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
Use a temporary undersized description/header layout and an ancestor clip as
negative controls; the former must fail and explicit clip geometry must block,
never pass. Confirm an unsupported control remains `blocked/not-implemented`.
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
hashes and the exact resolved package version; untransformed JIT `Reactor.dll`
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
