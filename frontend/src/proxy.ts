import { NextRequest, NextResponse } from "next/server";
import { inlineScriptHashes } from "@/lib/inlineScripts";

// Proxy (ex-middleware) always runs server-side, so it targets the API over the loopback rather than a relative URL.
const BASE = process.env.PHONIX_INTERNAL_API_URL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://127.0.0.1:5228";

type Settings = { maintenanceMode: boolean; analyticsId: string; customHeadScript: string };

const EMPTY: Settings = { maintenanceMode: false, analyticsId: "", customHeadScript: "" };

// One read serves both jobs below: the maintenance gate and the script hashes the CSP has to carry. The
// settings are read on the way through every request, so they are cached briefly rather than fetched each time.
let cache: { value: Settings; at: number } = { value: EMPTY, at: 0 };

async function settings(): Promise<Settings> {
  if (Date.now() - cache.at < 15000) return cache.value;
  try {
    const res = await fetch(`${BASE}/api/advanced-settings`, { cache: "no-store" });
    const data = await res.json();
    cache = {
      value: {
        maintenanceMode: Boolean(data.maintenanceMode),
        analyticsId: String(data.analyticsId ?? ""),
        customHeadScript: String(data.customHeadScript ?? ""),
      },
      at: Date.now(),
    };
  } catch {
    cache = { value: EMPTY, at: Date.now() };
  }
  return cache.value;
}

// script-src still refuses arbitrary inline script — the one thing that actually matters against injection,
// since a future XSS bug or a compromised third-party snippet would otherwise execute freely even with a CSP
// header present. What allows our own four inline scripts is now their SHA-256 hashes (see lib/inlineScripts)
// rather than a per-request nonce, because a nonce lives in the HTML and made every page uncacheable.
//
// That swap is why 'strict-dynamic' is gone: it tells the browser to ignore 'self', and Next's own
// /_next/static chunks are plain <script src> tags that could then only be admitted by a nonce. Falling back
// to 'self' for them is sound here because the API sends X-Content-Type-Options: nosniff and re-encodes
// uploads to real image types, so an uploaded file cannot come back as executable same-origin script.
// Applied to every response this proxy returns — including /admin, which renders the whole staff panel — not
// just the public storefront's maintenance-gated pages.
function withCsp(response: NextResponse, scriptHashes: string[]): NextResponse {
  const apiUrl = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5228";
  const isDev = process.env.NODE_ENV !== "production";
  const csp = [
    "default-src 'self'",
    // googletagmanager is listed because the GTM loader is an external <script src> and, without
    // 'strict-dynamic', every host it pulls from has to be named.
    `script-src 'self' ${scriptHashes.join(" ")} https://www.googletagmanager.com https://www.google-analytics.com${isDev ? " 'unsafe-eval'" : ""}`,
    // Inline `style={{...}}` props are used throughout this app's components — CSP gates those the same as
    // <style> tags, and nonce-ing every element's style attribute individually isn't practical. Kept as
    // 'unsafe-inline' deliberately: this only weakens CSS-injection defense, not script execution, which is
    // the part that actually matters for stealing session/2FA input.
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob: https:",
    `connect-src 'self' ${apiUrl} https://www.google-analytics.com https://www.googletagmanager.com`,
    "font-src 'self' data:",
    "frame-ancestors 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    // <object>/<embed> can execute in the page's origin, and default-src 'self' would still permit a
    // self-hosted one — which uploaded media makes reachable. Nothing here uses plugins, so: none.
    "object-src 'none'",
    // This app embeds no iframes at all EXCEPT the sandboxed one the admin mailbox renders untrusted mail
    // into, which is a srcdoc frame and needs no source permission. Anything else framing itself in is
    // injection, so only same-origin is allowed, and workers are pinned to 'self' rather than inheriting
    // script-src's third-party hosts.
    "frame-src 'self'",
    "worker-src 'self' blob:",
    // Any absolute http:// URL that survives in content gets fetched over TLS instead of silently
    // downgrading the connection.
    "upgrade-insecure-requests",
  ].join("; ");
  response.headers.set("Content-Security-Policy", csp);
  return response;
}

export async function proxy(req: NextRequest) {
  const { pathname } = req.nextUrl;

  const current = await settings();
  const hashes = await inlineScriptHashes(current);

  // admin, api, the maintenance page itself, and static files always pass through
  if (
    pathname.startsWith("/admin") ||
    pathname.startsWith("/api") ||
    pathname.startsWith("/maintenance") ||
    pathname.includes(".")
  ) {
    return withCsp(NextResponse.next(), hashes);
  }

  if (current.maintenanceMode) {
    const url = req.nextUrl.clone();
    url.pathname = "/maintenance";
    return withCsp(NextResponse.rewrite(url), hashes);
  }

  return withCsp(NextResponse.next(), hashes);
}

export const config = {
  matcher: ["/((?!_next).*)"],
};
