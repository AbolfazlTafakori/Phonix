import type { MetadataRoute } from "next";
import { api } from "@/lib/api";
import { getBlogPosts } from "@/lib/content";
import { SITE_URL, productPath } from "@/lib/seo";

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
  try {
    const products = await api.products.list();
    productPages = products
      .filter((p) => p.isActive)
      .map((p) => ({
        url: `${SITE_URL}${productPath(p)}`,
        changeFrequency: "weekly" as const,
        priority: 0.8,
      }));
  } catch {
    // API unavailable — still serve the static portion of the sitemap.
  }

  let blogPages: MetadataRoute.Sitemap = [];
  try {
    const posts = await getBlogPosts();
    blogPages = posts
      .filter((p) => p.isActive)
      .map((p) => ({
        url: `${SITE_URL}/blog/${encodeURIComponent(p.slug)}`,
        changeFrequency: "monthly" as const,
        priority: 0.5,
      }));
  } catch {
    // blog is optional
  }

  return [...staticPages, ...productPages, ...blogPages];
}
