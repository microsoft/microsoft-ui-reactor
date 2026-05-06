// FigmaBridge v2 — MCP data relay for Figma design data.
// Receives design tree from Figma plugin via WebSocket, exposes it to
// AI agents (Copilot CLI) via MCP JSON-RPC tools. No code generation —
// the LLM agent interprets the design using figma.md + design.md skills.

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:9228");

var app = builder.Build();
app.UseWebSockets();

// CORS for Figma plugin iframe and MCP clients
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

// ─── Shared State ────────────────────────────────────────────────────────────

FigmaSyncMessage? latestSync = null;
var syncLock = new object();
var changeSignal = new SemaphoreSlim(0);
var figmaConnected = false;

// Output directory for codegen patches (--output flag)
var outputDir = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
if (outputDir != null)
{
    if (outputDir.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        outputDir = Path.GetDirectoryName(outputDir) ?? outputDir;
    Directory.CreateDirectory(outputDir);
}

// LLM configuration: --llm-endpoint <url> --llm-key <key> --llm-model <model>
// Or env vars: LLM_ENDPOINT, LLM_API_KEY, LLM_MODEL
// Supports OpenAI, Azure OpenAI, GitHub Models, Ollama — any OpenAI-compatible chat API
var llmEndpoint = args.SkipWhile(a => a != "--llm-endpoint").Skip(1).FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("LLM_ENDPOINT")
    ?? "https://models.inference.ai.azure.com"; // GitHub Models default
var llmKey = args.SkipWhile(a => a != "--llm-key").Skip(1).FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("LLM_API_KEY")
    ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
var llmModel = args.SkipWhile(a => a != "--llm-model").Skip(1).FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("LLM_MODEL")
    ?? "gpt-4o";

// Load skill files for LLM prompt construction
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
var figmaSkill = TryReadFile(Path.Combine(repoRoot, "skills", "figma.md"));
var designSkill = TryReadFile(Path.Combine(repoRoot, "skills", "design.md"));
var mainSkill = TryReadFile(Path.Combine(repoRoot, "SKILL.md"));

static string TryReadFile(string path) =>
    File.Exists(path) ? File.ReadAllText(path) : $"[File not found: {path}]";

Console.WriteLine("[FigmaBridge] MCP relay server starting");
Console.WriteLine("[FigmaBridge] WebSocket: ws://localhost:9228/figma (for Figma plugin)");
Console.WriteLine("[FigmaBridge] MCP:       POST http://localhost:9228/mcp (for AI agents)");
Console.WriteLine($"[FigmaBridge] LLM:       {llmEndpoint} (model: {llmModel})");
Console.WriteLine($"[FigmaBridge] LLM key:   {(llmKey.Length > 0 ? $"configured ({llmKey.Length} chars)" : "NOT SET — set LLM_API_KEY or GITHUB_TOKEN")}");
if (outputDir != null)
    Console.WriteLine($"[FigmaBridge] Output:    {outputDir} (for code generation + live patching)");
else
    Console.WriteLine("[FigmaBridge] No --output dir — generation disabled (set --output)");

// ─── WebSocket Endpoint (Figma Plugin) ───────────────────────────────────────

app.Map("/figma", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("WebSocket expected");
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    figmaConnected = true;
    Console.WriteLine("[FigmaBridge] Figma plugin connected");

    var ack = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "connected" }));
    await ws.SendAsync(ack, WebSocketMessageType.Text, true, CancellationToken.None);

    var buffer = new byte[1024 * 512]; // 512KB buffer for large trees
    while (ws.State == WebSocketState.Open)
    {
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            figmaConnected = false;
            Console.WriteLine("[FigmaBridge] Figma plugin disconnected");
            break;
        }

        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        try
        {
            var jsonDoc = JsonDocument.Parse(json);
            var msgType = jsonDoc.RootElement.GetProperty("type").GetString() ?? "";

            if (msgType == "set-output")
            {
                var path = jsonDoc.RootElement.GetProperty("path").GetString() ?? "";
                if (!string.IsNullOrEmpty(path))
                {
                    // If user passed a .csproj path, use its directory
                    if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                        path = Path.GetDirectoryName(path) ?? path;
                    outputDir = path;
                    Directory.CreateDirectory(outputDir);
                    Console.WriteLine($"[FigmaBridge] Output set to: {outputDir}");
                    var resp = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                        new { type = "output-set", path = outputDir }));
                    await ws.SendAsync(resp, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            else if (msgType == "create-project")
            {
                var name = jsonDoc.RootElement.GetProperty("name").GetString() ?? "FigmaApp";
                var projectDir = Path.Combine(repoRoot, "samples", "apps", name.ToLowerInvariant());
                try
                {
                    var csprojPath = Path.Combine(projectDir, $"{name}.csproj");
                    if (Directory.Exists(projectDir) && File.Exists(csprojPath))
                    {
                        // Project already exists — just set output to it
                        outputDir = projectDir;
                        Console.WriteLine($"[FigmaBridge] Project exists, using: {projectDir}");
                        var resp = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                            new { type = "project-created", path = projectDir }));
                        await ws.SendAsync(resp, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    else
                    {
                        Directory.CreateDirectory(projectDir);
                        var csproj = $"""
                            <Project Sdk="Microsoft.NET.Sdk">
                              <PropertyGroup>
                                <OutputType>WinExe</OutputType>
                                <TargetFramework>net9.0-windows10.0.22621.0</TargetFramework>
                                <Platforms>x64;ARM64</Platforms>
                                <ImplicitUsings>enable</ImplicitUsings>
                                <Nullable>enable</Nullable>
                                <UseWinUI>true</UseWinUI>
                                <WindowsPackageType>None</WindowsPackageType>
                              </PropertyGroup>
                              <ItemGroup>
                                <PackageReference Include="Microsoft.WindowsAppSDK" Version="$(WindowsAppSDKVersion)" />
                              </ItemGroup>
                              <ItemGroup>
                                <ProjectReference Include="..\..\..\src\Reactor\Reactor.csproj" />
                              </ItemGroup>
                            </Project>
                            """;
                        File.WriteAllText(csprojPath, csproj);
                        File.WriteAllText(Path.Combine(projectDir, "Program.cs"),
                            "using Microsoft.UI.Reactor;\nusing static Microsoft.UI.Reactor.Factories;\n\n" +
                            $"ReactorApp.Run<{name}App>(\"App\", width: 1200, height: 800);\n\n" +
                            $"class {name}App : Component\n{{\n    public override Element Render() =>\n        TextBlock(\"Waiting for Figma generation...\");\n}}\n");

                        outputDir = projectDir;
                        Console.WriteLine($"[FigmaBridge] Created project: {projectDir}");
                        var resp = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                            new { type = "project-created", path = projectDir }));
                        await ws.SendAsync(resp, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    var resp = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                        new { type = "error", message = $"Failed to create project: {ex.Message}" }));
                    await ws.SendAsync(resp, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            else if (msgType == "browse-output")
            {
                // Launch Windows PowerShell with a picker script file
                try
                {
                    var initDir = (outputDir ?? Path.Combine(repoRoot, "samples", "apps")).Replace("\\", "\\\\");
                    var resultFile = Path.Combine(Path.GetTempPath(), "figma-picker-result.txt").Replace("\\", "\\\\");
                    if (File.Exists(resultFile.Replace("\\\\", "\\"))) File.Delete(resultFile.Replace("\\\\", "\\"));

                    var scriptFile = Path.Combine(Path.GetTempPath(), "figma-picker.ps1");
                    File.WriteAllText(scriptFile,
                        "Add-Type -AssemblyName System.Windows.Forms\n" +
                        "$d = New-Object System.Windows.Forms.OpenFileDialog\n" +
                        "$d.Title = 'Select Reactor .csproj file'\n" +
                        "$d.Filter = 'C# Project (*.csproj)|*.csproj'\n" +
                        $"$d.InitialDirectory = '{initDir}'\n" +
                        "if ($d.ShowDialog() -eq 'OK') {\n" +
                        $"  $d.FileName | Out-File '{resultFile}' -NoNewline\n" +
                        "}\n");

                    Console.WriteLine($"[FigmaBridge] Opening file picker...");
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -STA -File \"{scriptFile}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    var proc = System.Diagnostics.Process.Start(psi);
                    proc?.WaitForExit(60000);

                    var actualResultFile = resultFile.Replace("\\\\", "\\");
                    if (File.Exists(actualResultFile))
                    {
                        var selected = File.ReadAllText(actualResultFile).Trim();
                        File.Delete(actualResultFile);
                        if (!string.IsNullOrEmpty(selected))
                        {
                            outputDir = Path.GetDirectoryName(selected)!;
                            Console.WriteLine($"[FigmaBridge] Output set via picker: {outputDir}");
                            var resp = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                                new { type = "output-set", path = outputDir }));
                            await ws.SendAsync(resp, WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        else
                        {
                            var resp = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                                new { type = "status", message = "Picker cancelled" }));
                            await ws.SendAsync(resp, WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                    }
                    else
                    {
                        var resp = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                            new { type = "status", message = "Picker cancelled" }));
                        await ws.SendAsync(resp, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FigmaBridge] Picker error: {ex.Message}");
                    var resp = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                        new { type = "error", message = $"Picker failed: {ex.Message}" }));
                    await ws.SendAsync(resp, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            else if (msgType == "launch-watch")
            {
                if (outputDir == null)
                {
                    var resp = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                        new { type = "error", message = "No output directory set" }));
                    await ws.SendAsync(resp, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                else
                {
                    // Find the .csproj in the output dir
                    var csprojFile = Directory.GetFiles(outputDir, "*.csproj").FirstOrDefault();
                    if (csprojFile != null)
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "pwsh",
                            Arguments = $"-NoExit -Command \"dotnet watch run --project '{csprojFile}'\"",
                            WorkingDirectory = outputDir,
                            UseShellExecute = true,
                            CreateNoWindow = false,
                        };
                        System.Diagnostics.Process.Start(psi);
                        Console.WriteLine($"[FigmaBridge] Launched dotnet watch: {csprojFile}");
                        var resp = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                            new { type = "watch-launched", project = csprojFile }));
                        await ws.SendAsync(resp, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    else
                    {
                        var resp = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                            new { type = "error", message = $"No .csproj found in {outputDir}" }));
                        await ws.SendAsync(resp, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
            }
            else if (msgType == "incremental" && outputDir != null)
            {
                // Incremental changes received — request a full sync to diff the tree
                // The tree differ handles text, spacing, padding, width, height, and radius
                Console.WriteLine($"[FigmaBridge] Incremental change — waiting for full sync to diff");
                var response = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                    new { type = "request-sync" }));
                await ws.SendAsync(response, WebSocketMessageType.Text, true, CancellationToken.None);
                await ws.SendAsync(response, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            else
            {
                var msg = JsonSerializer.Deserialize<FigmaSyncMessage>(json);
                if (msg == null) continue;

                Console.WriteLine($"[FigmaBridge] Received: {msg.Type} — {msg.FrameName} ({msg.FrameId})");

                if (msg.Type == "generate" && msg.Tree != null)
                {
                    if (outputDir == null)
                    {
                        var errMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                            new { type = "error", message = "Set the output project directory first (use 'Set Output' or '+ New Project')" }));
                        await ws.SendAsync(errMsg, WebSocketMessageType.Text, true, CancellationToken.None);
                        lock (syncLock) { latestSync = msg; }
                        continue;
                    }
                    // ── Phase 1: LLM Generation ──────────────────────────
                    lock (syncLock) { latestSync = msg; }

                    var statusMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                        new { type = "status", message = "Generating code via LLM..." }));
                    await ws.SendAsync(statusMsg, WebSocketMessageType.Text, true, CancellationToken.None);

                    try
                    {
                        var summary = DesignSummarizer.Summarize(msg);
                        Console.WriteLine($"[FigmaBridge] Design summary: {summary.Length} chars");

                        var code = await LlmGenerator.Generate(
                            summary, figmaSkill, designSkill, mainSkill,
                            llmEndpoint, llmKey, llmModel, outputDir);

                        if (code == "LAUNCHED")
                        {
                            var doneMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                                new { type = "generating", message = "Copilot CLI launched — watch the terminal window" }));
                            await ws.SendAsync(doneMsg, WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        else if (code != null)
                        {
                            var filePath = Path.Combine(outputDir, "Program.cs");
                            File.WriteAllText(filePath, code);
                            Console.WriteLine($"[FigmaBridge] ✓ Wrote {filePath} ({code.Length} chars)");

                            var doneMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                                new { type = "generated", file = filePath, chars = code.Length }));
                            await ws.SendAsync(doneMsg, WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        else
                        {
                            var errMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                                new { type = "error", message = "LLM returned no code" }));
                            await ws.SendAsync(errMsg, WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FigmaBridge] LLM error: {ex.Message}");
                        var errMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                            new { type = "error", message = ex.Message }));
                        await ws.SendAsync(errMsg, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                else
                {
                    // ── Full sync: store tree + text diff patch ──────────
                    FigmaSyncMessage? oldSync;
                    lock (syncLock) { oldSync = latestSync; }

                    if (outputDir != null && oldSync?.Tree != null && msg.Tree != null
                        && msg.Type == "full-sync")
                    {
                        var textPatches = TreeDiffPatcher.ApplyDiff(oldSync.Tree, msg.Tree, outputDir);
                        if (textPatches > 0)
                            Console.WriteLine($"[FigmaBridge] Applied {textPatches} text patches via diff");
                    }

                    lock (syncLock) { latestSync = msg; }
                    changeSignal.Release();

                    var response = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                        new { type = "ack", frameId = msg.FrameId }));
                    await ws.SendAsync(response, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FigmaBridge] Parse error: {ex.Message}");
        }
    }
});

// ─── MCP JSON-RPC Endpoint (AI Agents) ───────────────────────────────────────

// MCP endpoint — handle both /mcp and / (some MCP clients POST to root)
async Task HandleMcpRequest(HttpContext context)
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    var request = JsonSerializer.Deserialize<JsonRpcRequest>(body);
    if (request == null)
    {
        context.Response.StatusCode = 400;
        return;
    }

    object? result = request.Method switch
    {
        "initialize" => new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { tools = new { } },
            serverInfo = new { name = "figma-bridge", version = "2.0.0" }
        },

        "tools/list" => new
        {
            tools = new object[]
            {
                new
                {
                    name = "figma_summary",
                    description = "Returns a compact design intent summary of the current Figma frame, optimized for LLM consumption. Includes: page sections, component instances mapped to WinUI control names, text content with typography classification, color fills with candidate Theme token names, layout structure, and spacing. Much smaller than figma_tree — use this as the primary input for code generation.",
                    inputSchema = new { type = "object", properties = new { } }
                },
                new
                {
                    name = "figma_tree",
                    description = "Returns the full Figma design tree as raw JSON. Large output (~300KB). Prefer figma_summary for code generation — use this only when you need raw node-level detail.",
                    inputSchema = new { type = "object", properties = new { } }
                },
                new
                {
                    name = "figma_status",
                    description = "Returns the connection status of the Figma plugin and info about the currently watched frame.",
                    inputSchema = new { type = "object", properties = new { } }
                },
                new
                {
                    name = "figma_watch",
                    description = "Blocks until the Figma design changes (or timeout). Returns the updated design summary. Use this in a loop to react to live Figma edits. Timeout default: 30 seconds.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            timeout_seconds = new { type = "number", description = "Max seconds to wait for a change. Default: 30." }
                        }
                    }
                }
            }
        },

        "tools/call" => await HandleToolCall(request.Params),
        _ => (object)new { error = $"Unknown method: {request.Method}" }
    };

    var response = new
    {
        jsonrpc = "2.0",
        result,
        id = request.Id
    };

    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(response);
}

app.MapPost("/mcp", HandleMcpRequest);
app.MapPost("/", HandleMcpRequest);

async Task<object> HandleToolCall(JsonElement? paramsEl)
{
    var toolName = paramsEl?.GetProperty("name").GetString() ?? "";
    var args = paramsEl?.TryGetProperty("arguments", out var a) == true ? a : (JsonElement?)null;

    return toolName switch
    {
        "figma_summary" => HandleFigmaSummary(),
        "figma_tree" => HandleFigmaTree(),
        "figma_status" => HandleFigmaStatus(),
        "figma_watch" => await HandleFigmaWatch(args),
        _ => new { content = new[] { new { type = "text", text = $"Unknown tool: {toolName}" } }, isError = true }
    };
}

object HandleFigmaSummary()
{
    FigmaSyncMessage? sync;
    lock (syncLock) { sync = latestSync; }

    if (sync?.Tree == null)
    {
        return new
        {
            content = new[] { new { type = "text", text = "No design data available. Make sure the Figma plugin is running and a frame is selected." } },
            isError = true
        };
    }

    var summary = DesignSummarizer.Summarize(sync);
    return new
    {
        content = new[] { new { type = "text", text = summary } }
    };
}

object HandleFigmaTree()
{
    FigmaSyncMessage? sync;
    lock (syncLock) { sync = latestSync; }

    if (sync?.Tree == null)
    {
        return new
        {
            content = new[] { new { type = "text", text = "No design data available. Make sure the Figma plugin is running and a frame is selected." } },
            isError = true
        };
    }

    var treeJson = JsonSerializer.Serialize(sync, new JsonSerializerOptions { WriteIndented = true });
    return new
    {
        content = new[] { new
        {
            type = "text",
            text = $"# Figma Design Tree\n\n**Frame:** {sync.FrameName} ({sync.FrameId})\n**Timestamp:** {DateTimeOffset.FromUnixTimeMilliseconds(sync.Timestamp):yyyy-MM-dd HH:mm:ss}\n\n```json\n{treeJson}\n```"
        }}
    };
}

object HandleFigmaStatus()
{
    FigmaSyncMessage? sync;
    lock (syncLock) { sync = latestSync; }

    return new
    {
        content = new[] { new
        {
            type = "text",
            text = JsonSerializer.Serialize(new
            {
                connected = figmaConnected,
                hasDesignData = sync != null,
                watchedFrame = sync != null ? new { id = sync.FrameId, name = sync.FrameName } : null,
                lastUpdate = sync != null ? DateTimeOffset.FromUnixTimeMilliseconds(sync.Timestamp).ToString("o") : null
            }, new JsonSerializerOptions { WriteIndented = true })
        }}
    };
}

async Task<object> HandleFigmaWatch(JsonElement? args)
{
    var timeoutSec = 30;
    if (args?.TryGetProperty("timeout_seconds", out var t) == true)
        timeoutSec = t.GetInt32();

    // Drain any existing signals
    while (changeSignal.CurrentCount > 0)
        await changeSignal.WaitAsync(0);

    // Wait for the next change
    var changed = await changeSignal.WaitAsync(TimeSpan.FromSeconds(timeoutSec));

    if (!changed)
    {
        return new
        {
            content = new[] { new { type = "text", text = "No changes detected within timeout." } }
        };
    }

    // Return the updated summary (compact, LLM-optimized)
    return HandleFigmaSummary();
}

// ─── Health Endpoint ─────────────────────────────────────────────────────────

app.Map("/health", () => Results.Ok(new
{
    status = "ok",
    figmaConnected,
    hasDesignData = latestSync != null,
    version = "2.0.0-mcp"
}));

app.Run();

// ─── Codegen Patcher (Fast Path) ─────────────────────────────────────────────
// Applies surgical string patches to Program.cs for property changes.
// No LLM needed — just find-and-replace for text, spacing, sizing.

static class CodegenPatcher
{
    public static int ApplyPatches(JsonElement patches, string outputDir)
    {
        var filePath = Path.Combine(outputDir, "Program.cs");
        if (!File.Exists(filePath)) return 0;

        var code = File.ReadAllText(filePath);
        var originalCode = code;
        var patchCount = 0;

        foreach (var patch in patches.EnumerateArray())
        {
            var property = patch.GetProperty("property").GetString() ?? "";
            var newCode = property switch
            {
                "characters" => PatchText(code, patch),
                "fontSize" => PatchFontSize(code, patch),
                "fontWeight" => PatchFontWeight(code, patch),
                "itemSpacing" => PatchSpacing(code, patch),
                "padding" => PatchPadding(code, patch),
                "width" => PatchDimension(code, patch, "Width", "MinWidth"),
                "height" => PatchDimension(code, patch, "Height", "MinHeight"),
                "cornerRadius" => PatchCornerRadius(code, patch),
                _ => code
            };

            if (newCode != code)
            {
                code = newCode;
                patchCount++;
            }
        }

        if (code != originalCode)
        {
            File.WriteAllText(filePath, code);
            Console.WriteLine($"[CodegenPatcher] Wrote {filePath}");
        }

        return patchCount;
    }

    static string PatchText(string code, JsonElement patch)
    {
        // The node name helps us find the right text in case of duplicates
        var newValue = patch.GetProperty("value").GetString() ?? "";

        // Strategy: find any quoted string that's a substring match in the code
        // and replace it. This works because text content is unique enough.
        // We look for TextBlock("old text"), Caption("old text"), SubHeading("old text"), etc.
        // The nodeId-based source map approach would be better but requires Phase 1 to annotate.

        // For now: use a simple approach — look for text patterns near the node name
        var escaped = EscapeForCSharp(newValue);

        // Try to find the previous value by looking at what the Figma plugin sent
        // Actually, we don't have the old value — just the new one.
        // The full-sync message stored in latestSync has the old tree.
        // For a v3 MVP, we'll do a full-file text replacement which works for unique strings.
        return code; // Text patching needs the old value — handled separately below
    }

    static string PatchFontSize(string code, JsonElement patch)
    {
        // Font size changes don't affect many places — typically just the style call
        return code;
    }

    static string PatchFontWeight(string code, JsonElement patch)
    {
        return code;
    }

    static string PatchSpacing(string code, JsonElement patch)
    {
        var newValue = patch.GetProperty("value").GetDouble();
        var rounded = Round4(newValue);
        // Find VStack(N, or HStack(N, patterns and update the gap
        // This is a simplified approach — a source map would be more precise
        return code;
    }

    static string PatchPadding(string code, JsonElement patch)
    {
        return code;
    }

    static string PatchDimension(string code, JsonElement patch, string prop, string minProp)
    {
        return code;
    }

    static string PatchCornerRadius(string code, JsonElement patch)
    {
        return code;
    }

    static int Round4(double value) => Math.Max(0, (int)(Math.Round(value / 4.0) * 4));
    static string EscapeForCSharp(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}

// ─── Tree Diff Patcher (compares old/new tree and patches Program.cs) ────────

static class TreeDiffPatcher
{
    public static int ApplyDiff(FigmaNode? oldTree, FigmaNode? newTree, string outputDir)
    {
        if (oldTree == null || newTree == null) return 0;

        var filePath = Path.Combine(outputDir, "Program.cs");
        if (!File.Exists(filePath)) return 0;

        var code = File.ReadAllText(filePath);
        var originalCode = code;
        var patches = new List<(string desc, string oldStr, string newStr)>();

        CollectChanges(oldTree, newTree, patches);

        foreach (var (desc, oldStr, newStr) in patches)
        {
            // Try to find with context first (avoids replacing wrong occurrence)
            var found = false;
            if (oldStr.StartsWith("\"") && oldStr.EndsWith("\""))
            {
                // Text change — try with surrounding factory call context
                string[] prefixes = ["TextBlock(", "SubHeading(", "Heading(", "Caption(", 
                    "Button(", "HyperlinkButton(", ".ApplyStyle(", "= \"", "NavItem("];
                foreach (var prefix in prefixes)
                {
                    var contextOld = prefix + oldStr;
                    var contextNew = prefix + newStr;
                    var idx = code.IndexOf(contextOld, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        code = code.Remove(idx, contextOld.Length).Insert(idx, contextNew);
                        Console.WriteLine($"[Patcher] {desc} (matched via {prefix})");
                        found = true;
                        break;
                    }
                }
            }
            if (!found)
            {
                var idx = code.IndexOf(oldStr, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    code = code.Remove(idx, oldStr.Length).Insert(idx, newStr);
                    Console.WriteLine($"[Patcher] {desc}");
                }
            }
        }

        if (code != originalCode)
        {
            File.WriteAllText(filePath, code);
            Console.WriteLine($"[Patcher] Wrote {filePath} ({patches.Count} patches)");
            return patches.Count;
        }
        return 0;
    }

    static void CollectChanges(FigmaNode oldNode, FigmaNode newNode,
        List<(string, string, string)> patches)
    {
        // Build flat maps keyed by node ID for reliable matching
        var oldMap = new Dictionary<string, FigmaNode>();
        var newMap = new Dictionary<string, FigmaNode>();
        FlattenTree(oldNode, oldMap);
        FlattenTree(newNode, newMap);

        foreach (var (id, newN) in newMap)
        {
            if (!oldMap.TryGetValue(id, out var oldN)) continue;

            // Text content changes — include likely factory prefix for targeted replacement
            if (oldN.Type == "TEXT" && newN.Type == "TEXT"
                && oldN.Characters != null && newN.Characters != null
                && oldN.Characters != newN.Characters)
            {
                var oldEsc = Esc(oldN.Characters);
                var newEsc = Esc(newN.Characters);
                // Guess the Reactor factory from font size to target the right occurrence
                var size = oldN.FontSize ?? 14;
                var weight = oldN.FontWeight ?? 400;
                string hint;
                if (size <= 12) hint = "Caption(";
                else if (size <= 20 && weight >= 600) hint = "SubHeading(";
                else if (size <= 28) hint = "Heading(";
                else if (size <= 40) hint = "TitleLarge:";
                else hint = "TextBlock(";

                patches.Add(($"text[{hint.TrimEnd('(', ':')}]: \"{Trunc(oldN.Characters)}\" → \"{Trunc(newN.Characters)}\"",
                    $"\"{oldEsc}\"", $"\"{newEsc}\""));
            }

            // Spacing (itemSpacing)
            if (oldN.ItemSpacing != newN.ItemSpacing
                && oldN.ItemSpacing > 0 && newN.ItemSpacing > 0)
            {
                var oldGap = R4(oldN.ItemSpacing ?? 0);
                var newGap = R4(newN.ItemSpacing ?? 0);
                if (oldGap != newGap)
                    patches.Add(($"gap: {oldGap} → {newGap}",
                        $"Stack({oldGap},", $"Stack({newGap},"));
            }

            // Padding changes
            var oldPad = Pad(oldN);
            var newPad = Pad(newN);
            if (oldPad != null && newPad != null && oldPad != newPad)
                patches.Add(($"padding: {oldPad} → {newPad}",
                    $".Padding({oldPad})", $".Padding({newPad})"));

            // Width changes
            if (oldN.Width != newN.Width && oldN.Width > 0 && newN.Width > 0)
            {
                var oldW = R4(oldN.Width); var newW = R4(newN.Width);
                if (oldW != newW && oldW > 0)
                {
                    TryPatchDim(patches, "Width", oldW, newW);
                    TryPatchDim(patches, "MinWidth", oldW, newW);
                }
            }

            // Height changes
            if (oldN.Height != newN.Height && oldN.Height > 0 && newN.Height > 0)
            {
                var oldH = R4(oldN.Height); var newH = R4(newN.Height);
                if (oldH != newH && oldH > 0)
                {
                    TryPatchDim(patches, "Height", oldH, newH);
                    TryPatchDim(patches, "MinHeight", oldH, newH);
                }
            }

            // Corner radius changes
            if (oldN.CornerRadius != newN.CornerRadius
                && oldN.CornerRadius > 0 && newN.CornerRadius > 0)
            {
                var oldR = (int)(oldN.CornerRadius ?? 0);
                var newR = (int)(newN.CornerRadius ?? 0);
                patches.Add(($"radius: {oldR} → {newR}",
                    $".CornerRadius({oldR})", $".CornerRadius({newR})"));
            }
        }
    }

    static void FlattenTree(FigmaNode node, Dictionary<string, FigmaNode> map)
    {
        map[node.Id] = node;
        if (node.Children != null)
            foreach (var c in node.Children) FlattenTree(c, map);
    }

    static string Trunc(string s) => s.Length > 30 ? s[..30] + "..." : s;

    static void TryPatchDim(List<(string, string, string)> patches, string prop, int oldV, int newV)
    {
        patches.Add(($"{prop}: {oldV} → {newV}",
            $".{prop}({oldV})", $".{prop}({newV})"));
    }

    static string? Pad(FigmaNode n)
    {
        var t = R4(n.PaddingTop ?? 0); var r = R4(n.PaddingRight ?? 0);
        var b = R4(n.PaddingBottom ?? 0); var l = R4(n.PaddingLeft ?? 0);
        if (t == 0 && r == 0 && b == 0 && l == 0) return null;
        if (t == b && l == r && t == l) return $"{t}";
        return $"{l}, {t}, {r}, {b}";
    }

    static int R4(double v) => Math.Max(0, (int)(Math.Round(v / 4.0) * 4));
    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

// ─── LLM Code Generator ──────────────────────────────────────────────────────

static class LlmGenerator
{
    /// <summary>
    /// Launches Copilot CLI (agency copilot) in autopilot mode with a prompt
    /// containing the design summary and instructions to generate Reactor code.
    /// Copilot CLI handles LLM auth, model selection, and tool access.
    /// </summary>
    public static async Task<string?> Generate(
        string designSummary, string figmaSkill, string designSkillContent, string mainSkill,
        string endpoint, string apiKey, string model,
        string outputDir)
    {
        // Write the design summary to a temp file
        var summaryFile = Path.Combine(Path.GetTempPath(), "figma-design-summary.md");
        File.WriteAllText(summaryFile, designSummary);

        var targetFile = Path.Combine(outputDir, "Program.cs");
        var csprojFile = Directory.GetFiles(outputDir, "*.csproj").FirstOrDefault() ?? "*.csproj";

        // Write prompt to a file to avoid cmd.exe quoting issues
        var prompt = $@"Read the Figma design summary from {summaryFile} and translate it into a pixel-accurate Reactor WinUI app.

CRITICAL — PIXEL FIDELITY RULES:
- Every element MUST have its exact width and height from the Figma design applied via .Width() and .Height(), or .MinWidth()/.MinHeight() for text containers.
- Every gap value (itemSpacing) MUST be applied exactly as the gap parameter in VStack(gap, ...) or HStack(gap, ...).
- Every padding value MUST be applied exactly using .Padding(left, top, right, bottom) wrapped in a Border.
- Every margin between elements MUST be applied using .Margin().
- Corner radius values from Figma: use ControlCornerRadius for 4px, OverlayCornerRadius for 8px, and exact values for other radii.
- The layout tree in the summary shows precise dimensions like [280×817, gap=4, pad=0,4,0,0] — use these EXACT values.
- Round spacing values to the nearest 4px grid value only if design.md requires it.

CONTROLS:
- Use NavigationView for side nav patterns, TitleBar for title bars.
- Use Button, HyperlinkButton, CheckBox, ToggleSwitch, AutoSuggestBox etc. for interactive controls.
- Use Theme tokens for all colors (CardBackground, Accent, SecondaryText, DividerStroke, etc.).
- Use semantic typography: Caption(), SubHeading(), Heading(), .ApplyStyle(""TitleLargeTextBlockStyle"").

BRUSHES & SURFACES — Map Figma fills/strokes to Reactor Theme tokens:
- ""Surface / App Surface"" App Base (Fill) → .Background(Theme.SolidBackground)
- ""Surface / App Surface"" App Base (Stroke) → .WithBorder(Theme.SurfaceStroke, 1)
- ""Surface / App Surface"" App Base (Shadow) → .Translation(0, 0, 32).Set(b => {{ b.Shadow = new ThemeShadow(); }})
- App Layer (Fill) → .Background(Theme.LayerFill)
- Card backgrounds with opacity → .Background(Theme.CardBackground).WithBorder(Theme.CardStroke, 1)
- Divider lines → Border(VStack()).Height(1).Background(Theme.DividerStroke)
- Control fills → .Background(Theme.ControlFill)
- Subtle/transparent fills → .Background(Theme.SubtleFill)
- Accent/blue fills (e.g. #005AB8) → .Background(Theme.Accent) or accent button Resources
- For any WinUI brush not in Theme.*, use Theme.Ref(""BrushKeyName"")
- NEVER use hardcoded hex colors for themed surfaces

OUTPUT:
- Write to {targetFile} as a complete, runnable Program.cs.
- Include ReactorApp.Run<ComponentName>() with devtools:true in DEBUG.
- After writing Program.cs, launch the app: dotnet watch run --project {csprojFile}";

        var promptFile = Path.Combine(Path.GetTempPath(), "figma-copilot-prompt.txt");
        File.WriteAllText(promptFile, prompt);

        Console.WriteLine($"[LLM] Launching Copilot CLI in autopilot mode...");
        Console.WriteLine($"[LLM] Design summary: {summaryFile} ({designSummary.Length} chars)");
        Console.WriteLine($"[LLM] Prompt file: {promptFile}");
        Console.WriteLine($"[LLM] Target: {targetFile}");

        // Launch in a new terminal via PowerShell (handles quoting better than cmd)
        var scriptContent = $"agency copilot -p (Get-Content '{promptFile}' -Raw) --autopilot --no-remote --allow-all-tools; Write-Host ''; Write-Host 'Generation complete. Press any key to close.'; $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')";
        var scriptFile = Path.Combine(Path.GetTempPath(), "figma-copilot-launch.ps1");
        File.WriteAllText(scriptFile, scriptContent);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoExit -File \"{scriptFile}\"",
            WorkingDirectory = outputDir,
            UseShellExecute = true,
            CreateNoWindow = false,
        };

        try
        {
            var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                Console.WriteLine($"[LLM] ✓ Copilot CLI launched (PID: {process.Id})");
                Console.WriteLine($"[LLM] Watch the terminal window for generation progress");
                return "LAUNCHED"; // Signal that the process was started
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM] Failed to launch Copilot CLI: {ex.Message}");
            Console.WriteLine($"[LLM] Make sure 'agency' is in PATH");
        }

        return null;
    }
}

// ─── Design Intent Summarizer ────────────────────────────────────────────────

static class DesignSummarizer
{
    public static string Summarize(FigmaSyncMessage sync)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Figma Design Summary");
        sb.AppendLine($"**Frame:** {sync.FrameName} (`{sync.FrameId}`)");
        sb.AppendLine($"**Size:** {sync.Tree!.Width:0} × {sync.Tree.Height:0}");
        sb.AppendLine();

        // Collect all elements into categorized lists
        var components = new List<string>();
        var textElements = new List<string>();
        var sections = new List<string>();

        WalkTree(sync.Tree, 0, components, textElements, sections, "");

        // ── Page Sections ──
        sb.AppendLine("## Page Sections");
        if (sections.Count > 0)
        {
            foreach (var s in sections) sb.AppendLine($"- {s}");
        }
        else
        {
            sb.AppendLine("- Single content area");
        }
        sb.AppendLine();

        // ── Component Instances (WinUI Controls) ──
        sb.AppendLine("## Controls Found");
        if (components.Count > 0)
        {
            foreach (var c in components.Distinct()) sb.AppendLine($"- {c}");
        }
        else
        {
            sb.AppendLine("- No recognized WinUI controls");
        }
        sb.AppendLine();

        // ── Text Content ──
        sb.AppendLine("## Text Content");
        foreach (var t in textElements) sb.AppendLine($"- {t}");
        sb.AppendLine();

        // ── Layout Tree (compact) ──
        sb.AppendLine("## Layout Structure");
        sb.AppendLine("```");
        WriteCompactTree(sync.Tree, sb, 0);
        sb.AppendLine("```");

        return sb.ToString();
    }

    static void WalkTree(FigmaNode node, int depth,
        List<string> components, List<string> textElements, List<string> sections,
        string parentPath)
    {
        if (!node.Visible) return;

        var path = string.IsNullOrEmpty(parentPath) ? node.Name : $"{parentPath} > {node.Name}";

        // Identify page sections (top-level named frames)
        if (depth == 1 && node.Type is "FRAME" or "INSTANCE")
        {
            var desc = node.LayoutMode != null ? $" ({node.LayoutMode}, {node.Width:0}×{node.Height:0})" : $" ({node.Width:0}×{node.Height:0})";
            sections.Add($"**{node.Name}**{desc}");
        }

        // Collect component instances → map to WinUI control names
        if (node.Type == "INSTANCE")
        {
            var name = node.Name.ToLowerInvariant();
            var compName = node.ComponentName?.ToLowerInvariant() ?? name;
            var controlName = MapToWinUIControl(name, compName);
            if (controlName != null)
            {
                var text = FindFirstText(node);
                var label = text != null ? $" — \"{text}\"" : "";
                components.Add($"`{controlName}`{label} (Figma: \"{node.Name}\")");
            }
        }

        // Collect text with typography classification
        if (node.Type == "TEXT" && !string.IsNullOrWhiteSpace(node.Characters))
        {
            var family = node.FontFamily?.ToLowerInvariant() ?? "";
            if (family.Contains("fluent") || family.Contains("symbol") || family.Contains("mwf"))
                return; // skip icon glyphs

            var size = node.FontSize ?? 14;
            var weight = node.FontWeight ?? 400;
            var style = ClassifyTypography(size, weight);
            var preview = node.Characters.Length > 60
                ? node.Characters[..60] + "..."
                : node.Characters;

            var fgDesc = "";
            if (node.Fills?.FirstOrDefault(f => f.Visible && f.Type == "SOLID" && f.Color != null) is { } fill)
            {
                var token = GuessTextToken(fill);
                if (token != null) fgDesc = $" [color: {token}]";
            }

            textElements.Add($"**{style}**: \"{preview}\"{fgDesc}");
        }

        // Recurse
        if (node.Children != null)
        {
            foreach (var child in node.Children)
                WalkTree(child, depth + 1, components, textElements, sections, path);
        }
    }

    static void WriteCompactTree(FigmaNode node, StringBuilder sb, int indent)
    {
        if (!node.Visible) return;

        var prefix = new string(' ', indent * 2);
        var name = node.Name;

        // Skip decorative elements
        var nameLower = name.ToLowerInvariant();
        if (nameLower is "base" or "shadow" or "stroke" or "fill" or "selector" or "mask"
            || nameLower.Contains("gradient") || nameLower.Contains(".ruler")
            || nameLower.Contains("backdrop")) return;

        // Compact description with precise dimensions
        var parts = new List<string>();
        if (node.Type == "TEXT" && node.Characters != null)
        {
            var preview = node.Characters.Length > 40 ? node.Characters[..40] + "…" : node.Characters;
            parts.Add($"\"{preview}\"");
            parts.Add(ClassifyTypography(node.FontSize ?? 14, node.FontWeight ?? 400));
        }
        else
        {
            if (node.Type == "INSTANCE") parts.Add("INSTANCE");
            if (node.LayoutMode is "VERTICAL") parts.Add("↓");
            else if (node.LayoutMode is "HORIZONTAL") parts.Add("→");
            // Precise dimensions
            parts.Add($"{node.Width:0}×{node.Height:0}");
            if (node.ItemSpacing > 0) parts.Add($"gap={node.ItemSpacing:0}");
            // Precise padding
            var pt = node.PaddingTop ?? 0; var pr = node.PaddingRight ?? 0;
            var pb = node.PaddingBottom ?? 0; var pl = node.PaddingLeft ?? 0;
            if (pt > 0 || pr > 0 || pb > 0 || pl > 0)
            {
                if (pt == pb && pl == pr && pt == pl) parts.Add($"pad={pt:0}");
                else parts.Add($"pad={pl:0},{pt:0},{pr:0},{pb:0}");
            }
            if (node.CornerRadius > 0) parts.Add($"r={node.CornerRadius:0}");
        }

        var desc = parts.Count > 0 ? $" [{string.Join(", ", parts)}]" : "";
        sb.AppendLine($"{prefix}{name}{desc}");

        // Recurse (max depth 8 for detailed structure)
        if (node.Children != null && indent < 8)
        {
            foreach (var child in node.Children)
                WriteCompactTree(child, sb, indent + 1);
        }
        else if (node.Children is { Count: > 0 })
        {
            sb.AppendLine($"{prefix}  ... ({node.Children.Count} children)");
        }
    }

    static string ClassifyTypography(double size, double weight)
    {
        if (size <= 12) return "Caption (12px)";
        if (size <= 14 && weight >= 600) return "Body Strong (14px SemiBold)";
        if (size <= 14) return "Body (14px)";
        if (size <= 18 && weight >= 600) return "Body Large Strong (18px SemiBold)";
        if (size <= 18) return "Body Large (18px)";
        if (size <= 20 && weight >= 600) return "Subtitle (20px SemiBold)";
        if (size <= 28) return "Title (28px)";
        if (size <= 40) return "Title Large (40px)";
        return "Display (68px)";
    }

    static string? MapToWinUIControl(string name, string comp)
    {
        bool has(string p) => name.Contains(p) || comp.Contains(p);

        if (has("title bar") && !has("caption") && !has("icon")) return "TitleBar";
        if (has("side nav") && !has("list item") && !has("menu") && !has("parts")) return "NavigationView";
        if (has("nav") && has("list item")) return "NavItem";
        if (has("breadcrumb")) return "BreadcrumbBar";
        if (has("tab view") || has("tabview")) return "TabView";
        if (has("button") && has("hyperlink")) return "HyperlinkButton";
        if (has("button") && has("toggle")) return "ToggleButton";
        if (has("button") && has("split")) return "SplitButton";
        if (has("button") && has("dropdown")) return "DropDownButton";
        if (has("button") && !has("caption") && !has("menu")) return "Button";
        if (has("checkbox")) return "CheckBox";
        if (has("toggle") && has("switch")) return "ToggleSwitch";
        if (has("radio")) return "RadioButton";
        if (has("combo") && has("box")) return "ComboBox";
        if (has("slider")) return "Slider";
        if (has("auto suggest") || has("search")) return "AutoSuggestBox";
        if (has("text") && (has("box") || has("field"))) return "TextField";
        if (has("password")) return "PasswordBox";
        if (has("number") && has("box")) return "NumberBox";
        if (has("info") && has("bar")) return "InfoBar";
        if (has("info") && has("badge")) return "InfoBadge";
        if (has("progress") && has("bar")) return "ProgressBar";
        if (has("progress") && has("ring")) return "ProgressRing";
        if (has("teaching") && has("tip")) return "TeachingTip";
        if (has("expander")) return "Expander";
        if (has("person") && has("picture")) return "PersonPicture";
        if (has("calendar") && has("date")) return "CalendarDatePicker";
        if (has("date") && has("picker")) return "DatePicker";
        if (has("time") && has("picker")) return "TimePicker";
        if (has("command") && has("bar")) return "CommandBar";
        if (has("menu") && has("bar")) return "MenuBar";
        if (has("scroll") && has("bar")) return null; // implicit
        if (has("surface")) return null; // decorative
        if (has("gripper")) return null;
        if (has("footer")) return "Footer (custom layout)";
        if (has("heading")) return "Heading (custom layout)";
        return null;
    }

    static string? GuessTextToken(FigmaFillData fill)
    {
        if (fill.Color == null) return null;
        var r = (int)(fill.Color.R * 255);
        var g = (int)(fill.Color.G * 255);
        var b = (int)(fill.Color.B * 255);
        var opacity = fill.Opacity ?? 1.0;

        if (r < 30 && g < 30 && b < 30 && opacity > 0.9) return null; // default PrimaryText
        if (r > 225 && g > 225 && b > 225 && opacity > 0.9) return null; // white primary in dark mode
        if (opacity < 0.7) return "Theme.SecondaryText";
        if (r > 90 && r < 170 && g > 90 && g < 170 && b > 90 && b < 170) return "Theme.SecondaryText";
        if (b > 150 && b > r && b > g) return "Theme.AccentText";
        return null;
    }

    static string? FindFirstText(FigmaNode node)
    {
        if (node.Type == "TEXT" && !string.IsNullOrWhiteSpace(node.Characters))
        {
            var family = node.FontFamily?.ToLowerInvariant() ?? "";
            if (!family.Contains("fluent") && !family.Contains("symbol") && !family.Contains("mwf"))
                return node.Characters.Length > 30 ? node.Characters[..30] + "..." : node.Characters;
        }
        if (node.Children != null)
        {
            foreach (var c in node.Children)
            {
                var t = FindFirstText(c);
                if (t != null) return t;
            }
        }
        return null;
    }
}

// ─── Models ──────────────────────────────────────────────────────────────────

record JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; init; } = "2.0";
    [JsonPropertyName("method")] public string Method { get; init; } = "";
    [JsonPropertyName("params")] public JsonElement? Params { get; init; }
    [JsonPropertyName("id")] public JsonElement? Id { get; init; }
}

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

record FillColor
{
    [JsonPropertyName("r")] public double R { get; init; }
    [JsonPropertyName("g")] public double G { get; init; }
    [JsonPropertyName("b")] public double B { get; init; }
    [JsonPropertyName("a")] public double A { get; init; }
}

