// Browser tests for the website, against every page as GitHub Pages would serve it.
const { test, expect } = require('@playwright/test');
const AxeBuilder = require('@axe-core/playwright').default;

const palette = {
  '--ink': '#080A09',
  '--panel': '#111512',
  '--raised': '#181E19',
  '--line': '#29302A',
  '--text': '#F2F5EE',
  '--muted': '#858D83',
  '--signal': '#D7FF3F',
  '--live': '#FF774D',
};

test.beforeEach(async ({ page }) => {
  // The release API is not part of the site; tests decide what it answers.
  await page.route('https://api.github.com/**', route => route.abort());
});

test('the home page loads without errors and names the product and the company', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('./');

  await expect(page).toHaveTitle(/rexplayer.*REX Technologies/);
  await expect(page.locator('.brand-company')).toHaveText('REX Technologies');
  await expect(page.locator('h1')).toHaveCount(1);
  expect(errors).toEqual([]);
});

test('the page uses the REX ink and signal palette', async ({ page }) => {
  await page.goto('./');
  const values = await page.evaluate(names => {
    const style = getComputedStyle(document.documentElement);
    return Object.fromEntries(names.map(name => [name, style.getPropertyValue(name).trim().toUpperCase()]));
  }, Object.keys(palette));

  expect(values).toEqual(palette);
  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(8, 10, 9)');
});

test('there are no serious accessibility problems', async ({ page }) => {
  await page.goto('./');
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const serious = results.violations.filter(v => v.impact === 'serious' || v.impact === 'critical');

  expect(serious.map(v => `${v.id}: ${v.help}`)).toEqual([]);
});

test('nothing scrolls sideways at phone, tablet or desktop widths', async ({ page }) => {
  for (const width of [320, 390, 1280]) {
    await page.setViewportSize({ width, height: 800 });
    await page.goto('./');
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, `at ${width}px`).toBeLessThanOrEqual(0);
  }
});

test('the download button works without the release API and is decorated with it', async ({ page }) => {
  await page.goto('./');
  await expect(page.locator('#download')).toHaveAttribute('href', 'https://github.com/tochi-mba/rexplayer/releases/latest');

  await page.unroute('https://api.github.com/**');
  await page.route('https://api.github.com/**', route => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({
      tag_name: 'v0.1.0',
      assets: [{ name: 'rexplayer-Setup-0.1.0.exe', size: 43000000, browser_download_url: 'https://github.com/tochi-mba/rexplayer/releases/download/v0.1.0/rexplayer-Setup-0.1.0.exe' }],
    }),
  }));
  await page.goto('./');

  await expect(page.locator('#download')).toHaveAttribute('download', 'rexplayer-Setup-0.1.0.exe');
  await expect(page.locator('#download-meta')).toHaveText('v0.1.0 · 41 MB · Windows 10 and 11, 64-bit');
});

test('a missing address shows the styled 404 page', async ({ page }) => {
  const response = await page.goto('./no/such/page');

  expect(response.status()).toBe(404);
  await expect(page).toHaveTitle(/Not found — rexplayer/);
  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(8, 10, 9)');
  await page.getByRole('link', { name: 'Back to rexplayer' }).click();
  await expect(page).toHaveTitle(/rexplayer — every file/);
});

test('every FAQ answer opens and the first is open already', async ({ page }) => {
  await page.goto('./');
  const items = page.locator('.faq details');

  await expect(items.first()).toHaveAttribute('open', '');
  for (const summary of await page.locator('.faq summary').all()) {
    await summary.click();
  }
  await expect(items.nth(1)).toHaveAttribute('open', '');
});

test('the mobile menu opens, closes from a link and closes with Escape', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'The menu button exists only at phone widths.');
  await page.goto('./');
  const button = page.locator('.menu-button');

  await button.click();
  await expect(button).toHaveAttribute('aria-expanded', 'true');
  await page.keyboard.press('Escape');
  await expect(button).toHaveAttribute('aria-expanded', 'false');
  await expect(button).toBeFocused();
  await button.click();
  await page.locator('.site-nav a').first().click();
  await expect(button).toHaveAttribute('aria-expanded', 'false');
});

test('the copy button puts the exact command on the clipboard', async ({ page, context, browserName }) => {
  test.skip(browserName !== 'chromium', 'Clipboard permissions are a Chromium feature.');
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await page.goto('./');

  await page.locator('.copy-button').click();

  await expect(page.locator('#copy-status')).toHaveText(/copied/i);
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe('rexplay play song.wav');
});
