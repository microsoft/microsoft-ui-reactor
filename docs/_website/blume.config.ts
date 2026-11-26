import { existsSync, readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

import { defineConfig } from "blume";
import { script } from "blume/analytics";

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

const { site, base, version, siteRoot, banner, selectors } = siteSettings(
  process.env,
  readJson<PublishedVersion[]>(process.env.DOCS_VERSIONS_FILE, [])
);

// Written by scripts/sync.mjs: every page whose MkDocs URL changed.
const redirects = readJson<{ from: string; to: string }[]>(
  fileURLToPath(new URL("./redirects.generated.json", import.meta.url)),
  []
);

// scripts/sync.mjs writes docs/changelog only when CHANGELOG.md has a dated
// release. An old tag rebuilt with this config may have none, and then there
// is no /changelog to link to.
const changelogDir = fileURLToPath(new URL("./docs/changelog/", import.meta.url));
const changelogLinks =
  existsSync(changelogDir) && readdirSync(changelogDir).some((file) => file.endsWith(".md"))
    ? [{ label: "Changelog", href: "/changelog" }]
    : [];

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
  changelog: {
    title: "Changelog",
    description: "What changed in each Reactor release.",
  },
  // Not analytics: script() is Blume's hook for a site script on every page.
  // It loads in production builds only, which is every published version.
  // A module, so it can import version-paths.js; modules are deferred anyway.
  analytics: version
    ? [
        script({
          src: "/js/version-menu.js",
          attributes: { type: "module", "data-root": siteRoot, "data-version": version },
        }),
      ]
    : [],
  navigation: {
    tabs: [
      { label: "Home", path: "/" },
      { label: "Get started", path: "/get-started" },
      { label: "Guides", path: "/guides" },
      { label: "Controls", path: "/controls" },
      { label: "Tools", path: "/tools" },
      { label: "API reference", path: "/reference" },
      { label: "How it works", path: "/how-it-works" },
    ],
    actions: changelogLinks,
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
      ...changelogLinks,
    ],
  },
});
