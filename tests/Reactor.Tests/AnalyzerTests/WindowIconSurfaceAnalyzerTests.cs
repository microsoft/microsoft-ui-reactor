using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="WindowIconSurfaceAnalyzer"/> (<c>REACTOR_ICON_001</c>).
/// </summary>
/// <remarks>
/// <para>The negatives carry the weight here. A rule that fires on valid code is worse than no
/// rule, and this one keys off a support matrix in which most combinations are perfectly fine —
/// so the suite pins every <i>allowed</i> pairing as well as the rejected ones, plus the shapes
/// where the icon's kind is not knowable and the analyzer must stay silent.</para>
/// <para>Stubs mirror the real surfaces rather than referencing the framework: the analyzer keys
/// off fully-qualified type names and the <c>WindowIcon</c> factory signatures, so the stubs
/// reproduce those exactly, including a same-named decoy in another namespace.</para>
/// </remarks>
public class WindowIconSurfaceAnalyzerTests
{
    private const string Stubs = @"
namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit { }
}

namespace Microsoft.UI.Reactor
{
    public enum WindowIconKind { Path, Resource, Binary }

    public sealed class WindowIcon
    {
        public static WindowIcon FromPath(string path) => null;
        public static WindowIcon FromResource(string uri) => null;
        public static WindowIcon FromBytes(System.ReadOnlySpan<byte> data) => null;
        public static WindowIcon FromRgba(System.ReadOnlySpan<byte> pixels, int width, int height) => null;
    }

    public readonly struct WindowKey
    {
        public static WindowKey Of(string name) => default;
    }

    // HICON surfaces — reject Resource.
    public sealed record TrayIconSpec(WindowIcon Icon, string Tooltip, WindowKey? Key = null, bool IsVisible = true);

    public sealed record ThumbnailToolbarButton(string Id, WindowIcon Icon, string Tooltip, System.Action OnClick);

    public sealed class TaskbarOverlay
    {
        public WindowIcon Icon { get; set; }
        public string AccessibleDescription { get; set; }
    }

    // Path/Uri surfaces — reject Binary.
    public sealed class WindowSpec
    {
        public string Title { get; init; }
        public WindowIcon Icon { get; init; }
    }

    public sealed record JumpListItem(string Title, string Arguments, WindowIcon Icon = null)
    {
        public static JumpListItem ForUri(string title, string uri, string description = null, WindowIcon icon = null) => null;
    }

    public static class ReactorApp
    {
        // Mirrors the real signatures in src/Reactor/Hosting/ReactorApp.cs: `icon` sits after
        // width/height/fullScreen, and every documented call site passes it by name.
        public static void Run<TRoot>(
            string title = ""Reactor App"",
            double? width = null,
            double? height = null,
            bool fullScreen = false,
            WindowIcon icon = null) { }

        public static void Run(
            string title,
            System.Func<object> rootRender,
            double? width = null,
            double? height = null,
            bool fullScreen = false,
            WindowIcon icon = null) { }
    }
}

namespace Microsoft.UI.Reactor.Core
{
    public class RenderContext
    {
        public T UseMemo<T>(System.Func<T> factory) => default;
        public T UseMemo<T, T1>(System.Func<T> factory, T1 d1) => default;
    }

    public abstract class Component : RenderContext { }
}

namespace Other
{
    // Same simple names, different namespace — the fully-qualified guard must keep the analyzer
    // from firing on somebody else's TrayIconSpec/WindowIcon.
    public sealed class WindowIcon
    {
        public static WindowIcon FromResource(string uri) => null;
    }

    public sealed record TrayIconSpec(WindowIcon Icon, string Tooltip);
}
";

    private static CSharpAnalyzerTest<WindowIconSurfaceAnalyzer, DefaultVerifier> Analyzer(string source) =>
        new() { TestCode = Stubs + source };

    // ══════════════════════════════════════════════════════════════
    //  Positives — the kind the surface silently drops
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Fires_On_Resource_Icon_For_A_Tray_Icon()
    {
        // The exact mistake Reactor's own spec 036 carried in its worked example until #1185.
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M()
        {
            var spec = new TrayIconSpec(
                Icon: {|REACTOR_ICON_001:WindowIcon.FromResource(""ms-appx:///Assets/tray.ico"")|},
                Tooltip: ""My App"");
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Resource_Icon_For_A_Thumbnail_Toolbar_Button()
    {
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M()
        {
            var b = new ThumbnailToolbarButton(
                ""pause"",
                {|REACTOR_ICON_001:WindowIcon.FromResource(""ms-appx:///Assets/pause.ico"")|},
                ""Pause"",
                () => { });
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Resource_Icon_Assigned_To_The_Taskbar_Overlay()
    {
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M(TaskbarOverlay overlay)
        {
            overlay.Icon = {|REACTOR_ICON_001:WindowIcon.FromResource(""ms-appx:///Assets/badge.ico"")|};
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Binary_Icon_For_The_Window_Caption()
    {
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M(byte[] pixels)
        {
            var spec = new WindowSpec
            {
                Title = ""Demo"",
                Icon = {|REACTOR_ICON_001:WindowIcon.FromRgba(pixels, 16, 16)|},
            };
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Binary_Icon_For_The_Run_Icon_Argument()
    {
        // The named form is what every documented call site uses, so it is the one that has to
        // work; the positional form covers FindArgument's other branch.
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class Root { }

    class App
    {
        void Named(byte[] data)
        {
            ReactorApp.Run<Root>(""Demo"", icon: {|REACTOR_ICON_001:WindowIcon.FromBytes(data)|});
        }

        void Positional(byte[] data)
        {
            ReactorApp.Run<Root>(""Demo"", 800, 600, false, {|REACTOR_ICON_001:WindowIcon.FromBytes(data)|});
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Binary_Icon_For_A_Jump_List_Entry()
    {
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M(byte[] data)
        {
            var item = JumpListItem.ForUri(""Open"", ""app://open"", null, {|REACTOR_ICON_001:WindowIcon.FromBytes(data)|});
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_Through_A_Local_Assigned_Once()
    {
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M()
        {
            var icon = WindowIcon.FromResource(""ms-appx:///Assets/tray.ico"");
            var spec = new TrayIconSpec(Icon: {|REACTOR_ICON_001:icon|}, Tooltip: ""My App"");
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_Through_The_Documented_UseMemo_Idiom()
    {
        // `var icon = UseMemo(() => WindowIcon.FromX(...))` is the shape the windowing guide
        // documents, so a rule that could not see through it would miss the common case.
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;
    using Microsoft.UI.Reactor.Core;

    class App : Component
    {
        void M()
        {
            var icon = UseMemo(() => WindowIcon.FromResource(""ms-appx:///Assets/tray.ico""));
            var spec = new TrayIconSpec(Icon: {|REACTOR_ICON_001:icon|}, Tooltip: ""My App"");
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    // ══════════════════════════════════════════════════════════════
    //  Negatives — every allowed pairing stays silent
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Silent_On_Every_Supported_Pairing()
    {
        // The support matrix, as code. If any of these ever reddens, the rule has started
        // flagging valid Reactor and must be reverted before it reaches an author.
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M(byte[] data, byte[] pixels, TaskbarOverlay overlay)
        {
            // HICON surfaces take a path or binary data.
            var t1 = new TrayIconSpec(Icon: WindowIcon.FromPath(""tray.ico""), Tooltip: ""t"");
            var t2 = new TrayIconSpec(Icon: WindowIcon.FromBytes(data), Tooltip: ""t"");
            var t3 = new TrayIconSpec(Icon: WindowIcon.FromRgba(pixels, 16, 16), Tooltip: ""t"");

            var b1 = new ThumbnailToolbarButton(""a"", WindowIcon.FromPath(""a.ico""), ""A"", () => { });
            var b2 = new ThumbnailToolbarButton(""b"", WindowIcon.FromRgba(pixels, 16, 16), ""B"", () => { });

            overlay.Icon = WindowIcon.FromPath(""badge.ico"");
            overlay.Icon = WindowIcon.FromBytes(data);

            // The window caption takes a path or a packaged resource.
            var w1 = new WindowSpec { Icon = WindowIcon.FromPath(""AppIcon.ico"") };
            var w2 = new WindowSpec { Icon = WindowIcon.FromResource(""ms-appx:///Assets/AppIcon.ico"") };
            ReactorApp.Run<Root>(""Demo"", icon: WindowIcon.FromPath(""AppIcon.ico""));
            ReactorApp.Run<Root>(""Demo"", icon: WindowIcon.FromResource(""ms-appx:///Assets/AppIcon.ico""));
        }
    }

    class Root { }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Silent_On_Jump_List_Path_And_Resource_Icons()
    {
        // Which of the two a jump list honours depends on package identity — a runtime property.
        // Reporting either would be a guess, so neither is reported.
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M()
        {
            var a = new JumpListItem(""Open"", ""app://open"", WindowIcon.FromPath(""open.ico""));
            var b = new JumpListItem(""Open"", ""app://open"", WindowIcon.FromResource(""ms-appx:///Assets/open.ico""));
            var c = JumpListItem.ForUri(""Open"", ""app://open"", null, WindowIcon.FromPath(""open.ico""));
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Silent_When_The_Kind_Depends_On_A_Condition()
    {
        // The correct way to write an identity-aware icon. Reporting either arm would punish
        // exactly the author who got it right.
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M(bool packaged)
        {
            var icon = packaged
                ? WindowIcon.FromResource(""ms-appx:///Assets/tray.ico"")
                : WindowIcon.FromPath(""tray.ico"");
            var spec = new TrayIconSpec(Icon: icon, Tooltip: ""t"");
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Silent_When_The_Local_Is_Reassigned()
    {
        // The declaration says Resource, but the value that reaches the surface does not.
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M(bool packaged)
        {
            var icon = WindowIcon.FromResource(""ms-appx:///Assets/tray.ico"");
            if (!packaged) icon = WindowIcon.FromPath(""tray.ico"");
            var spec = new TrayIconSpec(Icon: icon, Tooltip: ""t"");
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Silent_When_The_Icon_Comes_From_A_Parameter_Or_Field_Or_Method()
    {
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        private WindowIcon _field = WindowIcon.FromResource(""ms-appx:///Assets/tray.ico"");

        WindowIcon Make() => WindowIcon.FromResource(""ms-appx:///Assets/tray.ico"");

        void M(WindowIcon fromCaller)
        {
            var a = new TrayIconSpec(Icon: fromCaller, Tooltip: ""t"");
            var b = new TrayIconSpec(Icon: _field, Tooltip: ""t"");
            var c = new TrayIconSpec(Icon: Make(), Tooltip: ""t"");
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Silent_On_A_Same_Named_Type_In_Another_Namespace()
    {
        var source = @"
namespace TestApp
{
    class App
    {
        void M()
        {
            var spec = new Other.TrayIconSpec(
                Other.WindowIcon.FromResource(""ms-appx:///Assets/tray.ico""),
                ""t"");
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Silent_When_A_Block_Bodied_Memo_Could_Return_Either_Kind()
    {
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;
    using Microsoft.UI.Reactor.Core;

    class App : Component
    {
        void M(bool packaged)
        {
            var icon = UseMemo(() =>
            {
                if (packaged) return WindowIcon.FromResource(""ms-appx:///Assets/tray.ico"");
                return WindowIcon.FromPath(""tray.ico"");
            });
            var spec = new TrayIconSpec(Icon: icon, Tooltip: ""t"");
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Silent_When_No_Icon_Is_Supplied()
    {
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M()
        {
            var w = new WindowSpec { Title = ""Demo"" };
            var j = new JumpListItem(""Open"", ""app://open"");
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }
}
