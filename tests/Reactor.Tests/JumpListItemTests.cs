using Microsoft.UI.Reactor;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 036 §11.3 — JumpListItem record + ForUri factory.
/// Pure-data tests; no shell COM is exercised here.
/// </summary>
public class JumpListItemTests
{
    [Fact]
    public void Defaults_Are_Spec_036_Defaults()
    {
        var item = new JumpListItem(Title: "Open", Arguments: "/open");
        Assert.Equal("Open", item.Title);
        Assert.Equal("/open", item.Arguments);
        Assert.Equal(JumpListItemKind.Task, item.Kind);
        Assert.Null(item.Description);
        Assert.Null(item.Icon);
        Assert.Null(item.GroupCategory);
    }

    [Fact]
    public void Records_Are_Value_Equal_On_Identical_Field_Sets()
    {
        var a = new JumpListItem("Open", "/open");
        var b = new JumpListItem("Open", "/open");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Records_Are_Not_Equal_When_Title_Or_Args_Differ()
    {
        Assert.NotEqual(new JumpListItem("A", "/x"), new JumpListItem("B", "/x"));
        Assert.NotEqual(new JumpListItem("A", "/x"), new JumpListItem("A", "/y"));
    }

    [Fact]
    public void ForUri_Sets_Task_Kind_By_Default()
    {
        var item = JumpListItem.ForUri("Settings", "/settings");
        Assert.Equal("Settings", item.Title);
        Assert.Equal("/settings", item.Arguments);
        Assert.Equal(JumpListItemKind.Task, item.Kind);
        Assert.Null(item.GroupCategory);
    }

    [Fact]
    public void ForUri_With_GroupCategory_Sets_Custom_Kind()
    {
        var item = JumpListItem.ForUri("Project A", "/project/a", groupCategory: "Recent Projects");
        Assert.Equal(JumpListItemKind.Custom, item.Kind);
        Assert.Equal("Recent Projects", item.GroupCategory);
    }

    [Fact]
    public void ForUri_Throws_On_Null_Title()
    {
        Assert.Throws<ArgumentNullException>(() => JumpListItem.ForUri(null!, "/x"));
    }

    [Fact]
    public void ForUri_Throws_On_Null_Uri()
    {
        Assert.Throws<ArgumentNullException>(() => JumpListItem.ForUri("Title", null!));
    }

    // ─── Localization round-trip — spec 036 §0.3 ────────────────────────────
    [Theory]
    [InlineData("最近のファイル")]
    [InlineData("الملفات الحديثة")]
    [InlineData("файлы")]
    [InlineData("Récent — émoji 🎉")]
    public void Title_Round_Trips_Non_Ascii(string title)
    {
        var item = JumpListItem.ForUri(title, "/recent");
        Assert.Equal(title, item.Title);
    }
}

/// <summary>Spec 036 §11.4 — TrayIconSpec record + localization round-trip.</summary>
public class TrayIconSpecTests
{
    [Fact]
    public void Defaults_Are_Spec_036_Defaults()
    {
        var spec = new TrayIconSpec(Icon: WindowIcon.FromPath("foo.ico"), Tooltip: "MyApp");
        Assert.True(spec.IsVisible);
        Assert.Null(spec.Key);
    }

    [Fact]
    public void Records_Are_Value_Equal_On_Identical_Field_Sets()
    {
        var icon = WindowIcon.FromPath("foo.ico");
        var a = new TrayIconSpec(icon, "MyApp");
        var b = new TrayIconSpec(icon, "MyApp");
        Assert.Equal(a, b);
    }

    // ─── Localization round-trip — spec 036 §0.3 ────────────────────────────
    [Theory]
    [InlineData("通知")]
    [InlineData("الإشعارات")]
    [InlineData("уведомления")]
    [InlineData("Notif — 🔔")]
    public void Tooltip_Round_Trips_Non_Ascii(string tooltip)
    {
        var spec = new TrayIconSpec(WindowIcon.FromPath("x.ico"), tooltip);
        Assert.Equal(tooltip, spec.Tooltip);
        Assert.Equal(spec, new TrayIconSpec(spec.Icon, tooltip));
    }
}

/// <summary>Spec 036 §11.6 — LaunchActivation record + DeepLinkMap bridge.</summary>
public class LaunchActivationTests
{
    [Fact]
    public void Normal_Sentinel_Is_Stable()
    {
        Assert.Equal(LaunchKind.Normal, LaunchActivation.Normal.Kind);
        Assert.Null(LaunchActivation.Normal.Arguments);
        Assert.Empty(LaunchActivation.Normal.Files);
    }

    [Fact]
    public void Construction_Round_Trips_Fields()
    {
        var act = new LaunchActivation(LaunchKind.JumpList, "/settings/general", new[] { "x.txt" });
        Assert.Equal(LaunchKind.JumpList, act.Kind);
        Assert.Equal("/settings/general", act.Arguments);
        Assert.Single(act.Files);
        Assert.Equal("x.txt", act.Files[0]);
    }

    [Fact]
    public void TryResolve_Returns_False_For_Empty_Arguments()
    {
        var map = new Microsoft.UI.Reactor.Navigation.DeepLinkMap<string>()
            .Map("/", _ => "home");
        var act = new LaunchActivation(LaunchKind.Normal, null, Array.Empty<string>());
        Assert.False(act.TryResolve(map, out var result));
        Assert.False(result.Matched);
    }

    [Fact]
    public void TryResolve_Returns_True_When_Map_Matches()
    {
        var map = new Microsoft.UI.Reactor.Navigation.DeepLinkMap<string>()
            .Map("/", _ => "home")
            .Map("/settings/{id}", a => $"settings:{a.Get<string>("id")}");

        var act = new LaunchActivation(LaunchKind.JumpList, "/settings/general", Array.Empty<string>());
        Assert.True(act.TryResolve(map, out var result));
        Assert.True(result.Matched);
        Assert.Single(result.Routes);
        Assert.Equal("settings:general", result.Routes[0]);
    }

    [Fact]
    public void TryResolve_Returns_False_For_Unknown_Route()
    {
        var map = new Microsoft.UI.Reactor.Navigation.DeepLinkMap<string>()
            .Map("/", _ => "home");
        var act = new LaunchActivation(LaunchKind.JumpList, "/unmapped", Array.Empty<string>());
        Assert.False(act.TryResolve(map, out var result));
        Assert.False(result.Matched);
    }

    [Fact]
    public void TryResolve_Throws_On_Null_Map()
    {
        var act = new LaunchActivation(LaunchKind.JumpList, "/x", Array.Empty<string>());
        Microsoft.UI.Reactor.Navigation.DeepLinkMap<string>? map = null;
        Assert.Throws<ArgumentNullException>(() => act.TryResolve(map!, out _));
    }
}

/// <summary>
/// Spec 036 §11.3 — JumpList static state. We don't exercise the live shell
/// here (that's a selftest fixture); only the public state surface.
/// </summary>
public class JumpListStateTests
{
    [Fact]
    public void AppUserModelId_Round_Trips()
    {
        try
        {
            JumpList.AppUserModelId = "ContosoApp.JumpListTest";
            Assert.Equal("ContosoApp.JumpListTest", JumpList.AppUserModelId);
        }
        finally
        {
            JumpList.ResetForTests();
        }
    }

    [Fact]
    public void ShowRecent_And_ShowFrequent_Default_False()
    {
        JumpList.ResetForTests();
        Assert.False(JumpList.ShowRecent);
        Assert.False(JumpList.ShowFrequent);
    }

    [Fact]
    public void ShowRecent_Round_Trips()
    {
        try
        {
            JumpList.ShowRecent = true;
            Assert.True(JumpList.ShowRecent);
        }
        finally { JumpList.ResetForTests(); }
    }

    // -- EncodeArguments / EncodeArgument (W-2 hardening) -----------------------
    //
    // The spec is `CommandLineToArgvW`: the same parser Reactor.Cli (and any other
    // process the shell re-launches) consumes. Tests assert round-trip — encode
    // then split via the OS parser, expect the original sequence. On non-Windows
    // CI the round-trip uses a managed mirror of the same algorithm.

    [Fact]
    public void EncodeArgument_Plain_Token_Is_Unquoted()
    {
        Assert.Equal("simple", JumpListItem.EncodeArgument("simple"));
    }

    [Fact]
    public void EncodeArgument_Wraps_Whitespace()
    {
        Assert.Equal("\"hello world\"", JumpListItem.EncodeArgument("hello world"));
    }

    [Fact]
    public void EncodeArgument_Escapes_Embedded_Quote()
    {
        // foo"bar  →  "foo\"bar"
        Assert.Equal("\"foo\\\"bar\"", JumpListItem.EncodeArgument("foo\"bar"));
    }

    [Fact]
    public void EncodeArgument_Plain_Trailing_Backslashes_Need_No_Quoting()
    {
        // Trailing backslashes only need the "double them" rule when the value
        // is already being quoted. A bare `foo\\` round-trips through
        // CommandLineToArgvW as itself.
        Assert.Equal("foo\\\\", JumpListItem.EncodeArgument("foo\\\\"));
    }

    [Fact]
    public void EncodeArgument_Doubles_Trailing_Backslashes_Before_Closing_Quote()
    {
        // When the value already needs quoting (has whitespace), trailing
        // backslashes must be doubled so the parser does not consume the
        // closing quote as escaped: input "foo \\"  →  "\"foo \\\\\\\\\""
        // i.e. 2 trailing backslashes inside the value become 4 backslashes
        // inside the encoded form.
        Assert.Equal("\"foo \\\\\\\\\"", JumpListItem.EncodeArgument("foo \\\\"));
    }

    [Fact]
    public void EncodeArgument_Preserves_Internal_Backslashes_Without_Quotes()
    {
        Assert.Equal("\"a\\b c\"", JumpListItem.EncodeArgument("a\\b c"));
    }

    [Fact]
    public void EncodeArguments_Joins_With_Single_Spaces()
    {
        var encoded = JumpListItem.EncodeArguments(new[] { "open", "C:\\Users\\Demo File.txt" });
        Assert.Equal("open \"C:\\Users\\Demo File.txt\"", encoded);
    }

    [Fact]
    public void EncodeArguments_RoundTrips_Through_Argv_Parser()
    {
        var input = new[] { "open", "C:\\path with spaces\\file.txt", "/flag", "value with \"quotes\"" };
        var encoded = JumpListItem.EncodeArguments(input);
        var decoded = SplitCommandLine(encoded);
        Assert.Equal(input, decoded);
    }

    [Fact]
    public void EncodeArguments_Throws_On_Null_Element()
    {
        Assert.Throws<ArgumentException>(() =>
            JumpListItem.EncodeArguments(new string[] { "ok", null! }));
    }

    [Fact]
    public void ForCommandLine_Builds_Encoded_Arguments()
    {
        var item = JumpListItem.ForCommandLine(
            "Open Project",
            new[] { "open", "C:\\Code\\Sample Project" });
        Assert.Equal("Open Project", item.Title);
        Assert.Equal("open \"C:\\Code\\Sample Project\"", item.Arguments);
    }

    /// <summary>
    /// Managed mirror of <c>CommandLineToArgvW</c>. We don't P/Invoke into the
    /// real one in tests — keeps the suite cross-platform — but the parsing
    /// rules below match the documented MSVC behaviour and the actual shell
    /// parser. (The encoder under test was written to satisfy these rules.)
    /// </summary>
    private static string[] SplitCommandLine(string commandLine)
    {
        var args = new global::System.Collections.Generic.List<string>();
        var current = new global::System.Text.StringBuilder();
        bool inQuotes = false;
        int i = 0;
        while (i < commandLine.Length)
        {
            var c = commandLine[i];
            if (c == '\\')
            {
                int slashes = 0;
                while (i < commandLine.Length && commandLine[i] == '\\') { slashes++; i++; }
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', slashes / 2);
                    if (slashes % 2 == 0) { inQuotes = !inQuotes; }
                    else { current.Append('"'); }
                    i++;
                }
                else
                {
                    current.Append('\\', slashes);
                }
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
                i++;
            }
            else if (!inQuotes && (c == ' ' || c == '\t'))
            {
                args.Add(current.ToString());
                current.Clear();
                while (i < commandLine.Length && (commandLine[i] == ' ' || commandLine[i] == '\t')) i++;
            }
            else
            {
                current.Append(c);
                i++;
            }
        }
        if (current.Length > 0 || (commandLine.Length > 0 && commandLine[commandLine.Length - 1] != ' ' && args.Count == 0))
            args.Add(current.ToString());
        return args.ToArray();
    }
}
