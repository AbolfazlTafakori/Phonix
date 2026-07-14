import type { NextConfig } from "next";

const apiUrl = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5228";

// Where the Next server itself reaches the API. NEXT_PUBLIC_API_URL is the browser's view of it (a public
// domain), which a container cannot always route back to; API_INTERNAL_URL lets the server take the direct
// path instead (e.g. http://backend:5228 on the compose network). Only the /api/* rewrite uses this — and
// that rewrite is what the image optimizer follows when it fetches an uploaded image to re-encode.
const internalApiUrl = process.env.API_INTERNAL_URL ?? apiUrl;

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
  // The source PNGs in /public are full-resolution and are displayed far smaller than they are stored.
  // The optimizer re-encodes them to AVIF/WebP at the requested width and caches the result; the files
  // on disk are only ever read. Widths here must cover every value <Img> puts in its srcSet.
  images: {
    formats: ["image/avif", "image/webp"],
    deviceSizes: [640, 828, 1080, 1200, 1920],
    imageSizes: [64, 128, 256, 384],
    minimumCacheTTL: 60 * 60 * 24 * 30,
  },
  async headers() {
    return [
      { source: "/:path*", headers: securityHeaders },
      // Content-addressed by filename and never mutated in place, so they can sit in the browser cache.
      {
        source: "/figma/:path*",
        headers: [{ key: "Cache-Control", value: "public, max-age=31536000, immutable" }],
      },
    ];
  },
  // Same-origin /api/* (e.g. the relative image URLs the API returns, like /api/upload/{id}) is forwarded to
  // the backend. In production the reverse proxy routes /api straight to the API before Next sees it, so this
  // is inert there; it only bridges split-port local dev where the frontend and API run on different ports.
  // Client JS already calls the API on its absolute origin, so those requests don't pass through this rewrite.
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${internalApiUrl}/api/:path*` }];
  },
  // The products section moved from /films to /products. Keep the old paths working so existing links,
  // bookmarks, and any hrefs still stored as /films (e.g. home categories) don't 404.
  async redirects() {
    return [
      { source: "/films", destination: "/products", permanent: true },
      { source: "/films/:path*", destination: "/products/:path*", permanent: true },
      // Legacy product URLs: /products/detail?id=N → /products/N, which the
      // [slug] page then 308s to the full canonical slug.
      {
        source: "/products/detail",
        has: [{ type: "query", key: "id", value: "(?<id>\\d+)" }],
        destination: "/products/:id",
        permanent: true,
      },
      { source: "/products/detail", destination: "/products", permanent: true },
    ];
  },
};

export default nextConfig;
