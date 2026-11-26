import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

import { defineConfig } from "blume";

import { siteSettings } from "./scripts/site-env.mjs";

// One build per published version. scripts/publish.mjs sets the DOCS_*
// variables; scripts/site-env.mjs documents them and derives the settings.
interface PublishedVersion {
  version: string;
  title?: string;
  aliases?: string[];
}

const readJson = <T>(file: string | undefined, fallback: T): T =>
  file && existsSync(file) ? (JSON.parse(readFileSync(file, "utf8")) as T) : fallback;

const { site, base, banner, selectors } = siteSettings(
  process.env,
  readJson<PublishedVersion[]>(process.env.DOCS_VERSIONS_FILE, [])
);

// Written by scripts/sync.mjs: every page whose MkDocs URL changed.
const redirects = readJson<{ from: string; to: string }[]>(
  fileURLToPath(new URL("./redirects.generated.json", import.meta.url)),
  []
);

export default defineConfig({
  title: "Reactor",
  description: "Declarative UI framework for native Windows desktop apps in pure C#.",
  // The WinUI logo, from the Windows brand assets. Inlined next to the title,
  // and used as the brand mark on the generated social cards.
  logo: "/brand/winui.svg",
  github: { owner: "microsoft", repo: "microsoft-ui-reactor", branch: "main" },
  deployment: base ? { site, base } : { site },
  theme: {
    accent: "#0067c0",
    radius: "md",
    fonts: { display: "inter", body: "inter", mono: "geist-mono" },
  },
  banner,
  redirects,
  navigation: {
    tabs: [
      { label: "Home", path: "/" },
      { label: "Get started", path: "/get-started" },
      { label: "Guides", path: "/guides" },
      { label: "Controls", path: "/controls" },
      { label: "Recipes", path: "/recipes" },
      { label: "API reference", path: "/reference" },
      { label: "How it works", path: "/how-it-works" },
    ],
    selectors,
  },
  footer: {
    copyright: "© Microsoft Corporation. Released under the MIT License.",
    links: [
      { label: "Showcase", href: "/showcase" },
      {
        label: "Blog",
        href: "https://devblogs.microsoft.com/ifdef-windows/microsoft-ui-reactor-a-c-first-way-to-build-winui-apps/",
      },
      { label: "Samples", href: "https://github.com/microsoft/microsoft-ui-reactor/tree/main/samples" },
      { label: "Changelog", href: "https://github.com/microsoft/microsoft-ui-reactor/blob/main/CHANGELOG.md" },
    ],
  },
});
