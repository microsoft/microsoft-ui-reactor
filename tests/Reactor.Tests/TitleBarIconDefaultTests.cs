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
[Collection("AppBaseDirectoryAssets")]
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
        BestEffortDelete(() => global::System.IO.Directory.Delete(_root, recursive: true), _root);
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
            CleanUp(asset, created ? assets : null);
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
            var uri = Assert.IsType<Uri>(TitleBarIconDefault.BuildUriForTests(asset));

            Assert.Equal("ms-appx", uri.Scheme);
            Assert.Equal("ms-appx:///Assets/sub%20dir%231/AppIcon.ico", uri.OriginalString);
            // Separators survive as separators — an over-eager escape would collapse
            // the whole relative path into one name.
            Assert.Equal(3, uri.OriginalString["ms-appx:///".Length..].Split('/').Length);
        }
        finally
        {
            TitleBarIconDefault.SetBaseDirectoryForTests(_root);
            CleanUp(asset, nested, created ? assets : null);
        }
    }

    /// <summary>
    /// Best-effort cleanup of scratch files created under the test binary's own output
    /// directory. Deliberately non-throwing: this runs in a <c>finally</c>, so an
    /// exception here would replace the real assertion failure with a cleanup error and
    /// hide what actually broke. Directories are passed innermost-first, and <c>null</c>
    /// entries mean "this one already existed, leave it alone".
    /// </summary>
    private static void CleanUp(string file, params string?[] directories)
    {
        BestEffortDelete(() => global::System.IO.File.Delete(file), file);

        foreach (var dir in directories.Where(static d => d is not null))
            BestEffortDelete(() => global::System.IO.Directory.Delete(dir!), dir!);
    }

    /// <summary>
    /// Runs a scratch delete, reporting rather than throwing when the filesystem refuses.
    /// The two caught types are the ones a delete can legitimately raise here — a handle
    /// still open on the file, or a permission the test host lacks. Anything else is a
    /// real bug and propagates.
    /// </summary>
    private static void BestEffortDelete(Action delete, string target)
    {
        try
        {
            delete();
        }
        catch (global::System.IO.IOException ex)
        {
            // Reported, not swallowed silently: a leaked scratch path is worth seeing in
            // the log, but never worth failing (or masking) the test over.
            global::System.Diagnostics.Trace.WriteLine(
                $"[TitleBarIconDefaultTests] could not delete scratch '{target}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            global::System.Diagnostics.Trace.WriteLine(
                $"[TitleBarIconDefaultTests] not permitted to delete scratch '{target}': {ex.Message}");
        }
    }

    // ── Precedence, via the spec-level seam ─────────────────────────────────
    //
    // ResolveForSpec is the whole precedence rule minus the ambient window lookup,
    // so these run headlessly without staging a live window.

    [Fact]
    public void Spec_Without_Icon_Falls_Back_To_The_Convention_Asset()
    {
        var expected = WriteFile("Assets", "AppIcon.ico");
        TitleBarIconDefault.ResetForTests();

        var icon = Assert.IsType<ImageIconData>(TitleBarIconDefault.ResolveForSpec(new WindowSpec()));
        Assert.Equal(expected, icon.Source.LocalPath.Replace('/', IOPath.DirectorySeparatorChar));
    }

    [Fact]
    public void Declared_Spec_Icon_Wins_Over_The_Convention_Asset()
    {
        WriteFile("Assets", "AppIcon.ico");
        var declared = WriteFile("declared.ico");
        TitleBarIconDefault.ResetForTests();

        var icon = Assert.IsType<ImageIconData>(TitleBarIconDefault.ResolveForSpec(
            new WindowSpec { Icon = WindowIcon.FromPath(declared) }));

        Assert.Equal(declared, icon.Source.LocalPath.Replace('/', IOPath.DirectorySeparatorChar));
    }

    [Fact]
    public void A_Declared_Icon_That_Resolves_To_Nothing_Falls_Through_To_The_Convention()
    {
        // Mirrors ApplyChrome, where WindowIcon.Apply returning false hands off to the
        // convention/PE fallback rather than leaving the window with no icon at all.
        var convention = WriteFile("Assets", "AppIcon.ico");
        TitleBarIconDefault.ResetForTests();

        var icon = Assert.IsType<ImageIconData>(TitleBarIconDefault.ResolveForSpec(
            new WindowSpec { Icon = WindowIcon.FromPath(IOPath.Join(_root, "absent.ico")) }));

        Assert.Equal(convention, icon.Source.LocalPath.Replace('/', IOPath.DirectorySeparatorChar));
    }

    [Fact]
    public void An_Embedded_Window_Inherits_No_Icon()
    {
        // Positive control first: the same spec without Embed does resolve, so a null
        // below is the embed guard firing rather than the convention being unavailable.
        WriteFile("Assets", "AppIcon.ico");
        TitleBarIconDefault.ResetForTests();
        Assert.NotNull(TitleBarIconDefault.ResolveForSpec(new WindowSpec()));

        var embedded = new WindowSpec
        {
            Embed = new EmbedRequest(WindowEmbedStyle.Child, HostPid: 1234, InitialVisibility: true),
        };
        Assert.Null(TitleBarIconDefault.ResolveForSpec(embedded));
    }

    [Fact]
    public void An_Embedded_Window_Inherits_No_Icon_Even_With_A_Declared_One()
    {
        var declared = WriteFile("declared.ico");
        TitleBarIconDefault.ResetForTests();

        var embedded = new WindowSpec
        {
            Icon = WindowIcon.FromPath(declared),
            Embed = new EmbedRequest(WindowEmbedStyle.Child, HostPid: 1234, InitialVisibility: true),
        };
        Assert.Null(TitleBarIconDefault.ResolveForSpec(embedded));
    }

    [Fact]
    public void A_Null_Spec_Is_Not_An_Embed_And_Still_Resolves_The_Convention()
    {
        // A bare ReactorHost with no owning window.
        var expected = WriteFile("Assets", "AppIcon.ico");
        TitleBarIconDefault.ResetForTests();

        var icon = Assert.IsType<ImageIconData>(TitleBarIconDefault.ResolveForSpec(null));
        Assert.Equal(expected, icon.Source.LocalPath.Replace('/', IOPath.DirectorySeparatorChar));
    }

    [Fact]
    public void Alternating_Between_Two_Declared_Icons_Resolves_Each_Correctly()
    {
        // The declared-icon cache is a single slot keyed on the WindowIcon reference, so
        // two windows alternating renders evict each other. That must cost a re-probe,
        // never a wrong answer.
        var a = WriteFile("a.ico");
        var b = WriteFile("b.ico");
        TitleBarIconDefault.ResetForTests();
        var specA = new WindowSpec { Icon = WindowIcon.FromPath(a) };
        var specB = new WindowSpec { Icon = WindowIcon.FromPath(b) };

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(a, ((ImageIconData)TitleBarIconDefault.ResolveForSpec(specA)!)
                .Source.LocalPath.Replace('/', IOPath.DirectorySeparatorChar));
            Assert.Equal(b, ((ImageIconData)TitleBarIconDefault.ResolveForSpec(specB)!)
                .Source.LocalPath.Replace('/', IOPath.DirectorySeparatorChar));
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
    public void SuppressIcon_Wins_Over_A_Directly_Initialized_Icon()
    {
        // Both are public init properties, so a record initializer can set both. The
        // fluent pair normalizes the other field, but the contract on SuppressIcon says
        // it suppresses the icon entirely — so the contradictory record must honour it.
        var contradictory = new TitleBarElement("t")
        {
            Icon = new SymbolIconData("Home"),
            SuppressIcon = true,
        };

        Assert.Null(TitleBarIconDefault.Project(contradictory));
    }

    [Fact]
    public void Invalidating_Caches_Picks_Up_An_Asset_That_Appeared_After_A_Cached_Miss()
    {
        // The window re-probes the filesystem on every ApplyChrome. A cache that never
        // revalidates would let the caption show an icon the title bar does not, which is
        // exactly what sharing the resolver exists to prevent.
        TitleBarIconDefault.ResetForTests();
        Assert.Null(TitleBarIconDefault.ResolveDefault());

        WriteFile("Assets", "AppIcon.ico");

        // Still the cached miss until something invalidates.
        Assert.Null(TitleBarIconDefault.ResolveDefault());

        TitleBarIconDefault.ResetForTests();
        Assert.NotNull(TitleBarIconDefault.ResolveDefault());
    }

    [Fact]
    public void Invalidating_Caches_Drops_An_Asset_That_Was_Removed_After_A_Cached_Hit()
    {
        var asset = WriteFile("Assets", "AppIcon.ico");
        TitleBarIconDefault.ResetForTests();
        Assert.NotNull(TitleBarIconDefault.ResolveDefault());

        global::System.IO.File.Delete(asset);
        TitleBarIconDefault.ResetForTests();

        Assert.Null(TitleBarIconDefault.ResolveDefault());
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
    public void Default_Is_Value_Equal_Across_Calls_So_Apply_Skips_Redundant_Writes()
    {
        WriteFile("Assets", "AppIcon.ico");
        TitleBarIconDefault.ResetForTests();

        var first = TitleBarIconDefault.ResolveDefault();
        TitleBarIconDefault.ResetForTests();
        var second = TitleBarIconDefault.ResolveDefault();

        // Record value equality is what lets TitleBarIconDefault.Apply compare the new
        // projection against the one it last wrote and skip the write — and with it a
        // fresh BitmapImage decode — on every render after the first.
        Assert.NotNull(first);
        Assert.Equal(first, second);
    }
}
