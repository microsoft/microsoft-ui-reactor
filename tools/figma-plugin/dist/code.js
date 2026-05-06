"use strict";
// Reactor Figma Sync — Main thread (sandbox)
// Watches for design changes and sends the visible node tree to the bridge server.
// ─── Node Extraction ─────────────────────────────────────────────────────────
async function extractNode(node) {
    // Skip hidden nodes and their entire subtree
    if (!node.visible)
        return null;
    const data = {
        id: node.id,
        name: node.name,
        type: node.type,
        visible: node.visible,
        width: node.width,
        height: node.height,
    };
    // Layout properties (auto-layout frames)
    if ("layoutMode" in node && node.layoutMode !== "NONE") {
        const frame = node;
        data.layoutMode = frame.layoutMode;
        data.itemSpacing = frame.itemSpacing;
        data.paddingTop = frame.paddingTop;
        data.paddingRight = frame.paddingRight;
        data.paddingBottom = frame.paddingBottom;
        data.paddingLeft = frame.paddingLeft;
    }
    // Corner radius
    if ("cornerRadius" in node) {
        const cr = node.cornerRadius;
        if (typeof cr === "number" && cr > 0) {
            data.cornerRadius = cr;
        }
    }
    // Text properties
    if (node.type === "TEXT") {
        const textNode = node;
        data.characters = textNode.characters;
        if (typeof textNode.fontSize === "number") {
            data.fontSize = textNode.fontSize;
        }
        if (typeof textNode.fontWeight === "number") {
            data.fontWeight = textNode.fontWeight;
        }
        if (textNode.fontName !== figma.mixed && typeof textNode.fontName === "object") {
            data.fontFamily = textNode.fontName.family;
        }
        if (typeof textNode.lineHeight === "object" && "value" in textNode.lineHeight) {
            const lh = textNode.lineHeight;
            if (lh.unit === "PIXELS")
                data.lineHeight = lh.value;
        }
    }
    // Fills (visible only)
    if ("fills" in node) {
        const fills = node.fills;
        if (Array.isArray(fills)) {
            data.fills = fills
                .filter((f) => f.visible !== false)
                .map((f) => ({
                type: f.type,
                visible: f.visible !== false,
                color: f.type === "SOLID" ? f.color : undefined,
                opacity: f.type === "SOLID" ? f.opacity : undefined,
            }));
        }
    }
    // Strokes
    if ("strokes" in node) {
        const strokes = node.strokes;
        if (Array.isArray(strokes) && strokes.length > 0) {
            data.strokes = strokes
                .filter((s) => s.visible !== false)
                .map((s) => ({
                type: s.type,
                visible: s.visible !== false,
                weight: "strokeWeight" in node ? node.strokeWeight : undefined,
            }));
        }
    }
    // Component instance name (async for dynamic-page)
    if (node.type === "INSTANCE") {
        const instance = node;
        const mainComp = await instance.getMainComponentAsync();
        if (mainComp) {
            data.componentName = mainComp.name;
        }
    }
    // Recurse into children (visible only)
    if ("children" in node) {
        const parent = node;
        const children = [];
        for (const child of parent.children) {
            const extracted = await extractNode(child);
            if (extracted)
                children.push(extracted);
        }
        if (children.length > 0) {
            data.children = children;
        }
    }
    return data;
}
// ─── Change Handling ─────────────────────────────────────────────────────────
let debounceTimer = null;
const DEBOUNCE_MS = 300;
function getWatchedFrame() {
    const selection = figma.currentPage.selection;
    if (selection.length === 1 && selection[0].type === "FRAME") {
        return selection[0];
    }
    return null;
}
let watchedFrameId = null;
async function sendFullSync() {
    let frame = null;
    if (watchedFrameId) {
        frame = await figma.getNodeByIdAsync(watchedFrameId);
    }
    if (!frame || frame.type !== "FRAME") {
        frame = getWatchedFrame();
        if (frame)
            watchedFrameId = frame.id;
    }
    if (!frame) {
        figma.ui.postMessage({ type: "status", message: "Select a frame to watch" });
        return;
    }
    const tree = await extractNode(frame);
    if (!tree)
        return;
    const msg = {
        type: "full-sync",
        frameId: frame.id,
        frameName: frame.name,
        timestamp: Date.now(),
        tree,
    };
    figma.ui.postMessage({ type: "sync", payload: msg });
}
function onNodeChange(event) {
    if (!watchedFrameId)
        return;
    // Collect incremental property changes for the fast codegen path
    const patches = [];
    for (const change of event.nodeChanges) {
        if (change.type === "PROPERTY_CHANGE") {
            const node = change.node;
            for (const prop of change.properties) {
                // Fast-path properties: text, spacing, sizing, radius
                if (prop === "characters" && node.type === "TEXT") {
                    patches.push({
                        nodeId: node.id,
                        nodeName: node.name,
                        property: "characters",
                        value: node.characters,
                    });
                }
                else if (prop === "fontSize" && node.type === "TEXT") {
                    const fontSize = node.fontSize;
                    if (typeof fontSize === "number") {
                        patches.push({ nodeId: node.id, nodeName: node.name, property: "fontSize", value: fontSize });
                    }
                }
                else if (prop === "fontName" && node.type === "TEXT") {
                    const fw = node.fontWeight;
                    if (typeof fw === "number") {
                        patches.push({ nodeId: node.id, nodeName: node.name, property: "fontWeight", value: fw });
                    }
                }
                else if (prop === "itemSpacing" && "itemSpacing" in node) {
                    patches.push({ nodeId: node.id, nodeName: node.name, property: "itemSpacing", value: node.itemSpacing });
                }
                else if (prop === "paddingTop" && "paddingTop" in node) {
                    const f = node;
                    patches.push({ nodeId: node.id, nodeName: node.name, property: "padding", value: [f.paddingLeft, f.paddingTop, f.paddingRight, f.paddingBottom] });
                }
                else if (prop === "paddingLeft" || prop === "paddingRight" || prop === "paddingBottom") {
                    // Already handled by paddingTop batch above — skip duplicate
                }
                else if (prop === "width") {
                    patches.push({ nodeId: node.id, nodeName: node.name, property: "width", value: node.width });
                }
                else if (prop === "height") {
                    patches.push({ nodeId: node.id, nodeName: node.name, property: "height", value: node.height });
                }
                else if (prop === "cornerRadius" && "cornerRadius" in node) {
                    const cr = node.cornerRadius;
                    if (typeof cr === "number") {
                        patches.push({ nodeId: node.id, nodeName: node.name, property: "cornerRadius", value: cr });
                    }
                }
                else if (prop === "visible") {
                    patches.push({ nodeId: node.id, nodeName: node.name, property: "visible", value: node.visible });
                }
            }
        }
        else if (change.type === "CREATE" || change.type === "DELETE") {
            // Structural changes → need full re-sync (LLM path)
            patches.length = 0; // clear patches — full sync needed
            break;
        }
    }
    // Debounce
    if (debounceTimer !== null) {
        clearTimeout(debounceTimer);
    }
    debounceTimer = setTimeout(() => {
        debounceTimer = null;
        if (patches.length > 0) {
            // Fast path: send incremental patches
            figma.ui.postMessage({
                type: "sync",
                payload: {
                    type: "incremental",
                    frameId: watchedFrameId,
                    frameName: "",
                    timestamp: Date.now(),
                    patches,
                },
            });
        }
        else {
            // Slow path: full re-sync for structural changes
            sendFullSync();
        }
    }, DEBOUNCE_MS);
}
// ─── Plugin Lifecycle ────────────────────────────────────────────────────────
figma.showUI(__html__, { width: 340, height: 320, themeColors: true });
// Watch for selection changes — also triggers when exiting text edit mode
figma.on("selectionchange", () => {
    const frame = getWatchedFrame();
    if (frame) {
        watchedFrameId = frame.id;
        figma.ui.postMessage({
            type: "status",
            message: `Watching: ${frame.name}`,
        });
    }
    // Always re-sync when selection changes — text edits commit on deselect
    if (watchedFrameId) {
        sendFullSync();
    }
});
// Watch for design changes on the current page
figma.currentPage.on("nodechange", onNodeChange);
// Handle messages from the UI iframe
figma.ui.onmessage = (msg) => {
    if (msg.type === "request-sync") {
        sendFullSync();
    }
    else if (msg.type === "generate") {
        const frame = getWatchedFrame();
        if (frame) {
            watchedFrameId = frame.id;
        }
        if (watchedFrameId) {
            (async () => {
                const frame = await figma.getNodeByIdAsync(watchedFrameId);
                if (!frame) {
                    figma.ui.postMessage({ type: "status", message: "Frame not found — select a frame first" });
                    return;
                }
                const tree = await extractNode(frame);
                if (!tree) {
                    figma.ui.postMessage({ type: "status", message: "Failed to extract frame" });
                    return;
                }
                figma.ui.postMessage({
                    type: "sync",
                    payload: {
                        type: "generate",
                        frameId: frame.id,
                        frameName: frame.name,
                        timestamp: Date.now(),
                        tree,
                    },
                });
            })();
        }
        else {
            figma.ui.postMessage({ type: "status", message: "No frame selected — select a frame in Figma first" });
        }
    }
    else if (msg.type === "save-config") {
        if (msg.outputPath) {
            figma.clientStorage.setAsync("outputPath", msg.outputPath);
        }
    }
    else if (msg.type === "load-config") {
        (async () => {
            const outputPath = await figma.clientStorage.getAsync("outputPath");
            figma.ui.postMessage({
                type: "config-loaded",
                outputPath: outputPath || "",
            });
        })();
    }
};
// Initial sync if a frame is already selected
const initialFrame = getWatchedFrame();
if (initialFrame) {
    watchedFrameId = initialFrame.id;
    sendFullSync();
}
