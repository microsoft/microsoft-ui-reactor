using Reactor.Tests.Shared;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>What the cleanup sweep should do with one existing registration.</summary>
internal enum RegistrationDisposition
{
    /// <summary>Not ours to touch. Notably includes every concurrently running checkout.</summary>
    Leave,

    /// <summary>
    /// Contends with the layout about to be registered. Removal must fail loudly, because
    /// registering on top of it is the silent-wrong-binary bug this guards against.
    /// </summary>
    RemoveContending,

    /// <summary>
    /// A derived registration whose worktree is gone. Housekeeping: reclaiming it must never
    /// fail a test run.
    /// </summary>
    ReclaimAbandoned,
}

/// <summary>What is known about a registration's recorded install path.</summary>
/// <remarks>
/// Tri-state because <c>Directory.Exists</c> is not: it answers <see langword="false"/> both for
/// a worktree that was deleted and for one that merely cannot be read right now — an offline
/// network share, a dismounted volume, a directory whose ACL excludes this account. Collapsing
/// those two into one boolean makes an unreadable path indistinguishable from an abandoned one,
/// and the action taken on "abandoned" is to unregister someone else's package.
/// </remarks>
internal enum LayoutPresence
{
    /// <summary>The directory is there.</summary>
    Present,

    /// <summary>The directory is provably gone: an ancestor was readable and did not contain it.</summary>
    Absent,

    /// <summary>Could not be established. Treated as <see cref="Present"/> by every rule.</summary>
    Unknown,
}

/// <summary>One existing registration, reduced to the fields the decision depends on.</summary>
/// <param name="Name">The package's <c>Id.Name</c>.</param>
/// <param name="InstalledPath">
/// The package's recorded install location, or <see langword="null"/> when it could not be
/// read.
/// </param>
internal readonly record struct RegistrationRecord(string Name, string? InstalledPath);

/// <summary>
/// The decision half of the packaged tier's registration cleanup, separated from the
/// <c>PackageManager</c> calls that act on it.
/// </summary>
/// <remarks>
/// <para>Split out because the rules are destructive and, in a normal run, unobservable: a
/// packaged run presents only its own clean layout, so nothing would notice if these predicates
/// started selecting a live sibling checkout or stopped reclaiming the cases they exist for.
/// Deciding is pure and total, so it can be tested against the situations that matter without
/// installing packages.</para>
/// <para>The rules, in the order they are evaluated:</para>
/// <list type="number">
/// <item><description>This layout's derived name — a registration left by an earlier run of
/// this same checkout.</description></item>
/// <item><description>Any package installed from this exact layout directory, whatever it is
/// called. Catches a registration made under the base name before identities were derived;
/// registering a second package over a directory another package already claims was observed
/// to kill the host mid-run.</description></item>
/// <item><description>Any package whose name this algorithm would have derived for its own
/// recorded install path, when that path is provably gone and no live run still holds that
/// layout's lock — a deleted worktree.</description>
/// </item>
/// </list>
/// <para><b>None of the three can reach a concurrently running checkout.</b> Rule 1 is keyed to
/// this directory's hash, rule 2 to this directory itself, and rule 3 requires both that the
/// directory be provably absent and that nobody holds its lock. A path that cannot be read is
/// not absent, and a run whose worktree was deleted underneath it still holds its lock, because
/// the lock lives under <c>%LOCALAPPDATA%</c> rather than in the worktree.</para>
/// </remarks>
internal static class RegistrationSelection
{
    /// <summary>Classifies one registration against the layout about to be registered.</summary>
    /// <param name="package">The existing registration. Assumed already filtered to this repo's publisher.</param>
    /// <param name="layoutDir">The layout directory about to be registered, canonicalized.</param>
    /// <param name="effectivePackageName">The derived name that layout will register under.</param>
    /// <param name="basePackageName">The undecorated name from <c>Package.appxmanifest</c>.</param>
    /// <param name="probeLayout">
    /// Presence probe for the package's recorded install path. Injected so the abandoned-worktree
    /// rule is testable without creating and deleting directories, and so the unreadable case can
    /// be exercised at all — it cannot be staged on a normal developer machine.
    /// </param>
    /// <param name="isLayoutLive">
    /// Reports whether a run still holds the lock for the package's recorded install path.
    /// Absence of the directory alone does not mean the run that registered it has finished:
    /// deleting a worktree out from under a live packaged run leaves the run, its registration,
    /// and its lock all intact.
    /// </param>
    /// <param name="supportedVersions">
    /// Algorithm versions to re-derive against, defaulting to
    /// <see cref="WorktreeIdentity.SupportedAlgorithmVersions"/>. Injected because only one
    /// version exists today: without it, a test for multi-version reclamation could not fail
    /// even if the loop were deleted, and the guarantee would go unmeasured until the first
    /// version bump — exactly when it is least convenient to discover it was never implemented.
    /// </param>
    internal static RegistrationDisposition Classify(
        RegistrationRecord package,
        string layoutDir,
        string effectivePackageName,
        string basePackageName,
        Func<string, LayoutPresence> probeLayout,
        Func<string, bool> isLayoutLive,
        IReadOnlyList<string>? supportedVersions = null)
    {
        ArgumentNullException.ThrowIfNull(probeLayout);
        ArgumentNullException.ThrowIfNull(isLayoutLive);

        var versions = supportedVersions ?? WorktreeIdentity.SupportedAlgorithmVersions;

        if (string.Equals(package.Name, effectivePackageName, StringComparison.Ordinal))
            return RegistrationDisposition.RemoveContending;

        if (WorktreeIdentity.IsSameDirectory(package.InstalledPath, layoutDir))
            return RegistrationDisposition.RemoveContending;

        // Requires *exact* ownership, not a name that merely has the derived shape. Matching the
        // shape alone would remove any same-publisher package called <base>.w<suffix> whose
        // directory happens to be gone, including one this derivation never produced.
        // Re-deriving from the package's own recorded install path settles it: only a name this
        // algorithm would have generated for that exact path qualifies. Derivation is pure
        // string work over a canonical path, so a missing directory is no obstacle — a path that
        // cannot be resolved is left verbatim, the same property the pinned golden vectors rely
        // on. If a link in the path resolved at registration time and has since disappeared, the
        // re-derivation differs and the package is left alone: fail-closed, and a leaked
        // registration is recoverable where someone else's live one is not.
        //
        // Re-derived across every supported algorithm version, not just the current one. A
        // version bump changes every derived name, so checking only the current version would
        // strand everything the previous version registered and quietly break the reclamation
        // guarantee WorktreeIdentity documents.
        if (package.InstalledPath is not null &&
            probeLayout(package.InstalledPath) == LayoutPresence.Absent &&
            !isLayoutLive(package.InstalledPath) &&
            versions.Any(version =>
                string.Equals(
                    package.Name,
                    WorktreeIdentity.DerivePackageName(basePackageName, package.InstalledPath, version),
                    StringComparison.Ordinal)))
        {
            return RegistrationDisposition.ReclaimAbandoned;
        }

        return RegistrationDisposition.Leave;
    }
}
