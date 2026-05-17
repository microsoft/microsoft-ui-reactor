# Coverage tooling

Local helpers that wrap the canonical `dotnet-coverage` recipe from
`CONTRIBUTING.md § Code coverage`.

| Script | Purpose |
|---|---|
| `run-coverage.ps1` | Builds with portable PDBs, runs unit + selftest, merges into `coverage/merged.cobertura.xml`, prints overall line/branch %. |
| `report-gaps.ps1`  | Parses the merged cobertura output and prints a ranked hot-spot table; also writes `coverage/gap-report.md`. |

## Typical loop

```pwsh
# Full run (≈ 5–10 minutes depending on platform):
pwsh tools/coverage/run-coverage.ps1

# Hot-spot report:
pwsh tools/coverage/report-gaps.ps1

# Fast iteration while adding unit tests:
pwsh tools/coverage/run-coverage.ps1 -UnitOnly
```

Outputs land under `coverage/`, which is git-ignored.

The tracking document for the ongoing 85% uplift lives at
`docs/reports/coverage-uplift-85.md` — read it first if you're picking up
this work in a new session.
