// Spec 038 §8 — Suppress→Error guardrail.
//
// What it does: reads two `mur check --trace <path>` outputs (one written
// from an iteration-mode invocation, one from --final) and asserts the
// invariant:
//
//   For every diagnostic that surfaces as Error in the --final trace, the
//   live PolicyTable's iteration-mode score for that code at Error severity
//   must be > 0.
//
// In plain English: a code the policy table tries to suppress in iteration
// must not be able to also be a real error in --final. If it can be, the
// policy table is misclassifying real build breaks as noise.
//
// Under the current PolicyTable design the "universal floor" rule (Error
// always scores 1.0) means this invariant holds by construction. The
// guardrail is the defense-in-depth regression test that catches future
// policy-table edits that remove the floor — it's CI's job to fail the
// PR before the bad table reaches main.
//
// Exit codes:
//   0 — no violation
//   1 — at least one violation; details printed to stderr
//   2 — usage / IO error
//
// Usage:
//   Reactor.MurCheckGuardrail <iter-trace.jsonl> <final-trace.jsonl>

using Microsoft.UI.Reactor.MurCheckGuardrail;

return GuardrailRunner.Run(args, Console.Out, Console.Error);
