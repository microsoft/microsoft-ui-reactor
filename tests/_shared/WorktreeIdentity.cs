// Per-worktree MSIX identity derivation for the packaged test tier.
//
// Why this exists
// ---------------
// `tests/Reactor.PackagedTests` registers the packaged host's loose layout under the
// identity spelled in `Package.appxmanifest`. That identity is a *machine-global* name, and
// so is the `uap5:AppExecutionAlias` the tier launches through. With a fixed identity, two
// checkouts of this repo cannot run the packaged tier at the same time: whichever starts
// second removes the first one's live registration (registration is name-scoped, not
// path-scoped) and repoints the alias at its own build output, so the first run either dies
// or — worse — silently keeps testing the second checkout's binary.
//
// Deriving the identity from the layout directory makes the two runs disjoint: different
// checkouts produce different package names and different alias stubs, so neither can see
// or evict the other. Same checkout, same identity, stable across reruns.
//
// This mirrors the algorithm sketched in microsoft/winappCli#763 (`winapp run
// --unique-identity`). That feature would otherwise be the natural home for this, but its v1
// explicitly *refuses* manifests declaring `uap5:AppExecutionAlias` — which ours does, and
// which is load-bearing here because launching the alias stub is the only way to keep
// package identity while still inheriting stdout for the TAP stream.
//
// Linked (not duplicated) into every project that needs it, so the deployment side and the
// in-host guard can never disagree about what the effective identity is.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Reactor.Tests.Shared;

/// <summary>
/// Derives a deterministic, per-checkout MSIX package name and execution-alias file name
/// from the directory a loose layout is registered from.
/// </summary>
/// <remarks>
/// <para>The derivation is a pure function of the canonicalised layout directory, so the
/// deployment (which knows the directory it is about to register) and the host process
/// (which knows the directory it is running from) compute the same answer with no channel
/// between them. That is what lets the in-host identity guard stay an exact equality check
/// rather than degrading to a prefix match.</para>
/// <para><b>The algorithm is versioned, but only the seed is migratable.</b> Bumping
/// <see cref="AlgorithmVersion"/> changes every derived package family, which orphans existing
/// registrations and moves package-scoped app data; those registrations are still reclaimed by
/// the stale-layout sweep in the deployment once their layout directory is gone, because
/// <see cref="DeriveSuffix(string, string)"/> can reproduce any listed version's output by
/// re-running today's primitives over that version's seed.</para>
/// <para>Changing the <em>hash, alphabet, or suffix length</em> is a different and harder
/// change, and adding a version to <see cref="SupportedAlgorithmVersions"/> does not cover it.
/// Those primitives are constants applied to every version, so after such a change re-deriving
/// an old version reproduces the new output, never the name that version actually registered —
/// the sweep stops recognizing those registrations and strands them permanently. Preserve the
/// previous derivation alongside the new one, or accept the stranding deliberately and
/// document it. <c>Derivation_Matches_The_Pinned_Version1_Vectors</c> is what forces the
/// decision: any such change reddens it.</para>
/// </remarks>
internal static class WorktreeIdentity
{
    /// <summary>
    /// Version tag mixed into the hash input. Bump only with intent: it invalidates every
    /// previously derived identity.
    /// </summary>
    internal const string AlgorithmVersion = "1";

    /// <summary>
    /// Every algorithm version whose registrations this code still recognizes as its own,
    /// newest first.
    /// </summary>
    /// <remarks>
    /// <para>The cleanup sweep re-derives a package's expected name from its own recorded
    /// install path to decide whether it is one of ours. Re-deriving with only the current
    /// version would make that check fail for everything a previous version registered, so a
    /// version bump would strand those registrations permanently — exactly what the reclamation
    /// promise above says does not happen.</para>
    /// <para>When bumping <see cref="AlgorithmVersion"/>, prepend the new value and keep the old
    /// ones. Dropping a version is a deliberate decision to stop reclaiming its leftovers.</para>
    /// <para><b>This list migrates seed changes only.</b> A listed version is replayed by
    /// feeding its string to today's hash, alphabet, and suffix length, so it reproduces what
    /// that version emitted only while those primitives are unchanged. Changing them is not
    /// made migratable by adding an entry here; see the remarks on the type.</para>
    /// </remarks>
    internal static readonly string[] SupportedAlgorithmVersions = [AlgorithmVersion];

    /// <summary>Marker separating the base name from the derived suffix.</summary>
    /// <remarks>
    /// A literal <c>'w'</c> (for "worktree") keeps the suffix from starting with a digit and
    /// makes a derived name obvious at a glance in <c>Get-AppxPackage</c> output.
    /// </remarks>
    internal const char SuffixMarker = 'w';

    /// <summary>Number of encoded hash characters in the suffix.</summary>
    /// <remarks>
    /// 8 base32 characters is 40 bits. Collisions only matter between checkouts on one
    /// machine, so this is comfortably beyond what is needed while still leaving the derived
    /// name inside the MSIX length limit without truncating the base name.
    /// </remarks>
    internal const int SuffixHashLength = 8;

    /// <summary>Total length of a derived suffix, including <see cref="SuffixMarker"/>.</summary>
    internal const int SuffixLength = SuffixHashLength + 1;

    /// <summary>Maximum length of MSIX <c>Identity/@Name</c>.</summary>
    internal const int MaxPackageNameLength = 50;

    /// <summary>Minimum length of MSIX <c>Identity/@Name</c>.</summary>
    internal const int MinPackageNameLength = 3;

    /// <summary>
    /// Lowercase RFC 4648 base32 alphabet. Every character is an ASCII letter or digit, which
    /// MSIX <c>Identity/@Name</c> permits and which is also safe in a file name.
    /// </summary>
    private const string Base32Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    /// <summary>
    /// Normalises a directory path so that spellings which reach the same directory hash the
    /// same way.
    /// </summary>
    /// <remarks>
    /// <para>Absolutises, then asks Windows for the directory's <em>final</em> path, which
    /// collapses the alternate spellings the filesystem admits: symlinks and junctions in any
    /// component, mapped or <c>subst</c>'d drive letters, and the extended-length
    /// <c>\\?\</c> form all resolve to the one physical path. Trailing separators are stripped
    /// (<c>AppContext.BaseDirectory</c> has one, a joined layout path typically does not) and
    /// the result is lowercased — Windows paths are case-insensitive, so two spellings
    /// differing only in case are the same directory and must not derive two identities.</para>
    /// <para>8.3 short names are handled a step earlier and were already handled before this
    /// query existed: measured on Windows, <c>Path.GetFullPath</c> expands a short component
    /// of an existing path to its long form. <c>subst</c>'d drives are the counter-example that
    /// motivated going to the filesystem — <c>GetFullPath</c> returns <c>X:\bin\x64</c>
    /// unchanged, so without this two runs pointed at one layout through a drive mapping and
    /// through its real path would derive different identities, take different lock files, and
    /// then rewrite and register the same generated manifest concurrently.</para>
    /// <para>Final-path resolution needs a handle, so it only works on a directory that
    /// exists. When it does not — a path being derived before it is created, or a volume that
    /// refuses the query — the walk falls back to resolving links component by component,
    /// which covers the common junction case without a handle. The fallback is strictly
    /// weaker: a <c>subst</c> or <c>\\?\</c> spelling of a directory that does not yet exist
    /// still hashes differently from its physical form. That is acceptable only because a
    /// layout directory is always registered after it is built, so the path this is asked
    /// about at registration time exists by then.</para>
    /// <para><b>Known limitation: per-directory case sensitivity.</b> The lowercasing assumes
    /// the Windows default, where two spellings differing only in case are the same directory.
    /// Windows can enable case sensitivity per directory (<c>fsutil file setCaseSensitiveInfo</c>,
    /// which WSL sets), and under such a parent <c>Repo</c> and <c>repo</c> are two directories
    /// that derive one identity. Note this is only about the lowercasing and not about the
    /// filesystem query: measured here, <c>GetFinalPathNameByHandle</c> returns the path in its
    /// <em>on-disk</em> case whatever case it is asked with, so resolution alone already
    /// collapses case-insensitive spellings and already distinguishes case-sensitive ones. The
    /// lowercasing exists for the fallback above, where no handle is available and the spelling
    /// is whatever the caller wrote.</para>
    /// <para>Left as-is deliberately. Removing the lowercasing would invalidate every
    /// previously derived identity and so needs an algorithm version bump (see the class
    /// remarks), and the collision it would fix is already contained rather than dangerous:
    /// two such checkouts derive one name, and the second is refused by the ownership check in
    /// <c>RemoveExistingRegistrations</c> — which names a hash collision between two checkouts
    /// as one of its two causes and aborts with the conflicting install path — rather than
    /// evicting the first or running the wrong binary. The cost is that the two cannot run
    /// concurrently, which is the same cost as a genuine hash collision. Rename one of the
    /// directories to something other than a case variant.</para>
    /// </remarks>
    internal static string Canonicalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be a non-empty directory path.", nameof(path));

        var full = Path.GetFullPath(path);
        var resolved = FinalPath.TryResolve(full) ?? ResolveLinksInEveryComponent(full);
        return Path.TrimEndingDirectorySeparator(resolved).ToLowerInvariant();
    }

    /// <summary>
    /// Every canonical form one directory path could be reduced to, strongest first.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Canonicalize"/> is deliberately existence-<em>dependent</em>: it asks
    /// the filesystem what a path really points at, which is the only way to collapse a
    /// <c>subst</c>'d drive or an extended-length spelling onto the physical directory. The
    /// consequence is that the same input canonicalises one way while the directory exists and
    /// another way after it is gone, because the query needs a handle and the walk that
    /// replaces it cannot resolve what is no longer there.</para>
    /// <para>That is harmless for identity — a layout is registered after it is built, so
    /// derivation always happens against a live directory — but it is <b>not</b> harmless for
    /// the lock that arbitrates ownership of a registration. A run locks while its directory
    /// exists, so it holds the resolved form; a later run asking whether that registration may
    /// be reclaimed asks about a directory that has since been deleted, derives the weaker
    /// form, takes a <em>different</em> file, and is granted a lease over a live run.</para>
    /// <para>The answer is the one the cross-version lock set already uses: do not try to guess
    /// which spelling the other run chose, and instead take every name this layout could be
    /// known by. Locking the union is unconditionally fail-closed — an extra candidate can only
    /// cause a refusal, never a false grant — and for an ordinary path all three forms collapse
    /// to one, so nothing is locked that was not locked before.</para>
    /// </remarks>
    internal static IReadOnlyList<string> CandidateCanonicalForms(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be a non-empty directory path.", nameof(path));

        var full = Path.GetFullPath(path);

        // 1. What a run derives while the directory exists. 2. What a run derives once it is
        // gone but the links above it survive. 3. What is left when even those are gone.
        return new[]
            {
                Canonicalize(path),
                Normalize(ResolveLinksInEveryComponent(full)),
                Normalize(FinalPath.StripExtendedLengthPrefix(full)),
            }
            .Distinct(StringComparer.Ordinal)
            .ToList();

        static string Normalize(string value) =>
            Path.TrimEndingDirectorySeparator(value).ToLowerInvariant();
    }

    /// <summary>
    /// Walks a rooted path from the root down, replacing each component that is a symlink or
    /// junction with its final target.
    /// </summary>
    /// <remarks>
    /// The fallback for when a handle cannot be taken, so this is what canonicalises a path
    /// that does not exist yet. <c>Directory.ResolveLinkTarget</c> only reports a target when
    /// the path handed to it is itself a link, so calling it once on a full path leaves any
    /// linked parent unresolved. Resolving component by component is what makes the linked and
    /// physical spellings of one directory converge — a junction high up, such as a linked
    /// <c>C:\src</c>, is the realistic way a worktree acquires two spellings. Best-effort
    /// throughout: a component that cannot be inspected is kept verbatim, because an unreadable
    /// or not-yet-created path is not an error here.
    /// </remarks>
    private static string ResolveLinksInEveryComponent(string full)
    {
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return full;

        var components = full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        foreach (var component in components)
            current = ResolveOneComponent(current, component);

        return current;
    }

    /// <summary>
    /// Appends one component to an already-resolved parent, then resolves the result if it is
    /// itself a link.
    /// </summary>
    /// <remarks>
    /// Uses <c>Path.Join</c> rather than <c>Path.Combine</c>: <c>Combine</c> discards every
    /// earlier argument as soon as one looks rooted — measured, <c>Combine(@"C:\Users", "C:")</c>
    /// returns <c>"C:"</c> — which would silently throw away the parent this walk has already
    /// resolved. <c>Join</c> only ever concatenates.
    /// </remarks>
    private static string ResolveOneComponent(string parent, string component)
    {
        var candidate = Path.Join(parent, component);
        try
        {
            var resolved = Directory.ResolveLinkTarget(candidate, returnFinalTarget: true);
            return resolved is null ? candidate : Path.GetFullPath(resolved.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return candidate;
        }
    }

    /// <summary>
    /// True when two paths name the same directory once both are canonicalised.
    /// </summary>
    /// <remarks>
    /// <para>The single comparison used everywhere a recorded path is matched against a live
    /// one. Identity derivation hashes the <em>canonical</em> path, so anything that decides
    /// "is this registration mine?" has to canonicalise too — otherwise the two halves disagree
    /// for exactly the inputs <see cref="Canonicalize"/> exists to reconcile. A registration
    /// recorded through a junction and a run that reaches the same directory physically derive
    /// one identity but would fail a raw string comparison.</para>
    /// <para>Returns <see langword="false"/> rather than throwing for an empty or unusable
    /// path, because callers are asking a yes/no ownership question about data that came from
    /// outside the process.</para>
    /// </remarks>
    internal static bool IsSameDirectory(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            // Canonicalize lowercases, so an ordinal comparison is already case-insensitive.
            return string.Equals(Canonicalize(left), Canonicalize(right), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The derived suffix for a layout directory, without any separator — e.g. <c>w3ok5qta2</c>.
    /// </summary>
    internal static string DeriveSuffix(string layoutDirectory) =>
        DeriveSuffix(layoutDirectory, AlgorithmVersion);

    /// <summary>
    /// The derived suffix a given algorithm version produces for a layout directory.
    /// </summary>
    /// <remarks>
    /// The version is a parameter so the cleanup sweep can re-derive names produced by earlier
    /// versions. That is what makes the reclamation promise in the type remarks true: without
    /// it, bumping <see cref="AlgorithmVersion"/> would strand every registration the previous
    /// version made, because none of them could ever equal a current-version derivation.
    /// </remarks>
    internal static string DeriveSuffix(string layoutDirectory, string algorithmVersion)
    {
        ArgumentException.ThrowIfNullOrEmpty(algorithmVersion);

        return DeriveSuffixForCanonicalForm(Canonicalize(layoutDirectory), algorithmVersion);
    }

    /// <summary>
    /// The derived suffix for an already-canonical layout path.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="DeriveSuffix(string, string)"/> so a caller holding one of the
    /// forms <see cref="CandidateCanonicalForms"/> produced can hash <em>that</em> form. Feeding
    /// it back through <see cref="Canonicalize"/> would undo the distinction: while the
    /// directory still exists, every spelling resolves to the physical path again, the
    /// candidates collapse to one, and the lock set silently loses the extra names it exists to
    /// take. The hash input is unchanged, so this is the same value the pinned vectors record.
    /// </remarks>
    internal static string DeriveSuffixForCanonicalForm(
        string canonicalForm, string algorithmVersion)
    {
        ArgumentException.ThrowIfNullOrEmpty(algorithmVersion);
        ArgumentException.ThrowIfNullOrEmpty(canonicalForm);

        var seed = algorithmVersion + "\n" + canonicalForm;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return SuffixMarker + Base32(hash, SuffixHashLength);
    }

    /// <summary>
    /// The effective MSIX <c>Identity/@Name</c> for a layout directory, as
    /// <c>&lt;base&gt;.&lt;suffix&gt;</c> with the base truncated only if the limit requires it.
    /// </summary>
    internal static string DerivePackageName(string basePackageName, string layoutDirectory) =>
        DerivePackageName(basePackageName, layoutDirectory, AlgorithmVersion);

    /// <summary>
    /// The effective MSIX <c>Identity/@Name</c> a given algorithm version produces.
    /// </summary>
    internal static string DerivePackageName(
        string basePackageName, string layoutDirectory, string algorithmVersion)
    {
        if (string.IsNullOrWhiteSpace(basePackageName))
            throw new ArgumentException("Base package name must be non-empty.", nameof(basePackageName));

        var head = TryDeriveHead(basePackageName)
            ?? throw new ArgumentException(
                $"Base package name '{basePackageName}' cannot be truncated to a valid MSIX name " +
                $"alongside a {SuffixLength}-character derived suffix.", nameof(basePackageName));

        // ".": the separator between base and suffix. MSIX treats the name as dot-delimited
        // segments, so this keeps the derived name a well-formed sibling of the original
        // rather than a new top-level name.
        return head + "." + DeriveSuffix(layoutDirectory, algorithmVersion);
    }

    /// <summary>
    /// The exact leading segment <see cref="DerivePackageName"/> emits for a base name, or
    /// <see langword="null"/> when no valid one exists.
    /// </summary>
    /// <remarks>
    /// Single source of truth for the truncation rules, shared with <see cref="IsDerivedFrom"/>
    /// so recognition cannot drift from derivation. The budget is constant — the suffix is always
    /// <see cref="SuffixLength"/> characters — so this needs no layout directory.
    /// </remarks>
    private static string? TryDeriveHead(string basePackageName)
    {
        var budget = MaxPackageNameLength - (SuffixLength + 1);
        if (budget < MinPackageNameLength) return null;

        var head = basePackageName.Length <= budget ? basePackageName : basePackageName[..budget];

        // Truncation can land on a '.', which would produce ".." or a segment-less name.
        head = head.TrimEnd('.');
        return head.Length < MinPackageNameLength ? null : head;
    }

    /// <summary>
    /// The effective execution-alias file name for a layout directory, e.g.
    /// <c>reactor-packaged-test-host-w3ok5qta2.exe</c>.
    /// </summary>
    /// <remarks>
    /// The alias is created at a single fixed path under
    /// <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c>, so it must vary per checkout for the same
    /// reason the package name does — otherwise two registrations fight over one stub and
    /// Windows' duplicate-alias resolution decides which binary the tier actually launches.
    /// Hyphen-separated rather than dot-separated so the result keeps exactly one extension.
    /// </remarks>
    internal static string DeriveAliasExeName(string baseAliasExeName, string layoutDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseAliasExeName))
            throw new ArgumentException("Base alias name must be non-empty.", nameof(baseAliasExeName));

        var suffix = DeriveSuffix(layoutDirectory);
        var stem = Path.GetFileNameWithoutExtension(baseAliasExeName);
        var extension = Path.GetExtension(baseAliasExeName);

        if (string.IsNullOrEmpty(stem))
        {
            throw new ArgumentException(
                $"Base alias name '{baseAliasExeName}' has no file-name stem.", nameof(baseAliasExeName));
        }

        return stem + "-" + suffix + extension;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is <paramref name="basePackageName"/> itself or
    /// any name this type could derive from it.
    /// </summary>
    /// <remarks>
    /// Used to scope cleanup to registrations belonging to this repo without ever matching an
    /// unrelated package, and to let the deployment recognise an already-rewritten manifest.
    /// Shape-checked rather than merely prefix-checked, so a genuinely different package that
    /// happens to start with the same text is not mistaken for one of ours. The head is compared
    /// for <em>exact</em> equality against what <see cref="DerivePackageName"/> would emit: a
    /// shortened prefix such as <c>M.wabcdefgh</c> is not something this type can produce, and
    /// treating it as ours would let the abandoned-package sweep remove an unrelated package that
    /// merely shares a publisher.
    /// </remarks>
    internal static bool IsDerivedFrom(string candidate, string basePackageName)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(basePackageName))
            return false;
        if (string.Equals(candidate, basePackageName, StringComparison.Ordinal))
            return true;

        var dot = candidate.LastIndexOf('.');
        if (dot <= 0) return false;

        var head = candidate[..dot];
        var suffix = candidate[(dot + 1)..];

        // Exactly the head DerivePackageName emits — including its truncation, if the base is long.
        if (!string.Equals(head, TryDeriveHead(basePackageName), StringComparison.Ordinal)) return false;
        if (suffix.Length != SuffixLength) return false;
        if (suffix[0] != SuffixMarker) return false;

        for (var i = 1; i < suffix.Length; i++)
        {
            if (Base32Alphabet.IndexOf(suffix[i]) < 0) return false;
        }

        return true;
    }

    /// <summary>
    /// Encodes the leading bits of <paramref name="data"/> as <paramref name="length"/>
    /// lowercase base32 characters.
    /// </summary>
    private static string Base32(byte[] data, int length)
    {
        var sb = new StringBuilder(length);
        int buffer = 0, bits = 0, index = 0;

        while (sb.Length < length)
        {
            if (bits < 5)
            {
                if (index >= data.Length)
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Hash of {0} bytes cannot supply {1} base32 characters.",
                            data.Length, length));

                buffer = (buffer << 8) | data[index++];
                bits += 8;
            }

            bits -= 5;
            sb.Append(Base32Alphabet[(buffer >> bits) & 0x1F]);
        }

        return sb.ToString();
    }
}
