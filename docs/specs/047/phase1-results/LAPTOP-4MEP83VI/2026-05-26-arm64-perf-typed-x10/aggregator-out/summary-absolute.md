# Spec 047 §15.6 (a) — Absolute Comparison

Mean ns per op + alloc bytes, per variant. Columns are dashes when a variant has < min-reps repetitions. Architecture column distinguishes ARM64-native from x64-emulated runs (spec §15.5 — non-comparable across architectures).

| Bench | Arch | Direct ns | Today ns | V2 ns | Direct alloc | Today alloc | V2 alloc |
|---|---|---:|---:|---:|---:|---:|---:|
| M11 | Arm64 | 183.5 | 42256.3 | 39525.2 | 40 | 1720356 | 2583163 |
| M4 | Arm64 | 33908.8 | 167163.5 | 342480.6 | 5096905 | 10224766 | 12654464 |
| M5 | Arm64 | 29238.0 | 137884.1 | 309866.0 | 5096744 | 10240343 | 12666938 |
