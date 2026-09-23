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
//   DOCS_EXPECTED_RUN_ID=123 DOCS_EXPECTED_RUN_ATTEMPT=1 \
//   DOCS_EXPECTED_VERSIONS='[{"version":"main","aliases":[]}]' \
//   DOCS_PUBLISHED_VERSIONS='["main"]' \
//   node .github/scripts/verify-docs-deployment.mjs
//
// The decision logic is a pure function so it can be tested without a network:
// see .github/scripts/verify-docs-deployment.test.mjs.

import { randomUUID } from "node:crypto";
import { pathToFileURL } from "node:url";

export const STAMP_PATH = "deploy-stamp.json";
export const VERSIONS_PATH = "versions.json";
export const ROOT_INDEX_PATH = "index.html";
export const LATEST_ALIAS = "latest";

/**
 * Per-request ceiling. Deliberately well under the polling window so a stalled
 * connection costs one round rather than the whole budget, and under the job's
 * own timeout so the failure is this gate's diagnostic rather than a silent
 * runner kill.
 */
export const DEFAULT_REQUEST_TIMEOUT_MS = 20_000;

/**
 * Timeout signal for one request, plus the handle to cancel it.
 *
 * Deliberately not `AbortSignal.timeout()`: that timer is unref'd, so it does
 * not hold the event loop open and never fires when nothing else is pending.
 * Real traffic hides this because the socket keeps the loop alive, but it made
 * the regression suite hang and take the rest of the file down with it. A
 * plain `setTimeout` fires reliably, and cancelling it after the request
 * settles stops a fast response from leaving a 20s timer behind.
 */
function defaultCreateTimeout(ms) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(new Error(`timed out after ${ms}ms`)), ms);
  return { signal: controller.signal, cancel: () => clearTimeout(timer) };
}

/**
 * The page a version probe fetches.
 *
 * Explicitly `index.html` rather than the bare directory: a bare directory is
 * only equivalent on a server that has no directory listing. GitHub Pages 404s
 * a directory whose index is missing, but a plain static server answers 200
 * with a generated listing — which quietly turns a broken deployment into a
 * passing probe when this gate is exercised locally.
 */
function versionIndex(version) {
  return `${version}/index.html`;
}

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
 * Picks the version directories worth fetching.
 *
 * `targets` are the versions this run actually published, because those are the
 * ones whose bytes are new on the live site. Probing only the `latest` holder
 * would miss them: publishing a backported tag deliberately does not move
 * `latest` (see the "Publish the release version" step in docs.yml), and a
 * `main` push republishes `main` while `latest` sits on a release. In both
 * cases a broken new directory would pass as long as `versions.json` listed it.
 * The `latest` holder is probed as well, since it is what the site root serves.
 *
 * `control` is any *other* published version, fetched with an identical request
 * shape: it is the positive control that separates "the probe cannot see the
 * site at all" from "the site is serving someone else's deployment". A no-match
 * is not a measurement until the same probe is shown able to match.
 */
export function selectProbeTargets(expectedVersions, publishedVersions = []) {
  const entries = Array.isArray(expectedVersions) ? expectedVersions : [];
  const published = Array.isArray(publishedVersions) ? publishedVersions.filter(Boolean) : [];
  const latest = aliasHolder(entries, LATEST_ALIAS);

  // Deliberately not intersected with `expectedVersions`. A recorded version
  // that the site does not list is a real inconsistency, and dropping it here
  // would shrink the probe set on exactly the run that needs it most; evaluate()
  // reports it instead.
  const targets = [];
  for (const version of published) {
    if (!targets.includes(version)) targets.push(version);
  }

  // Only reached when the caller could not say what it published; keeps the
  // gate meaningful rather than probing nothing at all.
  if (targets.length === 0) {
    const fallback =
      latest ?? entries.find((entry) => entry?.version === "main")?.version ?? entries[0]?.version ?? null;
    if (fallback) targets.push(fallback);
  }

  if (latest && !targets.includes(latest)) targets.push(latest);

  const control =
    entries.map((entry) => entry?.version).find((version) => version && !targets.includes(version)) ?? null;

  return { targets, control };
}

/**
 * The version or alias a mike-generated site root forwards to, or null when the
 * document does not look like one.
 *
 * Parsed rather than substring-matched. `set-default` writes the target into a
 * `location.replace(...)` call and a `<noscript>` meta refresh, but the same
 * document also contains a human-visible `<a href="...">`. A contains() check
 * would accept a root that redirects to `not-latest/` (which contains
 * `latest/`), or one whose only mention of the expected target is that link
 * while the actual redirect points somewhere else.
 */
export function parseRootRedirect(html) {
  const text = typeof html === "string" ? html : "";

  // What a browser with scripting actually follows.
  const script = text.match(/location\s*\.\s*replace\(\s*["']([^"']+)["']/);
  if (script) return normaliseTarget(script[1]);

  const meta = text.match(/http-equiv=["']refresh["'][^>]*content=["'][^"']*url=\s*([^"'\s]+)/i);
  if (meta) return normaliseTarget(meta[1]);

  return null;
}

function normaliseTarget(target) {
  return target.replace(/^\.?\//, "").replace(/\/+$/, "");
}

/**
 * The version or alias the site root should redirect to.
 *
 * mike's `set-default` writes a root index.html that forwards to whichever
 * version or alias is default — `latest` once any release exists, and `main`
 * during the window before the first one. Returns null when neither is
 * published, because the workflow's own fallback is ambiguous there and a
 * guessed expectation would be worse than none.
 */
export function expectedRootTarget(expectedVersions) {
  const entries = Array.isArray(expectedVersions) ? expectedVersions : [];
  if (aliasHolder(entries, LATEST_ALIAS)) return LATEST_ALIAS;
  if (entries.some((entry) => entry?.version === "main")) return "main";
  return null;
}

/**
 * Classifies one round of observations. Pure — no clock, no network.
 *
 * @returns {{ status: "pass"|"stranded"|"probe-broken", failures: {kind: string, message: string}[], evidence: string[], liveRunId: string|null }}
 */
export function evaluate({
  expectedRunId,
  expectedRunAttempt,
  expectedVersions,
  publishedVersions = [],
  observations,
}) {
  const { stamp, versions, control, root, alias } = observations;
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
      const liveAttempt = parsed.value?.run_attempt == null ? null : String(parsed.value.run_attempt);
      const live = `${liveRunId ?? "(no run_id)"} attempt ${liveAttempt ?? "(unknown)"}`;
      const expected = `${expectedRunId} attempt ${expectedRunAttempt}`;

      // The attempt is part of the identity, not decoration. `run_id` is stable
      // across re-runs, so a full "Re-run all jobs" republishes under the same
      // id — and if that deployment is stranded while the previous attempt's
      // artifact stays live, an id-only comparison passes on someone else's
      // bytes.
      //
      // Compared against the attempt that produced *the artifact*, exported by
      // the publish job, rather than this job's own `github.run_attempt`.
      // Re-running only the failed `deploy` job reuses the original publish, so
      // the stamp legitimately carries the older attempt and the verify job's
      // own counter would disagree with it on every partial re-run.
      if (liveRunId !== String(expectedRunId) || liveAttempt !== String(expectedRunAttempt)) {
        failures.push({
          kind: "stamp-stale",
          message:
            `The live site is serving run ${live} (sha ${parsed.value?.sha ?? "unknown"}), ` +
            `not the artifact this run published (${expected}). ` +
            "Whatever stranded it, the bytes this run published are not the bytes being served.",
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

  // The publishing steps and mike produce these two lists independently, so a
  // name in one and not the other means the run does not know what it wrote.
  // The workflow fails earlier on this, which makes reaching it here a sign
  // that the earlier guard was bypassed rather than a routine outcome.
  const declared = new Set((expectedVersions ?? []).map((entry) => entry?.version));
  const unlisted = (publishedVersions ?? []).filter((version) => version && !declared.has(version));
  if (unlisted.length > 0) {
    failures.push({
      kind: "published-version-unlisted",
      message:
        `This run reported publishing ${unlisted.join(", ")}, but the artifact's version list does not contain ` +
        `${unlisted.length === 1 ? "it" : "them"}. The two are produced independently, so they must agree.`,
    });
  }

  // The URL readers actually land on. mike writes it only via `set-default`,
  // and it is the one page no version directory covers, so a stale or broken
  // root is invisible to every other check here.
  const rootTarget = expectedRootTarget(expectedVersions);
  if (root) {
    if (!root.probe?.ok) {
      failures.push({
        kind: "root-unreachable",
        message: `The site root did not respond 200 (${describeProbe(root.probe)}).`,
      });
    } else {
      evidence.push("the site root responded 200");
      if (rootTarget) {
        const actual = parseRootRedirect(root.probe.body);
        if (actual === null) {
          failures.push({
            kind: "root-unparseable",
            message:
              "The site root responded 200 but carries no recognisable redirect. " +
              "`mike set-default` writes one, so a root without it sends readers nowhere.",
          });
        } else if (actual !== rootTarget) {
          failures.push({
            kind: "root-mistargeted",
            message:
              `The site root redirects to '${actual}/', not '${rootTarget}/'. Readers landing on the ` +
              "bare URL would be sent somewhere other than the version this run made default.",
          });
        }
      }
    }
  }

  for (const target of observations.targets ?? []) {
    if (!target.probe?.ok) {
      failures.push({
        kind: "target-unreachable",
        message: `${target.version}/ did not respond 200 (${describeProbe(target.probe)}).`,
      });
    } else {
      evidence.push(`${target.version}/ responded 200`);
    }
  }

  if (control?.probe?.ok) {
    evidence.push(`the positive control ${control.version}/ responded 200`);
  }

  // The `latest` alias is its own copied tree, and the site root forwards to
  // it, so a broken `latest/` 404s every reader arriving at the bare URL even
  // when the version it aliases is perfectly healthy.
  if (alias) {
    if (!alias.probe?.ok) {
      failures.push({
        kind: "alias-unreachable",
        message:
          `${alias.version}/ did not respond 200 (${describeProbe(alias.probe)}). ` +
          "The site root forwards there, so readers landing on the bare URL would get nothing.",
      });
    } else {
      evidence.push(`${alias.version}/ responded 200`);
    }
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
export async function probe(
  baseUrl,
  path,
  {
    fetchImpl = fetch,
    uuid = randomUUID,
    requestTimeoutMs = DEFAULT_REQUEST_TIMEOUT_MS,
    createTimeout = defaultCreateTimeout,
  } = {},
) {
  const base = baseUrl.endsWith("/") ? baseUrl : `${baseUrl}/`;
  const url = new URL(path, base);
  url.searchParams.set("nc", uuid());

  // Bounded: neither fetch nor response.text() times out on its own, and
  // observe() awaits every probe together, so one stalled connection would
  // otherwise hold the whole polling loop past its deadline and swallow the
  // diagnostic the job exists to print.
  const timeout = createTimeout(requestTimeoutMs);
  try {
    const response = await fetchImpl(url.toString(), {
      cache: "no-store",
      redirect: "follow",
      signal: timeout.signal,
    });
    const body = await response.text();

    // A followed redirect means the requested path is *not* what served this
    // response, so accepting on the final status alone would let a missing
    // version directory pass by bouncing to a healthy page.
    if (response.redirected) {
      return {
        ok: false,
        status: response.status,
        body,
        error: `redirected to ${response.url ?? "an unknown location"}`,
      };
    }

    return { ok: response.status === 200, status: response.status, body, error: null };
  } catch (err) {
    return { ok: false, status: null, body: null, error: err instanceof Error ? err.message : String(err) };
  } finally {
    timeout.cancel();
  }
}

async function observe(baseUrl, expectedVersions, publishedVersions, deps) {
  const { targets, control } = selectProbeTargets(expectedVersions, publishedVersions);
  const latest = aliasHolder(expectedVersions ?? [], LATEST_ALIAS);

  // Built as a labelled list rather than positional destructuring: the probe
  // set is conditional, and an off-by-one there would silently swap two
  // results instead of failing.
  const jobs = [
    { key: "stamp", path: STAMP_PATH },
    { key: "versions", path: VERSIONS_PATH },
    { key: "root", path: ROOT_INDEX_PATH },
    // `mike deploy --alias-type copy` publishes `latest/` as its own tree, and
    // the site root forwards there, so a missing `latest/index.html` 404s every
    // reader arriving at the bare URL. Probing the version that *owns* the
    // alias does not cover it: they are separate directories.
    ...(latest ? [{ key: "alias", path: versionIndex(LATEST_ALIAS) }] : []),
    ...targets.map((version) => ({ key: "target", version, path: versionIndex(version) })),
    ...(control ? [{ key: "control", version: control, path: versionIndex(control) }] : []),
  ];

  const results = await Promise.all(jobs.map((job) => probe(baseUrl, job.path, deps)));

  const observations = { stamp: null, versions: null, root: null, alias: null, targets: [], control: null };
  jobs.forEach((job, i) => {
    const result = results[i];
    if (job.key === "target") observations.targets.push({ version: job.version, probe: result });
    else if (job.key === "control") observations.control = { version: job.version, probe: result };
    else if (job.key === "alias") observations.alias = { version: LATEST_ALIAS, probe: result };
    else if (job.key === "root") observations.root = { probe: result };
    else observations[job.key] = result;
  });
  return observations;
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
  expectedRunAttempt,
  expectedVersions,
  publishedVersions = [],
  timeoutMs = 600_000,
  intervalMs = 15_000,
  requestTimeoutMs = DEFAULT_REQUEST_TIMEOUT_MS,
  fetchImpl = fetch,
  uuid = randomUUID,
  createTimeout = defaultCreateTimeout,
  now = () => Date.now(),
  sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  log = console.log,
}) {
  const deadline = now() + timeoutMs;
  let attempt = 0;
  let verdict;

  for (;;) {
    attempt += 1;
    const observations = await observe(baseUrl, expectedVersions, publishedVersions, {
      fetchImpl,
      uuid,
      requestTimeoutMs,
      createTimeout,
    });
    verdict = {
      ...evaluate({ expectedRunId, expectedRunAttempt, expectedVersions, publishedVersions, observations }),
      attempt,
    };

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
export function report(verdict, { baseUrl, expectedRunId, expectedRunAttempt, log = console.log, err = console.error } = {}) {
  if (verdict.status === "pass") {
    log(`The live site at ${baseUrl} is serving run ${expectedRunId} attempt ${expectedRunAttempt}.`);
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

/** Signals a bad invocation, so the CLI block can exit 3 without a stack trace. */
class UsageError extends Error {}

function requireEnv(name) {
  const value = process.env[name];
  if (!value) throw new UsageError(`${name} is required.`);
  return value;
}

function requireJsonArrayEnv(name) {
  const parsed = parseJson(requireEnv(name));
  if (parsed.error !== null || !Array.isArray(parsed.value)) {
    throw new UsageError(`${name} is not a JSON array: ${parsed.error ?? "parsed to a non-array"}`);
  }
  return parsed.value;
}

// `import.meta.main` is Node 24+; compare URLs so this also runs on Node 20/22.
// pathToFileURL rather than string concatenation: a Windows drive letter would
// otherwise parse as the URL host and never match.
const invokedDirectly = Boolean(process.argv[1]) && import.meta.url === pathToFileURL(process.argv[1]).href;
if (invokedDirectly) {
  try {
    const baseUrl = requireEnv("DOCS_BASE_URL");
    const expectedRunId = requireEnv("DOCS_EXPECTED_RUN_ID");

    // The attempt that produced the artifact, not this job's own counter. See
    // the stamp comparison in evaluate() for why the two differ on a partial
    // re-run, and why an id-only check goes false-green on a full one.
    const expectedRunAttempt = requireEnv("DOCS_EXPECTED_RUN_ATTEMPT");
    const expectedVersions = requireJsonArrayEnv("DOCS_EXPECTED_VERSIONS");

    // Required rather than defaulted: without it the probe silently falls back
    // to the `latest` holder, which is exactly the blind spot that lets a
    // broken backported-tag or `main` directory pass. A wiring mistake should
    // be loud.
    const publishedVersions = requireJsonArrayEnv("DOCS_PUBLISHED_VERSIONS");

    const verdict = await verifyDeployment({
      baseUrl,
      expectedRunId,
      expectedRunAttempt,
      expectedVersions,
      publishedVersions,
      timeoutMs: Number(process.env.DOCS_VERIFY_TIMEOUT_SECONDS ?? 600) * 1000,
      intervalMs: Number(process.env.DOCS_VERIFY_INTERVAL_SECONDS ?? 15) * 1000,
    });

    // `process.exitCode` rather than `process.exit()`. On POSIX — which is
    // where this runs — writes to a piped stdout/stderr are asynchronous, and
    // `process.exit()` tears the process down without draining them. The whole
    // value of a failing run here is the annotations explaining *what* the live
    // site was serving, so truncating them loses exactly the output the gate
    // exists to produce. Setting the code lets node exit once the loop drains,
    // which flushes. (Not reproducible on Windows, where those pipes are
    // synchronous.)
    process.exitCode = report(verdict, { baseUrl, expectedRunId, expectedRunAttempt });
  } catch (err) {
    if (!(err instanceof UsageError)) throw err;
    console.error(`::error::${err.message}`);
    process.exitCode = 3;
  }
}
