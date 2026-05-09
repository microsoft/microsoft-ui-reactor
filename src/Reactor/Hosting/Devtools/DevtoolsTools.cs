using System.Diagnostics;
using System.Text.Json;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Registers the Phase 2 MCP tools on a <see cref="DevtoolsMcpServer"/>.
/// Each tool's handler is written to be side-effect-free on the HTTP thread
/// and marshal UI work through <see cref="DevtoolsMcpServer.OnDispatcher{T}"/>
/// where required. The <c>reactor.*</c> names in spec prose are unprefixed on
/// the wire — agents see the bare names.
/// </summary>
/// <summary>
/// Enriched component descriptor returned by the <c>components</c> tool when the
/// host provides <see cref="DevtoolsTools.ToolHostContext.GetComponentsDetailed"/>.
/// Agents use <see cref="IsNested"/> to filter out inner helper components (e.g.
/// <c>ContextDemo+AccentBadge</c>) when picking the "main" demo to mount.
/// </summary>
internal sealed record ComponentInfo(
    string Name,
    string FullName,
    bool IsNested,
    bool IsPublic,
    string? Namespace);

internal static class DevtoolsTools
{
    /// <summary>Supplies data the tools need from the host: components, switch callback, reload request.</summary>
    internal sealed class ToolHostContext
    {
        public required Func<IReadOnlyList<string>> GetComponents { get; init; }
        public required Func<string?> GetCurrentComponent { get; init; }
        public required Func<string, bool> SwitchComponent { get; init; }
        public required Action RequestReload { get; init; }
        public required Action RequestShutdown { get; init; }
        public required WindowRegistry Windows { get; init; }

        /// <summary>
        /// Optional callback for the <c>windows.open</c> MCP tool (spec 036 §10).
        /// The component name has already been validated against
        /// <see cref="GetComponents"/> by the tool layer before this fires —
        /// the implementation only needs to resolve the type and open the
        /// window. Returns the opened <see cref="ReactorWindow"/>'s id, or
        /// <c>null</c> only on internal failures (e.g. type lookup miss
        /// despite the name appearing in the allowlist).
        /// </summary>
        /// <remarks>
        /// Allowlist enforcement lives in <see cref="DevtoolsTools.RegisterCore"/>
        /// (next to the rest of the windows.open argument validation) so that a
        /// host implementation cannot accidentally bypass the gate by forgetting
        /// the lookup. (W-3 hardening; threat model 2026-05-08.)
        /// </remarks>
        public Func<WindowSpec, string, string?>? OpenWindowByAllowlistedComponent { get; init; }

        /// <summary>
        /// Optional enriched component descriptor lookup. When present, the
        /// <c>components</c> tool returns structured entries ({ name, fullName,
        /// isNested, isPublic, namespace }); otherwise it falls back to the
        /// flat string list produced by <see cref="GetComponents"/>.
        /// </summary>
        public Func<IReadOnlyList<ComponentInfo>>? GetComponentsDetailed { get; init; }

        /// <summary>
        /// Optional node registry — set by the host bring-up so
        /// <c>switchComponent</c> can invalidate stale ids for the active window
        /// after a successful swap. Absent in minimal selftest harnesses that
        /// don't rely on post-switch tree walks.
        /// </summary>
        public NodeRegistry? Nodes { get; init; }
    }

    public static void RegisterCore(DevtoolsMcpServer server, ToolHostContext ctx)
    {
        Register_Version(server);
        Register_Components(server, ctx);
        Register_SwitchComponent(server, ctx);
        Register_Reload(server, ctx);
        Register_Shutdown(server, ctx);
        Register_Windows(server, ctx);
        Register_WindowsList(server, ctx);
        Register_WindowsActivate(server, ctx);
        Register_WindowsClose(server, ctx);
        Register_WindowsOpen(server, ctx);
    }

    // -- version -----------------------------------------------------------------

    private static void Register_Version(DevtoolsMcpServer server)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "version",
                Description: "Returns the running app's build tag, pid, and MCP port. Zero side effects.",
                InputSchema: new { type = "object", properties = new { }, additionalProperties = false }),
            _ => new
            {
                build = server.BuildTag,
                pid = Process.GetCurrentProcess().Id,
                mcpPort = server.Port,
            });
    }

    // -- components --------------------------------------------------------------

    private static void Register_Components(DevtoolsMcpServer server, ToolHostContext ctx)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "components",
                Description:
                    "Lists the Component class names in the loaded assembly, top-level first. Each entry carries " +
                    "`isNested` (true for inner helper components like `ContextDemo+AccentBadge`) so agents can pick " +
                    "the user-facing demo without guessing. Use `current` to verify what's mounted now.",
                InputSchema: new { type = "object", properties = new { }, additionalProperties = false }),
            _ =>
            {
                if (ctx.GetComponentsDetailed is { } detailed)
                {
                    var infos = detailed()
                        .OrderBy(c => c.IsNested ? 1 : 0)
                        .ThenBy(c => c.Name, StringComparer.Ordinal)
                        .ToArray();
                    return new
                    {
                        components = infos,
                        current = ctx.GetCurrentComponent(),
                    };
                }
                return new
                {
                    components = ctx.GetComponents().ToArray(),
                    current = ctx.GetCurrentComponent(),
                };
            });
    }

    // -- switchComponent ---------------------------------------------------------

    private static void Register_SwitchComponent(DevtoolsMcpServer server, ToolHostContext ctx)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "switchComponent",
                Description: "Switches the hosted root component. Invalidates every tree id in the target window.",
                InputSchema: new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Component class name" },
                    },
                    required = new[] { "name" },
                    additionalProperties = false,
                }),
            @params =>
            {
                var name = ReadString(@params, "name")
                    ?? throw new McpToolException("switchComponent requires a 'name' argument.",
                        JsonRpcErrorCodes.InvalidParams);

                // The switch itself (calling back into the host's `Mount(...)`) and
                // the follow-up `WindowRegistry.Snapshot()` both touch WinUI state
                // and must run on the UI dispatcher. Without this hop the handler
                // would land on the HTTP worker thread, where `host.Mount(new T())`
                // can hit a WinUI COM thread-apartment error that surfaces as an
                // empty-message exception — the selftest fixture saw this as a
                // -32603 InternalError and crashed on the missing `ok`.
                return server.OnDispatcher<object>(() =>
                {
                    var ok = ctx.SwitchComponent(name);
                    if (!ok)
                        throw new McpToolException($"Component '{name}' not found.",
                            JsonRpcErrorCodes.ToolExecution,
                            new { code = "unknown-component", available = ctx.GetComponents().ToArray() });

                    // The old tree is gone; invalidate its ids so a subsequent
                    // selector resolution against an old id returns `"gone"`
                    // rather than silently reaching a stale element. Scoped to
                    // every known active window — the swap replaces all roots.
                    if (ctx.Nodes is { } nodes)
                    {
                        foreach (var snap in ctx.Windows.Snapshot())
                            nodes.InvalidateWindow(snap.Id);
                    }

                    return new { ok = true, current = name };
                });
            });
    }

    // -- reload ------------------------------------------------------------------

    private static void Register_Reload(DevtoolsMcpServer server, ToolHostContext ctx)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "reload",
                Description:
                    "Flushes the response, closes listeners, and exits with sentinel code 42 so the " +
                    "`mur devtools` supervisor rebuilds and relaunches. Old node ids do not carry over.",
                InputSchema: new
                {
                    type = "object",
                    properties = new
                    {
                        component = new { type = "string", description = "Optional component to focus after restart" },
                    },
                    additionalProperties = false,
                }),
            @params =>
            {
                // Return the response immediately; the reload fires after the HTTP write flushes.
                var exitingBuild = server.BuildTag;
                ctx.RequestReload();
                return new { ok = true, exitingBuild };
            });
    }

    // -- shutdown ----------------------------------------------------------------

    private static void Register_Shutdown(DevtoolsMcpServer server, ToolHostContext ctx)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "shutdown",
                Description:
                    "Closes the app cleanly. Flushes the HTTP response, disposes the MCP listener, " +
                    "closes the window, and exits with code 0 so the `mur devtools` supervisor " +
                    "returns without rebuilding. Use to release file locks on the build output.",
                InputSchema: new { type = "object", properties = new { }, additionalProperties = false }),
            @params =>
            {
                var exitingBuild = server.BuildTag;
                ctx.RequestShutdown();
                return new { ok = true, exitingBuild };
            });
    }

    // -- windows -----------------------------------------------------------------

    private static void Register_Windows(DevtoolsMcpServer server, ToolHostContext ctx)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "windows",
                Description:
                    "Lists active windows with their ids, titles, bounds, build tag, and the currently " +
                    "mounted Reactor component (per-window). The id is a stable handle — it does NOT " +
                    "reflect the window title, which changes on `switchComponent`. Scope selectors with " +
                    "`window` when more than one is active. " +
                    "Pass `includeHwnd: true` to include raw native window handles; omitted by " +
                    "default to keep HWNDs out of agent transcripts unless explicitly requested.",
                InputSchema: new
                {
                    type = "object",
                    properties = new
                    {
                        includeHwnd = new
                        {
                            type = "boolean",
                            description = "Include raw HWND values in the response. Defaults to false (W-7).",
                        },
                    },
                    additionalProperties = false,
                }),
            @params =>
            {
                // W-7: HWND opt-in. The token-holding caller is already
                // authorised, but raw HWNDs in agent transcripts are easy to
                // leak into logs. Default response shape excludes them.
                var includeHwnd = ReadBool(@params, "includeHwnd") ?? false;
                return server.OnDispatcher(() =>
                {
                    var current = ctx.GetCurrentComponent();
                    return new
                    {
                        windows = ctx.Windows.Snapshot().Select(w =>
                        {
                            // Anonymous types can't be conditionally shaped, so
                            // emit two structurally similar projections gated
                            // on the opt-in flag. The non-hwnd shape is the
                            // pre-W-7 shape minus that one field.
                            return includeHwnd
                                ? (object)new
                                {
                                    id = w.Id,
                                    title = w.Title,
                                    hwnd = w.Hwnd,
                                    bounds = new
                                    {
                                        x = w.Bounds.X,
                                        y = w.Bounds.Y,
                                        width = w.Bounds.Width,
                                        height = w.Bounds.Height,
                                    },
                                    isMain = w.IsMain,
                                    buildTag = w.BuildTag,
                                    // Only the main window reflects the component switch today;
                                    // secondary windows report null. Agents can cross-check
                                    // components.current against this value.
                                    currentComponent = w.IsMain ? current : null,
                                }
                                : new
                                {
                                    id = w.Id,
                                    title = w.Title,
                                    bounds = new
                                    {
                                        x = w.Bounds.X,
                                        y = w.Bounds.Y,
                                        width = w.Bounds.Width,
                                        height = w.Bounds.Height,
                                    },
                                    isMain = w.IsMain,
                                    buildTag = w.BuildTag,
                                    currentComponent = w.IsMain ? current : null,
                                };
                        }).ToArray(),
                    };
                });
            });
    }

    // -- windows.list / activate / close / open (spec 036 §10) ------------------

    private static void Register_WindowsList(DevtoolsMcpServer server, ToolHostContext ctx)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "windows.list",
                Description:
                    "Lists active Reactor windows with id, key, title, DIP size, DPI, " +
                    "state, and isMain. Use this to discover ids for windows.activate / " +
                    "windows.close. Spec 036 §10.",
                InputSchema: new { type = "object", properties = new { }, additionalProperties = false }),
            _ => server.OnDispatcher(() => new
            {
                windows = ctx.Windows.Snapshot().Select(w => new
                {
                    id = w.Id,
                    key = w.Key,
                    title = w.Title,
                    width = w.WidthDip,
                    height = w.HeightDip,
                    dpi = w.Dpi,
                    state = w.State,
                    isMain = w.IsMain,
                }).ToArray(),
            }));
    }

    private static void Register_WindowsActivate(DevtoolsMcpServer server, ToolHostContext ctx)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "windows.activate",
                Description: "Activates (focuses) the window with the given id. Spec 036 §10.",
                InputSchema: new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Window id from windows.list." },
                    },
                    required = new[] { "id" },
                    additionalProperties = false,
                }),
            @params =>
            {
                var id = ReadString(@params, "id")
                    ?? throw new McpToolException("windows.activate requires an 'id' argument.",
                        JsonRpcErrorCodes.InvalidParams);
                return server.OnDispatcher<object>(() =>
                {
                    var rw = ctx.Windows.ResolveReactorWindow(id);
                    if (rw is null)
                        throw new McpToolException(
                            $"Window '{id}' not found.",
                            JsonRpcErrorCodes.ToolExecution,
                            new { code = "unknown-window" });
                    rw.Activate();
                    return new { ok = true, id };
                });
            });
    }

    private static void Register_WindowsClose(DevtoolsMcpServer server, ToolHostContext ctx)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "windows.close",
                Description:
                    "Closes the window with the given id. Honors UseClosingGuard / Closing " +
                    "subscribers — returns { ok: false, cancelled: true } when the close was " +
                    "vetoed. Spec 036 §10.",
                InputSchema: new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Window id from windows.list." },
                    },
                    required = new[] { "id" },
                    additionalProperties = false,
                }),
            @params =>
            {
                var id = ReadString(@params, "id")
                    ?? throw new McpToolException("windows.close requires an 'id' argument.",
                        JsonRpcErrorCodes.InvalidParams);

                // Window.Close() is async: AppWindow.Closing / Window.Closed
                // pump after WM_CLOSE drains. Subscribe before invoking Close
                // and wait briefly outside the dispatcher so the message pump
                // can run; then resolve cancellation by what we observed plus
                // a final registry check (a UseClosingGuard veto short-circuits
                // before our Closing handler fires, so it falls through to the
                // post-timeout still-open branch).
                var doneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var observedCancel = false;
                EventHandler? onClosed = null;
                EventHandler<WindowClosingEventArgs>? onClosing = null;

                server.OnDispatcher(() =>
                {
                    var rw = ctx.Windows.ResolveReactorWindow(id);
                    if (rw is null)
                        throw new McpToolException(
                            $"Window '{id}' not found.",
                            JsonRpcErrorCodes.ToolExecution,
                            new { code = "unknown-window" });

                    onClosed = (_, _) => doneTcs.TrySetResult(true);
                    onClosing = (_, e) =>
                    {
                        // Reactor sets cea.Cancel synchronously inside
                        // OnAppWindowClosing — by the time our (last-added)
                        // handler runs it reflects the final decision.
                        if (e.Cancel) { observedCancel = true; doneTcs.TrySetResult(true); }
                    };
                    rw.Closed += onClosed;
                    rw.Closing += onClosing;
                    rw.Close();
                });

                doneTcs.Task.Wait(2000);

                return server.OnDispatcher<object>(() =>
                {
                    var rw = ctx.Windows.ResolveReactorWindow(id);
                    if (rw is not null)
                    {
                        if (onClosed is not null) rw.Closed -= onClosed;
                        if (onClosing is not null) rw.Closing -= onClosing;
                    }
                    var stillOpen = rw is not null;
                    var cancelled = observedCancel || stillOpen;
                    return new { ok = !cancelled, cancelled, id };
                });
            });
    }

    private static void Register_WindowsOpen(DevtoolsMcpServer server, ToolHostContext ctx)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "windows.open",
                Description:
                    "Opens a new top-level window mounting the named Component. The " +
                    "component name must be in the existing devtools allowlist (same gate " +
                    "as switchComponent) — loopback callers cannot spawn arbitrary types. " +
                    "Spec 036 §10. Returns { ok, id } on success.",
                InputSchema: new
                {
                    type = "object",
                    properties = new
                    {
                        component = new { type = "string", description = "Component class name (allowlisted)." },
                        title = new { type = "string" },
                        width = new { type = "number", description = "Initial DIP width (default 1024)." },
                        height = new { type = "number", description = "Initial DIP height (default 768)." },
                        key = new { type = "string", description = "Optional WindowKey for FindWindow lookup." },
                    },
                    required = new[] { "component" },
                    additionalProperties = false,
                }),
            @params =>
            {
                var component = ReadString(@params, "component")
                    ?? throw new McpToolException("windows.open requires a 'component' argument.",
                        JsonRpcErrorCodes.InvalidParams);
                if (ctx.OpenWindowByAllowlistedComponent is null)
                    throw new McpToolException(
                        "windows.open is not wired in this host.",
                        JsonRpcErrorCodes.ToolExecution,
                        new { code = "not-wired" });

                // Allowlist gate (W-3): enforced framework-side, alongside the
                // rest of the windows.open argument validation, so a host can't
                // accidentally bypass it by misimplementing the callback. Same
                // source-of-truth as the `components` and `switchComponent` tools.
                EnsureComponentAllowlisted(ctx.GetComponents(), component);

                var titleArg = ReadString(@params, "title");
                var widthArg = ReadDouble(@params, "width");
                var heightArg = ReadDouble(@params, "height");
                var keyArg = ReadString(@params, "key");

                var spec = new WindowSpec
                {
                    Title = titleArg ?? component,
                    Width = widthArg ?? 1024,
                    Height = heightArg ?? 768,
                    Key = string.IsNullOrEmpty(keyArg) ? (WindowKey?)null : WindowKey.Of(keyArg!),
                };
                try { spec.Validate(); }
                catch (ArgumentException ex)
                {
                    throw new McpToolException(
                        $"Invalid windows.open spec: {ex.Message}",
                        JsonRpcErrorCodes.InvalidParams,
                        new { code = "invalid-spec" });
                }

                return server.OnDispatcher<object>(() =>
                {
                    var id = ctx.OpenWindowByAllowlistedComponent(spec, component);
                    if (id is null)
                        throw new McpToolException(
                            $"Component '{component}' could not be resolved.",
                            JsonRpcErrorCodes.ToolExecution,
                            new { code = "open-failed" });
                    return new { ok = true, id };
                });
            });
    }

    // -- helpers -----------------------------------------------------------------

    /// <summary>
    /// W-3 hardening: enforce the windows.open allowlist using the same
    /// source-of-truth (<c>GetComponents</c>) that the <c>components</c> and
    /// <c>switchComponent</c> tools expose. Throws an
    /// <see cref="McpToolException"/> shaped like the rest of the
    /// invalid-input errors when <paramref name="component"/> is not in
    /// <paramref name="allowed"/>.
    /// </summary>
    /// <remarks>
    /// Lives at the framework layer rather than each host's
    /// <c>OpenWindowByAllowlistedComponent</c> callback so a host cannot
    /// silently weaken the gate by forgetting the lookup. Comparison is
    /// ordinal-ignore-case to match the existing <c>switchComponent</c>
    /// behaviour.
    /// </remarks>
    internal static void EnsureComponentAllowlisted(IReadOnlyList<string> allowed, string component)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(component);
        for (int i = 0; i < allowed.Count; i++)
        {
            if (string.Equals(allowed[i], component, StringComparison.OrdinalIgnoreCase))
                return;
        }
        throw new McpToolException(
            $"Component '{component}' is not in the devtools allowlist.",
            JsonRpcErrorCodes.ToolExecution,
            new
            {
                code = "unknown-component",
                available = allowed.ToArray(),
            });
    }

    internal static string? ReadString(JsonElement? args, string name)
    {
        if (args is not { } a || a.ValueKind != JsonValueKind.Object) return null;
        if (!a.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    internal static int? ReadInt(JsonElement? args, string name)
    {
        if (args is not { } a || a.ValueKind != JsonValueKind.Object) return null;
        if (!a.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) ? v : null;
    }

    internal static long? ReadLong(JsonElement? args, string name)
    {
        if (args is not { } a || a.ValueKind != JsonValueKind.Object) return null;
        if (!a.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var v) ? v : null;
    }

    internal static double? ReadDouble(JsonElement? args, string name)
    {
        if (args is not { } a || a.ValueKind != JsonValueKind.Object) return null;
        if (!a.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var v) ? v : null;
    }

    internal static bool? ReadBool(JsonElement? args, string name)
    {
        if (args is not { } a || a.ValueKind != JsonValueKind.Object) return null;
        if (!a.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}
