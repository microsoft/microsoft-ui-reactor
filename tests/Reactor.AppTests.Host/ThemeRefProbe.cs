using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host;

/// <summary>
/// Issue #1534 — fresh-process probe for resolving a theme resource for the theme the app is
/// not using.
/// </summary>
/// <remarks>
/// <para>WinUI creates a theme dictionary entry the first time it is read. An entry read from
/// code is resolved for the <em>application</em> theme and then kept for every element, so a
/// Light app that read the Dark entry of <c>ControlFillColorSecondaryBrush</c> from code got
/// the Light color back, and every Dark element in the app rendered the Light color from then
/// on. That state is process-wide and depends on which code touched the entry first, so an
/// in-process selftest cannot observe it reliably: any earlier fixture that rendered the key in
/// the other theme hides the bug. This mode starts clean and reads first.</para>
/// <para>Each scenario does one kind of first read for the theme opposite to the app's, then
/// opens a window with <c>.Background(Theme.Ref(key))</c> borders, which WinUI resolves itself
/// through <c>{ThemeResource}</c>, in both themes. Per key it reports what the first read
/// returned and what the two borders show. Pass when the opposite-theme border differs from
/// the app-theme border (the entry was not overwritten with the app theme's value) and the
/// first read equals the opposite-theme border. The <see cref="NoneScenario"/> run does no
/// first read, so it is the positive control: it shows the two borders can differ.</para>
/// <para>The app theme comes from the system setting and cannot be changed once WinUI has
/// started, so the direction under test is whichever is opposite on the machine.</para>
/// </remarks>
internal static class ThemeRefProbe
{
    internal const string Flag = "--themeref-probe";

    /// <summary>No first read: positive control.</summary>
    internal const string NoneScenario = "none";

    /// <summary><c>ThemeRef.Resolve(key, isDark)</c>, the public API.</summary>
    internal const string ResolveScenario = "resolve";

    /// <summary><c>ThemeRef.Resolve(key, fe)</c> on an element in the opposite theme, the path validation styling uses.</summary>
    internal const string ResolveElementScenario = "resolve-element";

    /// <summary>A <c>.Resources(...)</c> override with a theme token, mounted under an opposite-theme ancestor.</summary>
    internal const string ResourceOverrideScenario = "resource-override";

    internal static readonly string[] Scenarios = [NoneScenario, ResolveScenario, ResolveElementScenario, ResourceOverrideScenario];

    // Keys whose Light and Dark values differ, so the two borders can tell the themes apart.
    internal static readonly string[] Keys =
    [
        "ControlFillColorSecondaryBrush",
        "TextFillColorSecondaryBrush",
        "CardBackgroundFillColorDefaultBrush",
    ];

    internal const int PassExitCode = 40;
    internal const int FailExitCode = 41;
    internal const int UsageExitCode = 64;

    internal const string PassMarker = "THEMEREF-PASS";
    internal const string FailMarker = "THEMEREF-FAIL";
    internal const string KeyMarker = "THEMEREF-KEY";

    private const string OverrideTarget = "ReactorThemeRefProbeTarget";
    private const string OppositeTag = "themeref-opposite:";
    private const string AppTag = "themeref-app:";
    private const int SettleMs = 500;

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, Flag);
        if (index < 0 || index + 1 >= args.Length || !Scenarios.Contains(args[index + 1]))
        {
            Console.Error.WriteLine($"usage: {Flag} <{string.Join('|', Scenarios)}>");
            return UsageExitCode;
        }
        var scenario = args[index + 1];

        ReactorApp.Run(_ =>
        {
            var appTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
            var opposite = appTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            Console.WriteLine($"SCENARIO {scenario} appTheme={appTheme} opposite={opposite}");

            // The first read of each key for the opposite theme, before any element uses it.
            var firstRead = Keys.ToDictionary(k => k, k => FirstRead(scenario, k, opposite));

            var window = ReactorApp.OpenWindowCore(
                new WindowSpec { Title = "ThemeRef Probe", Width = 320, Height = 240 },
                rootFactory: () => new ProbeContent(appTheme, opposite),
                renderFunc: null,
                configure: null,
                excludeFromShutdownPolicy: false);

            var dispatcher = ReactorApp.UIDispatcher!;
            var timer = dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(SettleMs);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                var pass = true;
                foreach (var key in Keys)
                {
                    var oppositeColor = BorderColor(window.NativeWindow.Content, OppositeTag + key);
                    var appColor = BorderColor(window.NativeWindow.Content, AppTag + key);
                    var read = firstRead[key];
                    var distinct = oppositeColor is not null && appColor is not null && oppositeColor != appColor;
                    var readOk = scenario == NoneScenario || (read is not null && read == oppositeColor);
                    pass &= distinct && readOk;
                    Console.WriteLine(
                        $"{KeyMarker} {key} read={read ?? "null"} opposite={oppositeColor ?? "null"} app={appColor ?? "null"} distinct={distinct} readOk={readOk}");
                }
                Console.WriteLine(pass ? PassMarker : FailMarker);
                Console.Out.Flush();
                ReactorApp.Exit(pass ? PassExitCode : FailExitCode);
            };
            timer.Start();
        });

        // ReactorApp.Exit ends the process from the dispatcher, so this is reached only if
        // the loop unwound before the timer reported.
        Console.WriteLine(FailMarker + " loop exited");
        Console.Out.Flush();
        return FailExitCode;
    }

    private static string? FirstRead(string scenario, string key, ElementTheme theme)
    {
        switch (scenario)
        {
            case ResolveScenario:
                return Format(ThemeRef.Resolve(key, isDark: theme == ElementTheme.Dark));
            case ResolveElementScenario:
                return Format(ThemeRef.Resolve(key, new Border { RequestedTheme = theme }));
            case ResourceOverrideScenario:
            {
                // An ancestor sets the theme; the reconciler threads it to the override at mount.
                var root = new Reconciler().Mount(
                    VStack(TextBlock("override").Resources(r => r.Set(OverrideTarget, Theme.Ref(key))))
                        .RequestedTheme(theme),
                    static () => { });
                var text = FindTextBlock(root, "override");
                return text?.Resources is { } res && res.TryGetValue(OverrideTarget, out var value)
                    ? Format(value as Brush)
                    : null;
            }
            default:
                return null;
        }
    }

    private static string? Format(Brush? brush) => (brush as SolidColorBrush)?.Color.ToString();

    private static string? BorderColor(UIElement? root, string tag)
    {
        var border = Find<Border>(root, b => b.Child is TextBlock { Text: var t } && t == tag);
        return Format(border?.Background);
    }

    private static TextBlock? FindTextBlock(UIElement? root, string text) => Find<TextBlock>(root, t => t.Text == text);

    private static T? Find<T>(DependencyObject? root, Func<T, bool> predicate) where T : DependencyObject
    {
        if (root is null) return null;
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is T match && predicate(match)) return match;
            // An off-tree mount has no visual children yet; walk the logical content too.
            if (node is Panel panel)
                foreach (var child in panel.Children) queue.Enqueue(child);
            else if (node is Border { Child: { } borderChild })
                queue.Enqueue(borderChild);
            else
            {
                var count = VisualTreeHelper.GetChildrenCount(node);
                for (var i = 0; i < count; i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
        }
        return null;
    }

    private sealed class ProbeContent(ElementTheme appTheme, ElementTheme opposite) : Component
    {
        public override Element Render() =>
            VStack(Keys.SelectMany(key => new Element[]
            {
                Border(TextBlock(OppositeTag + key)).Background(Theme.Ref(key)).RequestedTheme(opposite),
                Border(TextBlock(AppTag + key)).Background(Theme.Ref(key)).RequestedTheme(appTheme),
            }).ToArray());
    }
}
