#!/usr/bin/env node
// HeadTrax service smoke test.
//
// Boots the real server against freshly generated data and asserts it answers
// with real rows. A build or lint check cannot catch what this catches: the
// bug that made this sample unrunnable for weeks was an import that only fails
// at startup.
//
// Self-contained and idempotent — generates into its own database with
// --reset, picks its own port, and cleans up. Run it locally with:
//
//   npm run smoke

import { spawn } from "child_process";
import { existsSync, rmSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const DB = join(__dirname, "headtrax.smoke.db");
const PORT = parseInt(process.env.SMOKE_PORT || "4123", 10);
const COUNT = 500;
// Every request is bounded. These queries run against 500 rows, so anything
// approaching this is a hang, not slowness.
const REQUEST_TIMEOUT_MS = 30_000;
const URL = `http://localhost:${PORT}/graphql`;

function run(cmd, args, opts = {}) {
  return new Promise((resolve, reject) => {
    const p = spawn(cmd, args, { stdio: "inherit", shell: false, ...opts });
    p.on("error", reject);
    p.on("exit", (code) =>
      code === 0 ? resolve() : reject(new Error(`${cmd} ${args.join(" ")} exited ${code}`)),
    );
  });
}

async function gql(query) {
  // Bounded for the same reason as the health probe: fetch has no default
  // timeout, so a resolver that never completes would hang this script until
  // the workflow's outer timeout instead of reporting a failure.
  const res = await fetch(URL, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ query }),
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status} from ${URL}`);
  return res.json();
}

const failures = [];
function check(label, actual, predicate, expectation) {
  const pass = predicate(actual);
  console.log(`  ${pass ? "ok  " : "FAIL"}  ${label}: ${JSON.stringify(actual)}${pass ? "" : `  (expected ${expectation})`}`);
  if (!pass) failures.push(label);
}

async function waitForHealth(deadlineMs = 60_000) {
  const started = Date.now();
  while (Date.now() - started < deadlineMs) {
    const remaining = deadlineMs - (Date.now() - started);
    try {
      // Bound each request: fetch has no default timeout, so a server that
      // accepts the connection but never responds would leave this await
      // pending forever and the loop would never recheck its clock — the
      // deadline above would not actually bound anything.
      const res = await fetch(`http://localhost:${PORT}/health`, {
        signal: AbortSignal.timeout(Math.max(1, Math.min(2_000, remaining))),
      });
      if (res.ok) return res.json();
    } catch {
      // not listening yet, or this probe timed out
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`server did not become healthy on port ${PORT} within ${deadlineMs}ms`);
}

let server;
let serverExited = false;
let tearingDown = false;
try {
  for (const f of [DB, `${DB}-wal`, `${DB}-shm`]) if (existsSync(f)) rmSync(f);

  console.log(`\n> generating ${COUNT} employees into ${DB}`);
  await run(process.execPath, [
    join(__dirname, "generate-data.js"),
    "--count", String(COUNT),
    "--db", DB,
    "--reset",
  ]);

  console.log(`\n> starting server on port ${PORT}`);
  server = spawn(process.execPath, [join(__dirname, "server.js")], {
    stdio: "inherit",
    env: { ...process.env, DB_PATH: DB, PORT: String(PORT) },
  });
  server.on("exit", (code, signal) => {
    serverExited = true;
    // Any exit before teardown is a failure, whatever the status. Checking for
    // a non-zero code is not enough: Node reports code === null when a child
    // dies by signal, and a server that exits 0 on its own mid-run is equally
    // wrong, so either would otherwise slip through to "smoke passed".
    if (!tearingDown) {
      console.error(`\nserver exited before teardown (code ${code}, signal ${signal})`);
      process.exitCode = 1;
    }
  });

  const health = await waitForHealth();
  console.log("\n> assertions");
  check("health.ok", health.ok, (v) => v === true, "true");

  // A 200 proves nothing on its own here: GraphQL answers 200 with an `errors`
  // body, and an empty page is also a 200. Assert the payload instead.
  const page = await gql(
    '{ stats { totalEmployees } departments employees(pageSize: 3, select: ["firstName","lastName","email"]) { items { firstName lastName email } } }',
  );
  check("no GraphQL errors", page.errors ?? null, (v) => v === null, "no errors key");

  const d = page.data ?? {};
  check("stats.totalEmployees", d.stats?.totalEmployees, (v) => v === COUNT, String(COUNT));
  check("departments count", d.departments?.length ?? 0, (v) => v > 0, "> 0");
  check("returned rows", d.employees?.items?.length ?? 0, (v) => v === 3, "3");
  check(
    "emails well-formed",
    (d.employees?.items ?? []).map((e) => e.email),
    (v) => v.length === 3 && v.every((e) => typeof e === "string" && e.includes("@")),
    "3 addresses each containing @",
  );

  // Full-text search is a separate SQLite index, so the page query above says
  // nothing about it.
  //
  // The probe terms are fixed constants, never sampled from the generated data.
  // faker is unseeded, so the data differs every run, and ~6% of surnames
  // contain a hyphen or apostrophe. server.js strips `'"*()` but not `-` before
  // appending `*` for FTS5 MATCH, so `Bayer-Kohler` errors with
  // `no such column: Kohler` and `O'Connell` collapses to `OConnell*` and
  // matches nothing. Sampling a surname would fail ~1 run in 17.
  //
  // `headtrax` is safe by construction: generate-data.js hardcodes the address
  // domain headtrax.example.com and `email` is an indexed FTS column.
  const count = async (term) =>
    (await gql(`{ employees(pageSize: 5, searchQuery: "${term}", select: ["lastName"]) { totalCount } }`))
      .data?.employees?.totalCount;

  const all = await count("headtrax");
  const some = await count("Engineering");
  const miss = await count("zzzznotarealsurname");

  // `headtrax` alone is a weak oracle: it matches every row, which is exactly
  // what an *ignored* searchQuery would return. The nonsense term rules that
  // out, and `Engineering` — a hardcoded department, so always populated —
  // proves the index discriminates rather than answering all-or-nothing.
  check("FTS all-rows term (headtrax)", all, (v) => v === COUNT, String(COUNT));
  check("FTS nonsense term", miss, (v) => v === 0, "0");
  check("FTS subset term (Engineering)", some, (v) => v > 0 && v < COUNT, `0 < n < ${COUNT}`);
} finally {
  // Set before kill() so the exit listener can tell an intentional shutdown
  // from a crash.
  tearingDown = true;
  // Wait for the server to actually release the database before removing it:
  // kill() only signals, and on Windows unlinking a file with an open handle
  // fails with EBUSY.
  if (server && !serverExited) {
    server.kill();
    await new Promise((resolve) => {
      const done = setTimeout(resolve, 5_000);
      server.once("exit", () => {
        clearTimeout(done);
        resolve();
      });
    });
  }
  // Best-effort: a leftover scratch database is gitignored and must never be
  // the reason this check fails.
  for (const f of [DB, `${DB}-wal`, `${DB}-shm`]) {
    try {
      if (existsSync(f)) rmSync(f, { force: true });
    } catch {
      console.warn(`could not remove ${f} (ignored)`);
    }
  }
}

if (failures.length > 0) {
  console.error(`\nsmoke FAILED: ${failures.length} check(s) — ${failures.join(", ")}\n`);
  process.exit(1);
}
// The server's exit listener records a failure if the child died unexpectedly
// — including after the last assertion or during teardown. Exiting 0
// unconditionally here would overwrite that and report success on a crash.
if (process.exitCode) {
  console.error(`\nsmoke FAILED: server exited unexpectedly (code ${process.exitCode})\n`);
  process.exit(process.exitCode);
}
console.log("\nsmoke passed\n");
