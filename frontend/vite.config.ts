import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      "^/api/bookings(?:/receipt)?$": { target: "http://127.0.0.1:5082" },
      "/api": { target: "http://127.0.0.1:5081" },
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    restoreMocks: true,
    coverage: {
      provider: "v8",
      include: ["src/**/*.{ts,tsx}"],
      exclude: [
        "src/main.tsx",
        "src/test/**",
        "src/**/*.test.{ts,tsx}",
        "src/types.ts",
      ],
      reporter: ["text", "json-summary", "html", "lcov"],
      thresholds: { statements: 80, branches: 80, functions: 80, lines: 80 },
    },
  },
});
