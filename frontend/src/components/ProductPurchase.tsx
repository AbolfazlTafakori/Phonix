"use client";

import { useState } from "react";
import type { Product } from "@/lib/types";
import { formatToman, toFa } from "@/lib/format";
import AddToCart from "@/components/AddToCart";

export default function ProductPurchase({ product }: { product: Product }) {
  const plans = product.plans.filter((p) => p.isActive);
  const types = [...new Set(plans.map((p) => p.type))];

  const [type, setType] = useState<string | null>(types[0] ?? null);
  const [planId, setPlanId] = useState<number | null>(plans[0]?.id ?? null);

  const typedPlans = plans.filter((p) => p.type === type);

  function selectType(t: string) {
    setType(t);
    setPlanId(plans.find((p) => p.type === t)?.id ?? null);
  }

  const selected = plans.find((p) => p.id === planId) ?? null;
  const unitPrice = selected?.finalPrice ?? product.finalPrice;
  const planLabel = selected ? `${selected.type} · ${toFa(selected.months)} ماهه` : null;

  return (
    <div>
      {types.length > 0 && (
        <>
          <div className="mt-6 flex gap-2 rounded-2xl border border-white/10 bg-white/[0.03] p-1.5">
            {types.map((t) => (
              <button
                key={t}
                onClick={() => selectType(t)}
                className={`flex-1 rounded-xl px-4 py-2.5 text-sm font-bold transition ${type === t ? "bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white" : "text-white/60 hover:text-white"}`}
              >
                {t}
              </button>
            ))}
          </div>

          <div className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-3">
            {typedPlans.map((p) => {
              const active = p.id === planId;
              return (
                <button
                  key={p.id}
                  onClick={() => setPlanId(p.id)}
                  className={`rounded-2xl border p-4 text-center transition ${active ? "border-[#e60053] bg-[#e60053]/10" : "border-white/10 hover:border-white/25"}`}
                >
                  <p className="text-sm font-bold text-white">{toFa(p.months)} ماهه</p>
                  {p.userCount > 0 && (
                    <span className="mt-1.5 inline-flex items-center gap-1 rounded-full bg-white/[0.06] px-2 py-0.5 text-[11px] font-bold text-white/70">
                      <svg viewBox="0 0 24 24" className="h-3 w-3" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" /></svg>
                      {toFa(p.userCount)} کاربر
                    </span>
                  )}
                  <p className="mt-2 text-sm font-bold text-emerald-400">{formatToman(p.finalPrice)}</p>
                  {p.discountPercent > 0 && <p className="mt-0.5 text-[11px] text-white/40 line-through">{formatToman(p.price)}</p>}
                </button>
              );
            })}
          </div>
        </>
      )}

      <div className="mt-6 flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-white/8 bg-[#15151f]/80 p-5">
        <div>
          <p className="text-xs text-white/45">قیمت نهایی</p>
          <p className="text-2xl font-bold text-white">{formatToman(unitPrice)}</p>
          {planLabel && <p className="mt-1 text-xs text-white/50">{planLabel}</p>}
        </div>
        <AddToCart
          key={planId ?? "base"}
          product={{ productId: product.id, name: product.name, image: product.image, price: unitPrice, planId, plan: planLabel }}
        />
      </div>
    </div>
  );
}
