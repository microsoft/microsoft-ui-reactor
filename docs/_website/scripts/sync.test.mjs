import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { after, describe, test } from "node:test";
import {
  REDIRECTS_FILE,
  REPO_URL,
  changelogEntry,
  changelogSlug,
  changelogSummary,
  convertPage,
  headingIds,
  linkIssues,
  mkdocsRoute,
  mkdocsSlug,
  parseChangelog,
  planPlacement,
  redirectsFor,
  checkRedirects,
  rewriteTarget,
  routeFor,
  sync,
  titleId,
  uniqueId,
  usesComponents,
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

  test("hand-written redirects join the generated ones and win on the same URL", () => {
    const placements = new Map([
      ["charting.md", { dest: "controls/charting.md" }],
      ["xaml-developers.md", { dest: "get-started/xaml-developers.md" }],
    ]);
    const extra = [
      { from: "/reactor-vs-xaml", to: "/get-started/xaml-developers" },
      { from: "/charting", to: "/get-started/xaml-developers" },
    ];
    assert.deepEqual(redirectsFor(placements, extra), [
      { from: "/charting", to: "/get-started/xaml-developers" },
      { from: "/reactor-vs-xaml", to: "/get-started/xaml-developers" },
      { from: "/xaml-developers", to: "/get-started/xaml-developers" },
    ]);
  });

  test("a hand-written redirect must leave a dead URL for a live page", () => {
    const placements = new Map([["charting.md", { dest: "controls/charting.md" }]]);
    assert.deepEqual(checkRedirects([{ from: "/old", to: "/controls/charting" }], placements), []);
    assert.match(checkRedirects([{ from: "/controls/charting", to: "/controls/charting" }], placements)[0], /still lives there/);
    assert.match(checkRedirects([{ from: "/old", to: "/nowhere" }], placements)[0], /no page lives at/);
    assert.match(checkRedirects([{ from: "old", to: "/controls/charting" }], placements)[0], /start with \//);
  });

  test("a redirect may point at a heading on a live page", () => {
    const placements = new Map([["charting.md", { dest: "controls/charting.md" }]]);
    const idsAt = () => new Set(["axes"]);
    assert.deepEqual(checkRedirects([{ from: "/old", to: "/controls/charting#axes" }], placements, idsAt), []);
    assert.deepEqual(checkRedirects([{ from: "/old", to: "/controls/charting?tab=1#axes" }], placements, idsAt), []);
    assert.match(checkRedirects([{ from: "/old", to: "/controls/charting#legend" }], placements, idsAt)[0], /no #legend heading/);
    assert.match(checkRedirects([{ from: "/old", to: "/nowhere#axes" }], placements, idsAt)[0], /no page lives at/);
  });
});

describe("changelog", () => {
  const source = [
    "# Changelog",
    "",
    "## [Unreleased]",
    "",
    "### Added",
    "",
    "- Not out yet.",
    "",
    "## [0.2.0] — 2026-12-01",
    "",
    "### Added",
    "",
    "- `UseThing` (issue #1482, [#77](https://example.com/77), `#888`).",
    "- A wrapped [link to",
    "  #99](https://example.com/99) and the [layout guide](docs/guide/layout.md#sizing).",
    "",
    "### Changed",
    "",
    "### Fixed",
    "",
    "- Something, see [TESTING.md](TESTING.md).",
    "",
    "## [0.2.0-preview.1] - 2026-11-01",
    "",
    "### Security",
    "",
    "- Patched.",
    "",
  ].join("\n");

  test("keeps dated releases, drops Unreleased and empty buckets", () => {
    const releases = parseChangelog(source);
    assert.deepEqual(
      releases.map((r) => [r.version, r.date, r.buckets.map((b) => b.title)]),
      [
        ["0.2.0", "2026-12-01", ["Added", "Fixed"]],
        ["0.2.0-preview.1", "2026-11-01", ["Security"]],
      ]
    );
    assert.equal(changelogSlug("0.2.0-preview.1"), "v0-2-0-preview-1");
  });

  test("links bare issue refs, but not inside code or existing links", () => {
    assert.equal(
      linkIssues("issue #12, [#77](x), [a\n#99](y), `#888`, a#55, &#123;"),
      `issue [#12](${REPO_URL}/issues/12), [#77](x), [a\n#99](y), \`#888\`, a#55, &#123;`
    );
  });

  test("an entry lifts buckets to h2, rewrites links from the repo root, and sums up the bullets", () => {
    const routes = new Map([["layout.md", "/guides/layout"]]);
    const context = { routes, titleIds: new Map(), repoRoot: scratch, ref: "v0.2.0", unresolved: [] };
    fs.writeFileSync(path.join(scratch, "TESTING.md"), "x");
    const [release, preview] = parseChangelog(source);
    const page = changelogEntry(release, context);
    assert.deepEqual(context.unresolved, []);
    assert.match(page, /^---\ntitle: "0\.2\.0"\ntype: changelog\ndate: 2026-12-01\nchangelog:\n {2}version: "0\.2\.0"\ndescription: "2 added and 1 fixed\."\n---\n/);
    assert.match(page, /\n## Added\n/);
    assert.match(page, /\[layout guide\]\(\/guides\/layout#sizing\)/);
    assert.match(page, new RegExp(`\\[TESTING\\.md\\]\\(${REPO_URL}/blob/v0\\.2\\.0/TESTING\\.md\\)`));
    assert.match(page, /\(issue \[#1482\]/);
    const previewPage = changelogEntry(preview, context);
    assert.match(previewPage, /description: "1 security fix\."/);
  });

  test("a bucket is known by its first word", () => {
    const bucket = (title, n) => ({ title, lines: Array.from({ length: n }, (_, i) => `- ${i}`) });
    assert.equal(changelogSummary([bucket("Changed (breaking)", 2), bucket("Fixed", 1)]), "2 changed and 1 fixed.");
    assert.equal(changelogSummary([bucket("Notes", 3)]), null);
  });

  test("reports a ## heading that is not a release, instead of dropping it silently", () => {
    const problems = [];
    const releases = parseChangelog("## [Unreleased]\n\n## [1.0.0] 2026-01-02\n\n- Lost.\n\n## [0.9.0] - 2026-01-01\n", problems);
    assert.deepEqual(releases.map((r) => r.version), ["0.9.0"]);
    assert.equal(problems.length, 1);
    assert.match(problems[0], /^line 3: "## \[1\.0\.0\] 2026-01-02" is not a release heading/);
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

  test("link text that wraps onto the next line is still rewritten", () => {
    const page = convertPage("# Page\n\nIntro.\n\nRead the [Hooks\nguide](../hooks.md) first, then `[a\nb](../hooks.md)`.\n", {
      file: "a/page.md",
      context: context(),
    });
    assert.match(page, /Read the \[Hooks\nguide\]\(\/hooks\) first, then `\[a\nb\]\(\.\.\/hooks\.md\)`\./);
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

describe("components", () => {
  const context = () => ({ routes: new Map(), titleIds: new Map(), repoRoot: scratch, ref: "main", unresolved: [] });
  const convert = (lines) => convertPage(["# T", "", ...lines, ""].join("\n"), { file: "t.md", context: context() });
  const body = (page) => page.replace(/^---[\s\S]*?---\n\n/, "");

  test("a page without markers is left as Markdown", () => {
    assert.equal(usesComponents("# T\n\n```md\n<!-- tabs -->\n```\n"), false);
    assert.equal(usesComponents("# T\n\n  <!-- steps -->\n"), true);
    assert.match(convert(["- Use {x} here."]), /- Use \{x\} here\./);
  });

  test("each heading at the first tab level becomes a tab that keeps its MkDocs id", () => {
    const page = body(
      convert(["<!-- tabs -->", "", "### Copilot `CLI`", "", "Run it.", "", "#### Detail", "", "### Claude", "", "Also.", "", "<!-- /tabs -->"])
    );
    assert.equal(
      page,
      [
        "<Tabs>",
        "",
        '<Tab title={"Copilot CLI"} id={"copilot-cli"}>',
        "",
        "Run it.",
        "",
        "#### Detail [#detail]",
        "",
        "</Tab>",
        "",
        '<Tab title={"Claude"} id={"claude"}>',
        "",
        "Also.",
        "",
        "</Tab>",
        "",
        "</Tabs>",
        "",
      ].join("\n")
    );
  });

  test("an ordered list becomes steps, with bold lead-ins as titles and the body dedented", () => {
    const page = body(
      convert([
        "<!-- steps -->",
        "",
        "1. **Install winapp.**",
        "",
        "   ```powershell",
        "   winget install {x}",
        "   ```",
        "",
        "2. **Create it:** with `new`",
        "and a lazy line.",
        "3. Run it.",
        "",
        "<!-- /steps -->",
      ])
    );
    assert.equal(
      page,
      [
        "<Steps>",
        "",
        '<Step title={"Install winapp"}>',
        "",
        "```powershell",
        "winget install {x}",
        "```",
        "",
        "</Step>",
        "",
        '<Step title={"Create it"}>',
        "",
        "with `new`",
        "and a lazy line.",
        "",
        "</Step>",
        "",
        "<Step>",
        "",
        "Run it.",
        "",
        "</Step>",
        "",
        "</Steps>",
        "",
      ].join("\n")
    );
  });

  test("a prompt copies the fenced text as paragraphs and shows it as the description", () => {
    const page = body(convert(["<!-- prompt -->", "", "```text", "Build a counter", "with {state}.", "", "Then run it.", "```"]));
    assert.equal(
      page,
      [
        '<Prompt description={"Build a counter with {state}. Then run it."}>',
        "",
        "Build a counter",
        "with \\{state\\}.",
        "",
        "Then run it.",
        "",
        "</Prompt>",
        "",
      ].join("\n")
    );
  });

  test("steps and prompts nest inside a tab", () => {
    const page = convert([
      "<!-- tabs -->",
      "### A",
      "<!-- steps -->",
      "1. One.",
      "",
      "   <!-- prompt -->",
      "   ```text",
      "   Do it.",
      "   ```",
      "<!-- /steps -->",
      "<!-- /tabs -->",
    ]);
    assert.match(page, /<Tab title=\{"A"\} id=\{"a"\}>\n\n<Steps>\n\n<Step>\n\nOne\.\n\n<Prompt description=\{"Do it\."\}>\n\nDo it\.\n\n<\/Prompt>\n\n<\/Step>\n\n<\/Steps>\n\n<\/Tab>\n\n<\/Tabs>/);
  });

  test("prose that MDX would read as code is escaped, and code is not", () => {
    const page = body(
      convert(["<!-- steps -->", "1. Go.", "<!-- /steps -->", "", "Pass <name> to {x}, see <https://a.b/c>, then<br>stop `<T>{}`.", "<!-- note", "hidden -->", "<kbd>F5</kbd>"])
    );
    assert.match(page, /Pass \\<name> to \\\{x\\\}, see \[https:\/\/a\.b\/c\]\(https:\/\/a\.b\/c\), then<br \/>stop `<T>\{\}`\.\n<kbd>F5<\/kbd>/);
    assert.doesNotMatch(page, /hidden/);
  });

  test("authoring mistakes name the file and line", () => {
    assert.throws(() => convert(["<!-- tabs -->", "Intro.", "### A", "<!-- /tabs -->"]), /t\.md:4: a tabs block must start with the heading/);
    assert.throws(() => convert(["<!-- tabs -->", "### A", "## Out", "<!-- /tabs -->"]), /t\.md:5: a heading above the tab level/);
    assert.throws(() => convert(["<!-- steps -->", "1. A.", "", "Loose.", "<!-- /steps -->"]), /t\.md:6: only an ordered list/);
    assert.throws(() => convert(["<!-- steps -->", "1. A."]), /t\.md:3: <!-- steps --> is never closed/);
    assert.throws(() => convert(["<!-- prompt -->", "", "Not a fence."]), /t\.md:3: <!-- prompt --> must come right before a fenced block/);
    assert.throws(() => convert(["<!-- /tabs -->"]), /t\.md:3: <!-- \/tabs --> closes nothing/);
  });
});

describe("sync", () => {
  test("writes pages, groups, images and redirects, and clears everything else", () => {
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
    assert.equal(fs.existsSync(path.join(docs, "index.mdx")), false);
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

  test("lenient mode skips a hand-written redirect an old tag predates", () => {
    const nav = {
      groups: [{ dir: "g", title: "G", pages: [{ file: "a.md" }, { file: "old.md" }] }],
      redirects: [{ from: "/g/old", to: "/g/a" }],
    };
    const pages = { "a.md": "# A\n", "old.md": "# Old\n" };
    const strict = makeRepo("redirect-strict", pages, nav);
    assert.match(sync({ lenient: false, ref: "main", guide: strict.guide, out: strict.website }).errors.join(), /still lives there/);
    const lenient = makeRepo("redirect-lenient", pages, nav);
    const result = sync({ lenient: true, ref: "v1", guide: lenient.guide, out: lenient.website });
    assert.deepEqual(result.errors, []);
    assert.match(result.warnings.join(), /still lives there\. Skipped\./);
    const redirects = JSON.parse(fs.readFileSync(path.join(lenient.website, REDIRECTS_FILE), "utf8"));
    assert.equal(redirects.some((r) => r.from === "/g/old"), false);
  });

  test("writes a page that uses components as .mdx and reports marker mistakes as errors", () => {
    const repo = makeRepo(
      "components",
      { "a.md": "# A\n\n<!-- steps -->\n1. Go.\n<!-- /steps -->\n", "b.md": "# B\n", "c.md": "# C\n\n<!-- tabs -->\n" },
      { groups: [{ dir: "g", title: "G", pages: [{ file: "a.md" }, { file: "b.md" }, { file: "c.md" }] }] }
    );
    const result = sync({ lenient: false, ref: "main", guide: repo.guide, out: repo.website });
    assert.match(result.errors.join("\n"), /docs\/guide\/c\.md:3: <!-- tabs --> is never closed/);
    const dir = path.join(repo.website, "docs", "g");
    assert.deepEqual(fs.readdirSync(dir).sort(), ["a.mdx", "b.md", "meta.ts"]);
  });

  test("writes one changelog entry per release from the repo's CHANGELOG.md", () => {
    const repo = makeRepo("changelog", { "a.md": "# A\n\nA page.\n" }, { groups: [{ dir: "g", title: "G", pages: [{ file: "a.md" }] }] });
    fs.writeFileSync(path.join(repo.root, "CHANGELOG.md"), "## [1.0.0] — 2026-01-02\n\n### Added\n\n- [A](docs/guide/a.md).\n");
    const result = sync({ lenient: false, ref: "main", guide: repo.guide, out: repo.website });
    assert.deepEqual(result.errors, []);
    assert.equal(result.releases, 1);
    const entry = fs.readFileSync(path.join(repo.website, "docs", "changelog", "v1-0-0.md"), "utf8");
    assert.match(entry, /type: changelog/);
    assert.match(entry, /\[A\]\(\/g\/a\)/);
  });

  test("a changelog link to a missing guide page or a bad release heading fails strict mode but only warns when lenient", () => {
    const nav = { groups: [{ dir: "g", title: "G", pages: [{ file: "a.md" }] }] };
    const changelog = "## [1.0.0] — 2026-01-02\n\n### Added\n\n- [Gone](docs/guide/gone.md).\n\n## [0.9.0]\n";
    const strict = makeRepo("changelog-strict", { "a.md": "# A\n" }, nav);
    fs.writeFileSync(path.join(strict.root, "CHANGELOG.md"), changelog);
    const errors = sync({ lenient: false, ref: "main", guide: strict.guide, out: strict.website }).errors.join("\n");
    assert.match(errors, /CHANGELOG\.md \[1\.0\.0\]: link docs\/guide\/gone\.md/);
    assert.match(errors, /CHANGELOG\.md line 7: "## \[0\.9\.0\]" is not a release heading/);
    const lenient = makeRepo("changelog-lenient", { "a.md": "# A\n" }, nav);
    fs.writeFileSync(path.join(lenient.root, "CHANGELOG.md"), changelog);
    const result = sync({ lenient: true, ref: "v1", guide: lenient.guide, out: lenient.website });
    assert.deepEqual(result.errors, []);
    const warnings = result.warnings.join("\n");
    assert.match(warnings, /CHANGELOG\.md \[1\.0\.0\]: link docs\/guide\/gone\.md/);
    assert.match(warnings, /CHANGELOG\.md line 7: "## \[0\.9\.0\]" is not a release heading/);
  });

  test("a changelog with no dated release writes no changelog folder", () => {
    const repo = makeRepo("changelog-none", { "a.md": "# A\n\nA page.\n" }, { groups: [{ dir: "g", title: "G", pages: [{ file: "a.md" }] }] });
    fs.writeFileSync(path.join(repo.root, "CHANGELOG.md"), "## [Unreleased]\n\n### Added\n\n- Soon.\n");
    const result = sync({ lenient: false, ref: "main", guide: repo.guide, out: repo.website });
    assert.deepEqual(result.errors, []);
    assert.equal(result.releases, 0);
    assert.equal(fs.existsSync(path.join(repo.website, "docs", "changelog")), false);
  });

  test("a page without a usable summary fails strict mode but only warns when lenient", () => {
    const pages = {
      "linked.md": "# Linked\n\nRead [hooks](ok.md) first.\n",
      "long.md": `# Long\n\n${"word ".repeat(100)}\n`,
      "ok.md": "# Ok\n\nA plain summary.\n",
      "reference/x.md": "# X\n\n| table |\n",
    };
    const nav = { groups: [{ dir: "g", title: "G", pages: [{ file: "linked.md" }, { file: "long.md" }, { file: "ok.md" }] }, { dir: "reference", title: "R", glob: "reference/*.md" }] };
    const strict = makeRepo("summary-strict", pages, nav);
    const errors = sync({ lenient: false, ref: "main", guide: strict.guide, out: strict.website }).errors;
    assert.deepEqual(errors.map((e) => e.split(":")[0]), ["docs/guide/linked.md", "docs/guide/long.md"]);
    const lenient = makeRepo("summary-lenient", pages, nav);
    const result = sync({ lenient: true, ref: "main", guide: lenient.guide, out: lenient.website });
    assert.deepEqual(result.errors, []);
    assert.equal(result.warnings.filter((w) => /page summary/.test(w)).length, 2);
  });
});
