// Companion to tests/stress_perf/StressPerf.Shared/PerfTracker.cs, but with
// the metrics re-shaped to be honest about RN's async commit pipeline.
//
// Differences from the C# tracker:
//   • No Avg Update / Max Update.  In C# those bracket the *synchronous*
//     UI-thread tick.  In RN, setState returns immediately while
//     reconcile → Fabric → Yoga → Composition runs across other threads,
//     so a JS-side begin/end span only captures dispatch.
//   • Avg Mount / Max Mount instead — time from setState dispatch to the
//     next rAF after React's useLayoutEffect commit.  Pure-JS proxy: it
//     covers JS dispatch + reconcile + Fabric commit + one frame of wait.
//     True JS-to-pixel mount time needs an ETW hook (METHODOLOGY.md).
//   • No Avg Memory / Peak Memory in the human-readable report.  The only
//     in-process memory reading we have is performance.memory.usedJSHeapSize,
//     which excludes Hermes engine, JSI bridge, Fabric shadow tree, Yoga,
//     and TypeLayout caches.  The harness samples WorkingSet64 externally
//     and that's the figure we publish.  We still emit per-second JS heap
//     into the CSV — diagnostic only — under a JsHeap_MB column header.

export class PerfTracker {
  private wallClockStart = performance.now();
  private frameCount = 0;
  private lastSampleTime = 0;
  private currentFps = 0;

  private readonly fpsSamples: number[] = [];
  private readonly jsHeapSamples: number[] = [];
  private readonly mountTimeSamples: number[] = [];
  private renderCount = 0;

  // Pending dispatch timestamps. Each beginMount() pushes one; each
  // recordMountCommit() consumes the *oldest* (so the sample reflects
  // worst-case latency from first dispatch to commit) and clears the
  // rest (one batched commit reflects all queued dispatches under React
  // batching). An empty queue at commit time means no sample — avoids
  // attributing stale stamps to unrelated commits.
  private pendingMountStarts: number[] = [];
  private lastMountMs = 0;

  startFrameLoop(): () => void {
    let stopped = false;
    const tick = () => {
      if (stopped) return;
      this.frameRendered();
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
    return () => {
      stopped = true;
    };
  }

  frameRendered(): void {
    this.frameCount++;
    const now = (performance.now() - this.wallClockStart) / 1000;
    const elapsed = now - this.lastSampleTime;
    if (elapsed >= 1.0) {
      this.currentFps = this.frameCount / elapsed;
      this.fpsSamples.push(this.currentFps);
      this.jsHeapSamples.push(this.currentJsHeapBytes());
      this.frameCount = 0;
      this.lastSampleTime = now;
    }
  }

  noteRender(): void {
    this.renderCount++;
  }

  get totalRenders(): number {
    return this.renderCount;
  }

  /**
   * Stamp T0 immediately before dispatching setState. Pair with
   * recordMountCommit() from a useLayoutEffect on the same state.
   * Multiple calls before a commit (React batching under load) all
   * queue — recordMountCommit consumes them as one batch.
   */
  beginMount(): void {
    this.pendingMountStarts.push(performance.now());
  }

  /**
   * Call from useLayoutEffect on the dispatched state. Schedules a
   * single rAF — by the time it fires, Fabric has had a chance to mount
   * and at least one display frame has been scheduled. Records the
   * (rAF-now − T0_oldest) interval as a mount-time sample, where
   * T0_oldest is the earliest queued dispatch (so batched ticks measure
   * worst-case user-perceived latency, not the optimistic latest).
   * Empties the queue afterward so unrelated later commits don't
   * attribute to stale stamps.
   */
  recordMountCommit(): void {
    if (this.pendingMountStarts.length === 0) return;
    const start = this.pendingMountStarts[0];
    this.pendingMountStarts.length = 0;
    requestAnimationFrame(() => {
      const dt = performance.now() - start;
      this.mountTimeSamples.push(dt);
      this.lastMountMs = dt;
    });
  }

  get fps(): number {
    return this.currentFps;
  }
  get mountMs(): number {
    return this.lastMountMs;
  }
  get jsHeapMB(): number {
    return Math.round(this.currentJsHeapBytes() / (1024 * 1024));
  }

  private currentJsHeapBytes(): number {
    const m = (performance as any).memory;
    return m?.usedJSHeapSize ?? 0;
  }

  buildReport(appName: string, percent: number): string {
    const elapsedSec = (performance.now() - this.wallClockStart) / 1000;
    if (this.fpsSamples.length === 0) return 'No data collected.';
    const avg = (xs: number[]) => xs.reduce((a, b) => a + b, 0) / xs.length;
    const lines = [
      `=== ${appName} ===`,
      `Duration:    ${elapsedSec.toFixed(1)}s`,
      `Percent:     ${percent.toFixed(0)}%`,
      `Avg FPS:     ${avg(this.fpsSamples).toFixed(1)}`,
      `Min FPS:     ${Math.min(...this.fpsSamples).toFixed(1)}`,
      `Max FPS:     ${Math.max(...this.fpsSamples).toFixed(1)}`,
      `Total Renders: ${this.renderCount}`,
    ];
    if (this.mountTimeSamples.length > 0) {
      lines.push(`Avg Mount:   ${avg(this.mountTimeSamples).toFixed(1)} ms`);
      lines.push(`Max Mount:   ${Math.max(...this.mountTimeSamples).toFixed(1)} ms`);
    }
    return lines.join('\n') + '\n';
  }

  buildSamplesCsv(): string {
    const lines = ['Second,FPS,JsHeap_MB'];
    const n = Math.min(this.fpsSamples.length, this.jsHeapSamples.length);
    for (let i = 0; i < n; i++) {
      const mb = (this.jsHeapSamples[i] / (1024 * 1024)).toFixed(1);
      lines.push(`${i + 1},${this.fpsSamples[i].toFixed(2)},${mb}`);
    }
    return lines.join('\n') + '\n';
  }
}
