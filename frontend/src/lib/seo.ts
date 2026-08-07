// Canonical site origin used by robots.txt, sitemap.xml, canonicals and JSON-LD.
// Override with NEXT_PUBLIC_SITE_URL when running on a different domain (staging etc).
export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL ?? "https://phoenixverify.com").replace(/\/$/, "");

export const SITE_NAME = "فونیکس وریفای | Phoenix Verify";

export function absoluteUrl(path: string): string {
  return `${SITE_URL}${path.startsWith("/") ? path : `/${path}`}`;
}

// Serializes a value for injection into a <script type="application/ld+json"> via
// dangerouslySetInnerHTML. JSON.stringify does NOT escape "<", ">" or "/", so admin-authored
// fields (product names, descriptions, FAQ text, blog content) containing "</script>" would
// otherwise break out of the tag and inject arbitrary HTML. Escaping these to their \uXXXX
// forms keeps the JSON-LD valid (crawlers parse the escapes identically) while closing the hole.
export function jsonLdScript(value: unknown): string {
  return JSON.stringify(value)
    .replace(/</g, "\\u003c")
    .replace(/>/g, "\\u003e")
    .replace(/&/g, "\\u0026");
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

// Page title for a product: admin-entered names may already start with «خرید»,
// so only prepend it when missing to avoid «خرید خرید …».
export function productTitle(name: string): string {
  const n = name.trim();
  return /^خرید([\s‌]|$)/.test(n) ? n : `خرید ${n}`;
}

// Latin brand token from a product name (e.g. "Netflix" out of
// «خرید اکانت نتفلیکس Netflix»), for schema.org brand markup.
export function latinBrand(name: string): string | null {
  const m = name.match(/[A-Za-z][A-Za-z0-9.+&-]*(?:\s+[A-Za-z0-9.+&-]+)*/);
  return m ? m[0].trim() : null;
}

// Strip markdown syntax and collapse whitespace for use in meta descriptions.
//
// Product descriptions are markdown documents that open with a heading, so taking the raw text would
// run that heading straight into the first paragraph ("… و پشتیبانی فارسی نتفلیکس بزرگ‌ترین سرویس …").
// Headings, list bullets, table rows and link targets are dropped so the excerpt starts at the first
// real sentence, and the cut lands on a word boundary instead of mid-word.
export function plainExcerpt(text: string, max = 160): string {
  const prose = text
    .replace(/\r\n/g, "\n")
    .split("\n")
    .filter((line) => {
      const l = line.trim();
      if (!l) return false;
      if (/^#{1,6}\s/.test(l)) return false; // headings
      if (/^[-*+]\s/.test(l)) return false; // bullets
      if (/^\|/.test(l)) return false; // table rows and separators
      if (/^\d+[.)]\s/.test(l) || /^[۰-۹]+[.)]\s/.test(l)) return false; // numbered steps
      return true;
    })
    .join(" ");

  const plain = (prose || text)
    .replace(/!?\[([^\]]*)\]\([^)]*\)/g, "$1") // links/images → their label
    .replace(/[#*_`>]/g, "")
    .replace(/\s+/g, " ")
    .trim();

  if (plain.length <= max) return plain;
  const cut = plain.slice(0, max - 1);
  const lastSpace = cut.lastIndexOf(" ");
  return `${(lastSpace > max * 0.6 ? cut.slice(0, lastSpace) : cut).replace(/[،,.\s]+$/, "")}…`;
}

// A search result shows roughly 120–160 characters, and an admin-written product description is often a
// single short sentence — which wastes most of that space. This tops a short one up from what the product
// actually offers, so the sentence stays true and, because it is built from that product's own plans and
// price, differs from every other product's rather than reading as the same boilerplate everywhere.
const META_MIN = 120;
const META_MAX = 158;

type MetaProduct = {
  name: string;
  description: string;
  categoryName?: string;
  finalPrice?: number;
  plans?: { isActive: boolean; months: number; finalPrice: number }[];
};

export function productMetaDescription(product: MetaProduct): string {
  const base = plainExcerpt(product.description, META_MAX);
  if (base.length >= META_MIN) return base;

  const active = (product.plans ?? []).filter((p) => p.isActive);
  const prices = active.map((p) => p.finalPrice).filter((n) => n > 0);
  const cheapest = prices.length > 0 ? Math.min(...prices) : (product.finalPrice ?? 0);

  // Built as one sentence rather than a joined list, so a product with a single plan doesn't read
  // "با از ۱۸۰,۰۰۰ تومان" — each clause is only added when it has something to say.
  const plans = active.length > 1 ? ` در ${toFaDigits(active.length)} پلن` : "";
  const price = cheapest > 0 ? ` از ${toFaDigits(cheapest.toLocaleString("en-US"))} تومان` : "";

  // Whatever the description already promises isn't promised again a few words later.
  const assurances = ["تحویل آنی", "پرداخت امن", "پشتیبانی ۲۴ ساعته"]
    .filter((phrase) => !base.includes(phrase));
  const tail = assurances.length > 0 ? ` با ${assurances.join("، ")}` : "";

  // With no description of its own there is no lead to write, and repeating the name twice would only
  // burn the space the sentence is trying to fill.
  const lead = base.length > 0 ? `${base.replace(/[.،]\s*$/, "")}. ` : "";

  const composed = `${lead}${productTitle(product.name)}${plans}${price}${tail}.`;
  return composed.length > META_MAX ? `${composed.slice(0, META_MAX - 1)}…` : composed;
}

// Persian digits for numerals embedded in a description, matching how prices read on the page itself.
function toFaDigits(value: string | number): string {
  return String(value).replace(/[0-9]/g, (d) => "۰۱۲۳۴۵۶۷۸۹"[Number(d)]);
}
