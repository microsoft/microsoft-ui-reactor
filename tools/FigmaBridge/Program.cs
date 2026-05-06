// FigmaBridge — WebSocket bridge that receives Figma design data and writes Reactor .cs files.
// Changes written to disk trigger dotnet watch hot reload on the target app.

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:9228");

var app = builder.Build();
app.UseWebSockets();

// CORS for Figma plugin iframe (null origin)
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
    context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
    context.Response.Headers.Append("Access-Control-Allow-Headers", "*");
    if (context.Request.Method == "OPTIONS")
    {
        context.Response.StatusCode = 204;
        return;
    }
    await next();
});

// Parse --output <dir> from command line for target project directory
var outputDir = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
if (outputDir == null)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("[FigmaBridge] WARNING: No --output directory specified.");
    Console.WriteLine("[FigmaBridge] Usage: dotnet run -- --output <path-to-reactor-app-dir>");
    Console.WriteLine("[FigmaBridge] Example: dotnet run -- --output C:\\repos\\microsoft-ui-reactor\\samples\\apps\\figma-codegen-test");
    Console.ResetColor();
    outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
}
Directory.CreateDirectory(outputDir);
Console.WriteLine($"[FigmaBridge] Output directory: {outputDir}");
Console.WriteLine($"[FigmaBridge] Listening on ws://localhost:9228/figma");

// Debounce state
var debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
FigmaSyncMessage? pendingMessage = null;
var syncLock = new object();

debounceTimer.Elapsed += (_, _) =>
{
    FigmaSyncMessage? msg;
    lock (syncLock) { msg = pendingMessage; pendingMessage = null; }
    if (msg != null) ProcessSync(msg, outputDir);
};

app.Map("/figma", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("WebSocket expected");
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine("[FigmaBridge] Client connected");

    // Send ack
    var ack = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "connected" }));
    await ws.SendAsync(ack, WebSocketMessageType.Text, true, CancellationToken.None);

    var buffer = new byte[1024 * 256]; // 256KB buffer
    while (ws.State == WebSocketState.Open)
    {
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            Console.WriteLine("[FigmaBridge] Client disconnected");
            break;
        }

        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        try
        {
            var msg = JsonSerializer.Deserialize<FigmaSyncMessage>(json);
            if (msg != null)
            {
                Console.WriteLine($"[FigmaBridge] Received: {msg.Type} — {msg.FrameName}");
                lock (syncLock) { pendingMessage = msg; }
                debounceTimer.Stop();
                debounceTimer.Start();

                var response = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                    new { type = "ack", frameId = msg.FrameId }));
                await ws.SendAsync(response, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FigmaBridge] Parse error: {ex.Message}");
        }
    }
});

app.Map("/health", () => Results.Ok(new { status = "ok", output = outputDir }));

app.Run();

// ─── Sync Processing ─────────────────────────────────────────────────────────

static void ProcessSync(FigmaSyncMessage msg, string outputDir)
{
    Console.WriteLine($"[FigmaBridge] Processing sync for frame: {msg.FrameName}");

    var componentName = SanitizeComponentName(msg.FrameName);
    var code = GenerateReactorCode(msg, componentName);
    var filePath = Path.Combine(outputDir, "Program.cs");

    // Only write if content changed
    if (File.Exists(filePath))
    {
        var existing = File.ReadAllText(filePath);
        if (existing == code)
        {
            Console.WriteLine($"[FigmaBridge] No changes detected, skipping write");
            return;
        }
    }

    File.WriteAllText(filePath, code);
    Console.WriteLine($"[FigmaBridge] Wrote {filePath} ({code.Length} chars)");
}

static string SanitizeComponentName(string name)
{
    // Convert Figma frame name to a valid C# class name
    var sanitized = new string(name
        .Replace(" ", "")
        .Replace("-", "")
        .Replace("/", "")
        .Where(c => char.IsLetterOrDigit(c) || c == '_')
        .ToArray());

    if (sanitized.Length == 0 || char.IsDigit(sanitized[0]))
        sanitized = "FigmaComponent" + sanitized;

    return sanitized;
}

static string GenerateReactorCode(FigmaSyncMessage msg, string componentName)
{
    var sb = new StringBuilder();
    sb.AppendLine("// ═══════════════════════════════════════════════════════════");
    sb.AppendLine($"// FIGMA LIVE SYNC — Auto-generated from Figma");
    sb.AppendLine($"// Frame: {msg.FrameName} ({msg.FrameId})");
    sb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine($"// DO NOT EDIT — this file is overwritten on each Figma change");
    sb.AppendLine("// ═══════════════════════════════════════════════════════════");
    sb.AppendLine();
    sb.AppendLine("using Microsoft.UI.Reactor;");
    sb.AppendLine("using Microsoft.UI.Reactor.Core;");
    sb.AppendLine("using Microsoft.UI.Reactor.Layout;");
    sb.AppendLine("using Microsoft.UI.Xaml;");
    sb.AppendLine("using Microsoft.UI.Xaml.Automation.Peers;");
    sb.AppendLine("using static Microsoft.UI.Reactor.Factories;");
    sb.AppendLine("using static Microsoft.UI.Reactor.Core.Theme;");
    sb.AppendLine();
    sb.AppendLine($"ReactorApp.Run<{componentName}>(\"App name\", width: 1316, height: 865");
    sb.AppendLine("#if DEBUG");
    sb.AppendLine("    , devtools: true");
    sb.AppendLine("#endif");
    sb.AppendLine(");");
    sb.AppendLine();
    sb.AppendLine($"class {componentName} : Component");
    sb.AppendLine("{");
    sb.AppendLine("    public override Element Render()");
    sb.AppendLine("    {");
    sb.AppendLine("        var controlCR = ThemeResource.CornerRadius(\"ControlCornerRadius\");");
    sb.AppendLine("        var overlayCR = ThemeResource.CornerRadius(\"OverlayCornerRadius\");");
    sb.AppendLine();

    if (msg.Tree != null)
    {
        var element = GenerateElement(msg.Tree, 2);
        sb.AppendLine($"        return {element};");
    }
    else
    {
        sb.AppendLine("        return TextBlock(\"Empty frame\");");
    }

    sb.AppendLine("    }");
    sb.AppendLine("}");
    return sb.ToString();
}

static string GenerateElement(FigmaNode node, int depth)
{
    // Prevent infinite recursion on deeply nested trees
    if (depth > 20) return "";

    var indent = new string(' ', depth * 4);

    // Skip decorative/structural elements that aren't real content
    if (IsDecorativeNode(node)) return "";

    // Text node → TextBlock with semantic style
    if (node.Type == "TEXT" && node.Characters != null)
    {
        // Skip icon font glyphs (Fluent icons, Segoe Fluent, symbol fonts)
        if (IsIconFont(node)) return "";
        return GenerateTextElement(node, indent);
    }

    // Instance → try to map to a WinUI control
    // Match on componentName (resolved) OR node.Name (fallback for community library instances)
    if (node.Type == "INSTANCE")
    {
        if (IsDecorativeInstance(node)) return "";
        var mapped = MapComponent(node, indent);
        if (mapped != null) return mapped;
    }

    // Frame/Group with children → layout container
    if (node.Children is { Count: > 0 })
    {
        return GenerateContainer(node, depth);
    }

    // Leaf frame/rectangle → Border placeholder
    if (node.Type is "RECTANGLE" or "ELLIPSE" or "LINE")
    {
        if (node.Type == "LINE")
            return "Border(VStack()).Height(1)\n" +
                   $"{indent}    .Background(DividerStroke)\n" +
                   $"{indent}    .HAlign(HorizontalAlignment.Stretch)";

        // Skip large rectangles that are just background fills
        if (node.Width > 200 && node.Height > 200) return "";
        return $"Border(VStack()).Width({Round4(node.Width)}).Height({Round4(node.Height)})";
    }

    return $"/* [{node.Type}] {Escape(node.Name)} */\n{indent}VStack()";
}

static string GenerateTextElement(FigmaNode node, string indent)
{
    var text = Escape(node.Characters ?? "");
    var size = node.FontSize ?? 14;
    var weight = node.FontWeight ?? 400;

    // Map to semantic text factories per figma.md typography + design.md §4
    string element;
    if (size <= 12) element = $"Caption(\"{text}\")";
    else if (size <= 14 && weight >= 600) element = $"TextBlock(\"{text}\").SemiBold()";
    else if (size <= 14) element = $"TextBlock(\"{text}\")";
    else if (size <= 18 && weight >= 600) element = $"TextBlock(\"{text}\").ApplyStyle(\"BodyLargeTextBlockStyle\").SemiBold()";
    else if (size <= 18) element = $"TextBlock(\"{text}\").ApplyStyle(\"BodyLargeTextBlockStyle\")";
    else if (size <= 20 && weight >= 600) element = $"SubHeading(\"{text}\")";
    else if (size <= 28) element = $"Heading(\"{text}\")";
    else if (size <= 40) element = $"TextBlock(\"{text}\").ApplyStyle(\"TitleLargeTextBlockStyle\")";
    else element = $"TextBlock(\"{text}\").ApplyStyle(\"DisplayTextBlockStyle\")";

    // Resolve text foreground color from fills (figma.md token resolution)
    var fg = ResolveTextForeground(node);
    if (fg != null) element += $"\n{indent}    .Foreground({fg})";

    // Body text that could wrap
    if (size >= 14 && text.Length > 60)
        element += $"\n{indent}    .TextWrapping(TextWrapping.WrapWholeWords)";

    return element;
}

static string? MapComponent(FigmaNode node, string indent)
{
    var comp = node.ComponentName?.ToLowerInvariant() ?? "";
    var nodeName = node.Name.ToLowerInvariant();
    // Use both component name and node name for matching — community library
    // components often can't resolve componentName across files, so the Figma
    // instance name (node.Name) is the primary fallback.
    var match = comp.Length > 0 ? comp : nodeName;
    // Helper: check if the component or node name matches a pattern
    bool has(string pattern) => comp.Contains(pattern) || nodeName.Contains(pattern);

    // ═══════════════════════════════════════════════════════════════════════
    // Windows UI Kit → Reactor WinUI Control Mapping
    // Source: figma.com/design/t7yLwpMUOWJSYt5ahz3ROC/Windows-UI-kit--Community-
    // ═══════════════════════════════════════════════════════════════════════

    // ── Shell / Navigation ──────────────────────────────────────────────
    if (has("title bar") && !has("caption") && !has("icon"))
    {
        var title = FindChildText(node) ?? "App";
        return $"TitleBar(\"{Escape(title)}\")";
    }
    if (has("side nav") && !has("list item") && !has("menu") && !has("parts"))
    {
        var navItems = ExtractNavItems(node);
        if (navItems.Count > 0)
        {
            var itemsStr = string.Join($",\n{indent}        ", navItems);
            return $"NavigationView(\n{indent}    [\n{indent}        {itemsStr}\n{indent}    ],\n{indent}    VStack()\n{indent}) with {{ IsSettingsVisible = true, IsPaneOpen = true }}";
        }
    }
    if (has("breadcrumb"))
    {
        var items = new List<string>();
        ExtractBreadcrumbItems(node, items);
        if (items.Count > 0)
        {
            var itemsStr = string.Join(", ", items);
            return $"BreadcrumbBar([{itemsStr}])";
        }
    }
    if (has("tab view") || has("tabview"))
    {
        return $"TabView(Tab(\"Tab 1\", VStack()), Tab(\"Tab 2\", VStack()))";
    }
    if (has("pivot"))
        return $"Pivot()";

    // ── Buttons ─────────────────────────────────────────────────────────
    if (has("button") && !has("hyperlink") && !has("toggle")
        && !has("radio") && !has("split") && !has("dropdown")
        && !has("repeat") && !has("caption") && !has("menu"))
    {
        var text = FindChildText(node) ?? "Button";
        if (HasAccentFill(node))
            return $"Button(\"{Escape(text)}\", () => {{ }})\n" +
                   $"{indent}    .Resources(r => r\n" +
                   $"{indent}        .Set(\"ButtonBackground\", Accent)\n" +
                   $"{indent}        .Set(\"ButtonBackgroundPointerOver\", AccentSecondary)\n" +
                   $"{indent}        .Set(\"ButtonBackgroundPressed\", AccentTertiary)\n" +
                   $"{indent}        .Set(\"ButtonForeground\", Ref(\"TextOnAccentFillColorPrimaryBrush\")))";
        return $"Button(\"{Escape(text)}\", () => {{ }})";
    }
    if (has("hyperlink"))
    {
        var text = FindChildText(node) ?? "Link";
        return $"HyperlinkButton(\"{Escape(text)}\")";
    }
    if (has("toggle") && has("button"))
    {
        var text = FindChildText(node) ?? "Toggle";
        return $"ToggleButton(\"{Escape(text)}\", isChecked: false, onToggled: on => {{ }})";
    }
    if (has("split") && has("button"))
    {
        var text = FindChildText(node) ?? "Split";
        return $"SplitButton(\"{Escape(text)}\", () => {{ }})";
    }
    if (has("dropdown") && has("button"))
    {
        var text = FindChildText(node) ?? "Menu";
        return $"DropDownButton(\"{Escape(text)}\")";
    }
    if (has("repeat") && has("button"))
    {
        var text = FindChildText(node) ?? "+";
        return $"RepeatButton(\"{Escape(text)}\", () => {{ }})";
    }

    // ── Selection / Toggle ──────────────────────────────────────────────
    if (has("checkbox"))
    {
        var text = FindChildText(node) ?? "";
        return $"CheckBox(false, label: \"{Escape(text)}\")";
    }
    if (has("toggle") && has("switch"))
        return "ToggleSwitch(false)";
    if (has("radio") && has("button"))
    {
        var text = FindChildText(node) ?? "Option";
        return $"RadioButton(\"{Escape(text)}\")";
    }
    if (has("combo") && has("box"))
        return "ComboBox([\"Option 1\", \"Option 2\", \"Option 3\"], 0)";
    if (has("slider"))
        return "Slider(50, min: 0, max: 100, onValueChanged: (s, e) => { })";
    if (has("rating"))
        return "RatingControl(3)";
    if (has("color") && has("picker"))
        return "ColorPicker()";

    // ── Text Input ──────────────────────────────────────────────────────
    if (has("auto suggest") || (has("auto") && has("suggest")))
    {
        return "AutoSuggestBox(\"\")";
    }
    if (has("text") && (has("box") || has("field") || has("input")))
    {
        var placeholder = FindChildText(node) ?? "Enter text";
        return $"TextField(\"\", placeholder: \"{Escape(placeholder)}\")";
    }
    if (has("password"))
        return "PasswordBox(\"\")";
    if (has("number") && has("box"))
        return "NumberBox(0)";
    if (has("rich") && has("edit"))
        return "RichEditBox()";
    if (has("search"))
    {
        return "AutoSuggestBox(\"\")";
    }

    // ── Status & Info ───────────────────────────────────────────────────
    if (has("info") && has("bar"))
        return "InfoBar(title: \"Info\", message: \"Message\", severity: InfoBarSeverity.Informational)";
    if (has("info") && has("badge"))
        return "InfoBadge()";
    if (has("badge"))
        return "InfoBadge()";    if (has("progress") && has("bar"))
        return "ProgressIndeterminate()";
    if (has("progress") && has("ring"))
        return "ProgressRing()";
    if (has("teaching") && has("tip"))
    {
        var text = FindChildText(node) ?? "Tip";
        return $"TeachingTip(\"{Escape(text)}\")";
    }
    if (has("tooltip"))
        return null; // tooltips are applied as modifiers, not standalone

    // ── Lists & Collections ─────────────────────────────────────────────
    if (has("list") && has("item") && !has("nav"))
    {
        var text = FindChildText(node) ?? "Item";
        return $"TextBlock(\"{Escape(text)}\")";
    }
    if (has("tree") && has("view"))
        return "TreeView()";

    // ── Dialogs & Flyouts ───────────────────────────────────────────────
    if (has("content") && has("dialog"))
        return "ContentDialog(\"Title\", TextBlock(\"Content\"), \"OK\")";
    if (has("flyout") && has("menu"))
    {
        var text = FindChildText(node) ?? "Menu";
        return $"/* FlyoutMenu: {Escape(text)} */\n{indent}VStack()";
    }

    // ── Layout & Containers ─────────────────────────────────────────────
    if (has("expander"))
    {
        var text = FindChildText(node) ?? "Expander";
        return $"Expander(\"{Escape(text)}\", VStack())";
    }
    if (has("scroll") && has("bar"))
        return null; // scrollbars are implicit in ScrollView

    // ── Date & Time ─────────────────────────────────────────────────────
    if (has("calendar") && has("date"))
        return "CalendarDatePicker()";
    if (has("date") && has("picker"))
        return "DatePicker()";
    if (has("time") && has("picker"))
        return "TimePicker()";
    if (has("calendar") && has("view"))
        return "CalendarView()";

    // ── Media ───────────────────────────────────────────────────────────
    if (has("person") && has("picture"))
        return "PersonPicture()";
    if (has("image") || has("media"))
        return $"Image(\"placeholder\").Width({Round4(node.Width)}).Height({Round4(node.Height)})";

    // ── Menus & Toolbars ────────────────────────────────────────────────
    if (has("command") && has("bar"))
        return "CommandBar()";
    if (has("menu") && has("bar"))
        return "MenuBar()";

    // ── Heading / Footer (custom kit components) ────────────────────────
    if (has("heading") || nodeName.Contains("heading"))
        return null; // let children render individually
    if (has("footer"))
        return null; // let children render individually

    // ── Side nav parts ──────────────────────────────────────────────────
    if (has("nav") && has("list item"))
    {
        var text = FindChildText(node) ?? "Item";
        return $"TextBlock(\"{Escape(text)}\")";
    }
    if (has("menu button") || has("back"))
        return null; // NavigationView handles these internally

    // ── Decorative / structural (skip) ──────────────────────────────────
    if (has("canvas") && !has("canvascontrol"))
        return null; // Figma canvas icon placeholder
    if (has(".icon"))
        return null; // icon placeholder

    return null;
}

static void ExtractBreadcrumbItems(FigmaNode node, List<string> items)
{
    if (node.Type == "TEXT" && !string.IsNullOrWhiteSpace(node.Characters) && !IsIconFont(node))
    {
        var text = node.Characters.Trim();
        if (text.Length > 0 && text != ">" && text != "/")
            items.Add($"Breadcrumb(\"{Escape(text)}\")");
    }
    if (node.Children != null)
        foreach (var c in node.Children) ExtractBreadcrumbItems(c, items);
}

static string? FindChildText(FigmaNode node)
{
    if (node.Type == "TEXT" && !string.IsNullOrWhiteSpace(node.Characters))
        return node.Characters;
    if (node.Children == null) return null;
    foreach (var child in node.Children)
    {
        // Skip icon font text
        if (child.FontFamily != null && child.FontFamily.Contains("Fluent"))
            continue;
        if (child.FontFamily != null && child.FontFamily.Contains("Symbol"))
            continue;
        var text = FindChildText(child);
        if (text != null) return text;
    }
    return null;
}

static string GenerateContainer(FigmaNode node, int depth)
{
    var indent = new string(' ', depth * 4);
    var childIndent = new string(' ', (depth + 1) * 4);

    var children = node.Children!
        .Select(c => GenerateElement(c, depth + 1))
        .Where(c => !string.IsNullOrWhiteSpace(c))
        .ToList();

    if (children.Count == 0)
        return "";

    // If only one real child, unwrap — don't add a redundant container
    if (children.Count == 1 && node.LayoutMode is null or "NONE"
        && !HasVisibleFill(node) && !HasVisibleStroke(node) && (node.CornerRadius ?? 0) == 0)
        return children[0];

    var childrenStr = string.Join($",\n{childIndent}", children);

    string container;
    var gap = Round4(node.ItemSpacing ?? 0);

    if (node.LayoutMode == "VERTICAL")
        container = gap > 0 ? $"VStack({gap},\n{childIndent}{childrenStr})" : $"VStack(\n{childIndent}{childrenStr})";
    else if (node.LayoutMode == "HORIZONTAL")
        container = gap > 0 ? $"HStack({gap},\n{childIndent}{childrenStr})" : $"HStack(\n{childIndent}{childrenStr})";
    else
        container = children.Count == 1
            ? $"Border(\n{childIndent}{childrenStr})"
            : $"VStack(\n{childIndent}{childrenStr})";

    // Add padding via wrapping Border (VStack/HStack don't support Padding)
    var pt = Round4(node.PaddingTop ?? 0);
    var pr = Round4(node.PaddingRight ?? 0);
    var pb = Round4(node.PaddingBottom ?? 0);
    var pl = Round4(node.PaddingLeft ?? 0);

    var hasPadding = pt > 0 || pr > 0 || pb > 0 || pl > 0;
    var hasFill = HasVisibleFill(node);
    var hasStroke = HasVisibleStroke(node);
    var hasRadius = (node.CornerRadius ?? 0) > 0;

    // Detect card pattern: frame with fill + stroke + corner radius (figma.md surface elements)
    var isCard = hasFill && hasStroke && hasRadius;

    // Wrap in Border when we need padding, background, border, or corner radius
    if (hasPadding || hasFill || hasStroke || hasRadius)
    {
        if (node.LayoutMode is "VERTICAL" or "HORIZONTAL")
        {
            container = $"Border(\n{childIndent}{container})";
        }

        // Apply padding
        if (hasPadding)
        {
            if (pt == pb && pl == pr && pt == pl)
                container += $".Padding({pt})";
            else
                container += $"\n{indent}    .Padding({pl}, {pt}, {pr}, {pb})";
        }

        // Apply background from fills (figma.md token resolution)
        var bg = ResolveFillToken(node);
        if (bg != null)
            container += $"\n{indent}    .Background({bg})";

        // Apply border from strokes
        if (hasStroke)
        {
            var strokeToken = ResolveStrokeToken(node);
            container += $"\n{indent}    .WithBorder({strokeToken}, 1)";
        }

        // Apply corner radius (design.md §5: use theme resources)
        if (hasRadius)
        {
            var cr = node.CornerRadius!.Value;
            if (cr <= 5)
                container += $"\n{indent}    .CornerRadius(controlCR.TopLeft)";
            else
                container += $"\n{indent}    .CornerRadius(overlayCR.TopLeft)";
        }
    }
    else if (hasPadding && node.LayoutMode is "VERTICAL" or "HORIZONTAL")
    {
        if (pt == pb && pl == pr && pt == pl)
            container = $"Border(\n{childIndent}{container}).Padding({pt})";
        else
            container = $"Border(\n{childIndent}{container}).Padding({pl}, {pt}, {pr}, {pb})";
    }

    return container;
}

// ─── Decorative Node Detection ───────────────────────────────────────────────

static bool IsDecorativeNode(FigmaNode node)
{
    var name = node.Name.ToLowerInvariant();

    string[] decorativeNames = [
        "base", "shadow", "stroke", "fill", "selector", "mask",
        "gradient", "backdrop", "gripper", "spacer", "divider line",
        "fixed-aspect-ratio", "aspect ratio", ".ruler"
    ];

    // Skip nodes with decorative names (fills, shadows, strokes, selectors)
    if (decorativeNames.Any(d => name.Contains(d)))
    {
        // Exception: keep if it has meaningful text children
        if (node.Children?.Any(c => c.Type == "TEXT" && !string.IsNullOrWhiteSpace(c.Characters) && !IsIconFont(c)) == true)
            return false;
        return true;
    }

    // Skip "Surface / App Surface" and similar surface instances
    if (name.Contains("surface")) return true;

    return false;
}

static bool IsDecorativeInstance(FigmaNode node)
{
    var compName = node.ComponentName?.ToLowerInvariant() ?? "";
    var name = node.Name.ToLowerInvariant();
    // Skip scroll bars, rulers, surfaces, grippers, caption controls, icons
    bool check(string p) => compName.Contains(p) || name.Contains(p);
    return check("scroll bar")
        || check("ruler")
        || (check("surface") && !check("nav") && !check("title"))
        || check("gripper")
        || check("caption control")
        || check(".ruler")
        || (name == ".icon");
}

static bool IsIconFont(FigmaNode node)
{
    var family = node.FontFamily?.ToLowerInvariant() ?? "";
    return family.Contains("fluent")
        || family.Contains("symbol")
        || family.Contains("segoe fluent")
        || family.Contains("mwf");
}

static int Round4(double value)
{
    var rounded = (int)(Math.Round(value / 4.0) * 4);
    return Math.Max(0, rounded);
}

static string Escape(string s) =>
    s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

// ─── Token Resolution (figma.md §Token Resolution) ──────────────────────────

static string? ResolveFillToken(FigmaNode node)
{
    if (node.Fills == null) return null;
    var fill = node.Fills.FirstOrDefault(f => f.Visible && f.Type == "SOLID" && f.Color != null);
    if (fill?.Color == null) return null;

    var r = (int)(fill.Color.R * 255);
    var g = (int)(fill.Color.G * 255);
    var b = (int)(fill.Color.B * 255);
    var opacity = fill.Opacity ?? 1.0;

    return MatchColorToToken(r, g, b, opacity, isBackground: true);
}

static string ResolveStrokeToken(FigmaNode node)
{
    // Default to CardStroke for stroked containers
    return "CardStroke";
}

static string? ResolveTextForeground(FigmaNode node)
{
    if (node.Fills == null) return null;
    var fill = node.Fills.FirstOrDefault(f => f.Visible && f.Type == "SOLID" && f.Color != null);
    if (fill?.Color == null) return null;

    var r = (int)(fill.Color.R * 255);
    var g = (int)(fill.Color.G * 255);
    var b = (int)(fill.Color.B * 255);
    var opacity = fill.Opacity ?? 1.0;

    return MatchColorToToken(r, g, b, opacity, isBackground: false);
}

static string? MatchColorToToken(int r, int g, int b, double opacity, bool isBackground)
{
    // Skip fully opaque black text (default PrimaryText — don't set explicitly per design.md §9)
    if (!isBackground && r < 30 && g < 30 && b < 30 && opacity > 0.9) return null;
    // Skip white-on-dark that's just primary text
    if (!isBackground && r > 225 && g > 225 && b > 225 && opacity > 0.9) return null;

    // ── Text foreground tokens ──
    if (!isBackground)
    {
        // Secondary text: mid-gray or reduced opacity
        if (opacity < 0.7) return "SecondaryText";
        if (r > 90 && r < 170 && g > 90 && g < 170 && b > 90 && b < 170) return "SecondaryText";
        // Tertiary text: lighter gray
        if (r > 170 && r < 220 && g > 170 && g < 220 && b > 170 && b < 220) return "TertiaryText";
        // Accent text: blue-ish
        if (b > 150 && b > r && b > g) return "AccentText";
        // White text on accent: very bright
        if (r > 240 && g > 240 && b > 240) return "Ref(\"TextOnAccentFillColorPrimaryBrush\")";
    }

    // ── Background tokens ──
    if (isBackground)
    {
        // Card background: white or near-white with transparency
        if (r > 240 && g > 240 && b > 240 && opacity < 0.8) return "CardBackground";
        // Layer fill: white with moderate transparency
        if (r > 240 && g > 240 && b > 240 && opacity < 0.6) return "LayerFill";
        // Solid white background
        if (r > 250 && g > 250 && b > 250 && opacity > 0.9) return "SolidBackground";
        // Control fill: light gray with transparency
        if (r > 240 && g > 240 && b > 240 && opacity < 0.9) return "ControlFill";
        // Accent: blue (0, 95, 184 is WinUI default accent)
        if (b > 150 && b > r * 1.5 && b > g * 1.5) return "Accent";
        // Subtle fill
        if (r > 230 && g > 230 && b > 230) return "SubtleFill";
        // Dark background (dark mode base)
        if (r < 50 && g < 50 && b < 50) return "SolidBackground";
        // App base gray (243,243,243 is the standard light theme base)
        if (r > 235 && r < 248 && g > 235 && g < 248 && b > 235 && b < 248 && opacity > 0.9)
            return "SolidBackground";
    }

    return null;
}

static bool HasAccentFill(FigmaNode node)
{
    // Check the node and its children for accent-colored fills
    if (node.Fills != null)
    {
        foreach (var f in node.Fills.Where(f => f.Visible && f.Type == "SOLID" && f.Color != null))
        {
            var b = (int)(f.Color!.B * 255);
            var r = (int)(f.Color.R * 255);
            var g = (int)(f.Color.G * 255);
            if (b > 150 && b > r * 1.5 && b > g * 1.5) return true;
        }
    }
    if (node.Children != null)
        return node.Children.Any(HasAccentFill);
    return false;
}

static bool HasVisibleFill(FigmaNode node) =>
    node.Fills?.Any(f => f.Visible && f.Type == "SOLID" && f.Color != null) == true;

static bool HasVisibleStroke(FigmaNode node) =>
    node.Strokes?.Any(s => s.Visible) == true;

static List<string> ExtractNavItems(FigmaNode navNode)
{
    var items = new List<string>();
    ExtractNavItemsRecursive(navNode, items);
    return items;
}

static void ExtractNavItemsRecursive(FigmaNode node, List<string> items)
{
    if (node.ComponentName?.ToLowerInvariant().Contains("list item") == true)
    {
        var text = FindChildText(node) ?? "Item";
        items.Add($"NavItem(\"{Escape(text)}\", icon: \"\\uE80F\", tag: \"{Escape(text).ToLowerInvariant()}\")");
        return;
    }
    if (node.Children != null)
    {
        foreach (var child in node.Children)
            ExtractNavItemsRecursive(child, items);
    }
}

// ─── Fill/Stroke Model Extensions ────────────────────────────────────────────

record FillColor
{
    [JsonPropertyName("r")] public double R { get; init; }
    [JsonPropertyName("g")] public double G { get; init; }
    [JsonPropertyName("b")] public double B { get; init; }
    [JsonPropertyName("a")] public double A { get; init; }
}

// ─── Models ──────────────────────────────────────────────────────────────────

record FigmaSyncMessage
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("frameId")] public string FrameId { get; init; } = "";
    [JsonPropertyName("frameName")] public string FrameName { get; init; } = "";
    [JsonPropertyName("timestamp")] public long Timestamp { get; init; }
    [JsonPropertyName("tree")] public FigmaNode? Tree { get; init; }
}

record FigmaNode
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("visible")] public bool Visible { get; init; } = true;
    [JsonPropertyName("layoutMode")] public string? LayoutMode { get; init; }
    [JsonPropertyName("itemSpacing")] public double? ItemSpacing { get; init; }
    [JsonPropertyName("paddingTop")] public double? PaddingTop { get; init; }
    [JsonPropertyName("paddingRight")] public double? PaddingRight { get; init; }
    [JsonPropertyName("paddingBottom")] public double? PaddingBottom { get; init; }
    [JsonPropertyName("paddingLeft")] public double? PaddingLeft { get; init; }
    [JsonPropertyName("cornerRadius")] public double? CornerRadius { get; init; }
    [JsonPropertyName("width")] public double Width { get; init; }
    [JsonPropertyName("height")] public double Height { get; init; }
    [JsonPropertyName("characters")] public string? Characters { get; init; }
    [JsonPropertyName("fontSize")] public double? FontSize { get; init; }
    [JsonPropertyName("fontWeight")] public double? FontWeight { get; init; }
    [JsonPropertyName("fontFamily")] public string? FontFamily { get; init; }
    [JsonPropertyName("lineHeight")] public double? LineHeight { get; init; }
    [JsonPropertyName("componentName")] public string? ComponentName { get; init; }
    [JsonPropertyName("fills")] public List<FigmaFillData>? Fills { get; init; }
    [JsonPropertyName("strokes")] public List<FigmaStrokeData>? Strokes { get; init; }
    [JsonPropertyName("children")] public List<FigmaNode>? Children { get; init; }
}

record FigmaFillData
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("visible")] public bool Visible { get; init; } = true;
    [JsonPropertyName("color")] public FillColor? Color { get; init; }
    [JsonPropertyName("opacity")] public double? Opacity { get; init; }
}

record FigmaStrokeData
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("visible")] public bool Visible { get; init; } = true;
    [JsonPropertyName("weight")] public double? Weight { get; init; }
}
