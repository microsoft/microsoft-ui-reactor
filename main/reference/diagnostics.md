---
title: "Diagnostics"
description: "Tracing, source maps, component snapshots and developer tools hosting."
sidebar:
  label: "Overview"
---

**Learn more:** [Diagnostics](/microsoft-ui-reactor/main/tools/diagnostics), [Devtools](/microsoft-ui-reactor/main/tools/devtools)

## Types [#types]

| Type | Description |
| --- | --- |
| [Developer tools hosting](/microsoft-ui-reactor/main/reference/diagnostics/devtools) | Types the developer tools use to start inside an app. |
| [Diagnostic snapshots](/microsoft-ui-reactor/main/reference/diagnostics/snapshots) | Snapshots of the component tree that the diagnostics API returns. |
| [`ReactorEvent`](/microsoft-ui-reactor/main/reference/diagnostics/ReactorEvent) | A single event captured from the `Microsoft-UI-Reactor` `EventSource`, delivered to subscribers of `ReactorTrace.Subscribe`. |
| [`ReactorSourceMap`](/microsoft-ui-reactor/main/reference/diagnostics/ReactorSourceMap) | Spec 010 — runtime switch and read path for Reactor source mapping. |
| [`ReactorSourceTransparentAttribute`](/microsoft-ui-reactor/main/reference/diagnostics/ReactorSourceTransparentAttribute) | Spec 010 — marks a helper method as *source-transparent*: elements it returns are attributed to the line that CALLED it, not to the line inside it that ran the DSL factory. |
| [`ReactorTrace`](/microsoft-ui-reactor/main/reference/diagnostics/ReactorTrace) | In-process subscription helper for the `Microsoft-UI-Reactor` `EventSource`. |
