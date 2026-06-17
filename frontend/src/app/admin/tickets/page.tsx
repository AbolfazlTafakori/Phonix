import { adminTickets, type AdminTicket } from "@/data/admin";
import { Card, PageHeader, StatusBadge, DataTable, type Column } from "@/components/admin/ui";

export const metadata = { title: "تیکت‌ها | پنل مدیریت" };

const columns: Column<AdminTicket>[] = [
  { header: "شماره", primary: true, td: "font-mono text-white/60", cell: (t) => t.id },
  { header: "کاربر", cell: (t) => t.user },
  { header: "موضوع", td: "text-white/70", cell: (t) => t.subject },
  { header: "دپارتمان", td: "text-white/65", cell: (t) => t.department },
  { header: "وضعیت", cell: (t) => <StatusBadge status={t.status} /> },
  { header: "تاریخ", td: "text-white/50", cell: (t) => t.date },
];

export default function AdminTicketsPage() {
  return (
    <div>
      <PageHeader title="تیکت‌های پشتیبانی" desc={`${adminTickets.length} تیکت`} />

      <Card className="overflow-hidden">
        <DataTable columns={columns} rows={adminTickets} rowKey={(t) => t.id} minWidth={720} />
      </Card>
    </div>
  );
}
