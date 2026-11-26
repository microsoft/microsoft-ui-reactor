// Before the move to Blume, the guide's index page was the site root, so links
// like /latest/#minimal-project-setup point at its sections. That page now
// lives at /get-started and the root is the custom home page. This script, put
// inline on the home page, sends a fragment the home page does not have to the
// same section on /get-started, keeping the query.

/** The inline script for the home page. `target` is the base-aware /get-started URL. */
export function legacyFragmentScript(target) {
  return `(() => {
  const forward = () => {
    const id = decodeURIComponent(location.hash.slice(1));
    if (!id || document.getElementById(id)) return;
    location.replace(${JSON.stringify(target)} + location.search + location.hash);
  };
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", forward);
  else forward();
})();`;
}
