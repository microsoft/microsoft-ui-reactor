# Spec 047 §15.6 (c) — WinUI Gap (V2 vs Direct)

Absolute overhead Reactor still adds on top of raw WinUI. One row per (bench, architecture).

| Bench | Arch | V2 ns | Direct ns | V2 - Direct ns | V2 alloc - Direct alloc |
|---|---|---:|---:|---:|---:|
| M4 | Arm64 | 157826.3 | 29826.3 | +128000.0 | +5116775 |
| M5 | Arm64 | 292060.0 | 58055.2 | +234004.8 | +5156192 |
