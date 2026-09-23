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
  DEFAULT_REQUEST_TIMEOUT_MS,
  evaluate,
  expectedRootTarget,
  parseRootRedirect,
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

// What a release-tag run publishes: the tag holds `latest` because it is newest.
const PUBLISHED_RELEASE = ["0.1.0-preview.16"];

function ok(body) {
  return { ok: true, status: 200, body, error: null };
}

function notFound() {
  return { ok: false, status: 404, body: "", error: null };
}

function unreachable() {
  return { ok: false, status: null, body: null, error: "getaddrinfo ENOTFOUND" };
}

// The real mike root stub: a script redirect, a noscript meta refresh, and a
// human-visible link, all naming the same target.
const ROOT_STUB = `<!DOCTYPE html><html><head>
  <noscript><meta http-equiv="refresh" content="1; url=latest/" /></noscript>
  <script>window.location.replace("latest/" + window.location.search + window.location.hash);</script>
</head><body>Redirecting to <a href="latest/">latest/</a>...</body></html>`;

/** A healthy live site: this run's stamp, the full version list, every page. */
function healthy(overrides = {}, published = PUBLISHED_RELEASE) {
  const { targets, control } = selectProbeTargets(EXPECTED_VERSIONS, published);
  return {
    stamp: ok(JSON.stringify({ run_id: THIS_RUN, sha: "459f7234" })),
    versions: ok(JSON.stringify(EXPECTED_VERSIONS)),
    root: { probe: ok(ROOT_STUB) },
    alias: { version: "latest", probe: ok("<html>") },
    targets: targets.map((version) => ({ version, probe: ok("<html>") })),
    control: control ? { version: control, probe: ok("<html>") } : null,
    ...overrides,
  };
}

function verdictFor(observations, expectedVersions = EXPECTED_VERSIONS, publishedVersions = PUBLISHED_RELEASE) {
  return evaluate({ expectedRunId: THIS_RUN, expectedVersions, publishedVersions, observations });
}

test("probe targets cover the published version and the latest holder", () => {
  assert.deepEqual(selectProbeTargets(EXPECTED_VERSIONS, PUBLISHED_RELEASE), {
    targets: ["0.1.0-preview.16"],
    control: "main",
  });
});

// A backported tag publishes its version without moving `latest`, so probing
// only the alias holder would never touch the directory this run just wrote.
test("a backported tag is probed alongside the untouched latest holder", () => {
  const { targets, control } = selectProbeTargets(EXPECTED_VERSIONS, ["0.1.0-preview.15"]);
  assert.deepEqual(targets, ["0.1.0-preview.15", "0.1.0-preview.16"]);
  assert.equal(control, "main");
});

test("a main push probes main, which does not hold latest", () => {
  const { targets } = selectProbeTargets(EXPECTED_VERSIONS, ["main"]);
  assert.deepEqual(targets, ["main", "0.1.0-preview.16"]);
});

test("a backfill probes every version it published", () => {
  const { targets } = selectProbeTargets(EXPECTED_VERSIONS, ["0.1.0-preview.15", "0.1.0-preview.16"]);
  assert.deepEqual(targets, ["0.1.0-preview.15", "0.1.0-preview.16"]);
});

test("probe targets fall back to the latest holder when nothing was reported", () => {
  assert.deepEqual(selectProbeTargets(EXPECTED_VERSIONS, []).targets, ["0.1.0-preview.16"]);
});

test("probe targets fall back to main when nothing holds latest", () => {
  const unreleased = [{ version: "main", aliases: [] }];
  assert.deepEqual(selectProbeTargets(unreleased, []), { targets: ["main"], control: null });
});

test("a published version the live site never listed is reported, not silently dropped", () => {
  // The recorded list and versions.json are produced independently, so a name
  // in one and not the other means the run does not know what it wrote.
  const published = ["0.9.9-never-published"];
  const { targets } = selectProbeTargets(EXPECTED_VERSIONS, published);
  assert.deepEqual(targets, ["0.9.9-never-published", "0.1.0-preview.16"]);

  const verdict = verdictFor(healthy({}, published), EXPECTED_VERSIONS, published);
  assert.equal(verdict.status, "stranded");
  assert.ok(verdict.failures.some((f) => f.kind === "published-version-unlisted"));
  assert.match(
    verdict.failures.find((f) => f.kind === "published-version-unlisted").message,
    /0\.9\.9-never-published/,
  );
});

test("the expected site-root target follows latest, then main", () => {
  assert.equal(expectedRootTarget(EXPECTED_VERSIONS), "latest");
  assert.equal(expectedRootTarget([{ version: "main", aliases: [] }]), "main");
  assert.equal(expectedRootTarget([{ version: "0.1.0", aliases: [] }]), null);
});

// The URL readers land on. mike writes it only via `set-default`, so no version
// directory covers it and a stale root is invisible to every other check.
test("a site root that 404s is stranded", () => {
  const verdict = verdictFor(healthy({ root: { probe: notFound() } }));
  assert.equal(verdict.status, "stranded");
  assert.ok(verdict.failures.some((f) => f.kind === "root-unreachable"));
});

test("a site root redirecting to the wrong version is stranded", () => {
  const stale = ROOT_STUB.replace(/latest\//g, "main/");
  const verdict = verdictFor(healthy({ root: { probe: ok(stale) } }));
  assert.equal(verdict.status, "stranded");
  assert.ok(verdict.failures.some((f) => f.kind === "root-mistargeted"));
});

test("the root redirect target is parsed, not substring-matched", () => {
  assert.equal(parseRootRedirect(ROOT_STUB), "latest");
  assert.equal(parseRootRedirect('<script>window.location.replace("0.1.0-preview.16/")</script>'), "0.1.0-preview.16");
  assert.equal(parseRootRedirect('<noscript><meta http-equiv="refresh" content="1; url=main/" /></noscript>'), "main");
  assert.equal(parseRootRedirect("<html>no redirect here</html>"), null);
});

// `not-latest/` contains `latest/`, so a contains() check accepted it.
test("a root redirecting to a name containing the target is stranded", () => {
  const impostor = ROOT_STUB.replace(/latest\//g, "not-latest/");
  const verdict = verdictFor(healthy({ root: { probe: ok(impostor) } }));
  assert.equal(verdict.status, "stranded");
  const failure = verdict.failures.find((f) => f.kind === "root-mistargeted");
  assert.ok(failure);
  assert.match(failure.message, /not-latest\//);
});

// The expected target present only in decoration, while the real redirect
// points elsewhere.
test("a root whose only mention of the target is an unrelated link is stranded", () => {
  const misleading =
    '<script>window.location.replace("main/" + window.location.search);</script>' +
    '<body>See also <a href="latest/">latest/</a></body>';
  const verdict = verdictFor(healthy({ root: { probe: ok(misleading) } }));
  assert.equal(verdict.status, "stranded");
  assert.ok(verdict.failures.some((f) => f.kind === "root-mistargeted"));
});

test("a root with no recognisable redirect at all is stranded", () => {
  const verdict = verdictFor(healthy({ root: { probe: ok("<html><body>hello</body></html>") } }));
  assert.equal(verdict.status, "stranded");
  assert.ok(verdict.failures.some((f) => f.kind === "root-unparseable"));
});

// `--alias-type copy` makes `latest/` a separate tree from the version it
// aliases, and the site root forwards to it, so it needs its own probe.
test("a broken latest alias is stranded even when its version is healthy", () => {
  const verdict = verdictFor(healthy({ alias: { version: "latest", probe: notFound() } }));
  assert.equal(verdict.status, "stranded");
  assert.deepEqual(
    verdict.failures.map((f) => f.kind),
    ["alias-unreachable"],
  );
  assert.match(verdict.failures[0].message, /site root forwards there/);
});

test("the site root is not judged when no default can be determined", () => {
  const unknown = [{ version: "0.1.0", aliases: [] }];
  const verdict = evaluate({
    expectedRunId: THIS_RUN,
    expectedVersions: unknown,
    publishedVersions: ["0.1.0"],
    observations: {
      stamp: ok(JSON.stringify({ run_id: THIS_RUN })),
      versions: ok(JSON.stringify(unknown)),
      root: { probe: ok("<html>nothing recognisable</html>") },
      targets: [{ version: "0.1.0", probe: ok("<html>") }],
      control: null,
    },
  });
  assert.equal(verdict.status, "pass");
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
  const { targets } = selectProbeTargets(EXPECTED_VERSIONS, PUBLISHED_RELEASE);
  const verdict = verdictFor(
    healthy({ targets: targets.map((version) => ({ version, probe: notFound() })) }),
  );
  assert.equal(verdict.status, "stranded");
  assert.deepEqual(
    verdict.failures.map((f) => f.kind),
    ["target-unreachable"],
  );
});

// The reviewer's case: a backported tag does not move `latest`, so a gate that
// only probed the alias holder would pass while the directory this run just
// published was broken.
test("a broken backported directory is stranded even though latest is fine", () => {
  const published = ["0.1.0-preview.15"];
  const { targets, control } = selectProbeTargets(EXPECTED_VERSIONS, published);
  const verdict = verdictFor({
    stamp: ok(JSON.stringify({ run_id: THIS_RUN })),
    versions: ok(JSON.stringify(EXPECTED_VERSIONS)),
    targets: targets.map((version) => ({
      version,
      probe: version === "0.1.0-preview.15" ? notFound() : ok("<html>"),
    })),
    control: control ? { version: control, probe: ok("<html>") } : null,
  });
  assert.equal(verdict.status, "stranded");
  assert.deepEqual(
    verdict.failures.map((f) => f.kind),
    ["target-unreachable"],
  );
  assert.match(verdict.failures[0].message, /0\.1\.0-preview\.15/);
});

// The positive control. Without it, a probe that cannot reach the site at all
// is indistinguishable from a site that is serving the wrong deployment, and
// the gate would blame the release for a broken network.
test("a probe that sees no known-good content anywhere reports probe-broken", () => {
  const { targets, control } = selectProbeTargets(EXPECTED_VERSIONS, PUBLISHED_RELEASE);
  const verdict = verdictFor({
    stamp: unreachable(),
    versions: unreachable(),
    targets: targets.map((version) => ({ version, probe: unreachable() })),
    control: { version: control, probe: unreachable() },
  });
  assert.equal(verdict.status, "probe-broken");
  assert.deepEqual(verdict.evidence, []);
});

test("a reachable versions.json keeps a stamp failure classified as stranded", () => {
  const { targets, control } = selectProbeTargets(EXPECTED_VERSIONS, PUBLISHED_RELEASE);
  const verdict = verdictFor({
    stamp: notFound(),
    versions: ok(JSON.stringify(EXPECTED_VERSIONS)),
    targets: targets.map((version) => ({ version, probe: notFound() })),
    control: { version: control, probe: notFound() },
  });
  assert.equal(verdict.status, "stranded");
  assert.ok(verdict.evidence.length > 0);
});

test("the poll loop returns as soon as the deployment propagates", async () => {
  let clock = 0;
  let stampFetches = 0;
  const requested = [];
  const fetchImpl = async (url) => {
    requested.push(url);
    if (url.includes("deploy-stamp.json")) {
      // The first round still serves the other run; the second serves this one.
      stampFetches += 1;
      const servedRun = stampFetches === 1 ? OTHER_RUN : THIS_RUN;
      return { status: 200, text: async () => JSON.stringify({ run_id: servedRun }) };
    }
    if (url.includes("versions.json")) {
      return { status: 200, text: async () => JSON.stringify(EXPECTED_VERSIONS) };
    }
    if (url.includes("/docs/index.html")) {
      return { status: 200, text: async () => ROOT_STUB };
    }
    return { status: 200, text: async () => "<html>" };
  };

  const verdict = await verifyDeployment({
    baseUrl: "https://example.test/docs/",
    expectedRunId: THIS_RUN,
    expectedVersions: EXPECTED_VERSIONS,
    publishedVersions: PUBLISHED_RELEASE,
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

  // The driver must actually request these, not just decide it should.
  assert.ok(requested.some((url) => url.includes("/0.1.0-preview.16/index.html?nc=")));
  assert.ok(requested.some((url) => url.includes("/docs/index.html?nc=")));
  assert.ok(requested.some((url) => url.includes("/latest/index.html?nc=")));
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
    if (url.includes("/docs/index.html")) {
      return { status: 200, text: async () => ROOT_STUB };
    }
    return { status: 200, text: async () => "<html>" };
  };

  const verdict = await verifyDeployment({
    baseUrl: "https://example.test/docs/",
    expectedRunId: THIS_RUN,
    expectedVersions: EXPECTED_VERSIONS,
    publishedVersions: PUBLISHED_RELEASE,
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
  await probe("https://owner.github.io/repo/", "0.1.0-preview.16/index.html", { fetchImpl, uuid: () => "k" });

  assert.deepEqual(seen, [
    "https://owner.github.io/repo/versions.json?nc=k",
    "https://owner.github.io/repo/0.1.0-preview.16/index.html?nc=k",
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

// Neither fetch nor response.text() times out on its own, and observe() awaits
// every probe together, so one stalled connection would hold the polling loop
// past its deadline and swallow the diagnostic the job exists to print.
test("a stalled request is bounded rather than hanging forever", async () => {
  const started = Date.now();
  const result = await probe("https://example.test/docs/", "versions.json", {
    // Settles only when the abort signal fires, as a real socket would.
    fetchImpl: (url, options) =>
      new Promise((_, reject) => {
        options.signal.addEventListener("abort", () => reject(new Error("The operation was aborted")));
      }),
    uuid: () => "fixed",
    requestTimeoutMs: 25,
  });

  assert.equal(result.ok, false);
  assert.equal(result.status, null);
  assert.match(result.error, /abort/i);
  assert.ok(Date.now() - started < 5_000, "the probe should give up quickly, not hang");
});

test("a body that never finishes streaming is bounded too", async () => {
  const result = await probe("https://example.test/docs/", "versions.json", {
    fetchImpl: async (url, options) => ({
      status: 200,
      text: () =>
        new Promise((_, reject) => {
          options.signal.addEventListener("abort", () => reject(new Error("terminated")));
        }),
    }),
    uuid: () => "fixed",
    requestTimeoutMs: 25,
  });
  assert.equal(result.ok, false);
  assert.match(result.error, /terminated/);
});

test("every probe carries an abort signal and a sane default ceiling", async () => {
  let seenOptions = null;
  await probe("https://example.test/docs/", "versions.json", {
    fetchImpl: async (url, options) => {
      seenOptions = options;
      return { status: 200, text: async () => "{}" };
    },
    uuid: () => "fixed",
  });
  assert.ok(seenOptions.signal, "no abort signal was passed to fetch");
  assert.ok(
    DEFAULT_REQUEST_TIMEOUT_MS < 15_000 * 2,
    "the per-request ceiling must stay well under the polling window",
  );
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
    targets: [],
    control: null,
  });
  assert.equal(report({ ...broken, attempt: 1 }, { log: silence, err: silence }), 2);
});
