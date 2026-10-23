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

test('the hero and library show genuine labeled native screenshots', async ({ page }) => {
  await page.goto('./');
  const images = page.locator('.product-shot img, .real-product-row img');
  await expect(images).toHaveCount(2);
  await expect(page.locator('.product-shot figcaption')).toContainText('Windows native library test capture');
  await expect(page.locator('#platforms')).toContainText('Windows and Mac');
  for (const image of await images.all()) {
    await expect(image).toHaveAttribute('src', /^assets\/windows-(movie-card|episode)-test-scene\.png$/);
    await expect(image).toHaveAttribute('alt', /Windows native|Actual Windows/);
    await image.scrollIntoViewIfNeeded();
    await expect(image).toBeVisible();
    await expect.poll(() => image.evaluate(el => el.naturalWidth)).toBeGreaterThan(0);
  }
});

test('the product does not suggest every Mac capability is already available', async ({ page }) => {
  await page.goto('./');
  await expect(page.locator('#platforms')).toContainText('Windows interface currently supports more');
  await expect(page.locator('#platforms a')).toHaveAttribute('href', /docs\/parity\.md$/);
});

test('native screenshot previews open a labeled modal and restore keyboard focus', async ({ page }) => {
  await page.goto('./');
  const previews = page.getByRole('link', { name: /View larger Windows.*screenshot/ });
  await expect(previews).toHaveCount(2);
  await previews.first().click();
  const lightbox = page.locator('#shot-lightbox');
  await expect(lightbox).toBeVisible();
  await expect(lightbox.getByRole('img')).toHaveAttribute('alt', /Windows library test scene/);
  await expect(page.locator('#shot-caption')).toContainText('Windows · Movie artwork — Native library regression test');
  await expect(lightbox.getByRole('button', { name: /Close/ })).toHaveCSS('white-space', 'nowrap');
  await lightbox.getByRole('button', { name: /Close/ }).click();
  await expect(lightbox).not.toBeVisible();
  await expect(previews.first()).toBeFocused();
  await previews.nth(1).click();
  await expect(lightbox).toBeVisible();
  await expect(lightbox.getByRole('img')).toHaveAttribute('alt', /Windows native TV episode test scene/);
  await page.keyboard.press('Escape');
  await expect(lightbox).not.toBeVisible();
  await expect(previews.nth(1)).toBeFocused();
});

test('the screenshots section communicates actual provenance and limitations', async ({ page }) => {
  await page.goto('./');
  await expect(page.locator('#screenshots')).toContainText('native UI regression tests');
  await expect(page.locator('#screenshots')).toContainText('Windows');
  await expect(page.locator('#screenshots')).toContainText('macOS');
  await expect(page.locator('#screenshots a[href$="parity.md"]')).toHaveAttribute('href', /docs\/parity\.md$/);
  await expect(page.locator('.site-nav a[href="#screenshots"]')).toHaveText('Screenshots');
});


test('full-size image links work when JavaScript is disabled', async ({ page, browser }) => {
  await page.goto('./');
  const context = await browser.newContext({ javaScriptEnabled: false });
  try {
    const withoutScript = await context.newPage();
    await withoutScript.goto(page.url());
    const first = withoutScript.getByRole('link', { name: /View larger Windows movie card screenshot/ });
    await expect(first).toBeVisible();
    await first.click();
    await expect(withoutScript).toHaveURL(/assets\/windows-movie-card-test-scene\.png$/);
  } finally {
    await context.close();
  }
});

test('gallery image transitions respect reduced-motion settings', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('./');
  await expect(page.locator('.shot-open img').first()).toHaveCSS('transition-duration', '0s');
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
  await expect(page.locator('a.brand')).toHaveAccessibleName(/REX Technologies\s*rexplayer/);
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const serious = results.violations.filter(v => v.impact === 'serious' || v.impact === 'critical');

  expect(serious.map(v => `${v.id}: ${v.help}`)).toEqual([]);
});

test('nothing scrolls sideways at phone, tablet or desktop widths', async ({ page }) => {
  for (const width of [320, 390, 760, 1280]) {
    await page.setViewportSize({ width, height: 800 });
    await page.goto('./');
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, `at ${width}px`).toBeLessThanOrEqual(0);
  }
});

test('Windows receives its installer and release details', async ({ page }) => {
  await page.addInitScript(() => Object.defineProperty(navigator, 'userAgentData', { configurable: true, value: { platform: 'Windows' } }));
  await page.goto('./');
  await expect(page.locator('#download')).toHaveAttribute('href', 'https://github.com/tochi-mba/rexplayer/releases/latest/download/rexplayer-Setup.exe');

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
  await expect(page.locator('#download-meta')).toHaveText('v0.1.0 · 41 MB · Windows 10 and 11 · 64-bit');
});

test('a Mac receives the universal app and can still choose an architecture build', async ({ page }) => {
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'userAgentData', { configurable: true, value: undefined });
    Object.defineProperty(navigator, 'platform', { configurable: true, value: 'MacIntel' });
  });
  await page.unroute('https://api.github.com/**');
  await page.route('https://api.github.com/**', route => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({
      tag_name: 'v0.30.3',
      assets: [
        { name: 'rexplayer-mac-universal.zip', size: 61000000, browser_download_url: 'https://github.com/tochi-mba/rexplayer/releases/download/v0.30.3/rexplayer-mac-universal.zip' },
        { name: 'rexplayer-mac-osx-arm64.zip', size: 39000000, browser_download_url: 'https://github.com/tochi-mba/rexplayer/releases/download/v0.30.3/rexplayer-mac-osx-arm64.zip' },
        { name: 'rexplayer-mac-osx-x64.zip', size: 41000000, browser_download_url: 'https://github.com/tochi-mba/rexplayer/releases/download/v0.30.3/rexplayer-mac-osx-x64.zip' },
      ],
    }),
  }));
  await page.goto('./');

  await expect(page.locator('#download')).toHaveAttribute('download', 'rexplayer-mac-universal.zip');
  await expect(page.locator('#download')).toHaveText('Download for Mac');
  await expect(page.locator('#download-meta')).toContainText('Apple silicon and Intel');
  await expect(page.getByRole('link', { name: 'Apple silicon', exact: true })).toHaveAttribute('href', /osx-arm64\.zip$/);
  await expect(page.getByRole('link', { name: 'Intel Mac', exact: true })).toHaveAttribute('href', /osx-x64\.zip$/);
});

test('an unknown operating system is asked to choose instead of receiving an incompatible file', async ({ page }) => {
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'userAgentData', { configurable: true, value: undefined });
    Object.defineProperty(navigator, 'platform', { configurable: true, value: 'Linux x86_64' });
  });
  await page.goto('./');
  await expect(page.locator('#download')).toHaveText('Choose your download');
  await expect(page.locator('#download')).toHaveAttribute('href', '#downloads');
  await expect(page.locator('#download-chooser')).toBeVisible();
});

test('a missing address shows the styled 404 page', async ({ page }) => {
  const response = await page.goto('./no/such/page');

  expect(response.status()).toBe(404);
  await expect(page).toHaveTitle(/Not found - rexplayer/);
  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(8, 10, 9)');
  await page.getByRole('link', { name: 'Back to rexplayer' }).click();
  await expect(page).toHaveTitle(/rexplayer.*your media/);
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
