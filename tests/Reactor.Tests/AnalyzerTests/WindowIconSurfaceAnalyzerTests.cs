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

    // Path/Uri surfaces — reject Binary. WindowSpec is a `record` in the real framework
    // (src/Reactor/Hosting/WindowSpec.cs), which is what makes `spec with { Icon = ... }` legal.
    public sealed record WindowSpec
    {
        public string Title { get; init; }
        public WindowIcon Icon { get; init; }
    }

    public enum JumpListItemKind { Task, Custom, Separator }

    // Mirrors the real record in src/Reactor/Hosting/Shell/JumpList.cs: Icon is the FIFTH
    // parameter, behind Kind and Description. A stub that moved it would let a positional test
    // pass against a signature no caller can write.
    public sealed record JumpListItem(
        string Title,
        string Arguments,
        JumpListItemKind Kind = JumpListItemKind.Task,
        string Description = null,
        WindowIcon Icon = null,
        string GroupCategory = null)
    {
        public static JumpListItem ForUri(
            string title,
            string uri,
            string description = null,
            WindowIcon icon = null,
            string groupCategory = null) => null;

        public static JumpListItem ForCommandLine(
            string title,
            System.Collections.Generic.IEnumerable<string> arguments,
            string description = null,
            WindowIcon icon = null,
            string groupCategory = null) => null;
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
    public async Task Fires_When_The_Icon_Is_Positional_And_Later_Arguments_Are_Named()
    {
        // C# requires a positional argument to bind at its own ordinal, so `Icon` here is
        // unambiguous even though the rest of the call is named. An ordinal-counting matcher
        // that bails on "any named argument present" would miss this, and it is a natural
        // shape to write.
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M()
        {
            var spec = new TrayIconSpec(
                {|REACTOR_ICON_001:WindowIcon.FromResource(""ms-appx:///Assets/tray.ico"")|},
                Tooltip: ""My App"",
                Key: WindowKey.Of(""main""));
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
        // All three shapes a jump-list icon can arrive through: the record's fifth positional
        // slot, and both convenience factories.
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void Record(byte[] data)
        {
            var item = new JumpListItem(
                ""Open"",
                ""app://open"",
                JumpListItemKind.Task,
                null,
                {|REACTOR_ICON_001:WindowIcon.FromBytes(data)|});
        }

        void ForUri(byte[] data)
        {
            var item = JumpListItem.ForUri(""Open"", ""app://open"", icon: {|REACTOR_ICON_001:WindowIcon.FromBytes(data)|});
        }

        void ForCommandLine(byte[] data)
        {
            var item = JumpListItem.ForCommandLine(
                ""Open"",
                new[] { ""--open"" },
                icon: {|REACTOR_ICON_001:WindowIcon.FromRgba(data, 16, 16)|});
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_Through_A_With_Expression_And_An_Implicit_New()
    {
        // Both syntax kinds are registered alongside explicit `new Type(...)`, so both need to be
        // held to it — a `with` in particular is how a spec is normally amended.
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M(WindowSpec existing, byte[] data)
        {
            WindowSpec implicitNew = new() { Icon = {|REACTOR_ICON_001:WindowIcon.FromBytes(data)|} };
            var amended = existing with { Icon = {|REACTOR_ICON_001:WindowIcon.FromRgba(data, 16, 16)|} };
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_A_Coalescing_Assignment_To_The_Overlay()
    {
        // `TaskbarOverlay.Icon` is `WindowIcon?`, so `??=` is legal C# and reaches the same
        // setter — and therefore the same silent skip — as a plain assignment.
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M(TaskbarOverlay overlay)
        {
            overlay.Icon ??= {|REACTOR_ICON_001:WindowIcon.FromResource(""ms-appx:///Assets/badge.ico"")|};
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Silent_When_The_Local_Is_Reassigned_By_Deconstruction()
    {
        // The declaration says Resource, but a deconstruction overwrote it before use. Missing
        // this shape would report the stale kind — a false positive, which is the failure mode
        // this rule is built to avoid.
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M()
        {
            var icon = WindowIcon.FromResource(""ms-appx:///Assets/tray.ico"");
            int unused;
            (icon, unused) = (WindowIcon.FromPath(""tray.ico""), 0);
            var spec = new TrayIconSpec(Icon: icon, Tooltip: ""t"");
        }
    }
}";
        await Analyzer(source).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Silent_When_The_Local_Is_Reassigned_By_Coalescing()
    {
        var source = @"
namespace TestApp
{
    using Microsoft.UI.Reactor;

    class App
    {
        void M(WindowIcon maybe)
        {
            var icon = maybe;
            icon ??= WindowIcon.FromResource(""ms-appx:///Assets/tray.ico"");
            var spec = new TrayIconSpec(Icon: icon, Tooltip: ""t"");
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
            var a = new JumpListItem(""Open"", ""app://open"", Icon: WindowIcon.FromPath(""open.ico""));
            var b = new JumpListItem(""Open"", ""app://open"", Icon: WindowIcon.FromResource(""ms-appx:///Assets/open.ico""));
            var c = JumpListItem.ForUri(""Open"", ""app://open"", icon: WindowIcon.FromPath(""open.ico""));
            var d = JumpListItem.ForCommandLine(""Open"", new[] { ""--open"" }, icon: WindowIcon.FromResource(""ms-appx:///Assets/open.ico""));
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
