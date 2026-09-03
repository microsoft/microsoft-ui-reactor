// React Native runtime globals that @react-native/typescript-config's curated
// `lib` list doesn't declare, so TypeScript reports TS2304 for them.
//
// `global` is only ever reached through an explicit `(global as any)` cast to
// read the `__PERF_T0` anchor that index.js sets before the first import.
declare const global: typeof globalThis;

declare function requestIdleCallback(
  callback: (deadline: {timeRemaining(): number; didTimeout: boolean}) => void,
  options?: {timeout: number},
): number;
