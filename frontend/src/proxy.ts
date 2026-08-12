import { NextRequest, NextResponse } from "next/server";

// Proxy (ex-middleware) always runs server-side, so it targets the API over the loopback rather than a relative URL.
const BASE = process.env.PHONIX_INTERNAL_API_URL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://127.0.0.1:5228";

let cache = { on: false, at: 0 };

async function maintenanceOn(): Promise<boolean> {
  if (Date.now() - cache.at < 15000) return cache.on;
  try {
    const res = await fetch(`${BASE}/api/advanced-settings`, { cache: "no-store" });
    const data = await res.json();
    cache = { on: Boolean(data.maintenanceMode), at: Date.now() };
  } catch {
    cache = { on: false, at: Date.now() };
  }
  return cache.on;
}

// Per-request nonce so script-src can drop 'unsafe-inline'/'unsafe-eval': without this, CSP was allowing ANY
// inline <script> to run, which is the one thing that actually matters against script injection — a future
// XSS bug (or a compromised third-party snippet) would otherwise execute freely even with a CSP header
// present. 'strict-dynamic' lets a nonce'd script's own dynamically-inserted children (Next's chunk loader,
// GTM's own loader) run too, without maintaining a host allowlist for every one of them. Applied to every
// response this proxy returns — including /admin, which renders the whole staff panel — not just the public
// storefront's maintenance-gated pages.
function withCsp(response: NextResponse, nonce: string): NextResponse {
  const apiUrl = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5228";
  const isDev = process.env.NODE_ENV !== "production";
  const csp = [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${isDev ? " 'unsafe-eval'" : ""}`,
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
    // injection, so only same-origin is allowed and workers are pinned to 'self' rather than inheriting
    // the looser script-src (where 'strict-dynamic' would otherwise carry over).
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

  // The nonce travels to Server Components via this request header (read back with next/headers in
  // layout.tsx to stamp onto the <Script> tags there) — there's no other way to thread a per-request value
  // into RSC.
  const nonce = Buffer.from(crypto.randomUUID()).toString("base64");
  const requestHeaders = new Headers(req.headers);
  requestHeaders.set("x-nonce", nonce);
  const forwarded = { request: { headers: requestHeaders } };

  // admin, api, the maintenance page itself, and static files always pass through
  if (
    pathname.startsWith("/admin") ||
    pathname.startsWith("/api") ||
    pathname.startsWith("/maintenance") ||
    pathname.includes(".")
  ) {
    return withCsp(NextResponse.next(forwarded), nonce);
  }

  if (await maintenanceOn()) {
    const url = req.nextUrl.clone();
    url.pathname = "/maintenance";
    return withCsp(NextResponse.rewrite(url, forwarded), nonce);
  }

  return withCsp(NextResponse.next(forwarded), nonce);
}

export const config = {
  matcher: ["/((?!_next).*)"],
};
