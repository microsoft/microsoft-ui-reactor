#!/usr/bin/env node
// Publishes one version of the docs into a checkout of the gh-pages branch.
// It replaces mike: every version lives in its own folder on gh-pages, the
// root versions.json lists them (in mike's format, so the frozen MkDocs builds
// of older releases keep finding it), and the root index.html redirects to the
// default version.
//
//   node scripts/publish.mjs --pages <gh-pages checkout> --version <main | x.y.z[-pre]>
//                            [--latest] [--title <title>] [--guide <dir>] [--ref <git ref>]
//                            [--lenient] [--site-base </path>] [--message <msg>] [--push]
//
// --latest     also publish this version as /latest/ and move the alias to it
// --guide      the docs/guide folder to build from (default: this checkout's)
// --ref        the git ref source links point at (default: main, or v<version>)
// --lenient    for rebuilding old tags: see scripts/sync.mjs, and a non-strict build
// --site-base  the path the site is served under (default: /microsoft-ui-reactor)
// --push       push the gh-pages commit to origin
//
// Blume bakes the base path into every URL, so /latest/ is a second build of
// the same content rather than a copy of the version folder. All builds finish
// before gh-pages is touched: a failed build leaves the checkout unchanged.

import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";

import { keepFragments, readRedirects } from "./redirect-pages.mjs";
import { WEBSITE_DIR, sync as syncContent } from "./sync.mjs";

export const MAIN_VERSION = "main";
export const LATEST_ALIAS = "latest";
export const MAIN_TITLE = "main (development)";
export const DEFAULT_SITE_BASE = "/microsoft-ui-reactor";
const SEMVER = /^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/;

// ---------------------------------------------------------------------------
// versions.json

/**
 * Only `main` or a semantic version. The script deletes `<pages>/<version>`
 * before writing it, so a free-form name could remove the root index.html,
 * versions.json or the `latest` copy.
 */
export function validateVersion(version) {
  if (version === MAIN_VERSION || SEMVER.test(version ?? "")) return version;
  const hint = /^v\d/.test(version ?? "") ? ` Drop the leading "v": ${version.slice(1)}.` : "";
  throw new Error(`"${version}" is not a version this script publishes. Use "main" or a semantic version like 0.1.0-preview.19.${hint}`);
}

function parseSemver(version) {
  const m = SEMVER.exec(version);
  return m && { core: [Number(m[1]), Number(m[2]), Number(m[3])], pre: m[4]?.split(".") ?? [] };
}

function comparePrerelease(a, b) {
  if (!a.length || !b.length) return b.length - a.length; // a release sorts above its prereleases
  for (let i = 0; i < Math.max(a.length, b.length); i++) {
    if (a[i] === undefined || b[i] === undefined) return a[i] === undefined ? -1 : 1;
    const an = /^\d+$/.test(a[i]);
    const bn = /^\d+$/.test(b[i]);
    if (an && bn && Number(a[i]) !== Number(b[i])) return Number(a[i]) - Number(b[i]);
    if (an !== bn) return an ? -1 : 1;
    if (a[i] !== b[i]) return a[i] < b[i] ? -1 : 1;
  }
  return 0;
}

/**
 * The order versions.json lists versions in: names that are not a version
 * (main) first, then releases newest first. Use it as a sort comparator.
 */
export function compareVersions(a, b) {
  const va = parseSemver(a);
  const vb = parseSemver(b);
  if (!va || !vb) return va ? 1 : vb ? -1 : a.localeCompare(b);
  for (let i = 0; i < 3; i++) if (va.core[i] !== vb.core[i]) return vb.core[i] - va.core[i];
  return comparePrerelease(vb.pre, va.pre);
}

/** Adds or updates one entry. With `latest`, the alias moves to it from any other entry. */
export function updateVersions(entries, { version, title, latest = false }) {
  const existing = entries.find((entry) => entry.version === version);
  const aliases = new Set(existing?.aliases ?? []);
  if (latest) aliases.add(LATEST_ALIAS);
  const entry = {
    version,
    title: title ?? existing?.title ?? (version === MAIN_VERSION ? MAIN_TITLE : version),
    aliases: [...aliases],
  };
  const others = entries
    .filter((other) => other.version !== version)
    .map((other) =>
      latest ? { ...other, aliases: (other.aliases ?? []).filter((alias) => alias !== LATEST_ALIAS) } : other
    );
  return [...others, entry].sort((a, b) => compareVersions(a.version, b.version));
}

/** What the site root redirects to: latest once a release holds it, else main, else the newest release. */
export function rootTarget(entries) {
  if (entries.some((entry) => entry.aliases?.includes(LATEST_ALIAS))) return LATEST_ALIAS;
  if (entries.some((entry) => entry.version === MAIN_VERSION)) return MAIN_VERSION;
  return [...entries].sort((a, b) => compareVersions(a.version, b.version))[0]?.version ?? null;
}

/** The root index.html, byte for byte the page `mike set-default` wrote. */
export function rootRedirectHtml(target) {
  return `<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>Redirecting</title>
  <noscript>
    <meta http-equiv="refresh" content="1; url=${target}/" />
  </noscript>
  <script>
    window.location.replace(
      "${target}/" + window.location.search + window.location.hash
    );
  </script>
</head>
<body>
  Redirecting to <a href="${target}/">${target}/</a>...
</body>
</html>
`;
}

// ---------------------------------------------------------------------------
// Building and committing

function run(command, args, options = {}) {
  const result = spawnSync(command, args, { encoding: "utf8", ...options });
  if (result.error) throw result.error;
  return result;
}

function git(cwd, ...args) {
  const result = run("git", args, { cwd });
  if (result.status !== 0) throw new Error(`git ${args.join(" ")} failed:\n${result.stderr || result.stdout}`);
  return result.stdout.trim();
}

function blumeVersion(website) {
  const manifest = path.join(website, "node_modules", "blume", "package.json");
  return fs.existsSync(manifest) ? JSON.parse(fs.readFileSync(manifest, "utf8")).version : "unknown";
}

/** Runs `blume build` for one base path and returns the folder it wrote. */
export function blumeBuild({ website, base, version, alias, versionsFile, strict }) {
  const dist = path.join(website, "dist");
  fs.rmSync(dist, { recursive: true, force: true });
  const env = { ...process.env, DOCS_BASE: base, DOCS_VERSION: version, DOCS_VERSIONS_FILE: versionsFile };
  delete env.DOCS_ALIAS;
  if (alias) env.DOCS_ALIAS = alias;
  const args = [path.join(website, "node_modules", "blume", "bin", "blume.mjs"), "build"];
  if (!strict) args.push("--no-strict");
  const result = run(process.execPath, args, { cwd: website, env, stdio: "inherit" });
  if (result.status !== 0) throw new Error(`blume build for ${base} failed with exit code ${result.status}.`);
  const { missing } = keepFragments(dist, readRedirects(website));
  if (missing.length) throw new Error(`blume build for ${base} wrote no redirect page for ${missing.join(", ")}.`);
  return dist;
}

function replaceDir(from, to) {
  fs.rmSync(to, { recursive: true, force: true });
  fs.cpSync(from, to, { recursive: true });
}

function readVersions(pages) {
  const file = path.join(pages, "versions.json");
  return fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, "utf8")) : [];
}

/**
 * Builds one version, writes it into the gh-pages checkout and commits it.
 * `build` and `syncContent` are injectable so the tests can run without Blume.
 *
 * @returns {{ committed: boolean, message: string, versions: object[], root: string, builtLatest: boolean }}
 */
export function publish({
  pages,
  version,
  title,
  latest = false,
  guide,
  ref,
  lenient = false,
  siteBase = DEFAULT_SITE_BASE,
  message,
  push = false,
  website = WEBSITE_DIR,
  build = blumeBuild,
  sync = syncContent,
  log = console.log,
}) {
  validateVersion(version);
  if (latest && version === MAIN_VERSION) throw new Error(`"${LATEST_ALIAS}" is for releases; ${MAIN_VERSION} cannot hold it.`);
  if (!pages || !fs.existsSync(pages)) throw new Error(`The gh-pages checkout ${pages} does not exist.`);
  guide = path.resolve(guide ?? path.join(website, "..", "guide"));
  if (!fs.existsSync(guide)) throw new Error(`The guide folder ${guide} does not exist.`);
  ref ??= version === MAIN_VERSION ? MAIN_VERSION : `v${version}`;
  siteBase = siteBase.replace(/\/+$/, "");

  const versions = updateVersions(readVersions(pages), { version, title, latest });
  const buildsLatest = versions.find((entry) => entry.version === version).aliases.includes(LATEST_ALIAS);

  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "reactor-docs-publish-"));
  try {
    const versionsFile = path.join(scratch, "versions.json");
    fs.writeFileSync(versionsFile, `${JSON.stringify(versions, null, 2)}\n`);

    const synced = sync({ lenient, ref, guide, out: website });
    for (const warning of synced.warnings ?? []) log(`warning: ${warning}`);
    if (synced.errors?.length) throw new Error(`Syncing ${guide} failed:\n${synced.errors.join("\n")}`);

    const builds = [{ dir: version, base: `${siteBase}/${version}` }];
    if (buildsLatest) builds.push({ dir: LATEST_ALIAS, base: `${siteBase}/${LATEST_ALIAS}`, alias: LATEST_ALIAS });
    for (const target of builds) {
      log(`Building ${target.base}/`);
      const dist = build({ website, base: target.base, version, alias: target.alias, versionsFile, strict: !lenient });
      target.output = path.join(scratch, target.dir);
      fs.cpSync(dist, target.output, { recursive: true });
    }

    for (const target of builds) replaceDir(target.output, path.join(pages, target.dir));
  } finally {
    fs.rmSync(scratch, { recursive: true, force: true });
  }

  const root = rootTarget(versions);
  fs.writeFileSync(path.join(pages, "versions.json"), `${JSON.stringify(versions, null, 2)}\n`);
  fs.writeFileSync(path.join(pages, "index.html"), rootRedirectHtml(root));
  fs.writeFileSync(path.join(pages, ".nojekyll"), "");

  let sha = "";
  try {
    sha = git(guide, "rev-parse", "HEAD").slice(0, 8);
  } catch {
    // Not a git checkout (only in tests); the message just omits the commit.
  }
  message ??= `Deployed ${sha ? `${sha} ` : ""}to ${version}${buildsLatest ? ` and ${LATEST_ALIAS}` : ""} with Blume ${blumeVersion(website)}`;

  git(pages, "add", "--all");
  // A rebuild of unchanged content can produce an identical tree; an empty
  // commit would only add noise to the branch history.
  const committed = run("git", ["diff", "--cached", "--quiet"], { cwd: pages }).status !== 0;
  if (committed) git(pages, "commit", "--quiet", "--message", message);
  log(committed ? `Committed: ${message}` : `Nothing changed for ${version}; no commit.`);
  if (push) git(pages, "push", "--quiet", "origin", "HEAD");

  return { committed, message, versions, root, builtLatest: buildsLatest };
}

// ---------------------------------------------------------------------------

export function parseArgs(argv) {
  const options = {};
  const value = (i, flag) => {
    if (i >= argv.length || argv[i].startsWith("--")) throw new Error(`${flag} needs a value.`);
    return argv[i];
  };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--latest") options.latest = true;
    else if (arg === "--lenient") options.lenient = true;
    else if (arg === "--push") options.push = true;
    else if (arg === "--pages") options.pages = path.resolve(value(++i, arg));
    else if (arg === "--version") options.version = value(++i, arg);
    else if (arg === "--title") options.title = value(++i, arg);
    else if (arg === "--guide") options.guide = path.resolve(value(++i, arg));
    else if (arg === "--ref") options.ref = value(++i, arg);
    else if (arg === "--site-base") options.siteBase = value(++i, arg);
    else if (arg === "--message") options.message = value(++i, arg);
    else throw new Error(`Unknown argument: ${arg}`);
  }
  if (!options.pages || !options.version) throw new Error("--pages and --version are required.");
  return options;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    publish(parseArgs(process.argv.slice(2)));
  } catch (err) {
    console.error(`error: ${err.message}`);
    process.exitCode = 1;
  }
}
