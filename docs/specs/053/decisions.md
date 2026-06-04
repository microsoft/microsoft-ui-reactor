# Spec 053 decisions

- §15 Q1 native asset trim: punt to Phase 4; Win2D's native runtime asset filtering needs WindowsAppSDK/Win2D validation and should not block Phase 1.
- §15 Q3 shared device: opt-in only for Phase 1; automatic sharing has ordering and lifetime subtleties that should be proven by the sample before becoming implicit behavior.
- §15 Q5 test host: use the existing `tests/Reactor.AppTests.Host`; the simpler CI topology is preferable and Win2D native binaries load only when a canvas is activated.
- Win2D version: pin `Microsoft.Graphics.Win2D` to 1.3.0; NuGet publishes 1.3.0 and the Win2D 1.3.0 release notes require/support `TargetPlatformMinVersion=10.0.17763.0` (Windows 10 1809).

## Phase 1B decisions

- `UseCanvasResources` uses the simpler explicit-ref shape for Phase 1B instead of implicit `Context<T>` propagation. Public Reactor context propagation is not part of the stable hook surface needed here, so the hook returns a `Ref<TResources?>` that authors pass/read explicitly near their canvas.
- `Reactor.Advanced` publishes as a trimmable library, not an executable trim root. `PublishTrimmed` is treated as a local property so the Phase 1 gate command succeeds without ILLink trying to find an entry point in a class library; `IsTrimmable`/`IsAotCompatible` keep trim analysis enabled.
