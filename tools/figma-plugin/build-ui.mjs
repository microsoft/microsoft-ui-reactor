// Build script: copies ui.html to dist/ (TypeScript handles code.ts → dist/code.js)
import { copyFileSync, mkdirSync } from "fs";
import { dirname } from "path";
import { fileURLToPath } from "url";

const __dirname = dirname(fileURLToPath(import.meta.url));
mkdirSync(`${__dirname}/dist`, { recursive: true });
copyFileSync(`${__dirname}/src/ui.html`, `${__dirname}/dist/ui.html`);
console.log("Copied ui.html → dist/ui.html");
