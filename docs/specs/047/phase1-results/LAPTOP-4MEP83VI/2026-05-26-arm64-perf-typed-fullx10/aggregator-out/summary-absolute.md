# Spec 047 §15.6 (a) — Absolute Comparison

Mean ns per op + alloc bytes, per variant. Columns are dashes when a variant has < min-reps repetitions. Architecture column distinguishes ARM64-native from x64-emulated runs (spec §15.5 — non-comparable across architectures).

| Bench | Arch | Direct ns | Today ns | V2 ns | Direct alloc | Today alloc | V2 alloc |
|---|---|---:|---:|---:|---:|---:|---:|
| M1 | Arm64 | 31695.0 | 34695.6 | 34544.8 | 3771903 | 5353536 | 5369818 |
| M11 | Arm64 | 162.2 | 38234.0 | 51323.9 | 40 | 1720356 | 2579278 |
| M2 | Arm64 | 54174.5 | 83492.0 | 64945.8 | 13384983 | 19594281 | 19625976 |
| M3 | Arm64 | 272534.7 | 824168.3 | 869382.2 | 27493434 | 50680486 | 44108762 |
| M4 | Arm64 | 215482.7 | 133525.4 | 202838.8 | 5457151 | 9695845 | 12520734 |
| M5 | Arm64 | 96379.2 | 163099.3 | 206519.8 | 4932192 | 10238879 | 12000119 |
| M6 | Arm64 | 56226.9 | 72585.8 | 78581.9 | 4131490 | 4687800 | 4687800 |
| M7 | Arm64 | 2256479.0 | 22194.3 | 21998.0 | 122687136 | 779584 | 779584 |
| M8 | Arm64 | 9039.9 | 8570.4 | 8540.5 | 1019536 | 2123320 | 2647618 |
