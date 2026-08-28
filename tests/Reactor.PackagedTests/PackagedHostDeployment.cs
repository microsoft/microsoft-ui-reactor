using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Windows.Management.Deployment;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>
/// Registers the packaged host's loose MSIX layout and resolves the execution-alias stub
/// used to launch it (issue #1148).
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

    internal AppxLooseLayoutDeployment(string layoutDir) => _layoutDir = layoutDir;

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

        // Drift guard, deliberately *before* registering. PackageName/PackagePublisher must
        // match the manifest's Identity element, and nothing at compile time links them.
        // Checking afterwards would be too late: the package would already be registered
        // under the manifest's identity while every lookup here — including cleanup —
        // searched for the stale constants, leaking the registration and leaving the alias
        // owned by a layout no later run could remove.
        var (manifestName, manifestPublisher) = ReadManifestIdentity(manifest);
        if (!string.Equals(manifestName, PackageName, StringComparison.Ordinal) ||
            !string.Equals(manifestPublisher, PackagePublisher, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The constants in {nameof(AppxLooseLayoutDeployment)} have drifted from the " +
                $"manifest's Identity element, so cleanup could not remove what registration " +
                $"would create.\nManifest: {manifest}\n" +
                $"  Name:      manifest '{manifestName}' vs constant '{PackageName}'\n" +
                $"  Publisher: manifest '{manifestPublisher}' vs constant '{PackagePublisher}'");
        }

        // Remove any registration left behind by an earlier run before registering this
        // one. Without this a stale layout — pointing at a different build directory —
        // keeps owning the alias, and the tier would silently exercise the wrong binary.
        // Packaged_IdentityGuard also checks the install location for exactly this
        // reason, but failing fast here gives a far clearer diagnostic.
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
                $"Registered '{manifest}' but no package named '{PackageName}' with publisher " +
                $"'{PackagePublisher}' was found afterwards.");
        }

        var alias = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", AliasExeName);

        if (!File.Exists(alias))
        {
            throw new FileNotFoundException(
                $"Registration reported success but the execution alias was not created: {alias}\n" +
                "Check that Package.appxmanifest still declares the windows.appExecutionAlias " +
                $"extension with Alias=\"{AliasExeName}\".");
        }

        return alias;
    }

    public void Unregister()
    {
        var manager = new PackageManager();
        try
        {
            // Remove exactly what was registered first, then sweep by identity. The
            // captured full name is what registration actually produced, so it stays
            // correct even if the constants below are edited mid-run; the sweep still
            // catches anything a previous run left behind.
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
        _registeredFullName = null;
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

    private static Windows.ApplicationModel.Package? FindRegistration(PackageManager manager) =>
        manager.FindPackagesForUser(string.Empty, PackageName, PackagePublisher).FirstOrDefault();

    private static void RemoveExistingRegistrations(PackageManager manager)
    {
        foreach (var pkg in manager.FindPackagesForUser(string.Empty, PackageName, PackagePublisher).ToList())
            RemovePackage(manager, pkg.Id.FullName);
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
