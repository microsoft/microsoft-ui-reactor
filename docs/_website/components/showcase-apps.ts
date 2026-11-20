import type { BloomVariant } from "./bloom-palettes";

export interface ShowcaseApp {
  id: string;
  name: string;
  /** One sentence: what the app is and what it shows about Reactor. */
  summary: string;
  /** A short label for what kind of app it is. */
  kind: string;
  /** Folder in the repository. */
  path: string;
  /** Lucide icon name. */
  icon: string;
  bloom: BloomVariant;
  /** Shown large at the top of the showcase. */
  featured?: boolean;
  /** A screenshot in public/showcase. Cards without one show the icon tile. */
  screenshot?: { src: string; width: number; height: number };
}

export const SOURCE_ROOT = "https://github.com/microsoft/microsoft-ui-reactor/tree/main/";

export const SUBMIT_URL =
  "https://github.com/microsoft/microsoft-ui-reactor/issues/new?template=showcase.yml";

const shot = (id: string, width: number, height: number) => ({
  src: `/showcase/${id}.webp`,
  width,
  height,
});

export const SHOWCASE_APPS: ShowcaseApp[] = [
  {
    id: "gallery",
    name: "Reactor Gallery",
    summary:
      "Every WinUI 3 control, rebuilt with the Reactor DSL. The Reactor version of WinUI 3 Gallery, with live samples and their source.",
    kind: "Controls",
    path: "samples/ReactorGallery",
    icon: "layout-grid",
    bloom: "blue",
    featured: true,
    screenshot: shot("gallery", 1280, 802),
  },
  {
    id: "ide",
    name: "Reactor IDE",
    summary:
      "A Visual Studio-style layout of tool windows, documents and panels, built on Reactor's docking system.",
    kind: "Docking",
    path: "samples/apps/reactor-ide",
    icon: "panels-top-left",
    bloom: "violet",
    featured: true,
    screenshot: shot("ide", 1280, 802),
  },
  {
    id: "regedit",
    name: "Registry Editor",
    summary: "A port of regedit.exe to a declarative component model.",
    kind: "Port",
    path: "samples/apps/regedit",
    icon: "folder-tree",
    bloom: "teal",
    screenshot: shot("regedit", 800, 501),
  },
  {
    id: "headtrax",
    name: "HeadTrax",
    summary: "A data grid over SQLite and GraphQL data sources.",
    kind: "Data",
    path: "samples/apps/headtrax",
    icon: "table",
    bloom: "green",
    screenshot: shot("headtrax", 800, 501),
  },
  {
    id: "charting",
    name: "Charting Gallery",
    summary: "Dozens of chart samples, from bar and donut charts to force-directed graphs and chord diagrams.",
    kind: "Charts",
    path: "samples/ReactorCharting.Gallery",
    icon: "chart-column",
    bloom: "amber",
    screenshot: shot("charting", 800, 501),
  },
  {
    id: "particles",
    name: "Particle Storm",
    summary: "Up to 100,000 particles in a Win2D animated canvas, with a sidebar built in Reactor.",
    kind: "Win2D",
    path: "samples/apps/particle-storm",
    icon: "sparkles",
    bloom: "rose",
    screenshot: shot("particles", 800, 501),
  },
  {
    id: "netpulse",
    name: "NetPulse",
    summary: "A stress test for the reconciler, driven by live network data.",
    kind: "Performance",
    path: "samples/apps/netpulse",
    icon: "activity",
    bloom: "blue",
    screenshot: shot("netpulse", 800, 501),
  },
  {
    id: "chat",
    name: "Chat",
    summary: "A native chat app with an in-memory provider. No server or API key needed.",
    kind: "App",
    path: "samples/apps/chat",
    icon: "message-square",
    bloom: "violet",
    screenshot: shot("chat", 800, 501),
  },
  {
    id: "widget-creator",
    name: "Widget Creator",
    summary: "Turns a one-line prompt into a single-file Reactor app, builds it, and runs it in a sandbox.",
    kind: "AI",
    path: "samples/apps/widget-creator",
    icon: "wand-sparkles",
    bloom: "teal",
    screenshot: shot("widget-creator", 800, 501),
  },
  {
    id: "minesweeper",
    name: "Minesweeper",
    summary: "The classic game, with all game state in one UseReducer, saved high scores, and dialogs.",
    kind: "Game",
    path: "samples/apps/minesweeper",
    icon: "bomb",
    bloom: "green",
    screenshot: shot("minesweeper", 626, 613),
  },
  {
    id: "wordpuzzle",
    name: "Word Puzzle",
    summary: "A sliding tile puzzle, ported from the .NET MAUI sample.",
    kind: "Game",
    path: "samples/apps/wordpuzzle",
    icon: "puzzle",
    bloom: "amber",
    screenshot: shot("wordpuzzle", 800, 603),
  },
  {
    id: "toolkit",
    name: "Toolkit controls",
    summary: "25 Windows Community Toolkit controls, wrapped for Reactor by the wrapper generator.",
    kind: "Interop",
    path: "samples/apps/wct-controls",
    icon: "blocks",
    bloom: "rose",
    screenshot: shot("toolkit", 800, 501),
  },
  {
    id: "a11y",
    name: "A11y Showcase",
    summary: "A task tracker that shows accessibility best practices.",
    kind: "Accessibility",
    path: "samples/apps/a11y-showcase",
    icon: "accessibility",
    bloom: "blue",
    screenshot: shot("a11y", 800, 501),
  },
  {
    id: "monaco",
    name: "Monaco editor",
    summary: "VS Code's Monaco editor in WebView2, wrapped as a custom Reactor control.",
    kind: "Custom control",
    path: "samples/apps/monaco-editor",
    icon: "code",
    bloom: "violet",
    screenshot: shot("monaco", 800, 501),
  },
];
