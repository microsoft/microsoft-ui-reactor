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

const Cli = {
  headless: process.env.STRESSPERF_HEADLESS === '1',
  count: Number(process.env.STRESSPERF_COUNT ?? '5000'),
  durationSeconds: Number(process.env.STRESSPERF_DURATION ?? '5'),
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

export default function App() {
  const [count, setCount] = useState<number>(Cli.count);
  const items = useMemo(() => generate(count), [count]);

  const [fpsLabel, setFpsLabel] = useState('FPS: --');
  const [p50Label, setP50Label] = useState('P50: -- ms');
  const [p95Label, setP95Label] = useState('P95: -- ms');
  const [p99Label, setP99Label] = useState('P99: -- ms');
  const [memLabel, setMemLabel] = useState('Mem: -- MB');
  const [status, setStatus] = useState('idle');

  const perfRef = useRef<PerfTracker>(new PerfTracker());
  const listRef = useRef<FlatList<ListItem>>(null);
  const viewportHRef = useRef<number>(600);

  // Bench state for the tween. We want to drive scrollToOffset on every
  // animation frame to match the Reactor sibling exactly (which uses
  // ChangeView from CompositionTarget.Rendering). FlatList recycles cells
  // as we scroll past them, exercising the virtualizer.
  const benchActiveRef = useRef(false);
  const benchDurationMs = Cli.durationSeconds * 1000;
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

    // Print report — file write needs `react-native-fs` after the bootstrap.
    // For now, stdout via `npx react-native log-windows`.
    const report =
      `=== ${APP_NAME} ===\n` +
      `Count:       ${count}\n` +
      `Frames:      ${r.frames.length}\n` +
      `Avg dt:      ${r.avg.toFixed(2)} ms  (~${(1000 / r.avg).toFixed(1)} fps)\n` +
      `P50 dt:      ${r.p50.toFixed(2)} ms\n` +
      `P95 dt:      ${r.p95.toFixed(2)} ms\n` +
      `P99 dt:      ${r.p99.toFixed(2)} ms\n` +
      `Max dt:      ${r.max.toFixed(2)} ms\n`;
    // eslint-disable-next-line no-console
    console.log('REPORT_BEGIN\n' + report + 'REPORT_END');
    const csv = ['FrameIndex,DeltaMs', ...r.frames.map((f, i) => `${i},${f.toFixed(2)}`)].join('\n');
    // eslint-disable-next-line no-console
    console.log('FRAMES_BEGIN\n' + csv + '\nFRAMES_END');
  }, [count]);

  const startBenchmark = useCallback(() => {
    if (!listRef.current) return;
    listRef.current.scrollToOffset({ offset: 0, animated: false });
    maxOffsetRef.current = Math.max(0, ROW_HEIGHT * count - viewportHRef.current);
    perfRef.current.beginBenchmark();
    benchActiveRef.current = true;
    setStatus('running…');
  }, [count]);

  // Headless: kick off the benchmark right after first paint, then exit.
  useEffect(() => {
    if (!Cli.headless) return;
    const startHandle = setTimeout(startBenchmark, 250);
    // Quit slack: bench duration + 2s.
    const quitHandle = setTimeout(() => {
      // RN-Windows: the cleanest exit is to just close the window via a
      // native module. Simpler in practice: the test runner kills the
      // process after capturing log output.
    }, (Cli.durationSeconds + 2) * 1000);
    return () => {
      clearTimeout(startHandle);
      clearTimeout(quitHandle);
    };
  }, [startBenchmark]);

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
