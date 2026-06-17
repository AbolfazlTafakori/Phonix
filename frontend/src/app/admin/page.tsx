import Link from "next/link";
import { kpis, salesData, adminOrders, adminTickets, type AdminOrder } from "@/data/admin";
import { Card, KpiCard, StatusBadge, SalesChart, DataTable, type Column } from "@/components/admin/ui";
import ServerStatus from "@/components/admin/ServerStatus";

const orderColumns: Column<AdminOrder>[] = [
  { header: "شماره", primary: true, td: "font-mono text-white/60", cell: (o) => o.id },
  { header: "مشتری", cell: (o) => o.customer },
  { header: "محصول", td: "text-white/70", cell: (o) => o.product },
  { header: "مبلغ", cell: (o) => o.amount },
  { header: "وضعیت", cell: (o) => <StatusBadge status={o.status} /> },
  { header: "تاریخ", td: "text-white/50", cell: (o) => o.date },
];

export default function AdminDashboard() {
  return (
    <div>
      {/* KPI cards */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {kpis.map((k) => (
          <KpiCard key={k.label} {...k} />
        ))}
      </div>

      {/* server status */}
      <div className="mt-6">
        <ServerStatus />
      </div>

      {/* chart + recent tickets */}
      <div className="mt-6 grid gap-6 lg:grid-cols-3">
        <Card className="p-6 lg:col-span-2">
          <div className="mb-6 flex items-center justify-between">
            <div>
              <h3 className="text-lg font-bold text-white">نمودار فروش</h3>
              <p className="text-sm text-white/45">۱۲ ماه گذشته</p>
            </div>
            <span className="rounded-full bg-emerald-500/15 px-3 py-1 text-xs font-bold text-emerald-400">
              ▲ ۱۸.۴٪ رشد
            </span>
          </div>
          <SalesChart data={salesData} />
        </Card>

        <Card className="p-6">
          <div className="mb-4 flex items-center justify-between">
            <h3 className="text-lg font-bold text-white">تیکت‌های اخیر</h3>
            <Link href="/admin/tickets" className="text-xs font-medium text-[#e60053] hover:underline">
              همه
            </Link>
          </div>
          <ul className="space-y-3">
            {adminTickets.map((t) => (
              <li key={t.id} className="flex items-start gap-3 rounded-xl bg-white/[0.03] p-3">
                <span className="mt-1 grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-[#e60053]/15 text-xs font-bold text-[#e60053]">
                  {t.id.replace("T-", "")}
                </span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium text-white">{t.subject}</p>
                  <p className="text-xs text-white/45">{t.user} · {t.department}</p>
                </div>
                <StatusBadge status={t.status} />
              </li>
            ))}
          </ul>
        </Card>
      </div>

      {/* recent orders */}
      <Card className="mt-6 overflow-hidden">
        <div className="flex items-center justify-between p-6 pb-4">
          <h3 className="text-lg font-bold text-white">سفارشات اخیر</h3>
          <Link href="/admin/orders" className="text-xs font-medium text-[#e60053] hover:underline">
            مشاهده همه
          </Link>
        </div>
        <DataTable columns={orderColumns} rows={adminOrders} rowKey={(o) => o.id} minWidth={700} />
      </Card>
    </div>
  );
}
