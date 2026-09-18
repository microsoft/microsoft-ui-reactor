using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Reactor.Tests.Shared;
using Windows.Management.Deployment;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>
/// Registers the packaged host's loose MSIX layout and resolves the execution-alias stub
/// used to launch it.
/// </summary>
/// <remarks>
/// <para>Deliberately behind an interface. Registration and activation are the only part
/// of this tier that MSTest may one day own itself: <c>Microsoft.Testing.Extensions.PackagedApp</c>
/// gained register-and-activate-by-AUMID in testfx <c>main</c>, and when that ships,
/// swapping to it should touch this file and nothing else.</para>
/// <para>It is <b>not</b> a candidate for AUMID activation today even ignoring the
/// shipping question. <c>IApplicationActivationManager</c> starts the app through the
/// shell broker rather than as a child process, so no handles are inherited and stdout
/// cannot be redirected at all — the TAP stream would need a file or a named pipe. The
/// execution alias avoids that entirely: launching the stub is an ordinary
/// <c>CreateProcess</c>, so the child inherits stdout/stderr and argv while still running
/// with full package identity.</para>
/// </remarks>
internal interface IPackagedHostDeployment
{
    /// <summary>Registers the layout and returns the absolute path of the alias stub.</summary>
    string Register();

    /// <summary>Removes the registration. Safe to call when registration failed.</summary>
    void Unregister();
}

/// <summary>
/// Registers the build output in place with <see cref="PackageManager"/> using
/// <see cref="DeploymentOptions.DevelopmentMode"/>, which is what allows an unsigned
/// layout to be registered (Developer Mode, or sideloading, must be enabled).
/// </summary>
internal sealed class AppxLooseLayoutDeployment : IPackagedHostDeployment
{
    /// <summary>Must match <c>Identity/@Name</c> in the host's Package.appxmanifest.</summary>
    internal const string PackageName = "Microsoft.UI.Reactor.PackagedTests.Host";

    /// <summary>Must match <c>Identity/@Publisher</c> in the host's Package.appxmanifest.</summary>
    internal const string PackagePublisher = "CN=Microsoft.UI.Reactor.PackagedTests.Host";

    /// <summary>Must match the <c>uap5:ExecutionAlias</c> in the host's Package.appxmanifest.</summary>
    internal const string AliasExeName = "reactor-packaged-test-host.exe";

    private readonly string _layoutDir;
    private string? _registeredFullName;
    private string? _effectivePackageName;
    private string? _effectiveAliasExeName;

    internal AppxLooseLayoutDeployment(string layoutDir) => _layoutDir = layoutDir;

    /// <summary>
    /// The package name this deployment actually registers: <see cref="PackageName"/> made
    /// unique to the layout directory.
    /// </summary>
    /// <remarks>
    /// <para>The manifest's identity is a machine-global name, and so is the execution alias.
    /// Registering under the literal constant means two checkouts of this repo cannot run the
    /// packaged tier concurrently — registration is name-scoped, not path-scoped, so the
    /// second run evicts the first one's package and repoints the alias at its own build
    /// output. Deriving from the layout directory makes concurrent checkouts disjoint while
    /// keeping a single checkout's identity stable across reruns.</para>
    /// <para>Computed lazily rather than in the constructor so that constructing a deployment
    /// is never the thing that throws for a malformed path.</para>
    /// </remarks>
    internal string EffectivePackageName =>
        _effectivePackageName ??= WorktreeIdentity.DerivePackageName(PackageName, _layoutDir);

    /// <summary>The execution-alias file name this deployment expects to be created.</summary>
    /// <remarks>
    /// Derived for the same reason as <see cref="EffectivePackageName"/>, and necessarily so:
    /// aliases land at one fixed path under <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c>, so
    /// uniquifying only the package name would still leave two registrations fighting over a
    /// single stub with Windows deciding which binary the tier launches.
    /// </remarks>
    internal string EffectiveAliasExeName =>
        _effectiveAliasExeName ??= WorktreeIdentity.DeriveAliasExeName(AliasExeName, _layoutDir);

    /// <summary>
    /// The build hint shared by every "host not built" diagnostic, so the message is the same
    /// whether the host is missing at discovery time or at registration time.
    /// </summary>
    /// <remarks>
    /// Names the platform actually being searched for. The host coerces an unspecified
    /// platform to x64 while this resolver looks under the running process architecture, so on
    /// an ARM64 machine a hint hardcoded to x64 would send the reader to build the very layout
    /// that just failed to match.
    /// </remarks>
    internal static string BuildHint =>
        $"Build it with: dotnet build tests/Reactor.PackagedTests.Host -p:Platform={HostPlatform}";

    /// <summary>Platform folder segment this resolver expects the host to have built into.</summary>
    private static string HostPlatform => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "ARM64",
        _ => "x64",
    };

    /// <summary>
    /// Validates and absolutises a <c>REACTOR_PACKAGED_HOST_DIR</c> override.
    /// </summary>
    /// <remarks>
    /// <para>The result reaches <c>RegisterPackageAsync</c> as <c>new Uri(manifestPath)</c>,
    /// which requires an absolute path — so returning a relative override unchanged made a
    /// perfectly valid value fail for a reason with nothing to do with the layout.</para>
    /// <para>A relative value is tried against the working directory first (the ordinary
    /// convention), then against the repo root. The second attempt exists because the
    /// documented shape of this override is a repo-relative path like
    /// <c>tests/Reactor.PackagedTests.Host/bin/x64/Debug/…</c>, while the test host's working
    /// directory is its own binary folder — so the intuitive value would otherwise never
    /// resolve.</para>
    /// </remarks>
    internal static string ResolveOverrideDirectory(string value, string? repoRoot = null)
    {
        if (Directory.Exists(value)) return Path.GetFullPath(value);

        if (!Path.IsPathRooted(value) && repoRoot is not null)
        {
            var fromRepoRoot = Path.GetFullPath(Path.Join(repoRoot, value));
            if (Directory.Exists(fromRepoRoot)) return fromRepoRoot;
        }

        var attempts = Path.IsPathRooted(value) || repoRoot is null
            ? Path.GetFullPath(value)
            : $"{Path.GetFullPath(value)}\n  {Path.GetFullPath(Path.Join(repoRoot, value))}";

        throw new DirectoryNotFoundException(
            $"REACTOR_PACKAGED_HOST_DIR points at a path that does not exist: {value}\n" +
            $"Tried:\n  {attempts}");
    }

    /// <summary>Walks up from the running assembly for the <c>Reactor.slnx</c> sentinel.</summary>
    internal static string? TryFindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Join(dir, "Reactor.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir;
    }

    /// <summary>
    /// Configuration and target framework of the *shim*, stamped in by MSBuild.
    /// </summary>
    /// <remarks>
    /// The host's output path contains the configuration, so hardcoding <c>Debug</c> here
    /// would break <c>dotnet test … -c Release</c>: the host builds to
    /// <c>bin\x64\Release\…</c> while this looked in <c>bin\x64\Debug\…</c> and reported
    /// "packaged host not built" for a host that had just been built. Reading the value
    /// MSBuild used keeps the two in step for any configuration.
    /// </remarks>
    private static string MetadataOr(string key, string fallback) =>
        typeof(AppxLooseLayoutDeployment).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(a => string.Equals(a.Key, key, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(a.Value))
            .Select(a => a.Value!)
            .FirstOrDefault() ?? fallback;

    /// <summary>The build output directory holding AppxManifest.xml and the host binaries.</summary>
    internal static string ResolveLayoutDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("REACTOR_PACKAGED_HOST_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return ResolveOverrideDirectory(overrideDir, TryFindRepoRoot());

        var dir = TryFindRepoRoot();
        if (dir == null)
            throw new DirectoryNotFoundException("Could not find repo root (Reactor.slnx)");

        // The host coerces an empty/AnyCPU platform to x64 and declares only x64/ARM64, so
        // its bin path always carries a platform segment; the running process architecture
        // is the one that was built.
        var platform = HostPlatform;

        var configuration = MetadataOr("ReactorPackagedTestsConfiguration", "Debug");
        var tfm = MetadataOr("ReactorPackagedTestsTargetFramework", "net10.0-windows10.0.22621.0");

        // Path.Join rather than Path.Combine throughout this file: Combine silently
        // discards everything before a segment that happens to be rooted, so a bad
        // constant or a future refactor could turn a repo-relative lookup into an
        // absolute one somewhere else on disk. Join always concatenates.
        return Path.Join(dir, "tests", "Reactor.PackagedTests.Host", "bin", platform,
            configuration, tfm);
    }

    public string Register()
    {
        // Claimed before anything is removed or registered. Identity is per-layout, so two
        // runs of the *same* checkout still derive the same package name and alias — and the
        // registration sweep below would then tear down a sibling run's live package. The
        // cross-checkout case is solved by derivation; this is the case derivation cannot
        // solve, and it fails closed rather than silently destroying the other run.
        AcquireLayoutLock();

        // Absolute: this is handed to RegisterPackageAsync as `new Uri(...)`, which requires
        // an absolute path. ResolveLayoutDirectory already absolutises, and this keeps the
        // guarantee local for a caller that constructed the type with its own layout dir.
        var manifest = Path.GetFullPath(Path.Join(_layoutDir, "AppxManifest.xml"));
        if (!File.Exists(manifest))
        {
            throw new FileNotFoundException(
                $"Packaged host not built. Expected a generated AppxManifest.xml at: {manifest}\n" +
                BuildHint);
        }

        var manager = new PackageManager();

        // Drift guard, deliberately *before* registering. The constants must match the
        // manifest's Identity element, and nothing at compile time links them. Checking
        // afterwards would be too late: the package would already be registered under the
        // manifest's identity while every lookup here — including cleanup — searched for the
        // stale constants, leaking the registration and leaving the alias owned by a layout
        // no later run could remove.
        //
        // The name is checked against both spellings because this method rewrites the
        // generated manifest in place: a freshly built layout carries the base name, and a
        // layout registered earlier without an intervening rebuild already carries the
        // derived one. Any *third* value is real drift.
        var (manifestName, manifestPublisher) = ReadManifestIdentity(manifest);
        var nameIsExpected =
            string.Equals(manifestName, PackageName, StringComparison.Ordinal) ||
            string.Equals(manifestName, EffectivePackageName, StringComparison.Ordinal);

        if (!nameIsExpected ||
            !string.Equals(manifestPublisher, PackagePublisher, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The constants in {nameof(AppxLooseLayoutDeployment)} have drifted from the " +
                $"manifest's Identity element, so cleanup could not remove what registration " +
                $"would create.\nManifest: {manifest}\n" +
                $"  Name:      manifest '{manifestName}' vs constant '{PackageName}' " +
                $"(or derived '{EffectivePackageName}')\n" +
                $"  Publisher: manifest '{manifestPublisher}' vs constant '{PackagePublisher}'");
        }

        // Rewrite the *generated* manifest in the build output, not the source
        // Package.appxmanifest. The tier registers this layout in place, so the build output
        // is already the thing being deployed; rewriting here is idempotent and self-healing
        // (a rebuild regenerates the base name and this simply derives it again), and it
        // leaves the source manifest as the readable statement of the base identity that the
        // drift tests check against.
        //
        // The rewrite lands *after* the build produced any resources.pri, and resource maps
        // are keyed by package identity. That is safe only while the host ships no indexed
        // resources — it has none today, and PackagedResourceTripwireTests fails the moment
        // that stops being true, at which point the derived identity has to be applied before
        // PRI indexing rather than after it.
        ApplyDerivedIdentity(manifest);

        // Remove any registration left behind by an earlier run *of this layout* before
        // registering. Without this a stale registration — pointing at a since-rebuilt or
        // relocated directory — keeps owning the alias, and the tier would silently exercise
        // the wrong binary. Packaged_IdentityGuard also checks the install location for
        // exactly this reason, but failing fast here gives a far clearer diagnostic.
        RemoveExistingRegistrations(manager);

        var result = manager
            .RegisterPackageAsync(new Uri(manifest), null, DeploymentOptions.DevelopmentMode)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        if (result.ExtendedErrorCode != null)
        {
            throw new InvalidOperationException(
                $"Registering the packaged host failed (0x{result.ExtendedErrorCode.HResult:X8}): " +
                $"{result.ErrorText}\nManifest: {manifest}\n" +
                "Registering an unsigned loose layout requires Developer Mode (or sideloading).");
        }

        // Captured so cleanup can remove precisely what was registered rather than
        // re-deriving it from the constants.
        _registeredFullName = FindRegistration(manager)?.Id.FullName;

        if (_registeredFullName is null)
        {
            throw new InvalidOperationException(
                $"Registered '{manifest}' but no package named '{EffectivePackageName}' with " +
                $"publisher '{PackagePublisher}' was found afterwards.");
        }

        var alias = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", EffectiveAliasExeName);

        if (!File.Exists(alias))
        {
            throw new FileNotFoundException(
                $"Registration reported success but the execution alias was not created: {alias}\n" +
                "Check that Package.appxmanifest still declares the windows.appExecutionAlias " +
                $"extension with Alias=\"{AliasExeName}\".");
        }

        return alias;
    }

    /// <summary>
    /// Rewrites the generated manifest's <c>Identity/@Name</c> and every
    /// <c>uap5:ExecutionAlias/@Alias</c> to this layout's derived values.
    /// </summary>
    /// <remarks>
    /// Writes only when something actually changed, so a rerun against an already-rewritten
    /// layout leaves the file — and its timestamp — alone. Elements are matched by local name
    /// so a revision of the manifest's namespace URIs does not silently turn this into a
    /// no-op; an alias that stopped being rewritten would reintroduce exactly the collision
    /// this is here to remove.
    /// </remarks>
    private void ApplyDerivedIdentity(string manifestPath)
    {
        var document = XDocument.Load(manifestPath);
        var changed = false;

        var identity = document.Root?
            .Elements()
            .FirstOrDefault(e => e.Name.LocalName == "Identity");

        var nameAttribute = identity?.Attribute("Name");
        if (nameAttribute is null)
        {
            throw new InvalidOperationException(
                $"Generated manifest has no Identity/@Name to uniquify: {manifestPath}");
        }

        if (!string.Equals(nameAttribute.Value, EffectivePackageName, StringComparison.Ordinal))
        {
            nameAttribute.Value = EffectivePackageName;
            changed = true;
        }

        var aliases = document.Descendants()
            .Where(e => e.Name.LocalName == "ExecutionAlias")
            .Select(e => e.Attribute("Alias"))
            .OfType<XAttribute>()
            .ToList();

        if (aliases.Count == 0)
        {
            throw new InvalidOperationException(
                "Generated manifest declares no windows.appExecutionAlias. The packaged tier " +
                "launches the host through its alias stub because that is the only way to keep " +
                "package identity while inheriting stdout for the TAP stream.\n" +
                $"Manifest: {manifestPath}");
        }

        // Filter on the sequence rather than in the loop body. Note this is deliberately not
        // applied to `aliases` above: that list backs the count check, which asserts the manifest
        // declares an alias at all. Narrowing it to aliases *needing* a change would make an
        // already-derived manifest look like one declaring no alias, and throw on a re-deploy.
        foreach (var alias in aliases.Where(
            a => !string.Equals(a.Value, EffectiveAliasExeName, StringComparison.Ordinal)))
        {
            alias.Value = EffectiveAliasExeName;
            changed = true;
        }

        if (changed) document.Save(manifestPath);
    }

    public void Unregister()
    {
        var manager = new PackageManager();
        try
        {
            // Remove exactly what was registered first, then sweep this layout's derived
            // identity. The captured full name is what registration actually produced, so it
            // stays correct even if the constants below are edited mid-run; the sweep still
            // catches anything a previous run of *this* layout left behind. Neither can reach
            // another checkout's registration.
            if (_registeredFullName is not null) RemovePackage(manager, _registeredFullName);
            RemoveExistingRegistrations(manager);
        }
        catch (Exception ex) when (
            ex is COMException or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            // Cleanup is best-effort: a failure here must not mask the real test outcome.
            // It is still reported, because a leaked registration makes the *next* run
            // ambiguous. Scoped to the failures the deployment APIs actually produce —
            // COM/WinRT faults, the InvalidOperationException RemoveExistingRegistrations
            // itself throws, permission denials, and I/O — so a genuine defect in this
            // harness still surfaces instead of being swallowed by teardown.
            Console.WriteLine($"[Reactor.PackagedTests] Unregister failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            ReleaseLayoutLock();
        }
        _registeredFullName = null;
    }

    /// <summary>
    /// Cross-process claim on this layout's derived identity, held for the whole
    /// register/run/unregister lifetime.
    /// </summary>
    /// <remarks>
    /// Named for the derived suffix, so the claim covers exactly the runs that would compute
    /// the same package name and alias — which, after derivation, means concurrent runs of one
    /// checkout. Session-scoped (<c>Local\</c>) rather than <c>Global\</c>: registrations are
    /// per-user, so a different session's run cannot collide with this one and has no reason
    /// to be blocked by it.
    /// </remarks>
    private Mutex? _layoutLock;

    /// <summary>How long to wait for a sibling run to finish before giving up.</summary>
    /// <remarks>
    /// Generous, because the expected wait is a full packaged run: blocking is the friendly
    /// outcome, and a timeout is a diagnosis rather than a policy. Ten minutes comfortably
    /// exceeds the tier's own runtime while still failing rather than hanging a CI job forever.
    /// </remarks>
    private static readonly TimeSpan LayoutLockTimeout = TimeSpan.FromMinutes(10);

    private void AcquireLayoutLock()
    {
        // The derived suffix already encodes the canonical layout path; reusing it keeps the
        // lock name short, legal, and impossible to drift from the identity it protects.
        var name = @"Local\Reactor.PackagedTests." + WorktreeIdentity.DeriveSuffix(_layoutDir);
        var mutex = new Mutex(initiallyOwned: false, name);

        bool acquired;
        try
        {
            acquired = mutex.WaitOne(LayoutLockTimeout);
        }
        catch (AbandonedMutexException)
        {
            // A previous run died holding the claim. The registration it left behind is what
            // RemoveExistingRegistrations is for, so take ownership and continue.
            Console.WriteLine(
                "[Reactor.PackagedTests] Reclaimed the layout lock from a run that exited without releasing it.");
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            throw new InvalidOperationException(
                $"Another packaged test run is already using this layout ({_layoutDir}) and did " +
                $"not finish within {LayoutLockTimeout.TotalMinutes:0} minutes. Both runs derive the " +
                "same package identity, so continuing would unregister the other run's live host " +
                "mid-test. Run the tier once per checkout, or wait for the other run to finish.");
        }

        _layoutLock = mutex;
    }

    private void ReleaseLayoutLock()
    {
        if (_layoutLock is null) return;

        try { _layoutLock.ReleaseMutex(); }
        catch (ApplicationException)
        {
            // A Mutex has thread affinity and MSTest does not promise that assembly cleanup
            // runs on the thread that ran assembly init, so this can legitimately be the wrong
            // thread. Benign: the claim then lasts until this process exits, which is the same
            // lifetime the deployment has anyway. Mutex is still the right primitive precisely
            // because of that exit behaviour — Windows marks it abandoned so a crashed run
            // cannot deadlock the next one, whereas a named semaphore would leak its count
            // permanently.
        }
        finally
        {
            _layoutLock.Dispose();
            _layoutLock = null;
        }
    }

    /// <summary>Reads <c>Identity/@Name</c> and <c>Identity/@Publisher</c> from a manifest.</summary>
    /// <remarks>
    /// Matched by local name so the check does not break if the manifest's foundation
    /// namespace is revised.
    /// </remarks>
    internal static (string? Name, string? Publisher) ReadManifestIdentity(string manifestPath)
    {
        var identity = XDocument.Load(manifestPath).Root?
            .Elements()
            .FirstOrDefault(e => e.Name.LocalName == "Identity");

        return (identity?.Attribute("Name")?.Value, identity?.Attribute("Publisher")?.Value);
    }

    private Windows.ApplicationModel.Package? FindRegistration(PackageManager manager) =>
        manager.FindPackagesForUser(string.Empty, EffectivePackageName, PackagePublisher)
            .FirstOrDefault();

    /// <summary>
    /// Removes every registration that would contend with the one about to be created.
    /// </summary>
    /// <remarks>
    /// <para>Three rules, one enumeration, all of them narrowed to this repo's own publisher
    /// first so nothing unrelated is ever a candidate:</para>
    /// <list type="number">
    /// <item><description>This layout's <see cref="EffectivePackageName"/> — a registration
    /// left by an earlier run of this same checkout.</description></item>
    /// <item><description>Any package installed from this exact layout directory, whatever it
    /// is called. This is what catches a registration made under the base name before
    /// identities were derived: registering a second package over a directory another package
    /// already claims is not a clean state, and leaving it behind was observed to kill the
    /// host mid-run.</description></item>
    /// <item><description>Any derived-shaped package whose install directory is simply gone —
    /// a deleted worktree. Per-checkout identities trade one shared registration for one per
    /// layout directory, so without this they accumulate for exactly the audience this change
    /// is for: agents that create and destroy worktrees constantly.</description></item>
    /// </list>
    /// <para><b>None of the three can reach a concurrently running checkout.</b> Rule 1 is
    /// keyed to this directory's hash, rule 2 to this directory itself, and rule 3 only fires
    /// when a directory does not exist — which a live checkout's does by definition. That is
    /// the whole point: the previous sweep matched the shared base name and evicted whatever
    /// another checkout had just registered.</para>
    /// </remarks>
    private void RemoveExistingRegistrations(PackageManager manager)
    {
        var layout = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_layoutDir));

        List<Windows.ApplicationModel.Package> ours;
        try
        {
            ours = manager.FindPackagesForUser(string.Empty)
                .Where(p => string.Equals(p.Id.Publisher, PackagePublisher, StringComparison.Ordinal))
                .ToList();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            // Enumeration is the optional half of this. A registration under the derived name
            // still has to go, so fall back to the targeted lookup rather than silently
            // registering on top of a stale package.
            ours = manager
                .FindPackagesForUser(string.Empty, EffectivePackageName, PackagePublisher)
                .ToList();
        }

        foreach (var pkg in ours)
        {
            var installedPath = TryGetInstalledPath(pkg);

            var isThisLayoutsName =
                string.Equals(pkg.Id.Name, EffectivePackageName, StringComparison.Ordinal);

            var ownsThisLayout = WorktreeIdentity.IsSameDirectory(installedPath, layout);

            var isAbandoned =
                WorktreeIdentity.IsDerivedFrom(pkg.Id.Name, PackageName) &&
                installedPath is not null &&
                !Directory.Exists(installedPath);

            if (!isThisLayoutsName && !ownsThisLayout && !isAbandoned) continue;

            // Contention with this layout (rules 1 and 2) must fail loudly — registering on
            // top of it is the silent-wrong-binary bug this guards. Reclaiming an unrelated
            // dead worktree (rule 3) is housekeeping and must never fail a test run.
            if (isThisLayoutsName || ownsThisLayout)
            {
                RemovePackage(manager, pkg.Id.FullName);
                continue;
            }

            try
            {
                RemovePackage(manager, pkg.Id.FullName);
            }
            catch (Exception ex) when (
                ex is COMException or InvalidOperationException or UnauthorizedAccessException or IOException)
            {
                Console.WriteLine(
                    $"[Reactor.PackagedTests] Could not reclaim abandoned registration " +
                    $"'{pkg.Id.FullName}': {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Install location of a package, or <c>null</c> when it cannot be read.</summary>
    private static string? TryGetInstalledPath(Windows.ApplicationModel.Package package)
    {
        try
        {
            var path = package.InstalledPath;
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException)
        {
            // A package whose own location cannot be read is not one to reason about.
            return null;
        }
    }

    private static void RemovePackage(PackageManager manager, string fullName)
    {
        var removal = manager.RemovePackageAsync(fullName).AsTask().GetAwaiter().GetResult();
        if (removal.ExtendedErrorCode != null)
        {
            throw new InvalidOperationException(
                $"Removing package '{fullName}' failed " +
                $"(0x{removal.ExtendedErrorCode.HResult:X8}): {removal.ErrorText}");
        }
    }
}
