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

// ---------------------------------------------------------------------------
// Components. A guide page asks for Blume's Tabs, Steps and Prompt with HTML
// comments, so it still reads well on GitHub:
//
//   <!-- tabs -->      each heading at the block's first heading level is a tab
//   <!-- /tabs -->
//   <!-- steps -->     an ordered list; `1. **Title.** Text` gives a step title
//   <!-- /steps -->
//   <!-- prompt -->    the fenced block after it becomes a prompt with a copy button
//
// Blume runs components only in `.mdx` pages, so a page that uses one is
// written as `.mdx` and made safe for MDX's stricter parser.

const MARKER = /^[ \t]*<!--[ \t]*(\/?)(tabs|steps|prompt)[ \t]*-->[ \t]*$/;
const ITEM = /^(\d{1,9}[.)])( {1,4})(?! )(.*)$/;
const STEP_TITLE = /^\*\*(.+?)\*\*[.:]?(?:[ \t]+(.*))?$/;
const COMPONENT_TAG = /^<\/?(Tabs|Tab|Steps|Step|Prompt)\b/;

/** True when the page asks for a component, so it must be written as `.mdx`. */
export function usesComponents(source) {
  const lines = source.replace(/\r\n?/g, "\n").split("\n");
  const mask = fenceMask(lines);
  return lines.some((line, i) => !mask[i] && MARKER.test(line));
}

const attr = (value) => `{${JSON.stringify(value)}}`;
const indentOf = (line) => /^ */.exec(line)[0].length;
const dedent = (line, width) => line.slice(Math.min(width, indentOf(line)));

/**
 * Turns the component markers into Blume components. `out` holds each source
 * line's converted text, indexed like `lines`; entries are replaced in place,
 * and may become several lines or null (dropped).
 */
function expandComponents(lines, mask, ids, out, file) {
  const fail = (i, message) => {
    throw new Error(`docs/guide/${file}:${i + 1}: ${message}`);
  };
  const stack = [];
  for (let i = 0; i < lines.length; i++) {
    const top = stack.at(-1);
    const marker = mask[i] ? null : MARKER.exec(lines[i]);
    if (marker) {
      const [, closing, kind] = marker;
      if (closing) {
        if (kind === "prompt" || top?.kind !== kind) fail(i, `<!-- /${kind} --> closes nothing.`);
        if (!top.open) fail(i, `the ${kind} block is empty.`);
        const child = kind === "tabs" ? "Tab" : "Step";
        out[i] = `\n</${child}>\n\n</${kind === "tabs" ? "Tabs" : "Steps"}>\n`;
        stack.pop();
      } else if (kind === "prompt") {
        i = expandPrompt(lines, mask, out, i, fail);
      } else {
        if (top?.kind === "steps") fail(i, `a ${kind} block can't sit inside a step.`);
        stack.push({ kind, line: i, open: false, level: null, width: 0 });
        out[i] = kind === "tabs" ? "\n<Tabs>\n" : "\n<Steps>\n";
      }
      continue;
    }

    if (top?.kind === "tabs") {
      const heading = mask[i] ? null : ids.get(i);
      if (heading && (top.level === null || heading.level <= top.level)) {
        if (top.level !== null && heading.level < top.level) fail(i, "a heading above the tab level ends the tabs too early.");
        top.level = heading.level;
        const open = `<Tab title=${attr(plainText(heading.text))} id=${attr(heading.id)}>\n`;
        out[i] = (top.open ? "\n</Tab>\n\n" : "\n") + open;
        top.open = true;
      } else if (!top.open && !blank(lines[i])) {
        fail(i, "a tabs block must start with the heading of its first tab.");
      }
    } else if (top?.kind === "steps") {
      const item = mask[i] ? null : ITEM.exec(lines[i]);
      if (item) {
        top.width = item[1].length + item[2].length;
        const text = out[i].slice(top.width);
        const titled = STEP_TITLE.exec(text);
        const title = titled ? plainText(titled[1]).replace(/[.:]$/, "") : null;
        const rest = titled ? titled[2] ?? "" : text;
        const open = [title ? `<Step title=${attr(title)}>` : "<Step>", ""];
        if (rest) open.push(rest);
        out[i] = (top.open ? "\n</Step>\n\n" : "\n") + open.join("\n");
        top.open = true;
      } else if (blank(lines[i])) {
        out[i] = "";
      } else if (!top.open) {
        fail(i, "a steps block must start with an ordered list.");
      } else if (mask[i] || indentOf(lines[i]) >= top.width || !blank(lines[i - 1])) {
        // Indented continuation, code, or a lazy continuation line of the paragraph above.
        out[i] = dedent(out[i], top.width);
      } else {
        fail(i, "only an ordered list can sit inside a steps block.");
      }
    }
  }
  if (stack.length) fail(stack.at(-1).line, `<!-- ${stack.at(-1).kind} --> is never closed.`);
}

/** Replaces `<!-- prompt -->` and the fence after it; returns the fence's last line. */
function expandPrompt(lines, mask, out, at, fail) {
  let start = at + 1;
  while (start < lines.length && blank(lines[start])) start++;
  const fence = FENCE.exec(lines[start] ?? "");
  if (!fence || !mask[start]) fail(at, "<!-- prompt --> must come right before a fenced block.");
  const closing = new RegExp(`^ {0,3}\\${fence[1][0]}{${fence[1].length},}[ \\t]*$`);
  let end = start + 1;
  while (end < lines.length && mask[end] && !closing.test(lines[end])) end++;
  if (end >= lines.length || !mask[end]) fail(at, "the prompt's fenced block is never closed.");

  // The fence's text, as paragraphs: the copy button reads the rendered body back as Markdown.
  const indent = indentOf(lines[start]);
  const text = lines.slice(start + 1, end).map((line) => dedent(line, indent).trimEnd());
  const description = text.join(" ").split(/\s+/).filter(Boolean).join(" ");
  if (!description) fail(at, "the prompt is empty.");
  out[at] = `\n<Prompt description=${attr(description)}>`;
  for (let i = at + 1; i <= end; i++) out[i] = null;
  out[start] = ["", ...text, "", "</Prompt>", ""].join("\n");
  return end;
}

const MDX_TAGS = new Set(["a", "b", "br", "code", "details", "div", "em", "i", "img", "kbd", "p", "span", "strong", "sub", "summary", "sup"]);

/**
 * Makes Markdown prose safe for MDX, which reads `{`, `<` and HTML comments as
 * code. Fenced code, inline code and component tags are left alone.
 */
export function mdxSafe(text) {
  const lines = text.split("\n");
  const mask = fenceMask(lines);
  let inComment = false;
  const out = [];
  lines.forEach((line, i) => {
    if (mask[i] || COMPONENT_TAG.test(line)) {
      out.push(line);
      return;
    }
    let rest = line;
    if (inComment) {
      const close = rest.indexOf("-->");
      if (close === -1) return;
      rest = rest.slice(close + 3);
      inComment = false;
      if (!rest.trim()) return;
    }
    const safe = outsideCode(rest, (prose) => {
      let s = prose.replace(/<!--[\s\S]*?-->/g, "");
      const open = s.indexOf("<!--");
      if (open !== -1) {
        inComment = true;
        s = s.slice(0, open);
      }
      return s
        .replace(/[{}]/g, (brace) => `\\${brace}`)
        .replace(/<((?:https?|mailto):[^\s<>]+)>/gi, (_, url) => `[${url}](${url})`)
        .replace(/<(br|img)\b([^>]*?)\s*\/?>/gi, (_, tag, attrs) => `<${tag}${attrs} />`)
        .replace(/<(\/?)([a-z][a-z0-9]*)?/gi, (match, slash, name) =>
          name && MDX_TAGS.has(name.toLowerCase()) ? match : `\\<${slash}${name ?? ""}`
        );
    });
    // Expanding components leaves runs of blank lines; one is enough outside code.
    if (safe.trim() || (!line.trim() && out.at(-1)?.trim())) out.push(safe);
  });
  return out.join("\n");
}

/**
 * Converts one guide page: the H1 becomes the title, the opening paragraph the
 * description, headings get their MkDocs ids pinned, links are rewritten, and
 * component markers become Blume components.
 */
export function convertPage(source, { file, label, context }) {
  const lines = source.replace(/\r\n?/g, "\n").split("\n");
  const mask = fenceMask(lines);
  const ids = headingIds(lines, mask);
  const pageDir = path.posix.dirname(file) === "." ? "" : path.posix.dirname(file);
  const rewritten = rewriteBody(lines, mask, ids, pageDir, context);

  let title = null;
  let titleLine = null;
  const out = lines.map((line, i) => {
    if (mask[i]) return line;
    const heading = ids.get(i);
    if (heading && heading.level === 1 && title === null) {
      title = plainText(heading.text);
      titleLine = i;
      return null;
    }
    if (heading && heading.level > 1) return `${rewritten[i].replace(/[ \t]+#+[ \t]*$/, "").trimEnd()} [#${heading.id}]`;
    return rewritten[i];
  });
  const mdx = usesComponents(source);
  if (mdx) expandComponents(lines, mask, ids, out, file);

  let titleAt = null;
  const body = [];
  out.forEach((entry, i) => {
    if (i === titleLine) titleAt = body.length;
    if (entry !== null) body.push(...entry.split("\n"));
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

  let text = body.join("\n").replace(/^\n+|\n+$/g, "");
  if (mdx) text = mdxSafe(text);
  return `${frontMatter.join("\n")}\n\n${text}\n`;
}

// ---------------------------------------------------------------------------
// Changelog: one Blume `type: changelog` entry per released version in the
// repository's CHANGELOG.md, so the site gets a /changelog timeline and feed.

export const CHANGELOG_DIR = "changelog";
const RELEASE_HEADING = /^## \[([^\]]+)\](?:\([^)]*\))?\s*[-–—]\s*(\d{4}-\d{2}-\d{2})\s*$/;
const BUCKET_WORDS = {
  added: ["added", "added"],
  changed: ["changed", "changed"],
  deprecated: ["deprecated", "deprecated"],
  removed: ["removed", "removed"],
  fixed: ["fixed", "fixed"],
  security: ["security fix", "security fixes"],
};

/**
 * The released versions in a Keep-a-Changelog file, newest first as written.
 * `## [Unreleased]` has no date and is left out. Each bucket (`### Added`, ...)
 * keeps its lines; a bucket with nothing in it is dropped.
 */
export function parseChangelog(source) {
  const lines = source.replace(/\r\n?/g, "\n").split("\n");
  const mask = fenceMask(lines);
  const releases = [];
  let release = null;
  let bucket = null;
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (!mask[i] && /^## /.test(line)) {
      const m = RELEASE_HEADING.exec(line);
      release = m ? { version: m[1], date: m[2], intro: [], buckets: [] } : null;
      if (release) releases.push(release);
      bucket = null;
    } else if (release && !mask[i] && /^### /.test(line)) {
      bucket = { title: line.slice(4).trim(), lines: [] };
      release.buckets.push(bucket);
    } else if (release) {
      (bucket ? bucket.lines : release.intro).push(line);
    }
  }
  for (const r of releases) r.buckets = r.buckets.filter((b) => b.lines.some((line) => !blank(line)));
  return releases;
}

/**
 * `0.1.0-preview.18` → `v0-1-0-preview-18`, the entry's file name and URL. The
 * `v` keeps Blume from reading the leading `0-` as a sidebar ordering prefix.
 */
export const changelogSlug = (version) =>
  `v${version.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "")}`;

/** "12 added, 4 changed and 9 fixed." from the top-level bullets of each bucket. */
function changelogSummary(buckets) {
  const parts = [];
  for (const { title, lines } of buckets) {
    const words = BUCKET_WORDS[title.toLowerCase()];
    const count = lines.filter((line) => /^[-*+] /.test(line)).length;
    if (words && count) parts.push(`${count} ${words[count === 1 ? 0 : 1]}`);
  }
  if (!parts.length) return null;
  const last = parts.pop();
  return `${parts.length ? `${parts.join(", ")} and ${last}` : last}.`;
}

const ISSUE_REF = /(^|[^\w/&#[`])#(\d{2,6})\b/g;

/** Links bare `#1234` references to GitHub, leaving code and existing links alone. */
export function linkIssues(text) {
  return outsideCode(text, (prose) => {
    const links = [];
    const masked = prose.replace(LINK, (link) => {
      links.push(link);
      return `\u0001${links.length - 1}\u0001`;
    });
    return masked
      .replace(ISSUE_REF, (_, before, n) => `${before}[#${n}](${REPO_URL}/issues/${n})`)
      .replace(/\u0001(\d+)\u0001/g, (_, i) => links[Number(i)]);
  });
}

/** One release as a Blume changelog page. CHANGELOG.md sits at the repo root, two levels above the guide. */
export function changelogEntry(release, context) {
  const lines = [...release.intro];
  for (const { title, lines: body } of release.buckets) lines.push(`## ${title}`, ...body);
  const mask = fenceMask(lines);
  const rewritten = rewriteBody(lines, mask, headingIds(lines, mask), "../..", context);
  const out = rewritten.slice();
  for (let i = 0; i < out.length; ) {
    if (mask[i] || blank(out[i])) {
      i++;
      continue;
    }
    let end = i;
    while (end < out.length && !mask[end] && !blank(out[end])) end++;
    out.splice(i, end - i, ...linkIssues(out.slice(i, end).join("\n")).split("\n"));
    i = end;
  }
  const body = out.join("\n").replace(/^\n+|\n+$/g, "");

  const frontMatter = [
    "---",
    `title: ${JSON.stringify(release.version)}`,
    "type: changelog",
    `date: ${release.date}`,
    "changelog:",
    `  version: ${JSON.stringify(release.version)}`,
  ];
  const summary = changelogSummary(release.buckets);
  if (summary) frontMatter.push(`description: ${JSON.stringify(summary)}`);
  frontMatter.push("---");
  return `${frontMatter.join("\n")}\n\n${body}\n`;
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
  let extraRedirects = nav.redirects ?? [];
  if (lenient) {
    // An old tag can predate a hand-written redirect: the page it retires may
    // still be there, or the page it points to may not exist yet.
    extraRedirects = extraRedirects.filter((redirect) => {
      const problems = checkRedirects([redirect], placements);
      warnings.push(...problems.map((problem) => `${problem} Skipped.`));
      return !problems.length;
    });
  }
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
    const source = sources.get(file);
    let page;
    try {
      page = convertPage(source, { file, label, context });
    } catch (error) {
      errors.push(error.message);
      continue;
    }
    // Blume runs components only in MDX pages.
    const written = usesComponents(source) ? dest.replace(/\.md$/, ".mdx") : dest;
    const target = path.join(docs, ...written.split("/"));
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

  let releases = 0;
  const changelog = path.join(repoRoot, "CHANGELOG.md");
  if (fs.existsSync(changelog)) {
    context.page = null;
    const dir = path.join(docs, CHANGELOG_DIR);
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, "meta.ts"), metaFile({ title: "Changelog", pages: [] }));
    for (const release of parseChangelog(fs.readFileSync(changelog, "utf8"))) {
      fs.writeFileSync(path.join(dir, `${changelogSlug(release.version)}.md`), changelogEntry(release, context));
      for (const miss of context.unresolved.splice(0)) {
        const message = `CHANGELOG.md [${release.version}]: link ${miss.target} ${miss.reason}.`;
        (miss.inGuide && !lenient ? errors : warnings).push(message);
      }
      releases++;
    }
  }

  const images = path.join(out, "public", "images");
  fs.rmSync(images, { recursive: true, force: true });
  if (fs.existsSync(path.join(guide, "images"))) fs.cpSync(path.join(guide, "images"), images, { recursive: true });

  const redirects = redirectsFor(placements, extraRedirects);
  fs.writeFileSync(path.join(out, REDIRECTS_FILE), `${JSON.stringify(redirects, null, 2)}\n`);

  return { errors, warnings, pages: placements.size, groups: groups.length, releases, redirects: redirects.length };
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const result = sync(parseArgs(process.argv.slice(2)));
  for (const warning of result.warnings) console.warn(`warning: ${warning}`);
  for (const error of result.errors) console.error(`error: ${error}`);
  if (result.errors.length) process.exit(1);
  console.log(
    `Synced ${result.pages} pages in ${result.groups} groups, ${result.releases} changelog entries, ${result.redirects} redirects.`
  );
}
