# Reactor Documentation

- **[guide/](guide/)** — User-facing documentation. Getting started, topic guides, screenshots.
- **[reference/](reference/)** — Architecture deep dives for framework contributors.
- **[specs/](specs/)** — Design specs, implementation tasks, and proposals.
- **[research/](research/)** — Competitive analysis, investigations, bug write-ups.
- **[reports/](reports/)** — Point-in-time reports (work summaries, metrics).
- **_pipeline/** — Internals of the doc-generation pipeline. Not reader-facing.

The `guide/` folder is pre-rendered because screenshot capture requires a running
WinUI host. To rebuild it locally, run `mur docs compile`.
