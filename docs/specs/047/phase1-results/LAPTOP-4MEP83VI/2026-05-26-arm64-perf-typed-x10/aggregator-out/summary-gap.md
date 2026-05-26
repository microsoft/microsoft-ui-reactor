# Spec 047 §15.6 (c) — WinUI Gap (V2 vs Direct)

Absolute overhead Reactor still adds on top of raw WinUI. One row per (bench, architecture).

| Bench | Arch | V2 ns | Direct ns | V2 - Direct ns | V2 alloc - Direct alloc |
|---|---|---:|---:|---:|---:|
| M11 | Arm64 | 39525.2 | 183.5 | +39341.7 | +2583123 |
| M4 | Arm64 | 342480.6 | 33908.8 | +308571.8 | +7557559 |
| M5 | Arm64 | 309866.0 | 29238.0 | +280628.1 | +7570194 |
