import Link from "next/link";
import { api } from "@/lib/api";
import { formatToman, formatNumber } from "@/lib/format";
import type { Product, Category } from "@/lib/types";

export const dynamic = "force-dynamic";
export const metadata = { title: "محصولات | Phoenix Verify" };

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
    <div className="mx-auto max-w-[1320px] px-5 pb-20 pt-10">
      <div className="relative mb-10 overflow-hidden rounded-3xl border border-white/10 bg-gradient-to-l from-[#e60053]/20 via-[#6b0a34]/10 to-transparent px-8 py-12">
        <h1 className="text-3xl font-bold text-white sm:text-4xl">{query ? "نتایج جستجو" : "محصولات"}</h1>
        <p className="mt-3 max-w-xl text-sm leading-7 text-white/70">
          {query
            ? `${formatNumber(shown.length)} نتیجه برای «${query}»`
            : "اکانت‌های وریفای‌شده و اشتراک سرویس‌های محبوب با بهترین قیمت و تحویل آنی."}
        </p>
      </div>

      <div className="mb-8 flex flex-wrap gap-3">
        <Link
          href="/products"
          className={`rounded-full border px-5 py-2 text-sm font-medium transition ${
            selected === 0 ? "border-transparent bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white" : "border-white/10 text-white/70 hover:bg-white/5 hover:text-white"
          }`}
        >
          همه
        </Link>
        {activeCats.map((c) => (
          <Link
            key={c.id}
            href={`/products?cat=${c.id}`}
            className={`rounded-full border px-5 py-2 text-sm font-medium transition ${
              selected === c.id ? "border-transparent bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white" : "border-white/10 text-white/70 hover:bg-white/5 hover:text-white"
            }`}
          >
            {c.name}
          </Link>
        ))}
      </div>

      {shown.length === 0 ? (
        <p className="py-20 text-center text-white/45">{query ? `برای «${query}» محصولی یافت نشد.` : "محصولی در این دسته یافت نشد."}</p>
      ) : (
        <div className="grid grid-cols-2 gap-5 sm:grid-cols-3 lg:grid-cols-4">
          {shown.map((p) => (
            <Link
              key={p.id}
              href={`/products/detail?id=${p.id}`}
              className="group relative block overflow-hidden rounded-2xl border border-white/8 bg-[#0d0d14] transition duration-300 hover:-translate-y-1 hover:border-[#e60053]/40 hover:shadow-[0_28px_70px_-28px_rgba(230,0,83,0.55)]"
            >
              <div className="relative aspect-[3/4]">
                <img src={p.image} alt={p.name} className="h-full w-full object-cover transition duration-500 group-hover:scale-105" />
                <div className="absolute inset-0 bg-gradient-to-t from-black/90 via-black/20 to-transparent" />
                {p.stock <= 0 ? (
                  <>
                    <div className="absolute inset-0 grid place-items-center">
                      <span className="-rotate-6 rounded-xl border border-white/25 bg-black/65 px-4 py-1.5 text-sm font-black tracking-wide text-white backdrop-blur">ناموجود</span>
                    </div>
                    <div className="absolute inset-x-0 bottom-0 p-4">
                      <h3 className="text-sm font-bold text-white/85">{p.name}</h3>
                    </div>
                  </>
                ) : (
                  <>
                    {p.discountPercent > 0 && (
                      <span className="absolute right-3 top-3 rounded-full bg-[#e60053] px-2.5 py-1 text-[11px] font-bold text-white">٪{p.discountPercent} تخفیف</span>
                    )}
                    <div className="absolute inset-x-0 bottom-0 p-4">
                      <h3 className="text-sm font-bold text-white">{p.name}</h3>
                      <div className="mt-1.5 flex items-center gap-2">
                        <span className="text-sm font-bold text-emerald-400">{formatToman(p.finalPrice)}</span>
                        {p.discountPercent > 0 && (
                          <span className="text-xs text-white/40 line-through">{formatToman(p.price)}</span>
                        )}
                      </div>
                    </div>
                  </>
                )}
              </div>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
