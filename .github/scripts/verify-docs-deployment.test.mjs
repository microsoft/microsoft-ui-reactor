// Regression tests for verify-docs-deployment.mjs — the gate that decides
// whether the live docs site is serving the artifact this workflow run
// published (issue #1268).
//
// Run directly with `node .github/scripts/verify-docs-deployment.test.mjs`, or
// let CI run it through DocsDeploymentVerifierTests in
// tests/Reactor.DocPipeline.Tests.
//
// Every case asserts a verdict the product code must *compute*: deleting any
// one of the four comparisons in evaluate() reddens at least one case here.

import test from "node:test";
import assert from "node:assert/strict";

import {
  evaluate,
  probe,
  report,
  selectProbeTargets,
  verifyDeployment,
} from "./verify-docs-deployment.mjs";

const THIS_RUN = "35774072302";
const OTHER_RUN = "35773982188";

const EXPECTED_VERSIONS = [
  { version: "main", title: "main (development)", aliases: [] },
  { version: "0.1.0-preview.16", title: "0.1.0-preview.16", aliases: ["latest"] },
  { version: "0.1.0-preview.15", title: "0.1.0-preview.15", aliases: [] },
];

function ok(body) {
  return { ok: true, status: 200, body, error: null };
}

function notFound() {
  return { ok: false, status: 404, body: "", error: null };
}

function unreachable() {
  return { ok: false, status: null, body: null, error: "getaddrinfo ENOTFOUND" };
}

/** A healthy live site: this run's stamp, the full version list, both pages. */
function healthy(overrides = {}) {
  const { target, control } = selectProbeTargets(EXPECTED_VERSIONS);
  return {
    stamp: ok(JSON.stringify({ run_id: THIS_RUN, sha: "459f7234" })),
    versions: ok(JSON.stringify(EXPECTED_VERSIONS)),
    target: { version: target, probe: ok("<html>") },
    control: { version: control, probe: ok("<html>") },
    ...overrides,
  };
}

function verdictFor(observations, expectedVersions = EXPECTED_VERSIONS) {
  return evaluate({ expectedRunId: THIS_RUN, expectedVersions, observations });
}

test("probe targets pick the latest holder and a distinct control", () => {
  assert.deepEqual(selectProbeTargets(EXPECTED_VERSIONS), {
    target: "0.1.0-preview.16",
    control: "main",
  });
});

test("probe targets fall back to main when nothing holds latest", () => {
  const unreleased = [{ version: "main", aliases: [] }];
  assert.deepEqual(selectProbeTargets(unreleased), { target: "main", control: null });
});

test("a live site serving this run passes", () => {
  const verdict = verdictFor(healthy());
  assert.equal(verdict.status, "pass");
  assert.deepEqual(verdict.failures, []);
  assert.equal(verdict.liveRunId, THIS_RUN);
});

// The reported bug, exactly: gh-pages was byte-correct, the deployment reported
// success, and the live site was serving the *other* run's artifact.
test("a stamp from another run is stranded and names both runs", () => {
  const verdict = verdictFor(
    healthy({ stamp: ok(JSON.stringify({ run_id: OTHER_RUN, sha: "459f7234" })) }),
  );
  assert.equal(verdict.status, "stranded");
  assert.deepEqual(
    verdict.failures.map((f) => f.kind),
    ["stamp-stale"],
  );
  assert.match(verdict.failures[0].message, new RegExp(OTHER_RUN));
  assert.match(verdict.failures[0].message, new RegExp(THIS_RUN));
});

test("a numeric run_id still matches the string this run is identified by", () => {
  const verdict = verdictFor(healthy({ stamp: ok(JSON.stringify({ run_id: Number(THIS_RUN) })) }));
  assert.equal(verdict.status, "pass");
});

test("a site that has never served a stamp is stranded, not passing", () => {
  const verdict = verdictFor(healthy({ stamp: notFound() }));
  assert.equal(verdict.status, "stranded");
  assert.deepEqual(
    verdict.failures.map((f) => f.kind),
    ["stamp-unreachable"],
  );
  assert.match(verdict.failures[0].message, /HTTP 404/);
});

test("a stamp that is not JSON is stranded", () => {
  const verdict = verdictFor(healthy({ stamp: ok("<html>404</html>") }));
  assert.equal(verdict.status, "stranded");
  assert.deepEqual(
    verdict.failures.map((f) => f.kind),
    ["stamp-unparseable"],
  );
});

test("a version this run published but the live site lacks is stranded", () => {
  const stale = EXPECTED_VERSIONS.filter((v) => v.version !== "0.1.0-preview.16");
  const verdict = verdictFor(healthy({ versions: ok(JSON.stringify(stale)) }));
  assert.equal(verdict.status, "stranded");
  assert.ok(verdict.failures.some((f) => f.kind === "versions-missing"));
  assert.match(
    verdict.failures.find((f) => f.kind === "versions-missing").message,
    /0\.1\.0-preview\.16/,
  );
});

test("latest sitting on the wrong version is stranded", () => {
  const dragged = EXPECTED_VERSIONS.map((v) => ({
    ...v,
    aliases: v.version === "0.1.0-preview.15" ? ["latest"] : [],
  }));
  const verdict = verdictFor(healthy({ versions: ok(JSON.stringify(dragged)) }));
  assert.equal(verdict.status, "stranded");
  assert.ok(verdict.failures.some((f) => f.kind === "latest-mismatch"));
});

test("a live site carrying extra newer versions still passes", () => {
  const newer = [{ version: "0.1.0-preview.17", aliases: [] }, ...EXPECTED_VERSIONS];
  const verdict = verdictFor(healthy({ versions: ok(JSON.stringify(newer)) }));
  assert.equal(verdict.status, "pass");
});

test("a published version whose directory 404s is stranded", () => {
  const { target } = selectProbeTargets(EXPECTED_VERSIONS);
  const verdict = verdictFor(healthy({ target: { version: target, probe: notFound() } }));
  assert.equal(verdict.status, "stranded");
  assert.deepEqual(
    verdict.failures.map((f) => f.kind),
    ["target-unreachable"],
  );
});

// The positive control. Without it, a probe that cannot reach the site at all
// is indistinguishable from a site that is serving the wrong deployment, and
// the gate would blame the release for a broken network.
test("a probe that sees no known-good content anywhere reports probe-broken", () => {
  const { target, control } = selectProbeTargets(EXPECTED_VERSIONS);
  const verdict = verdictFor({
    stamp: unreachable(),
    versions: unreachable(),
    target: { version: target, probe: unreachable() },
    control: { version: control, probe: unreachable() },
  });
  assert.equal(verdict.status, "probe-broken");
  assert.deepEqual(verdict.evidence, []);
});

test("a reachable versions.json keeps a stamp failure classified as stranded", () => {
  const { target, control } = selectProbeTargets(EXPECTED_VERSIONS);
  const verdict = verdictFor({
    stamp: notFound(),
    versions: ok(JSON.stringify(EXPECTED_VERSIONS)),
    target: { version: target, probe: notFound() },
    control: { version: control, probe: notFound() },
  });
  assert.equal(verdict.status, "stranded");
  assert.ok(verdict.evidence.length > 0);
});

test("the poll loop returns as soon as the deployment propagates", async () => {
  let clock = 0;
  let calls = 0;
  const fetchImpl = async (url) => {
    // The first round still serves the other run; the second serves this one.
    const servedRun = calls < 4 ? OTHER_RUN : THIS_RUN;
    calls += 1;
    if (url.includes("deploy-stamp.json")) {
      return { status: 200, text: async () => JSON.stringify({ run_id: servedRun }) };
    }
    if (url.includes("versions.json")) {
      return { status: 200, text: async () => JSON.stringify(EXPECTED_VERSIONS) };
    }
    return { status: 200, text: async () => "<html>" };
  };

  const verdict = await verifyDeployment({
    baseUrl: "https://example.test/docs/",
    expectedRunId: THIS_RUN,
    expectedVersions: EXPECTED_VERSIONS,
    timeoutMs: 60_000,
    intervalMs: 1_000,
    fetchImpl,
    uuid: () => "fixed",
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    },
    log: () => {},
  });

  assert.equal(verdict.status, "pass");
  assert.equal(verdict.attempt, 2);
});

test("the poll loop gives up once the window closes", async () => {
  let clock = 0;
  const fetchImpl = async (url) => {
    if (url.includes("deploy-stamp.json")) {
      return { status: 200, text: async () => JSON.stringify({ run_id: OTHER_RUN }) };
    }
    if (url.includes("versions.json")) {
      return { status: 200, text: async () => JSON.stringify(EXPECTED_VERSIONS) };
    }
    return { status: 200, text: async () => "<html>" };
  };

  const verdict = await verifyDeployment({
    baseUrl: "https://example.test/docs/",
    expectedRunId: THIS_RUN,
    expectedVersions: EXPECTED_VERSIONS,
    timeoutMs: 3_000,
    intervalMs: 1_000,
    fetchImpl,
    uuid: () => "fixed",
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    },
    log: () => {},
  });

  assert.equal(verdict.status, "stranded");
  assert.equal(verdict.attempt, 4);
});

// A cached response predating the deployment would make every check pass
// against content this run never produced, so the key must be both present and
// different on every request.
test("every probe carries a unique cache-busting key", async () => {
  const seen = [];
  let counter = 0;
  const fetchImpl = async (url) => {
    seen.push(url);
    return { status: 200, text: async () => "{}" };
  };

  await probe("https://example.test/docs/", "versions.json", {
    fetchImpl,
    uuid: () => `k${(counter += 1)}`,
  });
  await probe("https://example.test/docs/", "versions.json", {
    fetchImpl,
    uuid: () => `k${(counter += 1)}`,
  });

  assert.deepEqual(seen, [
    "https://example.test/docs/versions.json?nc=k1",
    "https://example.test/docs/versions.json?nc=k2",
  ]);
});

// actions/deploy-pages happens to emit a trailing slash today, but relative URL
// resolution drops the last segment without one — every probe would silently
// move to the domain root and report the whole site missing.
test("a base URL without a trailing slash still resolves inside the site", async () => {
  const seen = [];
  const fetchImpl = async (url) => {
    seen.push(url);
    return { status: 200, text: async () => "{}" };
  };

  await probe("https://owner.github.io/repo", "versions.json", { fetchImpl, uuid: () => "k" });
  await probe("https://owner.github.io/repo/", "0.1.0-preview.16/", { fetchImpl, uuid: () => "k" });

  assert.deepEqual(seen, [
    "https://owner.github.io/repo/versions.json?nc=k",
    "https://owner.github.io/repo/0.1.0-preview.16/?nc=k",
  ]);
});

test("a transport failure becomes a probe result rather than a throw", async () => {  const result = await probe("https://example.test/docs/", "versions.json", {
    fetchImpl: async () => {
      throw new Error("socket hang up");
    },
    uuid: () => "fixed",
  });
  assert.deepEqual(result, { ok: false, status: null, body: null, error: "socket hang up" });
});

test("exit codes separate a stranded deploy from a broken probe", () => {
  const silence = () => {};
  assert.equal(report({ ...verdictFor(healthy()), attempt: 1 }, { log: silence, err: silence }), 0);
  assert.equal(
    report({ ...verdictFor(healthy({ stamp: notFound() })), attempt: 1 }, { log: silence, err: silence }),
    1,
  );
  const broken = verdictFor({
    stamp: unreachable(),
    versions: unreachable(),
    target: null,
    control: null,
  });
  assert.equal(report({ ...broken, attempt: 1 }, { log: silence, err: silence }), 2);
});
