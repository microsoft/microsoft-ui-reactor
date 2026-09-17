// Regression tests for the redirect logic in 404.html.
//
// Run directly with `node docs/_site-root/404.redirect.test.js`, or let CI run it
// through SiteRootRedirectTests in tests/Reactor.DocPipeline.Tests.
//
// The script under test is extracted from 404.html rather than duplicated here,
// so these cases cannot pass against a copy that has drifted from the file the
// workflow actually publishes.
"use strict";

const fs = require("fs");
const path = require("path");
const vm = require("vm");

const htmlPath = path.join(__dirname, "404.html");
const html = fs.readFileSync(htmlPath, "utf8");

const match = html.match(/<script>([\s\S]*?)<\/script>/);
if (!match) {
  console.error(`FATAL: no <script> block found in ${htmlPath}`);
  process.exit(2);
}
const redirectScript = match[1];

// Minimal stand-ins for the two browser APIs the script touches. `replace` and
// the resolved link href are the observable outputs; everything else is inert.
function evaluate(href) {
  const url = new URL(href);
  const result = { replacedWith: null, linkHref: null };

  const link = {
    setAttribute(name, value) {
      if (name === "href") result.linkHref = value;
    },
  };

  const sandbox = {
    window: {
      location: {
        pathname: url.pathname,
        search: url.search,
        hash: url.hash,
        replace(to) {
          result.replacedWith = to;
        },
      },
    },
    document: {
      _handlers: [],
      addEventListener(type, handler) {
        if (type === "DOMContentLoaded") this._handlers.push(handler);
      },
      getElementById(id) {
        return id === "latest-link" ? link : null;
      },
    },
  };
  sandbox.window.window = sandbox.window;
  sandbox.window.document = sandbox.document;

  vm.createContext(sandbox);
  vm.runInContext(redirectScript, sandbox);

  // The script defers the link fix-up to DOMContentLoaded; fire it so the
  // no-redirect cases can assert what the reader actually ends up seeing.
  for (const handler of sandbox.document._handlers) handler();

  return result;
}

const PAGES = "https://microsoft.github.io";
const CUSTOM = "https://reactor.example.com";

// [description, input URL, expected redirect target (null = no redirect),
//  expected fallback-link href (null = not asserted)]
const cases = [
  ["legacy top-level page redirects into latest",
    `${PAGES}/microsoft-ui-reactor/getting-started/`,
    "/microsoft-ui-reactor/latest/getting-started/", null],

  ["legacy deep link keeps its anchor (the README link)",
    `${PAGES}/microsoft-ui-reactor/getting-started/#manual-setup`,
    "/microsoft-ui-reactor/latest/getting-started/#manual-setup", null],

  ["legacy nested page redirects",
    `${PAGES}/microsoft-ui-reactor/recipes/login/`,
    "/microsoft-ui-reactor/latest/recipes/login/", null],

  ["legacy page keeps its query string",
    `${PAGES}/microsoft-ui-reactor/controls/?q=button`,
    "/microsoft-ui-reactor/latest/controls/?q=button", null],

  ["a miss inside latest does not redirect, so it cannot loop",
    `${PAGES}/microsoft-ui-reactor/latest/nope/`,
    null, "/microsoft-ui-reactor/latest/"],

  ["a miss inside main does not gain a second version prefix",
    `${PAGES}/microsoft-ui-reactor/main/nope/`,
    null, "/microsoft-ui-reactor/latest/"],

  ["a miss inside a release version does not gain a second version prefix",
    `${PAGES}/microsoft-ui-reactor/0.1.0-preview.14/nope/`,
    null, "/microsoft-ui-reactor/latest/"],

  ["the site root itself is left alone",
    `${PAGES}/microsoft-ui-reactor/`,
    null, "/microsoft-ui-reactor/latest/"],

  ["a custom domain redirects without the project prefix",
    `${CUSTOM}/getting-started/`,
    "/latest/getting-started/", null],

  ["a custom domain miss inside latest does not loop",
    `${CUSTOM}/latest/nope/`,
    null, "/latest/"],
];

let failures = 0;

for (const [description, href, expectedRedirect, expectedLink] of cases) {
  let actual;
  try {
    actual = evaluate(href);
  } catch (error) {
    console.log(`not ok  ${description}\n        threw: ${error.message}`);
    failures++;
    continue;
  }

  const redirectOk = actual.replacedWith === expectedRedirect;
  const linkOk = expectedLink === null || actual.linkHref === expectedLink;

  if (redirectOk && linkOk) {
    console.log(`ok      ${description}`);
    continue;
  }

  failures++;
  console.log(`not ok  ${description}`);
  console.log(`        url:      ${href}`);
  if (!redirectOk) {
    console.log(`        redirect expected: ${expectedRedirect}`);
    console.log(`        redirect actual:   ${actual.replacedWith}`);
  }
  if (!linkOk) {
    console.log(`        link href expected: ${expectedLink}`);
    console.log(`        link href actual:   ${actual.linkHref}`);
  }
}

console.log(`\n${cases.length - failures}/${cases.length} passed`);
process.exit(failures === 0 ? 0 : 1);
