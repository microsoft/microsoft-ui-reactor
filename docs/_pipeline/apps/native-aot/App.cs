using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Devtools;
using static Microsoft.UI.Reactor.Factories;

// <snippet:program>
ReactorApp.Run<App>("Hello World AOT", width: 480, height: 240);

internal sealed class App : Component
{
    public override Element Render() => TextBlock("Hello, world!");
}
// </snippet:program>

static class DevtoolsEntryPoint
{
    public static void Run()
    {
        // <snippet:devtools-on>
        // Program.cs of an app that keeps devtools in its AOT build.
        AppContext.SetSwitch("Reactor.DevtoolsSupport", true);
        ReactorDevtools.EnsureRegistered();
        ReactorApp.Run<App>("Hello World AOT Devtools On", width: 480, height: 240);
        // </snippet:devtools-on>
    }
}

static class DirectRecordEntryPoint
{
    public static void Run()
    {
        // <snippet:register-all>
        // Registers every built-in control handler. The trimmer then keeps all of them.
        ReactorApp.RegisterAllBuiltIns();
        ReactorApp.Run<DirectRecordApp>("Direct records", width: 480, height: 240);
        // </snippet:register-all>
    }
}

// <snippet:direct-record>
sealed class DirectRecordApp : Component
{
    // `new` skips the TextBlock() factory, so it does not register the TextBlock handler.
    public override Element Render() => new TextBlockElement("Built without a factory");
}
// </snippet:direct-record>

static class FactoryTouchEntryPoint
{
    public static void Run()
    {
        // <snippet:factory-touch>
        // Registers only the TextBlock handler. Other controls stay trimmable.
        _ = TextBlock("");
        ReactorApp.Run<DirectRecordApp>("Direct records", width: 480, height: 240);
        // </snippet:factory-touch>
    }
}

// <snippet:page-registry>
sealed class HomePage : Component
{
    public override Element Render() => TextBlock("Home");
}

sealed class SettingsPage : Component
{
    public override Element Render() => TextBlock("Settings");
}

static class Pages
{
    // Each entry names its type in code, so the trimmer keeps exactly these pages.
    static readonly Dictionary<string, Func<Element>> ByName = new()
    {
        ["home"] = () => Component<HomePage>(),
        ["settings"] = () => Component<SettingsPage>(),
    };

    public static Element Create(string name) =>
        ByName.TryGetValue(name, out var create) ? create() : TextBlock($"Unknown page: {name}");
}
// </snippet:page-registry>

// <snippet:json-context>
record AppSettings(string Theme, int FontSize);

// The source generator writes the serializer at build time, so no reflection runs.
[JsonSerializable(typeof(AppSettings))]
partial class SettingsJsonContext : JsonSerializerContext { }

static class SettingsStore
{
    public static string Save(AppSettings settings) =>
        JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

    public static AppSettings? Load(string json) =>
        JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
}
// </snippet:json-context>

// <snippet:dam-annotation>
static class PropertyNames
{
    // The annotation tells the trimmer to keep T's public properties
    // for every type that callers pass in.
    public static IEnumerable<string> Of<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>() =>
        typeof(T).GetProperties().Select(p => p.Name);
}
// </snippet:dam-annotation>
