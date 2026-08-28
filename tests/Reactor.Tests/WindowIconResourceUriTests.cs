using Microsoft.UI.Reactor;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #1143 — <c>WindowIcon.TryResolveResourceUri</c> maps an <c>ms-appx:</c> asset URI
/// onto a real file under <see cref="AppContext.BaseDirectory"/>.
/// </summary>
/// <remarks>
/// <para>This exists because handing <c>AppWindow.SetIcon</c> an <c>ms-appx:</c> URI inside a
/// packaged app does not load the asset — it silently applies a default icon. Measured on
/// WinAppSDK 2.1 in a registered MSIX app: the URI form produced the same shared handle
/// (65579) in two separate packaged processes, while the path form produced distinct real
/// per-window handles.</para>
/// <para>The bug is invisible to the selftest suite because that host is <b>unpackaged</b>,
/// where <c>ms-appx:</c> happens to map to the executable directory and appears to work.
/// These tests therefore cover the mapping itself rather than the live window.</para>
/// <para>Shares the <c>AppBaseDirectoryAssets</c> collection with the <c>TitleBar</c>
/// icon-default tests: both create and delete asset files under
/// <see cref="AppContext.BaseDirectory"/>, so running them in parallel lets one class
/// delete a directory the other is mid-way through using.</para>
/// </remarks>
[Collection("AppBaseDirectoryAssets")]
public class WindowIconResourceUriTests
{
    private static string BesideApp(params string[] parts)
        => global::System.IO.Path.Join(
            new[] { AppContext.BaseDirectory }.Concat(parts).ToArray());

    /// <summary>Creates a real file under the base directory and deletes it afterwards.</summary>
    private sealed class TempAsset : IDisposable
    {
        public string Path { get; }
        public TempAsset(string relative)
        {
            Path = BesideApp(relative);
            global::System.IO.Directory.CreateDirectory(
                global::System.IO.Path.GetDirectoryName(Path)!);
            global::System.IO.File.WriteAllBytes(Path, new byte[] { 0, 0, 1, 0, 1, 0 });
        }
        public void Dispose()
        {
            try { global::System.IO.File.Delete(Path); }
            catch (global::System.IO.IOException ex)
            {
                // Best-effort cleanup: a locked file only affects a later run in the
                // same output directory, and failing here would mask the real assertion.
                global::System.Diagnostics.Debug.WriteLine(
                    $"[test] TempAsset could not delete '{Path}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                global::System.Diagnostics.Debug.WriteLine(
                    $"[test] TempAsset was denied deletion of '{Path}': {ex.Message}");
            }
        }
    }

    [Theory]
    [InlineData("ms-appx:///Assets/UriProbe.ico")]
    [InlineData("ms-appx://some-package-id/Assets/UriProbe.ico")]
    [InlineData("MS-APPX:///Assets/UriProbe.ico")]
    public void Appx_Uri_Maps_To_The_File_Beside_The_App(string uri)
    {
        using var asset = new TempAsset(@"Assets\UriProbe.ico");

        Assert.True(WindowIcon.TryResolveResourceUri(uri, out var resolved),
            $"'{uri}' should map onto {asset.Path}");
        Assert.Equal(asset.Path, resolved);

        // The whole point: nothing resembling the URI may survive, or SetIcon silently
        // applies a default icon in a packaged app.
        Assert.DoesNotContain("ms-appx", resolved, StringComparison.OrdinalIgnoreCase);
        Assert.True(global::System.IO.Path.IsPathRooted(resolved));
    }

    [Fact]
    public void Uri_Whose_Asset_Is_Absent_Is_Rejected()
    {
        // No file created. Reactor must not invent a path that does not exist, and must
        // not hand the raw URI to SetIcon either: in a packaged app that call succeeds
        // while applying a *default* icon, which would report success and suppress the
        // exe/convention fallback.
        const string uri = "ms-appx:///Assets/DefinitelyNotThere.ico";

        Assert.False(WindowIcon.TryResolveResourceUri(uri, out var resolved));
        Assert.Equal(uri, resolved);
    }

    [Theory]
    [InlineData("ms-resource:///Files/App.ico")]
    [InlineData(@"C:\Assets\App.ico")]
    [InlineData("Assets/App.ico")]
    [InlineData("")]
    public void Non_Appx_Sources_Are_Not_Rewritten(string source)
    {
        Assert.False(WindowIcon.TryResolveResourceUri(source, out var resolved));
        Assert.Equal(source, resolved);
    }

    [Theory]
    [InlineData("ms-appx:///Assets/../Assets/UriProbe.ico")]
    [InlineData(@"ms-appx:///Assets\..\Assets\UriProbe.ico")]
    public void Uri_That_Walks_Out_Of_The_Install_Root_Is_Rejected(string uri)
    {
        // Non-vacuous by construction: the asset genuinely exists and File.Exists
        // resolves ".." itself, so without the traversal guard each of these would map
        // successfully to a real file. A false here can only come from the guard.
        using var asset = new TempAsset(@"Assets\UriProbe.ico");
        Assert.True(global::System.IO.File.Exists(asset.Path),
            "precondition: the probe asset must exist, or this proves nothing");

        Assert.False(WindowIcon.TryResolveResourceUri(uri, out var resolved),
            $"'{uri}' steps out of the install root and must not be mapped");
        Assert.Equal(uri, resolved);
    }

    [Fact]
    public void Appx_Uri_Naming_No_Asset_Is_Rejected()
    {
        Assert.False(WindowIcon.TryResolveResourceUri("ms-appx:///", out _));
    }
}
