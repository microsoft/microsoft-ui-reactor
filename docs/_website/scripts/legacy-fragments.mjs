// Before the move to Blume, the guide's index page was the site root, so links
// like /latest/#minimal-project-setup point at its sections. That page now
// lives at /get-started and the root is the custom home page. This script, put
// inline on the home page, sends a fragment the home page does not have to the
// same section on /get-started, keeping the query.
//
// Blume uses Astro's client router, which runs an inline script only once and
// keeps its listeners across page swaps. So the listeners are registered once,
// act only while the home page is showing, and also run on `astro:page-load`
// for a client-side navigation back to the home page.

/**
 * The inline script for the home page.
 * @param {string} target  the base-aware /get-started URL
 * @param {string} home    the base-aware home page path, e.g. /microsoft-ui-reactor/latest/
 */
export function legacyFragmentScript(target, home) {
  return `(() => {
  if (globalThis.__reactorLegacyFragments) return;
  globalThis.__reactorLegacyFragments = true;
  const home = ${JSON.stringify(home.replace(/\/+$/, ""))};
  const forward = () => {
    if (location.pathname.replace(/\\/+$/, "") !== home) return;
    const id = decodeURIComponent(location.hash.slice(1));
    if (!id || document.getElementById(id)) return;
    location.replace(${JSON.stringify(target)} + location.search + location.hash);
  };
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", forward);
  else forward();
  addEventListener("hashchange", forward);
  document.addEventListener("astro:page-load", forward);
})();`;
}
