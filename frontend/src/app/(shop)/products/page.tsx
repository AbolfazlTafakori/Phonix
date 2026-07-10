import Link from "next/link";
import { api } from "@/lib/api";
import { formatNumber } from "@/lib/format";
import ProductCardImage from "@/components/ProductCardImage";
import type { Product, Category } from "@/lib/types";

export const dynamic = "force-dynamic";
export const metadata = { title: "محصولات | Phoenix Verify" };

const heroStats = [
  { value: "+10,000", label: "سفارش موفق", icon: "/figma/stat-orders.png" },
  { value: "+5,000", label: "مشتری رضایت‌مند", icon: "/figma/stat-customers.png" },
  { value: "99%", label: "تضمین کیفیت", icon: "/figma/stat-satisfaction.png" },
];

export default async function FilmsPage({ searchParams }: { searchParams: Promise<{ cat?: string; q?: string }> }) {
  const { cat, q } = await searchParams;
  const query = (q ?? "").trim();
  let products: Product[] = [];
  let categories: Category[] = [];
  try {
    [products, categories] = await Promise.all([api.products.list(), api.categories.list()]);
  } catch {
    // backend unavailable
  }

  const active = products.filter((p) => p.isActive);
  const selected = cat ? Number(cat) : 0;
  let shown = selected ? active.filter((p) => p.categoryId === selected) : active;
  if (query) {
    const needle = query.toLowerCase();
    shown = shown.filter((p) => p.name.toLowerCase().includes(needle) || p.sku.toLowerCase().includes(needle));
  }
  const activeCats = categories.filter((c) => c.isActive);

  return (
    <>
      {/* ── Hero (hidden while searching — a compact results header shows instead) ── */}
      {!query && (
        <section className="overflow-hidden border-b border-[var(--hl-border)] bg-[var(--hl-surface)]">
          <div className="mx-auto max-w-[1840px] px-4 sm:px-8 xl:px-16">
            <nav className="flex items-center justify-end gap-2 pb-2 pt-6 text-[13px] text-[var(--hl-muted)]">
              <Link href="/" className="flex items-center gap-1 transition hover:text-[var(--hl-red)]">
                <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 9.5L12 3l9 6.5V20a1 1 0 01-1 1H4a1 1 0 01-1-1z" /><path d="M9 22V12h6v10" /></svg>
                خانه
              </Link>
              <span>/</span>
              <span className="font-medium text-[var(--hl-ink)]">محصولات</span>
            </nav>

            <div className="flex flex-col items-center gap-7 pb-10 pt-2 sm:pt-4 lg:flex-row-reverse lg:items-center lg:gap-4 xl:gap-8">
              <div className="shrink-0 lg:w-[32%] xl:w-[34%]">
                <img src="/figma/productpage-hero-shield.png" alt="" className="mx-auto h-auto w-52 max-w-full object-contain sm:w-64 lg:w-full lg:max-w-[380px] xl:max-w-[430px]" />
              </div>

              <div className="flex-1 text-center lg:pr-6 lg:text-right xl:pr-14">
                <h1 className="text-[24px] font-black leading-[1.5] text-[var(--hl-red)] sm:text-[32px] xl:text-[38px]">
                  محصولات فونیکس وریفای
                </h1>
                <p className="mx-auto mt-3 max-w-xl text-[14px] leading-7 text-[var(--hl-muted)] sm:mt-4 sm:text-[16px] sm:leading-8 lg:mx-0">
                  بزرگ‌ترین مرجع خرید محصولات دیجیتال و خدمات مجازی با تحویل سریع
                  <br className="hidden sm:inline" />
                  و تضمین اصالت
                </p>
                <div className="mt-6 flex flex-nowrap items-center justify-center gap-x-4 sm:gap-x-6 lg:justify-start">
                  {heroStats.map((s) => (
                    <div key={s.label} className="flex shrink-0 items-center gap-2">
                      <img src={s.icon} alt="" aria-hidden className="h-8 w-8 shrink-0 object-contain sm:h-11 sm:w-11" />
                      <div className="text-right">
                        <div className="whitespace-nowrap text-[15px] font-black leading-none text-[var(--hl-ink)] sm:text-[20px]">{s.value}</div>
                        <div className="mt-1 whitespace-nowrap text-[11px] font-bold text-[var(--hl-muted)] sm:text-[13px]">{s.label}</div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              <div className="w-full max-w-md shrink-0 rounded-[22px] border border-[var(--hl-border)] bg-[#fdf0ec]/60 p-5 text-right shadow-sm sm:p-6 lg:w-[330px] lg:max-w-none xl:w-[370px]">
                <h3 className="text-[19px] font-black leading-[1.5] text-[var(--hl-ink)]">دسترسی جهانی، پرداخت امن</h3>
                <p className="mt-2 text-[13px] leading-7 text-[var(--hl-muted)]">بهترین اشتراک‌ها و خدمات دیجیتال را با قیمت مناسب و تحویل آنی تهیه کنید.</p>
                <div className="mt-4 flex items-end justify-between gap-3">
                  <img src="/figma/productpage-hero-offer.png" alt="" className="-mb-1 h-24 w-24 shrink-0 object-contain sm:h-28 sm:w-28" />
                  <Link
                    href="/products"
                    className="mb-2 inline-flex items-center gap-2 rounded-xl border border-[var(--hl-red)] px-5 py-2.5 text-[14px] font-bold text-[var(--hl-red)] transition hover:bg-[#fff2ee]"
                  >
                    مشاهده پیشنهادها
                    <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M9 6l6 6-6 6" />
                    </svg>
                  </Link>
                </div>
              </div>
            </div>
          </div>
        </section>
      )}

      <div className="mx-auto max-w-[1320px] px-5 pb-20 pt-10">
      {query && (
        <div className="relative mb-10 overflow-hidden rounded-3xl border border-[var(--hl-border)] bg-gradient-to-l from-[#e60053]/20 via-[#6b0a34]/10 to-transparent px-8 py-12">
          <h1 className="text-3xl font-bold text-[var(--hl-ink)] sm:text-4xl">نتایج جستجو</h1>
          <p className="mt-3 max-w-xl text-sm leading-7 text-[var(--hl-ink-2)]">{formatNumber(shown.length)} نتیجه برای «{query}»</p>
        </div>
      )}

      <div className="mb-8 flex flex-wrap gap-3">
        <Link
          href="/products"
          className={`rounded-full border px-5 py-2 text-sm font-medium transition ${
            selected === 0 ? "border-transparent bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white" : "border-[var(--hl-border)] text-[var(--hl-ink-2)] hover:bg-[var(--hl-border)]/40 hover:text-[var(--hl-ink)]"
          }`}
        >
          همه
        </Link>
        {activeCats.map((c) => (
          <Link
            key={c.id}
            href={`/products?cat=${c.id}`}
            className={`rounded-full border px-5 py-2 text-sm font-medium transition ${
              selected === c.id ? "border-transparent bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white" : "border-[var(--hl-border)] text-[var(--hl-ink-2)] hover:bg-[var(--hl-border)]/40 hover:text-[var(--hl-ink)]"
            }`}
          >
            {c.name}
          </Link>
        ))}
      </div>

      {shown.length === 0 ? (
        <p className="py-20 text-center text-[var(--hl-muted)]">{query ? `برای «${query}» محصولی یافت نشد.` : "محصولی در این دسته یافت نشد."}</p>
      ) : (
        <div className="grid grid-cols-2 gap-5 sm:grid-cols-3 lg:grid-cols-4">
          {shown.map((p) => (
            <Link
              key={p.id}
              href={`/products/detail?id=${p.id}`}
              className="group relative block overflow-hidden rounded-2xl border border-[var(--hl-border)] hl-card transition duration-300 hover:-translate-y-1 hover:border-[#e60053]/40 hover:shadow-[0_28px_70px_-28px_rgba(230,0,83,0.55)]"
            >
              <div className="relative aspect-[3/4]">
                <ProductCardImage src={p.image} alt={p.name} className="h-full w-full object-cover transition duration-500 group-hover:scale-105" />
                <div className="absolute inset-0 bg-gradient-to-t from-black/90 via-black/20 to-transparent" />
                <div className="absolute inset-x-0 bottom-0 p-4">
                  <h3 className="text-center text-sm font-bold text-white">{p.name}</h3>
                </div>
              </div>
            </Link>
          ))}
        </div>
      )}
      </div>
    </>
  );
}
