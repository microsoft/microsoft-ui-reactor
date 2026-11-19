import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

import { defineConfig } from "blume";

// One build per published version. scripts/publish.mjs sets these; a local
// `npm run dev` sets none and gets a plain site at the root with no version
// switcher or banner.
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
const siteRoot = base && version ? base.slice(0, base.lastIndexOf("/") + 1) : "/";
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
const releases = published.map((entry) => entry.version).filter((id) => id !== "main");

// Older builds keep the list they were built with, so the first two entries
// are the ones that never go stale: they follow the alias, not a number.
const versionItems = [
  { label: "Latest release", path: alias === "latest" ? "/" : versionUrl("latest") },
  { label: "Development (main)", path: version === "main" ? "/" : versionUrl("main") },
  ...releases.map((id) => ({ label: id, path: alias !== "latest" && version === id ? "/" : versionUrl(id) })),
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

export default defineConfig({
  title: "Reactor",
  description: "Declarative UI framework for native Windows desktop apps in pure C#.",
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
      { label: "Docs", path: "/" },
      { label: "Controls", path: "/controls" },
      { label: "Recipes", path: "/recipes" },
      { label: "API reference", path: "/reference" },
      { label: "Internals", path: "/internals" },
    ],
    selectors: version ? [{ kind: "version", label: "Version", items: versionItems }] : [],
  },
});
