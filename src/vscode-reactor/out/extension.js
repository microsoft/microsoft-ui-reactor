"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.activate = activate;
exports.deactivate = deactivate;
exports.resolveDotnet = resolveDotnet;
const vscode = __importStar(require("vscode"));
const cp = __importStar(require("child_process"));
const path = __importStar(require("path"));
const fs = __importStar(require("fs"));
const http = __importStar(require("http"));
const crypto = __importStar(require("crypto"));
let previewProcess;
let capturePort;
let captureToken;
let panel;
let statusBarItem;
let outputChannel;
let editorChangeDisposable;
let currentCsprojPath;
let currentComponents = [];
let currentComponentName;
let currentFilePath;
let isLaunching = false;
let legacyPreviewArgs = false;
let extensionContext;
function activate(context) {
    extensionContext = context;
    outputChannel = vscode.window.createOutputChannel("Reactor Preview");
    statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    statusBarItem.command = "reactor.previewFocus";
    context.subscriptions.push(statusBarItem);
    context.subscriptions.push(vscode.commands.registerCommand("reactor.preview", () => startAutoPreview(context)), vscode.commands.registerCommand("reactor.previewConnect", () => connectToPreview(context)), vscode.commands.registerCommand("reactor.previewStop", stopPreview), vscode.commands.registerCommand("reactor.previewFocus", focusPreviewWindow));
}
function deactivate() {
    stopPreview();
    editorChangeDisposable?.dispose();
}
// -- Auto Preview ------------------------------------------------------------
async function startAutoPreview(context) {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== "csharp") {
        vscode.window.showWarningMessage("Open a C# file containing a Reactor Component, then run this command.");
        return;
    }
    const csprojPath = await findCsprojFor(editor.document.uri.fsPath);
    if (!csprojPath) {
        vscode.window.showWarningMessage("Could not find a .csproj file for this file.");
        return;
    }
    currentCsprojPath = csprojPath;
    currentFilePath = editor.document.uri.fsPath;
    // If we already have a running process for this project, just switch via HTTP
    if (previewProcess && capturePort) {
        const fileComponents = findAllComponentClasses(editor.document.getText());
        if (fileComponents.length > 0) {
            await switchComponentViaHttp(fileComponents[0]);
        }
        return;
    }
    // Launch the preview process — no component name needed, it defaults to the first one
    await launchPreviewProcess(context, csprojPath);
    // Watch for editor changes to switch components via HTTP
    if (!editorChangeDisposable) {
        editorChangeDisposable = vscode.window.onDidChangeActiveTextEditor(async (newEditor) => {
            if (!newEditor || newEditor.document.languageId !== "csharp")
                return;
            if (!panel || !capturePort)
                return;
            const newFilePath = newEditor.document.uri.fsPath;
            if (newFilePath === currentFilePath)
                return;
            const fileComponents = findAllComponentClasses(newEditor.document.getText());
            if (fileComponents.length === 0)
                return;
            // Check if we need a new process (different csproj)
            const newCsproj = await findCsprojFor(newFilePath);
            if (!newCsproj)
                return;
            currentFilePath = newFilePath;
            if (newCsproj !== currentCsprojPath) {
                // Different project — need to relaunch
                currentCsprojPath = newCsproj;
                outputChannel.appendLine(`[reactor] Project changed to ${path.basename(newCsproj)}, relaunching...`);
                await launchPreviewProcess(extensionContext, newCsproj);
                return;
            }
            // Same project — switch component via HTTP (instant)
            outputChannel.appendLine(`[reactor] Editor switched to ${path.basename(newFilePath)}, switching to ${fileComponents[0]}...`);
            await switchComponentViaHttp(fileComponents[0]);
        });
    }
}
// -- Component Detection (regex fallback for file-level filtering) -----------
function findAllComponentClasses(text) {
    const pattern = /class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*Component(?:<[^>]*>)?\b/g;
    const results = [];
    let match;
    while ((match = pattern.exec(text)) !== null) {
        const name = match[1];
        if (name === "Component")
            continue;
        results.push(name);
    }
    return results;
}
async function findCsprojFor(filePath) {
    let dir = path.dirname(filePath);
    const root = path.parse(dir).root;
    while (dir !== root) {
        const entries = await fs.promises.readdir(dir);
        const csproj = entries.find((e) => e.endsWith(".csproj"));
        if (csproj) {
            return path.join(dir, csproj);
        }
        dir = path.dirname(dir);
    }
    return null;
}
// -- Launch ------------------------------------------------------------------
async function launchPreviewProcess(context, csprojPath) {
    if (isLaunching) {
        outputChannel.appendLine(`[reactor] Already launching, ignoring request`);
        return;
    }
    isLaunching = true;
    await killPreviewProcess();
    capturePort = undefined;
    captureToken = undefined;
    const workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ??
        path.dirname(csprojPath);
    // Launch with --devtools run (no component name = default to first) and --vscode.
    // Reactor packages older than the devtools rename only expose --preview. We probe
    // stdout: a `[preview]` prefix means the target Reactor is pre-rename, so we kill
    // and relaunch with the legacy args. telemetry_event_name is kept for one release.
    const args = buildDevtoolsArgs(csprojPath, legacyPreviewArgs);
    // SECURITY (TASK-027): resolve `dotnet` to an absolute path **outside** the
    // workspace before spawning. CreateProcessW on Windows searches the CWD before
    // %PATH%, so a hostile repo dropping `dotnet.exe`/`dotnet.bat` at its root would
    // get arbitrary code execution. We refuse such an exe and disable the legacy
    // CWD-first lookup via NoDefaultCurrentDirectoryInExePath as belt-and-braces.
    const dotnetExe = resolveDotnet(workspaceRoot);
    if (!dotnetExe) {
        vscode.window.showErrorMessage("Could not locate `dotnet` on PATH outside the workspace. Install the .NET SDK or remove any dotnet executable from the workspace root.");
        isLaunching = false;
        return;
    }
    outputChannel.appendLine(`[reactor] Launching: ${dotnetExe} ${args.join(" ")}`);
    logTelemetry(legacyPreviewArgs ? "reactor_preview_launch" : "reactor_devtools_launch");
    previewProcess = cp.spawn(dotnetExe, args, {
        cwd: workspaceRoot,
        stdio: ["ignore", "pipe", "pipe"],
        env: {
            ...process.env,
            // Belt-and-braces: even if a child re-spawns with a relative name,
            // CreateProcess will skip the CWD when this env var is set.
            NoDefaultCurrentDirectoryInExePath: "1",
        },
    });
    statusBarItem.text = `$(loading~spin) Reactor: Starting...`;
    statusBarItem.show();
    let sniffedPrefix = false;
    previewProcess.stdout?.on("data", (data) => {
        const text = data.toString();
        outputChannel.append(text);
        if (!sniffedPrefix && !legacyPreviewArgs && !capturePort) {
            if (/^\s*\[preview\]/m.test(text) && !/\[devtools\]/.test(text)) {
                sniffedPrefix = true;
                outputChannel.appendLine(`[reactor] Target Reactor is pre-devtools — falling back to --preview --vscode`);
                legacyPreviewArgs = true;
                killPreviewProcess().then(() => {
                    isLaunching = false;
                    launchPreviewProcess(context, csprojPath);
                });
                return;
            }
            if (/\[devtools\]/.test(text))
                sniffedPrefix = true;
        }
        const match = text.match(/CAPTURE_PORT=(\d+)/);
        if (match) {
            capturePort = parseInt(match[1], 10);
            isLaunching = false;
            outputChannel.appendLine(`[reactor] Capture server on port ${capturePort}`);
        }
        // SECURITY (TASK-018): the per-launch bearer token is announced on the
        // next stdout line. Capture it before issuing any request.
        const tokenMatch = text.match(/CAPTURE_TOKEN=([A-Za-z0-9_\-]+)/);
        if (tokenMatch) {
            captureToken = tokenMatch[1];
        }
        // Fire the initial fetch once both port and token are known.
        if (capturePort && captureToken && !panel) {
            fetchComponentsAndShow(context);
        }
    });
    previewProcess.stderr?.on("data", (data) => {
        outputChannel.append(data.toString());
    });
    previewProcess.on("exit", (code) => {
        isLaunching = false;
        outputChannel.appendLine(`[reactor] Preview process exited with code ${code}`);
        statusBarItem.text = "$(circle-slash) Reactor Preview: Stopped";
        setTimeout(() => {
            if (!previewProcess)
                statusBarItem.hide();
        }, 5000);
        previewProcess = undefined;
        capturePort = undefined;
        captureToken = undefined;
    });
}
/**
 * Walk %PATH% looking for a real `dotnet` executable that is NOT under the
 * workspace root. Returns null if none is found.
 *
 * SECURITY (TASK-027): a hostile repo can plant `dotnet.exe`/`dotnet.bat` at
 * its root; spawning unqualified `dotnet` with cwd=workspace would execute it.
 */
function resolveDotnet(workspaceRoot) {
    const pathEnv = process.env.PATH ?? process.env.Path ?? process.env.path ?? "";
    if (!pathEnv)
        return null;
    const sep = process.platform === "win32" ? ";" : ":";
    const isWin = process.platform === "win32";
    // PATHEXT semantics on Windows: dotnet.exe takes precedence, but .cmd/.bat
    // are also resolved by CreateProcess. We check the well-known list.
    const exts = isWin ? [".exe", ".cmd", ".bat", ".com"] : [""];
    const baseName = isWin ? "dotnet" : "dotnet";
    const wsNormalized = path.resolve(workspaceRoot).toLowerCase();
    for (const dir of pathEnv.split(sep)) {
        if (!dir)
            continue;
        const dirAbs = path.resolve(dir);
        // Refuse anything resolved from inside the workspace.
        const dirLower = dirAbs.toLowerCase();
        if (dirLower === wsNormalized || dirLower.startsWith(wsNormalized + path.sep.toLowerCase())) {
            continue;
        }
        for (const ext of exts) {
            const candidate = path.join(dirAbs, baseName + ext);
            try {
                const stat = fs.statSync(candidate);
                if (stat.isFile()) {
                    // Final guard: realpath in case PATH entry is a symlink that escapes
                    // back into the workspace.
                    let real;
                    try {
                        real = fs.realpathSync(candidate);
                    }
                    catch {
                        real = candidate;
                    }
                    const realLower = real.toLowerCase();
                    if (realLower === wsNormalized || realLower.startsWith(wsNormalized + path.sep.toLowerCase())) {
                        continue;
                    }
                    return candidate;
                }
            }
            catch {
                // Not present in this dir; keep searching.
            }
        }
    }
    return null;
}
function buildDevtoolsArgs(csprojPath, legacy) {
    const tail = legacy ? ["--preview", "--vscode"] : ["--devtools", "run", "--vscode"];
    return ["watch", "run", "--project", csprojPath, "--", ...tail];
}
function logTelemetry(eventName) {
    // Telemetry transport not wired here; the extension's upstream harness reads the
    // output channel in dev. The duplicated event name keeps legacy aggregators working
    // for one release while the devtools name becomes primary.
    outputChannel.appendLine(`[reactor] telemetry: ${eventName}`);
}
/**
 * After the capture server is up, GET /components to populate the dropdown,
 * then show the preview panel.
 */
async function fetchComponentsAndShow(context) {
    if (!capturePort)
        return;
    try {
        const data = await httpGetJson(`http://localhost:${capturePort}/components`);
        currentComponents = data.components;
        currentComponentName = data.current ?? data.components[0];
        statusBarItem.text = `$(eye) Reactor: ${currentComponentName}`;
        statusBarItem.tooltip = `Previewing ${currentComponentName} — port ${capturePort}\nClick to focus window`;
        showPreviewPanel(context, currentComponentName);
    }
    catch (err) {
        outputChannel.appendLine(`[reactor] Failed to fetch components: ${err}`);
        // Show panel anyway with whatever we have
        showPreviewPanel(context, "Preview");
    }
}
// -- Component Switching (HTTP, no restart) ----------------------------------
async function switchComponentViaHttp(componentName) {
    if (!capturePort)
        return;
    if (componentName === currentComponentName)
        return;
    try {
        const result = await httpPostJson(`http://localhost:${capturePort}/preview`, { component: componentName });
        if (result.ok) {
            currentComponentName = componentName;
            statusBarItem.text = `$(eye) Reactor: ${componentName}`;
            statusBarItem.tooltip = `Previewing ${componentName} — port ${capturePort}\nClick to focus window`;
            if (panel) {
                panel.title = `Reactor: ${componentName}`;
                // Notify the webview to update the dropdown selection
                panel.webview.postMessage({
                    type: "updateSelection",
                    selected: componentName,
                });
            }
            outputChannel.appendLine(`[reactor] Switched to ${componentName} via HTTP`);
        }
        else {
            outputChannel.appendLine(`[reactor] Switch failed: ${result.error}`);
        }
    }
    catch (err) {
        outputChannel.appendLine(`[reactor] Failed to switch component: ${err}`);
    }
}
async function killPreviewProcess() {
    if (previewProcess) {
        const proc = previewProcess;
        const pid = proc.pid;
        previewProcess = undefined;
        capturePort = undefined;
        captureToken = undefined;
        if (pid) {
            try {
                cp.execFileSync("taskkill", ["/T", "/F", "/PID", pid.toString()], { stdio: "ignore" });
            }
            catch {
                proc.kill();
            }
            await new Promise((resolve) => {
                if (proc.exitCode !== null) {
                    resolve();
                    return;
                }
                const timeout = setTimeout(() => resolve(), 3000);
                proc.on("exit", () => {
                    clearTimeout(timeout);
                    resolve();
                });
            });
        }
    }
    else {
        capturePort = undefined;
        captureToken = undefined;
    }
}
// -- Connect to existing Preview ---------------------------------------------
async function connectToPreview(context) {
    const portStr = await vscode.window.showInputBox({
        prompt: "Enter the capture server port (shown in the preview window title bar)",
        placeHolder: "e.g. 52431",
    });
    if (!portStr)
        return;
    const port = parseInt(portStr, 10);
    if (isNaN(port) || port < 1 || port > 65535) {
        vscode.window.showErrorMessage("Invalid port number.");
        return;
    }
    try {
        await httpGetJson(`http://localhost:${port}/status`);
    }
    catch {
        vscode.window.showErrorMessage(`Could not connect to capture server on port ${port}.`);
        return;
    }
    capturePort = port;
    statusBarItem.text = "$(eye) Reactor Preview";
    statusBarItem.tooltip = `Preview connected — port ${capturePort}. Click to focus window.`;
    statusBarItem.show();
    // Fetch components from the running server
    await fetchComponentsAndShow(context);
}
// -- WebView Panel -----------------------------------------------------------
function showPreviewPanel(context, componentName) {
    if (panel) {
        panel.title = `Reactor: ${componentName}`;
        updatePanelHtml();
        panel.reveal(vscode.ViewColumn.Beside, true);
        return;
    }
    panel = vscode.window.createWebviewPanel("reactorPreview", `Reactor: ${componentName}`, { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true }, {
        enableScripts: true,
        retainContextWhenHidden: true,
        // SECURITY (TASK-028): no local resources are loaded; lock the roots empty
        // so an attacker who lands a `vscode-resource:` URL cannot fetch repo files.
        localResourceRoots: [],
    });
    panel.onDidDispose(() => {
        panel = undefined;
    });
    panel.webview.onDidReceiveMessage(async (msg) => {
        if (msg.type === "selectComponent" && msg.name) {
            outputChannel.appendLine(`[reactor] Component selected from dropdown: ${msg.name}`);
            await switchComponentViaHttp(msg.name);
        }
    });
    updatePanelHtml();
}
function updatePanelHtml() {
    if (!panel || !capturePort || !captureToken)
        return;
    panel.webview.html = getWebviewHtml(capturePort, captureToken, currentComponents, currentComponentName ?? currentComponents[0]);
}
function getWebviewHtml(port, token, components, selectedComponent) {
    const optionsHtml = components
        .map((c) => `<option value="${escapeHtml(c)}"${c === selectedComponent ? " selected" : ""}>${escapeHtml(c)}</option>`)
        .join("\n");
    const selectorHtml = components.length > 1
        ? `<select id="componentSelect" title="Select component to preview">${optionsHtml}</select>`
        : `<span class="component-name">${escapeHtml(selectedComponent)}</span>`;
    // SECURITY (TASK-028): nonce-bound CSP for inline scripts. connect-src is
    // pinned to the loopback capture server. img-src allows blob: because frames
    // are fetched and converted to object URLs in the script.
    const nonce = crypto.randomBytes(16).toString("base64");
    const csp = [
        "default-src 'none'",
        `connect-src http://127.0.0.1:* http://localhost:*`,
        `img-src blob: data:`,
        `style-src 'unsafe-inline'`,
        `script-src 'nonce-${nonce}'`,
    ].join("; ");
    return /*html*/ `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy" content="${csp}">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <style>
    * { margin: 0; padding: 0; box-sizing: border-box; }
    body {
      background: var(--vscode-editor-background);
      color: var(--vscode-editor-foreground);
      font-family: var(--vscode-font-family);
      display: flex;
      flex-direction: column;
      height: 100vh;
      overflow: hidden;
    }
    .toolbar {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 6px 12px;
      background: var(--vscode-titleBar-activeBackground);
      border-bottom: 1px solid var(--vscode-panel-border);
      flex-shrink: 0;
    }
    .toolbar button {
      background: var(--vscode-button-background);
      color: var(--vscode-button-foreground);
      border: none;
      padding: 4px 10px;
      cursor: pointer;
      font-size: 12px;
      border-radius: 2px;
      flex-shrink: 0;
    }
    .toolbar button:hover {
      background: var(--vscode-button-hoverBackground);
    }
    .toolbar select {
      background: var(--vscode-dropdown-background);
      color: var(--vscode-dropdown-foreground);
      border: 1px solid var(--vscode-dropdown-border);
      padding: 3px 6px;
      font-size: 12px;
      font-family: var(--vscode-font-family);
      border-radius: 2px;
      min-width: 0;
    }
    .component-name {
      font-size: 12px;
      font-weight: 600;
    }
    .status {
      font-size: 11px;
      opacity: 0.7;
      margin-left: auto;
      flex-shrink: 0;
    }
    .status.building {
      color: var(--vscode-charts-orange);
      opacity: 1;
    }
    .status.error {
      color: var(--vscode-errorForeground);
      opacity: 1;
    }
    .preview-container {
      flex: 1;
      display: flex;
      align-items: center;
      justify-content: center;
      overflow: auto;
      padding: 8px;
    }
    #preview {
      max-width: 100%;
      max-height: 100%;
      object-fit: contain;
      image-rendering: auto;
      border: 1px solid var(--vscode-panel-border);
      cursor: pointer;
    }
    #preview.stale {
      opacity: 0.5;
    }
    .placeholder {
      text-align: center;
      opacity: 0.5;
      font-size: 13px;
    }
  </style>
</head>
<body>
  <div class="toolbar">
    ${selectorHtml}
    <button id="focusBtn" title="Bring native preview window to front">Focus Window</button>
    <span id="status" class="status">Connecting...</span>
  </div>
  <div class="preview-container">
    <img id="preview" alt="Reactor Preview" style="display:none" />
    <div id="placeholder" class="placeholder">Waiting for first frame...</div>
  </div>

  <script nonce="${nonce}">
    const vscode = acquireVsCodeApi();
    const PORT = ${port};
    const AUTH = 'Bearer ' + ${JSON.stringify(token)};
    const AUTH_INIT = { headers: { Authorization: AUTH } };
    const AUTH_POST = { method: 'POST', headers: { Authorization: AUTH } };
    const img = document.getElementById('preview');
    const placeholder = document.getElementById('placeholder');
    const statusEl = document.getElementById('status');
    const focusBtn = document.getElementById('focusBtn');
    const componentSelect = document.getElementById('componentSelect');

    let frameUrl = 'http://localhost:' + PORT + '/frame';
    let statusUrl = 'http://localhost:' + PORT + '/status';
    let focusUrl = 'http://localhost:' + PORT + '/focus';
    let failCount = 0;
    let visible = true;

    if (componentSelect) {
      componentSelect.addEventListener('change', (e) => {
        vscode.postMessage({
          type: 'selectComponent',
          name: e.target.value
        });
      });
    }

    // Listen for messages from the extension (e.g. update dropdown selection)
    window.addEventListener('message', (event) => {
      const msg = event.data;
      if (msg.type === 'updateSelection' && componentSelect) {
        componentSelect.value = String(msg.selected ?? '');
      }
      if (msg.type === 'updateComponents' && componentSelect && Array.isArray(msg.components)) {
        // SECURITY (TASK-029): build options via DOM APIs. Component names are
        // attacker-controllable via the loopback endpoint; innerHTML would let
        // a hostile name inject script.
        while (componentSelect.firstChild) componentSelect.removeChild(componentSelect.firstChild);
        const selected = String(msg.selected ?? '');
        for (const c of msg.components) {
          const value = String(c);
          const opt = document.createElement('option');
          opt.value = value;
          opt.textContent = value;
          if (value === selected) opt.selected = true;
          componentSelect.appendChild(opt);
        }
      }
    });

    document.addEventListener('visibilitychange', () => {
      visible = !document.hidden;
    });

    async function refreshFrame() {
      if (!visible) {
        setTimeout(refreshFrame, 500);
        return;
      }

      try {
        const resp = await fetch(frameUrl, { cache: 'no-store', headers: { Authorization: AUTH } });
        if (resp.ok && resp.status === 200) {
          const blob = await resp.blob();
          const url = URL.createObjectURL(blob);
          img.onload = () => URL.revokeObjectURL(url);
          img.src = url;
          img.style.display = 'block';
          img.classList.remove('stale');
          placeholder.style.display = 'none';
          failCount = 0;
        }
      } catch {
        failCount++;
        if (failCount > 30) {
          img.classList.add('stale');
          statusEl.textContent = 'Disconnected';
          statusEl.className = 'status error';
        }
      }

      setTimeout(refreshFrame, 100);
    }

    async function refreshStatus() {
      try {
        const resp = await fetch(statusUrl, { cache: 'no-store', headers: { Authorization: AUTH } });
        if (resp.ok) {
          const data = await resp.json();
          if (data.building) {
            statusEl.textContent = 'Building...';
            statusEl.className = 'status building';
          } else if (data.error) {
            statusEl.textContent = 'Error: ' + data.error;
            statusEl.className = 'status error';
          } else {
            statusEl.textContent = 'Live';
            statusEl.className = 'status';
          }
        }
      } catch { /* ignore */ }

      setTimeout(refreshStatus, 1000);
    }

    focusBtn.addEventListener('click', () => {
      fetch(focusUrl, AUTH_POST).catch(() => {});
    });

    img.addEventListener('click', () => {
      fetch(focusUrl, AUTH_POST).catch(() => {});
    });

    refreshFrame();
    refreshStatus();
  </script>
</body>
</html>`;
}
// -- Commands ----------------------------------------------------------------
async function stopPreview() {
    editorChangeDisposable?.dispose();
    editorChangeDisposable = undefined;
    await killPreviewProcess();
    currentCsprojPath = undefined;
    currentComponents = [];
    currentComponentName = undefined;
    currentFilePath = undefined;
    statusBarItem.hide();
    if (panel) {
        panel.dispose();
        panel = undefined;
    }
}
async function focusPreviewWindow() {
    if (!capturePort) {
        vscode.window.showWarningMessage("No preview is running.");
        return;
    }
    try {
        await httpPost(`http://localhost:${capturePort}/focus`);
    }
    catch {
        vscode.window.showWarningMessage("Could not focus preview window.");
    }
}
// -- HTML Helpers ------------------------------------------------------------
function escapeHtml(s) {
    return s
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;");
}
// -- HTTP Helpers ------------------------------------------------------------
function httpGetJson(url) {
    return new Promise((resolve, reject) => {
        const opts = captureToken
            ? { headers: { Authorization: `Bearer ${captureToken}` } }
            : {};
        const req = http.get(url, opts, (res) => {
            if (res.statusCode && (res.statusCode < 200 || res.statusCode >= 300)) {
                res.resume();
                reject(new Error(`HTTP ${res.statusCode}`));
                return;
            }
            let body = "";
            res.on("data", (chunk) => (body += chunk));
            res.on("end", () => {
                try {
                    resolve(JSON.parse(body));
                }
                catch (e) {
                    reject(e);
                }
            });
        });
        req.on("error", reject);
        req.setTimeout(5000, () => {
            req.destroy();
            reject(new Error("timeout"));
        });
    });
}
function httpPostJson(url, data) {
    return new Promise((resolve, reject) => {
        const body = JSON.stringify(data);
        const headers = {
            "Content-Type": "application/json",
            "Content-Length": Buffer.byteLength(body),
        };
        if (captureToken)
            headers["Authorization"] = `Bearer ${captureToken}`;
        const req = http.request(url, {
            method: "POST",
            headers,
        }, (res) => {
            let resBody = "";
            res.on("data", (chunk) => (resBody += chunk));
            res.on("end", () => {
                try {
                    resolve(JSON.parse(resBody));
                }
                catch (e) {
                    reject(e);
                }
            });
        });
        req.on("error", reject);
        req.setTimeout(5000, () => {
            req.destroy();
            reject(new Error("timeout"));
        });
        req.write(body);
        req.end();
    });
}
function httpPost(url) {
    return new Promise((resolve, reject) => {
        const opts = { method: "POST" };
        if (captureToken)
            opts.headers = { Authorization: `Bearer ${captureToken}` };
        const req = http.request(url, opts, (res) => {
            res.resume();
            res.on("end", resolve);
        });
        req.on("error", reject);
        req.end();
    });
}
//# sourceMappingURL=extension.js.map