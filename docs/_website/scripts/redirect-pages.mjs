#!/usr/bin/env node
// Makes the redirect pages of a static Blume build keep the URL fragment.
//
// GitHub Pages has no server redirects, so Astro writes each entry of
// `redirects` as a meta-refresh page at dist/<from>/index.html. A meta refresh
// goes to a fixed URL and drops the #fragment, so a link to a section of a
// moved page (/getting-started/#manual-setup) would land at the top of the new
// page. This adds a script ahead of the refresh that carries the query and
// fragment over, as the site root's redirect and 404.html already do.
//
//   node scripts/redirect-pages.mjs [--dist <dir>]

import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

import { REDIRECTS_FILE, WEBSITE_DIR } from "./sync.mjs";

const REFRESH = /<meta http-equiv="refresh" content="\d+;\s*url=([^"]+)">/;
const MARKER = "data-keep-fragment";

/** The redirect page with the fragment-keeping script, or null when it is not a redirect page. */
export function keepFragment(html) {
  if (html.includes(MARKER)) return html;
  const m = REFRESH.exec(html);
  if (!m) return null;
  const target = m[1].replace(/&amp;/g, "&");
  const script = `<script ${MARKER}>location.replace(${JSON.stringify(target)}+location.search+location.hash)</script>`;
  return html.slice(0, m.index) + script + html.slice(m.index);
}

/**
 * Patches the page of every redirect in `redirects` under `dist`.
 * @returns {{ patched: number, missing: string[] }}
 */
export function keepFragments(dist, redirects) {
  let patched = 0;
  const missing = [];
  for (const { from } of redirects) {
    const file = path.join(dist, ...from.split("/").filter(Boolean), "index.html");
    const html = fs.existsSync(file) ? keepFragment(fs.readFileSync(file, "utf8")) : null;
    if (html === null) {
      missing.push(from);
      continue;
    }
    fs.writeFileSync(file, html);
    patched++;
  }
  return { patched, missing };
}

export function readRedirects(website = WEBSITE_DIR) {
  const file = path.join(website, REDIRECTS_FILE);
  return fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, "utf8")) : [];
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const args = process.argv.slice(2);
  const at = args.indexOf("--dist");
  const dist = path.resolve(at === -1 ? path.join(WEBSITE_DIR, "dist") : args[at + 1]);
  const { patched, missing } = keepFragments(dist, readRedirects());
  for (const from of missing) console.error(`error: no redirect page for ${from} in ${dist}.`);
  if (missing.length) process.exit(1);
  console.log(`Redirect pages keep the fragment: ${patched}.`);
}
