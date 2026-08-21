using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Registers property-inspection, resource-browsing, and style-inspection
/// MCP tools: <c>properties</c>, <c>setProperty</c>, <c>resources</c>,
/// <c>setResource</c>, <c>styles</c>, <c>ancestors</c>.
/// </summary>
internal static class DevtoolsPropertyTools
{
    public static void Register(DevtoolsMcpServer server, SelectorResolver resolver)
    {
        Register_Properties(server, resolver);
        Register_SetProperty(server, resolver);
        Register_Resources(server, resolver);
        Register_SetResource(server, resolver);
        Register_Styles(server, resolver);
        Register_Ancestors(server, resolver);
    }

    // -- properties --------------------------------------------------------------

    private static void Register_Properties(DevtoolsMcpServer server, SelectorResolver resolver)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "properties",
                Description: "Read dependency properties on a UI element. Pass `name` to read a single property, or omit to enumerate all. Returns value, type, and whether the value is locally set.",
                InputSchema: Schema.Root(
                    new[] { "selector" },
                    ("selector", Schema.Str("Element selector.")),
                    ("name", Schema.Str("Optional DP name (e.g. 'Width', 'Margin'). Use 'Owner.Property' for attached DPs (e.g. 'Grid.Row'). The 'Property' suffix is optional. Omit to list all.")),
                    ("window", Schema.Str("Window id (omit for default).")))),
            (@params) => server.OnDispatcher(() =>
            {
                var selector = RequiredString(@params, "selector");
                var name = DevtoolsTools.ReadString(@params, "name");
                var windowId = DevtoolsTools.ReadString(@params, "window");
                var el = resolver.Resolve(selector, windowId);

                if (name is not null)
                {
                    var (dp, member) = FindDependencyProperty(el, name);
                    var value = el.GetValue(dp);
                    bool isLocal;
                    try { isLocal = !Equals(el.ReadLocalValue(dp), DependencyProperty.UnsetValue); }
                    catch { isLocal = false; }
                    return new PropertyResult(
                        name,
                        FormatValue(value),
                        value?.GetType().Name ?? "null",
                        member.DeclaringType?.Name,
                        isLocal);
                }

                // Enumerate all DPs via reflection on the type hierarchy.
                var props = EnumerateDependencyProperties(el);
                return (object)new PropertiesResult(props.Count, props);
            }));
    }

    // -- setProperty -------------------------------------------------------------

    private static void Register_SetProperty(DevtoolsMcpServer server, SelectorResolver resolver)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "setProperty",
                Description: "Set a dependency property on a UI element. Value is parsed from string (supports Thickness, CornerRadius, Brush hex, enums, bool, double, int).",
                InputSchema: Schema.Root(
                    new[] { "selector", "name", "value" },
                    ("selector", Schema.Str("Element selector.")),
                    ("name", Schema.Str("DP name (e.g. 'Width', 'Margin', 'Background'). Use 'Owner.Property' for attached DPs (e.g. 'Grid.Row').")),
                    ("value", Schema.Str("Value as string (e.g. '10', '1,2,3,4', '#FF0000', 'Visible').")),
                    ("window", Schema.Str("Window id (omit for default).")))),
            (@params) => server.OnDispatcher(() =>
            {
                var selector = RequiredString(@params, "selector");
                var name = RequiredString(@params, "name");
                var raw = RequiredString(@params, "value");
                var windowId = DevtoolsTools.ReadString(@params, "window");
                var el = resolver.Resolve(selector, windowId);

                var (dp, _) = FindDependencyProperty(el, name);

                // Determine the target type from the DP's current value. WinUI DPs
                // don't expose PropertyType directly, so we infer from the current
                // value's type, or fall back to the raw string.
                var currentValue = el.GetValue(dp);
                var targetType = currentValue?.GetType();
                var parsed = ParseValue(raw, targetType);
                el.SetValue(dp, parsed);

                return new SetPropertyResult(true, name, FormatValue(el.GetValue(dp)));
            }));
    }

    // -- resources ---------------------------------------------------------------

    private static void Register_Resources(DevtoolsMcpServer server, SelectorResolver resolver)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "resources",
                Description: "Browse XAML resources. Walks the ResourceDictionary chain from element → ancestor elements → window → application (including MergedDictionaries and ThemeDictionaries). Filter by regex on key.",
                InputSchema: Schema.Root(
                    ("selector", Schema.Str("Element selector (optional — starts walk from this element's Resources).")),
                    ("scope", Schema.Str("'element', 'window', or 'app' (default 'app'). Controls how far up the chain to walk.")),
                    ("filter", Schema.Str("Regex filter on resource key.")),
                    ("window", Schema.Str("Window id (omit for default).")))),
            (@params) => server.OnDispatcher(() =>
            {
                var selector = DevtoolsTools.ReadString(@params, "selector");
                var scope = DevtoolsTools.ReadString(@params, "scope") ?? "app";
                var filter = DevtoolsTools.ReadString(@params, "filter");
                var windowId = DevtoolsTools.ReadString(@params, "window");

                // Validate scope.
                if (scope is not ("element" or "window" or "app"))
                    throw new McpToolException($"Invalid scope '{scope}'. Must be 'element', 'window', or 'app'.", JsonRpcErrorCodes.InvalidParams);

                if (scope is "element" or "window" && selector is null)
                    throw new McpToolException($"Scope '{scope}' requires a selector.", JsonRpcErrorCodes.InvalidParams);

                Regex? filterRe = null;
                if (filter is not null)
                {
                    // SECURITY (TASK-014): cap regex execution to 200ms.
                    try { filterRe = new Regex(filter, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)); }
                    catch { throw new McpToolException($"Invalid regex: {filter}", JsonRpcErrorCodes.InvalidParams); }
                }

                var results = new List<ResourceEntry>();

                // Resolve starting element if given.
                FrameworkElement? startEl = null;
                if (selector is not null)
                    startEl = resolver.Resolve(selector, windowId) as FrameworkElement;

                // Walk resource scopes: element → ancestor elements → window/root → app.
                if (startEl is not null && (scope is "element" or "window" or "app"))
                    CollectResources(startEl.Resources, "element", filterRe, results);

                if (scope is "window" or "app")
                {
                    // Walk visual tree ancestors, collecting each element's Resources.
                    if (startEl is not null)
                    {
                        var parent = VisualTreeHelper.GetParent(startEl) as FrameworkElement;
                        while (parent is not null)
                        {
                            CollectResources(parent.Resources, $"ancestor:{parent.GetType().Name}", filterRe, results);
                            parent = VisualTreeHelper.GetParent(parent) as FrameworkElement;
                        }
                    }
                }

                if (scope is "app")
                    CollectResources(Application.Current.Resources, "app", filterRe, results);

                return new ResourcesResult(results.Count, results);
            }));
    }

    // -- setResource -------------------------------------------------------------

    private static void Register_SetResource(DevtoolsMcpServer server, SelectorResolver resolver)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "setResource",
                Description: "Set or add a XAML resource in a ResourceDictionary at the specified scope. " +
                    "Scope is required ('element' | 'window' | 'application'). 'application' additionally " +
                    "requires confirmAppWide=true since it mutates Application.Current.Resources for the " +
                    "lifetime of the process.",
                InputSchema: Schema.Root(
                    new[] { "key", "value", "scope" },
                    ("key", Schema.Str("Resource key.")),
                    ("value", Schema.Str("Value as string (same parsing as setProperty).")),
                    ("scope", Schema.Str("'element', 'window', or 'application' (no default — explicit choice required).")),
                    ("selector", Schema.Str("Element selector (required when scope is 'element').")),
                    ("window", Schema.Str("Window id (omit for default).")),
                    ("confirmAppWide", Schema.Bool("Required to be true when scope is 'application'. Speed-bump against accidental process-wide mutation.")))),
            (@params) => server.OnDispatcher(() =>
            {
                var key = RequiredString(@params, "key");
                var raw = RequiredString(@params, "value");
                // SECURITY (TASK-012): scope is required; no implicit "app"
                // default. Legacy "app" still accepted as an alias for
                // "application" but the old default-to-app path is gone.
                var scope = RequiredString(@params, "scope");
                if (scope == "app") scope = "application";
                if (scope is not ("element" or "window" or "application"))
                    throw new McpToolException(
                        $"scope must be 'element', 'window', or 'application'; got '{scope}'.",
                        JsonRpcErrorCodes.InvalidParams);
                if (scope == "application")
                {
                    var confirm = DevtoolsTools.ReadBool(@params, "confirmAppWide") ?? false;
                    if (!confirm)
                        throw new McpToolException(
                            "scope='application' mutates Application.Current.Resources for the lifetime of the process. " +
                            "Pass confirmAppWide=true to opt in, or use 'element' / 'window' instead.",
                            JsonRpcErrorCodes.InvalidParams,
                            new McpErrorData("app-wide-confirmation-required"));
                }
                var selector = DevtoolsTools.ReadString(@params, "selector");
                var windowId = DevtoolsTools.ReadString(@params, "window");

                ResourceDictionary dict;
                bool existedAtScope;
                if (scope == "element")
                {
                    if (selector is null)
                        throw new McpToolException("selector is required when scope is 'element'.", JsonRpcErrorCodes.InvalidParams);
                    var el = resolver.Resolve(selector, windowId) as FrameworkElement
                        ?? throw new McpToolException("Element is not a FrameworkElement.", JsonRpcErrorCodes.ToolExecution);
                    dict = el.Resources;
                }
                else if (scope == "window")
                {
                    // Walk up to root from selector, or use app resources.
                    FrameworkElement? root = null;
                    if (selector is not null)
                    {
                        root = resolver.Resolve(selector, windowId) as FrameworkElement;
                        if (root is not null)
                        {
                            var parent = VisualTreeHelper.GetParent(root) as FrameworkElement;
                            while (parent is not null) { root = parent; parent = VisualTreeHelper.GetParent(root) as FrameworkElement; }
                        }
                    }
                    dict = root?.Resources ?? Application.Current.Resources;
                }
                else
                {
                    dict = Application.Current.Resources;
                }

                existedAtScope = dict.ContainsKey(key);

                // Try to infer target type from existing value.
                Type? targetType = null;
                if (dict.ContainsKey(key))
                    targetType = dict[key]?.GetType();

                var parsed = ParseValue(raw, targetType);
                dict[key] = parsed;

                return new SetResourceResult(true, key, FormatValue(parsed), existedAtScope);
            }));
    }

    // -- styles ------------------------------------------------------------------

    private static void Register_Styles(DevtoolsMcpServer server, SelectorResolver resolver)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "styles",
                Description: "Inspect the explicitly-assigned Style on a UI element: TargetType, Setters (property + value), and the BasedOn chain. Note: returns null when only a default/theme style is active — WinUI does not expose the resolved implicit style.",
                InputSchema: Schema.Root(
                    new[] { "selector" },
                    ("selector", Schema.Str("Element selector.")),
                    ("window", Schema.Str("Window id (omit for default).")))),
            (@params) => server.OnDispatcher(() =>
            {
                var selector = RequiredString(@params, "selector");
                var windowId = DevtoolsTools.ReadString(@params, "window");
                var el = resolver.Resolve(selector, windowId);

                if (el is not FrameworkElement fe)
                    throw new McpToolException("Element is not a FrameworkElement.", JsonRpcErrorCodes.ToolExecution);

                var style = fe.Style;
                if (style is null)
                    return new StylesResult(false);

                return new StylesResult(true, DescribeStyle(style));
            }));
    }

    // -- ancestors ---------------------------------------------------------------

    private static void Register_Ancestors(DevtoolsMcpServer server, SelectorResolver resolver)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "ancestors",
                Description: "Walk the visual tree upward from the matched element to the root. Returns type, name, and automationId for each ancestor.",
                InputSchema: Schema.Root(
                    new[] { "selector" },
                    ("selector", Schema.Str("Element selector.")),
                    ("window", Schema.Str("Window id (omit for default).")))),
            (@params) => server.OnDispatcher(() =>
            {
                var selector = RequiredString(@params, "selector");
                var windowId = DevtoolsTools.ReadString(@params, "window");
                var el = resolver.Resolve(selector, windowId);

                var chain = new List<AncestorEntry>();
                var current = VisualTreeHelper.GetParent(el);
                while (current is not null)
                {
                    var fe = current as FrameworkElement;
                    chain.Add(new AncestorEntry(
                        current.GetType().Name,
                        fe?.Name,
                        fe is not null
                            ? Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(fe)
                            : null));
                    current = VisualTreeHelper.GetParent(current);
                }
                return new AncestorsResult(chain.Count, chain);
            }));
    }

    // -- helpers ------------------------------------------------------------------

    private static string RequiredString(JsonElement? args, string key) =>
        DevtoolsTools.ReadString(args, key)
            ?? throw new McpToolException($"Missing required argument '{key}'.",
                JsonRpcErrorCodes.InvalidParams);

    /// <summary>Find a static DependencyProperty member on the element's type hierarchy,
    /// or on an owner type for attached properties (e.g. "Grid.Row").</summary>
    /// <remarks>
    /// A DP static can be either a field or a property depending on where the type
    /// comes from: C#-authored DPs (Reactor's own, third-party controls) are
    /// <c>public static readonly</c> <b>fields</b>, while CsWinRT projects the WinUI
    /// ones as static <b>properties</b> — <c>typeof(Button)</c> exposes zero DP-typed
    /// static fields and 112 DP-typed static properties. Checking only fields is what
    /// made every WinUI lookup here fail (issue #1109), so both member kinds are
    /// resolved, fields first.
    /// </remarks>
    private static (DependencyProperty dp, MemberInfo member) FindDependencyProperty(UIElement el, string name)
    {
        // Support attached property syntax: "Grid.Row" → look on Grid type.
        if (name.Contains('.'))
        {
            var parts = name.Split('.', 2);

            // Search well-known WinUI namespaces for the owner type.
            var ownerType = FindTypeByName(parts[0]);
            if (ownerType is not null && TryReadDependencyPropertyStatic(ownerType, ToMemberName(parts[1])) is { } attached)
                return attached;

            throw new McpToolException(
                $"No attached DependencyProperty '{name}' found. Check the owner type name.{TrimmedMetadataHint(ownerType)}",
                JsonRpcErrorCodes.InvalidParams);
        }

        var memberName = ToMemberName(name);
        for (var type = el.GetType(); type is not null; type = type.BaseType)
        {
            if (TryReadDependencyPropertyStatic(type, memberName) is { } found)
                return found;
        }

        throw new McpToolException(
            $"No DependencyProperty '{name}' found on {el.GetType().Name} or its base types. For attached properties, use 'OwnerType.Property' syntax (e.g. 'Grid.Row').{TrimmedMetadataHint(el.GetType())}",
            JsonRpcErrorCodes.InvalidParams);
    }

    /// <summary>Convention: the DP behind property "Foo" is the static member "FooProperty".</summary>
    private static string ToMemberName(string name) =>
        name.EndsWith("Property", StringComparison.Ordinal) ? name : name + "Property";

    /// <summary>
    /// Distinguish "you named it wrong" from "the runtime can no longer see it".
    /// </summary>
    /// <remarks>
    /// Under NativeAOT / trimming, ILCompiler emits no reflection metadata for the
    /// CsWinRT-projected DP statics unless something roots <c>PublicProperties</c> on
    /// those types, so a perfectly valid name reads back as "not found" (issue #1109,
    /// docs/aot-support.md). A type that reports <i>zero</i> DP statics is the tell:
    /// every live WinUI element has dozens, so an empty result means the metadata went
    /// away, not that the caller typed the name wrong. Runs on the failure path only.
    /// </remarks>
    private static string TrimmedMetadataHint(Type? type) =>
        type is null || DeclaresAnyDependencyProperty(type)
            ? string.Empty
            : " Note: this type reports no DependencyProperty statics at all, so its reflection"
                + " metadata was most likely trimmed — the property may exist but be undiscoverable"
                + " in this build (see docs/aot-support.md).";

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Best-effort diagnostic probe over a Type that carries no DynamicallyAccessedMembers annotation, used only to word a failure message. Returning false because the trimmer removed the members is exactly the condition it reports.")]
    private static bool DeclaresAnyDependencyProperty(Type type)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        return type.GetFields(Flags).Any(f => f.FieldType == typeof(DependencyProperty))
            || type.GetProperties(Flags).Any(p => p.PropertyType == typeof(DependencyProperty));
    }

    /// <summary>
    /// Read the DependencyProperty exposed by the static member <paramref name="memberName"/>
    /// on <paramref name="type"/>, as a field or (for CsWinRT-projected WinUI types) a
    /// static property. Returns null when the member is absent, is not DP-typed, or
    /// cannot be read.
    /// </summary>
    /// <remarks>
    /// Neither lookup catches <see cref="AmbiguousMatchException"/>: for a name-only
    /// lookup the binder applies hide-by-name and resolves to the most-derived
    /// declaration, which <c>Devtools_Dp_Shadowed{Property,Field}ResolvesToDerived</c>
    /// pins for both member kinds. A speculative catch there would have been an
    /// untestable branch.
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Devtools discovers DependencyProperty statics (fields on C#-authored types, CsWinRT-projected properties on WinUI ones) by reflecting over a Type that carries no DynamicallyAccessedMembers annotation. A member the trimmer removed reads back as null and is reported as not found.")]
    private static (DependencyProperty dp, MemberInfo member)? TryReadDependencyPropertyStatic(Type type, string memberName)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        var field = type.GetField(memberName, Flags);
        if (field is not null && field.FieldType == typeof(DependencyProperty)
            && ReadStatic(() => field.GetValue(null)) is DependencyProperty fieldDp)
        {
            return (fieldDp, field);
        }

        // CanRead guards the GetValue below: reading a write-only static throws
        // ArgumentException, which ReadStatic deliberately does not catch.
        var property = type.GetProperty(memberName, Flags);
        if (property is not null && property.PropertyType == typeof(DependencyProperty) && property.CanRead
            && ReadStatic(() => property.GetValue(null)) is DependencyProperty propertyDp)
        {
            return (propertyDp, property);
        }

        return null;
    }

    /// <summary>
    /// Read a static DP member, mapping a failing static initializer / WinRT
    /// activation onto "not found" instead of surfacing the raw exception through
    /// the MCP transport.
    /// </summary>
    /// <remarks>
    /// <see cref="TypeInitializationException"/> needs its own arm because the two
    /// runtimes disagree. On CoreCLR/JIT, reflection wraps it in a
    /// <see cref="TargetInvocationException"/> (measured for field reads and property
    /// getters, first and subsequent access alike), so the first arm would suffice.
    /// On NativeAOT it comes back bare, and since it derives from
    /// <see cref="SystemException"/> rather than <see cref="MemberAccessException"/>,
    /// nothing else here catches it — one control with a failing static constructor
    /// would take down the whole <c>properties</c> call. Devtools is a debugging tool
    /// pointed at whatever the app happens to contain, so it has to survive that.
    /// <para>
    /// <see cref="ArgumentException"/> is deliberately absent: that is what
    /// <c>GetValue(null)</c> throws for a write-only property, and swallowing it here
    /// would hide the fact that the <c>CanRead</c> guard in
    /// <see cref="TryReadDependencyPropertyStatic"/> had stopped working.
    /// </para>
    /// </remarks>
    private static object? ReadStatic(Func<object?> read)
    {
        try { return read(); }
        catch (TargetInvocationException) { return null; }
        catch (TypeInitializationException) { return null; }
        catch (MemberAccessException) { return null; }
    }

    /// <summary>Resolve a short type name to a WinUI type (Grid, Canvas, ToolTipService, etc.).</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Devtools uses Assembly.GetType to resolve WinUI type names at runtime.")]
    private static Type? FindTypeByName(string name)
    {
        // Search the Microsoft.UI.Xaml assemblies.
        var candidates = new[]
        {
            typeof(Grid).Assembly,          // Microsoft.WinUI
            typeof(UIElement).Assembly,      // Microsoft.WinUI
        };
        foreach (var asm in candidates.Distinct())
        {
            foreach (var ns in new[] { "Microsoft.UI.Xaml.Controls", "Microsoft.UI.Xaml", "Microsoft.UI.Xaml.Media", "Microsoft.UI.Xaml.Controls.Primitives" })
            {
                var type = asm.GetType($"{ns}.{name}");
                if (type is not null) return type;
            }
        }
        return null;
    }

    /// <summary>Enumerate all public static DependencyProperty members on the element's type chain.</summary>
    /// <remarks>
    /// Both member kinds are walked for the reason described on
    /// <see cref="FindDependencyProperty"/>: WinUI's DP statics are CsWinRT-projected
    /// static <b>properties</b>, C#-authored ones are static <b>fields</b>. Fields are
    /// read first within each type, and the <c>seen</c> set keys on the trimmed
    /// property name, so a DP exposed as both kinds is reported once.
    /// </remarks>
    private static List<PropertyResult> EnumerateDependencyProperties(UIElement el)
    {
        var seen = new HashSet<string>();
        var results = new List<PropertyResult>();

        for (var type = el.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            CollectDependencyProperties(el, type, seen, results);
        }

        return results;
    }

    /// <summary>Append the DP statics declared directly on <paramref name="type"/> to <paramref name="results"/>.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Devtools enumerates DependencyProperty statics (fields on C#-authored types, CsWinRT-projected properties on WinUI ones) over a Type that carries no DynamicallyAccessedMembers annotation. The result is a best-effort inventory for a diagnostic tool: a member the trimmer removed is simply absent from the listing, which degrades the output rather than breaking the tool.")]
    private static void CollectDependencyProperties(
        UIElement el,
        Type type,
        HashSet<string> seen,
        List<PropertyResult> results)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var f in type.GetFields(Flags))
        {
            if (f.FieldType != typeof(DependencyProperty)) continue;
            AddDependencyProperty(el, type, f.Name, () => f.GetValue(null), seen, results);
        }

        foreach (var p in type.GetProperties(Flags))
        {
            if (p.PropertyType != typeof(DependencyProperty) || !p.CanRead) continue;
            AddDependencyProperty(el, type, p.Name, () => p.GetValue(null), seen, results);
        }
    }

    /// <summary>Read one DP static and append its current value, skipping names already reported.</summary>
    private static void AddDependencyProperty(
        UIElement el,
        Type declaringType,
        string memberName,
        Func<object?> read,
        HashSet<string> seen,
        List<PropertyResult> results)
    {
        var propName = memberName.EndsWith("Property", StringComparison.Ordinal)
            ? memberName[..^"Property".Length]
            : memberName;

        if (seen.Contains(propName)) return;

        // Claim the name only once a DP has actually been read. A member that exists
        // but can't be read (failed initializer / WinRT activation) must not consume
        // the name, or a readable same-named member of the other kind further up the
        // base chain — a derived static property shadowing a base static field, say —
        // would be skipped and the DP would vanish from the listing entirely.
        if (ReadStatic(read) is not DependencyProperty dp) return;

        seen.Add(propName);

        // el.GetValue / ReadLocalValue run arbitrary WinUI property-system code —
        // including third-party property getters and changed-callbacks — once for
        // each of the ~112 DPs on a control, across the WinRT ABI. The set of
        // exception types that can come back is therefore open-ended, so these stay
        // broad on purpose: narrowing to a fixed list would mean one control outside
        // that list turns the whole `properties` listing into a JSON-RPC error
        // (McpDispatcher's own catch) instead of degrading a single row. What the
        // catch must not do is *hide* the failure, so the type is reported.
        object? value;
        try { value = el.GetValue(dp); }
        catch (Exception ex) { value = $"<error: {ex.GetType().Name}>"; }

        // No equivalent channel for this one — it feeds a bool — so a probe failure
        // is reported as "not locally set". The value column above still carries the
        // error when the DP is unreadable at all.
        var isLocal = false;
        try { isLocal = !Equals(el.ReadLocalValue(dp), DependencyProperty.UnsetValue); }
        catch (Exception) { isLocal = false; }

        results.Add(new PropertyResult(
            propName,
            FormatValue(value),
            value?.GetType().Name ?? "null",
            declaringType.Name,
            isLocal));
    }

    /// <summary>Format a DP value to a JSON-friendly string.</summary>
    internal static string? FormatValue(object? value)
    {
        if (value is null) return null;
        return value switch
        {
            SolidColorBrush b => $"#{b.Color.A:X2}{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}",
            Thickness t => string.Create(
                CultureInfo.InvariantCulture,
                $"{t.Left},{t.Top},{t.Right},{t.Bottom}"),
            CornerRadius cr => string.Create(
                CultureInfo.InvariantCulture,
                $"{cr.TopLeft},{cr.TopRight},{cr.BottomRight},{cr.BottomLeft}"),
            global::Windows.UI.Color c => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}",
            Brush _ => value.GetType().Name, // LinearGradientBrush etc. — just report type
            IFormattable f => f.ToString(format: null, formatProvider: CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    /// <summary>Parse a string value into a typed object, guided by an optional target type hint.</summary>
    internal static object? ParseValue(string raw, Type? targetType)
    {
        // Enum parse first if we have a target type that's an enum.
        if (targetType is not null && targetType.IsEnum)
        {
            if (Enum.TryParse(targetType, raw, ignoreCase: true, out var enumVal))
                return enumVal;
        }

        // Well-known WinUI types.
        if (targetType == typeof(Visibility) || raw.Equals("Visible", StringComparison.OrdinalIgnoreCase))
            if (Enum.TryParse<Visibility>(raw, ignoreCase: true, out var vis)) return vis;

        if (targetType == typeof(HorizontalAlignment))
            if (Enum.TryParse<HorizontalAlignment>(raw, ignoreCase: true, out var ha)) return ha;

        if (targetType == typeof(VerticalAlignment))
            if (Enum.TryParse<VerticalAlignment>(raw, ignoreCase: true, out var va)) return va;

        // Bool.
        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

        // Thickness: "1" → uniform, "1,2" → (left/right, top/bottom), "1,2,3,4"
        if (targetType == typeof(Thickness) || (raw.Contains(',') && TryParseThickness(raw, out var thickness)))
            if (TryParseThickness(raw, out thickness)) return thickness;

        // CornerRadius: same pattern.
        if (targetType == typeof(CornerRadius) || (raw.Contains(',') && TryParseCornerRadius(raw, out var cr)))
            if (TryParseCornerRadius(raw, out cr)) return cr;

        // Brush / Color: hex string.
        if (raw.StartsWith('#'))
        {
            if (TryParseColor(raw, out var color))
                return new SolidColorBrush(color);
        }

        // Double.
        if (targetType == typeof(double) || raw.Contains('.'))
            if (double.TryParse(raw, CultureInfo.InvariantCulture, out var d)) return d;

        // Int.
        if (targetType == typeof(int))
            if (int.TryParse(raw, CultureInfo.InvariantCulture, out var i)) return i;

        // Double fallback for numeric strings.
        if (double.TryParse(raw, CultureInfo.InvariantCulture, out var dbl)) return dbl;

        // If a targetType was specified and we fell through, the input is invalid for that type.
        if (targetType is not null)
            throw new McpToolException($"Cannot parse '{raw}' as {targetType.Name}.", JsonRpcErrorCodes.InvalidParams);

        // String fallback.
        return raw;
    }

    internal static bool TryParseThickness(string raw, out Thickness result)
    {
        result = default;
        var parts = raw.Split(',');
        switch (parts.Length)
        {
            case 1 when double.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, out var u):
                result = new Thickness(u);
                return true;
            case 2 when double.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, out var lr) && double.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture, out var tb):
                result = new Thickness(lr, tb, lr, tb);
                return true;
            case 4 when double.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, out var l) && double.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture, out var t)
                     && double.TryParse(parts[2].Trim(), CultureInfo.InvariantCulture, out var r) && double.TryParse(parts[3].Trim(), CultureInfo.InvariantCulture, out var b):
                result = new Thickness(l, t, r, b);
                return true;
            default:
                return false;
        }
    }

    internal static bool TryParseCornerRadius(string raw, out CornerRadius result)
    {
        result = default;
        var parts = raw.Split(',');
        switch (parts.Length)
        {
            case 1 when double.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, out var u):
                result = new CornerRadius(u);
                return true;
            case 4 when double.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, out var tl) && double.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture, out var tr)
                     && double.TryParse(parts[2].Trim(), CultureInfo.InvariantCulture, out var br) && double.TryParse(parts[3].Trim(), CultureInfo.InvariantCulture, out var bl):
                result = new CornerRadius(tl, tr, br, bl);
                return true;
            default:
                return false;
        }
    }

    internal static bool TryParseColor(string hex, out global::Windows.UI.Color color)
    {
        color = default;
        var h = hex.TrimStart('#');
        try
        {
            switch (h.Length)
            {
                case 3:
                    // Expand #RGB → #RRGGBB
                    color = global::Windows.UI.Color.FromArgb(0xFF,
                        byte.Parse($"{h[0]}{h[0]}", NumberStyles.HexNumber),
                        byte.Parse($"{h[1]}{h[1]}", NumberStyles.HexNumber),
                        byte.Parse($"{h[2]}{h[2]}", NumberStyles.HexNumber));
                    return true;
                case 6:
                    color = global::Windows.UI.Color.FromArgb(0xFF,
                        byte.Parse(h[0..2], NumberStyles.HexNumber),
                        byte.Parse(h[2..4], NumberStyles.HexNumber),
                        byte.Parse(h[4..6], NumberStyles.HexNumber));
                    return true;
                case 8:
                    color = global::Windows.UI.Color.FromArgb(
                        byte.Parse(h[0..2], NumberStyles.HexNumber),
                        byte.Parse(h[2..4], NumberStyles.HexNumber),
                        byte.Parse(h[4..6], NumberStyles.HexNumber),
                        byte.Parse(h[6..8], NumberStyles.HexNumber));
                    return true;
                default:
                    return false;
            }
        }
        catch { return false; }
    }

    private static void CollectResources(ResourceDictionary dict, string scope, Regex? filter, List<ResourceEntry> results)
    {
        foreach (var key in dict.Keys)
        {
            var keyStr = key?.ToString() ?? "";
            if (filter is not null)
            {
                bool match;
                // SECURITY (TASK-014): the regex carries a 200ms MatchTimeout;
                // a pathological pattern+key combo throws — treat as non-match.
                try { match = filter.IsMatch(keyStr); }
                catch (RegexMatchTimeoutException) { match = false; }
                if (!match) continue;
            }

            object? val;
            try { val = dict[key]; }
            catch { val = null; }

            results.Add(new ResourceEntry(
                keyStr,
                val?.GetType().Name ?? "null",
                FormatValue(val),
                scope));
        }

        // Walk MergedDictionaries.
        foreach (var merged in dict.MergedDictionaries)
            CollectResources(merged, scope + "/merged", filter, results);

        // Walk ThemeDictionaries.
        foreach (var kvp in dict.ThemeDictionaries)
        {
            if (kvp.Value is ResourceDictionary themeDict)
            {
                var themeName = kvp.Key?.ToString() ?? "unknown";
                CollectResources(themeDict, $"{scope}/theme:{themeName}", filter, results);
            }
        }
    }

    private static StyleInfo DescribeStyle(Style style)
    {
        var setters = new List<StyleSetterInfo>();
        foreach (var setterBase in style.Setters)
        {
            if (setterBase is Setter setter)
            {
                setters.Add(new StyleSetterInfo(
                    setter.Property?.ToString() ?? "unknown",
                    FormatValue(setter.Value),
                    setter.Value?.GetType().Name ?? "null"));
            }
        }

        return new StyleInfo(
            style.TargetType?.Name,
            setters.Count,
            setters,
            style.BasedOn is not null ? DescribeStyle(style.BasedOn) : null);
    }
}

// -- DevtoolsPropertyTools result shapes -----------------------------------------
// Every dynamic DP / resource / style value is reduced to a string by FormatValue,
// so these are fully closed, source-generated records (registered in
// DevtoolsJsonContext), and the serialized payloads no longer need the reflection
// resolver fallback. The tool *logic* still introspects via reflection
// (EnumerateDependencyProperties, DescribeStyle).
internal sealed record PropertyResult(string Name, string? Value, string ValueType, string? DeclaringType, bool IsLocal);

internal sealed record PropertiesResult(int Count, IReadOnlyList<PropertyResult> Properties);

internal sealed record SetPropertyResult(bool Ok, string Name, string? NewValue) : IOkResult;

internal sealed record ResourceEntry(string Key, string ValueType, string? Value, string Scope);

internal sealed record ResourcesResult(int Count, IReadOnlyList<ResourceEntry> Resources);

internal sealed record SetResourceResult(bool Ok, string Key, string? NewValue, bool Replaced) : IOkResult;

internal sealed record StyleSetterInfo(string Property, string? Value, string ValueType);

internal sealed record StyleInfo(
    string? TargetType,
    int SetterCount,
    IReadOnlyList<StyleSetterInfo> Setters,
    StyleInfo? BasedOn);

internal sealed record StylesResult(bool HasStyle, StyleInfo? Style = null);

internal sealed record AncestorEntry(string Type, string? Name, string? AutomationId);

internal sealed record AncestorsResult(int Count, IReadOnlyList<AncestorEntry> Ancestors);