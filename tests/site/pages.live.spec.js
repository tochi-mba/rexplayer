// Runs only after the GitHub Pages deployment completes. It cannot use
// local snapshots, local static serving, or an earlier main-branch deployment.
const fs = require('node:fs');
const path = require('node:path');
const { test, expect } = require('@playwright/test');

test('deployed commit, native image assets and phone/desktop layout match the published revision', async ({ page }) => {
  const expected = process.env.EXPECTED_PAGES_SHA;
  expect(expected, 'The deployed revision must be an explicit GitHub commit').toMatch(/^[a-f0-9]{40}$/);

  // Pages deployment completion can precede CDN propagation. A cache-busting
  // source stamp proves that we do not mistake the previous main build for
  // the new PR content after it merges.
  await expect.poll(async () => {
    const response = await page.request.get(`./deployment.json?sha=${expected}`, {
      headers: { 'Cache-Control': 'no-cache' },
    });
    return response.ok() ? (await response.json()).commit : 'not yet deployed';
  }, { timeout: 90000, intervals: [1000, 2000, 5000] }).toBe(expected);

  const folder = path.join('artifacts', 'deployed-pages');
  fs.mkdirSync(folder, { recursive: true });

  for (const width of [320, 1280]) {
    await page.setViewportSize({ width, height: 800 });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto('./');
    await expect(page).toHaveTitle(/rexplayer.*REX Technologies/);
    await expect(page.locator('#screenshots')).toBeVisible();

    const images = page.locator('.showcase-card .shot-open img, .product-shot img, .real-product-row img');
    await expect(images).toHaveCount(8);
    for (const image of await images.all()) {
      await expect(image).toHaveAttribute('alt', /Windows/);
      await image.scrollIntoViewIfNeeded();
      await expect.poll(() => image.evaluate(el =>
        el.complete && el.naturalWidth >= 320 && el.naturalHeight >= 180)).toBeTruthy();
      const rect = await image.boundingBox();
      expect(rect, `image bounds at ${width}px`).not.toBeNull();
      expect(rect.width).toBeGreaterThan(100);
      expect(rect.x).toBeGreaterThanOrEqual(-1);
      expect(rect.x + rect.width).toBeLessThanOrEqual(width + 1);
    }

    const allSteps = page.locator('[data-tour-step]');
    await expect(allSteps).toHaveCount(6);
    for (const step of await allSteps.all()) {
      const source = await step.getAttribute('data-tour-src');
      const alt = await step.getAttribute('data-tour-alt');
      expect(alt?.trim()).toBeTruthy();
      await step.click();
      const image = page.locator('#tour-image');
      await expect(image).toHaveAttribute('src', source);
      await expect(image).toHaveAttribute('alt', alt);
      await expect.poll(() => image.evaluate(el =>
        el.complete && el.naturalWidth >= 320 && el.naturalHeight >= 180)).toBeTruthy();
    }

    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth
    ), `horizontal overflow at ${width}px`).toBeLessThanOrEqual(0);
    expect(errors).toEqual([]);

    // These are real deployed-page review captures, not native app release
    // screenshots. Preserve both widths in the workflow artifact for review.
    await page.screenshot({ path: path.join(folder, `pages-${width}.png`), fullPage: true });

    const preview = page.getByRole('link', { name: /View larger Windows.*screenshot/ }).first();
    await preview.click();
    await expect(page.locator('#shot-lightbox')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.locator('#shot-lightbox')).not.toBeVisible();
    await expect(preview).toBeFocused();
  }
});
