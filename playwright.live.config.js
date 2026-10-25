// Unlike the local Playwright config, this contacts the real published Pages URL.
// No webServer fallback is allowed: a localhost pass cannot satisfy deployment gates.
const { defineConfig, devices } = require('@playwright/test');

module.exports = defineConfig({
  testDir: './tests/site',
  testMatch: 'pages.live.spec.js',
  timeout: 120000,
  expect: { timeout: 12000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['line'], ['html', { outputFolder: 'playwright-report-live', open: 'never' }]],
  use: {
    baseURL: process.env.PAGES_URL || 'https://tochi-mba.github.io/rexplayer/',
    ...devices['Desktop Chrome'],
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
});
