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
/// </remarks>
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
            catch (global::System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
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
    public void Uri_Whose_Asset_Is_Absent_Is_Left_For_The_Platform()
    {
        // No file created — Reactor must not invent a path that does not exist; the
        // caller passes the original URI through instead.
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

    [Fact]
    public void Appx_Uri_Naming_No_Asset_Is_Rejected()
    {
        Assert.False(WindowIcon.TryResolveResourceUri("ms-appx:///", out _));
    }
}
