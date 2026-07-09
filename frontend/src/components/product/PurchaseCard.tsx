"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { addToCart } from "@/lib/cart";
import { formatToman, toFa } from "@/lib/format";
import type { Product, ProductPlan } from "@/lib/types";

/** Sticky purchase card: plan type + duration selectors, quantity stepper, add-to-cart
 *  with rules confirmation, quick-buy, and the compare/share/favorite action row. */
export default function PurchaseCard({ product }: { product: Product }) {
  const router = useRouter();
  const { user } = useAuth();

  const plans = product.plans.filter((p) => p.isActive);
  const types = [...new Set(plans.map((p) => p.type))];
  const [type, setType] = useState<string | null>(types[0] ?? null);
  const typedPlans = useMemo(
    () => plans.filter((p) => p.type === type).sort((a, b) => a.months - b.months),
    [plans, type],
  );
  const [planId, setPlanId] = useState<number | null>(typedPlans[0]?.id ?? null);
  const selected: ProductPlan | undefined = typedPlans.find((p) => p.id === planId) ?? typedPlans[0];

  const [qty, setQty] = useState(1);
  const [confirming, setConfirming] = useState(false);
  const [added, setAdded] = useState(false);
  const [fav, setFav] = useState(false);
  const [favBusy, setFavBusy] = useState(false);
  const [shared, setShared] = useState(false);

  const out = product.stock <= 0;
  const unitPrice = selected?.finalPrice ?? product.finalPrice;
  const planLabel = selected ? `${selected.type} · ${toFa(selected.months)} ماهه` : "";
  const rules = (selected?.rules ?? "").trim();

  function selectType(t: string) {
    setType(t);
    const first = plans.filter((p) => p.type === t).sort((a, b) => a.months - b.months)[0];
    setPlanId(first?.id ?? null);
  }

  function cartItem() {
    return { productId: product.id, name: product.name, image: product.image, price: unitPrice, planId: selected?.id ?? null, plan: planLabel };
  }

  function commit(goCheckout: boolean) {
    addToCart(cartItem(), qty);
    setConfirming(false);
    if (goCheckout) router.push("/checkout");
    else setAdded(true);
  }

  const [pendingQuick, setPendingQuick] = useState(false);
  function onAdd(quick: boolean) {
    if (out) return;
    setPendingQuick(quick);
    if (rules) setConfirming(true);
    else commit(quick);
  }

  async function toggleFav() {
    if (!user) { router.push("/login"); return; }
    setFavBusy(true);
    try {
      const r = await api.favorites.toggle(product.id);
      setFav(r.favorited);
    } finally {
      setFavBusy(false);
    }
  }

  async function share() {
    const url = window.location.href;
    try {
      if (navigator.share) await navigator.share({ title: product.name, url });
      else { await navigator.clipboard.writeText(url); setShared(true); setTimeout(() => setShared(false), 2000); }
    } catch { /* cancelled */ }
  }

  return (
    <div
      className="rounded-[22px] border bg-white p-5 lg:sticky lg:top-[100px]"
      style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}
    >
      {/* price */}
      <div className="flex items-end justify-between gap-2">
        <div>
          <p className="text-[12px]" style={{ color: "var(--ac-muted)" }}>قیمت نهایی</p>
          <p className="mt-1 flex items-baseline gap-1.5">
            <span className="text-[30px] font-black leading-none" style={{ color: "#F2551F" }}>{formatToman(unitPrice * qty).replace(" تومان", "")}</span>
            <span className="text-[13px]" style={{ color: "var(--ac-muted)" }}>تومان</span>
          </p>
        </div>
        {planLabel && <span className="rounded-full px-3 py-1 text-[11px] font-bold" style={{ background: "var(--ac-stat-icon-orange-bg)", color: "#F2551F" }}>{planLabel}</span>}
      </div>

      {/* type selector */}
      {types.length > 0 && (
        <div className="mt-5 grid grid-cols-2 gap-2">
          {types.map((t) => {
            const active = type === t;
            return (
              <button
                key={t}
                type="button"
                onClick={() => selectType(t)}
                className="h-11 rounded-xl border text-[13px] font-bold transition"
                style={active
                  ? { borderColor: "var(--ac-menu-active-border)", background: "var(--ac-menu-active-bg)", color: "var(--ac-menu-active-text)" }
                  : { borderColor: "var(--ac-panel-border)", color: "var(--ac-text)" }}
              >
                {t}
              </button>
            );
          })}
        </div>
      )}

      {/* duration grid */}
      {typedPlans.length > 0 && (
        <div className="mt-3 grid grid-cols-2 gap-2">
          {typedPlans.map((p) => {
            const active = p.id === (selected?.id ?? null);
            return (
              <button
                key={p.id}
                type="button"
                onClick={() => setPlanId(p.id)}
                className="relative rounded-xl border p-3 text-right transition"
                style={active
                  ? { borderColor: "var(--ac-menu-active-border)", background: "var(--ac-menu-active-bg)" }
                  : { borderColor: "var(--ac-panel-border)" }}
              >
                <p className="text-[13px] font-black" style={{ color: "var(--ac-title)" }}>{toFa(p.months)} ماهه</p>
                <p className="mt-1 text-[12px] font-bold" style={{ color: active ? "#F2551F" : "var(--ac-text)" }}>{formatToman(p.finalPrice)}</p>
                {p.userCount > 0 && (
                  <p className="mt-0.5 text-[10px]" style={{ color: "var(--ac-muted)" }}>{toFa(p.userCount)} کاربره</p>
                )}
                {p.discountPercent > 0 && (
                  <span className="absolute -top-2 left-2 rounded-full bg-emerald-500 px-2 py-0.5 text-[10px] font-black text-white">
                    ٪{toFa(p.discountPercent)} تخفیف
                  </span>
                )}
              </button>
            );
          })}
        </div>
      )}

      {/* quantity */}
      <div className="mt-4 flex items-center justify-between rounded-xl border px-4 py-2.5" style={{ borderColor: "var(--ac-panel-border)" }}>
        <span className="text-[13px] font-bold" style={{ color: "var(--ac-text)" }}>تعداد</span>
        <div className="flex items-center gap-3">
          <button type="button" onClick={() => setQty((q) => Math.max(1, q - 1))} aria-label="کاهش" className="grid h-8 w-8 place-items-center rounded-lg border text-[16px] font-bold transition hover:bg-[color:var(--ac-menu-hover)]" style={{ borderColor: "var(--ac-panel-border)", color: "var(--ac-text)" }}>−</button>
          <span className="w-6 text-center text-[15px] font-black" style={{ color: "var(--ac-title)" }}>{toFa(qty)}</span>
          <button type="button" onClick={() => setQty((q) => Math.min(99, q + 1))} aria-label="افزایش" className="grid h-8 w-8 place-items-center rounded-lg border text-[16px] font-bold transition hover:bg-[color:var(--ac-menu-hover)]" style={{ borderColor: "var(--ac-panel-border)", color: "var(--ac-text)" }}>+</button>
        </div>
      </div>

      {/* CTAs */}
      {out ? (
        <button type="button" disabled className="mt-4 h-14 w-full cursor-not-allowed rounded-xl border text-[15px] font-black" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)", color: "var(--ac-muted)" }}>
          ناموجود
        </button>
      ) : added ? (
        <div className="mt-4 space-y-2.5">
          <div className="grid h-14 w-full place-items-center rounded-xl bg-emerald-500/15 text-[14px] font-black text-emerald-500">✓ به سبد اضافه شد</div>
          <button type="button" onClick={() => router.push("/cart")} className="h-12 w-full rounded-xl text-[14px] font-black text-white transition hover:brightness-105" style={{ background: "var(--ac-btn)" }}>
            مشاهده سبد خرید
          </button>
        </div>
      ) : (
        <div className="mt-4 space-y-2.5">
          <button type="button" onClick={() => onAdd(false)} className="h-14 w-full rounded-xl text-[15px] font-black text-white shadow-[0_14px_38px_rgba(242,85,31,0.35)] transition hover:brightness-105" style={{ background: "var(--ac-btn)" }}>
            افزودن به سبد خرید
          </button>
          <button type="button" onClick={() => onAdd(true)} className="h-12 w-full rounded-xl border bg-white text-[14px] font-bold transition hover:bg-[color:var(--ac-menu-hover)]" style={{ borderColor: "var(--ac-btn-secondary-border)", color: "var(--ac-btn-secondary-text)" }}>
            خرید سریع
          </button>
        </div>
      )}

      {/* action bar */}
      <div className="mt-4 grid grid-cols-3 gap-2 border-t pt-4" style={{ borderColor: "var(--ac-divider)" }}>
        <a href="#plan-compare" className="flex flex-col items-center gap-1.5 rounded-xl px-2 py-2 text-[11px] font-bold transition hover:bg-[color:var(--ac-menu-hover)]" style={{ color: "var(--ac-text)" }}>
          <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M16 3h5v5M8 21H3v-5M21 3l-7.5 7.5M3 21l7.5-7.5" /></svg>
          مقایسه
        </a>
        <button type="button" onClick={share} className="flex flex-col items-center gap-1.5 rounded-xl px-2 py-2 text-[11px] font-bold transition hover:bg-[color:var(--ac-menu-hover)]" style={{ color: "var(--ac-text)" }}>
          <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="18" cy="5" r="3" /><circle cx="6" cy="12" r="3" /><circle cx="18" cy="19" r="3" /><path d="M8.6 13.5l6.8 4M15.4 6.5l-6.8 4" /></svg>
          {shared ? "کپی شد ✓" : "اشتراک‌گذاری"}
        </button>
        <button type="button" onClick={toggleFav} disabled={favBusy} className="flex flex-col items-center gap-1.5 rounded-xl px-2 py-2 text-[11px] font-bold transition hover:bg-[color:var(--ac-menu-hover)] disabled:opacity-60" style={{ color: fav ? "#FF3D2E" : "var(--ac-text)" }}>
          <svg viewBox="0 0 24 24" className="h-5 w-5" fill={fav ? "currentColor" : "none"} stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1-1.1a5.5 5.5 0 0 0-7.8 7.8l1 1L12 21l7.8-7.6 1-1a5.5 5.5 0 0 0 0-7.8z" /></svg>
          علاقه‌مندی
        </button>
      </div>

      {/* rules confirmation */}
      {confirming && (
        <div className="fixed inset-0 z-[70] grid place-items-center p-4" dir="rtl">
          <div onClick={() => setConfirming(false)} className="absolute inset-0 bg-black/70 backdrop-blur-sm" />
          <div className="relative flex max-h-[85vh] w-full max-w-lg flex-col overflow-hidden rounded-2xl border bg-white shadow-2xl" style={{ borderColor: "var(--ac-panel-border)" }}>
            <div className="flex items-start justify-between gap-3 border-b px-5 py-4" style={{ borderColor: "var(--ac-divider)" }}>
              <div>
                <h3 className="text-lg font-bold" style={{ color: "var(--ac-title)" }}>قوانین و مقررات</h3>
                <p className="mt-0.5 text-xs" style={{ color: "var(--ac-muted)" }}>{product.name}{planLabel ? ` · ${planLabel}` : ""}</p>
              </div>
              <button onClick={() => setConfirming(false)} aria-label="بستن" className="grid h-8 w-8 shrink-0 place-items-center rounded-full transition hover:bg-[color:var(--ac-menu-hover)]" style={{ color: "var(--ac-muted)" }}>
                <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M18 6 6 18M6 6l12 12" /></svg>
              </button>
            </div>
            <div className="overflow-y-auto px-5 py-4">
              <div className="whitespace-pre-wrap rounded-xl border p-4 text-sm leading-8" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)", color: "var(--ac-text)" }}>
                {rules}
              </div>
              <div className="mt-3 flex gap-2.5 rounded-xl border border-rose-500/30 bg-rose-500/[0.08] px-3.5 py-3">
                <span className="text-rose-400">⚠</span>
                <p className="text-xs leading-7 text-rose-400">در صورت عدم رعایت قوانین بالا، مسئولیت مسدود شدن اشتراک بر عهده‌ی خریدار است.</p>
              </div>
            </div>
            <div className="flex gap-3 border-t px-5 py-4" style={{ borderColor: "var(--ac-divider)" }}>
              <button onClick={() => commit(pendingQuick)} className="grid h-11 flex-1 place-items-center rounded-xl text-sm font-bold text-white transition hover:brightness-105" style={{ background: "var(--ac-btn)" }}>
                می‌پذیرم و ادامه
              </button>
              <button onClick={() => setConfirming(false)} className="h-11 rounded-xl border px-6 text-sm font-bold transition hover:bg-[color:var(--ac-menu-hover)]" style={{ borderColor: "var(--ac-panel-border)", color: "var(--ac-text)" }}>
                انصراف
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
