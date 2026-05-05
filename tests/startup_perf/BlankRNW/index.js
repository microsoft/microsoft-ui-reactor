/**
 * @format
 *
 * Anchor a wall-clock T0 *before* any imports so JS TTFP measures the full
 * JS-runtime cost of getting from "Hermes hands control to JS" → first paint.
 * Mirrors microsoft-ui-xaml-lift/.../rnw-fabric-ttfp-tti-bench/index.js.
 */

(globalThis).__PERF_T0 = Date.now();

import { AppRegistry } from 'react-native';
import App from './App';
import { name as appName } from './app.json';

AppRegistry.registerComponent(appName, () => App);
