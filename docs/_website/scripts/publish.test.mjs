import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { after, describe, test } from "node:test";

import { expectedRootTarget, parseRootRedirect } from "../../../.github/scripts/verify-docs-deployment.mjs";
import {
  compareVersions,
  parseArgs,
  publish,
  rootRedirectHtml,
  rootTarget,
  updateVersions,
  validateVersion,
} from "./publish.mjs";

const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "reactor-docs-publish-test-"));
after(() => fs.rmSync(scratch, { recursive: true, force: true }));

const BASE = "/microsoft-ui-reactor";

// The root page gh-pages serves today, written by `mike set-default latest`.
const MIKE_ROOT = `<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>Redirecting</title>
  <noscript>
    <meta http-equiv="refresh" content="1; url=latest/" />
  </noscript>
  <script>
    window.location.replace(
      "latest/" + window.location.search + window.location.hash
    );
  </script>
</head>
<body>
  Redirecting to <a href="latest/">latest/</a>...
</body>
</html>
`;

const entry = (version, aliases = [], title = version) => ({ version, title, aliases });

function git(cwd, ...args) {
  const result = spawnSync("git", args, { cwd, encoding: "utf8" });
  assert.equal(result.status, 0, `git ${args.join(" ")}: ${result.stderr}`);
  return result.stdout.trim();
}

function write(root, file, text) {
  const full = path.join(root, ...file.split("/"));
  fs.mkdirSync(path.dirname(full), { recursive: true });
  fs.writeFileSync(full, text);
}

const read = (root, file) => fs.readFileSync(path.join(root, ...file.split("/")), "utf8");
const exists = (root, file) => fs.existsSync(path.join(root, ...file.split("/")));

/** A git repo shaped like gh-pages: an older main, a MkDocs release holding latest. */
function makePages(name, { seeded = true } = {}) {
  const pages = path.join(scratch, name, "pages");
  fs.mkdirSync(pages, { recursive: true });
  git(pages, "init", "--quiet");
  git(pages, "config", "user.name", "test");
  git(pages, "config", "user.email", "test@example.com");
  git(pages, "config", "commit.gpgsign", "false");
  if (seeded) {
    write(pages, "versions.json", JSON.stringify([entry("main", [], "main (development)"), entry("0.1.0-preview.18", ["latest"])]));
    write(pages, "index.html", MIKE_ROOT);
    write(pages, "main/stale.html", "old main page");
    write(pages, "0.1.0-preview.18/index.html", "mkdocs 18");
    write(pages, "latest/index.html", "mkdocs latest");
    git(pages, "add", "--all");
    git(pages, "commit", "--quiet", "--message", "seed");
  }
  return pages;
}

/** Stands in for Blume and sync.mjs, and records how publish called them. */
function fakes(name, { failOn } = {}) {
  const website = path.join(scratch, name, "website");
  const guide = path.join(scratch, name, "guide");
  fs.mkdirSync(website, { recursive: true });
  fs.mkdirSync(guide, { recursive: true });
  const builds = [];
  const syncs = [];
  return {
    website,
    guide,
    builds,
    syncs,
    sync: (options) => {
      syncs.push(options);
      return { errors: [], warnings: [] };
    },
    build: ({ website: dir, base, version, alias, versionsFile, strict }) => {
      const versions = JSON.parse(fs.readFileSync(versionsFile, "utf8"));
      builds.push({ base, version, alias, strict, versions });
      if (failOn === base) throw new Error(`build for ${base} failed`);
      const dist = path.join(dir, "dist");
      fs.rmSync(dist, { recursive: true, force: true });
      write(dist, "index.html", `built ${base} alias=${alias ?? "none"}`);
      write(dist, ".well-known/ai-catalog.json", "{}");
      return dist;
    },
    log: () => {},
  };
}

const commitCount = (pages) => Number(git(pages, "rev-list", "--count", "HEAD"));

describe("version order", () => {
  test("main first, then releases newest first", () => {
    const sorted = ["0.1.0-preview.9", "main", "0.1.0", "0.1.0-preview.18", "0.2.0", "0.1.0-rc.1", "0.1.0-preview.18.1"]
      .sort(compareVersions);
    assert.deepEqual(sorted, ["main", "0.2.0", "0.1.0", "0.1.0-rc.1", "0.1.0-preview.18.1", "0.1.0-preview.18", "0.1.0-preview.9"]);
  });

  test("only main and semantic versions are publishable", () => {
    for (const ok of ["main", "0.1.0-preview.19", "1.2.3", "1.0.0-rc-1"]) assert.equal(validateVersion(ok), ok);
    for (const bad of ["latest", "index.html", "versions.json", "../main", "main/x", "", undefined, "0.1"]) {
      assert.throws(() => validateVersion(bad), /not a version/, String(bad));
    }
    assert.throws(() => validateVersion("v0.1.0"), /Drop the leading "v": 0\.1\.0/);
  });
});

describe("versions.json", () => {
  const live = [entry("main", [], "main (development)"), entry("0.1.0-preview.18", ["latest"]), entry("0.1.0-preview.17")];

  test("--latest moves the alias to the new release", () => {
    const next = updateVersions(live, { version: "0.1.0-preview.19", latest: true });
    assert.deepEqual(next, [
      entry("main", [], "main (development)"),
      entry("0.1.0-preview.19", ["latest"]),
      entry("0.1.0-preview.18"),
      entry("0.1.0-preview.17"),
    ]);
  });

  test("republishing keeps the entry's title and aliases", () => {
    assert.deepEqual(updateVersions(live, { version: "0.1.0-preview.18" }), live);
    assert.deepEqual(updateVersions([], { version: "main" }), [entry("main", [], "main (development)")]);
  });

  test("an older release published without --latest leaves the alias alone", () => {
    const next = updateVersions(live, { version: "0.1.0-preview.12" });
    assert.deepEqual(next.at(-1), entry("0.1.0-preview.12"));
    assert.deepEqual(next.find((e) => e.version === "0.1.0-preview.18").aliases, ["latest"]);
  });

  test("the root follows latest, then main, then the newest release", () => {
    const cases = [
      [live, "latest"],
      [[entry("main"), entry("0.1.0-preview.18")], "main"],
      [[entry("0.1.0-preview.12"), entry("0.1.0-preview.14")], "0.1.0-preview.14"],
      [[], null],
    ];
    for (const [entries, expected] of cases) {
      assert.equal(rootTarget(entries), expected);
      // The deployment verifier must expect the same root, where it has an opinion.
      if (expected === "latest" || expected === "main") assert.equal(expectedRootTarget(entries), expected);
    }
  });

  test("the root page is the one mike wrote, and the verifier reads its target", () => {
    assert.equal(rootRedirectHtml("latest"), MIKE_ROOT);
    assert.equal(parseRootRedirect(rootRedirectHtml("main")), "main");
  });
});

describe("publish into a gh-pages checkout", () => {
  test("main replaces only its own folder and commits", () => {
    const pages = makePages("main");
    const f = fakes("main");
    const before = commitCount(pages);

    const result = publish({ pages, version: "main", ...f });

    assert.equal(result.committed, true);
    assert.equal(commitCount(pages), before + 1);
    assert.match(git(pages, "log", "-1", "--format=%s"), /^Deployed to main with Blume /);
    assert.equal(exists(pages, "main/stale.html"), false);
    assert.equal(read(pages, "main/index.html"), `built ${BASE}/main alias=none`);
    assert.equal(exists(pages, "main/.well-known/ai-catalog.json"), true);
    assert.equal(read(pages, "0.1.0-preview.18/index.html"), "mkdocs 18");
    assert.equal(read(pages, "latest/index.html"), "mkdocs latest");
    assert.equal(read(pages, "index.html"), MIKE_ROOT);
    assert.equal(exists(pages, ".nojekyll"), true);
    assert.deepEqual(f.builds.map((b) => [b.base, b.strict]), [[`${BASE}/main`, true]]);
    assert.deepEqual(f.syncs.map((s) => [s.ref, s.lenient, s.out, s.guide]), [["main", false, f.website, f.guide]]);
    assert.equal(git(pages, "status", "--porcelain"), "");
  });

  test("an unchanged rebuild makes no commit", () => {
    const pages = makePages("unchanged");
    const f = fakes("unchanged");
    publish({ pages, version: "main", ...f });
    const before = commitCount(pages);

    const result = publish({ pages, version: "main", ...f });

    assert.equal(result.committed, false);
    assert.equal(commitCount(pages), before);
  });

  test("a --latest release also rebuilds latest/ and moves the alias", () => {
    const pages = makePages("release");
    const f = fakes("release");

    const result = publish({ pages, version: "0.1.0-preview.19", latest: true, ...f });

    assert.equal(result.builtLatest, true);
    assert.deepEqual(f.builds.map((b) => [b.base, b.alias]), [
      [`${BASE}/0.1.0-preview.19`, undefined],
      [`${BASE}/latest`, "latest"],
    ]);
    // Both builds see the new release in the version selector.
    for (const b of f.builds) assert.equal(b.versions[1].version, "0.1.0-preview.19");
    assert.equal(f.syncs[0].ref, "v0.1.0-preview.19");
    assert.equal(read(pages, "latest/index.html"), `built ${BASE}/latest alias=latest`);
    assert.equal(read(pages, "0.1.0-preview.19/index.html"), `built ${BASE}/0.1.0-preview.19 alias=none`);
    assert.equal(read(pages, "0.1.0-preview.18/index.html"), "mkdocs 18");
    const versions = JSON.parse(read(pages, "versions.json"));
    assert.deepEqual(versions.map((e) => [e.version, e.aliases]), [
      ["main", []],
      ["0.1.0-preview.19", ["latest"]],
      ["0.1.0-preview.18", []],
    ]);
    assert.equal(parseRootRedirect(read(pages, "index.html")), "latest");
  });

  test("a failed build leaves gh-pages untouched", () => {
    const pages = makePages("failed");
    const f = fakes("failed", { failOn: `${BASE}/latest` });
    const head = git(pages, "rev-parse", "HEAD");

    assert.throws(() => publish({ pages, version: "0.1.0-preview.19", latest: true, ...f }), /latest failed/);

    assert.equal(git(pages, "status", "--porcelain"), "");
    assert.equal(git(pages, "rev-parse", "HEAD"), head);
  });

  test("--lenient builds without strict mode, for old tags", () => {
    const pages = makePages("lenient");
    const f = fakes("lenient");
    publish({ pages, version: "0.1.0-preview.12", lenient: true, ref: "v0.1.0-preview.12", ...f });
    assert.deepEqual(f.builds.map((b) => b.strict), [false]);
    assert.deepEqual(f.syncs.map((s) => [s.lenient, s.ref]), [[true, "v0.1.0-preview.12"]]);
  });

  test("the first publish to an empty branch points the root at main", () => {
    const pages = makePages("empty", { seeded: false });
    const f = fakes("empty");
    const result = publish({ pages, version: "main", ...f });
    assert.equal(result.root, "main");
    assert.equal(parseRootRedirect(read(pages, "index.html")), "main");
    assert.deepEqual(JSON.parse(read(pages, "versions.json")), [entry("main", [], "main (development)")]);
  });

  test("main cannot take the latest alias", () => {
    const pages = makePages("main-latest");
    const f = fakes("main-latest");
    assert.throws(() => publish({ pages, version: "main", latest: true, ...f }), /cannot hold it/);
    assert.equal(f.builds.length, 0);
  });
});

describe("command line", () => {
  test("parses every option", () => {
    const options = parseArgs([
      "--pages", "p", "--version", "0.1.0", "--latest", "--lenient", "--push",
      "--title", "T", "--guide", "g", "--ref", "v0.1.0", "--site-base", "/x", "--message", "m",
    ]);
    assert.equal(options.pages, path.resolve("p"));
    assert.equal(options.guide, path.resolve("g"));
    assert.deepEqual(
      [options.version, options.latest, options.lenient, options.push, options.title, options.ref, options.siteBase, options.message],
      ["0.1.0", true, true, true, "T", "v0.1.0", "/x", "m"]
    );
  });

  test("rejects missing values and unknown flags", () => {
    assert.throws(() => parseArgs(["--version", "main"]), /required/);
    assert.throws(() => parseArgs(["--pages", "--version", "main"]), /--pages needs a value/);
    assert.throws(() => parseArgs(["--pages", "p", "--version", "main", "--alias", "x"]), /Unknown argument/);
  });
});
