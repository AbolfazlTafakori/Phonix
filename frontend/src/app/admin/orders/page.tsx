import { adminOrders, type AdminOrder } from "@/data/admin";
import { Card, PageHeader, StatusBadge, DataTable, type Column } from "@/components/admin/ui";

export const metadata = { title: "سفارشات | پنل مدیریت" };

const filters = ["همه", "پرداخت شده", "در انتظار", "لغو شده"];

const columns: Column<AdminOrder>[] = [
  { header: "شماره", primary: true, td: "font-mono text-white/60", cell: (o) => o.id },
  { header: "مشتری", cell: (o) => o.customer },
  { header: "محصول", td: "text-white/70", cell: (o) => o.product },
  { header: "مبلغ", cell: (o) => o.amount },
  { header: "وضعیت", cell: (o) => <StatusBadge status={o.status} /> },
  { header: "تاریخ", td: "text-white/50", cell: (o) => o.date },
];

export default function AdminOrdersPage() {
  return (
    <div>
      <PageHeader title="سفارشات" desc={`${adminOrders.length} سفارش`} />

      <div className="mb-5 flex flex-wrap gap-2">
        {filters.map((f, i) => (
          <button
            key={f}
            className={`rounded-full border px-4 py-1.5 text-sm font-medium transition ${
              i === 0 ? "border-transparent bg-white/10 text-white" : "border-white/10 text-white/60 hover:text-white"
            }`}
          >
            {f}
          </button>
        ))}
      </div>

      <Card className="overflow-hidden">
        <DataTable columns={columns} rows={adminOrders} rowKey={(o) => o.id} minWidth={720} />
      </Card>
    </div>
  );
}
