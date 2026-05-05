// BlankRNW — startup-only baseline for React Native Windows.
//
// Mirrors microsoft-ui-xaml-lift/.../rnw-fabric-ttfp-tti-bench/App.tsx.
// Anchors a JS-side T0 in index.js (Date.now() before any imports), then:
//   - rAF callback         → JS TTFP
//   - requestIdleCallback  → JS TTI
//   - StartupTiming TM     → reports back into native, which fires
//                            FirstRender + FirstIdle ETW events on the
//                            shared BenchmarkSyntheticApps provider so this
//                            app shows up in the same WPR Regions panel as
//                            BlankWinUI3 / BlankReactor.
//
// On-screen banner shows the breakdown (process init, config, native→JS
// gap, JS TTFP, JS TTI, true TTFP, true TTI). For the harness, the ETW
// events are the source of truth.

import React from 'react';
import { NativeModules, StyleSheet, Text, View } from 'react-native';

const T0: number = (global as any).__PERF_T0 ?? Date.now();

interface NativeTimings {
  processInitMs: number;
  configMs: number;
  nativeElapsedMs: number;
}

interface Phases {
  processInitMs: number;
  configMs: number;
  nativeToJsMs: number;
  jsTtfpMs: number;
  jsToTtiMs: number;
  trueTtfpMs: number;
  trueTtiMs: number;
}

function useStartupPipeline() {
  const [ttfp, setTtfp] = React.useState<number | null>(null);
  const [tti, setTti] = React.useState<number | null>(null);
  const [phases, setPhases] = React.useState<Phases | null>(null);
  const ran = React.useRef(false);

  React.useEffect(() => {
    if (ran.current) return;
    ran.current = true;

    // Tell native that React has committed its first render. Native fires
    // ReactMounted on the BenchmarkSyntheticApps provider.
    try {
      NativeModules.StartupTiming.reportReactMounted();
    } catch {}

    requestAnimationFrame(() => {
      const ttfpMs = Date.now() - T0;
      setTtfp(ttfpMs);

      // Fire FirstRender now, inside rAF. Doing this from the idle
      // callback below would record the FirstRender ETW event with an
      // idle-time timestamp, which the harness's region math interprets
      // as TTFP — collapsing TTFP onto TTI in the reports.
      try {
        const nt1: NativeTimings = NativeModules.StartupTiming.getTimings();
        const nativeToJsAtTtfp = Math.max(0, nt1.nativeElapsedMs - ttfpMs);
        const trueTtfp = +(nativeToJsAtTtfp + ttfpMs).toFixed(1);
        NativeModules.StartupTiming.reportFirstRender(trueTtfp);
      } catch {}

      requestIdleCallback(() => {
        const ttiMs = Date.now() - T0;
        setTti(ttiMs);

        try {
          const nt: NativeTimings = NativeModules.StartupTiming.getTimings();
          const nativeToJsMs = Math.max(0, nt.nativeElapsedMs - ttiMs);
          const hermesGapMs = Math.max(0, nativeToJsMs - nt.processInitMs - nt.configMs);
          const trueTtfp = +(nativeToJsMs + ttfpMs).toFixed(1);
          const trueTti = +(nativeToJsMs + ttiMs).toFixed(1);
          const p: Phases = {
            processInitMs: +nt.processInitMs.toFixed(1),
            configMs: +nt.configMs.toFixed(1),
            nativeToJsMs: +hermesGapMs.toFixed(1),
            jsTtfpMs: ttfpMs,
            jsToTtiMs: ttiMs - ttfpMs,
            trueTtfpMs: trueTtfp,
            trueTtiMs: trueTti,
          };
          setPhases(p);
          NativeModules.StartupTiming.reportFirstIdle(trueTti);
        } catch {}
      });
    });
  }, []);

  return { ttfp, tti, phases };
}

function App() {
  const { ttfp, tti, phases } = useStartupPipeline();
  return (
    <View style={s.banner}>
      <Text style={s.title}>BlankRNW Startup</Text>
      {phases ? (
        <>
          <Text style={s.section}>NATIVE (QPC)</Text>
          <Row label="Process init" ms={phases.processInitMs} />
          <Row label="Config" ms={phases.configMs} />
          <Row label="Native->JS (Hermes+bundle)" ms={phases.nativeToJsMs} dim />
          <Text style={s.section}>JS</Text>
          <Row label="TTFP (T0->rAF)" ms={phases.jsTtfpMs} />
          <Row label="TTI  (rAF->idle)" ms={phases.jsToTtiMs} />
          <View style={s.hr} />
          <Row label="* TRUE TTFP" ms={phases.trueTtfpMs} hi />
          <Row label="* TRUE TTI" ms={phases.trueTtiMs} hi />
        </>
      ) : (
        <>
          <Row label="TTFP" ms={ttfp} />
          <Row label="TTI" ms={tti} />
        </>
      )}
    </View>
  );
}

function Row({ label, ms, hi, dim }: { label: string; ms: number | null; hi?: boolean; dim?: boolean }) {
  return (
    <Text style={[s.row, hi && s.hi, dim && s.dim]}>
      {label}: {ms !== null ? `${ms} ms` : '...'}
    </Text>
  );
}

const s = StyleSheet.create({
  banner: { flex: 1, backgroundColor: '#1a1a2e', paddingVertical: 12, paddingHorizontal: 20 },
  title: { color: '#e94560', fontSize: 16, fontWeight: 'bold', marginBottom: 6 },
  section: { color: '#888', fontSize: 10, fontWeight: '600', marginTop: 6, textTransform: 'uppercase', letterSpacing: 1 },
  row: { color: '#eee', fontSize: 13, fontFamily: 'Consolas', lineHeight: 20 },
  hi: { color: '#4ecca3', fontWeight: 'bold', fontSize: 14 },
  dim: { color: '#666', fontStyle: 'italic' },
  hr: { borderTopWidth: 1, borderTopColor: '#333', marginVertical: 6 },
});

export default App;
