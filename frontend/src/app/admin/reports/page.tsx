"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { formatToman, formatNumber } from "@/lib/format";
import type { OverviewStats, TopProductStat } from "@/lib/types";
import { Card, PageHeader, Spinner } from "@/components/admin/ui";

export default function AdminReportsPage() {
  const [stats, setStats] = useState<OverviewStats | null>(null);
  const [top, setTop] = useState<TopProductStat[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    (async () => {
      try {
        const [s, t] = await Promise.all([api.stats.overview(), api.stats.topProducts()]);
        setStats(s);
        setTop(t);
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری گزارش‌ها");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  if (error) {
    return <Card className="p-8 text-center text-rose-400">{error}</Card>;
  }
  if (loading || !stats) {
    return <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>;
  }

  const maxSold = Math.max(1, ...top.map((p) => p.sold));
  const summary = [
    { label: "درآمد کل (تکمیل‌شده)", value: formatToman(stats.revenue), color: "text-emerald-400" },
    { label: "تعداد کل سفارش", value: formatNumber(stats.ordersCount), color: "text-white" },
    { label: "سفارش تکمیل‌شده", value: formatNumber(stats.completedOrders), color: "text-[#6f93ff]" },
    { label: "در انتظار/آماده‌سازی", value: formatNumber(stats.pendingOrders + stats.preparingOrders), color: "text-amber-400" },
  ];

  return (
    <div>
      <PageHeader title="گزارش‌ها" desc="تحلیل فروش و عملکرد بر اساس سفارش‌های واقعی" />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {summary.map((s) => (
          <Card key={s.label} className="p-5">
            <p className="text-sm text-white/50">{s.label}</p>
            <p className={`mt-2 text-2xl font-bold ${s.color}`}>{s.value}</p>
          </Card>
        ))}
      </div>

      <Card className="mt-6 overflow-hidden">
        <h3 className="p-6 pb-4 text-lg font-bold text-white">پرفروش‌ترین محصولات</h3>
        {top.length === 0 ? (
          <p className="px-6 pb-10 text-sm text-white/40">هنوز فروشی ثبت نشده است.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[560px] text-right">
              <thead>
                <tr className="border-y border-white/8 text-sm text-white/45">
                  <th className="px-6 py-3 font-medium">محصول</th>
                  <th className="px-6 py-3 font-medium">تعداد فروش</th>
                  <th className="px-6 py-3 font-medium">درآمد</th>
                  <th className="px-6 py-3 font-medium">سهم</th>
                </tr>
              </thead>
              <tbody>
                {top.map((p) => (
                  <tr key={p.productId} className="border-b border-white/5 text-sm text-white/85">
                    <td className="px-6 py-3">
                      <div className="flex items-center gap-3">
                        <img src={p.image} alt={p.name} className="h-9 w-9 rounded-lg object-cover" />
                        <span className="font-medium">{p.name}</span>
                      </div>
                    </td>
                    <td className="px-6 py-3">{formatNumber(p.sold)} عدد</td>
                    <td className="px-6 py-3 text-emerald-400">{formatToman(p.revenue)}</td>
                    <td className="px-6 py-3">
                      <div className="flex items-center gap-2">
                        <div className="h-2 w-24 overflow-hidden rounded-full bg-white/10">
                          <div className="h-full rounded-full bg-gradient-to-l from-[#e60053] to-[#6d28d9]" style={{ width: `${(p.sold / maxSold) * 100}%` }} />
                        </div>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  );
}
