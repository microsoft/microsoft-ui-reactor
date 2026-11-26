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
// A local `npm run dev` sets none and gets a plain site at the root with no
// version switcher or banner.

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
  const siteRoot = base && version ? base.slice(0, base.lastIndexOf("/") + 1) : "/";
  const latestUrl = `${site}${siteRoot}`;
  const versionUrl = (id) => `${site}${siteRoot}${id}/`;

  const releases = published.map((entry) => entry.version).filter((id) => id !== "main");

  // Older builds keep the list they were built with, so the first two entries
  // are the ones that never go stale: they follow the alias, not a number.
  const versionItems = [
    { label: "Latest release", path: alias === "latest" ? "/" : versionUrl("latest") },
    { label: "Development (main)", path: version === "main" ? "/" : versionUrl("main") },
    ...releases.map((id) => ({ label: id, path: alias !== "latest" && version === id ? "/" : versionUrl(id) })),
  ];

  let banner;
  if (version && alias !== "latest") {
    banner = {
      content:
        version === "main"
          ? "You're reading the docs for the unreleased main branch."
          : `You're reading the docs for version ${version}.`,
      link: { href: latestUrl, text: "Go to the latest release" },
    };
  }

  return {
    site,
    base,
    siteRoot,
    banner,
    selectors: version ? [{ kind: "version", label: "Version", items: versionItems }] : [],
  };
}
