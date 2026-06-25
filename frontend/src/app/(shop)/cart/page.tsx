"use client";

import Link from "next/link";
import { useCart, setQuantity, removeFromCart } from "@/lib/cart";
import { formatToman } from "@/lib/format";

export default function CartPage() {
  const { items, total, count, ready } = useCart();

  return (
    <div className="mx-auto max-w-[900px] px-5 pb-20 pt-10">
      <h1 className="mb-6 text-2xl font-bold text-white">سبد خرید</h1>

      {!ready ? null : items.length === 0 ? (
        <div className="rounded-2xl border border-white/8 bg-[#15151f]/80 p-12 text-center">
          <p className="text-white/60">سبد خرید شما خالی است.</p>
          <Link href="/products" className="mt-4 inline-block rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-6 py-2.5 text-sm font-bold text-white transition hover:brightness-110">
            مشاهده محصولات
          </Link>
        </div>
      ) : (
        <div className="grid gap-6 lg:grid-cols-[1fr_320px]">
          <div className="space-y-3">
            {items.map((i) => (
              <div key={`${i.productId}:${i.planId ?? ""}`} className="flex items-center gap-4 rounded-2xl border border-white/8 bg-[#15151f]/80 p-4">
                <img src={i.image} alt={i.name} className="h-16 w-16 shrink-0 rounded-lg object-cover" />
                <div className="min-w-0 flex-1">
                  <p className="truncate font-bold text-white">{i.name}</p>
                  {i.plan && <p className="text-xs text-white/50">{i.plan}</p>}
                  <p className="text-sm text-emerald-400">{formatToman(i.price)}</p>
                </div>
                <div className="flex items-center gap-2">
                  <button onClick={() => setQuantity(i.productId, i.quantity - 1, i.planId)} className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-white/70 transition hover:bg-white/5">−</button>
                  <span className="w-8 text-center text-sm font-bold text-white">{i.quantity}</span>
                  <button onClick={() => setQuantity(i.productId, i.quantity + 1, i.planId)} className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-white/70 transition hover:bg-white/5">+</button>
                </div>
                <button onClick={() => removeFromCart(i.productId, i.planId)} className="text-sm text-rose-400 transition hover:text-rose-300">حذف</button>
              </div>
            ))}
          </div>

          <div className="h-fit rounded-2xl border border-white/8 bg-[#15151f]/80 p-6">
            <h3 className="mb-4 text-lg font-bold text-white">خلاصه سفارش</h3>
            <div className="flex items-center justify-between text-sm text-white/70">
              <span>تعداد اقلام</span>
              <span className="text-white">{count}</span>
            </div>
            <div className="mt-3 flex items-center justify-between border-t border-white/8 pt-3">
              <span className="text-sm text-white/70">مبلغ کل</span>
              <span className="text-lg font-bold text-emerald-400">{formatToman(total)}</span>
            </div>
            <Link href="/checkout" className="mt-5 flex h-12 items-center justify-center rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] text-sm font-bold text-white transition hover:brightness-110">
              ادامه و پرداخت
            </Link>
          </div>
        </div>
      )}
    </div>
  );
}
