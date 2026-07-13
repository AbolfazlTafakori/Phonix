"use client";

import Link from "next/link";
import { useCart, setQuantity, removeFromCart } from "@/lib/cart";
import { formatToman } from "@/lib/format";

export default function CartPage() {
  const { items, total, count, ready } = useCart();

  return (
    <div className="mx-auto max-w-[900px] px-5 pb-20 pt-10">
      <h1 className="mb-6 text-2xl font-bold text-[var(--hl-ink)]">سبد خرید</h1>

      {!ready ? null : items.length === 0 ? (
        <div className="hl-card rounded-2xl p-12 text-center">
          <p className="text-[var(--hl-ink-2)]">سبد خرید شما خالی است.</p>
          <Link href="/products" className="hl-cta mt-4 inline-block rounded-xl bg-gradient-to-l from-[#ff7a2e] to-[#f0392c] px-6 py-2.5 text-sm font-bold text-white">
            مشاهده محصولات
          </Link>
        </div>
      ) : (
        <div className="grid gap-6 lg:grid-cols-[1fr_320px]">
          <div className="space-y-3">
            {items.map((i) => (
              <div key={`${i.productId}:${i.planId ?? ""}`} className="hl-card flex flex-wrap items-center gap-3 rounded-2xl p-4 sm:gap-4">
                <img loading="lazy" decoding="async" src={i.image} alt={i.name} className="h-14 w-14 shrink-0 rounded-lg object-cover sm:h-16 sm:w-16" />
                <div className="min-w-0 flex-1">
                  <p className="truncate font-bold text-[var(--hl-ink)]">{i.name}</p>
                  {i.plan && <p className="text-xs text-[var(--hl-muted)]">{i.plan}</p>}
                  <p className="text-sm font-bold text-emerald-500">{formatToman(i.price)}</p>
                </div>
                <div className="flex w-full items-center justify-between gap-2 sm:w-auto sm:justify-end sm:gap-4">
                  <div className="flex items-center gap-2">
                    <button onClick={() => setQuantity(i.productId, i.quantity - 1, i.planId)} className="grid h-8 w-8 place-items-center rounded-lg border border-[var(--hl-border)] text-[var(--hl-ink-2)] transition hover:bg-[var(--hl-border)]">−</button>
                    <span className="w-8 text-center text-sm font-bold text-[var(--hl-ink)]">{i.quantity}</span>
                    <button onClick={() => setQuantity(i.productId, i.quantity + 1, i.planId)} className="grid h-8 w-8 place-items-center rounded-lg border border-[var(--hl-border)] text-[var(--hl-ink-2)] transition hover:bg-[var(--hl-border)]">+</button>
                  </div>
                  <button onClick={() => removeFromCart(i.productId, i.planId)} className="text-sm text-rose-500 transition hover:text-rose-400">حذف</button>
                </div>
              </div>
            ))}
          </div>

          <div className="hl-card h-fit rounded-2xl p-6">
            <h3 className="mb-4 text-lg font-bold text-[var(--hl-ink)]">خلاصه سفارش</h3>
            <div className="flex items-center justify-between text-sm text-[var(--hl-ink-2)]">
              <span>تعداد اقلام</span>
              <span className="text-[var(--hl-ink)]">{count}</span>
            </div>
            <div className="mt-3 flex items-center justify-between border-t border-[var(--hl-border)] pt-3">
              <span className="text-sm text-[var(--hl-ink-2)]">مبلغ کل</span>
              <span className="text-lg font-bold text-emerald-500">{formatToman(total)}</span>
            </div>
            <p className="mt-2 text-[11px] leading-5 text-[var(--hl-ink-2)]">مالیات بر ارزش افزوده و کارمزد درگاه در مرحله پرداخت محاسبه می‌شود.</p>
            <Link href="/checkout" className="hl-cta mt-5 flex h-12 items-center justify-center rounded-xl bg-gradient-to-l from-[#ff7a2e] to-[#f0392c] text-sm font-bold text-white">
              ادامه و پرداخت
            </Link>
          </div>
        </div>
      )}
    </div>
  );
}
