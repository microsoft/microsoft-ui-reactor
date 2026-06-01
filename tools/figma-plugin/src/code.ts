// Reactor Figma Sync — Main thread (sandbox)
// Extracts the selected frame's Figma URL and posts it to the UI.
// No bridge, no WebSocket — the UI constructs CLI commands that the
// developer runs in their terminal.

// ─── Plugin Lifecycle ────────────────────────────────────────────────────────

figma.showUI(__html__, { width: 340, height: 160, themeColors: true });

// Accept FRAME, COMPONENT, COMPONENT_SET, and SECTION as valid targets
const FRAME_TYPES: string[] = ["FRAME", "COMPONENT", "COMPONENT_SET", "SECTION"];

function getSelectedFrame(): SceneNode | null {
  const selection = figma.currentPage.selection;
  if (selection.length === 1 && FRAME_TYPES.includes(selection[0].type)) {
    return selection[0];
  }
  if (selection.length === 1 && "children" in selection[0]) {
    return selection[0];
  }
  return null;
}

// Resolve file key: figma.fileKey (private plugins) or user-provided fallback
let cachedFileKey: string | null = null;

async function resolveFileKey(): Promise<string | null> {
  // 1. Try the native API (requires enablePrivatePluginApi + private plugin)
  if (figma.fileKey) return figma.fileKey;
  // 2. Try cached value from this session
  if (cachedFileKey) return cachedFileKey;
  // 3. Try persisted value from clientStorage
  const stored = await figma.clientStorage.getAsync("fileKey") as string | undefined;
  if (stored) { cachedFileKey = stored; return stored; }
  return null;
}

async function sendFrameInfo() {
  const frame = getSelectedFrame();
  if (frame) {
    const fileKey = await resolveFileKey();
    const nodeId = frame.id;
    const urlNodeId = nodeId.replace(":", "-");

    var figmaUrl = "";
    if (fileKey) {
      figmaUrl = "https://www.figma.com/design/" + fileKey +
        "?node-id=" + urlNodeId;
    }

    figma.ui.postMessage({
      type: "frame-selected",
      frameId: frame.id,
      frameName: frame.name,
      fileKey: fileKey || "",
      nodeId: urlNodeId,
      figmaUrl: figmaUrl,
      width: Math.round(frame.width),
      height: Math.round(frame.height),
      needsFileKey: !fileKey,
    });
  } else {
    figma.ui.postMessage({
      type: "no-frame",
      message: figma.currentPage.selection.length === 0
        ? "Nothing selected"
        : "Selected node type: " + figma.currentPage.selection[0].type,
    });
  }
}

// Track selection changes
figma.on("selectionchange", () => { sendFrameInfo(); });

// Handle messages from the UI
figma.ui.onmessage = (msg: { type: string; url?: string }) => {
  if (msg.type === "request-frame-info") {
    sendFrameInfo();
  } else if (msg.type === "set-file-url" && msg.url) {
    // User pasted a Figma URL — extract the file key
    const match = msg.url.match(/figma\.com\/(?:design|file)\/([a-zA-Z0-9]+)/i);
    if (match) {
      cachedFileKey = match[1];
      figma.clientStorage.setAsync("fileKey", cachedFileKey);
      sendFrameInfo(); // re-send with the resolved key
    }
  }
};

// Send initial frame info
sendFrameInfo();
