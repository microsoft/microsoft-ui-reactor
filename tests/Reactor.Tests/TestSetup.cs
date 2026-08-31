using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Initializes WinRT COM support and the Windows App SDK runtime before any tests run.
/// This enables tests to create WinUI types (SolidColorBrush, FontWeights, etc.)
/// without a full Application host.
/// </summary>
internal static class TestSetup
{
    [DllImport("Microsoft.WindowsAppRuntime.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsAppRuntime_EnsureIsLoaded();

    [ModuleInitializer]
    internal static void Initialize()
    {
        ApplyRequestedCulture();

        // Set base directory so the runtime DLL can be found
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);

        // Load the Windows App SDK runtime (registers WinUI activation factories)
        WindowsAppRuntime_EnsureIsLoaded();

        // Initialize COM wrappers for WinRT interop
        WinRT.ComWrappersSupport.InitializeComWrappers();
    }

    /// <summary>
    /// Issue #1159: CI only ever runs en-US, so culture-sensitive defects — a machine-readable
    /// string built with the current culture, or a test asserting an en-US literal against a
    /// deliberately culture-sensitive path — are invisible to it and surface only on a
    /// contributor's non-en-US machine. Setting <c>REACTOR_TESTS_CULTURE</c> (e.g. <c>nl-NL</c>)
    /// reproduces that locally without changing the OS locale.
    /// <para>
    /// This sets the *default* culture for the assembly, so a <c>[CulturedFact]</c> test — which
    /// pins the culture around its own invocation — still wins over it.
    /// </para>
    /// </summary>
    private static void ApplyRequestedCulture()
    {
        var requested = Environment.GetEnvironmentVariable("REACTOR_TESTS_CULTURE");
        if (string.IsNullOrWhiteSpace(requested))
            return;

        // Under invariant globalization every CultureInfo silently collapses to invariant
        // behaviour, so the run would look green while proving nothing. Refuse rather than
        // mislead.
        if (IsInvariantGlobalization())
        {
            throw new global::System.InvalidOperationException(
                $"REACTOR_TESTS_CULTURE='{requested}' cannot take effect: this process runs in " +
                "invariant globalization mode, where every culture formats identically. " +
                "Unset InvariantGlobalization / DOTNET_SYSTEM_GLOBALIZATION_INVARIANT, or unset " +
                "the REACTOR_TESTS_CULTURE environment variable.");
        }

        global::System.Globalization.CultureInfo culture;
        try
        {
            culture = new global::System.Globalization.CultureInfo(requested);
        }
        catch (global::System.Globalization.CultureNotFoundException ex)
        {
            // Failing loudly matters more than usual here: a typo'd name that was silently
            // ignored would leave the suite running en-US and report a meaningless pass.
            throw new global::System.InvalidOperationException(
                $"REACTOR_TESTS_CULTURE='{requested}' is not a valid culture name.", ex);
        }

        global::System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        global::System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    /// <summary>
    /// Reports whether the process is running under invariant globalization, mirroring the
    /// runtime's own resolution order (<c>GlobalizationMode.GetSwitchValue</c>): the
    /// <c>System.Globalization.Invariant</c> AppContext switch wins when it is present,
    /// otherwise the <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT</c> environment variable decides.
    /// </summary>
    /// <remarks>
    /// Checking only the AppContext switch is not enough, and the gap is worst exactly where
    /// this guard matters. Measured on this runtime: with the environment-variable form set,
    /// <c>AppContext.TryGetSwitch</c> reports <c>false</c>, so the guard would not fire — and
    /// then either <c>new CultureInfo("nl-NL")</c> throws, making this code blame a perfectly
    /// valid culture name, or, if <c>DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY=false</c>
    /// is also set, it *succeeds* and formats 1.5 as "1.50" — the whole suite then runs
    /// invariantly while believing it is under nl-NL, and reports the meaningless pass this
    /// guard exists to prevent.
    /// </remarks>
    private static bool IsInvariantGlobalization()
    {
        if (AppContext.TryGetSwitch("System.Globalization.Invariant", out bool configured))
            return configured;

        var env = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT");
        return env is not null
            && (env == "1"
                || env.Equals("true", global::System.StringComparison.OrdinalIgnoreCase));
    }
}
