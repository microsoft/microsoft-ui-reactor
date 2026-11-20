#!/usr/bin/env node
// Builds the Blume content tree (docs/_website/docs) from the generated guide
// (docs/guide). The guide stays the source of truth: it is compiled from
// docs/_pipeline/templates by tools/Reactor.DocPipeline and is readable on
// GitHub as plain Markdown. This script only places each page (nav.json),
// rewrites links for the site, lifts the H1 and first paragraph into front
// matter, and pins every heading to the anchor id MkDocs gave it, so links
// into the old site keep landing on the right section.
//
//   node scripts/sync.mjs [--lenient] [--ref <git ref>] [--guide <dir>] [--out <dir>]
//
// --lenient is for rebuilding old release tags: a nav.json entry whose page
// does not exist at that tag is skipped, and a page nav.json does not know
// goes under a "More" group instead of failing the sync.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
export const WEBSITE_DIR = path.resolve(here, "..");
export const REPO_URL = "https://github.com/microsoft/microsoft-ui-reactor";
export const REDIRECTS_FILE = "redirects.generated.json";
const GUIDE_PREFIX = "docs/guide";
const MORE_GROUP = { dir: "99-(more)", title: "More" };
const MAX_DESCRIPTION = 420;

// ---------------------------------------------------------------------------
// Anchor ids, exactly as MkDocs (Python-Markdown's toc extension) made them.

/** Python-Markdown's default `slugify`. */
export function slugify(text) {
  return text
    .normalize("NFKD")
    .replace(/[^\x00-\x7f]/g, "")
    .replace(/[^\w\s-]/g, "")
    .trim()
    .toLowerCase()
    .replace(/[-\s]+/g, "-");
}

/** Python-Markdown's `unique`: append `_1`, then count up, until the id is free. */
export function uniqueId(id, used) {
  while (used.has(id) || !id) {
    const m = /^(.*)_([0-9]+)$/.exec(id);
    id = m ? `${m[1]}_${Number(m[2]) + 1}` : `${id}_1`;
  }
  used.add(id);
  return id;
}

const NAMED_ENTITIES = { amp: "&", lt: "<", gt: ">", quot: '"', apos: "'", nbsp: "\u00a0" };

function unescapeHtml(text) {
  return text.replace(/&(#[xX][0-9a-fA-F]+|#[0-9]+|[a-zA-Z]+);/g, (whole, name) => {
    if (name[0] === "#") {
      const hex = name[1] === "x" || name[1] === "X";
      return String.fromCodePoint(parseInt(name.slice(hex ? 2 : 1), hex ? 16 : 10));
    }
    return NAMED_ENTITIES[name] ?? whole;
  });
}

const escapeHtml = (text) => text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

/** Python-Markdown's `strip_tags`: comments, then anything between `<` and `>`. */
function stripTags(html) {
  let text = html.replace(/<!--[\s\S]*?-->/g, "");
  let start;
  while ((start = text.indexOf("<")) !== -1) {
    const end = text.indexOf(">", start);
    if (end === -1) break;
    text = text.slice(0, start) + text.slice(end + 1);
  }
  return text.split(/\s+/).filter(Boolean).join(" ");
}

// The inline HTML Python-Markdown keeps as a tag. Any other `<` or `>` is
// escaped, so it survives `strip_tags` as text.
const INLINE_HTML = /<(\/?[a-zA-Z][^<>@ ]*( [^<>]*)?|!--[\s\S]*?--)>|[<>]/g;

/**
 * Turns inline Markdown into text. With `asHtml`, the result is the inner HTML
 * Python-Markdown would render, which is what its toc extension slugs.
 */
function inlineMarkdown(source, { asHtml }) {
  const codes = [];
  let text = source.replace(/(`+)([\s\S]*?[^`])\1(?!`)/g, (_, _ticks, body) => {
    codes.push(asHtml ? escapeHtml(body.trim()) : body.trim());
    return `\u0000${codes.length - 1}\u0000`;
  });
  text = text
    .replace(/!\[[^\]]*\]\([^)]*\)/g, asHtml ? "<img>" : "")
    .replace(/\[([^\]]*)\]\([^)]*\)/g, "$1")
    .replace(/\[([^\]]*)\]\[[^\]]*\]/g, "$1")
    .replace(/(^|[^\w])(\*\*|__|\*|_)(?=\S)(.+?)(?<=\S)\2(?!\w)/g, "$1$3")
    .replace(/\\([\\`*_{}[\]()#+\-.!<>])/g, (_, ch) => (asHtml ? escapeHtml(ch) : ch));
  if (asHtml) {
    text = text.replace(INLINE_HTML, (match) =>
      match === "<" ? "&lt;" : match === ">" ? "&gt;" : match
    );
  }
  return text.replace(/\u0000(\d+)\u0000/g, (_, i) => codes[Number(i)]);
}

/** The id MkDocs gave a heading with this Markdown text, before de-duplication. */
export function mkdocsSlug(headingMarkdown) {
  return slugify(unescapeHtml(stripTags(inlineMarkdown(headingMarkdown, { asHtml: true }))));
}

/** Inline Markdown as plain text, for front matter. */
export function plainText(markdown) {
  return inlineMarkdown(markdown, { asHtml: false }).split(/\s+/).filter(Boolean).join(" ");
}

// ---------------------------------------------------------------------------
// Line scanning

const HEADING = /^ {0,3}(#{1,6})(?:[ \t]+(.*?))?(?:[ \t]+#+)?[ \t]*$/;
const FENCE = /^ {0,3}(`{3,}|~{3,})/;

/** Marks each line as inside a fenced code block (fence lines included) or not. */
export function fenceMask(lines) {
  const mask = [];
  let open = null;
  for (const line of lines) {
    const m = FENCE.exec(line);
    if (open) {
      mask.push(true);
      if (m && m[1][0] === open[0] && m[1].length >= open.length && !line.trim().slice(m[1].length).trim()) {
        open = null;
      }
    } else if (m && !(m[1][0] === "`" && line.slice(line.indexOf(m[1]) + m[1].length).includes("`"))) {
      open = m[1];
      mask.push(true);
    } else {
      mask.push(false);
    }
  }
  return mask;
}

/**
 * The anchor id of every heading outside code, in document order, computed the
 * way MkDocs did. The H1 takes part: it claimed its own slug first.
 */
export function headingIds(lines, mask = fenceMask(lines)) {
  const used = new Set();
  const ids = new Map();
  lines.forEach((line, i) => {
    if (mask[i]) return;
    const m = HEADING.exec(line);
    if (!m) return;
    ids.set(i, { level: m[1].length, text: m[2] ?? "", id: uniqueId(mkdocsSlug(m[2] ?? ""), used) });
  });
  return ids;
}

/** The anchor id MkDocs gave a page's first H1, or undefined when it has none. */
export function titleId(source) {
  const lines = source.replace(/\r\n?/g, "\n").split("\n");
  for (const heading of headingIds(lines).values()) {
    if (heading.level === 1) return heading.id;
  }
  return undefined;
}

// ---------------------------------------------------------------------------
// Placement and routes

/** The URL Blume serves a content file at: numeric prefixes and `(group)` folders drop out. */
export function routeFor(destRel) {
  const parts = destRel
    .replace(/\.mdx?$/, "")
    .split("/")
    .map((seg) => seg.replace(/^\d+[-_.]/, ""))
    .filter((seg) => !/^\(.*\)$/.test(seg));
  if (parts.at(-1) === "index") parts.pop();
  return "/" + parts.join("/");
}

/** The URL MkDocs served a guide page at (`use_directory_urls`), without the trailing slash. */
export function mkdocsRoute(guideRel) {
  const stem = guideRel.replace(/\.md$/, "");
  const route = stem === "index" ? "" : stem.replace(/(^|\/)index$/, "");
  return "/" + route;
}

const stemOf = (file) => path.posix.basename(file).replace(/\.md$/, "");

function globToRegExp(glob) {
  const body = glob
    .split("*")
    .map((part) => part.replace(/[.+?^${}()|[\]\\]/g, "\\$&"))
    .join("[^/]*");
  return new RegExp(`^${body}$`);
}

/**
 * Decides where every guide page goes. Returns the placements (guide path to
 * destination), the groups to write a meta.ts for, and any problems.
 */
export function planPlacement(nav, guideFiles, { lenient = false } = {}) {
  const available = new Set(guideFiles);
  const placements = new Map();
  const groups = [];
  const errors = [];
  const warnings = [];

  const place = (group, file, label, as) => {
    const stem = as ?? stemOf(file);
    const dest = `${group.dir}/${stem}.md`;
    placements.set(file, { dest, label, group: group.dir });
    group.pages.push(stem);
  };

  for (const entry of nav.groups) {
    const group = { dir: entry.dir, title: entry.title, pages: [] };
    groups.push(group);
    for (const page of entry.pages ?? []) {
      if (!available.has(page.file)) {
        (lenient ? warnings : errors).push(`nav.json lists ${page.file}, which docs/guide does not have.`);
        continue;
      }
      if (placements.has(page.file)) {
        errors.push(`nav.json places ${page.file} twice.`);
        continue;
      }
      place(group, page.file, page.label, page.as);
    }
    if (entry.glob) {
      const pattern = globToRegExp(entry.glob);
      for (const file of [...available].sort()) {
        if (pattern.test(file) && !placements.has(file)) place(group, file, stemOf(file));
      }
    }
  }

  const ignored = new Set(nav.ignore ?? []);
  const unplaced = [...available].filter((file) => !placements.has(file) && !ignored.has(file)).sort();
  if (unplaced.length && lenient) {
    const more = { ...MORE_GROUP, pages: [] };
    groups.push(more);
    for (const file of unplaced) place(more, file, undefined);
  } else {
    for (const file of unplaced) {
      errors.push(`docs/guide/${file} is not in docs/_website/nav.json, so the site would not show it. Add it to a group.`);
    }
  }

  const destinations = new Map();
  for (const [file, { dest }] of placements) {
    const route = routeFor(dest);
    if (destinations.has(route)) errors.push(`${file} and ${destinations.get(route)} both land on ${route}.`);
    destinations.set(route, file);
  }

  return { placements, groups, errors, warnings };
}

/**
 * One redirect per page whose MkDocs URL differs from its new one, plus the
 * redirects nav.json lists by hand for pages that were removed or merged.
 */
export function redirectsFor(placements, extra = []) {
  const redirects = new Map();
  for (const [file, { dest }] of placements) {
    const from = mkdocsRoute(file);
    const to = routeFor(dest);
    if (from !== to && from !== "/") redirects.set(from, { from, to });
  }
  for (const { from, to } of extra) redirects.set(from, { from, to });
  return [...redirects.values()].sort((a, b) => a.from.localeCompare(b.from));
}

/** Problems with nav.json's hand-written redirects: each must leave a dead URL for a live page. */
export function checkRedirects(extra, placements) {
  const live = new Set([...placements.values()].map(({ dest }) => routeFor(dest)));
  const errors = [];
  for (const { from, to } of extra) {
    if (typeof from !== "string" || !from.startsWith("/") || typeof to !== "string" || !to.startsWith("/")) {
      errors.push(`nav.json redirect ${JSON.stringify({ from, to })} needs a "from" and a "to" that start with /.`);
    } else if (live.has(from)) {
      errors.push(`nav.json redirects ${from}, but a page still lives there.`);
    } else if (!live.has(to)) {
      errors.push(`nav.json redirects ${from} to ${to}, which no page lives at.`);
    }
  }
  return errors;
}

// ---------------------------------------------------------------------------
// Page conversion

const LINK = /(!?)\[((?:[^[\]]|\[[^[\]]*\])*)\]\(\s*(<[^>]*>|[^)\s]+)((?:\s+(?:"[^"]*"|'[^']*'|\([^)]*\)))?\s*)\)/g;
const REF_DEF = /^( {0,3}\[[^\]]+\]:[ \t]*)(<[^>]*>|\S+)(.*)$/;

/** Applies `fn` to the parts of a line outside inline code spans. */
function outsideCode(line, fn) {
  const codes = [];
  const masked = line.replace(/(`+)[\s\S]*?[^`]\1(?!`)/g, (span) => {
    codes.push(span);
    return `\u0000${codes.length - 1}\u0000`;
  });
  return fn(masked).replace(/\u0000(\d+)\u0000/g, (_, i) => codes[Number(i)]);
}

/**
 * Where a link written in a guide page should point on the site. `context`
 * carries the routes, the repo root (to tell files from folders), the git ref
 * for GitHub links, and a list to report links that resolve nowhere.
 */
export function rewriteTarget(target, pageDir, context) {
  const bracketed = target.startsWith("<") && target.endsWith(">");
  const raw = bracketed ? target.slice(1, -1) : target;
  if (/^[a-z][a-z0-9+.-]*:/i.test(raw) || raw.startsWith("/") || !raw) return target;

  const hash = raw.indexOf("#");
  const filePart = hash === -1 ? raw : raw.slice(0, hash);
  let fragment = hash === -1 ? "" : raw.slice(hash);
  if (!filePart) {
    // The H1 becomes the page title and loses its anchor; the page itself is the target.
    return fragment === `#${context.titleIds?.get(context.page)}` ? context.routes.get(context.page) : target;
  }
  let decoded;
  try {
    decoded = decodeURI(filePart);
  } catch {
    decoded = filePart;
  }
  const repoRel = path.posix.normalize(path.posix.join(GUIDE_PREFIX, pageDir, decoded)).replace(/\/$/, "");

  if (repoRel.startsWith("../")) {
    context.unresolved.push({ target, reason: "points above the repository root" });
    return target;
  }

  const inGuide = repoRel === GUIDE_PREFIX || repoRel.startsWith(`${GUIDE_PREFIX}/`);
  const guideRel = inGuide ? repoRel.slice(GUIDE_PREFIX.length + 1) : null;
  if (guideRel?.startsWith("images/")) return `/${guideRel}${fragment}`;
  if (guideRel !== null && context.routes.has(guideRel)) {
    if (fragment === `#${context.titleIds?.get(guideRel)}`) fragment = "";
    return context.routes.get(guideRel) + fragment;
  }

  const onDisk = path.join(context.repoRoot, ...repoRel.split("/"));
  let stat = null;
  try {
    stat = fs.statSync(onDisk);
  } catch {
    context.unresolved.push({ target, reason: `${repoRel} does not exist`, inGuide });
    return target;
  }
  const kind = stat.isDirectory() ? "tree" : "blob";
  const encoded = repoRel.split("/").map(encodeURIComponent).join("/");
  return `${REPO_URL}/${kind}/${context.ref}/${encoded}${fragment}`;
}

function rewriteLinks(text, pageDir, context) {
  return outsideCode(text, (prose) =>
    prose.replace(
      LINK,
      (_, bang, label, target, title) => `${bang}[${label}](${rewriteTarget(target, pageDir, context)}${title})`
    )
  );
}

function rewriteLine(line, pageDir, context) {
  const refDef = REF_DEF.exec(line);
  if (refDef) return refDef[1] + rewriteTarget(refDef[2], pageDir, context) + refDef[3];
  return rewriteLinks(line, pageDir, context);
}

const blank = (line) => !line.trim();

/**
 * Rewrites the links on every line outside fences. Prose is handled a paragraph
 * at a time, because wrapped link text (`[Rules of\nReactor](x.md)`) is still
 * one link. Headings, table rows and reference definitions stay line by line.
 */
function rewriteBody(lines, mask, headings, pageDir, context) {
  const out = lines.slice();
  const ownLine = (i) => headings.has(i) || REF_DEF.test(lines[i]) || /^\s*\|/.test(lines[i]);
  const prose = (i) => i < lines.length && !mask[i] && !blank(lines[i]) && !ownLine(i);
  for (let i = 0; i < lines.length; ) {
    if (!prose(i)) {
      if (!mask[i]) out[i] = rewriteLine(lines[i], pageDir, context);
      i++;
      continue;
    }
    let end = i;
    while (prose(end)) end++;
    const rewritten = rewriteLinks(lines.slice(i, end).join("\n"), pageDir, context).split("\n");
    for (let k = 0; k < rewritten.length; k++) out[i + k] = rewritten[k];
    i = end;
  }
  return out;
}

/** The [start, end) line range of the paragraph that begins at or after `from`. */
function paragraphAt(lines, from) {
  let start = from;
  while (start < lines.length && blank(lines[start])) start++;
  let end = start;
  while (end < lines.length && !blank(lines[end])) end++;
  return [start, end];
}

const NOT_PROSE = /^\s*([|#>!<\-*+]|\d+[.)]|```|~~~|\[[^\]]+\]:)/;

// Blume shows `description` under the title as plain text, so only a paragraph
// without links can move there. One with links stays on the page and lends its
// text to `seo.description`, which feeds the <head>, social cards and llms.txt.
const hasLink = (paragraph) => /\]\(|\]\[|<https?:/.test(paragraph.replace(/(`+)[\s\S]*?[^`]\1(?!`)/g, ""));

/**
 * Converts one guide page: the H1 becomes the title, the opening paragraph the
 * description, headings get their MkDocs ids pinned, and links are rewritten.
 */
export function convertPage(source, { file, label, context }) {
  const lines = source.replace(/\r\n?/g, "\n").split("\n");
  const mask = fenceMask(lines);
  const ids = headingIds(lines, mask);
  const pageDir = path.posix.dirname(file) === "." ? "" : path.posix.dirname(file);
  const rewritten = rewriteBody(lines, mask, ids, pageDir, context);

  let title = null;
  let titleAt = null;
  const body = [];
  lines.forEach((line, i) => {
    if (mask[i]) {
      body.push(line);
      return;
    }
    const heading = ids.get(i);
    if (heading && heading.level === 1 && title === null) {
      title = plainText(heading.text);
      titleAt = body.length;
      return;
    }
    let out = rewritten[i];
    if (heading && heading.level > 1) out = `${out.replace(/[ \t]+#+[ \t]*$/, "").trimEnd()} [#${heading.id}]`;
    body.push(out);
  });

  let description = null;
  let seoDescription = null;
  // Lifts lines [start, end) of `body` into the description when they carry no
  // links; otherwise leaves them on the page. `drop` widens what gets removed.
  const lift = (start, end, drop = [start, end]) => {
    const paragraph = body.slice(start, end).join(" ");
    const text = plainText(paragraph) || null;
    if (!text) return;
    if (hasLink(paragraph)) {
      seoDescription = text;
      return;
    }
    description = text;
    body.splice(drop[0], drop[1] - drop[0]);
  };
  if (titleAt !== null) {
    const [start, end] = paragraphAt(body, titleAt);
    const paragraph = body.slice(start, end);
    if (paragraph.some((line) => line.includes("_cref_"))) {
      // Generated API pages open with the member kind and its cref; the summary
      // section underneath is the readable description.
      body.splice(start, end - start);
      const summary = body.findIndex((line) => /^## Summary(\s|$)/.test(line));
      if (summary !== -1) {
        const [s, e] = paragraphAt(body, summary + 1);
        lift(s, e, [summary, e]);
      }
    } else if (paragraph.length && !NOT_PROSE.test(paragraph[0]) && plainText(paragraph.join(" ")).length <= MAX_DESCRIPTION) {
      lift(start, end);
    }
  }

  const frontMatter = ["---", `title: ${JSON.stringify(title ?? label ?? stemOf(file))}`];
  if (description) frontMatter.push(`description: ${JSON.stringify(description)}`);
  if (seoDescription) frontMatter.push("seo:", `  description: ${JSON.stringify(seoDescription)}`);
  if (label && label !== title) frontMatter.push("sidebar:", `  label: ${JSON.stringify(label)}`);
  frontMatter.push("---");

  const text = body.join("\n").replace(/^\n+|\n+$/g, "");
  return `${frontMatter.join("\n")}\n\n${text}\n`;
}

export function metaFile(group) {
  const lines = ['import { defineMeta } from "blume";', "", "export default defineMeta({"];
  lines.push(`  title: ${JSON.stringify(group.title)},`);
  if (group.pages.length) lines.push(`  pages: ${JSON.stringify(group.pages)},`);
  lines.push("});", "");
  return lines.join("\n");
}

// ---------------------------------------------------------------------------
// Driver

function listMarkdown(root, dir = "") {
  const files = [];
  for (const entry of fs.readdirSync(path.join(root, dir), { withFileTypes: true })) {
    const rel = dir ? `${dir}/${entry.name}` : entry.name;
    if (entry.isDirectory()) files.push(...listMarkdown(root, rel));
    else if (entry.name.endsWith(".md")) files.push(rel);
  }
  return files;
}

function parseArgs(argv) {
  const options = { lenient: false, ref: "main", guide: null, out: WEBSITE_DIR };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--lenient") options.lenient = true;
    else if (arg === "--ref") options.ref = argv[++i];
    else if (arg === "--guide") options.guide = path.resolve(argv[++i]);
    else if (arg === "--out") options.out = path.resolve(argv[++i]);
    else throw new Error(`Unknown argument: ${arg}`);
  }
  options.guide ??= path.resolve(options.out, "..", "guide");
  return options;
}

export function sync({ lenient, ref, guide, out }) {
  const repoRoot = path.resolve(guide, "..", "..");
  const nav = JSON.parse(fs.readFileSync(path.join(out, "nav.json"), "utf8"));
  const files = listMarkdown(guide).filter((file) => !file.startsWith("images/"));
  const { placements, groups, errors, warnings } = planPlacement(nav, files, { lenient });
  const extraRedirects = nav.redirects ?? [];
  errors.push(...checkRedirects(extraRedirects, placements));
  if (errors.length) return { errors, warnings };

  const routes = new Map([...placements].map(([file, { dest }]) => [file, routeFor(dest)]));
  const sources = new Map([...placements.keys()].map((file) => [file, fs.readFileSync(path.join(guide, file), "utf8")]));
  const titleIds = new Map([...sources].map(([file, source]) => [file, titleId(source)]));
  const context = { routes, titleIds, repoRoot, ref, unresolved: [] };

  const docs = path.join(out, "docs");
  fs.rmSync(docs, { recursive: true, force: true });
  fs.mkdirSync(docs, { recursive: true });

  for (const [file, { dest, label }] of placements) {
    context.page = file;
    const page = convertPage(sources.get(file), { file, label, context });
    const target = path.join(docs, ...dest.split("/"));
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, page);
    for (const miss of context.unresolved.splice(0)) {
      const message = `docs/guide/${file}: link ${miss.target} ${miss.reason}.`;
      (miss.inGuide && !lenient ? errors : warnings).push(message);
    }
  }

  for (const group of groups) {
    const dir = path.join(docs, ...group.dir.split("/"));
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, "meta.ts"), metaFile(group));
  }

  const images = path.join(out, "public", "images");
  fs.rmSync(images, { recursive: true, force: true });
  if (fs.existsSync(path.join(guide, "images"))) fs.cpSync(path.join(guide, "images"), images, { recursive: true });

  const redirects = redirectsFor(placements, extraRedirects);
  fs.writeFileSync(path.join(out, REDIRECTS_FILE), `${JSON.stringify(redirects, null, 2)}\n`);

  return { errors, warnings, pages: placements.size, groups: groups.length, redirects: redirects.length };
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const result = sync(parseArgs(process.argv.slice(2)));
  for (const warning of result.warnings) console.warn(`warning: ${warning}`);
  for (const error of result.errors) console.error(`error: ${error}`);
  if (result.errors.length) process.exit(1);
  console.log(`Synced ${result.pages} pages in ${result.groups} groups, ${result.redirects} redirects.`);
}
