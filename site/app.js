// The site's behaviour: the mobile menu, copy buttons, the footer year and the download button.
// Pure functions are exported for the tests in tests/site; start() wires them to a document and does
// nothing without one, so importing this file in Node is harmless.

export const REPOSITORY = 'tochi-mba/rexplayer';
export const RELEASE_API = `https://api.github.com/repos/${REPOSITORY}/releases/latest`;
const DOWNLOAD_PREFIX = `https://github.com/${REPOSITORY}/releases/download/`;

/** The installer asset of a release, accepted only when it is hosted by this repository. */
export function findInstaller(release) {
  const assets = Array.isArray(release?.assets) ? release.assets : [];
  return assets.find(asset =>
    /^rexplayer-Setup-.*\.exe$/.test(asset?.name ?? '') &&
    String(asset?.browser_download_url ?? '').startsWith(DOWNLOAD_PREFIX)) ?? null;
}

export function formatSize(bytes) {
  return `${Math.max(1, Math.round(bytes / 1048576))} MB`;
}

/** The line under the download button, such as "v0.1.0 · 41 MB · Windows 10 and 11, 64-bit". */
export function describeRelease(release, installer) {
  return `${release.tag_name} · ${formatSize(installer.size)} · Windows 10 and 11, 64-bit`;
}

/** Asks GitHub for the latest release; any failure leaves the static link in place. */
export async function fetchRelease(fetcher) {
  try {
    const response = await fetcher(RELEASE_API, { headers: { Accept: 'application/vnd.github+json' }, credentials: 'omit' });
    return response.ok ? await response.json() : null;
  } catch {
    return null;
  }
}

export async function decorateDownload(doc, fetcher) {
  const release = await fetchRelease(fetcher);
  const installer = release ? findInstaller(release) : null;
  if (!installer) return false;
  doc.querySelectorAll('[data-latest-installer]').forEach(link => {
    link.setAttribute('href', installer.browser_download_url);
    link.setAttribute('download', installer.name);
  });
  const meta = doc.getElementById('download-meta');
  if (meta) meta.textContent = describeRelease(release, installer);
  return true;
}

export function wireMenu(doc) {
  const button = doc.querySelector('.menu-button');
  const nav = doc.querySelector('.site-nav');
  if (!button || !nav) return;
  const isOpen = () => button.getAttribute('aria-expanded') === 'true';
  const setMenu = (open, returnFocus = false) => {
    nav.classList.toggle('open', open);
    button.setAttribute('aria-expanded', String(open));
    if (!open && returnFocus) button.focus();
  };
  button.addEventListener('click', () => setMenu(!isOpen()));
  nav.querySelectorAll('a').forEach(link => link.addEventListener('click', () => setMenu(false)));
  // Escape closes an open menu and returns focus to its button; with the menu closed it does nothing.
  doc.addEventListener('keydown', event => {
    if (event.key === 'Escape' && isOpen()) setMenu(false, true);
  });
}

export function wireCopyButtons(doc, clipboard, schedule) {
  const status = doc.getElementById('copy-status');
  doc.querySelectorAll('.copy-button').forEach(button => button.addEventListener('click', async () => {
    const label = button.textContent;
    let done = true;
    try {
      await clipboard.writeText(button.dataset.copy);
    } catch {
      done = false;
    }
    button.textContent = done ? 'Copied' : 'Copy failed';
    if (status) status.textContent = done ? 'Command copied to the clipboard' : 'Could not copy the command';
    schedule(() => { button.textContent = label; }, 1400);
  }));
}

export function start(doc = globalThis.document, win = globalThis) {
  if (!doc) return;
  wireMenu(doc);
  wireCopyButtons(doc, win.navigator?.clipboard ?? { writeText: () => Promise.reject(new Error('no clipboard')) }, win.setTimeout?.bind(win) ?? (() => {}));
  const year = doc.getElementById('year');
  if (year) year.textContent = String(new Date().getFullYear());
  if (typeof win.fetch === 'function') decorateDownload(doc, win.fetch.bind(win));
}

start();
