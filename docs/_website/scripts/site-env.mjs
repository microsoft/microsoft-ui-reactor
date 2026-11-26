// The per-version settings blume.config.ts derives from the environment that
// scripts/publish.mjs sets. Kept apart from the config so the tests can check
// them without running a Blume build.
//
//   DOCS_SITE           origin the site is served from
//   DOCS_BASE           path the build is served under, e.g. /microsoft-ui-reactor/main
//   DOCS_VERSION        the version this build is for: "main" or a release like 0.1.0-preview.19
//   DOCS_ALIAS          "latest" when this build is the copy served at /latest/
//   DOCS_VERSIONS_FILE  versions.json (mike's format) listing every published version
//
// A local `npm run dev` sets none and gets a plain site at the root, whose
// version menu links to the published docs, and no banner.

import { DEFAULT_SITE_BASE, LATEST_ALIAS, MAIN_VERSION } from "./publish.mjs";

/**
 * @param {Record<string, string | undefined>} env
 * @param {{ version: string, title?: string, aliases?: string[] }[]} published  the versions.json entries
 */
export function siteSettings(env, published = []) {
  const site = env.DOCS_SITE || "https://microsoft.github.io";
  const base = env.DOCS_BASE?.replace(/\/+$/, "") || undefined;
  const version = env.DOCS_VERSION || undefined;
  const alias = env.DOCS_ALIAS || undefined;

  // The site root: the folder that holds every version, and redirects to latest/.
  const siteRoot = base && version ? base.slice(0, base.lastIndexOf("/") + 1) : `${DEFAULT_SITE_BASE}/`;
  const latestUrl = `${site}${siteRoot}`;
  const versionUrl = (id) => `${site}${siteRoot}${id}/`;

  // The version menu, in versions.json order like mike's: main, then releases
  // newest first, with the release that holds "latest" linking to /latest/.
  // This list is the one the build was made with; public/js/version-menu.js
  // rebuilds it from the live versions.json, so older builds list newer
  // releases too, and opens the same page in the version you pick.
  const isLatest = (entry) => entry.aliases?.includes(LATEST_ALIAS) ?? false;
  const versionTag = (entry) =>
    isLatest(entry) ? LATEST_ALIAS : entry.version === MAIN_VERSION ? "unreleased" : undefined;
  const entries =
    version && !published.some((entry) => entry.version === version)
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

  let banner;
  if (version && alias !== LATEST_ALIAS) {
    banner = {
      content:
        version === MAIN_VERSION
          ? "You're reading the docs for the unreleased main branch."
          : `You're reading the docs for version ${version}.`,
      link: { href: latestUrl, text: "Go to the latest release" },
    };
  }

  return {
    site,
    base,
    version,
    siteRoot,
    banner,
    selectors: [{ kind: "version", label: "Version", items: versionItems }],
  };
}
