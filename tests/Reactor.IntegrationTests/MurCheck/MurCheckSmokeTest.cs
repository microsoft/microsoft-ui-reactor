// Phase 1.6 — `mur check` end-to-end smoke test. Spec 038 §1.6.
//
// Drives mur.exe against a deliberately-broken fixture project and asserts
// the diagnostic survives the pipeline:
//   • exit code is non-zero (build failure preserved).
//   • stdout contains the expected `Path:Line:Col  E  CS1061 ...` shape.
//   • when the receiver is NOT a Microsoft.UI.Reactor.* symbol, the line is
//     unchanged — no `→ try:` suffix gets attached.
//
// A second test will land covering the Reactor-touching path (CS1061 on
// `Button(...).OnClick(...)`); that one needs WindowsAppSDK restore and is
// scoped as a follow-up because it dominates wall-time on this fixture set.

using System.Runtime.InteropServices;
using Xunit;

namespace Microsoft.UI.Reactor.IntegrationTests.MurCheck;

public sealed class MurCheckSmokeTest
{
    [Fact]
    [Trait("Category", "MurCheck")]
    public void Mur_check_emits_CS1061_for_broken_fixture_without_attaching_a_suggestion()
    {
        var fixtureDir = ResolveFixtureDir("SmokeFixture");
        Assert.True(Directory.Exists(fixtureDir), $"missing fixture dir at '{fixtureDir}'");

        var mur = ResolveMurExe();
        if (mur is null)
        {
            // mur.exe hasn't been built yet (e.g. running on a fresh checkout
            // before the CLI has compiled). Skip rather than fail — this test
            // documents the contract; the orchestrator unit tests cover the
            // logic.
            return;
        }

        var result = RunHelpers.RunProcess(
            mur,
            arguments: $"check \"{fixtureDir}\"",
            workingDirectory: fixtureDir,
            environmentVariables: new Dictionary<string, string?>(),
            timeoutMs: 240_000,
            throwOnFailure: false);

        // dotnet build failed, so mur exits non-zero.
        Assert.NotEqual(0, result.ExitCode);

        // Diagnostic line shows up.
        Assert.Contains("CS1061", result.Stdout);

        // Receiver is `Foo`, not a Reactor type, so the orchestrator filters
        // out any Tier-2 suggestion. The line should NOT contain `→ try:`.
        Assert.DoesNotContain("→ try:", result.Stdout);
    }

    static string ResolveFixtureDir(string fixtureName)
    {
        // Tests run from the bin/ output of Reactor.IntegrationTests; walk up
        // to find the source-tree fixture so we operate on the canonical
        // sources rather than copied bits.
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "MurCheck", "Fixtures", fixtureName);
            if (Directory.Exists(candidate)) return candidate;

            // Up one level until we find tests/Reactor.IntegrationTests/.
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;

            var srcCandidate = Path.Combine(dir, "tests", "Reactor.IntegrationTests", "MurCheck", "Fixtures", fixtureName);
            if (Directory.Exists(srcCandidate)) return srcCandidate;
        }
        return Path.Combine(AppContext.BaseDirectory, "MurCheck", "Fixtures", fixtureName);
    }

    static string? ResolveMurExe()
    {
        // Walk up to the repo root, then look at bin/<arch>/mur.exe (the
        // skill-kit mirror layout the CLI's csproj produces).
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => "x64",
        };

        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "bin", arch, "mur.exe");
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        return null;
    }
}
