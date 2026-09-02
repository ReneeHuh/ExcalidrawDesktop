import { cp, mkdir } from "node:fs/promises";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { fileURLToPath, URL } from "node:url";

import react from "@vitejs/plugin-react";
import { defineConfig, type Plugin } from "vite";

const webRoot = fileURLToPath(new URL(".", import.meta.url));
const require = createRequire(import.meta.url);
const excalidrawEntry = require.resolve("@excalidraw/excalidraw");
const fontSource = join(dirname(excalidrawEntry), "fonts");
const fontDestination = fileURLToPath(new URL("./dist/fonts", import.meta.url));

const copyExcalidrawFonts = (): Plugin => ({
  name: "copy-excalidraw-fonts",
  apply: "build",
  async closeBundle() {
    await mkdir(fontDestination, { recursive: true });
    await cp(fontSource, fontDestination, { recursive: true });
  },
});

export default defineConfig({
  root: webRoot,
  base: "/",
  plugins: [react(), copyExcalidrawFonts()],
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: false,
    rollupOptions: {
      output: {
        entryFileNames: "assets/entry-[hash].js",
        chunkFileNames: "assets/chunk-[hash].js",
        assetFileNames: "assets/asset-[hash][extname]",
      },
    },
  },
});
