<!--
Thanks for contributing to Reactor! A few notes before you open this PR:

- Link the issue or spec this PR addresses (Fixes #..., Implements docs/specs/0XX-...).
- Keep the change focused. Smaller, well-scoped PRs land faster.
- Include tests. See CONTRIBUTING.md for the unit / selftest / e2e split.
- Before pushing, reproduce the CI `Build solution` gate: `dotnet restore Reactor.slnx`, then
  `dotnet build Reactor.slnx --no-restore -c Release`. Then `dotnet test tests/Reactor.Tests`.
  (`-c Release` matters — `TreatWarningsAsErrors` is Release-only, so a green Debug build does
  not clear CI. Keep restore separate, as CI does: a combined `-c Release` restore additionally
  turns NuGet restore warnings into errors CI never sees. See CONTRIBUTING.md.)
- First-time contributors: the Microsoft CLA bot will comment automatically; sign once and you're set.
-->

## Summary

<!-- One or two sentences: what changes, and why. -->

## Linked issue / spec

<!-- Fixes #...  /  Implements docs/specs/0XX-...  /  Part of #... -->

## Test plan

<!-- Bulleted list of how this was verified. Examples:
- [ ] Added unit tests in tests/Reactor.Tests/...
- [ ] Added selftest fixture in tests/Reactor.AppTests.Host/SelfTest/Fixtures/...
- [ ] Ran `dotnet test tests/Reactor.Tests`
- [ ] Manually ran `dotnet run --project samples/Reactor.TestApp`
-->

## Risk / breaking changes

<!-- Any public-API change, behavior shift, or perf-sensitive path? Flag it here. -->
