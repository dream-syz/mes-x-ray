import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

// The UI never talks to a database or an AI provider directly: everything goes through the X-Ray API,
// which enforces the read-only tool whitelist and evidence binding. In development the API runs on :5080.
// Override with VITE_XRAY_API_URL in .env.local (never commit real hosts; see .env.example).
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, ".", "VITE_");
  const target = env.VITE_XRAY_API_URL || "http://localhost:5080";
  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        "/api": { target, changeOrigin: true },
        "/openapi": { target, changeOrigin: true },
      },
    },
    build: {
      // All build output of the repository lives under /artifacts (see Directory.Build.props for the .NET side).
      outDir: "../../artifacts/web",
      emptyOutDir: true,
      sourcemap: false,
    },
  };
});
