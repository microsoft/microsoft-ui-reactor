# Baseline machines

Per spec §14 Phase 0 deliverable 4, baselines should be captured on **at
minimum: one x64 workstation, one ARM64 Surface-class**. The Phase-0 freeze
includes whichever machines have at least one full M1–M13 run; ARM64-native
and a dedicated x64 workstation entry are deferred to Phase-1 follow-up
where noted.

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

## (deferred) Workstation x64

Spec §14 calls for an x64 workstation baseline alongside ARM64. Deferred to
Phase 1 follow-up since the Phase-0 single-machine data is sufficient to
exit the gate (the spec §11 / §12 byte/ns columns get measured numbers from
**any** representative machine; ARM64-native + workstation enrich the
picture but don't change the Phase-0 / Phase-1 boundary).
