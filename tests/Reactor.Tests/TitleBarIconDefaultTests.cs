using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Xunit;
using IOPath = global::System.IO.Path;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// The path and URI half of the <c>TitleBar</c> icon default — the part that can be
/// measured without a XAML object. The projection itself (does the resulting
/// <c>IconSource</c> render?) lives in the <c>TitleBarIcon_*</c> self-test fixtures,
/// because constructing any <c>Microsoft.UI.Xaml</c> type headlessly throws
/// <c>COMException</c>.
/// </summary>
public class TitleBarIconDefaultTests : IDisposable
{
    private readonly string _root;

    public TitleBarIconDefaultTests()
    {
        _root = IOPath.Join(IOPath.GetTempPath(), "ReactorTBIcon_" + Guid.NewGuid().ToString("N"));
        global::System.IO.Directory.CreateDirectory(_root);
        TitleBarIconDefault.SetBaseDirectoryForTests(_root);
    }

    public void Dispose()
    {
        TitleBarIconDefault.SetBaseDirectoryForTests(null);
        try { global::System.IO.Directory.Delete(_root, recursive: true); }
        catch (global::System.IO.IOException) { /* best-effort scratch cleanup */ }
        catch (UnauthorizedAccessException) { /* ditto */ }
        global::System.GC.SuppressFinalize(this);
    }

    /// <summary>Writes a stand-in icon file and returns its full path.</summary>
    private string WriteFile(params string[] segments)
    {
        var path = IOPath.Join(new[] { _root }.Concat(segments).ToArray());
        global::System.IO.Directory.CreateDirectory(IOPath.GetDirectoryName(path)!);
        global::System.IO.File.WriteAllBytes(path, new byte[] { 0, 0, 1, 0 });
        return path;
    }

    // ── AppIconConvention ───────────────────────────────────────────────────

    [Fact]
    public void Convention_Finds_AppIcon_Under_Assets()
    {
        var expected = WriteFile("Assets", "AppIcon.ico");

        Assert.True(AppIconConvention.TryGetAssetPath(_root, out var path));
        Assert.Equal(expected, path);
    }

    [Fact]
    public void Convention_Misses_When_Asset_Absent()
    {
        // Same probe, same root, only the file differs — so a false here is a
        // measurement rather than a broken call. The positive control above shares
        // every input except the file's existence.
        Assert.False(AppIconConvention.TryGetAssetPath(_root, out _));
    }

    [Fact]
    public void Convention_Misses_When_Icon_Is_Outside_Assets_Directory()
    {
        WriteFile("AppIcon.ico");
        Assert.False(AppIconConvention.TryGetAssetPath(_root, out _));
    }

    [Fact]
    public void Convention_Misses_On_Malformed_Root_Without_Throwing()
    {
        Assert.False(AppIconConvention.TryGetAssetPath("\0not-a-path", out _));
    }

    // ── WindowIcon.TryResolvePath ───────────────────────────────────────────

    [Fact]
    public void WindowIcon_Resolves_Absolute_Path()
    {
        var expected = WriteFile("icon.ico");

        Assert.True(WindowIcon.FromPath(expected).TryResolvePath(out var path));
        Assert.Equal(expected, path);
    }

    [Fact]
    public void WindowIcon_Fails_On_Missing_File()
    {
        var missing = IOPath.Join(_root, "nope.ico");
        Assert.False(WindowIcon.FromPath(missing).TryResolvePath(out _));
    }

    [Fact]
    public void WindowIcon_Resolves_Relative_Path_Against_App_Base_Directory()
    {
        // Relative sources resolve against the app base directory, not the process
        // working directory — this is the real AppContext.BaseDirectory, which the
        // test-only convention override deliberately does not move.
        var beside = IOPath.Join(AppContext.BaseDirectory, "tbicon-rel-probe.ico");
        global::System.IO.File.WriteAllBytes(beside, new byte[] { 0, 0, 1, 0 });
        try
        {
            Assert.True(WindowIcon.FromPath("tbicon-rel-probe.ico").TryResolvePath(out var path));
            Assert.Equal(beside, path);
        }
        finally { global::System.IO.File.Delete(beside); }
    }

    [Fact]
    public void WindowIcon_Resolves_MsAppx_Uri_To_A_File_Under_The_App_Root()
    {
        var beside = IOPath.Join(AppContext.BaseDirectory, "tbicon-res-probe.ico");
        global::System.IO.File.WriteAllBytes(beside, new byte[] { 0, 0, 1, 0 });
        try
        {
            Assert.True(WindowIcon.FromResource("ms-appx:///tbicon-res-probe.ico")
                .TryResolvePath(out var path));
            Assert.Equal(beside, path);
        }
        finally { global::System.IO.File.Delete(beside); }
    }

    [Fact]
    public void WindowIcon_Fails_On_MsAppx_Uri_Naming_No_Asset()
    {
        Assert.False(WindowIcon.FromResource("ms-appx:///Assets/definitely-absent.ico")
            .TryResolvePath(out _));
    }

    [Theory]
    [InlineData("ms-appx:///../outside.ico")]
    [InlineData("ms-appx:///Assets/../../outside.ico")]
    [InlineData("ms-appx:///C:/Windows/win.ico")]
    public void WindowIcon_Rejects_Uris_That_Escape_The_App_Root(string uri)
    {
        Assert.False(WindowIcon.FromResource(uri).TryResolvePath(out _));
    }

    // ── URI projection ──────────────────────────────────────────────────────
    //
    // ResolveDefault() reads ReactorApp.ActiveHostInternal, which is null in a
    // headless run — so with no owning window it falls through to the convention
    // probe, which the fixture has pointed at a scratch root. That is exactly the
    // path these assertions want.

    [Fact]
    public void Default_Is_Null_When_No_Icon_Is_Resolvable()
    {
        Assert.Null(TitleBarIconDefault.ResolveDefault());
    }

    [Fact]
    public void Default_Uses_File_Uri_For_An_Icon_Outside_The_App_Root()
    {
        var expected = WriteFile("Assets", "AppIcon.ico");
        TitleBarIconDefault.ResetForTests();

        var icon = Assert.IsType<ImageIconData>(TitleBarIconDefault.ResolveDefault());
        Assert.Equal("file", icon.Source.Scheme);
        Assert.Equal(expected, icon.Source.LocalPath.Replace('/', IOPath.DirectorySeparatorChar));
    }

    [Fact]
    public void Default_Uses_MsAppx_Uri_For_An_Icon_Under_The_App_Root()
    {
        // The real package root, not the overridden probe root: "ms-appx:///x" names
        // a file the platform resolves under AppContext.BaseDirectory, and a test
        // cannot move that. Deriving the URI from a relocated root would emit one
        // naming a file that does not exist.
        var assets = IOPath.Join(AppContext.BaseDirectory, "Assets");
        var created = !global::System.IO.Directory.Exists(assets);
        global::System.IO.Directory.CreateDirectory(assets);
        var asset = IOPath.Join(assets, "AppIcon.ico");
        global::System.IO.File.WriteAllBytes(asset, new byte[] { 0, 0, 1, 0 });
        try
        {
            TitleBarIconDefault.SetBaseDirectoryForTests(AppContext.BaseDirectory);

            var icon = Assert.IsType<ImageIconData>(TitleBarIconDefault.ResolveDefault());
            Assert.Equal("ms-appx", icon.Source.Scheme);
            Assert.Equal("ms-appx:///Assets/AppIcon.ico", icon.Source.ToString());
        }
        finally
        {
            TitleBarIconDefault.SetBaseDirectoryForTests(_root);
            global::System.IO.File.Delete(asset);
            if (created) global::System.IO.Directory.Delete(assets);
        }
    }

    [Fact]
    public void Default_Percent_Escapes_Path_Segments_Without_Escaping_Separators()
    {
        var assets = IOPath.Join(AppContext.BaseDirectory, "Assets");
        var created = !global::System.IO.Directory.Exists(assets);
        global::System.IO.Directory.CreateDirectory(assets);

        // A '#' in a path segment would otherwise be read as a URI fragment
        // delimiter and silently truncate the resource name.
        var nested = IOPath.Join(assets, "sub dir#1");
        global::System.IO.Directory.CreateDirectory(nested);
        var asset = IOPath.Join(nested, "AppIcon.ico");
        global::System.IO.File.WriteAllBytes(asset, new byte[] { 0, 0, 1, 0 });
        try
        {
            TitleBarIconDefault.SetBaseDirectoryForTests(AppContext.BaseDirectory);
            var uri = TitleBarIconDefault.BuildUriForTests(asset);

            Assert.Equal("ms-appx", uri.Scheme);
            Assert.Equal("ms-appx:///Assets/sub%20dir%231/AppIcon.ico", uri.OriginalString);
            // Separators survive as separators — an over-eager escape would collapse
            // the whole relative path into one name.
            Assert.Equal(3, uri.OriginalString["ms-appx:///".Length..].Split('/').Length);
        }
        finally
        {
            TitleBarIconDefault.SetBaseDirectoryForTests(_root);
            global::System.IO.File.Delete(asset);
            global::System.IO.Directory.Delete(nested);
            if (created) global::System.IO.Directory.Delete(assets);
        }
    }

    // ── Element-level projection (no XAML objects involved) ─────────────────

    [Fact]
    public void Project_Prefers_An_Explicit_Icon_Over_The_Default()
    {
        WriteFile("Assets", "AppIcon.ico");
        TitleBarIconDefault.ResetForTests();
        var explicitIcon = new SymbolIconData("Home");

        var projected = TitleBarIconDefault.Project(
            new TitleBarElement("t") { Icon = explicitIcon });

        Assert.Same(explicitIcon, projected);
    }

    [Fact]
    public void Project_Returns_Null_When_The_Element_Opted_Out()
    {
        WriteFile("Assets", "AppIcon.ico");
        TitleBarIconDefault.ResetForTests();

        // Positive control: the same element without the opt-out does inherit, so a
        // null here is the opt-out working rather than the default being unavailable.
        Assert.NotNull(TitleBarIconDefault.Project(new TitleBarElement("t")));
        Assert.Null(TitleBarIconDefault.Project(new TitleBarElement("t") { SuppressIcon = true }));
    }

    [Fact]
    public void NoIcon_Clears_A_Previously_Declared_Icon()
    {
        var el = new TitleBarElement("t").Icon(new SymbolIconData("Home")).NoIcon();

        Assert.Null(el.Icon);
        Assert.True(el.SuppressIcon);
    }

    [Fact]
    public void Icon_Clears_A_Previous_Opt_Out()
    {
        var el = new TitleBarElement("t").NoIcon().Icon(new SymbolIconData("Home"));

        Assert.False(el.SuppressIcon);
        Assert.Equal(new SymbolIconData("Home"), el.Icon);
    }

    [Fact]
    public void Default_Is_Value_Equal_Across_Calls_So_The_Descriptor_Diff_Skips_Rewrites()
    {
        WriteFile("Assets", "AppIcon.ico");
        TitleBarIconDefault.ResetForTests();

        var first = TitleBarIconDefault.ResolveDefault();
        TitleBarIconDefault.ResetForTests();
        var second = TitleBarIconDefault.ResolveDefault();

        // Record value equality is what lets OneWay's diff skip the write, and with it
        // a fresh BitmapImage decode, on every render after the first.
        Assert.NotNull(first);
        Assert.Equal(first, second);
    }
}
