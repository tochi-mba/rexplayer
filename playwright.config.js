// Browser tests for the website in site/, served the way GitHub Pages serves it.
const { defineConfig, devices } = require('@playwright/test');

module.exports = defineConfig({
  testDir: './tests/site',
  testMatch: 'pages.spec.js',
  timeout: 30000,
  expect: { timeout: 5000 },
  fullyParallel: true,
  // Two browsers at a time: more starves a laptop and turns timing into flakiness.
  workers: 2,
  retries: 0,
  reporter: process.env.CI ? [['line'], ['html', { outputFolder: 'playwright-report', open: 'never' }]] : 'list',
  use: {
    baseURL: 'http://127.0.0.1:4791/rexplayer/',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop-chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'mobile-chromium', use: { ...devices['Pixel 7'] } },
  ],
  webServer: {
    command: `${process.platform === 'win32' ? 'python' : 'python3'} tests/site/serve.py 4791`,
    url: 'http://127.0.0.1:4791/rexplayer/',
    reuseExistingServer: !process.env.CI,
    timeout: 20000,
  },
});
