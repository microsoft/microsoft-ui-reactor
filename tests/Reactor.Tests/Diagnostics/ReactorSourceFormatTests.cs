using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.SourceMap.Generator;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// <see cref="ReactorDiagnostics.SourceProperty"/> — the per-control string an
/// out-of-process inspector reads with no managed agent. Headless half: the value grammar,
/// escaping and gating. Writing the property needs live WinUI controls and is covered by
/// the <c>ReactorSource_*</c> selftest fixture.
/// </summary>
// Mutates ReactorSourcePublisher statics (IsEnabled / NoManagedAgent).
[Collection("ReactorSourcePublisherGlobals")]
public sealed class ReactorSourceFormatTests
{
    private sealed class Counter : Component
    {
        public override Element Render() => throw new NotSupportedException();
    }

    private sealed class ItemList<T> : Component
    {
        public override Element Render() => throw new NotSupportedException();
    }

    // ── Grammar ───────────────────────────────────────────────────────────

    [Fact]
    public void FullValue_FieldsInGrammarOrder()
    {
        var value = ReactorSourceFormat.Build(
            new SourceLocation(@"C:\src\App.cs", 36, 13),
            owner: "Counter",
            element: "Button",
            mounts: "Inner",
            root: "App",
            key: "t1",
            name: "incrementButton",
            hooks: "0:count@36;1:density@37");

        Assert.Equal(
            @"v=1|at=C:\src\App.cs:36:13|owner=Counter|element=Button|mounts=Inner|root=App|key=t1|name=incrementButton|hooks=0:count@36;1:density@37",
            value);
    }

    [Fact]
    public void UnknownFieldsAreOmitted_ColumnZeroIsOmitted()
    {
        Assert.Equal("v=1|element=TextBlock", ReactorSourceFormat.Build(null, null, "TextBlock"));
        Assert.Equal(
            "v=1|at=/_/src/App.cs:12|element=Stack",
            ReactorSourceFormat.Build(new SourceLocation("/_/src/App.cs", 12), null, "Stack"));
        Assert.Equal(
            "v=1|element=Stack",
            ReactorSourceFormat.Build(new SourceLocation("", 12, 3), null, "Stack"));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a|b", "a%7Cb")]
    [InlineData("100%", "100%25")]
    [InlineData("%7C|", "%257C%7C")]
    public void Escape_PercentAndPipeOnly(string raw, string expected)
        => Assert.Equal(expected, ReactorSourceFormat.Escape(raw));

    [Fact]
    public void Values_AreEscaped_SoTheFieldSplitIsUnambiguous()
    {
        var value = ReactorSourceFormat.Build(
            new SourceLocation(@"C:\odd|dir\100%.cs", 1, 2), "Own|er", "Button", key: "k|1%");

        Assert.Equal(@"v=1|at=C:\odd%7Cdir\100%25.cs:1:2|owner=Own%7Cer|element=Button|key=k%7C1%25", value);

        // A reader's split: every field survives, and unescaping restores the originals.
        var fields = value.Split('|').Select(f => f.Split('=', 2)).ToDictionary(f => f[0], f => Unescape(f[1]));
        Assert.Equal(@"C:\odd|dir\100%.cs:1:2", fields["at"]);
        Assert.Equal("Own|er", fields["owner"]);
        Assert.Equal("k|1%", fields["key"]);
    }

    private static string Unescape(string value) => value.Replace("%7C", "|").Replace("%25", "%");

    // ── at= paths are relative (XAML parity) ──────────────────────────────

    [Fact]
    public void Rel_FollowsAt()
        => Assert.Equal(
            "v=1|at=App.cs:3:5|rel=0|element=Button",
            ReactorSourceFormat.Build(new SourceLocation("App.cs", 3, 5), null, "Button", rel: "0"));

    [Fact]
    public void PublishedPath_IsRelativeToTheProjectThenTheRoot_ElseFileNameOnly()
    {
        // Unique fake roots: the static-info table is process-wide.
        var root = $@"Q:\reactor-relpath-{Guid.NewGuid():N}";
        Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.RegisterStaticInfo(b => b.Roots($@"{root}\src\App\", root));

        string? marker;
        Assert.Equal("Pages/Main.cs",
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.ToPublishedPath($@"{root}\src\App\Pages\Main.cs", out marker));
        Assert.Null(marker);

        Assert.Equal("src/Lib/Card.cs",
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.ToPublishedPath($@"{root}\src\Lib\Card.cs", out marker));
        Assert.Equal("root", marker);

        Assert.Equal("Shared.cs",
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.ToPublishedPath(@"R:\elsewhere\Shared.cs", out marker));
        Assert.Equal("0", marker);
    }

    [Theory]
    [InlineData("/_/src/App/App.cs", "/_/src/App/App.cs")] // deterministic (PathMap): already anonymous
    [InlineData(@"src\App\App.cs", "src/App/App.cs")]       // already relative
    public void PublishedPath_KeepsNonAbsolutePaths(string path, string expected)
    {
        Assert.Equal(expected, Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.ToPublishedPath(path, out var marker));
        Assert.Null(marker);
    }

    [Fact]
    public void ToPublishedPath_UnderscoreDirectoryThatIsNotADeterministicRoot_IsFileNameOnly()
    {
        Assert.Equal("App.cs", Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.ToPublishedPath("/_work/agent/src/App.cs", out var marker));
        Assert.Equal("0", marker);
    }

    [Fact]
    public void ToPublishedPath_CurrentDriveRootedPath_IsFileNameOnly()
    {
        Assert.Equal("App.cs", Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.ToPublishedPath(@"\Users\me\repo\App.cs", out var marker));
        Assert.Equal("0", marker);
    }

    [Theory]
    [InlineData(@"C:\src\App.cs", true)]
    [InlineData("C:/src/App.cs", true)]
    [InlineData(@"\\server\share\App.cs", true)]
    [InlineData(@"\Users\me\App.cs", true)]
    [InlineData("/home/me/App.cs", true)]
    [InlineData("/_/src/App.cs", false)]
    [InlineData("/_1/src/App.cs", false)]
    [InlineData("/_12/src/App.cs", false)]
    [InlineData("/_work/src/App.cs", true)]
    [InlineData("/_home/me/App.cs", true)]
    [InlineData("/_1a/src/App.cs", true)]
    [InlineData("/_", true)]
    [InlineData("App.cs", false)]
    public void IsAbsolutePath(string path, bool expected)
        => Assert.Equal(expected, Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.IsAbsolutePath(path));

    [Theory]
    [InlineData(typeof(TextBlockElement), "TextBlock")]
    [InlineData(typeof(ComponentElement), "Component")]
    [InlineData(typeof(ComponentElement<int>), "Component")]
    [InlineData(typeof(FuncElement), "Func")]
    [InlineData(typeof(MemoElement), "Memo")]
    [InlineData(typeof(Element), "Element")]
    public void KindOf_StripsArityAndElementSuffix(Type type, string expected)
        => Assert.Equal(expected, ReactorSourceFormat.KindOf(type));

    [Fact]
    public void ComponentName_MatchesTheComponentEventsNaming()
    {
        Assert.Equal("Counter", ReactorSourceFormat.ComponentName(typeof(Counter)));
        Assert.Equal("ItemList<Int32>", ReactorSourceFormat.ComponentName(typeof(ItemList<int>)));
        Assert.Equal("Counter", ReactorSourceFormat.ComponentName(null, Factories.Component<Counter>()));
        Assert.Equal("FuncElement", ReactorSourceFormat.ComponentName(null, Factories.RenderEachTime(_ => Factories.TextBlock("f"))));
    }

    [Fact]
    public void KeyText_IsInvariant()
    {
        Assert.Null(ReactorSourceFormat.KeyText(null));
        Assert.Equal("t1", ReactorSourceFormat.KeyText("t1"));
        Assert.Equal("1.5", ReactorSourceFormat.KeyText(1.5));
    }

    // ── Gating ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Environment_OnlyExactlyOneEnables(string? value, bool expected)
        => Assert.Equal(expected, ReactorSourcePublisher.IsEnabledByEnvironment(value));

    [Fact]
    public void BuildSwitch_IsTheTrimmableDevtoolsFeatureSwitch()
    {
        // The trimmer substitutes a [FeatureSwitchDefinition] property with the configured
        // constant; this attribute is what removes publishing from builds without the switch.
        var property = typeof(Microsoft.UI.Reactor.Hosting.ReactorFeatures).GetProperty(
            nameof(Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported), BindingFlags.Static | BindingFlags.NonPublic)!;
        var attribute = property.GetCustomAttribute<FeatureSwitchDefinitionAttribute>();
        Assert.Equal("Reactor.DevtoolsSupport", attribute?.SwitchName);

        var expected = AppContext.TryGetSwitch("Reactor.DevtoolsSupport", out var on) && on;
        Assert.Equal(expected, ReactorSourcePublisher.IsSupported);
    }

    [Fact]
    public void Publishing_RequiresBothTheSwitchAndTheEnvironment()
    {
        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            ReactorSourcePublisher.IsEnabled = false;
            Assert.False(ReactorDiagnostics.IsSourcePublishingEnabled);

            ReactorSourcePublisher.IsEnabled = true;
            Assert.Equal(ReactorSourcePublisher.IsSupported, ReactorDiagnostics.IsSourcePublishingEnabled);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
        }
    }
}

/// <summary>
/// Guards the generator's <see cref="SourceMapInterceptorGenerator.SingleSlotHooks"/> table
/// against the runtime: each hook it lists must take exactly one <c>RenderContext</c> slot,
/// on every overload, or the <c>hooks=</c> indices an inspector reads would be off by one
/// from the slots <c>ReactorDiagnostics</c> reports.
/// </summary>
public sealed class HookSlotTableTests
{
    private static int Slots(Action<RenderContext> hook)
    {
        var ctx = new RenderContext();
        ctx.BeginRender(static () => { }, new ContextScope());
        var hooks = (global::System.Collections.ICollection)typeof(RenderContext)
            .GetField("_hooks", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ctx)!;
        int before = hooks.Count;
        hook(ctx);
        return hooks.Count - before;
    }

    private static readonly Context<int> s_context = new(0);

    public static TheoryData<string, Action<RenderContext>> Hooks => new()
    {
        { "UseState", static c => c.UseState(0) },
        { "UseState", static c => c.UseState(0, threadSafe: true) },
        { "UseReducer", static c => c.UseReducer(0) },
        { "UseReducer", static c => c.UseReducer<int, int>(static (s, a) => s + a, 0) },
        { "UseRef", static c => c.UseRef(0) },
        { "UseEffect", static c => c.UseEffect(static () => { }) },
        { "UseEffect", static c => c.UseEffect(static () => static () => { }) },
        { "UseEffect", static c => c.UseEffect(static () => { }, 1) },
        { "UseEffect", static c => c.UseEffect(static () => { }, 1, 2) },
        { "UseEffect", static c => c.UseEffect(static () => { }, 1, 2, 3) },
        { "UseEffect", static c => c.UseEffect(static () => static () => { }, 1) },
        { "UseMemo", static c => c.UseMemo(static () => 1) },
        { "UseMemo", static c => c.UseMemo(static () => 1, 1) },
        { "UseMemo", static c => c.UseMemo(static () => 1, 1, 2) },
        { "UseMemo", static c => c.UseMemo(static () => 1, 1, 2, 3) },
        { "UseCallback", static c => c.UseCallback(static () => { }) },
        { "UseCallback", static c => c.UseCallback(static () => { }, 1) },
        { "UseCallback", static c => c.UseCallback(static () => { }, 1, 2) },
        { "UseCallback", static c => c.UseCallback(static () => { }, 1, 2, 3) },
        { "UseContext", static c => c.UseContext(s_context) },
    };

    [Theory]
    [MemberData(nameof(Hooks))]
    public void SingleSlotHook_TakesExactlyOneSlot(string name, Action<RenderContext> hook)
    {
        Assert.Contains(name, SourceMapInterceptorGenerator.SingleSlotHooks);
        Assert.Equal(1, Slots(hook));
    }

    [Fact]
    public void EveryListedHook_IsExercised()
    {
        var exercised = Hooks.Select(static row => row.Data.Item1).ToHashSet();
        Assert.Equal(SourceMapInterceptorGenerator.SingleSlotHooks.OrderBy(static n => n), exercised.OrderBy(static n => n));
    }
}
