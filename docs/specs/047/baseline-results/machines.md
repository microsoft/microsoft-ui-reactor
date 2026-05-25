# Baseline machines

Per spec §14 Phase 0 deliverable 4, baselines should be captured on **at
minimum: one x64 workstation, one ARM64 Surface-class**. The Phase-0 freeze
includes both: ARM64-native on
[`LAPTOP-4MEP83VI`](#laptop-4mep83vi) (Snapdragon X) and x64-native on
[`CPC-ander-YTZ3O`](#cpc-ander-ytz3o) (Windows 365 Cloud PC, AMD EPYC 7763).

## LAPTOP-4MEP83VI

| Field | Value |
|---|---|
| Class | Snapdragon X laptop (Qualcomm) |
| CPU | ARMv8 (64-bit) Family 8 Model 1 Revision 201, Qualcomm Technologies Inc |
| Process architecture (headline baseline) | **Arm64 (native)** — `2026-05-25-arm64/` folder |
| Process architecture (reference baseline) | **x64 (emulated)** — `2026-05-25/` folder, superseded |
| OS | Microsoft Windows NT 10.0.26200.0 (Windows 11 Enterprise 26200) |
| .NET | 10.0.8 |
| Build configuration | Release (retail) |
| Date of headline M1–M13 capture | 2026-05-25 (ARM64-native) |

**ARM-on-ARM headline.** The Phase-0 exit-gate baseline is the ARM64-native
retail build running on its native ARM hardware. Numbers under
`2026-05-25-arm64/` are the load-bearing data for spec §11 / §12. The earlier
x64-emulated capture under `2026-05-25/` is preserved only as a worst-case
reference — do not diff across architectures (the comparison emitter
rejects rows whose `Architecture` field differs).

**Empirical ARM64-vs-x64-emulated delta on this machine** (M1–M13 mean
ns / op, ARM64-native ÷ x64-emulated):
- M1: 0.06× (ARM64 native is ~17× faster)
- M4: 0.13× (~8×)
- M5: 0.09× (~11×)
- M7: 0.12× (~8×)
- M9: 0.39× (~2.6× — bottleneck is GC pressure, not dispatch)
- M13: 0.14× (~7×)

Across the suite, ARM64-native is **~8–17× faster** than x64-emulated x86_64
on the same silicon for the mount/dispatch-dominated tests; GC-pressure-
dominated tests narrow to ~2.5×. This is why ARM-on-ARM is non-negotiable
for the headline baseline.

**Architecture-specific gotchas** (per
[`perf-suite-runbook.md`](../perf-suite-runbook.md) §10): none recorded
yet at Phase-0 freeze. Add entries here as encountered.

## CPC-ander-YTZ3O

| Field | Value |
|---|---|
| Class | Windows 365 Cloud PC (workstation x64) |
| CPU | AMD EPYC 7763 64-Core Processor (host); 8 cores / 16 logical processors exposed to the VM |
| Process architecture (headline baseline) | **x64 (native)** — `2026-05-25-x64/` folder |
| OS | Microsoft Windows NT 10.0.26200.0 (Windows 11 Enterprise 25H2, build 26200.8390) |
| .NET | 10.0.8 (runtime); SDK 10.0.204 |
| Build configuration | Release (retail) |
| Date of headline M1–M13 capture | 2026-05-25 (x64-native) |
| Power plan at capture | High performance (AC power; cloud-hosted VM, no battery state) |
| Locked refresh rate at capture | n/a — virtual display, no physical monitor |

**Closes Phase-0 §14 deliverable 4** ("at minimum: one x64 workstation, one
ARM64") alongside the ARM64-native baseline on
[`LAPTOP-4MEP83VI`](#laptop-4mep83vi). 195 rows ingested, 0 excluded;
M13 `OnIsOnChangedFireCount = 1` on ReactorToday and ReactorV2 confirms the
§8.2 baseline bug is present.

**Empirical x64-native-vs-ARM64-native delta** (M1–M13 mean ns / op,
**ratio CPC-ander-YTZ3O x64 ÷ LAPTOP-4MEP83VI ARM64**, ReactorToday column):
- M1: 3.1× (x64 EPYC Cloud PC slower)
- M4: 1.6×
- M5: 1.7×
- M6: 3.0×
- M7: 2.2× (ReactorToday)
- M9: 1.6×
- M10: 2.3×
- M12: 3.4×
- M13: 1.7×

The Snapdragon X laptop is **faster than this Cloud PC** at every Mn — not
the result one might naively expect from "workstation x64." The reason is
that this machine is a Windows 365 Cloud PC where the EPYC host is shared
and the VM gets a slice; it is not a dedicated workstation. Both rows are
nonetheless valid x64-vs-ARM64 datapoints — the spec §15.6 emitter
intentionally keeps them in separate rows by `Architecture`, so the
comparison is apples-to-apples within an architecture only.

**Alloc-bytes parity.** Per-op allocation bytes match between the two
machines within a few percent on every Mn (e.g. M1 Today: x64 5,353,584 B
vs ARM64 5,353,584 B; M9 Today: ~624 MB both). This is expected — alloc is
architecture-independent given the same framework code path and rep
count. It also confirms the bench is measuring the same thing on both
machines.

**Architecture-specific gotchas** (per
[`perf-suite-runbook.md`](../perf-suite-runbook.md) §10): none recorded
yet at Phase-0 freeze. Add entries here as encountered.
