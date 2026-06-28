"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { Order, OrderStatus } from "@/lib/types";
import { formatToman, formatNumber } from "@/lib/format";
import { orderStatusLabel } from "@/lib/labels";
import { Card, PageHeader, Spinner, StatusBadge, inputCls } from "@/components/admin/ui";
import { Pagination, usePaged } from "@/components/admin/Pagination";
import OrderHistory from "@/components/admin/OrderHistory";

type Filter = "all" | OrderStatus;

// Read-only overview: which stage every order is in (approved, rejected, preparing, delivered…) plus the
// full change history. Distinct from the receipt-approval and fulfillment sections, which act on orders;
// this one only reports their state for any team that needs the bird's-eye view.
export default function OrderStatusPage() {
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
  const [term, setTerm] = useState("");

  useEffect(() => {
    (async () => {
      try {
        setOrders(await api.orders.list());
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const counts = useMemo(
    () => ({
      PendingApproval: orders.filter((o) => o.status === "PendingApproval").length,
      Preparing: orders.filter((o) => o.status === "Preparing").length,
      Completed: orders.filter((o) => o.status === "Completed").length,
      Cancelled: orders.filter((o) => o.status === "Cancelled").length,
    }),
    [orders],
  );

  const shown = useMemo(() => {
    const q = term.trim().toLowerCase();
    return orders.filter((o) => {
      if (filter !== "all" && o.status !== filter) return false;
      if (!q) return true;
      return o.code.toLowerCase().includes(q) || (o.userName ?? "").toLowerCase().includes(q);
    });
  }, [orders, filter, term]);

  const { page, setPage, totalPages, slice, total, pageSize } = usePaged(shown, 10);

  const filters: { key: Filter; label: string; count?: number }[] = [
    { key: "all", label: "همه" },
    { key: "PendingApproval", label: "در انتظار تأیید رسید", count: counts.PendingApproval },
    { key: "Preparing", label: "در حال آماده‌سازی", count: counts.Preparing },
    { key: "Completed", label: "تحویل‌شده", count: counts.Completed },
    { key: "Cancelled", label: "لغو/رد شده", count: counts.Cancelled },
  ];

  return (
    <div>
      <PageHeader title="وضعیت سفارشات" desc="مرحله‌ی هر سفارش و تاریخچه‌ی کامل تغییرات — نمای کلی برای پیگیری" />

      <div className="mb-4">
        <input
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          placeholder="جستجو با کد سفارش یا نام کاربر…"
          className={`${inputCls} max-w-sm`}
        />
      </div>

      <div className="mb-5 flex flex-wrap gap-2">
        {filters.map((f) => (
          <button
            key={f.key}
            onClick={() => setFilter(f.key)}
            className={`flex items-center gap-2 rounded-full border px-4 py-1.5 text-sm font-medium transition ${
              filter === f.key ? "border-transparent bg-white/10 text-white" : "border-white/10 text-white/60 hover:text-white"
            }`}
          >
            {f.label}
            {f.count ? <span className="rounded-full bg-[#e60053]/20 px-1.5 text-[11px] font-bold text-[#ff5a8a]">{formatNumber(f.count)}</span> : null}
          </button>
        ))}
      </div>

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : shown.length === 0 ? (
        <Card className="p-12 text-center text-white/40">سفارشی یافت نشد</Card>
      ) : (
        <div className="space-y-4">
          {slice.map((o) => (
            <Card key={o.id} className="p-5">
              <div className="flex flex-wrap items-center justify-between gap-3 border-b border-white/8 pb-3">
                <div>
                  <p className="font-mono text-sm text-white/70">{o.code}</p>
                  <p className="text-xs text-white/40">{o.userName} · {o.date} · {o.paymentMethod}</p>
                </div>
                <div className="flex items-center gap-3">
                  {o.deliveredAt && <span className="text-xs text-white/40">تحویل {o.deliveredAt}</span>}
                  <StatusBadge status={orderStatusLabel[o.status]} />
                </div>
              </div>

              <div className="mt-3 grid gap-2 sm:grid-cols-2">
                {o.items.map((it) => (
                  <div key={`${it.productId}:${it.plan ?? ""}`} className="flex items-center gap-3 rounded-lg bg-white/[0.03] p-2">
                    <img src={it.image} alt={it.name} className="h-9 w-9 rounded object-cover" />
                    <span className="flex-1 text-sm text-white/80">
                      {it.name} × {it.quantity}
                      {it.plan && <span className="text-white/45"> · {it.plan}</span>}
                    </span>
                    <span className="text-xs text-white/55">{formatToman(it.lineTotal)}</span>
                  </div>
                ))}
              </div>

              <div className="mt-4 flex items-center justify-between border-t border-white/8 pt-3">
                <span className="text-sm text-white/60">مبلغ کل</span>
                <span className="font-bold text-emerald-400">{formatToman(o.total)}</span>
              </div>

              <OrderHistory history={o.history ?? []} />
            </Card>
          ))}
          <Pagination page={page} totalPages={totalPages} total={total} pageSize={pageSize} onPage={setPage} />
        </div>
      )}
    </div>
  );
}
