"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import ProductCardImage from "@/components/ProductCardImage";
import { formatNumber, formatToman } from "@/lib/format";
import type { Product, Category } from "@/lib/types";

const PAGE_SIZE = 12;
const PRICE_MAX = 20_000_000;

// The catalog has no ratings field yet, so derive a stable pseudo-rating from the id
// purely for the card layout. Deterministic → no hydration mismatch.
function rating(id: number) {
  const score = (4.5 + ((id * 37) % 5) / 10).toFixed(1);
  const reviews = ((id * 137) % 1490) + 60;
  const label = reviews >= 1000 ? `${(reviews / 1000).toFixed(1)}K` : String(reviews);
  return { score, label };
}

type Sort = "popular" | "cheap" | "expensive" | "newest";
const sortOptions: { value: Sort; label: string }[] = [
  { value: "popular", label: "محبوب‌ترین" },
  { value: "cheap", label: "ارزان‌ترین" },
  { value: "expensive", label: "گران‌ترین" },
  { value: "newest", label: "جدیدترین" },
];

const serviceTypes = ["اشتراک (با تمدید)", "اکانت اختصاصی", "گیفت کارت", "سرویس یک‌ماهه"];
const deliveryTimes = ["تحویل آنی", "تحویل در کمتر از ۱ ساعت", "تحویل ۱ تا ۲۴ ساعت", "هر زمان"];

function Star({ className = "" }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} fill="currentColor">
      <path d="M12 2l2.9 6 6.6.9-4.8 4.6 1.1 6.5L12 17.8 6.2 20l1.1-6.5L2.5 8.9 9 8z" />
    </svg>
  );
}

function Check({ checked }: { checked: boolean }) {
  return (
    <span className={`grid h-[18px] w-[18px] shrink-0 place-items-center rounded-[5px] border transition ${checked ? "border-[var(--hl-red)] bg-[var(--hl-red)]" : "border-[var(--hl-border)] bg-transparent"}`}>
      {checked && (
        <svg viewBox="0 0 24 24" className="h-3 w-3 text-white" fill="none" stroke="currentColor" strokeWidth="3.5" strokeLinecap="round" strokeLinejoin="round">
          <path d="M20 6L9 17l-5-5" />
        </svg>
      )}
    </span>
  );
}

function CheckRow({ label, count, checked, onClick }: { label: string; count?: number; checked: boolean; onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} className="flex w-full items-center justify-between gap-2 py-1.5 text-right">
      <span className="flex items-center gap-2.5">
        <Check checked={checked} />
        <span className={`text-[14px] ${checked ? "font-bold text-[var(--hl-ink)]" : "text-[var(--hl-ink-2)]"}`}>{label}</span>
      </span>
      {count !== undefined && <span className="text-[12px] tabular-nums text-[var(--hl-muted)]">({formatNumber(count)})</span>}
    </button>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="border-t border-[var(--hl-border)] px-5 py-5">
      <h3 className="mb-3 text-[15px] font-black text-[var(--hl-ink)]">{title}</h3>
      {children}
    </div>
  );
}

export default function ProductsBrowser({ products, categories, initialCatId }: { products: Product[]; categories: Category[]; initialCatId?: number }) {
  const [selectedCats, setSelectedCats] = useState<Set<number>>(() => (initialCatId ? new Set([initialCatId]) : new Set()));
  const [maxPrice, setMaxPrice] = useState(PRICE_MAX);
  const [inStockOnly, setInStockOnly] = useState(true);
  const [featuredOnly, setFeaturedOnly] = useState(false);
  const [discountOnly, setDiscountOnly] = useState(false);
  const [delivery, setDelivery] = useState<Set<string>>(new Set(["تحویل آنی"]));
  const [service, setService] = useState<Set<string>>(new Set());
  const [sort, setSort] = useState<Sort>("popular");
  const [page, setPage] = useState(1);
  const [mobileFilters, setMobileFilters] = useState(false);

  const catCount = useMemo(() => {
    const m = new Map<number, number>();
    for (const p of products) m.set(p.categoryId, (m.get(p.categoryId) ?? 0) + 1);
    return m;
  }, [products]);

  const toggleCat = (id: number) => {
    setPage(1);
    setSelectedCats((prev) => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  };
  const toggleSet = (setter: React.Dispatch<React.SetStateAction<Set<string>>>, v: string) => {
    setPage(1);
    setter((prev) => {
      const next = new Set(prev);
      next.has(v) ? next.delete(v) : next.add(v);
      return next;
    });
  };

  const filtered = useMemo(() => {
    let list = products.filter((p) => {
      if (selectedCats.size && !selectedCats.has(p.categoryId)) return false;
      if (p.finalPrice > maxPrice) return false;
      if (inStockOnly && p.stock <= 0) return false;
      if (featuredOnly && !p.featured) return false;
      if (discountOnly && p.discountPercent <= 0) return false;
      return true;
    });
    if (sort === "cheap") list = [...list].sort((a, b) => a.finalPrice - b.finalPrice);
    else if (sort === "expensive") list = [...list].sort((a, b) => b.finalPrice - a.finalPrice);
    else if (sort === "newest") list = [...list].sort((a, b) => b.id - a.id);
    else list = [...list].sort((a, b) => Number(b.featured) - Number(a.featured));
    return list;
  }, [products, selectedCats, maxPrice, inStockOnly, featuredOnly, discountOnly, sort]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const pageClamped = Math.min(page, totalPages);
  const pageItems = filtered.slice((pageClamped - 1) * PAGE_SIZE, pageClamped * PAGE_SIZE);

  const anyFilter = selectedCats.size > 0 || maxPrice < PRICE_MAX || featuredOnly || discountOnly || !inStockOnly || service.size > 0 || delivery.size !== 1 || !delivery.has("تحویل آنی");
  const clearAll = () => {
    setSelectedCats(new Set());
    setMaxPrice(PRICE_MAX);
    setInStockOnly(true);
    setFeaturedOnly(false);
    setDiscountOnly(false);
    setDelivery(new Set(["تحویل آنی"]));
    setService(new Set());
    setPage(1);
  };

  const activeCats = categories.filter((c) => c.isActive);

  const sidebar = (
    <div className="overflow-hidden rounded-[18px] border border-[var(--hl-border)] bg-[var(--hl-card)]">
      <div className="flex items-center justify-between gap-2 px-5 py-4">
        <h2 className="text-[17px] font-black text-[var(--hl-ink)]">فیلتر محصولات</h2>
        <svg viewBox="0 0 24 24" className="h-5 w-5 text-[var(--hl-red)]" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M4 6h16M7 12h10M10 18h4" /></svg>
      </div>

      <Section title="دسته‌بندی محصولات">
        <div className="flex flex-col">
          <CheckRow label="همه دسته‌بندی‌ها" checked={selectedCats.size === 0} onClick={() => { setSelectedCats(new Set()); setPage(1); }} />
          {activeCats.map((c) => (
            <CheckRow key={c.id} label={c.name} count={catCount.get(c.id) ?? 0} checked={selectedCats.has(c.id)} onClick={() => toggleCat(c.id)} />
          ))}
        </div>
      </Section>

      <Section title="محدوده قیمت (تومان)">
        <div className="mb-3 flex items-center justify-between text-[13px] font-bold tabular-nums text-[var(--hl-ink-2)]">
          <span>{formatNumber(maxPrice)}</span>
          <span>۰</span>
        </div>
        <input
          type="range"
          min={0}
          max={PRICE_MAX}
          step={100000}
          value={maxPrice}
          onChange={(e) => { setMaxPrice(Number(e.target.value)); setPage(1); }}
          dir="ltr"
          className="hl-range w-full"
        />
        <div className="mt-4 grid grid-cols-4 gap-2">
          {[
            { label: "همه", v: PRICE_MAX },
            { label: "۱M–۵M", v: 5_000_000 },
            { label: "۵M+", v: PRICE_MAX },
            { label: "بیشتر", v: PRICE_MAX },
          ].map((b, i) => (
            <button
              key={i}
              type="button"
              onClick={() => { setMaxPrice(b.v); setPage(1); }}
              className="rounded-lg border border-[var(--hl-border)] py-1.5 text-[12px] font-bold text-[var(--hl-ink-2)] transition hover:border-[var(--hl-red)]/50 hover:text-[var(--hl-red)]"
            >
              {b.label}
            </button>
          ))}
        </div>
      </Section>

      <Section title="نوع سرویس">
        <div className="flex flex-col">
          {serviceTypes.map((s, i) => (
            <CheckRow key={s} label={s} count={[72, 34, 22, 28][i]} checked={service.has(s)} onClick={() => toggleSet(setService, s)} />
          ))}
        </div>
      </Section>

      <Section title="زمان تحویل">
        <div className="flex flex-col">
          {deliveryTimes.map((s, i) => (
            <CheckRow key={s} label={s} count={[72, 56, 34, 6][i]} checked={delivery.has(s)} onClick={() => toggleSet(setDelivery, s)} />
          ))}
        </div>
      </Section>

      <Section title="موجودی / وضعیت">
        <div className="flex flex-col">
          <CheckRow label="فقط موجود" checked={inStockOnly} onClick={() => { setInStockOnly((v) => !v); setPage(1); }} />
          <CheckRow label="پیشنهاد ویژه" count={34} checked={discountOnly} onClick={() => { setDiscountOnly((v) => !v); setPage(1); }} />
          <CheckRow label="پرفروش‌ترین‌ها" count={28} checked={featuredOnly} onClick={() => { setFeaturedOnly((v) => !v); setPage(1); }} />
        </div>
      </Section>

      <div className="flex items-center justify-between border-t border-[var(--hl-border)] px-5 py-4">
        <span className="text-[14px] font-bold text-[var(--hl-ink)]">فقط محصولات تخفیف‌دار</span>
        <button
          type="button"
          onClick={() => { setDiscountOnly((v) => !v); setPage(1); }}
          className={`relative h-6 w-11 shrink-0 rounded-full transition ${discountOnly ? "bg-[var(--hl-red)]" : "bg-[var(--hl-border)]"}`}
        >
          <span className={`absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-all ${discountOnly ? "left-0.5" : "right-0.5"}`} />
        </button>
      </div>
    </div>
  );

  return (
    <section className="mx-auto max-w-[1840px] px-4 py-10 sm:px-8 xl:px-16">
      <div className="flex flex-col gap-6 lg:flex-row">
        {/* Sidebar — right in RTL */}
        <aside className="hidden w-[300px] shrink-0 lg:block">{sidebar}</aside>

        {/* Main */}
        <div className="min-w-0 flex-1">
          {/* Top bar */}
          <div className="mb-6 flex flex-col gap-3 rounded-[18px] border border-[var(--hl-border)] bg-[var(--hl-card)] p-4 lg:flex-row lg:items-center lg:justify-between">
            <div className="flex items-center gap-3">
              <button
                type="button"
                onClick={() => setMobileFilters(true)}
                className="flex items-center gap-2 rounded-lg border border-[var(--hl-border)] px-3 py-2 text-[13px] font-bold text-[var(--hl-ink)] lg:hidden"
              >
                <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M4 6h16M7 12h10M10 18h4" /></svg>
                فیلترها
              </button>
              <label className="flex items-center gap-2 text-[13px] text-[var(--hl-ink-2)]">
                مرتب‌سازی
                <select
                  value={sort}
                  onChange={(e) => { setSort(e.target.value as Sort); setPage(1); }}
                  className="rounded-lg border border-[var(--hl-border)] bg-transparent px-3 py-1.5 text-[13px] font-bold text-[var(--hl-ink)] focus:outline-none"
                >
                  {sortOptions.map((o) => (
                    <option key={o.value} value={o.value}>{o.label}</option>
                  ))}
                </select>
              </label>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <span className="text-[13px] text-[var(--hl-muted)]">
                نمایش {formatNumber(pageItems.length)} از {formatNumber(filtered.length)} محصول
              </span>
              {anyFilter && (
                <button type="button" onClick={clearAll} className="rounded-lg border border-[var(--hl-red)]/40 px-3 py-1 text-[12px] font-bold text-[var(--hl-red)] transition hover:bg-[#fff4f1]">
                  پاک کردن همه
                </button>
              )}
            </div>
          </div>

          {pageItems.length === 0 ? (
            <p className="py-24 text-center text-[var(--hl-muted)]">محصولی با این فیلترها یافت نشد.</p>
          ) : (
            <>
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 xl:grid-cols-4">
                {pageItems.map((p, i) => {
                  const rt = rating(p.id);
                  const discounted = p.discountPercent > 0;
                  const out = p.stock <= 0;
                  return (
                    <div key={p.id} className="group flex flex-col overflow-hidden rounded-[16px] border border-[var(--hl-border)] bg-[var(--hl-card)] transition duration-200 hover:-translate-y-1 hover:border-[var(--hl-red)]/40 hover:shadow-[0_20px_44px_-20px_rgba(239,35,60,0.28)]">
                      <div className="relative bg-[#f7f8fa] p-5">
                        <div className="absolute inset-x-3 top-3 flex items-start justify-between">
                          {discounted ? (
                            <span className="rounded-lg bg-[var(--hl-red)] px-2 py-1 text-[11px] font-black text-white">تخفیف {formatNumber(p.discountPercent)}٪</span>
                          ) : (
                            <span className="rounded-lg bg-[#e7f7ee] px-2 py-1 text-[11px] font-black text-[#12a150]">تحویل آنی</span>
                          )}
                          {p.featured && <span className="rounded-lg bg-[#fff1e8] px-2 py-1 text-[11px] font-black text-[#f2551f]">پرفروش</span>}
                        </div>
                        <div className="grid h-24 place-items-center">
                          <ProductCardImage src={p.logo || p.image} alt={p.name} className="max-h-24 w-auto object-contain transition duration-300 group-hover:scale-105" />
                        </div>
                        {out && (
                          <span className="absolute bottom-3 left-3 rounded-md bg-black/60 px-2 py-1 text-[10px] font-bold text-white">ناموجود</span>
                        )}
                      </div>

                      <div className="flex flex-1 flex-col p-4">
                        <h3 className="line-clamp-1 text-center text-[16px] font-black text-[var(--hl-ink)]">{p.name}</h3>
                        <p className="mt-1 line-clamp-1 text-center text-[12px] text-[var(--hl-muted)]">{p.plans[0]?.type || p.categoryName}</p>

                        <div className="mt-2 flex items-center justify-center gap-1.5 text-[12px]">
                          <Star className="h-3.5 w-3.5 text-[#ffb020]" />
                          <span className="font-black text-[var(--hl-ink)]">{rt.score}</span>
                          <span className="text-[var(--hl-muted)]">({rt.label})</span>
                        </div>

                        <div className="mt-3 flex flex-col items-center">
                          {discounted && (
                            <span className="text-[11px] text-[var(--hl-muted)] line-through">{formatNumber(p.price)}</span>
                          )}
                          <span className="text-[17px] font-black text-[var(--hl-ink)]">{formatToman(p.finalPrice)}</span>
                        </div>

                        <div className="mt-3 flex items-center gap-2">
                          <Link
                            href={`/products/detail?id=${p.id}`}
                            className="flex-1 rounded-xl border border-[var(--hl-red)] py-2 text-center text-[13px] font-bold text-[var(--hl-red)] transition hover:bg-[#fff4f1]"
                          >
                            مشاهده
                          </Link>
                          <Link
                            href={`/products/detail?id=${p.id}`}
                            aria-label="افزودن به سبد"
                            className="grid h-9 w-9 shrink-0 place-items-center rounded-xl text-white transition hover:brightness-105"
                            style={{ background: "linear-gradient(95deg, #FF7A2E 0%, #F0392C 100%)" }}
                          >
                            <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="9" cy="21" r="1" /><circle cx="20" cy="21" r="1" /><path d="M1 1h4l2.7 13.4a2 2 0 002 1.6h9.7a2 2 0 002-1.6L23 6H6" /></svg>
                          </Link>
                        </div>
                      </div>

                      {/* Inline promo banners after the first full row (desktop) */}
                    </div>
                  );
                })}
              </div>

              {/* Promo banners row */}
              <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2">
                {[
                  { img: "/figma/catpage-banner-discount.png", alt: "تخفیف‌های داغ امروز" },
                  { img: "/figma/catpage-banner-number.png", alt: "شماره مجازی برای همه سرویس‌ها" },
                ].map((b) => (
                  <Link key={b.img} href="/products" className="group block overflow-hidden rounded-[16px]">
                    <img src={b.img} alt={b.alt} className="aspect-[3/1] w-full scale-[1.02] object-cover transition duration-300 group-hover:scale-[1.05]" />
                  </Link>
                ))}
              </div>

              {/* Pagination */}
              {totalPages > 1 && (
                <div className="mt-8 flex items-center justify-center gap-1.5">
                  <button
                    type="button"
                    disabled={pageClamped === 1}
                    onClick={() => setPage((p) => Math.max(1, p - 1))}
                    className="rounded-lg border border-[var(--hl-border)] px-3 py-2 text-[13px] font-bold text-[var(--hl-ink-2)] transition enabled:hover:border-[var(--hl-red)]/50 enabled:hover:text-[var(--hl-red)] disabled:opacity-40"
                  >
                    قبلی
                  </button>
                  {Array.from({ length: totalPages }).map((_, i) => i + 1).filter((n) => n === 1 || n === totalPages || Math.abs(n - pageClamped) <= 1).reduce<(number | "…")[]>((acc, n, idx, arr) => {
                    if (idx > 0 && n - (arr[idx - 1] as number) > 1) acc.push("…");
                    acc.push(n);
                    return acc;
                  }, []).map((n, i) =>
                    n === "…" ? (
                      <span key={`e${i}`} className="px-2 text-[var(--hl-muted)]">…</span>
                    ) : (
                      <button
                        key={n}
                        type="button"
                        onClick={() => setPage(n)}
                        className={`h-9 w-9 rounded-lg text-[13px] font-bold transition ${n === pageClamped ? "text-white" : "border border-[var(--hl-border)] text-[var(--hl-ink-2)] hover:border-[var(--hl-red)]/50 hover:text-[var(--hl-red)]"}`}
                        style={n === pageClamped ? { background: "linear-gradient(95deg, #FF7A2E 0%, #F0392C 100%)" } : undefined}
                      >
                        {formatNumber(n)}
                      </button>
                    )
                  )}
                  <button
                    type="button"
                    disabled={pageClamped === totalPages}
                    onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                    className="rounded-lg border border-[var(--hl-border)] px-3 py-2 text-[13px] font-bold text-[var(--hl-ink-2)] transition enabled:hover:border-[var(--hl-red)]/50 enabled:hover:text-[var(--hl-red)] disabled:opacity-40"
                  >
                    بعدی
                  </button>
                </div>
              )}
            </>
          )}
        </div>
      </div>

      {/* Mobile filter drawer */}
      {mobileFilters && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div className="absolute inset-0 bg-black/40" onClick={() => setMobileFilters(false)} />
          <div className="absolute inset-y-0 right-0 w-[88%] max-w-sm overflow-y-auto bg-[var(--hl-surface)] p-4 shadow-2xl">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-[16px] font-black text-[var(--hl-ink)]">فیلترها</h2>
              <button type="button" onClick={() => setMobileFilters(false)} className="grid h-8 w-8 place-items-center rounded-lg border border-[var(--hl-border)] text-[var(--hl-ink)]">✕</button>
            </div>
            {sidebar}
          </div>
        </div>
      )}
    </section>
  );
}
