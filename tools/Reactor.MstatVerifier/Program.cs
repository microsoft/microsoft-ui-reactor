using System.Text;
using Mono.Cecil;

const long MaxHelloWorldAotExeBytes = 13_107_200;

MstatAssertion[] assertions =
[
    new("Microsoft.UI.Reactor.Hosting.Devtools.*", name =>
        name.Contains("DevtoolsMcpServer", StringComparison.Ordinal)
        || name.Contains("LockfileRegistry", StringComparison.Ordinal)
        || name.Contains("DevtoolsDockingTools", StringComparison.Ordinal)
        || name.Contains("DevtoolsPropertyTools", StringComparison.Ordinal)
        || name.Contains("DevtoolsUiaTools", StringComparison.Ordinal)
        || name.Contains("DevtoolsJsonContext", StringComparison.Ordinal)),
    new("Microsoft.UI.Reactor.Hosting.PreviewCaptureServer", name => name.Contains("Microsoft.UI.Reactor.Hosting.PreviewCaptureServer", StringComparison.Ordinal) || name.Contains("PreviewCaptureServer", StringComparison.Ordinal)),
    new("System.Net.Http.HttpClient", name => name.Contains("System.Net.Http.HttpClient", StringComparison.Ordinal) || name.Contains("HttpClient", StringComparison.Ordinal)),
    new("System.Net.Http.SocketsHttpHandler", name => name.Contains("SocketsHttpHandler", StringComparison.Ordinal)),
    new("System.Net.Http.HttpConnection", name => name.Contains("HttpConnection", StringComparison.Ordinal)),
    new("System.Net.Http.Http2Connection", name => name.Contains("Http2Connection", StringComparison.Ordinal)),
    new("System.Net.Security.SslStream", name => name.Contains("System.Net.Security.SslStream", StringComparison.Ordinal) || name.Contains("SslStream", StringComparison.Ordinal)),
    new("System.Net.HttpListener", name => name.Contains("System.Net.HttpListener", StringComparison.Ordinal) || name.Contains("HttpListener", StringComparison.Ordinal)),
    new("System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>", name => name.Contains("JsonTypeInfo", StringComparison.Ordinal)),
    new("System.Text.Json.Serialization.JsonConverter<T>", name => name.Contains("JsonConverter", StringComparison.Ordinal)),
];

if (args.Length != 3 || !IsMode(args[0]))
{
    Console.Error.WriteLine("Usage: Reactor.MstatVerifier <absence|presence> <path-to.mstat> <path-to.exe>");
    return 2;
}

var mode = args[0].ToLowerInvariant();
var mstatPath = args[1];
var exePath = args[2];

if (!File.Exists(mstatPath)) return Fail($"mstat file not found: {mstatPath}");
if (!File.Exists(exePath)) return Fail($"exe file not found: {exePath}");

var exeBytes = new FileInfo(exePath).Length;
if (mode == "absence" && exeBytes > MaxHelloWorldAotExeBytes)
{
    return Fail($"{Path.GetFileName(exePath)} is {exeBytes:N0} bytes; limit is {MaxHelloWorldAotExeBytes:N0} bytes.");
}

var names = ReadMstatNames(mstatPath);
var failures = new List<string>();

foreach (var assertion in assertions)
{
    var present = names.Any(assertion.Matches);
    if (mode == "absence" && present)
        failures.Add($"Expected absent but found: {assertion.Label}");
    else if (mode == "presence" && !present)
        failures.Add($"Expected present but not found: {assertion.Label}");
}

if (failures.Count > 0)
{
    foreach (var failure in failures)
        Console.Error.WriteLine(failure);
    return 1;
}

Console.WriteLine($"mstat {mode} verification passed: {Path.GetFileName(mstatPath)} ({exeBytes:N0} bytes)");
return 0;

static bool IsMode(string value) =>
    string.Equals(value, "absence", StringComparison.OrdinalIgnoreCase)
    || string.Equals(value, "presence", StringComparison.OrdinalIgnoreCase);

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static IReadOnlyList<string> ReadMstatNames(string mstatPath)
{
    var names = new HashSet<string>(StringComparer.Ordinal);

    try
    {
        using var assembly = AssemblyDefinition.ReadAssembly(mstatPath);
        foreach (var module in assembly.Modules)
        foreach (var type in module.Types)
            AddTypeAndNestedTypes(type, names);
    }
    catch (BadImageFormatException)
    {
        // Some NativeAOT mstat payloads are PE-like data files that Cecil cannot
        // fully materialize; the embedded metadata strings below are the gate.
    }

    foreach (var text in ExtractAsciiStrings(File.ReadAllBytes(mstatPath)))
        names.Add(text);

    return names.ToArray();
}

static void AddTypeAndNestedTypes(TypeDefinition type, ISet<string> names)
{
    names.Add(type.FullName);
    foreach (var nested in type.NestedTypes)
        AddTypeAndNestedTypes(nested, names);
}

static IEnumerable<string> ExtractAsciiStrings(byte[] bytes)
{
    var builder = new StringBuilder();
    foreach (var b in bytes)
    {
        if (b is >= 32 and <= 126)
        {
            builder.Append((char)b);
            continue;
        }

        if (builder.Length >= 4)
            yield return builder.ToString();
        builder.Clear();
    }

    if (builder.Length >= 4)
        yield return builder.ToString();
}

internal sealed record MstatAssertion(string Label, Func<string, bool> Matches);
