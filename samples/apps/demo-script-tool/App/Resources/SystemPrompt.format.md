# System Prompt Output Format

The Layer 1 system prompt at `SystemPrompt.txt` instructs the model to emit a
machine-grep-able envelope for each step. `Services/GeneratedOutputParser.cs`
consumes the same shape; **change both in lock-step**.

## Per-step envelope

```
===STEP <N>===
===CODE <relative path>===
```<lang>
<file body>
```
===CODE <relative path>===
```<lang>
<file body>
```
===DELTA===
<plain-text presenter notes>
===END STEP <N>===
```

- `===STEP N===` opens a step. `N` matches the step number from the demo script.
- `===CODE <path>===` opens a code block. Path is relative to the project root.
  - Single-file mode: `step-NN.cs`.
  - Multi-file mode: `step-NN/<filename>` (one block per file under that folder).
- A fenced code block (```` ``` ````) immediately follows the `CODE` marker. The
  language tag is informational only.
- `===DELTA===` opens the presenter notes for the step.
- `===END STEP N===` closes the step.

## Fix mode

When prompted with `FIX_MODE`, the model returns a single `===CODE <path>===`
block — no `STEP`, no `DELTA`, no `END STEP`. The parser has a dedicated
fix-mode entry point.
