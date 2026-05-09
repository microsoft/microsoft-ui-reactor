// Reactor.SignaturesGen — emits skills/reactor.api.txt by reflecting over the
// built Reactor.dll. The output is a flat, alphabetized signatures index meant
// to be loaded by AI agents in lieu of grepping src/Reactor/**/*.cs to verify
// a factory or modifier signature. One line per symbol; no prose.
//
// Layout:
//   ## Factories             (Microsoft.UI.Reactor.Factories — public static partial)
//   ## Modifiers             (extension methods on Element / on T : Element)
//   ## Hooks                 (extension methods on RenderContext / Component)
//   ## Theme                 (Microsoft.UI.Reactor.Core.Theme tokens → resource keys)
//   ## Enums                 (public enums under Microsoft.UI.Reactor.*)
//
// Usage:
//   dotnet run --project tools/Reactor.SignaturesGen -- <repo-root>
//   (build target in csproj passes the repo root automatically)

using System.Reflection;
using System.Text;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: Reactor.SignaturesGen <repo-root>");
    return 1;
}

var repoRoot = Path.GetFullPath(args[0]);

// Write to both the legacy path (consumed by `mur --api` embedding and the
// `agentkit/` NuGet layout) and the plugin-format path (consumed by the
// `reactor-dsl` skill's `references/`). One generation source of truth —
// keeps the two committed copies from drifting.
var outputPaths = new[]
{
    Path.Combine(repoRoot, "skills", "reactor.api.txt"),
    Path.Combine(repoRoot, "plugins", "reactor", "skills", "reactor-dsl", "references", "reactor.api.txt"),
};

var asm = typeof(Microsoft.UI.Reactor.Factories).Assembly;
var sb = new StringBuilder();

sb.AppendLine("# Reactor API — signatures index (generated)");
sb.AppendLine("# Source of truth: " + asm.GetName().Name + ".dll public surface.");
sb.AppendLine("# Regenerate: `mur --regen-api`  (or build tools/Reactor.SignaturesGen — its");
sb.AppendLine("#             AfterBuild target rewrites this file).");
sb.AppendLine("# Format: one symbol per line. No prose. Alphabetized within each section.");
sb.AppendLine();

EmitFactories(asm, sb);
EmitModifiers(asm, sb);
EmitHooks(asm, sb);
EmitTheme(asm, sb);
EmitEnums(asm, sb);

var content = sb.ToString();

foreach (var outputPath in outputPaths)
{
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

    // Skip rewriting if unchanged — keeps file mtimes stable for incremental builds.
    if (File.Exists(outputPath) && File.ReadAllText(outputPath) == content)
    {
        Console.WriteLine($"reactor.api.txt unchanged ({outputPath})");
        continue;
    }

    File.WriteAllText(outputPath, content);
    Console.WriteLine($"wrote {outputPath} ({content.Length} bytes)");
}
return 0;

// ---------------------------------------------------------------------------

static void EmitFactories(Assembly asm, StringBuilder sb)
{
    var factories = asm.GetType("Microsoft.UI.Reactor.Factories");
    if (factories is null) return;

    sb.AppendLine("## Factories");
    sb.AppendLine("# All in `Microsoft.UI.Reactor.Factories` — `using static Microsoft.UI.Reactor.Factories;`");
    sb.AppendLine();

    var methods = factories
        .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(m => !m.IsSpecialName)
        .Select(FormatMethod)
        .Distinct()
        .OrderBy(s => s, StringComparer.Ordinal);

    foreach (var line in methods) sb.AppendLine(line);
    sb.AppendLine();
}

static void EmitModifiers(Assembly asm, StringBuilder sb)
{
    sb.AppendLine("## Modifiers (extension methods on Element)");
    sb.AppendLine("# Fluent — preserves concrete element type. Type-specific sugar (e.g. .Bold()");
    sb.AppendLine("# on TextBlockElement) MUST come before generic .Margin/.Padding/etc.");
    sb.AppendLine();

    var lines = asm
        .GetExportedTypes()
        .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && !t.IsGenericType)  // static class
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        .Where(m => m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
        .Where(m => IsElementExtension(m))
        .Select(FormatMethod)
        .Distinct()
        .OrderBy(s => s, StringComparer.Ordinal);

    foreach (var line in lines) sb.AppendLine(line);
    sb.AppendLine();
}

static void EmitHooks(Assembly asm, StringBuilder sb)
{
    sb.AppendLine("## Hooks (RenderContext / Component)");
    sb.AppendLine("# Call from Render() / function-component body. Same order every render —");
    sb.AppendLine("# never inside if/for. Use the result conditionally, not the call.");
    sb.AppendLine();

    // Instance hooks declared on RenderContext (UseState, UseEffect, UseMemo, ...).
    var rc = asm.GetType("Microsoft.UI.Reactor.Core.RenderContext");
    var instanceHooks = rc is null
        ? Enumerable.Empty<string>()
        : rc.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
             .Where(m => m.Name.StartsWith("Use", StringComparison.Ordinal))
             .Select(m => "RenderContext." + FormatMethod(m));

    // Extension hooks (UseValidationContext, UseInfiniteResource, UseAnnounce, ...).
    var extHooks = asm
        .GetExportedTypes()
        .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && !t.IsGenericType)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        .Where(m => m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
        .Where(IsHookExtension)
        .Select(FormatMethod);

    var lines = instanceHooks.Concat(extHooks)
        .Distinct()
        .OrderBy(s => s, StringComparer.Ordinal);

    foreach (var line in lines) sb.AppendLine(line);
    sb.AppendLine();
}

static void EmitTheme(Assembly asm, StringBuilder sb)
{
    sb.AppendLine("## Theme tokens (Microsoft.UI.Reactor.Core.Theme)");
    sb.AppendLine("# Use these for ALL themed colors — never hardcoded hex on themed surfaces.");
    sb.AppendLine("# Each token resolves to the WinUI resource key shown in the comment.");
    sb.AppendLine();

    var theme = asm.GetType("Microsoft.UI.Reactor.Core.Theme");
    if (theme is null) { sb.AppendLine(); return; }

    var properties = theme
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(p => p.PropertyType.Name == "ThemeRef")
        .OrderBy(p => p.Name, StringComparer.Ordinal);

    foreach (var p in properties)
    {
        // Each Theme.* property returns a `ThemeRef(ResourceKey)`. Invoke and read it.
        string? key = null;
        try
        {
            var themeRef = p.GetValue(null);
            if (themeRef is not null)
            {
                var rk = themeRef.GetType().GetProperty("ResourceKey")
                       ?? themeRef.GetType().GetProperty("Key");
                key = rk?.GetValue(themeRef)?.ToString();
            }
        }
        catch { }
        sb.AppendLine($"Theme.{p.Name,-32} → {key ?? "?"}");
    }
    sb.AppendLine();
}

static void EmitEnums(Assembly asm, StringBuilder sb)
{
    sb.AppendLine("## Enums (public, under Microsoft.UI.Reactor.*)");
    sb.AppendLine();

    var enums = asm.GetExportedTypes()
        .Where(t => t.IsEnum)
        .OrderBy(t => t.FullName, StringComparer.Ordinal);

    foreach (var t in enums)
    {
        var values = string.Join(", ", Enum.GetNames(t));
        sb.AppendLine($"{Short(t)} {{ {values} }}");
    }
    sb.AppendLine();
}

// ---------------------------------------------------------------------------
//  Heuristics
// ---------------------------------------------------------------------------

static bool IsElementExtension(MethodInfo m)
{
    // First parameter is the receiver. Element / T : Element / generic Element-bounded
    // counts as a UI modifier. Hooks (RenderContext / Component) are routed elsewhere.
    var ps = m.GetParameters();
    if (ps.Length == 0) return false;
    var t = ps[0].ParameterType;
    if (IsRenderContextOrComponent(t)) return false;
    if (IsElementOrSubclass(t)) return true;
    if (t.IsGenericParameter)
    {
        var constraints = t.GetGenericParameterConstraints();
        return constraints.Any(IsElementOrSubclass);
    }
    return false;
}

static bool IsHookExtension(MethodInfo m)
{
    var ps = m.GetParameters();
    if (ps.Length == 0) return false;
    return IsRenderContextOrComponent(ps[0].ParameterType);
}

static bool IsRenderContextOrComponent(Type t)
{
    for (var cur = t; cur is not null; cur = cur.BaseType)
    {
        if (cur.FullName is "Microsoft.UI.Reactor.Core.RenderContext"
                          or "Microsoft.UI.Reactor.Component") return true;
    }
    // Also catch generic Component<T>.
    if (t.IsGenericType && t.GetGenericTypeDefinition().FullName?.StartsWith("Microsoft.UI.Reactor.Component") == true)
        return true;
    return false;
}

static bool IsElementOrSubclass(Type t)
{
    for (var cur = t; cur is not null; cur = cur.BaseType)
    {
        if (cur.FullName == "Microsoft.UI.Reactor.Core.Element") return true;
    }
    return false;
}

// ---------------------------------------------------------------------------
//  Formatting
// ---------------------------------------------------------------------------

static string FormatMethod(MethodInfo m)
{
    var generics = m.IsGenericMethodDefinition
        ? "<" + string.Join(", ", m.GetGenericArguments().Select(g => g.Name)) + ">"
        : "";

    var isExt = m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);
    var ps = m.GetParameters();
    var paramList = string.Join(", ", ps.Skip(isExt ? 1 : 0).Select(FormatParam));

    var receiver = isExt && ps.Length > 0 ? Short(ps[0].ParameterType) + "." : "";
    return $"{receiver}{m.Name}{generics}({paramList}) → {Short(m.ReturnType)}";
}

static string FormatParam(ParameterInfo p)
{
    var s = $"{Short(p.ParameterType)} {p.Name}";
    if (p.HasDefaultValue) s += " = " + FormatDefault(p.DefaultValue, p.ParameterType);
    if (p.IsOptional && !p.HasDefaultValue) s += " = ?";
    return s;
}

static string FormatDefault(object? v, Type t)
{
    if (v is null) return "null";
    if (v is bool b) return b ? "true" : "false";
    if (v is string s) return "\"" + s + "\"";
    if (t.IsEnum) return t.Name + "." + v;
    return v.ToString() ?? "?";
}

// Compact type display: drop System.* / WinUI namespaces, keep generics.
static string Short(Type t)
{
    if (t.IsByRef) return "ref " + Short(t.GetElementType()!);
    if (t.IsArray) return Short(t.GetElementType()!) + "[]";

    if (t.IsGenericType)
    {
        var def = t.GetGenericTypeDefinition();
        var name = def.Name;
        var tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick];
        var args = string.Join(", ", t.GetGenericArguments().Select(Short));
        // Nullable<T> → T?
        if (def == typeof(Nullable<>)) return Short(t.GetGenericArguments()[0]) + "?";
        return name + "<" + args + ">";
    }

    return t.Name switch
    {
        "Void" => "void",
        "Boolean" => "bool",
        "String" => "string",
        "Int32" => "int",
        "Int64" => "long",
        "Double" => "double",
        "Single" => "float",
        "Object" => "object",
        _ => t.Name,
    };
}
