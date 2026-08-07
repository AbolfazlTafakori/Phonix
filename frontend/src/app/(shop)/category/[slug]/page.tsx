import Link from "next/link";
import { notFound } from "next/navigation";
import { api } from "@/lib/api";
import type { Category, Product } from "@/lib/types";
import ProductsBrowser from "@/components/products/ProductsBrowser";
import HomeNewsletter from "@/components/home/HomeNewsletter";
import { absoluteUrl, jsonLdScript, plainExcerpt, productPath } from "@/lib/seo";
import { categoryHeading, categoryIntro, categoryPath, categorySlug } from "@/lib/categorySeo";
import { toCardData } from "@/lib/productCard";

// Served from cache and refreshed in the background, so a visitor never waits on the API for a page
// whose contents only change when an admin edits them.
export const revalidate = 60;


async function resolve(slug: string): Promise<{ category: Category; products: Product[]; categories: Category[] } | null> {
  const wanted = decodeURIComponent(slug).toLowerCase();
  const [products, categories] = await Promise.all([api.products.listCached(), api.categories.listCached()]);
  const active = categories.filter((c) => c.isActive);
  const category = active.find((c) => categorySlug(c) === wanted);
  if (!category) return null;
  const inCategory = products.filter((p) => p.isActive && p.categoryId === category.id);
  // An empty category page is a thin page with nothing to rank for — don't serve one.
  if (inCategory.length === 0) return null;
  return { category, products: inCategory, categories: active };
}

export async function generateMetadata({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  try {
    const found = await resolve(slug);
    if (!found) return { title: "دسته‌بندی" };
    const { category, products } = found;
    const heading = categoryHeading(category);
    const intro = categoryIntro(category);
    const names = products.slice(0, 4).map((p) => p.name.replace(/^خرید\s+(اکانت\s+)?/, "")).join("، ");
    const description =
      plainExcerpt(intro, 158) ||
      `${heading} با تحویل سریع و پرداخت ریالی. ${names} و دیگر سرویس‌های این دسته در فونیکس وریفای.`;
    return {
      title: heading,
      description,
      alternates: { canonical: categoryPath(category) },
      openGraph: { type: "website", title: `${heading} | Phoenix Verify`, description, url: categoryPath(category) },
    };
  } catch {
    return { title: "دسته‌بندی" };
  }
}

export default async function CategoryLandingPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;

  let found: Awaited<ReturnType<typeof resolve>> = null;
  try {
    found = await resolve(slug);
  } catch {
    notFound();
  }
  if (!found) notFound();
  const { category, products, categories } = found;

  const heading = categoryHeading(category);
  const intro = categoryIntro(category);

  const breadcrumbLd = {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: [
      { "@type": "ListItem", position: 1, name: "صفحه اصلی", item: absoluteUrl("/") },
      { "@type": "ListItem", position: 2, name: "دسته‌بندی‌ها", item: absoluteUrl("/categories") },
      { "@type": "ListItem", position: 3, name: category.name, item: absoluteUrl(categoryPath(category)) },
    ],
  };
  const itemListLd = {
    "@context": "https://schema.org",
    "@type": "CollectionPage",
    name: heading,
    url: absoluteUrl(categoryPath(category)),
    mainEntity: {
      "@type": "ItemList",
      numberOfItems: products.length,
      itemListElement: products.map((p, i) => ({
        "@type": "ListItem",
        position: i + 1,
        name: p.name,
        url: absoluteUrl(productPath(p)),
      })),
    },
  };

  return (
    <>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdScript(breadcrumbLd) }} />
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdScript(itemListLd) }} />

      <section className="border-b border-[var(--hl-border)] bg-[var(--hl-surface)]">
        <div className="mx-auto max-w-[1840px] px-4 sm:px-8 xl:px-16">
          <nav className="flex items-center justify-start gap-2 pb-2 pt-6 text-[13px] text-[var(--hl-muted)]">
            <Link href="/" className="transition hover:text-[var(--hl-red-text)]">خانه</Link>
            <span>/</span>
            <Link href="/categories" className="transition hover:text-[var(--hl-red-text)]">دسته‌بندی‌ها</Link>
            <span>/</span>
            <span className="font-medium text-[var(--hl-ink)]">{category.name}</span>
          </nav>

          <div className="flex flex-col items-center gap-6 pb-10 pt-4 lg:flex-row-reverse lg:items-center lg:gap-10">
            {category.icon && (
              <img
                fetchPriority="high"
                decoding="async"
                src={category.icon}
                alt=""
                className="h-28 w-28 shrink-0 object-contain sm:h-36 sm:w-36 lg:h-44 lg:w-44"
              />
            )}
            <div className="flex-1 text-center lg:text-right">
              <h1 className="text-[26px] font-black leading-[1.5] text-[var(--hl-red-text)] sm:text-[36px] xl:text-[40px]">
                {heading}
              </h1>
              {intro && (
                <p className="mx-auto mt-4 max-w-3xl text-[15px] leading-8 text-[var(--hl-ink-2)] sm:text-[17px] sm:leading-9 lg:mx-0">
                  {intro}
                </p>
              )}
              <p className="mt-4 text-[14px] font-bold text-[var(--hl-muted)]">
                {products.length} محصول در این دسته
              </p>
            </div>
          </div>
        </div>
      </section>

      <ProductsBrowser products={products.map(toCardData)} categories={categories} initialCatId={category.id} />

      <HomeNewsletter />
    </>
  );
}
