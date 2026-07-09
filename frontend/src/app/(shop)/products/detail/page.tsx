import Link from "next/link";
import { api } from "@/lib/api";
import { formatNumber, formatToman, toFa } from "@/lib/format";
import type { Product, Comment } from "@/lib/types";
import Stars from "@/components/Stars";
import BestSellersCarousel, { type CarouselCard } from "@/components/home/BestSellersCarousel";
import PurchaseCard from "@/components/product/PurchaseCard";
import ProductTabs, { TrustItem } from "@/components/product/ProductTabs";
import OpenChatButton from "@/components/product/OpenChatButton";
import ProductGallery from "@/components/product/ProductGallery";
import HomeNewsletter from "@/components/home/HomeNewsletter";

export const dynamic = "force-dynamic";
export const metadata = { title: "جزئیات محصول | Phoenix Verify" };

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

export default async function ProductDetailPage({ searchParams }: { searchParams: Promise<{ id?: string }> }) {
  const { id } = await searchParams;

  let product: Product | null = null;
  let related: Product[] = [];
  let failed = false;
  try {
    const products = await api.products.list();
    const active = products.filter((p) => p.isActive);
    product = (id ? active.find((p) => p.id === Number(id)) : null) ?? active[0] ?? null;
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

  if (failed || !product) {
    return (
      <div className="mx-auto max-w-[640px] px-6 py-24 text-center">
        <div className="rounded-[22px] border bg-white p-10" style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}>
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

  // plan comparison data: months columns × type rows, from real plans.
  const plans = product.plans.filter((p) => p.isActive);
  const months = [...new Set(plans.map((p) => p.months))].sort((a, b) => a - b);
  const types = [...new Set(plans.map((p) => p.type))];
  const bestDiscount = Math.max(0, ...plans.map((p) => p.discountPercent));

  return (<>
    <div className="mx-auto max-w-[1840px] px-4 pb-16 pt-6 sm:px-8 xl:px-16">
      {/* breadcrumb */}
      <nav className="mb-5 flex flex-wrap items-center gap-2 text-[13px]" style={{ color: "var(--ac-muted)" }}>
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
            <img src={product.logo} alt="" className="h-12 w-12 shrink-0 rounded-xl object-contain" />
          )}
          <h1 className="text-[24px] font-black leading-snug" style={{ color: "var(--ac-title)" }}>{product.name}</h1>
        </div>
        {rated.length > 0 && (
          <div className="mt-2 flex items-center gap-2">
            <span className="text-[15px] font-black" style={{ color: "var(--ac-title)" }}>{toFa(avg.toFixed(1))}</span>
            <Stars value={avg} />
            <span className="text-[13px]" style={{ color: "var(--ac-muted)" }}>({formatNumber(rated.length)} نظر)</span>
          </div>
        )}
      </div>

      {/* main grid — purchase card pinned right, gallery on the left (desktop) */}
      <div className="grid items-start gap-6 lg:grid-cols-[320px_1fr_420px]">
        {/* gallery */}
        <div className="order-1 lg:order-3">
          <ProductGallery image={product.image} gallery={product.gallery ?? []} name={product.name} featured={product.featured} out={out} />
        </div>

        {/* info */}
        <div className="order-2 lg:order-2">
          <div className="hidden lg:block">
            <div className="flex items-center gap-3">
              {product.logo && (
                <img src={product.logo} alt="" className="h-14 w-14 shrink-0 rounded-xl object-contain" />
              )}
              <h1 className="text-[30px] font-black leading-snug" style={{ color: "var(--ac-title)" }}>{product.name}</h1>
            </div>
            {rated.length > 0 && (
              <div className="mt-2 flex items-center gap-2">
                <span className="text-[15px] font-black" style={{ color: "var(--ac-title)" }}>{toFa(avg.toFixed(1))}</span>
                <Stars value={avg} />
                <span className="text-[13px]" style={{ color: "var(--ac-muted)" }}>({formatNumber(rated.length)} نظر)</span>
              </div>
            )}
          </div>

          <p className="mt-3 text-[14px] leading-8 line-clamp-3" style={{ color: "var(--ac-text)" }}>
            {product.description.replace(/[#*_\[\]()]/g, "").slice(0, 220)}
          </p>

          {/* mini benefits */}
          <div className="mt-5 grid grid-cols-3 gap-2.5">
            {[
              { icon: I.headset, label: "پشتیبانی ۲۴/۷" },
              { icon: I.shield, label: "ضمانت اصالت" },
              { icon: I.bolt, label: "تحویل آنی" },
            ].map((b) => (
              <div key={b.label} className="flex flex-col items-center gap-2 rounded-xl border px-2 py-3.5 text-center" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)" }}>
                <span style={{ color: "#F2551F" }}><Icon d={b.icon} /></span>
                <span className="text-[12px] font-bold" style={{ color: "var(--ac-text)" }}>{b.label}</span>
              </div>
            ))}
          </div>

          {/* stock box */}
          <div className={`mt-4 flex items-center justify-between rounded-xl px-4 py-3.5 ${out ? "bg-rose-500/10" : "bg-emerald-500/10"}`}>
            <span className={`flex items-center gap-2 text-[13px] font-black ${out ? "text-rose-500" : "text-emerald-600"}`}>
              <span className={`h-2 w-2 rounded-full ${out ? "bg-rose-500" : "bg-emerald-500"}`} />
              {out ? "موجود نیست" : "موجود در انبار"}
            </span>
            {!out && <span className="text-[13px] font-bold text-emerald-600">تحویل آنی</span>}
          </div>

          {/* sku */}
          {product.sku && (
            <p className="mt-4 text-[12px]" style={{ color: "var(--ac-muted)" }}>
              شناسه محصول: <span className="font-mono" dir="ltr">{product.sku}</span>
            </p>
          )}
        </div>

        {/* purchase card */}
        <div className="order-3 lg:sticky lg:top-[100px] lg:order-1">
          <PurchaseCard product={product} />
        </div>

        {/* trust row — below gallery+info, beside purchase card */}
        <div className="order-4 lg:col-start-2 lg:col-span-2 grid grid-cols-2 rounded-[22px] border bg-white sm:grid-cols-3 lg:grid-cols-5" style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}>
          <TrustItem icon={<Icon d={I.tag} />} title="قیمت مناسب" desc="بهترین قیمت بازار" />
          <TrustItem icon={<Icon d={I.shield} />} title="ضمانت اصالت" desc="اشتراک کاملاً قانونی" />
          <TrustItem icon={<Icon d={I.lock} />} title="پرداخت امن" desc="درگاه مطمئن و رمزنگاری‌شده" />
          <TrustItem icon={<Icon d={I.headset} />} title="پشتیبانی ۲۴/۷" desc="همیشه پاسخگوی شما" />
          <TrustItem icon={<Icon d={I.bolt} />} title="تحویل آنی" desc="بلافاصله پس از پرداخت" />
        </div>
      </div>

      {/* tabs */}
      <ProductTabs product={product} comments={comments} />

      {/* support + comparison */}
      <div className="mt-10 grid items-start gap-6 lg:grid-cols-[320px_1fr]">
        <div className="rounded-[22px] border bg-white p-6 text-center" style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}>
          <span className="mx-auto grid h-14 w-14 place-items-center rounded-full" style={{ background: "var(--ac-stat-icon-orange-bg)", color: "#F2551F" }}>
            <Icon d={I.headset} className="h-7 w-7" />
          </span>
          <h3 className="mt-4 text-[17px] font-black" style={{ color: "var(--ac-title)" }}>سوالی دارید؟</h3>
          <p className="mt-2 text-[13px] leading-6" style={{ color: "var(--ac-muted)" }}>تیم پشتیبانی فونیکس وریفای به‌صورت شبانه‌روزی آماده‌ی پاسخگویی است.</p>
          <Link href="/account/tickets" className="mt-5 flex h-12 items-center justify-center rounded-xl text-[14px] font-black text-white transition hover:brightness-105" style={{ background: "var(--ac-btn)" }}>
            ارسال تیکت
          </Link>
          <OpenChatButton />
        </div>

        {months.length > 0 && (
          <div id="plan-compare" className="overflow-hidden rounded-[22px] border bg-white" style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}>
            <h3 className="border-b px-6 py-4 text-[16px] font-black" style={{ borderColor: "var(--ac-divider)", color: "var(--ac-title)" }}>مقایسه پلن‌ها</h3>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[520px] border-collapse text-center text-[13px]">
                <thead>
                  <tr style={{ background: "var(--ac-menu-hover)" }}>
                    <th className="px-4 py-3.5 text-right font-bold" style={{ color: "var(--ac-text)" }}>نوع پلن</th>
                    {months.map((m) => {
                      const best = plans.some((p) => p.months === m && p.discountPercent === bestDiscount && bestDiscount > 0);
                      return (
                        <th key={m} className="px-4 py-3.5 font-black" style={{ color: "var(--ac-title)" }}>
                          {toFa(m)} ماهه
                          {best && <span className="mr-1.5 rounded-full bg-emerald-500 px-2 py-0.5 text-[10px] font-black text-white">٪{toFa(bestDiscount)}−</span>}
                        </th>
                      );
                    })}
                  </tr>
                </thead>
                <tbody>
                  {types.map((t) => (
                    <tr key={t} className="border-t" style={{ borderColor: "var(--ac-divider)" }}>
                      <td className="px-4 py-3.5 text-right font-bold" style={{ color: "var(--ac-title)" }}>{t}</td>
                      {months.map((m) => {
                        const p = plans.find((x) => x.type === t && x.months === m);
                        return (
                          <td key={m} className="px-4 py-3.5" style={{ color: "var(--ac-text)" }}>
                            {p ? (
                              <span className="inline-flex flex-col items-center gap-0.5">
                                <span className="font-bold">{formatToman(p.finalPrice)}</span>
                                {p.userCount > 0 && <span className="text-[11px]" style={{ color: "var(--ac-muted)" }}>{toFa(p.userCount)} کاربره</span>}
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
                      <td className="px-4 py-3 text-right text-[12px]" style={{ color: "var(--ac-text)" }}>{f.text}</td>
                      {months.map((m) => (
                        <td key={m} className="px-4 py-3 text-emerald-500"><Icon d={I.check} className="mx-auto h-4 w-4" /></td>
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

    {/* related products — full width like homepage */}
    {related.length > 0 && (
      <section className="mx-auto max-w-[1840px] px-4 sm:px-8 xl:px-16 py-4 mt-8">
        <div className="mb-8 flex items-end justify-between">
          <div className="flex items-start gap-2">
            <span className="mt-2.5 h-6 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f]" />
            <div>
              <h2 className="text-[22px] sm:text-[26px] font-black text-[var(--hl-ink)]">محصولات مرتبط</h2>
              <p className="mt-1.5 text-[15px] text-[var(--hl-ink-2)]">پیشنهادهای مشابه برای شما</p>
            </div>
          </div>
          <Link
            href="/products"
            className="shrink-0 rounded-xl border border-[var(--hl-border)] bg-white px-4 py-2 text-[16px] font-bold text-[var(--hl-red)] transition hover:bg-[#fff6f2]"
          >
            مشاهده همه
          </Link>
        </div>
        <BestSellersCarousel products={related.map((p): CarouselCard => ({
          key: String(p.id),
          name: p.name,
          categoryName: p.categoryName,
          priceLabel: formatToman(Math.min(p.finalPrice, ...p.plans.filter((x) => x.isActive).map((x) => x.finalPrice))),
          badge: p.featured ? "پرفروش" : "تحویل فوری",
          image: p.image,
          href: `/products/detail?id=${p.id}`,
        }))} />
      </section>
    )}

    {/* newsletter */}
    <HomeNewsletter />
  </>
  );
}
