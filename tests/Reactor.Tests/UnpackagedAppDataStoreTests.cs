using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Persistence;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// spec 063 §4 / §8 — <see cref="UnpackagedAppDataStore"/>, the Windows App SDK
/// <c>ApplicationData.GetForUnpackaged()</c>-backed store.
/// </summary>
/// <remarks>
/// <para>These assertions deliberately avoid a bare write/read round-trip. Both this
/// store and <see cref="JsonFileStore"/> write the same JSON document shape, so a
/// round-trip passes identically whichever one ran and would prove nothing about the
/// adoption. Every test below pins the thing that actually differs: the <b>storage
/// location</b>.</para>
/// <para>The xUnit host is unpackaged, which is precisely the condition
/// <c>GetForUnpackaged</c> targets — no packaged tier needed.</para>
/// </remarks>
public sealed class UnpackagedAppDataStoreTests : IDisposable
{
    private readonly string _publisher;
    private const string Product = "ReactorPersistence";

    public UnpackagedAppDataStoreTests() =>
        _publisher = "ReactorTest" + Guid.NewGuid().ToString("N").Substring(0, 12);

    public void Dispose()
    {
        // The store writes under %LOCALAPPDATA%; the LocalSettings positive control in
        // Does_Not_Route_Window_Placement_Through_The_Roaming_Registry_Hive additionally
        // creates an HKCU key. Remove both so a test run leaves no residue.
        //
        // Both catches are narrowed to the failures cleanup can legitimately hit — a
        // transient lock or an ACL denial. A bare catch here would also swallow a real
        // defect (say, a malformed key path), and cleanup failures are reported rather
        // than discarded so a leaking test run is diagnosable instead of silent.
        try
        {
            var dir = global::System.IO.Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _publisher);
            if (global::System.IO.Directory.Exists(dir))
                global::System.IO.Directory.Delete(dir, recursive: true);
        }
        catch (global::System.Exception ex) when (ex is global::System.IO.IOException
                                                    or UnauthorizedAccessException)
        {
            global::System.Diagnostics.Debug.WriteLine(
                $"[test cleanup] app-data dir for '{_publisher}': {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            // Both roots: the store writes under the roaming key today, but the
            // positive control in Does_Not_Route_Window_Placement_Through_The_Roaming_
            // Registry_Hive is designed to trip onto the machine-local key once the
            // runtime carries the #6559 fix. Deleting only the roaming root would then
            // leak a key on every run of the very test that detects the fix.
            global::Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                @"SOFTWARE\" + _publisher, throwOnMissingSubKey: false);
            global::Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                @"SOFTWARE\Classes\Local Settings\Software\" + _publisher, throwOnMissingSubKey: false);
        }
        catch (global::System.Exception ex) when (ex is global::System.IO.IOException
                                                    or UnauthorizedAccessException
                                                    or global::System.Security.SecurityException)
        {
            global::System.Diagnostics.Debug.WriteLine(
                $"[test cleanup] registry key for '{_publisher}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string SdkLocalPath(string publisher, string product) =>
        global::Microsoft.Windows.Storage.ApplicationData.GetForUnpackaged(publisher, product).LocalPath;

    // ══════════════════════════════════════════════════════════════
    //  Location — the only thing that distinguishes this store.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Persists_Under_The_Sdk_Provided_AppData_Root_Not_The_Process_Name_Root()
    {
        var store = new UnpackagedAppDataStore(_publisher, Product);

        var sdkRoot = SdkLocalPath(_publisher, Product);

        // Positive control: the SDK root must actually mention the publisher/product we
        // asked for. If GetForUnpackaged ever returned a constant or empty path, the
        // StartsWith assertion below would degenerate into a tautology.
        Assert.Contains(_publisher, sdkRoot, StringComparison.Ordinal);
        Assert.Contains(Product, sdkRoot, StringComparison.Ordinal);

        Assert.StartsWith(sdkRoot, store.Path, StringComparison.OrdinalIgnoreCase);

        // ...and specifically NOT the bespoke process-name root JsonFileStore uses.
        // This is the assertion that reddens if the adoption is reverted to a
        // hand-rolled path: the two roots are genuinely different directories.
        Assert.NotEqual(
            global::System.IO.Path.GetDirectoryName(JsonFileStore.DefaultPath()),
            global::System.IO.Path.GetDirectoryName(store.Path),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Written_Bytes_Land_In_A_File_Beneath_The_Sdk_AppData_Root()
    {
        var store = new UnpackagedAppDataStore(_publisher, Product);
        var payload = new byte[] { 9, 8, 7, 250, 251, 0, 42 };

        store.Write("main", payload);

        // Resolve the expected location independently of the store rather than trusting
        // store.Path: a store that reported one path and wrote to another would satisfy
        // a self-consistent File.Exists(store.Path) check.
        var expected = global::System.IO.Path.Join(
            SdkLocalPath(_publisher, Product), "reactor-windows.json");

        // Assert on the filesystem, not just the store's own read-back: a store that
        // silently no-opped its write would still satisfy an API-level round-trip if it
        // cached in memory.
        Assert.True(
            global::System.IO.File.Exists(expected),
            $"Expected a persistence file at the SDK app-data root: {expected}");

        var onDisk = global::System.IO.File.ReadAllText(expected);
        Assert.Contains(Convert.ToBase64String(payload), onDisk, StringComparison.Ordinal);

        Assert.True(store.TryRead("main", out var read));
        Assert.Equal(payload, read);
    }

    [Fact]
    public void A_Second_Store_Over_The_Same_Publisher_Product_Sees_The_Same_Data()
    {
        // Pins that the root is derived from publisher/product rather than from
        // per-instance state — the property that makes this store stable across a
        // process rename, which the process-name-keyed JsonFileStore is not.
        new UnpackagedAppDataStore(_publisher, Product).Write("main", new byte[] { 1, 2, 3 });

        Assert.True(new UnpackagedAppDataStore(_publisher, Product).TryRead("main", out var read));
        Assert.Equal(new byte[] { 1, 2, 3 }, read);

        // Contrast case: the shared read above would also pass for any deterministic
        // singleton path (including JsonFileStore's process-name root), so on its own
        // it does not show the identity is doing the keying. A *different* product
        // must not see that data.
        var otherProduct = new UnpackagedAppDataStore(_publisher, Product + "Other");
        Assert.NotEqual(otherProduct.Path, new UnpackagedAppDataStore(_publisher, Product).Path);
        Assert.False(otherProduct.TryRead("main", out _));
    }

    // ══════════════════════════════════════════════════════════════
    //  The design decision, encoded as a guard (spec 063 §3.1 / §3.2 / §6 D1).
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Does_Not_Route_Window_Placement_Through_The_Roaming_Registry_Hive()
    {
        // On the Windows App Runtime this repo currently builds against,
        // GetForUnpackaged().LocalSettings opens HKCU\SOFTWARE\<publisher>\<product> —
        // a ROAMING hive — instead of the machine-local
        // HKCU\SOFTWARE\Classes\Local Settings\Software\... it is contracted to use
        // (WindowsAppSDK#6559). Window placement is monitor-topology dependent and must
        // not roam, so UnpackagedAppDataStore uses the LocalPath surface instead. This
        // test is what stops someone "simplifying" it onto LocalSettings.
        var roamingKey = @"SOFTWARE\" + _publisher;

        // ── Positive control ──────────────────────────────────────────────────────
        // A "the key isn't there" assertion is worthless unless the same probe can be
        // shown to find the key when the defect IS exercised. Drive LocalSettings
        // directly under a sibling product and confirm the roaming key appears.
        //
        // This control is ALSO the follow-up trigger for spec 063 §6 D1, which is why
        // it is phrased as an observable condition rather than a version number: it
        // fails the moment the loaded runtime starts writing to the machine-local hive,
        // whichever release that turns out to be. A trigger keyed to a version inferred
        // from release notes can rot; a trigger keyed to a measurement cannot.
        const string ControlProduct = "RoamingControl";
        global::Microsoft.Windows.Storage.ApplicationData
            .GetForUnpackaged(_publisher, ControlProduct)
            .LocalSettings.Values["control"] = "present";

        var controlSeen = global::Microsoft.Win32.Registry.CurrentUser
            .OpenSubKey(roamingKey + @"\" + ControlProduct) is not null;

        Assert.True(
            controlSeen,
            $"""
            Positive control failed: writing through GetForUnpackaged().LocalSettings did
            not create HKCU\{roamingKey}\{ControlProduct}.

            Two possibilities, and they are distinguishable — do not guess:

            (a) The probe is wrong. Check the machine-local path
                HKCU\SOFTWARE\Classes\Local Settings\Software\{_publisher}\{ControlProduct}.
                If the value is THERE, the runtime now behaves correctly (case b). If it
                is in NEITHER hive, the probe is broken and this is not a measurement.

            (b) The loaded Windows App Runtime has picked up the fix for
                WindowsAppSDK#6559 (RuntimeCompatibilityChange
                ApplicationData_GetForUnpackaged_LocalSettings).

                ** This is the follow-up trigger for spec 063 §6 D1. **

                Before acting on it, confirm WHICH runtime produced the result. This
                suite sets WindowsAppSDKSelfContained=true, so it loads the runtime
                bundled in its own output directory rather than the machine-wide
                package — verify with:

                    Process.GetCurrentProcess().Modules -> Microsoft.WindowsAppRuntime.dll -> FileName

                A path under the test project's bin\ means the result reflects the
                PINNED WindowsAppSDKVersion. A path under WindowsApps\ means it reflects
                whatever 2.x runtime is installed on this machine, which is NOT evidence
                about the pinned version (2.x services in place, so a newer runtime can
                satisfy an app built against an older SDK).

                The path only proves the runtime is bundled. To identify WHICH one —
                the number the fix boundary actually attaches to — hash the loaded
                Microsoft.WindowsAppRuntime.dll / Microsoft.Windows.Storage.Projection.dll
                and match them back to a Microsoft.WindowsAppSDK.Foundation package in
                the NuGet cache. The fix lives in Foundation, and the metapackage ->
                Foundation pairing is neither stable nor monotonic (2.2.0 -> 2.1.0,
                2.3.1 -> 2.3.5), so the metapackage version is two steps removed from
                the behaviour. See spec 063 §3.2.

                Once confirmed, revisit spec 063 §6 D1: LocalSettings becomes a viable
                surface, and making this store the unpackaged default becomes worth
                reconsidering — subject to the migration problem in §6 D2 and the
                deployment-variance problem in §3.2, neither of which this solves.
            """);

        // ── The actual assertion ──────────────────────────────────────────────────
        new UnpackagedAppDataStore(_publisher, Product).Write("main", new byte[] { 1, 2, 3 });

        Assert.Null(global::Microsoft.Win32.Registry.CurrentUser.OpenSubKey(roamingKey + @"\" + Product));
    }

    // ══════════════════════════════════════════════════════════════
    //  Authoring-error surface.
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("", "Prod")]
    [InlineData("Pub", "")]
    [InlineData(null, "Prod")]
    public void Rejects_Empty_Publisher_Or_Product(string? publisher, string? product) =>
        Assert.Throws<ArgumentException>(() => new UnpackagedAppDataStore(publisher!, product!));

    [Theory]
    [InlineData(@"..\..\Escape")]
    [InlineData(@"C:\Rooted")]
    public void Rejects_A_Publisher_The_Sdk_Considers_Invalid(string publisher)
    {
        // The SDK validates publisher/product itself and rejects traversal and rooted
        // values. Pinned so a future change that started sanitizing (or stopped
        // propagating the SDK's rejection) is visible rather than silently writing
        // outside %LOCALAPPDATA%.
        Assert.Throws<ArgumentException>(() => new UnpackagedAppDataStore(publisher, Product));
    }

    [Theory]
    [InlineData(@"..\..\Escape")]
    [InlineData(@"C:\Rooted")]
    public void Rejects_A_Product_The_Sdk_Considers_Invalid(string product)
    {
        // The `product` segment reaches the path join exactly as `publisher` does, so
        // it needs the same pin — the publisher-only version of this test left the
        // second half of the documented validation claim unverified.
        Assert.Throws<ArgumentException>(() => new UnpackagedAppDataStore(_publisher, product));
    }

    // ══════════════════════════════════════════════════════════════
    //  The conservative default (spec 063 §6 D2).
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Auto_Detection_Still_Picks_JsonFileStore_When_Unpackaged()
    {
        // Adopting the API deliberately did NOT change the default: switching stores
        // would silently strand every existing unpackaged app's saved layout, because
        // the two stores key their data differently. If someone flips the default in
        // ResolvePersistenceStore, this reddens.
        using (ReactorApp.UseWindowPersistenceStoreForTests(null))
        {
            var store = ReactorApp.ResolvePersistenceStore();
            Assert.IsType<JsonFileStore>(store);
        }
    }
}
