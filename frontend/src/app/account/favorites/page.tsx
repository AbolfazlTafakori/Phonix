"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatToman } from "@/lib/format";
import { PageTitle, Panel } from "@/components/account/Panel";
import type { Product } from "@/lib/types";

export default function FavoritesPage() {
  const { user } = useAuth();
  const [items, setItems] = useState<Product[]>([]);
  const [loading, setLoading] = useState(true);

  async function load() {
    if (!user) return;
    const [ids, products] = await Promise.all([api.favorites.ids(user.id), api.products.list()]);
    setItems(products.filter((p) => ids.includes(p.id)));
    setLoading(false);
  }
  useEffect(() => {
    load();
  }, [user]);

  async function remove(productId: number) {
    if (!user) return;
    await api.favorites.toggle(productId);
    setItems((p) => p.filter((x) => x.id !== productId));
  }

  return (
    <div>
      <PageTitle title="محصولات موردعلاقه" desc="محصولاتی که برای خرید بعدی ذخیره کرده‌اید." />

      {loading ? (
        <Panel><div className="grid h-24 place-items-center"><span className="inline-block h-7 w-7 animate-spin rounded-full border-2 border-white/20 border-t-[#e60053]" /></div></Panel>
      ) : items.length === 0 ? (
        <Panel>
          <div className="py-8 text-center">
            <p className="text-white/60">لیست علاقه‌مندی شما خالی است.</p>
            <Link href="/products" className="mt-4 inline-block rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-6 py-2.5 text-sm font-bold text-white transition hover:brightness-110">مشاهده محصولات</Link>
          </div>
        </Panel>
      ) : (
        <div className="grid grid-cols-2 gap-5 lg:grid-cols-3">
          {items.map((product) => (
            <div key={product.id} className="group relative overflow-hidden rounded-2xl border border-white/8 bg-[#0d0d14]">
              <Link href={`/products/detail?id=${product.id}`} className="relative block aspect-[3/4]">
                <img src={product.image} alt={product.name} className="h-full w-full object-cover" />
                <div className="absolute inset-0 bg-gradient-to-t from-black/90 via-black/20 to-transparent" />
                <div className="absolute inset-x-0 bottom-0 p-4">
                  <p className="text-sm font-bold text-white">{product.name}</p>
                  <p className="mt-1 text-sm font-bold text-emerald-400">{formatToman(product.finalPrice)}</p>
                </div>
              </Link>
              <button
                onClick={() => remove(product.id)}
                aria-label="حذف از علاقه‌مندی"
                className="absolute left-3 top-3 grid h-9 w-9 place-items-center rounded-full bg-black/50 text-[#ff5a8a] transition hover:bg-black/70"
              >
                ♥
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
