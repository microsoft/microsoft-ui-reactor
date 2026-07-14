// Reactor.ApiIndex — builds the text of skills/reactor.api.txt by reflecting over
// the built Reactor.dll. The output is a flat, alphabetized signatures index meant
// to be loaded by AI agents in lieu of grepping src/Reactor/**/*.cs to verify a
// factory, modifier, hook, or public-type member signature. One line per symbol;
// no prose.
//
// Layout:
//   ## Factories             (Microsoft.UI.Reactor.Factories — public static partial)
//   ## Modifiers             (extension methods on Element / on T : Element)
//   ## Hooks                 (extension methods on RenderContext / Component)
//   ## Theme                 (Microsoft.UI.Reactor.Core.Theme tokens → resource keys)
//   ## Enums                 (public enums under Microsoft.UI.Reactor.*)
//   ## Public types          (ctors/properties/methods/events on every public type)
//
// All reflection lives here, in tooling — never in the shipping Reactor library
// (which treats trimming/AOT warnings as errors).

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace Microsoft.UI.Reactor.ApiIndex;

public static class ApiIndexGenerator
{
    // Every entry point below reflects over the whole public surface of the built
    // Reactor.dll (GetExportedTypes / GetMembers / GetType-by-name) to emit reactor.api.txt.
    // The tool's entire purpose is to enumerate exactly what the trimmer would remove, so it
    // is inherently trim/AOT-unsafe and is only ever run at build time (never shipped, never
    // trimmed, never AOT-published — see Reactor.ApiIndex.csproj / README.md). The requirement
    // is surfaced honestly via [RequiresUnreferencedCode]/[RequiresDynamicCode] rather than
    // silenced, so the IL2*/IL3* analyzer still guards against *new*, unintended reflection.
    const string ReflectionJustification =
        "Reflects over the entire public surface of the built Reactor.dll to emit reactor.api.txt; " +
        "enumerates exactly what the trimmer would remove. Build-time-only generator — never trimmed or AOT-published.";

    // Single source of truth for the index text. Program.cs (the apphost) and the
    // ApiIndexGeneratorTests (in-process, ARM64-safe) both call this.
    [RequiresUnreferencedCode(ReflectionJustification)]
    [RequiresDynamicCode(ReflectionJustification)]
    public static string Generate(Assembly asm)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Reactor API — signatures index (generated)");
        sb.AppendLine("# Source of truth: " + asm.GetName().Name + ".dll public surface.");
        sb.AppendLine("# Regenerate: `mur --regen-api`  (or build tools/Reactor.SignaturesGen — its");
        sb.AppendLine("#             AfterBuild target rewrites this file).");
        sb.AppendLine("# Format: one symbol per line. No prose. Alphabetized within each section.");
        sb.AppendLine("# Sections: Factories, Modifiers, Reference builders, Hooks, Theme, Enums, Public types.");
        sb.AppendLine("# Public types now ARE covered — ctors/properties/methods/events on every");
        sb.AppendLine("# public type (e.g. WindowSpec.Opacity, ReactorWindow.SetPosition).");
        sb.AppendLine();

        EmitFactories(asm, sb);
        EmitModifiers(asm, sb);
        EmitReferenceBuilders(asm, sb);
        EmitHooks(asm, sb);
        EmitTheme(asm, sb);
        EmitEnums(asm, sb);
        EmitPublicTypes(asm, sb);

        return sb.ToString();
    }

    // -----------------------------------------------------------------------

    [RequiresUnreferencedCode(ReflectionJustification)]
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
            .Where(m => !IsObsolete(m))
            .Select(FormatMethod)
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal);

        foreach (var line in methods) sb.AppendLine(line);
        sb.AppendLine();
    }

    [RequiresUnreferencedCode(ReflectionJustification)]
    static void EmitModifiers(Assembly asm, StringBuilder sb)
    {
        sb.AppendLine("## Modifiers (extension methods on Element)");
        sb.AppendLine("# Fluent — preserves concrete element type. Type-specific sugar (e.g. .Bold()");
        sb.AppendLine("# on TextBlockElement) MUST come before generic .Margin/.Padding/etc.");
        sb.AppendLine();

        var lines = asm
            .GetExportedTypes()
            .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && !t.IsGenericType)  // static class
            .Where(t => !IsObsolete(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.IsDefined(typeof(global::System.Runtime.CompilerServices.ExtensionAttribute), false))
            .Where(m => IsElementExtension(m))
            .Where(m => !IsObsolete(m))
            .Select(FormatMethod)
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal);

        foreach (var line in lines) sb.AppendLine(line);
        sb.AppendLine();
    }

    [RequiresUnreferencedCode(ReflectionJustification)]
    static void EmitReferenceBuilders(Assembly asm, StringBuilder sb)
    {
        sb.AppendLine("## Reference builders (custom controls)");
        sb.AppendLine("# Use descriptor.Reference/ReferenceList for regular custom controls;");
        sb.AppendLine("# use binding.Reference/ReferenceList from hand-coded handlers.");
        sb.AppendLine();

        var typeNames = new[]
        {
            "Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor`2",
            "Microsoft.UI.Reactor.Core.V1Protocol.ReactorBinding`1",
        };

        var lines = typeNames
            .Select(asm.GetType)
            .Where(t => t is not null)
            .SelectMany(t => t!.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name is "Reference" or "ReferenceList")
                .Where(m => !IsObsolete(m))
                .Select(m => Short(t) + "." + FormatMethod(m)))
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal);

        foreach (var line in lines) sb.AppendLine(line);
        sb.AppendLine();
    }

    [RequiresUnreferencedCode(ReflectionJustification)]
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
                 .Where(m => !IsObsolete(m))
                 .Select(m => "RenderContext." + FormatMethod(m));

        // Extension hooks (UseValidationContext, UseInfiniteResource, UseAnnounce, ...).
        var extHooks = asm
            .GetExportedTypes()
            .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && !t.IsGenericType)
            .Where(t => !IsObsolete(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.IsDefined(typeof(global::System.Runtime.CompilerServices.ExtensionAttribute), false))
            .Where(IsHookExtension)
            .Where(m => !IsObsolete(m))
            .Select(FormatMethod);

        var lines = instanceHooks.Concat(extHooks)
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal);

        foreach (var line in lines) sb.AppendLine(line);
        sb.AppendLine();
    }

    [RequiresUnreferencedCode(ReflectionJustification)]
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
            .Where(p => !IsObsolete(p))
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
            catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or InvalidOperationException)
            {
                // Theme.* property failed to reflect (e.g. WinUI resource not registered at design-time).
                // Falling through to emit `?` for the resource key — the rest of the index is unaffected.
            }
            sb.AppendLine($"Theme.{p.Name,-32} → {key ?? "?"}");
        }
        sb.AppendLine();
    }

    [RequiresUnreferencedCode(ReflectionJustification)]
    static void EmitEnums(Assembly asm, StringBuilder sb)
    {
        sb.AppendLine("## Enums (public, under Microsoft.UI.Reactor.*)");
        sb.AppendLine();

        var enums = asm.GetExportedTypes()
            .Where(t => t.IsEnum)
            .Where(t => !IsObsolete(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var t in enums)
        {
            var values = Enum.GetNames(t)
                .Where(name => !IsEnumMemberObsolete(t, name));
            sb.AppendLine($"{Short(t)} {{ {string.Join(", ", values)} }}");
        }
        sb.AppendLine();
    }

    // -----------------------------------------------------------------------
    //  Public types — ctors / properties / methods / events on every public type
    //  not already surfaced by the five sections above. Closes the discovery gap
    //  where members like WindowSpec.Opacity / ReactorWindow.SetPosition returned
    //  zero hits in reactor.api.txt.
    // -----------------------------------------------------------------------

    [RequiresUnreferencedCode(ReflectionJustification)]
    static void EmitPublicTypes(Assembly asm, StringBuilder sb)
    {
        sb.AppendLine("## Public types (ctors / properties / methods / events)");
        sb.AppendLine("# Every public type not already covered above. Members alphabetized within");
        sb.AppendLine("# each type: constructors, then properties, then methods, then events.");
        sb.AppendLine();

        var types = asm.GetExportedTypes()
            .Where(IsIndexablePublicType)
            .ToList();

        // Disambiguate `### <kind> <name>` headers for nested types and for two
        // distinct types that share a simple name (different namespaces). A nested
        // type is shown as `Outer.Inner`; a colliding simple name gets a trailing
        // ` (in <namespace>)` suffix so a reader can tell them apart.
        static string BaseDisplayName(Type t) =>
            t.IsNested ? $"{Short(t.DeclaringType!)}.{Short(t)}" : Short(t);

        var baseNameCounts = types
            .GroupBy(BaseDisplayName)
            .ToDictionary(g => g.Key, g => g.Count());

        string DisplayName(Type t)
        {
            var name = BaseDisplayName(t);
            return baseNameCounts.TryGetValue(name, out var n) && n > 1
                ? $"{name} (in {t.Namespace})"
                : name;
        }

        types.Sort((a, b) => StringComparer.Ordinal.Compare(DisplayName(a), DisplayName(b)));

        foreach (var t in types)
        {
            var lines = new List<string>();

            // Constructors.
            foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                if (IsObsolete(c)) continue;
                var ps = string.Join(", ", c.GetParameters().Select(FormatParam));
                lines.Add($"new({ps})");
            }
            lines.Sort(StringComparer.Ordinal);
            var headerEmitted = false;

            void Section(IEnumerable<string> items)
            {
                foreach (var line in items.OrderBy(s => s, StringComparer.Ordinal))
                {
                    if (!headerEmitted)
                    {
                        sb.AppendLine($"### {KindOf(t)} {DisplayName(t)}");
                        headerEmitted = true;
                    }
                    sb.AppendLine(line);
                }
            }

            // Constructors are already sorted above; emit via Section for header logic.
            Section(lines);

            // Properties (instance + static), excluding indexers.
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => !IsObsolete(p))
                .Select(FormatProperty)
                .Where(s => s is not null)
                .Select(s => s!);
            Section(props);

            // Public DeclaredOnly methods — instance AND static (symmetric with the
            // property query). `IsSpecialName` filters operator overloads / accessors.
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Where(m => !m.Name.Contains('<') && !m.Name.Contains('>'))
                .Where(m => !IsObsolete(m))
                .Select(FormatMethod);
            Section(methods);

            // Public events.
            var events = t.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(e => !IsObsolete(e))
                .Select(e => $"event {Short(e.EventHandlerType!)} {e.Name}");
            Section(events);

            if (headerEmitted) sb.AppendLine();
        }
    }

    static bool IsIndexablePublicType(Type t)
    {
        if (t.FullName == "Microsoft.UI.Reactor.Factories") return false;
        if (t.IsEnum) return false;
        if (t.IsClass && t.IsAbstract && t.IsSealed) return false;     // static class
        if (typeof(Delegate).IsAssignableFrom(t)) return false;
        if (t.Name.Contains('<') || t.Name.Contains('>')) return false; // compiler-generated
        if (IsObsolete(t)) return false;
        return true;
    }

    [RequiresUnreferencedCode(ReflectionJustification)]
    static string KindOf(Type t)
    {
        if (t.IsInterface) return "interface";
        // Records (class) synthesize a `<Clone>$` method.
        if (t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null)
            return "record";
        if (t.IsValueType) return "struct";
        return "class";
    }

    static string? FormatProperty(PropertyInfo p)
    {
        var getter = p.GetGetMethod();
        var setter = p.GetSetMethod();
        var hasGet = getter is not null;
        var hasSet = setter is not null;
        if (!hasGet && !hasSet) return null;

        var isInit = false;
        if (setter is not null)
        {
            isInit = setter.ReturnParameter
                .GetRequiredCustomModifiers()
                .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
        }

        var accessors = hasSet ? (isInit ? "get; init;" : "get; set;") : "get;";
        return $"{Short(p.PropertyType)} {p.Name} {{ {accessors} }}";
    }

    // -----------------------------------------------------------------------
    //  Obsolete filtering — keep deprecated surface out of the index so AI agents
    //  don't paste from it. Mirrors what `using` would surface to a real consumer.
    // -----------------------------------------------------------------------

    static bool IsObsolete(MemberInfo m) =>
        m.IsDefined(typeof(ObsoleteAttribute), inherit: false);

    [RequiresUnreferencedCode(ReflectionJustification)]
    static bool IsEnumMemberObsolete(Type enumType, string name)
    {
        var field = enumType.GetField(name, BindingFlags.Public | BindingFlags.Static);
        return field is not null && IsObsolete(field);
    }

    // -----------------------------------------------------------------------
    //  Heuristics
    // -----------------------------------------------------------------------

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

    // -----------------------------------------------------------------------
    //  Formatting
    // -----------------------------------------------------------------------

    static string FormatMethod(MethodInfo m)
    {
        var generics = m.IsGenericMethodDefinition
            ? "<" + string.Join(", ", m.GetGenericArguments().Select(g => g.Name)) + ">"
            : "";

        var isExt = m.IsDefined(typeof(global::System.Runtime.CompilerServices.ExtensionAttribute), false);
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
}
