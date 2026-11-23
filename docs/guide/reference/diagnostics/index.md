# Diagnostics

Tracing, source maps, component snapshots and developer tools hosting.

**Learn more:** [Diagnostics](../../diagnostics.md)

## Types

| Type | Description |
| --- | --- |
| [Developer tools hosting](devtools.md) | Types the developer tools use to start inside an app. |
| [Diagnostic snapshots](snapshots.md) | Snapshots of the component tree that the diagnostics API returns. |
| [`ReactorEvent`](ReactorEvent.md) | A single event captured from the `Microsoft-UI-Reactor` `EventSource`, delivered to subscribers of `ReactorTrace.Subscribe`. |
| [`ReactorSourceMap`](ReactorSourceMap.md) | Spec 010 — runtime switch and read path for Reactor source mapping. |
| [`ReactorSourceTransparentAttribute`](ReactorSourceTransparentAttribute.md) | Spec 010 — marks a helper method as *source-transparent*: elements it returns are attributed to the line that CALLED it, not to the line inside it that ran the DSL factory. |
| [`ReactorTrace`](ReactorTrace.md) | In-process subscription helper for the `Microsoft-UI-Reactor` `EventSource`. |
