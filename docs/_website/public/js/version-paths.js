// The path and folder rules behind version-menu.js, kept free of the DOM so
// scripts/version-menu.test.mjs can run them under node.
//
// versions.json is mike's format: [{ version, title, aliases }], newest first.
// The entry whose aliases hold "latest" is served from /latest/, every other
// one from a folder named after its version.

export const LATEST = "latest";

export const isLatest = (entry) => entry.aliases?.includes(LATEST) ?? false;

export const folderOf = (entry) => (isLatest(entry) ? LATEST : entry.version);

export const tagOf = (entry) => (isLatest(entry) ? LATEST : entry.version === "main" ? "unreleased" : "");

export const homeOf = (root, entry) => `${root}${folderOf(entry)}/`;

// The page's path inside its version folder: "guides/hooks/" for
// /microsoft-ui-reactor/main/guides/hooks/ with root /microsoft-ui-reactor/.
// Empty for a version's home page or a path outside the site.
export const pageInVersion = (pathname, root) => {
  if (!pathname.startsWith(root)) return "";
  const rest = pathname.slice(root.length);
  const slash = rest.indexOf("/");
  return slash === -1 ? "" : rest.slice(slash + 1);
};

// The rows the menu shows, or null when versions.json is missing or empty and
// the build's own rows should stay. A build newer than versions.json still
// lists itself first, so the reader can see where they are.
export const menuEntries = (list, current) => {
  if (!Array.isArray(list) || list.length === 0) return null;
  return list.some((entry) => entry.version === current) ? list : [{ version: current, aliases: [] }, ...list];
};
