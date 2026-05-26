# Spec 047 §15.6 (a) — Absolute Comparison

Mean ns per op + alloc bytes, per variant. Columns are dashes when a variant has < min-reps repetitions. Architecture column distinguishes ARM64-native from x64-emulated runs (spec §15.5 — non-comparable across architectures).

| Bench | Arch | Direct ns | Today ns | V2 ns | Direct alloc | Today alloc | V2 alloc |
|---|---|---:|---:|---:|---:|---:|---:|
| M1 | Arm64 | 38524.0 | 73010.3 | 74645.9 | 3771903 | 5353536 | 5369818 |
| M11 | Arm64 | 163.1 | 42475.7 | 43181.2 | 40 | 1720356 | 1704934 |
| M2 | Arm64 | 111346.4 | 164493.8 | 165375.7 | 13368874 | 18247250 | 18999201 |
| M3 | Arm64 | 606029.7 | 637323.9 | 2100603.4 | 27354942 | 45721269 | 48098079 |
| M4 | Arm64 | 554867.1 | 170601.3 | 203859.6 | 5457188 | 9961582 | 12034111 |
| M5 | Arm64 | 115443.6 | 165702.8 | 224089.3 | 5194954 | 9976128 | 9946457 |
| M6 | Arm64 | 52595.2 | 55131.1 | 60999.8 | 3607192 | 4949346 | 4687800 |
| M7 | Arm64 | 2075572.5 | 22415.4 | 23787.0 | 122949290 | 779584 | 779584 |
| M8 | Arm64 | 9321.8 | 8779.8 | 8791.1 | 1019536 | 2123320 | 2123320 |
