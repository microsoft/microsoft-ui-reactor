# Spec 047 §15.6 (a) — Absolute Comparison

Mean ns per op + alloc bytes, per variant. Columns are dashes when a variant has < min-reps repetitions. Architecture column distinguishes ARM64-native from x64-emulated runs (spec §15.5 — non-comparable across architectures).

| Bench | Arch | Direct ns | Today ns | V2 ns | Direct alloc | Today alloc | V2 alloc |
|---|---|---:|---:|---:|---:|---:|---:|
| M4 | Arm64 | 29826.3 | 83554.6 | 157826.3 | 5096877 | 10267454 | 10213652 |
| M5 | Arm64 | 58055.2 | 258223.8 | 292060.0 | 5096744 | 10212098 | 10252936 |
