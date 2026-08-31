using System.Threading.Tasks;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Asserts that <c>SelfTestFixtureRegistry.TierRequirements</c> only declares tiers for fixtures
/// that still exist (issue #1154).
///
/// <para><b>Why a stale declaration is dangerous rather than untidy.</b> Both tier-filtered
/// corpora are built by scanning <c>AllFixtures</c>, so a key naming a fixture that has been
/// deleted from that array is <i>invisible</i> to every other guard in the mechanism. The
/// unpackaged exclusion trailer simply drops from 3 to 2 — still satisfying
/// <c>SelfTestBatch.NotApplicableFixtures_AreExcludedFromThisTier</c>'s "at least one" — and the
/// packaged trailer stays 0, so both of those assertions pass while a fixture has silently
/// vanished from the corpus. Silent partial deletion is exactly the failure this mechanism exists
/// to make visible, and without this check it would walk straight through it.</para>
///
/// <para><b>Why it is a fixture rather than a wrapper test.</b> The registry is <c>internal</c> to
/// this Host, and both <c>Reactor.SelfTests</c> and <c>Reactor.PackagedTests</c> reference the
/// Host with <c>ReferenceOutputAssembly=false</c> — they drive it as a subprocess and never link
/// against it. A fixture is the only code that can read the map, and it has the useful property of
/// running in <i>both</i> tiers.</para>
/// </summary>
internal class TierDeclarationConsistencyFixture(Harness h) : SelfTestFixtureBase(h)
{
    /// <summary>Registered name; see <c>SelfTestFixtureRegistry.AllFixtures</c>.</summary>
    public const string FixtureName = "SelfTestRegistry_TierDeclarationsMatchCorpus";

    public override Task RunAsync()
    {
        var stale = SelfTestFixtureRegistry.StaleTierDeclarations();

        if (stale.Length > 0)
        {
            // Named individually rather than counted: the whole point is to say which declaration
            // outlived its fixture, because the reader's next question is "was that fixture
            // deleted on purpose, or renamed and half-updated?".
            Console.WriteLine(
                "# Stale TierRequirements keys (declared, but absent from AllFixtures): " +
                string.Join(", ", stale));
        }

        H.Check("TierDeclarations_AllNameALiveFixture", stale.Length == 0);

        // A second, independent reading rather than a restatement: the first check can only fail,
        // never confirm the map is doing anything. If TierRequirements were emptied, the check
        // above would still pass — vacuously — while the tier mechanism silently stopped
        // excluding anything. This asserts the map is non-empty, so "no stale keys" is a
        // statement about a live map rather than about nothing at all.
        H.Check("TierDeclarations_MapIsNotEmpty",
            SelfTestFixtureRegistry.DeclaredTierFixtureCount > 0);

        return Task.CompletedTask;
    }
}
