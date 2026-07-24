"use client";

import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { addToCart, setQuantity, useCart } from "@/lib/cart";
import { formatToman, toFa } from "@/lib/format";
import type { Product, ProductPlan } from "@/lib/types";

// The buying experience, split across the page rather than stacked in one card.
//
// The layout puts the gallery on the right, what you are choosing in the middle, and the price and the
// buttons in a sticky box on the left — so the selectors and the CTA live in two different grid cells but
// must share one selection. A context is what makes that possible: `PurchaseProvider` owns every piece of
// state, `PlanPicker` renders the choices wherever the page wants them, and `BuyBox` renders the price and
// the actions somewhere else entirely, both reading the same source of truth.

const TYPE_DESC: Record<string, string> = {
  "اشتراکی": "ارزان‌تر و اقتصادی",
  "اختصاصی": "اکانت اختصاصی شما",
};

function TYPE_ICON(type: string) {
  const priv = type.includes("اختصاصی");
  return (
    <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      {priv ? (
        <>
          <circle cx="9" cy="7" r="4" />
          <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
          <path d="M19 8v6M22 11h-6" />
        </>
      ) : (
        <>
          <circle cx="9" cy="7" r="4" />
          <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
          <path d="M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" />
        </>
      )}
    </svg>
  );
}

type PurchaseValue = {
  product: Product;
  types: string[];
  type: string | null;
  selectType: (t: string) => void;
  typedPlans: ProductPlan[];
  selected: ProductPlan | undefined;
  setPlanId: (id: number) => void;
  isV2Ray: boolean;
  typeHeading: string;
  planHeading: string;
  out: boolean;
  overLevel: boolean;
  requiredLevel: number;
  level: number | null;
  planRequired: boolean;
  unitPrice: number;
  planLabel: string;
  inCartQty: number;
  onAdd: (quick: boolean) => void;
  changeQty: (next: number) => void;
  goto: (path: string) => void;
  fav: boolean;
  favBusy: boolean;
  toggleFav: () => void;
  shared: boolean;
  share: () => void;
};

const Ctx = createContext<PurchaseValue | null>(null);

function usePurchase(): PurchaseValue {
  const v = useContext(Ctx);
  if (!v) throw new Error("PlanPicker/BuyBox must be rendered inside <PurchaseProvider>");
  return v;
}

export function PurchaseProvider({ product, children }: { product: Product; children: ReactNode }) {
  const router = useRouter();
  const { user } = useAuth();

  const plans = useMemo(() => product.plans.filter((p) => p.isActive), [product.plans]);
  const types = useMemo(() => [...new Set(plans.map((p) => p.type))], [plans]);
  const [type, setType] = useState<string | null>(types[0] ?? null);
  const typedPlans = useMemo(
    () => plans.filter((p) => p.type === type).sort((a, b) => a.months - b.months),
    [plans, type],
  );
  const [planId, setPlanId] = useState<number | null>(typedPlans[0]?.id ?? null);
  const selected: ProductPlan | undefined = typedPlans.find((p) => p.id === planId) ?? typedPlans[0];

  // A V2Ray-linked product sells per server: the first level is the location the operator named, the second
  // its plans — so the two headings say that instead of "account type" and "duration".
  const isV2Ray = (product.v2rayCategoryId ?? 0) > 0;
  const typeHeading = isV2Ray ? "انتخاب سرور" : "انتخاب نوع اکانت";
  const planHeading = isV2Ray ? "انتخاب پلن" : "انتخاب مدت زمان";

  // Identity-level gate, mirrored here from the server rule (PlaceOrder) so a buyer who can't reach the
  // level never fills a cart they can't check out.
  const requiredLevel = product.requiredLevel ?? 1;
  const [level, setLevel] = useState<number | null>(null);
  useEffect(() => {
    if (!user) { setLevel(null); return; }
    let cancelled = false;
    api.account.me()
      .then((me) => { if (!cancelled) setLevel(me.verificationLevel); })
      .catch(() => { /* leave unknown — checkout and the server still gate it */ });
    return () => { cancelled = true; };
  }, [user]);
  const overLevel = user !== null && level !== null && level < requiredLevel;

  // A product that sells per plan can't be ordered without one: the plan carries the price and the
  // "collect info from the customer" step. The server refuses such a checkout too.
  const planRequired = plans.length > 0 && selected == null;

  const { items: cartItems } = useCart();
  const inCartQty = selected
    ? cartItems.find((i) => i.productId === product.id && (i.planId ?? null) === (selected.id ?? null))?.quantity ?? 0
    : 0;

  const [confirming, setConfirming] = useState(false);
  const [pendingQuick, setPendingQuick] = useState(false);
  const [fav, setFav] = useState(false);
  const [favBusy, setFavBusy] = useState(false);
  const [shared, setShared] = useState(false);

  const out = product.stock <= 0;
  const unitPrice = selected?.finalPrice ?? product.finalPrice;
  const planLabel = selected
    ? `${selected.type} · ${selected.label || `${toFa(selected.months)} ماهه`}`
    : "";
  const rules = (selected?.rules ?? "").trim();

  function selectType(t: string) {
    setType(t);
    const first = plans.filter((p) => p.type === t).sort((a, b) => a.months - b.months)[0];
    setPlanId(first?.id ?? null);
  }

  function commit(goCheckout: boolean) {
    addToCart(
      { productId: product.id, name: product.name, image: product.image, price: unitPrice, planId: selected?.id ?? null, plan: planLabel },
      1,
    );
    setConfirming(false);
    if (goCheckout) router.push("/checkout");
  }

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

  const value: PurchaseValue = {
    product, types, type, selectType, typedPlans, selected,
    setPlanId: (id) => setPlanId(id),
    isV2Ray, typeHeading, planHeading,
    out, overLevel, requiredLevel, level, planRequired,
    unitPrice, planLabel, inCartQty,
    onAdd,
    changeQty: (next) => setQuantity(product.id, next, selected?.id ?? null),
    goto: (path) => router.push(path),
    fav, favBusy, toggleFav, shared, share,
  };

  return (
    <Ctx.Provider value={value}>
      {children}

      {/* Rules confirmation lives with the provider, not either panel: it is triggered from the BuyBox but
          belongs to the purchase as a whole, and rendering it once here keeps it out of both layouts. */}
      {confirming && (
        <div className="fixed inset-0 z-[70] grid place-items-center p-4" dir="rtl">
          <div onClick={() => setConfirming(false)} className="absolute inset-0 bg-black/70 backdrop-blur-sm" />
          <div className="relative flex max-h-[85vh] w-full max-w-lg flex-col overflow-hidden rounded-2xl border bg-[var(--ac-panel-bg)] shadow-2xl" style={{ borderColor: "var(--ac-panel-border)" }}>
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
    </Ctx.Provider>
  );
}

/** What you are choosing — its own card in the middle column, next to the gallery. */
export function PlanPicker() {
  const { types, type, selectType, typedPlans, selected, setPlanId, isV2Ray, typeHeading, planHeading } = usePurchase();
  if (types.length === 0) return null;

  return (
    <div
      className="rounded-[22px] border bg-[var(--ac-panel-bg)] p-4 sm:p-5"
      style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}
    >
      <p className="mb-2.5 text-right text-[13px] font-bold" style={{ color: "var(--ac-text)" }}>{typeHeading}</p>
      <div className="space-y-2.5">
        {types.map((t) => {
          const active = type === t;
          return (
            <button
              key={t}
              type="button"
              onClick={() => selectType(t)}
              className="flex w-full items-center justify-between gap-3 rounded-xl border px-3.5 py-3 text-right transition"
              style={active
                ? { borderColor: "var(--ac-menu-active-border)", background: "var(--ac-menu-active-bg)" }
                : { borderColor: "var(--ac-panel-border)" }}
            >
              <span className="flex items-center gap-3">
                <span style={{ color: active ? "#F2551F" : "var(--ac-icon)" }}>{TYPE_ICON(t)}</span>
                <span className="leading-tight">
                  <span className="block text-[14px] font-black" style={{ color: active ? "#F2551F" : "var(--ac-title)" }}>{t}</span>
                  <span className="mt-0.5 block text-[11px]" style={{ color: "var(--ac-muted)" }}>{TYPE_DESC[t] ?? (isV2Ray ? "لوکیشن سرویس" : "اشتراک دیجیتال")}</span>
                </span>
              </span>
              <span className="grid h-5 w-5 shrink-0 place-items-center rounded-full border-2" style={{ borderColor: active ? "#F2551F" : "var(--ac-panel-border)" }}>
                {active && <span className="h-2.5 w-2.5 rounded-full" style={{ background: "#F2551F" }} />}
              </span>
            </button>
          );
        })}
      </div>

      {typedPlans.length > 0 && (
        <div className="mt-5">
          <p className="mb-2.5 text-right text-[13px] font-bold" style={{ color: "var(--ac-text)" }}>{planHeading}</p>
          <div className="grid grid-cols-2 gap-2.5 sm:grid-cols-3">
            {typedPlans.map((p) => {
              const active = p.id === (selected?.id ?? null);
              return (
                <button
                  key={p.id}
                  type="button"
                  onClick={() => setPlanId(p.id)}
                  className="flex flex-col items-center gap-1 rounded-xl border px-2 py-3 text-center transition"
                  style={active
                    ? { borderColor: "var(--ac-menu-active-border)", background: "var(--ac-menu-active-bg)" }
                    : { borderColor: "var(--ac-panel-border)" }}
                >
                  <span className="text-[14px] font-black" style={{ color: active ? "#F2551F" : "var(--ac-title)" }}>{p.label || `${toFa(p.months)} ماهه`}</span>
                  <span className="text-[12px] font-bold" style={{ color: "var(--ac-text)" }}>{formatToman(p.finalPrice)}</span>
                  {p.userCount > 0 ? (
                    <span className="mt-0.5 inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-[10px] font-black" style={{ background: "var(--ac-stat-icon-orange-bg)", color: "#F2551F" }}>
                      <svg viewBox="0 0 24 24" className="h-3 w-3" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" /></svg>
                      {toFa(p.userCount)} کاربر
                    </span>
                  ) : p.discountPercent > 0 && (
                    <span className="mt-0.5 rounded-md bg-emerald-500/15 px-2 py-0.5 text-[10px] font-black text-emerald-600">
                      ٪{toFa(p.discountPercent)} تخفیف
                    </span>
                  )}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * The mobile purchase bar — pinned to the bottom of the viewport below `lg`, the way marketplace apps keep
 * the price and the primary action in reach while the shopper scrolls the gallery, tabs and reviews. It
 * reads the same purchase state as the sticky desktop BuyBox, mirrors its states (out of stock, level gate,
 * plan required, already in cart) and slides out of the way once the page bottom scrolls into view so it
 * never sits over the footer.
 */
export function MobileBuyBar() {
  const {
    product, selected, out, overLevel, requiredLevel, level, planRequired,
    unitPrice, planLabel, inCartQty, onAdd, goto,
  } = usePurchase();

  const [tucked, setTucked] = useState(false);
  useEffect(() => {
    const el = document.getElementById("buy-mobile-sentinel");
    if (!el || typeof IntersectionObserver === "undefined") return;
    // Tuck the bar away once the footer sentinel reaches the viewport (and keep it tucked once scrolled past,
    // when the sentinel sits above the top edge) so the bar never covers the footer. IntersectionObserver
    // tracks layout directly, so it works regardless of which element actually scrolls the page.
    const io = new IntersectionObserver(
      ([e]) => setTucked(e.isIntersecting || e.boundingClientRect.top <= 0),
      { threshold: 0 },
    );
    io.observe(el);
    return () => io.disconnect();
  }, []);

  const discount = selected ? selected.discountPercent : product.discountPercent;
  const basePrice = selected ? selected.price : product.price;

  const cta = (() => {
    if (out) return <span className="grid h-12 place-items-center rounded-xl border px-6 text-[13px] font-black" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)", color: "var(--ac-muted)" }}>ناموجود</span>;
    if (overLevel) return <button type="button" onClick={() => goto(requiredLevel >= 2 && (level ?? 0) >= 1 ? "/account/kyc" : "/account/cards")} className="grid h-12 place-items-center rounded-xl px-6 text-[13px] font-black text-white transition active:brightness-95" style={{ background: "var(--ac-btn)" }}>احراز هویت</button>;
    if (planRequired) return <span className="grid h-12 place-items-center rounded-xl border px-5 text-[13px] font-black" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)", color: "var(--ac-muted)" }}>انتخاب پلن</span>;
    if (inCartQty > 0) return <button type="button" onClick={() => goto("/checkout")} className="grid h-12 place-items-center rounded-xl px-7 text-[14px] font-black text-white shadow-[0_10px_26px_rgba(242,85,31,0.32)] transition active:brightness-95" style={{ background: "var(--ac-btn)" }}>ادامه پرداخت</button>;
    return (
      <button type="button" onClick={() => onAdd(false)} className="flex h-12 items-center gap-2 rounded-xl px-6 text-[14px] font-black text-white shadow-[0_10px_26px_rgba(242,85,31,0.32)] transition active:brightness-95" style={{ background: "var(--ac-btn)" }}>
        <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="9" cy="21" r="1" /><circle cx="18" cy="21" r="1" /><path d="M2 3h3l2.4 12.4a2 2 0 0 0 2 1.6h8.2a2 2 0 0 0 2-1.6L23 7H5.5" /></svg>
        افزودن به سبد
      </button>
    );
  })();

  return (
    <div
      className={`fixed inset-x-0 bottom-0 z-40 border-t bg-[var(--ac-panel-bg)] px-4 pt-2.5 transition-transform duration-300 lg:hidden ${tucked ? "translate-y-full" : "translate-y-0"}`}
      style={{ borderColor: "var(--ac-panel-border)", boxShadow: "0 -8px 26px rgba(0,0,0,0.09)", paddingBottom: "max(10px, env(safe-area-inset-bottom))" }}
    >
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0">
          {out ? (
            <p className="text-[14px] font-black" style={{ color: "var(--ac-muted)" }}>فعلاً موجود نیست</p>
          ) : (
            <>
              <div className="flex items-center gap-2">
                <p className="text-[19px] font-black leading-none" style={{ color: "var(--ac-title)" }}>{formatToman(unitPrice)}</p>
                {discount > 0 && <span className="rounded-md px-1.5 py-0.5 text-[10px] font-black text-white" style={{ background: "#F2551F" }}>٪{toFa(discount)}</span>}
              </div>
              {discount > 0 ? (
                <p className="mt-1 text-[11px] line-through leading-none" style={{ color: "var(--ac-muted)" }}>{formatToman(basePrice)}</p>
              ) : planLabel ? (
                <p className="mt-1 truncate text-[11px] leading-none" style={{ color: "var(--ac-muted)" }}>{planLabel}</p>
              ) : null}
            </>
          )}
        </div>
        <div className="shrink-0">{cta}</div>
      </div>
    </div>
  );
}

/** Price and the actions — the sticky box in the left column. */
export function BuyBox() {
  const {
    product, selected, out, overLevel, requiredLevel, level, planRequired,
    unitPrice, planLabel, inCartQty, onAdd, changeQty, goto,
    fav, favBusy, toggleFav, shared, share,
  } = usePurchase();

  const basePrice = selected ? selected.price : product.price;
  const discount = selected ? selected.discountPercent : product.discountPercent;

  return (
    <div
      className="rounded-[22px] border bg-[var(--ac-panel-bg)] p-5"
      style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}
    >
      <div className="flex items-end justify-between gap-2">
        <div>
          <p className="text-[12px]" style={{ color: "var(--ac-muted)" }}>قیمت نهایی</p>
          <p className="mt-1 text-[24px] font-black leading-none" style={{ color: "var(--ac-title)" }}>
            {formatToman(unitPrice)}
          </p>
          {discount > 0 && (
            <p className="mt-1.5 text-[12px] line-through" style={{ color: "var(--ac-muted)" }}>{formatToman(basePrice)}</p>
          )}
        </div>
        {planLabel && (
          <span className="rounded-lg px-2.5 py-1 text-[11px] font-black" style={{ background: "var(--ac-stat-icon-orange-bg)", color: "#F2551F" }}>
            {planLabel}
          </span>
        )}
      </div>

      {out ? (
        <button type="button" disabled className="mt-4 h-14 w-full cursor-not-allowed rounded-xl border text-[15px] font-black" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)", color: "var(--ac-muted)" }}>
          ناموجود
        </button>
      ) : overLevel ? (
        <div className="mt-4 rounded-xl border p-4 text-center" style={{ borderColor: "var(--ac-btn-secondary-border)", background: "var(--ac-stat-icon-orange-bg)" }}>
          <p className="text-[13px] font-black" style={{ color: "#F2551F" }}>برای خرید این محصول احراز هویت لازم است</p>
          <p className="mt-1 text-[12px] leading-6" style={{ color: "var(--ac-text)" }}>
            این محصول به سطح {toFa(requiredLevel)} ({requiredLevel >= 2 ? "تأیید کارت ملی" : "تأیید کارت بانکی"}) نیاز دارد؛
            سطح فعلی شما {toFa(level ?? 0)} است.
          </p>
          <Link
            href={requiredLevel >= 2 && (level ?? 0) >= 1 ? "/account/kyc" : "/account/cards"}
            className="mt-3 grid h-11 w-full place-items-center rounded-xl text-[14px] font-black text-white transition hover:brightness-105"
            style={{ background: "var(--ac-btn)" }}
          >
            {requiredLevel >= 2 && (level ?? 0) >= 1 ? "احراز هویت با کارت ملی" : "ثبت و تأیید کارت بانکی"}
          </Link>
        </div>
      ) : planRequired ? (
        <button type="button" disabled className="mt-4 h-14 w-full cursor-not-allowed rounded-xl border text-[15px] font-black" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)", color: "var(--ac-muted)" }}>
          ابتدا یک پلن انتخاب کنید
        </button>
      ) : inCartQty > 0 ? (
        <div className="mt-4 space-y-2.5">
          <div className="flex items-center justify-between rounded-xl border px-4 py-2.5" style={{ borderColor: "var(--ac-menu-active-border)", background: "var(--ac-menu-active-bg)" }}>
            <span className="text-[13px] font-bold" style={{ color: "#F2551F" }}>تعداد</span>
            <div className="flex items-center gap-3">
              <button type="button" onClick={() => changeQty(inCartQty - 1)} aria-label="کاهش" className="grid h-8 w-8 place-items-center rounded-lg border text-[16px] font-bold transition hover:bg-[color:var(--ac-menu-hover)]" style={{ borderColor: "var(--ac-panel-border)", color: "var(--ac-text)", background: "var(--ac-panel-bg)" }}>−</button>
              <span className="w-6 text-center text-[15px] font-black" style={{ color: "var(--ac-title)" }}>{toFa(inCartQty)}</span>
              <button type="button" onClick={() => changeQty(Math.min(99, inCartQty + 1))} aria-label="افزایش" className="grid h-8 w-8 place-items-center rounded-lg border text-[16px] font-bold transition hover:bg-[color:var(--ac-menu-hover)]" style={{ borderColor: "var(--ac-panel-border)", color: "var(--ac-text)", background: "var(--ac-panel-bg)" }}>+</button>
            </div>
          </div>
          <button type="button" onClick={() => goto("/checkout")} className="flex h-14 w-full items-center justify-center gap-2.5 rounded-xl text-[15px] font-black text-white shadow-[0_14px_38px_rgba(242,85,31,0.35)] transition hover:brightness-105" style={{ background: "var(--ac-btn)" }}>
            ادامه به پرداخت
          </button>
          <button type="button" onClick={() => goto("/cart")} className="flex h-12 w-full items-center justify-center gap-2 rounded-xl border bg-[var(--ac-panel-bg)] text-[14px] font-bold transition hover:bg-[color:var(--ac-menu-hover)]" style={{ borderColor: "var(--ac-btn-secondary-border)", color: "var(--ac-btn-secondary-text)" }}>
            مشاهده سبد خرید
          </button>
        </div>
      ) : (
        <div className="mt-4 space-y-2.5">
          <button type="button" onClick={() => onAdd(false)} className="flex h-14 w-full items-center justify-center gap-2.5 rounded-xl text-[15px] font-black text-white shadow-[0_14px_38px_rgba(242,85,31,0.35)] transition hover:brightness-105" style={{ background: "var(--ac-btn)" }}>
            <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="9" cy="21" r="1" /><circle cx="18" cy="21" r="1" /><path d="M2 3h3l2.4 12.4a2 2 0 0 0 2 1.6h8.2a2 2 0 0 0 2-1.6L23 7H5.5" /></svg>
            افزودن به سبد خرید
          </button>
          <button type="button" onClick={() => onAdd(true)} className="flex h-12 w-full items-center justify-center gap-2 rounded-xl border bg-[var(--ac-panel-bg)] text-[14px] font-bold transition hover:bg-[color:var(--ac-menu-hover)]" style={{ borderColor: "var(--ac-btn-secondary-border)", color: "var(--ac-btn-secondary-text)" }}>
            <svg viewBox="0 0 24 24" className="h-4 w-4" fill="currentColor"><path d="M13 2L3 14h7l-1 8 11-13h-7z" /></svg>
            خرید سریع
          </button>
        </div>
      )}

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
    </div>
  );
}
