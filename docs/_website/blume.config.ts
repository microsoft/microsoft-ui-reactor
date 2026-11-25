import { existsSync, readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

import { defineConfig } from "blume";
import { script } from "blume/analytics";

import { DEFAULT_SITE_BASE, LATEST_ALIAS, MAIN_VERSION } from "./scripts/publish.mjs";

// One build per published version. scripts/publish.mjs sets these; a local
// `npm run dev` sets none and gets a plain site at the root, whose version
// menu links to the published docs, and no banner.
//
//   DOCS_SITE           origin the site is served from
//   DOCS_BASE           path the build is served under, e.g. /microsoft-ui-reactor/main
//   DOCS_VERSION        the version this build is for: "main" or a release like 0.1.0-preview.19
//   DOCS_ALIAS          "latest" when this build is the copy served at /latest/
//   DOCS_VERSIONS_FILE  versions.json (mike's format) listing every published version
const env = process.env;
const site = env.DOCS_SITE || "https://microsoft.github.io";
const base = env.DOCS_BASE?.replace(/\/+$/, "") || undefined;
const version = env.DOCS_VERSION || undefined;
const alias = env.DOCS_ALIAS || undefined;

// The site root: the folder that holds every version, and redirects to latest/.
const siteRoot = base && version ? base.slice(0, base.lastIndexOf("/") + 1) : `${DEFAULT_SITE_BASE}/`;
const latestUrl = `${site}${siteRoot}`;
const versionUrl = (id: string) => `${site}${siteRoot}${id}/`;

interface PublishedVersion {
  version: string;
  title?: string;
  aliases?: string[];
}

const readJson = <T>(file: string | undefined, fallback: T): T =>
  file && existsSync(file) ? (JSON.parse(readFileSync(file, "utf8")) as T) : fallback;

const published = readJson<PublishedVersion[]>(env.DOCS_VERSIONS_FILE, []);

// The version menu, in versions.json order like mike's: main, then releases
// newest first, with the release that holds "latest" linking to /latest/.
// This list is the one the build was made with; public/js/version-menu.js
// rebuilds it from the live versions.json, so older builds list newer
// releases too, and opens the same page in the version you pick.
const isLatest = (entry: PublishedVersion) => entry.aliases?.includes(LATEST_ALIAS) ?? false;
const versionTag = (entry: PublishedVersion) =>
  isLatest(entry) ? LATEST_ALIAS : entry.version === MAIN_VERSION ? "unreleased" : undefined;
const entries = version && !published.some((entry) => entry.version === version)
  ? [{ version, aliases: alias ? [alias] : [] }, ...published]
  : published;
const versionItems = version
  ? entries.map((entry) => ({
      label: entry.version,
      tag: versionTag(entry),
      path: entry.version === version ? "/" : versionUrl(isLatest(entry) ? LATEST_ALIAS : entry.version),
    }))
  : [
      { label: "Local", tag: "this build", path: "/" },
      { label: "Latest release", tag: LATEST_ALIAS, path: versionUrl(LATEST_ALIAS) },
      { label: MAIN_VERSION, tag: "unreleased", path: versionUrl(MAIN_VERSION) },
    ];

let banner: { content: string; link: { href: string; text: string } } | undefined;
if (version && alias !== "latest") {
  banner = {
    content:
      version === "main"
        ? "You're reading the docs for the unreleased main branch."
        : `You're reading the docs for version ${version}.`,
    link: { href: latestUrl, text: "Go to the latest release" },
  };
}

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
    selectors: [{ kind: "version", label: "Version", items: versionItems }],
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
