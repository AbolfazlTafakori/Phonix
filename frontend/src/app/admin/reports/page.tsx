"use client";

import { useState } from "react";
import { reports, adminProducts, type AdminProduct } from "@/data/admin";
import { Card, PageHeader, SalesChart, DataTable, type Column } from "@/components/admin/ui";

type TopProduct = AdminProduct & { sales: number; share: number };

const topProducts: TopProduct[] = adminProducts.slice(0, 5).map((p, i) => ({
  ...p,
  sales: (5 - i) * 47,
  share: (5 - i) * 18,
}));

const topColumns: Column<TopProduct>[] = [
  {
    header: "محصول",
    primary: true,
    cell: (p) => (
      <div className="flex items-center gap-3">
        <img src={p.image} alt={p.name} className="h-9 w-9 rounded-lg object-cover" />
        <span className="font-medium">{p.name}</span>
      </div>
    ),
  },
  { header: "دسته", td: "text-white/60", cell: (p) => p.category },
  { header: "فروش", cell: (p) => `${p.sales} عدد` },
  {
    header: "سهم",
    cell: (p) => (
      <div className="flex items-center gap-2">
        <div className="h-2 w-24 overflow-hidden rounded-full bg-white/10">
          <div className="h-full rounded-full bg-gradient-to-l from-[#e60053] to-[#6d28d9]" style={{ width: `${p.share}%` }} />
        </div>
        <span className="text-xs text-white/50">٪{p.share}</span>
      </div>
    ),
  },
];

const periods = [
  { key: "day", label: "روز" },
  { key: "week", label: "هفته" },
  { key: "month", label: "ماه" },
  { key: "year", label: "سال" },
] as const;

type PeriodKey = (typeof periods)[number]["key"];

export default function AdminReportsPage() {
  const [period, setPeriod] = useState<PeriodKey>("week");
  const data = reports[period];
  const periodLabel = periods.find((p) => p.key === period)!.label;

  return (
    <div>
      <PageHeader
        title="گزارش‌ها"
        desc="تحلیل عملکرد فروش در بازه‌های زمانی"
        action={
          <div className="flex rounded-xl border border-white/10 bg-white/5 p-1">
            {periods.map((p) => (
              <button
                key={p.key}
                onClick={() => setPeriod(p.key)}
                className={`rounded-lg px-4 py-1.5 text-sm font-bold transition ${
                  period === p.key ? "bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white" : "text-white/55 hover:text-white"
                }`}
              >
                {p.label}
              </button>
            ))}
          </div>
        }
      />

      {/* summary */}
      <div className="grid gap-4 sm:grid-cols-3">
        <Card className="p-5">
          <p className="text-sm text-white/50">فروش این {periodLabel}</p>
          <p className="mt-2 text-2xl font-bold text-emerald-400">{data.total}</p>
        </Card>
        <Card className="p-5">
          <p className="text-sm text-white/50">تعداد سفارش</p>
          <p className="mt-2 text-2xl font-bold text-white">{data.orders}</p>
        </Card>
        <Card className="p-5">
          <p className="text-sm text-white/50">میانگین هر سفارش</p>
          <p className="mt-2 text-2xl font-bold text-[#6f93ff]">{data.avg}</p>
        </Card>
      </div>

      {/* chart */}
      <Card className="mt-6 p-6">
        <h3 className="mb-6 text-lg font-bold text-white">روند فروش ({periodLabel})</h3>
        <SalesChart data={data.chart} />
      </Card>

      {/* top products */}
      <Card className="mt-6 overflow-hidden">
        <h3 className="p-6 pb-4 text-lg font-bold text-white">پرفروش‌ترین محصولات</h3>
        <DataTable columns={topColumns} rows={topProducts} rowKey={(p) => p.id} minWidth={560} />
      </Card>
    </div>
  );
}
