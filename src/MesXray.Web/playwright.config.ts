import { defineConfig } from "@playwright/test";
import path from "node:path";
import { fileURLToPath } from "node:url";

// End-to-end rehearsal of the demo (docs/demo-script.md): Playwright starts the X-Ray API (offline, fixtures only)
// and the Vite dev server on ports of their own, so a running `scripts/xray.sh` stack is never disturbed.
//
//   npm run e2e          the seven scenes in both languages, plus the language switch
//   npm run demo:shots   re-shoot docs/demo/*.png from the same scene definitions
//
// Locally the tests drive the Google Chrome that is already installed (PW_CHANNEL=chrome, nothing to download);
// CI installs Playwright's Chromium. XRAY_E2E_API_PORT / XRAY_E2E_WEB_PORT move the ports, XRAY_E2E_CONFIGURATION
// picks the .NET build configuration (Debug locally, Release in CI where the API is built beforehand).

const here = path.dirname(fileURLToPath(import.meta.url));
const CI = !!process.env.CI;
const API_PORT = Number(process.env.XRAY_E2E_API_PORT ?? 5090);
const WEB_PORT = Number(process.env.XRAY_E2E_WEB_PORT ?? 5199);
const API_URL = `http://localhost:${API_PORT}`;
const WEB_URL = `http://localhost:${WEB_PORT}`;
const configuration = process.env.XRAY_E2E_CONFIGURATION ?? (CI ? "Release" : "Debug");
const artifacts = path.resolve(here, "../../artifacts/e2e");

export default defineConfig({
  testDir: "./e2e",
  outputDir: path.join(artifacts, "test-results"),
  fullyParallel: true,
  forbidOnly: CI,
  retries: CI ? 1 : 0,
  workers: CI ? 2 : 3,
  timeout: 90_000,
  expect: { timeout: 20_000 },
  reporter: CI ? [["list"], ["html", { outputFolder: path.join(artifacts, "report"), open: "never" }]] : [["list"]],
  use: {
    baseURL: WEB_URL,
    viewport: { width: 1600, height: 1000 },
    channel: CI ? undefined : (process.env.PW_CHANNEL ?? "chrome"),
    trace: CI ? "retain-on-failure" : "off",
    locale: "en-US",
    timezoneId: "Asia/Shanghai",
  },
  projects: [
    { name: "scenes", testIgnore: /screenshots\.spec\.ts$/ },
    { name: "screenshots", testMatch: /screenshots\.spec\.ts$/ },
  ],
  webServer: [
    {
      command: `dotnet run --project ../MesXray.Api --configuration ${configuration} --no-launch-profile${CI ? " --no-build" : ""}`,
      url: `${API_URL}/api/xray/health`,
      timeout: 180_000,
      reuseExistingServer: !CI,
      // appsettings.json binds Kestrel through configuration, so the port is moved the same way scripts/xray.sh does it.
      env: { Kestrel__Endpoints__Http__Url: API_URL, ASPNETCORE_ENVIRONMENT: "Development", DOTNET_CLI_TELEMETRY_OPTOUT: "1", DOTNET_NOLOGO: "1" },
      stdout: "ignore",
      stderr: "pipe",
    },
    {
      command: `npx vite --port ${WEB_PORT} --strictPort`,
      url: `${WEB_URL}/`,
      timeout: 60_000,
      reuseExistingServer: !CI,
      env: { VITE_XRAY_API_URL: API_URL },
      stdout: "ignore",
      stderr: "pipe",
    },
  ],
});
