import type { MetadataRoute } from "next";
import { api } from "@/lib/api";
import { getBlogPosts } from "@/lib/content";
import { SITE_URL, productPath } from "@/lib/seo";
import { categoryPath } from "@/lib/categorySeo";
import { parsePostDate } from "@/lib/jalali";

export const dynamic = "force-dynamic";

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const staticPages: MetadataRoute.Sitemap = [
    { url: `${SITE_URL}/`, changeFrequency: "daily", priority: 1 },
    { url: `${SITE_URL}/products`, changeFrequency: "daily", priority: 0.9 },
    { url: `${SITE_URL}/categories`, changeFrequency: "weekly", priority: 0.7 },
    { url: `${SITE_URL}/blog`, changeFrequency: "weekly", priority: 0.6 },
    { url: `${SITE_URL}/terms`, changeFrequency: "yearly", priority: 0.2 },
  ];

  let productPages: MetadataRoute.Sitemap = [];
  let categoryPages: MetadataRoute.Sitemap = [];
  try {
    const [products, categories] = await Promise.all([api.products.list(), api.categories.list()]);
    const activeProducts = products.filter((p) => p.isActive);
    productPages = activeProducts.map((p) => ({
      url: `${SITE_URL}${productPath(p)}`,
      changeFrequency: "weekly" as const,
      priority: 0.8,
    }));
    // A category landing page 404s when it has no products, so only the ones that resolve are listed.
    categoryPages = categories
      .filter((c) => c.isActive && activeProducts.some((p) => p.categoryId === c.id))
      .map((c) => ({
        url: `${SITE_URL}${categoryPath(c)}`,
        changeFrequency: "weekly" as const,
        priority: 0.85,
      }));
  } catch {
    // API unavailable — still serve the static portion of the sitemap.
  }

  let blogPages: MetadataRoute.Sitemap = [];
  try {
    const posts = await getBlogPosts();
    blogPages = posts
      .filter((p) => p.isActive)
      .map((p) => {
        // Only the posts whose label parses get a lastmod — a guessed date would teach Google to
        // distrust the field across the whole sitemap.
        const published = parsePostDate(p.date);
        return {
          url: `${SITE_URL}/blog/${encodeURIComponent(p.slug)}`,
          ...(published && { lastModified: published }),
          changeFrequency: "monthly" as const,
          priority: 0.5,
        };
      });
  } catch {
    // blog is optional
  }

  return [...staticPages, ...categoryPages, ...productPages, ...blogPages];
}
