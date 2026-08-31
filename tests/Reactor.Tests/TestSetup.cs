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
    /// This sets the *default* culture for the assembly, so a <c>[UseCulture]</c> test — which
    /// assigns the thread's culture directly — still wins over it.
    /// </para>
    /// </summary>
    private static void ApplyRequestedCulture()
    {
        var requested = Environment.GetEnvironmentVariable("REACTOR_TESTS_CULTURE");
        if (string.IsNullOrWhiteSpace(requested))
            return;

        // Under invariant globalization every CultureInfo silently collapses to invariant
        // behaviour: `new CultureInfo("nl-NL")` succeeds and still formats 1.5 as "1.5". The run
        // would look green while proving nothing, so refuse rather than mislead.
        if (AppContext.TryGetSwitch("System.Globalization.Invariant", out bool invariant) && invariant)
        {
            throw new global::System.InvalidOperationException(
                $"REACTOR_TESTS_CULTURE='{requested}' cannot take effect: this process runs in " +
                "invariant globalization mode, where every culture formats identically. " +
                "Unset InvariantGlobalization or the environment variable.");
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
}
