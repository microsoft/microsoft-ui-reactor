"use strict";
// Reactor Figma Sync — Main thread (sandbox)
// Extracts the selected frame's Figma URL and posts it to the UI.
// No bridge, no WebSocket — the UI constructs CLI commands that the
// developer runs in their terminal.
// ─── Plugin Lifecycle ────────────────────────────────────────────────────────
figma.showUI(__html__, { width: 360, height: 320, themeColors: true });
function getSelectedFrame() {
    const selection = figma.currentPage.selection;
    if (selection.length === 1 && selection[0].type === "FRAME") {
        return selection[0];
    }
    return null;
}
function sendFrameInfo() {
    const frame = getSelectedFrame();
    if (frame) {
        // Build the Figma URL for this frame
        const fileKey = figma.fileKey;
        const nodeId = frame.id; // format: "123:456"
        const urlNodeId = nodeId.replace(":", "-"); // URL format: "123-456"
        const figmaUrl = fileKey
            ? `https://www.figma.com/design/${fileKey}/${encodeURIComponent(figma.root.name)}?node-id=${urlNodeId}`
            : null;
        figma.ui.postMessage({
            type: "frame-selected",
            frameId: frame.id,
            frameName: frame.name,
            fileKey: fileKey !== null && fileKey !== void 0 ? fileKey : "",
            nodeId: urlNodeId,
            figmaUrl,
            width: Math.round(frame.width),
            height: Math.round(frame.height),
        });
    }
    else {
        figma.ui.postMessage({
            type: "no-frame",
        });
    }
}
// Track selection changes
figma.on("selectionchange", sendFrameInfo);
// Handle messages from the UI
figma.ui.onmessage = (msg) => {
    if (msg.type === "request-frame-info") {
        sendFrameInfo();
    }
};
// Send initial frame info
sendFrameInfo();
