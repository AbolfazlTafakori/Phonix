import Link from "next/link";
import { api } from "@/lib/api";
import { formatNumber } from "@/lib/format";
import ProductCardImage from "@/components/ProductCardImage";
import ProductsBrowser from "@/components/products/ProductsBrowser";
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

            <div className="flex flex-col items-center gap-7 pb-10 pt-2 sm:pt-4 lg:flex-row-reverse lg:items-center lg:justify-center lg:gap-10 xl:gap-16">
              <div className="shrink-0">
                <img src="/figma/productpage-hero-shield.png" alt="" className="mx-auto h-auto w-56 max-w-full object-contain sm:w-72 lg:w-[430px] lg:max-w-none xl:w-[490px]" />
              </div>

              <div className="w-full text-center lg:w-auto lg:text-right">
                <h1 className="text-[30px] font-black leading-[1.5] text-[var(--hl-red)] sm:text-[42px] xl:text-[52px]">
                  محصولات فونیکس وریفای
                </h1>
                <p className="mx-auto mt-3 max-w-xl text-[16px] leading-7 text-[var(--hl-muted)] sm:mt-4 sm:text-[20px] sm:leading-9 lg:mx-0">
                  بزرگ‌ترین مرجع خرید محصولات دیجیتال و خدمات مجازی با تحویل سریع
                  <br className="hidden sm:inline" />
                  و تضمین اصالت
                </p>
                <div className="mt-7 flex flex-nowrap items-center justify-center gap-x-4 sm:gap-x-6 lg:justify-start">
                  {heroStats.map((s) => (
                    <div key={s.label} className="flex shrink-0 items-center gap-2">
                      <img src={s.icon} alt="" aria-hidden className="h-9 w-9 shrink-0 object-contain sm:h-12 sm:w-12" />
                      <div className="text-right">
                        <div className="whitespace-nowrap text-[17px] font-black leading-none text-[var(--hl-ink)] sm:text-[25px]">{s.value}</div>
                        <div className="mt-1 whitespace-nowrap text-[12px] font-bold text-[var(--hl-muted)] sm:text-[15px]">{s.label}</div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

            </div>
          </div>
        </section>
      )}

      {query ? (
        <div className="mx-auto max-w-[1320px] px-5 pb-20 pt-10">
          {shown.length === 0 ? (
            <p className="py-20 text-center text-[var(--hl-muted)]">برای «{query}» محصولی یافت نشد.</p>
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
      ) : (
        <ProductsBrowser products={active} categories={activeCats} initialCatId={selected || undefined} />
      )}
    </>
  );
}
