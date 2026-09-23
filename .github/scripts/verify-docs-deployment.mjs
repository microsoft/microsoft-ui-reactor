// Asserts that the GitHub Pages deployment *this* workflow run produced is the
// one the live docs site is actually serving.
//
// Why this exists (issue #1268). A release PR always edits docs/guide/**, which
// matches the `push: branches: [main]` path filter in docs.yml, and the release
// tag then points at that same merge commit. Both runs hand
// `actions/deploy-pages` the same `pages_build_version` — it is `github.sha`,
// and the action exposes no input to override it — so the two deployments
// collide under one identity and one of them is silently stranded. Every job
// goes green, the deployment reports success, and `gh-pages` is byte-correct;
// the only symptom is that the live site keeps showing the previous release.
//
// Nothing else in the pipeline ever looks at the live site, so no amount of
// green CI could catch that. This gate is cause-agnostic: it fails whenever the
// bytes on the live site did not come from this run, whatever stranded them.
//
// Run it locally against a deployed site with:
//   DOCS_BASE_URL=https://microsoft.github.io/microsoft-ui-reactor/ \
//   DOCS_EXPECTED_RUN_ID=123 DOCS_EXPECTED_VERSIONS='[{"version":"main","aliases":[]}]' \
//   node .github/scripts/verify-docs-deployment.mjs
//
// The decision logic is a pure function so it can be tested without a network:
// see .github/scripts/verify-docs-deployment.test.mjs.

import { randomUUID } from "node:crypto";
import { pathToFileURL } from "node:url";

export const STAMP_PATH = "deploy-stamp.json";
export const VERSIONS_PATH = "versions.json";
export const LATEST_ALIAS = "latest";

/**
 * @typedef {{ ok: boolean, status: number|null, body: string|null, error: string|null }} Probe
 * @typedef {{ version: string, title?: string, aliases?: string[] }} VersionEntry
 */

/** Human-readable shorthand for what a probe actually returned. */
export function describeProbe(probe) {
  if (!probe) return "not attempted";
  if (probe.error) return `request failed: ${probe.error}`;
  return `HTTP ${probe.status}`;
}

function parseJson(text) {
  try {
    return { value: JSON.parse(text ?? ""), error: null };
  } catch (err) {
    return { value: null, error: err instanceof Error ? err.message : String(err) };
  }
}

function aliasHolder(entries, alias) {
  const match = entries.find((entry) => (entry?.aliases ?? []).includes(alias));
  return match ? match.version : null;
}

/**
 * Picks the two version directories worth fetching.
 *
 * `target` is the version this run most plausibly published — the one holding
 * `latest`, else `main`, else whatever came first. `control` is any *other*
 * published version, fetched with an identical request shape: it is the
 * positive control that separates "the probe cannot see the site at all" from
 * "the site is serving someone else's deployment". A no-match is not a
 * measurement until the same probe is shown able to match.
 */
export function selectProbeTargets(expectedVersions) {
  const entries = Array.isArray(expectedVersions) ? expectedVersions : [];
  const target =
    aliasHolder(entries, LATEST_ALIAS) ??
    entries.find((entry) => entry?.version === "main")?.version ??
    entries[0]?.version ??
    null;
  const control = entries.find((entry) => entry?.version && entry.version !== target)?.version ?? null;
  return { target, control };
}

/**
 * Classifies one round of observations. Pure — no clock, no network.
 *
 * @returns {{ status: "pass"|"stranded"|"probe-broken", failures: {kind: string, message: string}[], evidence: string[], liveRunId: string|null }}
 */
export function evaluate({ expectedRunId, expectedVersions, observations }) {
  const { stamp, versions, target, control } = observations;
  const failures = [];
  const evidence = [];
  let liveRunId = null;

  // --- The per-run oracle. ---------------------------------------------
  // Comparing the live version list alone is vacuous for a `main` push:
  // `mike deploy main` republishes an existing version, so the version *set*
  // is unchanged and the comparison passes whether or not this run's
  // deployment ever landed. The stamp is what makes the check mean something
  // on every trigger.
  if (!stamp?.ok) {
    failures.push({
      kind: "stamp-unreachable",
      message: `${STAMP_PATH} did not respond 200 (${describeProbe(stamp)}).`,
    });
  } else {
    const parsed = parseJson(stamp.body);
    if (parsed.error !== null) {
      failures.push({
        kind: "stamp-unparseable",
        message: `${STAMP_PATH} responded 200 but is not JSON (${parsed.error}).`,
      });
    } else {
      evidence.push(`${STAMP_PATH} responded 200 with parseable JSON`);
      liveRunId = parsed.value?.run_id == null ? null : String(parsed.value.run_id);
      if (liveRunId !== String(expectedRunId)) {
        failures.push({
          kind: "stamp-stale",
          message:
            `The live site is serving run ${liveRunId ?? "(no run_id)"} ` +
            `(sha ${parsed.value?.sha ?? "unknown"}), not this run ${expectedRunId}. ` +
            "Two deployments collided under one pages_build_version and this one lost — see issue #1268.",
        });
      }
    }
  }

  // --- Defence in depth: the version list and one rendered page. -------
  if (!versions?.ok) {
    failures.push({
      kind: "versions-unreachable",
      message: `${VERSIONS_PATH} did not respond 200 (${describeProbe(versions)}).`,
    });
  } else {
    const parsed = parseJson(versions.body);
    if (parsed.error !== null || !Array.isArray(parsed.value)) {
      failures.push({
        kind: "versions-unparseable",
        message:
          `${VERSIONS_PATH} responded 200 but is not a JSON array ` +
          `(${parsed.error ?? "parsed to a non-array"}).`,
      });
    } else {
      evidence.push(`${VERSIONS_PATH} responded 200 with a parseable array`);
      const live = parsed.value;
      const liveNames = new Set(live.map((entry) => entry?.version));
      const missing = (expectedVersions ?? [])
        .map((entry) => entry?.version)
        .filter((name) => name && !liveNames.has(name));
      if (missing.length > 0) {
        failures.push({
          kind: "versions-missing",
          message:
            `The live ${VERSIONS_PATH} is missing ${missing.join(", ")}. ` +
            `It lists: ${[...liveNames].join(", ") || "(nothing)"}.`,
        });
      }

      const expectedLatest = aliasHolder(expectedVersions ?? [], LATEST_ALIAS);
      const liveLatest = aliasHolder(live, LATEST_ALIAS);
      if (expectedLatest !== null && liveLatest !== expectedLatest) {
        failures.push({
          kind: "latest-mismatch",
          message:
            `The live '${LATEST_ALIAS}' alias points at ${liveLatest ?? "nothing"}, ` +
            `but this run published it on ${expectedLatest}.`,
        });
      }
    }
  }

  if (target && !target.probe?.ok) {
    failures.push({
      kind: "target-unreachable",
      message: `${target.version}/ did not respond 200 (${describeProbe(target.probe)}).`,
    });
  } else if (target) {
    evidence.push(`${target.version}/ responded 200`);
  }

  if (control?.probe?.ok) {
    evidence.push(`the positive control ${control.version}/ responded 200`);
  }

  if (failures.length === 0) {
    return { status: "pass", failures, evidence, liveRunId };
  }

  // A probe that cannot see *any* known-good content proves nothing about the
  // content it failed to find, so say so rather than blaming the deployment.
  if (evidence.length === 0) {
    return { status: "probe-broken", failures, evidence, liveRunId };
  }
  return { status: "stranded", failures, evidence, liveRunId };
}

/**
 * Fetches one URL with a unique cache-busting key, so a pass can never come
 * from a CDN entry that predates this deployment.
 *
 * The base is normalised to end in `/` first: relative resolution drops the
 * last path segment otherwise, so a `page_url` of
 * `https://owner.github.io/repo` would send every probe to
 * `https://owner.github.io/versions.json` and report the whole site missing.
 */
export async function probe(baseUrl, path, { fetchImpl = fetch, uuid = randomUUID } = {}) {
  const base = baseUrl.endsWith("/") ? baseUrl : `${baseUrl}/`;
  const url = new URL(path, base);
  url.searchParams.set("nc", uuid());
  try {
    const response = await fetchImpl(url.toString(), { cache: "no-store", redirect: "follow" });
    const body = await response.text();
    return { ok: response.status === 200, status: response.status, body, error: null };
  } catch (err) {
    return { ok: false, status: null, body: null, error: err instanceof Error ? err.message : String(err) };
  }
}

async function observe(baseUrl, expectedVersions, deps) {
  const { target, control } = selectProbeTargets(expectedVersions);
  const [stamp, versions, targetProbe, controlProbe] = await Promise.all([
    probe(baseUrl, STAMP_PATH, deps),
    probe(baseUrl, VERSIONS_PATH, deps),
    target ? probe(baseUrl, `${target}/`, deps) : Promise.resolve(null),
    control ? probe(baseUrl, `${control}/`, deps) : Promise.resolve(null),
  ]);
  return {
    stamp,
    versions,
    target: target ? { version: target, probe: targetProbe } : null,
    control: control ? { version: control, probe: controlProbe } : null,
  };
}

/**
 * Polls the live site until it serves this run's artifact, or the window closes.
 *
 * Pages propagation is not instantaneous, so a single shot would turn ordinary
 * latency into a red release. Polling on *any* non-pass verdict is deliberate:
 * a stale stamp is exactly what a deployment still propagating looks like.
 */
export async function verifyDeployment({
  baseUrl,
  expectedRunId,
  expectedVersions,
  timeoutMs = 600_000,
  intervalMs = 15_000,
  fetchImpl = fetch,
  uuid = randomUUID,
  now = () => Date.now(),
  sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  log = console.log,
}) {
  const deadline = now() + timeoutMs;
  let attempt = 0;
  let verdict;

  for (;;) {
    attempt += 1;
    const observations = await observe(baseUrl, expectedVersions, { fetchImpl, uuid });
    verdict = { ...evaluate({ expectedRunId, expectedVersions, observations }), attempt };

    if (verdict.status === "pass") return verdict;

    if (now() >= deadline) return verdict;

    log(
      `Attempt ${attempt}: ${verdict.status} — ${verdict.failures[0]?.message ?? "no detail"} ` +
        `Retrying in ${Math.round(intervalMs / 1000)}s.`,
    );
    await sleep(intervalMs);
  }
}

/** Renders a verdict as GitHub Actions annotations. Returns the process exit code. */
export function report(verdict, { baseUrl, expectedRunId, log = console.log, err = console.error } = {}) {
  if (verdict.status === "pass") {
    log(`The live site at ${baseUrl} is serving run ${expectedRunId}.`);
    for (const line of verdict.evidence) log(`  ✓ ${line}`);
    return 0;
  }

  const headline =
    verdict.status === "probe-broken"
      ? `Could not read ${baseUrl} at all, so the deployment is unverified. ` +
        "Not one probe returned known-good content, so this says nothing about the deployment itself — treat it as a broken check, not a stranded deploy."
      : `${baseUrl} is not serving the artifact this run published (issue #1268). ` +
        "Re-serve it by dispatching the Publish docs workflow on main — the artifact is the whole gh-pages branch, so any allowed ref republishes every version.";

  // Everything on one stream, so the annotation and the observations it rests
  // on cannot interleave out of order in the run log.
  err(`::error::${headline}`);
  for (const failure of verdict.failures) err(`::error::  ${failure.kind}: ${failure.message}`);
  for (const line of verdict.evidence) err(`  ✓ ${line} — so the probe itself works`);
  err(`Gave up after ${verdict.attempt} attempt(s).`);

  return verdict.status === "probe-broken" ? 2 : 1;
}

function requireEnv(name) {
  const value = process.env[name];
  if (!value) {
    console.error(`::error::${name} is required.`);
    process.exit(3);
  }
  return value;
}

// `import.meta.main` is Node 24+; compare URLs so this also runs on Node 20/22.
// pathToFileURL rather than string concatenation: a Windows drive letter would
// otherwise parse as the URL host and never match.
const invokedDirectly = Boolean(process.argv[1]) && import.meta.url === pathToFileURL(process.argv[1]).href;
if (invokedDirectly) {
  const baseUrl = requireEnv("DOCS_BASE_URL");
  const expectedRunId = requireEnv("DOCS_EXPECTED_RUN_ID");
  const rawVersions = requireEnv("DOCS_EXPECTED_VERSIONS");

  const parsed = parseJson(rawVersions);
  if (parsed.error !== null || !Array.isArray(parsed.value)) {
    console.error(`::error::DOCS_EXPECTED_VERSIONS is not a JSON array: ${parsed.error ?? "parsed to a non-array"}`);
    process.exit(3);
  }

  const verdict = await verifyDeployment({
    baseUrl,
    expectedRunId,
    expectedVersions: parsed.value,
    timeoutMs: Number(process.env.DOCS_VERIFY_TIMEOUT_SECONDS ?? 600) * 1000,
    intervalMs: Number(process.env.DOCS_VERIFY_INTERVAL_SECONDS ?? 15) * 1000,
  });

  process.exit(report(verdict, { baseUrl, expectedRunId }));
}
