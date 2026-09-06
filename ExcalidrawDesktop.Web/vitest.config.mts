import { createRequire } from "node:module";

import { defineConfig } from "vitest/config";

const require = createRequire(import.meta.url);
const excalidrawProductionEntry = require.resolve("@excalidraw/excalidraw");
const roughJsEntry = require.resolve("roughjs/bin/rough.js");

export default defineConfig({
  resolve: {
    alias: [
      {
        find: /^@excalidraw\/excalidraw$/,
        replacement: excalidrawProductionEntry,
      },
      {
        find: /^roughjs\/bin\/rough$/,
        replacement: roughJsEntry,
      },
    ],
  },
  test: {
    environment: "jsdom",
    include: ["src/**/*.test.ts", "src/**/*.test.tsx"],
    setupFiles: ["vitest-canvas-mock"],
    server: {
      deps: {
        inline: ["@excalidraw/excalidraw"],
      },
    },
  },
});
