import type { NextConfig } from "next";

const apiUrl = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5228";

// Permissive enough for Next.js hydration + analytics, strict on framing/sniffing.
const csp = [
  "default-src 'self'",
  "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://www.googletagmanager.com",
  "style-src 'self' 'unsafe-inline'",
  "img-src 'self' data: blob: https:",
  `connect-src 'self' ${apiUrl} https://www.google-analytics.com https://www.googletagmanager.com`,
  "font-src 'self' data:",
  "frame-ancestors 'none'",
  "base-uri 'self'",
  "form-action 'self'",
].join("; ");

const securityHeaders = [
  { key: "Content-Security-Policy", value: csp },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=()" },
];

const nextConfig: NextConfig = {
  // Emit a self-contained server bundle (.next/standalone) so the Docker runtime image stays small.
  output: "standalone",
  async headers() {
    return [{ source: "/:path*", headers: securityHeaders }];
  },
  // Same-origin /api/* (e.g. the relative image URLs the API returns, like /api/upload/{id}) is forwarded to
  // the backend. In production the reverse proxy routes /api straight to the API before Next sees it, so this
  // is inert there; it only bridges split-port local dev where the frontend and API run on different ports.
  // Client JS already calls the API on its absolute origin, so those requests don't pass through this rewrite.
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiUrl}/api/:path*` }];
  },
  // The products section moved from /films to /products. Keep the old paths working so existing links,
  // bookmarks, and any hrefs still stored as /films (e.g. home categories) don't 404.
  async redirects() {
    return [
      { source: "/films", destination: "/products", permanent: true },
      { source: "/films/:path*", destination: "/products/:path*", permanent: true },
    ];
  },
};

export default nextConfig;
