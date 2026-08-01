import { fileURLToPath, URL } from "node:url";
import vue from "@vitejs/plugin-vue";
import { defineConfig } from "vitest/config";

export default defineConfig({
  base: "/admin/",
  plugins: [vue()],
  publicDir: false,
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url))
    }
  },
  build: {
    outDir: "../wwwroot/admin",
    emptyOutDir: false,
    sourcemap: false,
    rollupOptions: {
      output: {
        entryFileNames: "assets/admin.js",
        chunkFileNames: "assets/chunk-[name].js",
        assetFileNames: "assets/admin[extname]"
      }
    }
  },
  test: {
    include: ["tests/**/*.test.ts"],
    environment: "happy-dom",
    setupFiles: ["./tests/setup.ts"]
  }
});
