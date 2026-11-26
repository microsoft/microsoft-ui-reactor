// Before the move to Blume, the guide's index page was the site root, so links
// like /latest/#minimal-project-setup point at its sections. That page now
// lives at /get-started and the root is the custom home page. This script, put
// inline on the home page, sends a fragment the home page does not have to the
// page that owns it now, keeping the query.
//
// Blume uses Astro's client router, which runs an inline script only once and
// keeps its listeners across page swaps. So the listeners are registered once,
// act only while the home page is showing, and also run on `astro:page-load`
// for a client-side navigation back to the home page.

/**
 * Old index-page fragments whose section moved or was renamed by #1692.
 * Keys are the old fragment, values are `[path relative to the site base, new fragment]`.
 * A fragment that is absent from this map still goes to /get-started unchanged.
 */
const MOVED_FRAGMENTS = Object.freeze({
  "minimal-project-setup": ["/get-started/install", "minimal-project-setup"],
  "why-reactor": ["/get-started", "why-pick-reactor"],
  "quick-look": ["/get-started", "a-first-look"],
});

/**
 * The inline script for the home page.
 * @param {string} target  the base-aware /get-started URL
 * @param {string} home    the base-aware home page path, e.g. /microsoft-ui-reactor/latest/
 */
export function legacyFragmentScript(target, home) {
  const suffix = "/get-started";
  const base = target.endsWith(suffix) ? target.slice(0, -suffix.length) : "";
  return `(() => {
  if (globalThis.__reactorLegacyFragments) return;
  globalThis.__reactorLegacyFragments = true;
  const home = ${JSON.stringify(home.replace(/\/+$/, ""))};
  const base = ${JSON.stringify(base)};
  const moved = ${JSON.stringify(MOVED_FRAGMENTS)};
  const forward = () => {
    if (location.pathname.replace(/\\/+$/, "") !== home) return;
    const id = decodeURIComponent(location.hash.slice(1));
    if (!id || document.getElementById(id)) return;
    const movedTo = moved[id];
    const url = movedTo
      ? base + movedTo[0] + location.search + "#" + movedTo[1]
      : ${JSON.stringify(target)} + location.search + location.hash;
    location.replace(url);
  };
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", forward);
  else forward();
  addEventListener("hashchange", forward);
  document.addEventListener("astro:page-load", forward);
})();`;
}
