(() => {
  'use strict';
  const repository = 'stisvladnext3-creator/moskompremont';
  const platforms = [
    { key: 'windows', tag: /^moskom-vpn-v(\d+\.\d+\.\d+)$/, filename: version => `MoskomVPN-Setup-${version}.exe` },
    { key: 'android', tag: /^moskom-android-v(\d+\.\d+\.\d+)$/, filename: version => `MoskomVPN-Android-${version}.apk` }
  ];
  // Keep the verified fallback link usable when the version check is unavailable.
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 7000);
  fetch(`https://api.github.com/repos/${repository}/releases?per_page=30`, {
    credentials: 'omit', cache: 'no-store', signal: controller.signal,
    headers: { Accept: 'application/vnd.github+json' }
  }).then(response => {
    if (!response.ok) throw new Error('Release check unavailable');
    return response.json();
  }).then(releases => {
    if (!Array.isArray(releases)) return;
    for (const platform of platforms) {
      const button = document.getElementById(`${platform.key}-download`);
      const caption = document.getElementById(`${platform.key}-version`);
      if (!button || !caption) continue;
      const candidates = [];
      for (const release of releases) {
        if (release.draft || release.prerelease || typeof release.tag_name !== 'string') continue;
        const match = platform.tag.exec(release.tag_name);
        if (!match || !Array.isArray(release.assets)) continue;
        const version = match[1], name = platform.filename(version);
        const expected = `https://github.com/${repository}/releases/download/${release.tag_name}/${name}`;
        const installer = release.assets.find(asset => asset.name === name && asset.browser_download_url === expected && asset.state === 'uploaded' && asset.size > 0);
        if (installer) candidates.push({ version, url: expected, parts: version.split('.').map(Number) });
      }
      candidates.sort((a, b) => b.parts[0] - a.parts[0] || b.parts[1] - a.parts[1] || b.parts[2] - a.parts[2]);
      if (candidates.length) {
        button.href = candidates[0].url;
        caption.textContent = `Версия ${candidates[0].version}`;
      }
    }
  }).catch(() => {}).finally(() => clearTimeout(timer));
})();
