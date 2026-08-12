// Every inline <script> the app ships, in one place.
//
// CSP has to allow these somehow, and the two ways to do it are a per-request nonce or a hash of the exact
// script text. A nonce is written into the HTML, so any page carrying one can never be cached — which is
// what kept the entire site on the dynamic render path. Hashes depend only on the text, so the same HTML
// stays valid for every visitor and can be cached.
//
// The catch with hashes is that the proxy computes them while the layout renders them, and a single
// character of drift between the two silently stops the script from running. That is why both sides import
// from this module instead of writing the script text themselves: there is only one copy to change.

// Applies the saved (or system) theme before first paint, so the page never flashes the wrong one. Written
// straight into the HTML because it must run while the document is still parsing — an external file would
// be fetched too late to prevent the flash.
export const THEME_INIT_SRC = `(function(){try{var mq=window.matchMedia&&window.matchMedia('(prefers-color-scheme: dark)');function mode(){try{return localStorage.getItem('phonix-theme')||'system';}catch(e){return 'system';}}function apply(){var m=mode();var dark=m==='dark'||(m==='system'&&!!(mq&&mq.matches));document.documentElement.classList.toggle('home-dark',dark);}apply();window.__phonixApplyTheme=apply;if(mq){var h=function(){if(mode()==='system')apply();};mq.addEventListener?mq.addEventListener('change',h):mq.addListener(h);}}catch(e){}})();`;

// GA/GTM ids are alphanumeric + dash; strip anything else so the value can never break out of the inline
// script string. Both callers must sanitize identically or the hash stops matching what is rendered.
export const sanitizeAnalyticsId = (id: string): string => id.replace(/[^A-Za-z0-9-]/g, "");

export const gaInitSrc = (analyticsId: string): string =>
  `window.dataLayer=window.dataLayer||[];function gtag(){dataLayer.push(arguments);}gtag('js',new Date());gtag('config','${analyticsId}');`;

// The admin supplies this one, so strip any script tags they wrapped it in — it is injected as the body of
// a <script> that already exists.
export const sanitizeCustomScript = (src: string): string => src.replace(/<\/?script[^>]*>/gi, "");

export const gtagSrc = (analyticsId: string): string =>
  `https://www.googletagmanager.com/gtag/js?id=${analyticsId}`;

// CSP wants base64 of the raw SHA-256 digest of exactly the bytes that appear between the script tags.
export async function sha256Base64(src: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(src));
  return btoa(String.fromCharCode(...new Uint8Array(digest)));
}

// The script-src hashes for a given settings snapshot. Mirrors exactly what layout.tsx renders: the theme
// boot script always, plus the analytics pair and the custom script only when they are configured.
export async function inlineScriptHashes(settings: {
  analyticsId?: string;
  customHeadScript?: string;
}): Promise<string[]> {
  const sources = [THEME_INIT_SRC];
  const analyticsId = sanitizeAnalyticsId(settings.analyticsId ?? "");
  if (analyticsId) sources.push(gaInitSrc(analyticsId));
  const custom = settings.customHeadScript ? sanitizeCustomScript(settings.customHeadScript) : "";
  if (custom) sources.push(custom);
  return Promise.all(sources.map(async (s) => `'sha256-${await sha256Base64(s)}'`));
}
