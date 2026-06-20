"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { formatToman, formatNumber } from "@/lib/format";
import { orderStatusLabel, ticketStatusLabel } from "@/lib/labels";
import type { OverviewStats, TopProductStat, Order, Ticket } from "@/lib/types";
import { Card, StatusBadge, SalesChart, Spinner } from "@/components/admin/ui";
import ServerStatus from "@/components/admin/ServerStatus";
import AdminIcon from "@/components/admin/AdminIcon";

export default function AdminDashboard() {
  const [stats, setStats] = useState<OverviewStats | null>(null);
  const [top, setTop] = useState<TopProductStat[]>([]);
  const [orders, setOrders] = useState<Order[]>([]);
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    (async () => {
      try {
        const [s, t, o, tk] = await Promise.all([
          api.stats.overview(),
          api.stats.topProducts(),
          api.orders.list(),
          api.tickets.list(),
        ]);
        setStats(s);
        setTop(t);
        setOrders(o);
        setTickets(tk);
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری داشبورد");
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

  const cards = [
    { label: "درآمد (تکمیل‌شده)", value: formatToman(stats.revenue), icon: "wallet", accent: "#22c55e" },
    { label: "سفارش‌ها", value: formatNumber(stats.ordersCount), icon: "cart", accent: "#3a64f2" },
    { label: "کاربران", value: formatNumber(stats.usersCount), icon: "users", accent: "#a855f7" },
    { label: "در انتظار تأیید", value: formatNumber(stats.pendingOrders), icon: "tag", accent: "#e60053" },
  ];

  return (
    <div>
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {cards.map((c) => (
          <Card key={c.label} className="flex items-center gap-4 p-5">
            <div className="grid h-11 w-11 place-items-center rounded-xl" style={{ background: `${c.accent}1f`, color: c.accent }}>
              <AdminIcon name={c.icon} className="h-5 w-5" />
            </div>
            <div className="min-w-0">
              <p className="truncate text-xl font-bold text-white">{c.value}</p>
              <p className="text-sm text-white/50">{c.label}</p>
            </div>
          </Card>
        ))}
      </div>

      <div className="mt-6">
        <ServerStatus />
      </div>

      <div className="mt-6 grid gap-6 lg:grid-cols-3">
        <Card className="p-6 lg:col-span-2">
          <h3 className="mb-6 text-lg font-bold text-white">پرفروش‌ترین محصولات</h3>
          {top.length === 0 ? (
            <p className="py-12 text-center text-sm text-white/40">هنوز فروشی ثبت نشده است</p>
          ) : (
            <SalesChart data={top.map((p) => ({ label: p.name, value: p.sold }))} />
          )}
        </Card>

        <Card className="p-6">
          <div className="mb-4 flex items-center justify-between">
            <h3 className="text-lg font-bold text-white">تیکت‌های اخیر</h3>
            <Link href="/admin/tickets" className="text-xs font-medium text-[#e60053] hover:underline">همه</Link>
          </div>
          <ul className="space-y-3">
            {tickets.slice(0, 4).map((t) => (
              <li key={t.id} className="flex items-start gap-3 rounded-xl bg-white/[0.03] p-3">
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium text-white">{t.subject}</p>
                  <p className="text-xs text-white/45">{t.userName} · {t.department}</p>
                </div>
                <StatusBadge status={ticketStatusLabel[t.status]} />
              </li>
            ))}
            {tickets.length === 0 && <li className="py-6 text-center text-sm text-white/40">تیکتی نیست</li>}
          </ul>
        </Card>
      </div>

      <Card className="mt-6 overflow-hidden">
        <div className="flex items-center justify-between p-6 pb-4">
          <h3 className="text-lg font-bold text-white">سفارش‌های اخیر</h3>
          <Link href="/admin/orders" className="text-xs font-medium text-[#e60053] hover:underline">مشاهده همه</Link>
        </div>
        <div className="divide-y divide-white/5">
          {orders.slice(0, 6).map((o) => (
            <div key={o.id} className="flex items-center justify-between gap-3 px-6 py-3">
              <div>
                <p className="font-mono text-sm text-white/70">{o.code}</p>
                <p className="text-xs text-white/40">{o.userName} · {o.date}</p>
              </div>
              <span className="text-sm text-white/80">{formatToman(o.total)}</span>
              <StatusBadge status={orderStatusLabel[o.status]} />
            </div>
          ))}
          {orders.length === 0 && <p className="py-10 text-center text-sm text-white/40">سفارشی نیست</p>}
        </div>
      </Card>
    </div>
  );
}
