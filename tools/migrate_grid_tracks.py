"""
One-shot migration: rewrite Grid(["*", "Auto", "200"], ...) string-form calls
to Grid([GridSize.Star(), GridSize.Auto, GridSize.Px(200)], ...).

Spec 033 §1. We deliberately keep this conservative — only token-literal
strings and collection-expression / new[] { ... } shapes; dynamic string[]
arrays are left for the [Obsolete] warning to flag.

Run after editing the input list at the bottom; backups are not made (use git).
"""
from __future__ import annotations
import re
import sys
from pathlib import Path

# Match a literal track string and convert to GridSize.X form.
def convert_one_token(token: str) -> str | None:
    s = token.strip()
    if s.startswith('"') and s.endswith('"'):
        inner = s[1:-1].strip()
        if inner.lower() == "auto":
            return "GridSize.Auto"
        if inner == "*":
            return "GridSize.Star()"
        if inner.endswith("*"):
            num = inner[:-1]
            try:
                v = float(num)
                if v == 1.0:
                    return "GridSize.Star()"
                # Render with minimal trailing zeros
                if v.is_integer():
                    return f"GridSize.Star({int(v)})"
                return f"GridSize.Star({num})"
            except ValueError:
                return None
        try:
            v = float(inner)
            if v.is_integer():
                return f"GridSize.Px({int(v)})"
            return f"GridSize.Px({inner})"
        except ValueError:
            return None
    return None

# Convert a token list inside [ ... ] or new[] { ... }
def convert_token_list(content: str) -> str | None:
    parts = [p.strip() for p in content.split(",")]
    if not parts:
        return None
    converted = []
    for p in parts:
        if not p:
            continue
        c = convert_one_token(p)
        if c is None:
            return None
        converted.append(c)
    return ", ".join(converted)

# Patterns:
#   Grid([...], [...], ...)
#   Grid(new[] { ... }, new[] { ... }, ...)
#   Factories.Grid(...)
COLLECTION_RE = re.compile(r'\[((?:[^\[\]]|"[^"]*")*?)\]')
NEW_ARR_RE = re.compile(r'new(?:\s*string)?\s*\[\s*\]\s*\{((?:[^{}]|"[^"]*")*?)\}')

def convert_grid_args(text: str) -> str:
    # Process collection-expressions [ "...", "..." ]
    def repl_coll(m: re.Match) -> str:
        inner = m.group(1)
        # Only attempt if the inner is purely string literals
        # (heuristic: starts with " or contains only whitespace/strings/commas)
        stripped = inner.strip()
        if not stripped:
            return m.group(0)
        # Quick check: every comma-split piece must be a string literal
        parts = [p.strip() for p in stripped.split(",") if p.strip()]
        if not all(p.startswith('"') and p.endswith('"') for p in parts):
            return m.group(0)
        converted = convert_token_list(inner)
        if converted is None:
            return m.group(0)
        return "[" + converted + "]"

    def repl_newarr(m: re.Match) -> str:
        inner = m.group(1)
        stripped = inner.strip()
        if not stripped:
            return m.group(0)
        parts = [p.strip() for p in stripped.split(",") if p.strip()]
        if not all(p.startswith('"') and p.endswith('"') for p in parts):
            return m.group(0)
        converted = convert_token_list(inner)
        if converted is None:
            return m.group(0)
        return "new[] { " + converted + " }"

    text = COLLECTION_RE.sub(repl_coll, text)
    text = NEW_ARR_RE.sub(repl_newarr, text)
    return text

# Only convert lines that are part of a Grid(...) call. Use a heuristic: scan
# for "Grid(" and rewrite the next two collection/array literals on the
# subsequent few lines.
def rewrite_file(path: Path) -> bool:
    text = path.read_text(encoding="utf-8")
    new_text = []
    lines = text.split("\n")
    i = 0
    changed = False
    while i < len(lines):
        line = lines[i]
        # Match a Grid(...) opening that's missing the "GridSize" hint
        if re.search(r'\bGrid\s*\(', line) and "GridSize" not in line and "GridDefinition" not in line:
            # Collect the next ~6 lines (until matching close paren via simple counter
            buf = [line]
            depth = line.count("(") - line.count(")")
            j = i + 1
            while depth > 0 and j < len(lines) and j - i < 12:
                buf.append(lines[j])
                depth += lines[j].count("(") - lines[j].count(")")
                j += 1
            joined = "\n".join(buf)
            converted = convert_grid_args(joined)
            if converted != joined:
                changed = True
                buf = converted.split("\n")
            new_text.extend(buf)
            i = j
        else:
            new_text.append(line)
            i += 1
    if changed:
        path.write_text("\n".join(new_text), encoding="utf-8")
    return changed

if __name__ == "__main__":
    files = [Path(p) for p in sys.argv[1:]]
    for f in files:
        if not f.exists():
            print(f"missing: {f}")
            continue
        if rewrite_file(f):
            print(f"updated: {f}")
        else:
            print(f"no-op:   {f}")
