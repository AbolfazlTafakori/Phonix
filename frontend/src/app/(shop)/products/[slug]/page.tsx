import Link from "next/link";
import { notFound, permanentRedirect } from "next/navigation";
import { api } from "@/lib/api";
import { formatNumber, formatToman, productDisplayPrice, toFa } from "@/lib/format";
import type { Product, Comment } from "@/lib/types";
import Stars from "@/components/Stars";
import BestSellersCarousel, { type CarouselCard } from "@/components/home/BestSellersCarousel";
import PurchaseCard from "@/components/product/PurchaseCard";
import ProductTabs, { TrustItem } from "@/components/product/ProductTabs";
import OpenChatButton from "@/components/product/OpenChatButton";
import ProductGallery from "@/components/product/ProductGallery";
import HomeNewsletter from "@/components/home/HomeNewsletter";
import { absoluteUrl, jsonLdScript, latinBrand, plainExcerpt, productPath, productSlug, productTitle } from "@/lib/seo";

export const dynamic = "force-dynamic";

// slug format: "{id}-{name-slug}" — resolve by the numeric id prefix.
function idFromSlug(slug: string): number | null {
  const m = /^(\d+)/.exec(decodeURIComponent(slug));
  return m ? Number(m[1]) : null;
}

async function findProduct(slug: string): Promise<Product | null> {
  const id = idFromSlug(slug);
  if (id == null) return null;
  const products = await api.products.list();
  return products.find((p) => p.isActive && p.id === id) ?? null;
}

export async function generateMetadata({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  try {
    const product = await findProduct(slug);
    if (!product) return { title: "جزئیات محصول" };
    const description = plainExcerpt(product.description);
    const canonical = productPath(product);
    const title = productTitle(product.name);
    return {
      title,
      description,
      alternates: { canonical },
      openGraph: {
        type: "website",
        title: `${title} | Phoenix Verify`,
        description,
        url: canonical,
        images: product.image ? [{ url: product.image, alt: product.name }] : undefined,
      },
      twitter: {
        card: "summary_large_image",
        title: `${title} | Phoenix Verify`,
        description,
        images: product.image ? [product.image] : undefined,
      },
    };
  } catch {
    return { title: "جزئیات محصول" };
  }
}

const Icon = ({ d, className = "h-5 w-5" }: { d: string; className?: string }) => (
  <svg viewBox="0 0 24 24" className={className} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d={d} /></svg>
);

const I = {
  headset: "M3 12a9 9 0 0 1 18 0M3 12v4a2 2 0 0 0 2 2h1a1 1 0 0 0 1-1v-4a1 1 0 0 0-1-1H4M21 12v4a2 2 0 0 1-2 2h-1a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1h2",
  shield: "M12 2l8 4v6c0 5-3.5 8.5-8 10-4.5-1.5-8-5-8-10V6z",
  bolt: "M13 2L3 14h7l-1 8 11-13h-7z",
  tag: "M20 10L11 2H4v7l9 9a2 2 0 0 0 2.8 0l4.2-4.2a2 2 0 0 0 0-2.8zM7 7h.01",
  lock: "M5 11h14v10H5zM8 11V7a4 4 0 0 1 8 0v4",
  check: "M20 6L9 17l-5-5",
};

export default async function ProductDetailPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;

  let product: Product | null = null;
  let related: Product[] = [];
  let failed = false;
  const id = idFromSlug(slug);
  if (id == null) notFound();
  try {
    const products = await api.products.list();
    const active = products.filter((p) => p.isActive);
    product = active.find((p) => p.id === id) ?? null;
    if (product) {
      const pid = product.id;
      related = active.filter((p) => p.id !== pid && p.categoryId === product!.categoryId).slice(0, 6);
      if (related.length < 3) {
        const extra = active.filter((p) => p.id !== pid && !related.includes(p)).slice(0, 6 - related.length);
        related = [...related, ...extra];
      }
    }
  } catch {
    failed = true;
  }

  // Unknown id → 404; wrong/renamed slug → 301 to the canonical URL.
  if (!failed && !product) notFound();
  if (product && decodeURIComponent(slug) !== productSlug(product)) {
    permanentRedirect(productPath(product));
  }

  if (failed || !product) {
    return (
      <div className="mx-auto max-w-[640px] px-4 py-24 text-center sm:px-6">
        <div className="rounded-[22px] border bg-[var(--ac-panel-bg)] p-6 sm:p-10" style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}>
          <p className="text-lg font-black" style={{ color: "var(--ac-title)" }}>مشکلی در دریافت اطلاعات محصول پیش آمد.</p>
          <p className="mt-2 text-sm" style={{ color: "var(--ac-muted)" }}>لطفاً چند لحظه بعد دوباره تلاش کنید.</p>
          <a href="" className="mt-6 inline-block rounded-xl px-8 py-3 text-sm font-bold text-white transition hover:brightness-105" style={{ background: "var(--ac-btn)" }}>
            تلاش مجدد
          </a>
        </div>
      </div>
    );
  }

  let comments: Comment[] = [];
  try {
    comments = await api.comments.forProduct(product.id);
  } catch {
    // reviews are optional
  }
  const topLevel = comments.filter((c) => c.parentId == null);
  const rated = topLevel.filter((c) => c.rating > 0);
  const avg = rated.length ? rated.reduce((s, c) => s + c.rating, 0) / rated.length : 0;
  const out = product.stock <= 0;

  const plans = product.plans.filter((p) => p.isActive);
  const months = [...new Set(plans.map((p) => p.months))].sort((a, b) => a - b);
  const types = [...new Set(plans.map((p) => p.type))];
  const bestDiscount = Math.max(0, ...plans.map((p) => p.discountPercent));

  const prices = plans.length ? plans.map((p) => p.finalPrice) : [product.finalPrice];
  const productLd = {
    "@context": "https://schema.org",
    "@type": "Product",
    name: product.name,
    description: plainExcerpt(product.description, 300),
    image: product.image ? absoluteUrl(product.image) : undefined,
    sku: product.sku || undefined,
    ...(latinBrand(product.name) && { brand: { "@type": "Brand", name: latinBrand(product.name) } }),
    ...(rated.length > 0 && {
      aggregateRating: {
        "@type": "AggregateRating",
        ratingValue: Number(avg.toFixed(1)),
        reviewCount: rated.length,
      },
    }),
    offers: {
      "@type": "AggregateOffer",
      // Prices are stored in Toman; IRR (the ISO 4217 code Google accepts) is 10 rials per toman.
      priceCurrency: "IRR",
      lowPrice: Math.min(...prices) * 10,
      highPrice: Math.max(...prices) * 10,
      offerCount: Math.max(plans.length, 1),
      availability: out ? "https://schema.org/OutOfStock" : "https://schema.org/InStock",
      url: absoluteUrl(productPath(product)),
    },
  };
  const faq = (product.faq ?? []).filter((f) => f.question.trim() && f.answer.trim());
  const faqLd = faq.length > 0 ? {
    "@context": "https://schema.org",
    "@type": "FAQPage",
    mainEntity: faq.map((f) => ({
      "@type": "Question",
      name: f.question,
      acceptedAnswer: { "@type": "Answer", text: f.answer },
    })),
  } : null;
  const breadcrumbLd = {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: [
      { "@type": "ListItem", position: 1, name: "صفحه اصلی", item: absoluteUrl("/") },
      { "@type": "ListItem", position: 2, name: "محصولات", item: absoluteUrl("/products") },
      ...(product.categoryName
        ? [{ "@type": "ListItem", position: 3, name: product.categoryName, item: absoluteUrl(`/products?cat=${product.categoryId}`) }]
        : []),
      { "@type": "ListItem", position: product.categoryName ? 4 : 3, name: product.name, item: absoluteUrl(productPath(product)) },
    ],
  };

  return (<>
    <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdScript(productLd) }} />
    <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdScript(breadcrumbLd) }} />
    {faqLd && <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdScript(faqLd) }} />}
    <div className="mx-auto max-w-[1840px] px-4 pb-16 pt-6 sm:px-6 lg:px-8 xl:px-16">
      {/* breadcrumb */}
      <nav className="mb-5 flex flex-wrap items-center gap-1.5 text-[12px] sm:gap-2 sm:text-[13px]" style={{ color: "var(--ac-muted)" }}>
        <Link href="/" className="transition hover:text-[color:var(--ac-title)]">صفحه اصلی</Link>
        <span>/</span>
        <Link href="/products" className="transition hover:text-[color:var(--ac-title)]">محصولات</Link>
        {product.categoryName && (
          <>
            <span>/</span>
            <Link href={`/products?cat=${product.categoryId}`} className="transition hover:text-[color:var(--ac-title)]">{product.categoryName}</Link>
          </>
        )}
        <span>/</span>
        <span className="font-bold" style={{ color: "var(--ac-title)" }}>{product.name}</span>
      </nav>

      {/* mobile title + logo + rating */}
      <div className="mb-4 lg:hidden">
        <div className="flex items-center gap-3">
          {product.logo && (
            <img loading="lazy" decoding="async" src={product.logo} alt="" className="h-10 w-10 shrink-0 rounded-xl object-contain sm:h-12 sm:w-12" />
          )}
          <h1 className="text-[20px] font-black leading-snug sm:text-[24px]" style={{ color: "var(--ac-title)" }}>{product.name}</h1>
        </div>
        {rated.length > 0 && (
          <div className="mt-2 flex items-center gap-2">
            <span className="text-[15px] font-black" style={{ color: "var(--ac-title)" }}>{toFa(avg.toFixed(1))}</span>
            <Stars value={avg} />
            <span className="text-[13px]" style={{ color: "var(--ac-muted)" }}>({formatNumber(rated.length)} نظر)</span>
          </div>
        )}
      </div>

      {/* main grid — mobile: gallery → info → purchase stacked; desktop: purchase | info | gallery */}
      <div className="grid items-start gap-5 sm:gap-6 lg:grid-cols-[minmax(280px,320px)_1fr_minmax(320px,420px)]">
        {/* gallery — first on mobile; left column, top row on desktop */}
        <div className="order-1 lg:col-start-3 lg:row-start-1">
          <ProductGallery image={product.image} gallery={product.gallery ?? []} name={product.name} featured={product.featured} out={out} />
        </div>

        {/* info — middle column, top row on desktop */}
        <div className="order-2 lg:col-start-2 lg:row-start-1">
          <div className="hidden lg:block">
            <div className="flex items-center gap-3">
              {product.logo && (
                <img loading="lazy" decoding="async" src={product.logo} alt="" className="h-14 w-14 shrink-0 rounded-xl object-contain" />
              )}
              {/* the page's single <h1> lives in the mobile block above; this desktop copy stays a <p> */}
              <p className="text-[26px] font-black leading-snug xl:text-[30px]" style={{ color: "var(--ac-title)" }}>{product.name}</p>
            </div>
            {rated.length > 0 && (
              <div className="mt-2 flex items-center gap-2">
                <span className="text-[15px] font-black" style={{ color: "var(--ac-title)" }}>{toFa(avg.toFixed(1))}</span>
                <Stars value={avg} />
                <span className="text-[13px]" style={{ color: "var(--ac-muted)" }}>({formatNumber(rated.length)} نظر)</span>
              </div>
            )}
          </div>

          <p className="mt-3 text-[13px] leading-8 line-clamp-3 sm:text-[14px]" style={{ color: "var(--ac-text)" }}>
            {product.description.replace(/[#*_\[\]()]/g, "").slice(0, 220)}
          </p>

          {/* mini benefits */}
          <div className="mt-4 grid grid-cols-3 gap-2 sm:mt-5 sm:gap-2.5">
            {[
              { icon: I.headset, label: "پشتیبانی ۲۴/۷" },
              { icon: I.shield, label: "ضمانت اصالت" },
              { icon: I.bolt, label: "تحویل آنی" },
            ].map((b) => (
              <div key={b.label} className="flex flex-col items-center gap-1.5 rounded-xl border px-2 py-2.5 text-center sm:gap-2 sm:py-3.5" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)" }}>
                <span style={{ color: "#F2551F" }}><Icon d={b.icon} className="h-4 w-4 sm:h-5 sm:w-5" /></span>
                <span className="text-[10px] font-bold sm:text-[12px]" style={{ color: "var(--ac-text)" }}>{b.label}</span>
              </div>
            ))}
          </div>

          {/* stock box */}
          <div className={`mt-3 flex items-center justify-between rounded-xl px-3 py-3 sm:mt-4 sm:px-4 sm:py-3.5 ${out ? "bg-rose-500/10" : "bg-emerald-500/10"}`}>
            <span className={`flex items-center gap-2 text-[12px] font-black sm:text-[13px] ${out ? "text-rose-500" : "text-emerald-600"}`}>
              <span className={`h-2 w-2 rounded-full ${out ? "bg-rose-500" : "bg-emerald-500"}`} />
              {out ? "موجود نیست" : "موجود در انبار"}
            </span>
            {!out && <span className="text-[12px] font-bold text-emerald-600 sm:text-[13px]">تحویل آنی</span>}
          </div>

          {/* sku */}
          {product.sku && (
            <p className="mt-3 text-[11px] sm:mt-4 sm:text-[12px]" style={{ color: "var(--ac-muted)" }}>
              شناسه محصول: <span className="font-mono" dir="ltr">{product.sku}</span>
            </p>
          )}
        </div>

        {/* purchase card — right column on desktop, spanning both content rows so the trust row can sit
            in the gap under the shorter gallery/info instead of below this tall card */}
        <div className="order-3 lg:sticky lg:top-[100px] lg:col-start-1 lg:row-start-1 lg:row-span-2">
          <PurchaseCard product={product} />
        </div>

        {/* trust row — full width on mobile; on desktop it fills the second row under gallery+info */}
        <div className="order-4 grid grid-cols-2 gap-px overflow-hidden rounded-[22px] border bg-[var(--ac-panel-bg)] sm:grid-cols-3 lg:col-start-2 lg:col-end-4 lg:row-start-2 lg:grid-cols-5" style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}>
          <TrustItem icon={<Icon d={I.tag} />} title="قیمت مناسب" desc="بهترین قیمت بازار" />
          <TrustItem icon={<Icon d={I.shield} />} title="ضمانت اصالت" desc="اشتراک کاملاً قانونی" />
          <TrustItem icon={<Icon d={I.lock} />} title="پرداخت امن" desc="درگاه مطمئن و رمزنگاری‌شده" />
          <TrustItem icon={<Icon d={I.headset} />} title="پشتیبانی ۲۴/۷" desc="همیشه پاسخگوی شما" />
          <TrustItem icon={<Icon d={I.bolt} />} title="تحویل آنی" desc="بلافاصله پس از پرداخت" />
        </div>
      </div>

      {/* tabs */}
      <ProductTabs product={product} comments={comments} />

      {/* FAQ — server-rendered so crawlers and AI answer engines read it without JS */}
      {faq.length > 0 && (
        <section className="mt-8 rounded-[22px] border bg-[var(--ac-panel-bg)] p-5 sm:mt-10 sm:p-8" style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}>
          <div className="mb-5 flex items-start gap-2 sm:mb-6">
            <span className="mt-1 h-5 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f] sm:h-6" />
            <div>
              <h2 className="text-[18px] font-black sm:text-[22px]" style={{ color: "var(--ac-title)" }}>سوالات متداول درباره {product.name}</h2>
              <p className="mt-1 text-[13px] sm:text-[15px]" style={{ color: "var(--ac-muted)" }}>پاسخ پرتکرارترین پرسش‌های خریداران</p>
            </div>
          </div>
          <div className="divide-y" style={{ borderColor: "var(--ac-divider)" }}>
            {faq.map((f, i) => (
              <details key={i} className="group py-1" style={{ borderColor: "var(--ac-divider)" }}>
                <summary className="flex cursor-pointer list-none items-center justify-between gap-3 py-4 text-[14px] font-black sm:text-[15px]" style={{ color: "var(--ac-title)" }}>
                  {f.question}
                  <span className="shrink-0 text-[var(--ac-muted)] transition-transform group-open:rotate-45" aria-hidden>+</span>
                </summary>
                <p className="pb-4 text-[13px] leading-8 sm:text-[14px]" style={{ color: "var(--ac-text)" }}>{f.answer}</p>
              </details>
            ))}
          </div>
        </section>
      )}

      {/* support + comparison */}
      <div className="mt-8 grid items-start gap-5 sm:mt-10 sm:gap-6 lg:grid-cols-[280px_1fr]">
        <div className="rounded-[22px] border bg-[var(--ac-panel-bg)] p-5 text-center sm:p-6" style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}>
          <span className="mx-auto grid h-12 w-12 place-items-center rounded-full sm:h-14 sm:w-14" style={{ background: "var(--ac-stat-icon-orange-bg)", color: "#F2551F" }}>
            <Icon d={I.headset} className="h-6 w-6 sm:h-7 sm:w-7" />
          </span>
          <h3 className="mt-3 text-[15px] font-black sm:mt-4 sm:text-[17px]" style={{ color: "var(--ac-title)" }}>سوالی دارید؟</h3>
          <p className="mt-1.5 text-[12px] leading-6 sm:mt-2 sm:text-[13px]" style={{ color: "var(--ac-muted)" }}>تیم پشتیبانی فونیکس وریفای به‌صورت شبانه‌روزی آماده‌ی پاسخگویی است.</p>
          <Link href="/account/tickets" className="mt-4 flex h-11 items-center justify-center rounded-xl text-[13px] font-black text-white transition hover:brightness-105 sm:mt-5 sm:h-12 sm:text-[14px]" style={{ background: "var(--ac-btn)" }}>
            ارسال تیکت
          </Link>
          <OpenChatButton />
        </div>

        {months.length > 0 && (
          <div id="plan-compare" className="overflow-hidden rounded-[22px] border bg-[var(--ac-panel-bg)]" style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}>
            <h3 className="border-b px-4 py-3.5 text-[14px] font-black sm:px-6 sm:py-4 sm:text-[16px]" style={{ borderColor: "var(--ac-divider)", color: "var(--ac-title)" }}>مقایسه پلن‌ها</h3>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[420px] border-collapse text-center text-[12px] sm:min-w-[520px] sm:text-[13px]">
                <thead>
                  <tr style={{ background: "var(--ac-menu-hover)" }}>
                    <th className="px-3 py-3 text-right font-bold sm:px-4 sm:py-3.5" style={{ color: "var(--ac-text)" }}>نوع پلن</th>
                    {months.map((m) => {
                      const best = plans.some((p) => p.months === m && p.discountPercent === bestDiscount && bestDiscount > 0);
                      return (
                        <th key={m} className="px-3 py-3 font-black sm:px-4 sm:py-3.5" style={{ color: "var(--ac-title)" }}>
                          {toFa(m)} ماهه
                          {best && <span className="mr-1 rounded-full bg-emerald-500 px-1.5 py-0.5 text-[9px] font-black text-white sm:mr-1.5 sm:px-2 sm:text-[10px]">٪{toFa(bestDiscount)}−</span>}
                        </th>
                      );
                    })}
                  </tr>
                </thead>
                <tbody>
                  {types.map((t) => (
                    <tr key={t} className="border-t" style={{ borderColor: "var(--ac-divider)" }}>
                      <td className="px-3 py-3 text-right font-bold sm:px-4 sm:py-3.5" style={{ color: "var(--ac-title)" }}>{t}</td>
                      {months.map((m) => {
                        const p = plans.find((x) => x.type === t && x.months === m);
                        return (
                          <td key={m} className="px-3 py-3 sm:px-4 sm:py-3.5" style={{ color: "var(--ac-text)" }}>
                            {p ? (
                              <span className="inline-flex flex-col items-center gap-0.5">
                                <span className="font-bold">{formatToman(p.finalPrice)}</span>
                                {p.userCount > 0 && <span className="text-[10px] sm:text-[11px]" style={{ color: "var(--ac-muted)" }}>{toFa(p.userCount)} کاربره</span>}
                              </span>
                            ) : (
                              <span style={{ color: "var(--ac-muted)" }}>—</span>
                            )}
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                  {product.features.filter((f) => f.included).slice(0, 3).map((f) => (
                    <tr key={f.text} className="border-t" style={{ borderColor: "var(--ac-divider)" }}>
                      <td className="px-3 py-2.5 text-right text-[11px] sm:px-4 sm:py-3 sm:text-[12px]" style={{ color: "var(--ac-text)" }}>{f.text}</td>
                      {months.map((m) => (
                        <td key={m} className="px-3 py-2.5 text-emerald-500 sm:px-4 sm:py-3"><Icon d={I.check} className="mx-auto h-3.5 w-3.5 sm:h-4 sm:w-4" /></td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>

    </div>

    {/* related products — full width */}
    {related.length > 0 && (
      <section className="mx-auto max-w-[1840px] px-4 py-4 mt-6 sm:px-6 sm:mt-8 lg:px-8 xl:px-16">
        <div className="mb-6 flex flex-wrap items-end justify-between gap-3 sm:mb-8">
          <div className="flex items-start gap-2">
            <span className="mt-2 h-5 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f] sm:mt-2.5 sm:h-6" />
            <div>
              <h2 className="text-[18px] font-black text-[var(--hl-ink)] sm:text-[22px] lg:text-[26px]">محصولات مرتبط</h2>
              <p className="mt-1 text-[13px] text-[var(--hl-ink-2)] sm:mt-1.5 sm:text-[15px]">پیشنهادهای مشابه برای شما</p>
            </div>
          </div>
          <Link
            href="/products"
            className="shrink-0 rounded-xl border border-[var(--hl-border)] bg-[var(--ac-panel-bg)] px-3 py-1.5 text-[13px] font-bold text-[var(--hl-red)] transition hover:bg-[#fff6f2] sm:px-4 sm:py-2 sm:text-[16px]"
          >
            مشاهده همه
          </Link>
        </div>
        <BestSellersCarousel products={related.map((p): CarouselCard => ({
          key: String(p.id),
          name: p.name,
          categoryName: p.categoryName,
          priceLabel: formatToman(productDisplayPrice(p)),
          badge: p.featured ? "پرفروش" : "تحویل فوری",
          image: p.image,
          href: productPath(p),
        }))} />
      </section>
    )}

    {/* newsletter */}
    <HomeNewsletter />
  </>
  );
}
