import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { after, describe, test } from "node:test";
import vm from "node:vm";

import { legacyFragmentScript } from "./legacy-fragments.mjs";
import { keepFragment, keepFragments } from "./redirect-pages.mjs";

const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "reactor-docs-redirects-test-"));
after(() => fs.rmSync(scratch, { recursive: true, force: true }));

// The page Astro 5 writes for a static redirect, as Blume 2.2.7 builds it.
const astroStub = (to) =>
  `<!doctype html><title>Redirecting to: ${to}</title><meta http-equiv="refresh" content="0;url=${to}">` +
  `<meta name="robots" content="noindex"><link rel="canonical" href="https://microsoft.github.io${to}">` +
  `<body>\t<a href="${to}">Redirecting from <code>/x</code> to <code>${to}</code></a></body>`;

/** Runs the page's inline script against a fake location and returns where it went. */
function navigate(html, { search = "", hash = "" }) {
  const scripts = [...html.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/g)].map((m) => m[1]);
  assert.equal(scripts.length, 1, "expected one inline script");
  let went = null;
  vm.runInNewContext(scripts[0], { location: { search, hash, replace: (url) => (went = url) } });
  return went;
}

describe("redirect pages", () => {
  test("carry the fragment and query to the new page", () => {
    const html = keepFragment(astroStub("/microsoft-ui-reactor/latest/get-started/quickstart"));
    assert.equal(
      navigate(html, { hash: "#manual-setup", search: "?q=1" }),
      "/microsoft-ui-reactor/latest/get-started/quickstart?q=1#manual-setup"
    );
  });

  test("the script runs before the meta refresh", () => {
    const html = keepFragment(astroStub("/a"));
    assert.ok(html.indexOf("<script") < html.indexOf('http-equiv="refresh"'));
  });

  test("patching twice leaves one script", () => {
    const once = keepFragment(astroStub("/a"));
    assert.equal(keepFragment(once), once);
  });

  test("a page that is not a redirect is reported", () => {
    assert.equal(keepFragment("<!doctype html><title>Hooks</title>"), null);
  });

  test("every listed redirect is patched, and a missing page is reported", () => {
    const dist = path.join(scratch, "dist");
    fs.mkdirSync(path.join(dist, "getting-started"), { recursive: true });
    fs.writeFileSync(path.join(dist, "getting-started", "index.html"), astroStub("/get-started/quickstart"));
    const result = keepFragments(dist, [
      { from: "/getting-started", to: "/get-started/quickstart" },
      { from: "/hooks", to: "/guides/hooks" },
    ]);
    assert.deepEqual(result, { patched: 1, missing: ["/hooks"] });
    const html = fs.readFileSync(path.join(dist, "getting-started", "index.html"), "utf8");
    assert.equal(navigate(html, { hash: "#one-file-no-project" }), "/get-started/quickstart#one-file-no-project");
  });
});

describe("legacy home-page fragments", () => {
  const load = ({ hash, search = "", ids = [] }) => {
    const page = { went: null, listeners: {} };
    page.location = { hash, search, replace: (url) => (page.went = url) };
    vm.runInNewContext(legacyFragmentScript("/microsoft-ui-reactor/latest/get-started"), {
      location: page.location,
      document: { readyState: "complete", getElementById: (id) => (ids.includes(id) ? {} : null) },
      addEventListener: (type, fn) => (page.listeners[type] = fn),
      decodeURIComponent,
    });
    return page;
  };
  const run = (options) => load(options).went;

  test("a section the home page lacks goes to the same section on /get-started", () => {
    assert.equal(
      run({ hash: "#minimal-project-setup", search: "?q=1" }),
      "/microsoft-ui-reactor/latest/get-started?q=1#minimal-project-setup"
    );
  });

  test("the home page's own anchors and a bare root stay put", () => {
    assert.equal(run({ hash: "#guides-title", ids: ["guides-title"] }), null);
    assert.equal(run({ hash: "" }), null);
  });

  test("a fragment set after the page loaded is forwarded too", () => {
    const page = load({ hash: "" });
    assert.equal(page.went, null);
    page.location.hash = "#why-reactor";
    page.listeners.hashchange();
    assert.equal(page.went, "/microsoft-ui-reactor/latest/get-started#why-reactor");
  });
});