using System.Diagnostics;
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

return args.Length switch
{
    2 when IsMode(args[0], "reactor-il") => VerifyReactorIl(args[1]),
    2 when IsMode(args[0], "negative-resolution") => VerifyNegativeResolution(args[1]),
    3 when IsMode(args[0], "absence") || IsMode(args[0], "presence") => VerifyMstat(args[0], args[1], args[2]),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Reactor.MstatVerifier <absence|presence> <path-to.mstat> <path-to.exe>");
    Console.Error.WriteLine("  Reactor.MstatVerifier reactor-il <path-to-Reactor.dll>");
    Console.Error.WriteLine("  Reactor.MstatVerifier negative-resolution <path-to-fixture.csproj>");
    return 2;
}

int VerifyMstat(string modeArg, string mstatPath, string exePath)
{
    var mode = modeArg.ToLowerInvariant();

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
}

static bool IsMode(string value, string mode) =>
    string.Equals(value, mode, StringComparison.OrdinalIgnoreCase);

static int VerifyReactorIl(string reactorDllPath)
{
    if (!File.Exists(reactorDllPath)) return Fail($"Reactor.dll not found: {reactorDllPath}");

    using var assembly = AssemblyDefinition.ReadAssembly(reactorDllPath);
    var failures = new List<string>();
    foreach (var type in assembly.Modules.SelectMany(m => m.Types).SelectMany(FlattenTypes))
    {
        var fullName = type.FullName.Replace('/', '+');
        if (IsAllowedCoreDevtoolsType(type))
            continue;

        if (IsDevtoolsImplementationNamespace(type.Namespace))
            failures.Add($"Unexpected devtools implementation type in Reactor.dll: {fullName}");

        if (fullName.Contains("PreviewCaptureServer", StringComparison.Ordinal)
            || fullName.Contains("LockfileRegistry", StringComparison.Ordinal))
            failures.Add($"Unexpected devtools implementation symbol in Reactor.dll: {fullName}");
    }

    if (failures.Count > 0)
    {
        foreach (var failure in failures.Distinct(StringComparer.Ordinal))
            Console.Error.WriteLine(failure);
        return 1;
    }

    Console.WriteLine($"Reactor.dll IL verification passed: {Path.GetFileName(reactorDllPath)}");
    return 0;
}

static bool IsDevtoolsImplementationNamespace(string? ns) =>
    string.Equals(ns, "Microsoft.UI.Reactor.Hosting.Devtools", StringComparison.Ordinal)
    || (ns?.StartsWith("Microsoft.UI.Reactor.Hosting.Devtools.", StringComparison.Ordinal) ?? false);

static bool IsAllowedCoreDevtoolsType(TypeDefinition type)
{
    if (!IsDevtoolsImplementationNamespace(type.Namespace))
        return false;

    return type.Name is
        "DevtoolsSubverb" or
        "McpTransport" or
        "DevtoolsCliOptions" or
        "DevtoolsCliParser" or
        "IReactorDevtoolsHost" or
        "ReactorDevtoolsBootRequest" or
        "ReactorDevtoolsBootstrap";
}

static IEnumerable<TypeDefinition> FlattenTypes(TypeDefinition type)
{
    yield return type;
    foreach (var nested in type.NestedTypes)
    foreach (var child in FlattenTypes(nested))
        yield return child;
}

static int VerifyNegativeResolution(string projectPath)
{
    if (!File.Exists(projectPath)) return Fail($"negative-resolution project not found: {projectPath}");

    var psi = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    psi.ArgumentList.Add("build");
    psi.ArgumentList.Add(projectPath);
    psi.ArgumentList.Add("-p:Platform=x64");
    psi.ArgumentList.Add("--nologo");

    using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet build.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    var output = stdout + stderr;
    if (process.ExitCode == 0)
        return Fail("Negative-resolution fixture unexpectedly compiled. Devtools implementation types resolved without Microsoft.UI.Reactor.Devtools.");

    if (!output.Contains("CS0246", StringComparison.Ordinal) && !output.Contains("CS0234", StringComparison.Ordinal))
    {
        Console.Error.WriteLine(output);
        return Fail("Negative-resolution fixture failed, but not with the expected type/namespace-not-found diagnostic.");
    }

    Console.WriteLine("negative-resolution verification passed: devtools implementation type is unavailable without Microsoft.UI.Reactor.Devtools");
    return 0;
}

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
