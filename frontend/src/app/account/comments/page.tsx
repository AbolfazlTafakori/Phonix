"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { PageTitle, Panel } from "@/components/account/Panel";
import Stars from "@/components/Stars";
import { productPath } from "@/lib/seo";
import type { Comment, CommentStatus, Product } from "@/lib/types";

const STATUS: Record<CommentStatus, { text: string; bg: string; color: string }> = {
  Approved: { text: "منتشر شده", bg: "rgba(34,181,115,0.14)", color: "#22B573" },
  Pending: { text: "در انتظار تأیید", bg: "rgba(244,164,58,0.16)", color: "#F4A43A" },
  Rejected: { text: "تأیید نشده", bg: "rgba(224,80,80,0.14)", color: "#E05050" },
};

export default function MyCommentsPage() {
  const { user } = useAuth();
  const [items, setItems] = useState<Comment[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!user) return;
    let alive = true;
    Promise.all([api.comments.mine(), api.products.list()])
      .then(([mine, list]) => { if (alive) { setItems(mine); setProducts(list); } })
      .catch(() => {})
      .finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, [user]);

  const productOf = (id: number) => products.find((p) => p.id === id);

  return (
    <div>
      <PageTitle title="دیدگاه‌ها و پرسش‌ها" desc="دیدگاه‌هایی که برای محصولات ثبت کرده‌اید." />

      {loading ? (
        <Panel>
          <div className="grid h-24 place-items-center">
            <span className="inline-block h-7 w-7 animate-spin rounded-full border-2 border-[rgba(166,102,45,0.2)] border-t-[#FF5A1F]" />
          </div>
        </Panel>
      ) : items.length === 0 ? (
        <Panel>
          <div className="py-8 text-center">
            <p style={{ color: "var(--ac-muted)" }}>هنوز دیدگاهی ثبت نکرده‌اید.</p>
            <Link
              href="/products"
              className="mt-4 inline-block rounded-xl px-6 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
              style={{ background: "var(--ac-btn)" }}
            >
              مشاهده محصولات
            </Link>
          </div>
        </Panel>
      ) : (
        <div className="flex flex-col gap-3">
          {items.map((c) => {
            const product = productOf(c.productId);
            const badge = STATUS[c.status];
            return (
              <Panel key={c.id}>
                <div className="flex flex-wrap items-center justify-between gap-2">
                  {product ? (
                    <Link href={productPath(product)} className="flex min-w-0 items-center gap-2 text-[13px] font-bold transition hover:opacity-70" style={{ color: "var(--ac-title)" }}>
                      {product.image && <img loading="lazy" decoding="async" src={product.image} alt="" className="h-8 w-8 shrink-0 rounded-lg object-contain" />}
                      <span className="truncate">{product.name}</span>
                    </Link>
                  ) : (
                    <span className="text-[13px] font-bold" style={{ color: "var(--ac-title)" }}>محصول حذف شده</span>
                  )}
                  <span className="shrink-0 rounded-full px-2.5 py-1 text-[11px] font-bold" style={{ background: badge.bg, color: badge.color }}>
                    {badge.text}
                  </span>
                </div>

                {c.rating > 0 && <div className="mt-2.5"><Stars value={c.rating} /></div>}

                <p className="mt-2 whitespace-pre-wrap text-[13px] leading-7" style={{ color: "var(--ac-text)" }}>{c.body}</p>

                {c.date && <p className="mt-2 text-[11px]" style={{ color: "var(--ac-muted)" }}>{c.date}</p>}
              </Panel>
            );
          })}
        </div>
      )}
    </div>
  );
}
