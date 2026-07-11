// Canonical site origin used by robots.txt, sitemap.xml, canonicals and JSON-LD.
// Override with NEXT_PUBLIC_SITE_URL when running on a different domain (staging etc).
export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL ?? "https://phoenixverify.com").replace(/\/$/, "");

export const SITE_NAME = "فونیکس وریفای | Phoenix Verify";

export function absoluteUrl(path: string): string {
  return `${SITE_URL}${path.startsWith("/") ? path : `/${path}`}`;
}

// URL slug from a (mostly Persian) product name: keep Persian/Latin letters and
// digits, collapse everything else into single dashes.
export function slugify(name: string): string {
  return name
    .trim()
    .replace(/[^؀-ۿa-zA-Z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 80);
}

// Canonical slug (unencoded, for comparisons): "{id}-{name-slug}". The id
// prefix is what the page resolves by, so slug drift never breaks the link.
export function productSlug(p: { id: number; name: string }): string {
  const s = slugify(p.name);
  return `${p.id}${s ? `-${s}` : ""}`;
}

// Canonical product URL, percent-encoded so it is safe in Location headers,
// sitemaps and hrefs alike.
export function productPath(p: { id: number; name: string }): string {
  return `/products/${encodeURIComponent(productSlug(p))}`;
}

// Strip markdown syntax and collapse whitespace for use in meta descriptions.
export function plainExcerpt(text: string, max = 160): string {
  const plain = text.replace(/[#*_\[\]()`>]/g, "").replace(/\s+/g, " ").trim();
  return plain.length > max ? `${plain.slice(0, max - 1)}…` : plain;
}
