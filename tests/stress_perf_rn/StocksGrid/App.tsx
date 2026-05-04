// React Native port of tests/stress_perf/StressPerf.Reactor/Program.cs.
// Runs on react-native-windows so the rendering target is XAML/WinUI 3
// (the same target the C# variants paint into).
//
// Layout: 70 cols × 70 rows = 4,900 cells, 64×18 each, FontSize 8.
// Update loop: setInterval @ 33 ms; each tick mutates N% of cells (slider).
// Stats: FPS via requestAnimationFrame; mount time via beginMount-stamp
// before setSnapshot + recordMountCommit-on-useLayoutEffect (rAF after
// commit); JS heap via performance.memory (diagnostic only — RSS is
// captured by the harness externally).
//
// Match policy: behaviorally identical to the Reactor app — same data
// generation algorithm (StockDataSource.ts), same tick rate, same sample
// schedule.  PerfTracker writes the same flavor of report file.

import * as React from 'react';
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import {
  Button,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
// Slider was once part of react-native core but is now a native-only
// community package whose Windows backend (@react-native-community/slider's
// SliderWindows.vcxproj) trips a Windows App SDK transitive-dep lookup at
// build time. We don't need it for the benchmark — headless ignores
// interactive controls — so percent is exposed as preset buttons instead.

import {
  COLUMNS,
  ROWS,
  TOTAL_ITEMS,
  StockDataSource,
  formatCell,
} from './StockDataSource';
import { PerfTracker } from './PerfTracker';

const APP_NAME = 'StressPerf.RN.StocksGrid';

// CLI flags arrive as initial props from the C++ host (StocksGrid.cpp parses
// argv and routes them via ReactViewOptions.InitialProps). We do *not* use
// process.env here — RN bundles env-var references at compile time, so
// runtime values from the launching shell are invisible to JS.
type AppProps = {
  headless?: boolean;
  percent?: number;
  duration?: number;
};

const TICK_MS = 33;

const GREEN = '#008000';
const RED = '#FF0000';

// ── Cell renderer ───────────────────────────────────────────────────────────

// Memoized cell so when only N out of 4,900 items mutate per tick, only N
// cells re-render. The shallow-equal on (cell, posStyle) is what makes this
// comparable to the Reactor variant where the reconciler diffs per-cell text.
type Cell = { symbol: string; currentPrice: number; isUp: boolean };

const StockCell = React.memo(function StockCell({
  cell,
  posStyle,
}: {
  cell: Cell;
  posStyle: object;
}) {
  // posStyle is stable per cell index (precomputed once in App), and
  // cellUp/cellDown are stable across renders, so this composes two
  // stable references into a fresh array per *changed-cell* render only.
  return (
    <Text
      style={cell.isUp ? [posStyle, cellTextStyles.up] : [posStyle, cellTextStyles.down]}
      numberOfLines={1}>
      {cell.symbol} {cell.currentPrice.toFixed(2)}
    </Text>
  );
});

// Precomputed text styles — picked by isUp instead of an inline {color: ...}
// object that would allocate per render and defeat downstream caching.
const cellTextStyles = StyleSheet.create({
  up:   { color: '#008000', fontSize: 8, paddingHorizontal: 2, paddingVertical: 1 },
  down: { color: '#FF0000', fontSize: 8, paddingHorizontal: 2, paddingVertical: 1 },
});

// ── App ─────────────────────────────────────────────────────────────────────

export default function App(props: AppProps) {
  const headless = !!props.headless;
  const initialPercent = props.percent ?? 10;
  const durationSeconds = props.duration ?? 10;

  const sourceRef = useRef<StockDataSource | null>(null);
  if (sourceRef.current === null) sourceRef.current = new StockDataSource();
  const source = sourceRef.current;

  // We keep a frozen snapshot in state so React knows when to re-render.
  // Each tick we mutate the underlying array AND replace the snapshot,
  // matching the Reactor variant which also calls setData(src.Snapshot()).
  const [snapshot, setSnapshot] = useState<Cell[]>(() =>
    source.items.map(i => ({
      symbol: i.symbol,
      currentPrice: i.currentPrice,
      isUp: i.isUp,
    }))
  );
  const [percent, setPercent] = useState<number>(initialPercent);
  const [running, setRunning] = useState<boolean>(false);
  const [fpsLabel, setFpsLabel] = useState('FPS: --');
  const [mountLabel, setMountLabel] = useState('Mount: -- ms');
  const [memLabel, setMemLabel] = useState('JS Heap: -- MB');
  // Surfaces the final headless report in a single TextBlock so we can read
  // it back via UI Automation (WinUI .exe apps have no stdout by default).
  const [report, setReport] = useState<string>('');

  const perfRef = useRef<PerfTracker>(new PerfTracker());
  // Count each top-level render so we can compare to Reactor's
  // `Total Renders` line. On Reactor the UI thread serializes ticks so
  // renders ≈ visible frames; on RN-Fabric they don't, and the divergence
  // is itself part of what we're measuring.
  perfRef.current.noteRender();

  // Frame counter loop (FPS). Always on while the app is mounted, like the
  // C# variant which hooks CompositionTarget.Rendering at start-up.
  useEffect(() => {
    const stop = perfRef.current.startFrameLoop();
    return stop;
  }, []);

  // Update loop. Stamps T0 immediately before setSnapshot; the
  // useLayoutEffect below records a mount-time sample once React commits.
  // Bracketing setSnapshot with a synchronous begin/end span (the C# pattern)
  // would only measure JS dispatch — Fabric's commit pipeline is async.
  useEffect(() => {
    if (!running) return;
    const perf = perfRef.current;
    const handle = setInterval(() => {
      const changed = source.update(percent);
      perf.beginMount();
      setSnapshot(prev => {
        const next = prev.slice();
        for (const idx of changed) {
          const item = source.items[idx];
          next[idx] = {
            symbol: item.symbol,
            currentPrice: item.currentPrice,
            isUp: item.isUp,
          };
        }
        return next;
      });

      setFpsLabel(`FPS: ${perf.fps.toFixed(0)}`);
      setMountLabel(`Mount: ${perf.mountMs.toFixed(1)} ms`);
      setMemLabel(`JS Heap: ${perf.jsHeapMB} MB`);
    }, TICK_MS);
    return () => clearInterval(handle);
  }, [running, percent, source]);

  // Records a mount-time sample after each commit. useLayoutEffect runs
  // post-commit on the JS thread; the tracker schedules a single rAF inside
  // so the sample brackets through to the next display frame.
  useLayoutEffect(() => {
    perfRef.current.recordMountCommit();
  }, [snapshot]);

  // Headless auto-start mirrors the Reactor variant's CliOpts.Headless path.
  useEffect(() => {
    if (!headless) return;
    setPercent(initialPercent);
    setRunning(true);
    const quit = setTimeout(() => {
      setRunning(false);
      const r = perfRef.current.buildReport(APP_NAME, initialPercent);
      setReport('REPORT_BEGIN\n' + r + 'REPORT_END');
    }, durationSeconds * 1000);
    return () => clearTimeout(quit);
  }, [headless, initialPercent, durationSeconds]);

  // Precompute one position style per cell index. position:'absolute' with
  // explicit left/top mirrors Reactor's `Grid(row, column)` — both frameworks
  // do the same fixed-grid layout work without paying flexbox wrap cost.
  // Stable across renders so React.memo's prop comparison short-circuits.
  const cellPosStyles = useMemo(() => {
    const out = new Array<object>(TOTAL_ITEMS);
    for (let i = 0; i < TOTAL_ITEMS; i++) {
      const r = Math.floor(i / COLUMNS);
      const c = i % COLUMNS;
      out[i] = {
        position: 'absolute' as const,
        left: c * CELL_W,
        top: r * CELL_H,
        width: CELL_W,
        height: CELL_H,
      };
    }
    return out;
  }, []);

  // Build the grid as a flat list of absolutely-positioned cells. We render
  // all 4,900 cells (no virtualization) so we measure the same rendering
  // cost as the C# variant's full-grid mount. `key={i}` since positions are
  // stable.
  const cells = useMemo(() => {
    const out: React.ReactNode[] = new Array(TOTAL_ITEMS);
    for (let i = 0; i < TOTAL_ITEMS; i++) {
      out[i] = (
        <StockCell key={i} cell={snapshot[i]} posStyle={cellPosStyles[i]} />
      );
    }
    return out;
  }, [snapshot, cellPosStyles]);

  return (
    <View style={styles.root}>
      <View style={styles.toolbar}>
        <Button
          title={running ? 'Stop' : 'Start'}
          onPress={() => setRunning(r => !r)}
        />
        <Text style={styles.toolbarText}>{`Update %: ${percent}`}</Text>
        <Button title="10%" onPress={() => setPercent(10)} disabled={percent === 10} />
        <Button title="20%" onPress={() => setPercent(20)} disabled={percent === 20} />
        <Button title="50%" onPress={() => setPercent(50)} disabled={percent === 50} />
        <Button title="100%" onPress={() => setPercent(100)} disabled={percent === 100} />
        <Text style={[styles.toolbarText, styles.fixedW90]}>{fpsLabel}</Text>
        <Text style={[styles.toolbarText, styles.fixedW120]}>{mountLabel}</Text>
        <Text style={[styles.toolbarText, styles.fixedW120]}>{memLabel}</Text>
      </View>
      {!!report && (
        <Text testID="HeadlessReport" style={styles.report} selectable>
          {report}
        </Text>
      )}
      <ScrollView horizontal>
        <ScrollView>
          <View style={styles.grid}>{cells}</View>
        </ScrollView>
      </ScrollView>
    </View>
  );
}

const CELL_W = 64;
const CELL_H = 18;

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: '#FFFFFF' },
  toolbar: {
    flexDirection: 'row',
    alignItems: 'center',
    padding: 8,
    gap: 12,
  },
  toolbarText: { fontSize: 12 },
  fixedW90: { width: 90 },
  fixedW120: { width: 120 },
  report: { fontSize: 12, padding: 8, fontFamily: 'Consolas, monospace' },
  // Container for the absolutely-positioned cells. Explicit dimensions so
  // both ScrollViews know the content size (no flex layout pass over 4,900
  // children — that was the previous flex-wrap version's cost).
  grid: {
    width: CELL_W * COLUMNS,
    height: CELL_H * ROWS,
  },
});
