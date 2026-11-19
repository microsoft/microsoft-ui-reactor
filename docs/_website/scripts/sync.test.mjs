import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { after, describe, test } from "node:test";
import {
  REPO_URL,
  convertPage,
  headingIds,
  mkdocsRoute,
  mkdocsSlug,
  planPlacement,
  redirectsFor,
  rewriteTarget,
  routeFor,
  sync,
  titleId,
  uniqueId,
} from "./sync.mjs";

const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "reactor-docs-sync-"));
after(() => fs.rmSync(scratch, { recursive: true, force: true }));

/** A throwaway repo: docs/guide with the given pages, docs/_website with a nav.json. */
function makeRepo(name, pages, nav) {
  const root = path.join(scratch, name);
  for (const [file, text] of Object.entries(pages)) {
    const full = path.join(root, "docs", "guide", ...file.split("/"));
    fs.mkdirSync(path.dirname(full), { recursive: true });
    fs.writeFileSync(full, text);
  }
  const website = path.join(root, "docs", "_website");
  fs.mkdirSync(website, { recursive: true });
  if (nav) fs.writeFileSync(path.join(website, "nav.json"), JSON.stringify(nav));
  return { root, guide: path.join(root, "docs", "guide"), website };
}

const lines = (text) => text.split("\n");

describe("anchor ids match MkDocs", () => {
  test("slugs render inline Markdown and HTML the way Python-Markdown does", () => {
    assert.equal(mkdocsSlug("Getting `UseState` right"), "getting-usestate-right");
    // A bare <T> is an HTML tag and disappears; inside code it is text.
    assert.equal(mkdocsSlug("Optional<T>"), "optional");
    assert.equal(mkdocsSlug("`List<T>`"), "listt");
    assert.equal(mkdocsSlug("[Links](other.md) and **bold**"), "links-and-bold");
    assert.equal(mkdocsSlug("Café & más"), "cafe-mas");
  });

  test("repeated ids count up from _1", () => {
    const used = new Set();
    assert.deepEqual(["a", "a", "a", ""].map((id) => uniqueId(id, used)), ["a", "a_1", "a_2", "_1"]);
  });

  test("the H1 claims its slug first and fenced code is not a heading", () => {
    const ids = headingIds(
      lines(["# Setup", "", "````md", "## Setup", "```", "## Inside", "````", "## Setup", "#487). Not a heading"].join("\n"))
    );
    assert.deepEqual([...ids.values()].map((h) => h.id), ["setup", "setup_1"]);
  });

  test("titleId is the first H1's id", () => {
    assert.equal(titleId("Intro\n\n## Part\n\n# Use`Window`Size\n"), "usewindowsize");
    assert.equal(titleId("## Only parts\n"), undefined);
  });
});

describe("routes and redirects", () => {
  test("routes drop numeric prefixes, (group) folders and index", () => {
    assert.equal(routeFor("01-(get-started)/introduction.md"), "/introduction");
    assert.equal(routeFor("controls/index.md"), "/controls");
    assert.equal(routeFor("reference/hooks/01-(hooks)/UseState.md"), "/reference/hooks/UseState");
    assert.equal(mkdocsRoute("index.md"), "/");
    assert.equal(mkdocsRoute("controls/index.md"), "/controls");
    assert.equal(mkdocsRoute("charting.md"), "/charting");
  });

  test("a redirect is written only when the URL moved, never from the root", () => {
    const placements = new Map([
      ["index.md", { dest: "01-(get-started)/introduction.md" }],
      ["charting.md", { dest: "controls/charting.md" }],
      ["hooks.md", { dest: "02-(core)/hooks.md" }],
    ]);
    assert.deepEqual(redirectsFor(placements), [{ from: "/charting", to: "/controls/charting" }]);
  });
});

describe("placement", () => {
  const nav = {
    ignore: ["README.md"],
    groups: [{ dir: "01-(core)", title: "Core", pages: [{ file: "hooks.md", label: "Hooks" }] }],
  };

  test("strict mode rejects a page that nav.json does not place", () => {
    const { errors } = planPlacement(nav, ["hooks.md", "layout.md", "README.md"]);
    assert.equal(errors.length, 1);
    assert.match(errors[0], /layout\.md is not in docs\/_website\/nav\.json/);
  });

  test("lenient mode puts unplaced pages in a More group", () => {
    const { errors, placements, groups } = planPlacement(nav, ["hooks.md", "layout.md"], { lenient: true });
    assert.deepEqual(errors, []);
    assert.equal(placements.get("layout.md").dest, "99-(more)/layout.md");
    assert.equal(groups.at(-1).title, "More");
  });

  test("duplicates, missing pages and route collisions are errors", () => {
    const { errors } = planPlacement(
      {
        groups: [
          { dir: "01-(a)", title: "A", pages: [{ file: "x.md" }, { file: "x.md" }, { file: "gone.md" }] },
          { dir: "02-(b)", title: "B", pages: [{ file: "y.md", as: "x" }] },
        ],
      },
      ["x.md", "y.md"]
    );
    assert.equal(errors.length, 3);
    assert.match(errors.join("\n"), /places x\.md twice/);
    assert.match(errors.join("\n"), /gone\.md, which docs\/guide does not have/);
    assert.match(errors.join("\n"), /both land on \/x/);
  });

  test("a glob group takes the matching pages the earlier groups left", () => {
    const { placements } = planPlacement(
      {
        groups: [
          { dir: "01-(hooks)", title: "Hooks", glob: "reference/hooks/Use*.md" },
          { dir: "02-(types)", title: "Types", glob: "reference/hooks/*.md" },
        ],
      },
      ["reference/hooks/UseState.md", "reference/hooks/Error.md"]
    );
    assert.equal(placements.get("reference/hooks/UseState.md").dest, "01-(hooks)/UseState.md");
    assert.equal(placements.get("reference/hooks/Error.md").dest, "02-(types)/Error.md");
  });
});

describe("links", () => {
  const repo = makeRepo("links", { "hooks.md": "# Hooks\n", "a/page.md": "# Page\n" });
  fs.mkdirSync(path.join(repo.root, "samples", "Demo"), { recursive: true });
  fs.writeFileSync(path.join(repo.root, "samples", "Demo", "App.cs"), "");
  const context = () => ({
    routes: new Map([
      ["hooks.md", "/hooks"],
      ["a/page.md", "/a/page"],
    ]),
    titleIds: new Map([
      ["hooks.md", "hooks"],
      ["a/page.md", "page"],
    ]),
    page: "a/page.md",
    repoRoot: repo.root,
    ref: "v1.2.3",
    unresolved: [],
  });

  test("guide pages become routes and keep their fragments", () => {
    assert.equal(rewriteTarget("../hooks.md#usestate", "a", context()), "/hooks#usestate");
    assert.equal(rewriteTarget("../images/x/y.png", "a", context()), "/images/x/y.png");
  });

  test("a fragment naming the page's H1 is dropped: the H1 has no anchor on the site", () => {
    assert.equal(rewriteTarget("../hooks.md#hooks", "a", context()), "/hooks");
    assert.equal(rewriteTarget("#page", "a", context()), "/a/page");
    assert.equal(rewriteTarget("#other", "a", context()), "#other");
  });

  test("other repo files link to GitHub at the synced ref", () => {
    assert.equal(rewriteTarget("../../../samples/Demo/App.cs", "a", context()), `${REPO_URL}/blob/v1.2.3/samples/Demo/App.cs`);
    assert.equal(rewriteTarget("../../../samples/Demo", "a", context()), `${REPO_URL}/tree/v1.2.3/samples/Demo`);
  });

  test("a missing guide page is reported, and absolute links are left alone", () => {
    const ctx = context();
    assert.equal(rewriteTarget("missing.md", "a", ctx), "missing.md");
    assert.deepEqual(ctx.unresolved.map((m) => m.inGuide), [true]);
    for (const target of ["https://example.com/x.md", "/reference", "mailto:a@b.c"]) {
      assert.equal(rewriteTarget(target, "a", ctx), target);
    }
  });

  test("links inside code spans are not rewritten", () => {
    const page = convertPage("# Page\n\nIntro.\n\nSee [hooks](../hooks.md) and `[hooks](../hooks.md)`.\n", {
      file: "a/page.md",
      context: context(),
    });
    assert.match(page, /See \[hooks\]\(\/hooks\) and `\[hooks\]\(\.\.\/hooks\.md\)`/);
  });
});

describe("page conversion", () => {
  const context = () => ({ routes: new Map(), titleIds: new Map(), repoRoot: scratch, ref: "main", unresolved: [] });

  test("the H1 becomes the title, the opening paragraph the description, and headings keep their ids", () => {
    const page = convertPage("# Using `UseState`\n\nState that **survives** renders.\n\n## Setup\n\n## Setup ##\n", {
      file: "hooks.md",
      label: "State",
      context: context(),
    });
    assert.equal(
      page,
      [
        "---",
        'title: "Using UseState"',
        'description: "State that survives renders."',
        "sidebar:",
        '  label: "State"',
        "---",
        "",
        "## Setup [#setup]",
        "",
        "## Setup [#setup_1]",
        "",
      ].join("\n")
    );
  });

  test("an opening list or a long paragraph stays in the body", () => {
    const list = convertPage("# T\n\n- one\n- two\n", { file: "t.md", context: context() });
    assert.doesNotMatch(list, /^description:/m);
    assert.match(list, /- one\n- two/);
    const long = convertPage(`# T\n\n${"word ".repeat(100)}\n`, { file: "t.md", context: context() });
    assert.doesNotMatch(long, /^description:/m);
  });

  test("an opening paragraph with a link stays on the page and only feeds seo.description", () => {
    const linked = convertPage("# T\n\nRead [the docs](https://example.com) first.\n", { file: "t.md", context: context() });
    assert.doesNotMatch(linked, /^description:/m);
    assert.match(linked, /^seo:\n {2}description: "Read the docs first\."$/m);
    assert.match(linked, /\n\nRead \[the docs\]\(https:\/\/example\.com\) first\.\n$/);
    const codeOnly = convertPage("# T\n\nWrite `[x](y)` to link.\n", { file: "t.md", context: context() });
    assert.match(codeOnly, /^description: "Write \[x\]\(y\) to link\."$/m);
    assert.doesNotMatch(codeOnly, /^seo:/m);
  });

  test("generated API pages use the Summary section as the description", () => {
    const page = convertPage("# UseState\n\nMethod _cref_ `M:X.UseState`\n\n## Summary\n\nHolds state.\n\n## Parameters\n", {
      file: "reference/hooks/UseState.md",
      context: context(),
    });
    assert.match(page, /^description: "Holds state\."$/m);
    assert.doesNotMatch(page, /_cref_|## Summary/);
    assert.match(page, /## Parameters \[#parameters\]/);
  });
});

describe("sync", () => {
  test("writes pages, groups, images and redirects, and keeps the home page", () => {
    const repo = makeRepo(
      "ok",
      { "index.md": "# Intro\n\nHello.\n", "charting.md": "# Charts\n\nCharts.\n\nSee [intro](index.md#intro).\n", "images/a.png": "png" },
      {
        groups: [
          { dir: "01-(start)", title: "Start", pages: [{ file: "index.md", label: "Introduction", as: "introduction" }] },
          { dir: "controls", title: "Controls", pages: [{ file: "charting.md", label: "Charts" }] },
        ],
      }
    );
    fs.mkdirSync(path.join(repo.website, "docs"), { recursive: true });
    fs.writeFileSync(path.join(repo.website, "docs", "index.mdx"), "home");
    fs.writeFileSync(path.join(repo.website, "docs", "stale.md"), "old");

    const result = sync({ lenient: false, ref: "main", guide: repo.guide, out: repo.website });

    assert.deepEqual(result.errors, []);
    const docs = path.join(repo.website, "docs");
    assert.equal(fs.readFileSync(path.join(docs, "index.mdx"), "utf8"), "home");
    assert.equal(fs.existsSync(path.join(docs, "stale.md")), false);
    assert.match(fs.readFileSync(path.join(docs, "controls", "charting.md"), "utf8"), /\[intro\]\(\/introduction\)/);
    assert.match(fs.readFileSync(path.join(docs, "01-(start)", "meta.ts"), "utf8"), /pages: \["introduction"\]/);
    assert.equal(fs.readFileSync(path.join(repo.website, "public", "images", "a.png"), "utf8"), "png");
    assert.deepEqual(JSON.parse(fs.readFileSync(path.join(repo.website, "redirects.generated.json"), "utf8")), [
      { from: "/charting", to: "/controls/charting" },
    ]);
  });

  test("a broken link to a guide page fails strict mode but only warns when lenient", () => {
    const pages = { "a.md": "# A\n\n[gone](gone.md)\n" };
    const nav = { groups: [{ dir: "g", title: "G", pages: [{ file: "a.md" }] }] };
    const strict = makeRepo("strict", pages, nav);
    assert.match(sync({ lenient: false, ref: "main", guide: strict.guide, out: strict.website }).errors.join(), /gone\.md/);
    const lenient = makeRepo("lenient", pages, nav);
    const result = sync({ lenient: true, ref: "main", guide: lenient.guide, out: lenient.website });
    assert.deepEqual(result.errors, []);
    assert.match(result.warnings.join(), /gone\.md/);
  });
});
