// Mirror of tests/stress_perf/StressPerf.Shared/PerfTracker.cs. Tracks FPS
// via requestAnimationFrame, per-tick update time via begin/end stamps, and
// memory via the perf.memory polyfill where available (RN-Windows exposes
// the V8/Hermes used heap).

export class PerfTracker {
  private wallClockStart = performance.now();
  private updateStart = 0;
  private frameCount = 0;
  private lastSampleTime = 0;
  private currentFps = 0;
  private lastUpdateMs = 0;

  private readonly fpsSamples: number[] = [];
  private readonly memorySamples: number[] = [];
  private readonly updateTimeSamples: number[] = [];
  private renderCount = 0;

  // Hooks `requestAnimationFrame` so each composed frame increments the
  // counter. Returns the cancel handle.
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
      this.memorySamples.push(this.currentMemoryBytes());
      this.frameCount = 0;
      this.lastSampleTime = now;
    }
  }

  /**
   * Increment the render-count tally. Call once per root-component render
   * (App.tsx top-level body increments this) so the count compares against
   * Reactor's `Total Renders` line.
   */
  noteRender(): void {
    this.renderCount++;
  }

  get totalRenders(): number {
    return this.renderCount;
  }

  beginUpdate(): void {
    this.updateStart = performance.now();
  }

  endUpdate(): void {
    this.lastUpdateMs = performance.now() - this.updateStart;
    this.updateTimeSamples.push(this.lastUpdateMs);
  }

  get fps(): number {
    return this.currentFps;
  }
  get updateMs(): number {
    return this.lastUpdateMs;
  }
  get memoryMB(): number {
    return Math.round(this.currentMemoryBytes() / (1024 * 1024));
  }

  private currentMemoryBytes(): number {
    // performance.memory is a Chrome / Hermes / V8 extension. RN-Windows on
    // Hermes exposes `.usedJSHeapSize`. Fall back to 0 if absent so the field
    // doesn't blow up the report.
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
    ];
    if (this.updateTimeSamples.length > 0) {
      lines.push(`Total Renders: ${this.renderCount}`);
      lines.push(`Avg Update:  ${avg(this.updateTimeSamples).toFixed(1)} ms`);
      lines.push(`Max Update:  ${Math.max(...this.updateTimeSamples).toFixed(1)} ms`);
    }
    if (this.memorySamples.length > 0) {
      const toMB = (b: number) => b / (1024 * 1024);
      lines.push(`Avg Memory:  ${toMB(avg(this.memorySamples)).toFixed(1)} MB`);
      lines.push(`Peak Memory: ${toMB(Math.max(...this.memorySamples)).toFixed(1)} MB`);
    }
    return lines.join('\n') + '\n';
  }

  buildSamplesCsv(): string {
    const lines = ['Second,FPS,Memory_MB'];
    const n = Math.min(this.fpsSamples.length, this.memorySamples.length);
    for (let i = 0; i < n; i++) {
      const mb = (this.memorySamples[i] / (1024 * 1024)).toFixed(1);
      lines.push(`${i + 1},${this.fpsSamples[i].toFixed(2)},${mb}`);
    }
    return lines.join('\n') + '\n';
  }
}
