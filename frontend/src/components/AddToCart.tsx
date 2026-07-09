"use client";

import { useState } from "react";
import Link from "next/link";
import { addToCart, type CartItem } from "@/lib/cart";

export default function AddToCart({ product, rules }: { product: Omit<CartItem, "quantity">; rules?: string }) {
  const [added, setAdded] = useState(false);
  // Rules acceptance modal, shown before the item lands in the cart whenever the selected plan carries rules.
  const [confirming, setConfirming] = useState(false);

  const hasRules = (rules ?? "").trim().length > 0;

  function commit() {
    addToCart(product);
    setConfirming(false);
    setAdded(true);
  }

  function onAdd() {
    if (hasRules) setConfirming(true);
    else commit();
  }

  if (added) {
    return (
      <div className="flex flex-1 items-center gap-3">
        <span className="flex h-12 flex-1 items-center justify-center rounded-xl bg-emerald-500/15 text-sm font-bold text-emerald-400">✓ به سبد اضافه شد</span>
        <Link href="/cart" className="flex h-12 items-center rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-6 text-sm font-bold text-white transition hover:brightness-110">
          مشاهده سبد
        </Link>
      </div>
    );
  }

  return (
    <>
      <button
        onClick={onAdd}
        className="h-12 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-10 text-base font-bold text-white shadow-[0_14px_40px_-12px_rgba(230,0,83,0.7)] transition hover:brightness-110"
      >
        افزودن به سبد خرید
      </button>

      {confirming && (
        <div className="fixed inset-0 z-[70] grid place-items-center p-4" dir="rtl">
          <div onClick={() => setConfirming(false)} className="absolute inset-0 bg-black/70 backdrop-blur-sm" />
          <div className="relative flex max-h-[85vh] w-full max-w-lg flex-col overflow-hidden rounded-2xl border border-[var(--hl-border)] hl-card shadow-2xl">
            <div className="flex items-start justify-between gap-3 border-b border-[var(--hl-border)] px-5 py-4">
              <div>
                <h3 className="text-lg font-bold text-[var(--hl-ink)]">قوانین و مقررات</h3>
                <p className="mt-0.5 text-xs text-[var(--hl-muted)]">{product.name}{product.plan ? ` · ${product.plan}` : ""}</p>
              </div>
              <button onClick={() => setConfirming(false)} aria-label="بستن" className="grid h-8 w-8 shrink-0 place-items-center rounded-full text-[var(--hl-muted)] transition hover:bg-[var(--hl-border)]/40 hover:text-[var(--hl-ink)]">
                <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M18 6 6 18M6 6l12 12" /></svg>
              </button>
            </div>

            <div className="overflow-y-auto px-5 py-4">
              <div className="rounded-xl border border-[var(--hl-border)] bg-[var(--hl-border)]/20 p-4 text-sm leading-8 text-[var(--hl-ink-2)] whitespace-pre-wrap">
                {rules}
              </div>
              <div className="mt-3 flex gap-2.5 rounded-xl border border-rose-500/30 bg-rose-500/[0.08] px-3.5 py-3">
                <span className="text-rose-300">⚠</span>
                <p className="text-xs leading-7 text-rose-100/85">در صورت عدم رعایت قوانین بالا، مسئولیت مسدود شدن اشتراک بر عهده‌ی خریدار است.</p>
              </div>
            </div>

            <div className="flex gap-3 border-t border-[var(--hl-border)] px-5 py-4">
              <button
                onClick={commit}
                className="grid h-11 flex-1 place-items-center rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] text-sm font-bold text-white transition hover:brightness-110"
              >
                می‌پذیرم و افزودن به سبد
              </button>
              <button
                onClick={() => setConfirming(false)}
                className="h-11 rounded-xl border border-[var(--hl-border)] px-6 text-sm font-bold text-[var(--hl-ink-2)] transition hover:bg-[var(--hl-border)]/40"
              >
                انصراف
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
