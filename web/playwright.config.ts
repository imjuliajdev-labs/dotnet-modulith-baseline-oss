import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig, devices } from '@playwright/test';

const webRoot = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(webRoot, '..');
const apiHostUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'https://localhost:5078';
const starterDatabaseConnectionString = process.env.ConnectionStrings__BaselineDatabase
  ?? 'Host=127.0.0.1;Port=5432;Database=baseline;Username=postgres;Password=postgres';

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: false,
  workers: 1,
  reporter: 'line',
  use: {
    baseURL: apiHostUrl,
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure'
  },
  webServer: {
    command: `pwsh -NoLogo -File ./scripts/Start-ApiHost.ps1 -Configuration Release -NoBuild -ConnectionString "${starterDatabaseConnectionString}"`,
    cwd: repoRoot,
    ignoreHTTPSErrors: true,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: process.env.ASPNETCORE_ENVIRONMENT ?? 'Development',
      ASPNETCORE_URLS: apiHostUrl,
      ConnectionStrings__BaselineDatabase: starterDatabaseConnectionString,
      Modules__Identity__SeededAdmin__Password: process.env.Modules__Identity__SeededAdmin__Password ?? 'LocalOnly!123',
      Modules__Identity__SeededMachine__ApiKey: process.env.Modules__Identity__SeededMachine__ApiKey ?? 'MachineOnly!123'
    },
    reuseExistingServer: !process.env.CI,
    timeout: 240000,
    url: `${apiHostUrl}/health`
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    }
  ]
});