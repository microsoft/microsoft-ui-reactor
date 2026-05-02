// React Native virtualizing-list benchmark. Companion to
// tests/stress_perf/StressPerf.VirtualList.Reactor/Program.cs.
//
// Runs on react-native-windows so we paint into the same XAML/WinUI host as
// the Reactor variant. Renders a FlatList of N rows, each with avatar +
// 3-line text + likes pill (see SPEC.md). The "Run benchmark" button drives
// a deterministic 5-second linear scroll tween from offset 0 to bottom and
// records the per-frame delta-ms so we can report P50/P95/P99 against the
// Reactor sibling.

import * as React from 'react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Button,
  FlatList,
  StyleSheet,
  Text,
  View,
  ListRenderItemInfo,
} from 'react-native';

import {
  ROW_HEIGHT,
  AVATAR_SIZE,
  generate,
  hslToHex,
  type ListItem,
} from './ListItemSource';
import { PerfTracker } from './PerfTracker';

const APP_NAME = 'StressPerf.RN.VirtualList';

// CLI flags arrive as initial props from the C++ host (VirtualList.cpp parses
// argv and routes them via ReactViewOptions.InitialProps). We do *not* use
// process.env here — RN bundles env-var references at compile time, so
// runtime values from the launching shell are invisible to JS.
type AppProps = {
  headless?: boolean;
  count?: number;
  duration?: number;
};

// ── Row renderer ────────────────────────────────────────────────────────────

const Row = React.memo(function Row({ item, index }: { item: ListItem; index: number }) {
  const bg = (index & 1) === 0 ? '#FFFFFF' : '#F5F5F5';
  const avatarBg = useMemo(() => hslToHex(item.avatarHue, 0.55, 0.45), [item.avatarHue]);
  return (
    <View style={[styles.row, { backgroundColor: bg }]}>
      <View style={[styles.avatar, { backgroundColor: avatarBg }]}>
        <Text style={styles.avatarText}>{item.initial}</Text>
      </View>
      <View style={styles.center}>
        <Text style={styles.line1} numberOfLines={1}>
          {item.name} • {item.category}
        </Text>
        <Text style={styles.line2} numberOfLines={1}>
          {item.message}
        </Text>
        <Text style={styles.line3} numberOfLines={1}>
          {item.timestamp} • #{item.tag}
        </Text>
      </View>
      <View style={styles.pill}>
        <Text style={styles.pillText}>♥ {item.likes}</Text>
      </View>
    </View>
  );
});

// ── App ─────────────────────────────────────────────────────────────────────

export default function App(props: AppProps) {
  const headless = !!props.headless;
  const initialCount = props.count ?? 5000;
  const durationSeconds = props.duration ?? 5;

  const [count, setCount] = useState<number>(initialCount);
  const items = useMemo(() => generate(count), [count]);

  const [fpsLabel, setFpsLabel] = useState('FPS: --');
  const [p50Label, setP50Label] = useState('P50: -- ms');
  const [p95Label, setP95Label] = useState('P95: -- ms');
  const [p99Label, setP99Label] = useState('P99: -- ms');
  const [memLabel, setMemLabel] = useState('Mem: -- MB');
  const [status, setStatus] = useState('idle');
  // Final headless report rendered on-screen so the harness can scrape it
  // via UI Automation. Mirrors StocksGrid/App.tsx — WinUI .exe apps don't
  // have stdout, so an on-screen TextBlock is the most reliable channel.
  const [report, setReport] = useState<string>('');

  const perfRef = useRef<PerfTracker>(new PerfTracker());
  const listRef = useRef<FlatList<ListItem>>(null);
  const viewportHRef = useRef<number>(600);

  // Bench state for the tween. We want to drive scrollToOffset on every
  // animation frame to match the Reactor sibling exactly (which uses
  // ChangeView from CompositionTarget.Rendering). FlatList recycles cells
  // as we scroll past them, exercising the virtualizer.
  const benchActiveRef = useRef(false);
  const benchDurationMs = durationSeconds * 1000;
  const maxOffsetRef = useRef(0);

  // FPS frame loop, always on; this also drives the bench tween when active.
  useEffect(() => {
    const stop = perfRef.current.startFrameLoop(() => {
      if (!benchActiveRef.current) return;
      const elapsed = perfRef.current.benchElapsedMs();
      const t = Math.min(1, elapsed / benchDurationMs);
      const offset = maxOffsetRef.current * t;
      listRef.current?.scrollToOffset({ offset, animated: false });
      if (t >= 1) {
        finishBenchmark();
      }
    });
    return stop;
  }, []);

  const finishBenchmark = useCallback(() => {
    benchActiveRef.current = false;
    const r = perfRef.current.finishBenchmark();
    if (r.frames.length === 0) {
      setStatus('no frames captured');
      return;
    }
    setP50Label(`P50: ${r.p50.toFixed(1)} ms`);
    setP95Label(`P95: ${r.p95.toFixed(1)} ms`);
    setP99Label(`P99: ${r.p99.toFixed(1)} ms`);
    setFpsLabel(`FPS: ${perfRef.current.fps.toFixed(0)}`);
    setMemLabel(`Mem: ${perfRef.current.memoryMB} MB`);
    setStatus(`done (${r.frames.length} frames)`);

    // Surface the report on-screen so the harness can scrape it via UIA.
    const reportText =
      `=== ${APP_NAME} ===\n` +
      `Count:       ${count}\n` +
      `Frames:      ${r.frames.length}\n` +
      `Avg dt:      ${r.avg.toFixed(2)} ms  (~${(1000 / r.avg).toFixed(1)} fps)\n` +
      `P50 dt:      ${r.p50.toFixed(2)} ms\n` +
      `P95 dt:      ${r.p95.toFixed(2)} ms\n` +
      `P99 dt:      ${r.p99.toFixed(2)} ms\n` +
      `Max dt:      ${r.max.toFixed(2)} ms\n`;
    setReport('REPORT_BEGIN\n' + reportText + 'REPORT_END');
  }, [count]);

  const startBenchmark = useCallback(() => {
    if (!listRef.current) return;
    listRef.current.scrollToOffset({ offset: 0, animated: false });
    maxOffsetRef.current = Math.max(0, ROW_HEIGHT * count - viewportHRef.current);
    perfRef.current.beginBenchmark();
    benchActiveRef.current = true;
    setStatus('running…');
  }, [count]);

  // Headless: kick off the benchmark right after first paint. The harness
  // kills the process after the duration + slack window — see
  // tests/stress_perf/run_stocks_grid_baseline.ps1.
  useEffect(() => {
    if (!headless) return;
    const startHandle = setTimeout(startBenchmark, 250);
    return () => clearTimeout(startHandle);
  }, [headless, startBenchmark]);

  const renderItem = useCallback(
    ({ item, index }: ListRenderItemInfo<ListItem>) => <Row item={item} index={index} />,
    []
  );
  const keyExtractor = useCallback((item: ListItem) => String(item.id), []);
  const getItemLayout = useCallback(
    (_: any, index: number) => ({ length: ROW_HEIGHT, offset: ROW_HEIGHT * index, index }),
    []
  );

  return (
    <View style={styles.root}>
      <View style={styles.toolbar}>
        <Button title="1k" onPress={() => setCount(1000)} disabled={count === 1000} />
        <Button title="5k" onPress={() => setCount(5000)} disabled={count === 5000} />
        <Button title="10k" onPress={() => setCount(10000)} disabled={count === 10000} />
        <Button title="Run benchmark" onPress={startBenchmark} />
        <Text style={[styles.toolbarText, { width: 90 }]}>{fpsLabel}</Text>
        <Text style={[styles.toolbarText, { width: 110 }]}>{p50Label}</Text>
        <Text style={[styles.toolbarText, { width: 110 }]}>{p95Label}</Text>
        <Text style={[styles.toolbarText, { width: 110 }]}>{p99Label}</Text>
        <Text style={[styles.toolbarText, { width: 110 }]}>{memLabel}</Text>
        <Text style={[styles.toolbarText, styles.dim]}>{status}</Text>
      </View>
      {!!report && (
        <Text testID="HeadlessReport" style={styles.report} selectable>
          {report}
        </Text>
      )}
      <FlatList
        ref={listRef}
        data={items}
        renderItem={renderItem}
        keyExtractor={keyExtractor}
        getItemLayout={getItemLayout}
        // FlatList recycles cells via windowSize and removeClippedSubviews;
        // these are the modern defaults but we set them explicitly so the
        // Reactor comparison is apples-to-apples.
        windowSize={5}
        initialNumToRender={20}
        maxToRenderPerBatch={20}
        removeClippedSubviews={true}
        onLayout={e => {
          viewportHRef.current = e.nativeEvent.layout.height;
        }}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: '#FFFFFF' },
  toolbar: {
    flexDirection: 'row',
    alignItems: 'center',
    padding: 8,
    gap: 8,
  },
  toolbarText: { fontSize: 12 },
  dim: { color: '#6E6E6E', flex: 1 },
  report: { fontSize: 12, padding: 8, fontFamily: 'Consolas, monospace' },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    height: ROW_HEIGHT,
    paddingHorizontal: 12,
    paddingVertical: 8,
  },
  avatar: {
    width: AVATAR_SIZE,
    height: AVATAR_SIZE,
    borderRadius: 6,
    alignItems: 'center',
    justifyContent: 'center',
    marginRight: 12,
  },
  avatarText: { color: '#FFFFFF', fontSize: 18, fontWeight: '600' },
  center: { flex: 1, justifyContent: 'center' },
  line1: { fontSize: 14, fontWeight: '600' },
  line2: { fontSize: 14 },
  line3: { fontSize: 12, color: '#6E6E6E' },
  pill: {
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: 10,
    backgroundColor: '#F0F0F0',
    marginLeft: 12,
  },
  pillText: { fontSize: 12 },
});
