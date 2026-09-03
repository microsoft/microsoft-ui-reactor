// `performance` is supplied by the React Native runtime (Hermes), but
// @react-native/typescript-config pins a curated `lib` list that has no DOM
// entry, so TypeScript cannot see the global and reports TS2304 on every
// `performance.now()` in PerfTracker.
//
// Only `now()` is declared: the `memory` reading is non-standard and already
// goes through an explicit `(performance as any).memory` cast.
declare const performance: {
  now(): number;
};
