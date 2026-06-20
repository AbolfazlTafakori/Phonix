"use client";

import { useState } from "react";
import Link from "next/link";
import { addToCart, type CartItem } from "@/lib/cart";

export default function AddToCart({ product }: { product: Omit<CartItem, "quantity"> }) {
  const [added, setAdded] = useState(false);

  function add() {
    addToCart(product);
    setAdded(true);
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
    <button
      onClick={add}
      className="h-12 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-10 text-base font-bold text-white shadow-[0_14px_40px_-12px_rgba(230,0,83,0.7)] transition hover:brightness-110"
    >
      افزودن به سبد خرید
    </button>
  );
}
