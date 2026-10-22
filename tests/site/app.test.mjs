// Tests for site/app.js against a minimal stand-in document. Run with: node --test tests/site/app.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  RELEASE_API, findInstaller, findDownload, formatSize, describeRelease, fetchRelease,
  platformFromNavigator, macKindFromArchitecture, detectDownload, showDownloadChoice,
  decorateDownload, wireMenu, wireCopyButtons, start,
} from '../../site/app.js';

class FakeElement {
  constructor(attributes = {}) {
    this.attributes = { ...attributes };
    this.listeners = {};
    this.textContent = attributes.text ?? '';
    this.dataset = attributes.dataset ?? {};
    this.classes = new Set();
    this.children = [];
    this.focused = false;
    this.classList = { toggle: (name, on) => (on ? this.classes.add(name) : this.classes.delete(name)) };
  }

  getAttribute(name) { return this.attributes[name] ?? null; }

  setAttribute(name, value) { this.attributes[name] = value; }

  addEventListener(type, handler) { (this.listeners[type] ??= []).push(handler); }

  async fire(type, event = {}) { for (const handler of this.listeners[type] ?? []) await handler(event); }

  focus() { this.focused = true; }

  querySelectorAll() { return this.children; }
}

class FakeDocument extends FakeElement {
  constructor() {
    super();
    this.byId = {};
    this.bySelector = {};
  }

  getElementById(id) { return this.byId[id] ?? null; }

  querySelector(selector) { return (this.bySelector[selector] ?? [])[0] ?? null; }

  querySelectorAll(selector) { return this.bySelector[selector] ?? []; }
}

const release = {
  tag_name: 'v0.1.0',
  assets: [
    { name: 'rexplayer-0.1.0-win-x64.zip', size: 1, browser_download_url: 'https://github.com/tochi-mba/rexplayer/releases/download/v0.1.0/rexplayer-0.1.0-win-x64.zip' },
    { name: 'rexplayer-Setup-0.1.0.exe', size: 43_000_000, browser_download_url: 'https://github.com/tochi-mba/rexplayer/releases/download/v0.1.0/rexplayer-Setup-0.1.0.exe' },
    { name: 'rexplayer-mac-universal.zip', size: 61_000_000, browser_download_url: 'https://github.com/tochi-mba/rexplayer/releases/download/v0.1.0/rexplayer-mac-universal.zip' },
    { name: 'rexplayer-mac-osx-arm64.zip', size: 39_000_000, browser_download_url: 'https://github.com/tochi-mba/rexplayer/releases/download/v0.1.0/rexplayer-mac-osx-arm64.zip' },
    { name: 'rexplayer-mac-osx-x64.zip', size: 41_000_000, browser_download_url: 'https://github.com/tochi-mba/rexplayer/releases/download/v0.1.0/rexplayer-mac-osx-x64.zip' },
  ],
};

const respond = (body, ok = true) => async () => ({ ok, json: async () => body });

test('the installer is found by name and only when this repository hosts it', () => {
  assert.equal(findInstaller(release).name, 'rexplayer-Setup-0.1.0.exe');
  assert.equal(findInstaller({ assets: [{ name: 'rexplayer-Setup-1.exe', browser_download_url: 'https://evil.example/x.exe' }] }), null);
  assert.equal(findInstaller({}), null);
  assert.equal(findInstaller(null), null);
  assert.equal(findInstaller({ assets: [{}] }), null);
  assert.equal(findDownload(release, 'macUniversal').name, 'rexplayer-mac-universal.zip');
  assert.equal(findDownload(release, 'macArm64').name, 'rexplayer-mac-osx-arm64.zip');
  assert.equal(findDownload(release, 'macX64').name, 'rexplayer-mac-osx-x64.zip');
  assert.equal(findDownload(release, 'unknown'), null);
});

test('sizes and descriptions read naturally', () => {
  assert.equal(formatSize(43_000_000), '41 MB');
  assert.equal(formatSize(10), '1 MB');
  assert.equal(describeRelease(release, findInstaller(release)), 'v0.1.0 · 41 MB · Windows 10 and 11 · 64-bit');
  assert.equal(describeRelease(release, findDownload(release, 'macUniversal'), 'macUniversal'), 'v0.1.0 · 58 MB · macOS 12 or later · Apple silicon and Intel');
});

test('platform detection identifies Windows and Mac without pretending to know an unknown OS', async () => {
  assert.equal(platformFromNavigator({ userAgentData: { platform: 'Windows' } }), 'windows');
  assert.equal(platformFromNavigator({ platform: 'MacIntel' }), 'mac');
  assert.equal(platformFromNavigator({ userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 15_8_1)' }), 'mac');
  assert.equal(platformFromNavigator({ userAgent: 'Linux' }), 'other');
  assert.equal(await detectDownload({ userAgentData: { platform: 'Windows' } }), 'windows');
  assert.equal(await detectDownload({ platform: 'MacIntel' }), 'macUniversal');
  assert.equal(await detectDownload({ userAgent: 'Linux' }), null);
});

test('architecture labels understand browser architecture vocabulary', () => {
  assert.equal(macKindFromArchitecture('arm'), 'macArm64');
  assert.equal(macKindFromArchitecture('aarch64'), 'macArm64');
  assert.equal(macKindFromArchitecture('x86_64'), 'macX64');
  assert.equal(macKindFromArchitecture('amd64'), 'macX64');
  assert.equal(macKindFromArchitecture(''), null);
});

test('the release request asks GitHub once, without credentials, and survives failures', async () => {
  const calls = [];
  const fetcher = async (url, options) => { calls.push([url, options]); return { ok: true, json: async () => release }; };
  assert.equal((await fetchRelease(fetcher)).tag_name, 'v0.1.0');
  assert.equal(calls[0][0], RELEASE_API);
  assert.equal(calls[0][1].credentials, 'omit');
  assert.equal(await fetchRelease(respond(null, false)), null);
  assert.equal(await fetchRelease(async () => { throw new Error('offline'); }), null);
});

test('release assets decorate the detected download and every explicit choice', async () => {
  const doc = new FakeDocument();
  const primary = new FakeElement();
  const windows = new FakeElement();
  const universal = new FakeElement();
  doc.byId.download = primary;
  doc.bySelector['[data-download="windows"]'] = [windows];
  doc.bySelector['[data-download="macUniversal"]'] = [universal];
  doc.bySelector['[data-download="macArm64"]'] = [];
  doc.bySelector['[data-download="macX64"]'] = [];
  doc.byId['download-meta'] = new FakeElement();

  assert.equal(await decorateDownload(doc, respond(release), 'macUniversal', 'mac'), true);
  assert.equal(universal.getAttribute('download'), 'rexplayer-mac-universal.zip');
  assert.match(primary.getAttribute('href'), /rexplayer-mac-universal\.zip$/);
  assert.equal(doc.byId['download-meta'].textContent, 'v0.1.0 · 58 MB · macOS 12 or later · Apple silicon and Intel');
});

test('without a usable release the static link stays', async () => {
  const doc = new FakeDocument();
  assert.equal(await decorateDownload(doc, respond({ assets: [] })), false);
  assert.equal(await decorateDownload(doc, respond(null, false)), false);
  assert.equal(await decorateDownload(doc, respond(release)), true);
});

test('a Mac release without a universal app exposes architecture choices', async () => {
  const doc = new FakeDocument();
  doc.byId.download = new FakeElement();
  doc.byId['download-chooser'] = new FakeElement();
  const splitRelease = { ...release, assets: release.assets.filter(asset => asset.name !== 'rexplayer-mac-universal.zip') };
  assert.equal(await decorateDownload(doc, respond(splitRelease), 'macUniversal', 'mac'), true);
  assert.equal(doc.byId.download.getAttribute('href'), '#downloads');
  assert.equal(doc.byId.download.textContent, 'Choose your Mac');
  assert.equal(doc.byId['download-chooser'].hidden, false);
});

test('the main call to action becomes a direct safe download or an explicit chooser', () => {
  const doc = new FakeDocument();
  doc.byId.download = new FakeElement();
  doc.byId['download-meta'] = new FakeElement();
  doc.byId['download-chooser'] = new FakeElement();
  showDownloadChoice(doc, 'windows', 'windows');
  assert.match(doc.byId.download.getAttribute('href'), /rexplayer-Setup\.exe$/);
  assert.equal(doc.byId['download-chooser'].hidden, false);
  showDownloadChoice(doc, 'mac', null);
  assert.equal(doc.byId.download.getAttribute('href'), '#downloads');
  assert.equal(doc.byId.download.textContent, 'Choose your Mac');
  assert.equal(doc.byId['download-chooser'].hidden, false);
});

test('the menu opens, closes from a link, and Escape closes it only when open', async () => {
  const doc = new FakeDocument();
  const button = new FakeElement({ 'aria-expanded': 'false' });
  const nav = new FakeElement();
  const link = new FakeElement();
  nav.children = [link];
  doc.bySelector['.menu-button'] = [button];
  doc.bySelector['.site-nav'] = [nav];
  wireMenu(doc);

  await doc.fire('keydown', { key: 'Escape' });
  assert.equal(button.focused, false);
  await button.fire('click');
  assert.equal(button.getAttribute('aria-expanded'), 'true');
  assert.ok(nav.classes.has('open'));
  await doc.fire('keydown', { key: 'Enter' });
  assert.equal(button.getAttribute('aria-expanded'), 'true');
  await doc.fire('keydown', { key: 'Escape' });
  assert.equal(button.getAttribute('aria-expanded'), 'false');
  assert.equal(button.focused, true);
  await button.fire('click');
  await link.fire('click');
  assert.equal(button.getAttribute('aria-expanded'), 'false');
});

test('a page without a menu is left alone', () => {
  wireMenu(new FakeDocument());
});

test('copy buttons copy, announce and restore their label', async () => {
  const doc = new FakeDocument();
  const button = new FakeElement({ text: 'Copy', dataset: { copy: 'rexplay play song.wav' } });
  const status = new FakeElement();
  doc.bySelector['.copy-button'] = [button];
  doc.byId['copy-status'] = status;
  const copied = [];
  const scheduled = [];
  wireCopyButtons(doc, { writeText: async text => { copied.push(text); } }, (fn, ms) => scheduled.push([fn, ms]));

  await button.fire('click');
  assert.deepEqual(copied, ['rexplay play song.wav']);
  assert.equal(button.textContent, 'Copied');
  assert.equal(status.textContent, 'Command copied to the clipboard');
  scheduled[0][0]();
  assert.equal(button.textContent, 'Copy');
  assert.equal(scheduled[0][1], 1400);
});

test('a failed copy says so, with or without a status line', async () => {
  const doc = new FakeDocument();
  const button = new FakeElement({ text: 'Copy', dataset: { copy: 'x' } });
  doc.bySelector['.copy-button'] = [button];
  wireCopyButtons(doc, { writeText: async () => { throw new Error('denied'); } }, () => {});
  await button.fire('click');
  assert.equal(button.textContent, 'Copy failed');

  const announced = new FakeDocument();
  const second = new FakeElement({ text: 'Copy', dataset: { copy: 'x' } });
  announced.bySelector['.copy-button'] = [second];
  announced.byId['copy-status'] = new FakeElement();
  wireCopyButtons(announced, { writeText: async () => { throw new Error('denied'); } }, () => {});
  await second.fire('click');
  assert.equal(announced.byId['copy-status'].textContent, 'Could not copy the command');
});

test('start wires everything, fills the year and does nothing without a document', async () => {
  const doc = new FakeDocument();
  doc.byId.year = new FakeElement();
  doc.byId.download = new FakeElement();
  doc.byId['download-meta'] = new FakeElement();
  doc.byId['download-chooser'] = new FakeElement();
  for (const kind of ['windows', 'macUniversal', 'macArm64', 'macX64']) doc.bySelector[`[data-download="${kind}"]`] = [];
  await start(doc, { fetch: respond(release), navigator: { userAgentData: { platform: 'Windows' } }, setTimeout: () => {} });

  assert.equal(doc.byId.year.textContent, String(new Date().getFullYear()));
  assert.equal(doc.byId.download.getAttribute('download'), 'rexplayer-Setup-0.1.0.exe');
  start(undefined, {});
  start(new FakeDocument(), {});
});

test('without a clipboard a copy reports failure', async () => {
  const doc = new FakeDocument();
  const button = new FakeElement({ text: 'Copy', dataset: { copy: 'x' } });
  doc.bySelector['.copy-button'] = [button];
  start(doc, {});
  await button.fire('click');
  assert.equal(button.textContent, 'Copy failed');
});
