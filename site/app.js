// The site's behaviour: navigation, copy buttons and safe platform-specific downloads.

export const REPOSITORY = 'tochi-mba/rexplayer';
export const RELEASE_API = `https://api.github.com/repos/${REPOSITORY}/releases/latest`;
const DOWNLOAD_PREFIX = `https://github.com/${REPOSITORY}/releases/download/`;
const LATEST_PREFIX = `https://github.com/${REPOSITORY}/releases/latest/download/`;

export const DOWNLOADS = {
  windows: { pattern: /^rexplayer-Setup-(?!.*\.sha256$).*\.exe$/, stable: `${LATEST_PREFIX}rexplayer-Setup.exe`, label: 'Download for Windows', detail: 'Windows 10 and 11 · 64-bit' },
  macUniversal: { pattern: /^rexplayer-mac-universal\.zip$/, stable: `${LATEST_PREFIX}rexplayer-mac-universal.zip`, label: 'Download for Mac', detail: 'macOS 12 or later · Apple silicon and Intel' },
  macArm64: { pattern: /^rexplayer-mac-osx-arm64\.zip$/, stable: `${LATEST_PREFIX}rexplayer-mac-osx-arm64.zip`, label: 'Download for Apple silicon', detail: 'macOS · Apple silicon' },
  macX64: { pattern: /^rexplayer-mac-osx-x64\.zip$/, stable: `${LATEST_PREFIX}rexplayer-mac-osx-x64.zip`, label: 'Download for Intel Mac', detail: 'macOS · Intel' },
};

function isRepositoryAsset(asset) {
  return String(asset?.browser_download_url ?? '').startsWith(DOWNLOAD_PREFIX);
}

export function findDownload(release, kind) {
  const config = DOWNLOADS[kind];
  if (!config) return null;
  return (Array.isArray(release?.assets) ? release.assets : [])
    .find(asset => config.pattern.test(asset?.name ?? '') && isRepositoryAsset(asset)) ?? null;
}

export function findInstaller(release) { return findDownload(release, 'windows'); }

export function formatSize(bytes) { return `${Math.max(1, Math.round(bytes / 1048576))} MB`; }

export function describeRelease(release, asset, kind = 'windows') {
  return `${release.tag_name} · ${formatSize(asset.size)} · ${DOWNLOADS[kind].detail}`;
}

export function platformFromNavigator(nav = {}) {
  const platform = String(nav.userAgentData?.platform ?? nav.platform ?? '');
  const userAgent = String(nav.userAgent ?? '');
  if (/mac/i.test(platform)) return 'mac';
  if (/win/i.test(platform)) return 'windows';
  if (platform) return 'other';
  if (/macintosh|mac os x/i.test(userAgent)) return 'mac';
  if (/windows/i.test(userAgent)) return 'windows';
  return 'other';
}

export function macKindFromArchitecture(architecture) {
  const value = String(architecture ?? '').toLowerCase();
  if (/^(arm|arm64|aarch64)$/.test(value)) return 'macArm64';
  if (/^(x86|x86_64|amd64)$/.test(value)) return 'macX64';
  return null;
}

export async function detectDownload(nav = {}) {
  const platform = platformFromNavigator(nav);
  if (platform === 'windows') return 'windows';
  if (platform !== 'mac') return null;
  return 'macUniversal';
}

export async function fetchRelease(fetcher) {
  try {
    const response = await fetcher(RELEASE_API, { headers: { Accept: 'application/vnd.github+json' }, credentials: 'omit' });
    return response.ok ? await response.json() : null;
  } catch { return null; }
}

function setLink(link, kind, asset = null, updateLabel = true) {
  const config = DOWNLOADS[kind];
  link.setAttribute('href', asset?.browser_download_url ?? config.stable);
  if (asset?.name) link.setAttribute('download', asset.name);
  if (updateLabel) link.textContent = config.label;
}

export function showDownloadChoice(doc, platform, kind) {
  const primary = doc.getElementById('download');
  const chooser = doc.getElementById('download-chooser');
  const meta = doc.getElementById('download-meta');
  if (primary && kind) setLink(primary, kind);
  if (primary && !kind) {
    primary.setAttribute('href', '#downloads');
    primary.textContent = platform === 'mac' ? 'Choose your Mac' : 'Choose your download';
  }
  if (chooser) chooser.hidden = false;
  if (meta && kind) meta.textContent = DOWNLOADS[kind].detail;
  return kind;
}

export async function decorateDownload(doc, fetcher, kind = 'windows', platform = kind?.startsWith('mac') ? 'mac' : 'windows') {
  const release = await fetchRelease(fetcher);
  if (!release) return false;
  let changed = false;
  for (const key of Object.keys(DOWNLOADS)) {
    const asset = findDownload(release, key);
    if (!asset) continue;
    doc.querySelectorAll(`[data-download="${key}"]`).forEach(link => setLink(link, key, asset, false));
    if (key === kind) {
      const primary = doc.getElementById('download');
      if (primary) setLink(primary, key, asset);
      const meta = doc.getElementById('download-meta');
      if (meta) meta.textContent = describeRelease(release, asset, key);
    }
    changed = true;
  }
  if (platform === 'mac' && kind === 'macUniversal' && !findDownload(release, kind)) {
    showDownloadChoice(doc, 'mac', null);
  }
  return changed;
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
  doc.addEventListener('keydown', event => {
    if (event.key === 'Escape' && isOpen()) setMenu(false, true);
  });
}

export function wireCopyButtons(doc, clipboard, schedule) {
  const status = doc.getElementById('copy-status');
  doc.querySelectorAll('.copy-button').forEach(button => button.addEventListener('click', async () => {
    const label = button.textContent;
    let done = true;
    try { await clipboard.writeText(button.dataset.copy); } catch { done = false; }
    button.textContent = done ? 'Copied' : 'Copy failed';
    if (status) status.textContent = done ? 'Command copied to the clipboard' : 'Could not copy the command';
    schedule(() => { button.textContent = label; }, 1400);
  }));
}

export function wireScreenshotGallery(doc) {
  const dialog = doc.getElementById('shot-lightbox');
  const enlarged = doc.getElementById('shot-enlarged');
  const caption = doc.getElementById('shot-caption');
  const close = doc.getElementById('shot-close');
  if (!dialog || !enlarged || !caption || !close) return;
  let opener = null;
  doc.querySelectorAll('.shot-open').forEach(button => {
    button.addEventListener('click', event => {
      // If <dialog> is unsupported, use the original image link instead.
      if (typeof dialog.showModal !== 'function') return;
      const preview = button.querySelector('img');
      if (!preview) return;
      event.preventDefault();
      opener = button;
      enlarged.src = preview.currentSrc || preview.src;
      enlarged.alt = preview.alt;
      const figcaption = button.closest('figure')?.querySelector('figcaption');
      const heading = figcaption?.querySelector('strong')?.textContent?.trim() ?? '';
      const description = figcaption?.querySelector('span')?.textContent?.trim() ?? '';
      caption.textContent = [heading, description].filter(Boolean).join(' · ');
      dialog.showModal();
    });
  });
  close.addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', event => {
    if (event.target === dialog) dialog.close();
  });
  dialog.addEventListener('close', () => {
    enlarged.removeAttribute('src');
    opener?.focus();
  });
}

export function wireScreenshotTour(doc) {
  const steps = [...doc.querySelectorAll('[data-tour-step]')];
  const image = doc.getElementById('tour-image');
  const link = doc.getElementById('tour-image-link');
  const heading = doc.getElementById('tour-title');
  const description = doc.getElementById('tour-description');
  const progress = doc.getElementById('tour-progress');
  const previous = doc.getElementById('tour-previous');
  const next = doc.getElementById('tour-next');
  if (!steps.length || !image || !link || !heading || !description || !progress || !previous || !next) return;

  let current = 0;
  function showStep(index) {
    current = (index + steps.length) % steps.length;
    const step = steps[current];
    image.src = step.dataset.tourSrc;
    image.alt = step.dataset.tourAlt;
    link.setAttribute('href', step.dataset.tourSrc);
    link.setAttribute('aria-label', `Open full-size Windows ${step.dataset.tourTitle} screenshot`);
    heading.textContent = step.dataset.tourTitle;
    description.textContent = step.dataset.tourDescription;
    progress.textContent = `Step ${current + 1} of ${steps.length}`;
    steps.forEach((button, i) => {
      button.setAttribute('aria-pressed', String(i === current));
      button.classList.toggle('is-current', i === current);
    });
  }
  steps.forEach((button, index) => button.addEventListener('click', () => showStep(index)));
  previous.addEventListener('click', () => showStep(current - 1));
  next.addEventListener('click', () => showStep(current + 1));
}

export async function start(doc = globalThis.document, win = globalThis) {
  if (!doc) return;
  wireMenu(doc);
  wireScreenshotGallery(doc);
  wireScreenshotTour(doc);
  wireCopyButtons(doc, win.navigator?.clipboard ?? { writeText: () => Promise.reject(new Error('no clipboard')) }, win.setTimeout?.bind(win) ?? (() => {}));
  const year = doc.getElementById('year');
  if (year) year.textContent = String(new Date().getFullYear());
  const platform = platformFromNavigator(win.navigator);
  const kind = await detectDownload(win.navigator);
  showDownloadChoice(doc, platform, kind);
  if (typeof win.fetch === 'function') await decorateDownload(doc, win.fetch.bind(win), kind, platform);
}

start();
