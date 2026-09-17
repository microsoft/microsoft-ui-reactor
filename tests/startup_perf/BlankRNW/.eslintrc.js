module.exports = {
  root: true,
  extends: '@react-native',
  // index.js anchors a wall-clock T0 on `globalThis` before any import. The
  // shared @react-native config doesn't enable an environment that declares
  // that ES2020 global, so no-undef flags it.
  env: {es2020: true},
};
