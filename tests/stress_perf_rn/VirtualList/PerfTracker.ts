// Frame-time tracker for the virtualizing-list benchmark. Counts FPS via
// requestAnimationFrame and records per-frame deltas during a benchmark run
// so we can report P50/P95/P99 like the Reactor sibling.

export class PerfTracker {
  private wallClockStart = performance.now();
  private frameCount = 0;
  private lastSampleTime = 0;
  private currentFps = 0;

  private readonly fpsSamples: number[] = [];
  private readonly memorySamples: number[] = [];

  // Set by start/finishBenchmark.
  private benchActive = false;
  private benchStart = 0;
  private lastFrameAt = 0;
  private benchFrames: number[] = [];

  startFrameLoop(onTick?: () => void): () => void {
    let stopped = false;
    const tick = () => {
      if (stopped) return;
      this.frameRendered();
      onTick?.();
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
    return () => {
      stopped = true;
    };
  }

  frameRendered(): void {
    const now = performance.now();
    this.frameCount++;
    const elapsed = (now - this.wallClockStart) / 1000 - this.lastSampleTime;
    if (elapsed >= 1.0) {
      this.currentFps = this.frameCount / elapsed;
      this.fpsSamples.push(this.currentFps);
      this.memorySamples.push(this.currentMemoryBytes());
      this.frameCount = 0;
      this.lastSampleTime = (now - this.wallClockStart) / 1000;
    }
    if (this.benchActive) {
      if (this.lastFrameAt !== 0) {
        this.benchFrames.push(now - this.lastFrameAt);
      }
      this.lastFrameAt = now;
    }
  }

  beginBenchmark(): void {
    this.benchActive = true;
    this.benchStart = performance.now();
    this.lastFrameAt = 0;
    this.benchFrames = [];
  }

  /** Returns elapsed ms since beginBenchmark(). */
  benchElapsedMs(): number {
    return performance.now() - this.benchStart;
  }

  finishBenchmark(): { p50: number; p95: number; p99: number; avg: number; max: number; frames: number[] } {
    this.benchActive = false;
    const sorted = this.benchFrames.slice().sort((a, b) => a - b);
    if (sorted.length === 0) return { p50: 0, p95: 0, p99: 0, avg: 0, max: 0, frames: [] };
    const at = (q: number) => sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * q))];
    const sum = sorted.reduce((a, b) => a + b, 0);
    return {
      p50: at(0.5),
      p95: at(0.95),
      p99: at(0.99),
      avg: sum / sorted.length,
      max: sorted[sorted.length - 1],
      frames: sorted,
    };
  }

  get fps(): number {
    return this.currentFps;
  }

  get memoryMB(): number {
    return Math.round(this.currentMemoryBytes() / (1024 * 1024));
  }

  private currentMemoryBytes(): number {
    const m = (performance as any).memory;
    return m?.usedJSHeapSize ?? 0;
  }
}
