// Spec 038 §8 — the four `mur check` invocation modes.
//
//   Iteration (default): emit errors always; emit warnings only if ranker
//                        score ≥ iteration threshold; suppress info/style.
//   Strict:              promote warnings to errors. CI gates; not the loop.
//   Final:               emit every diagnostic, no suppression. The "I am
//                        done iterating" pre-merge gate.
//   Quiet:               errors only. Sub-iteration loops where the agent
//                        just wants the smallest possible signal.

namespace Microsoft.UI.Reactor.Cli.Check;

internal enum Mode
{
    Iteration = 0,
    Strict,
    Final,
    Quiet,
}
