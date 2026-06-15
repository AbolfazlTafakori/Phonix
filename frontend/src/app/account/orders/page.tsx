import { PageTitle, Panel, StatusBadge } from "@/components/account/Panel";
import { orders } from "@/data/account";

export const metadata = { title: "سفارشات من | Phoenix Verify" };

export default function OrdersPage() {
  return (
    <div>
      <PageTitle title="سفارشات من" desc="تاریخچه‌ی سفارش‌ها و وضعیت آن‌ها." />

      <Panel className="overflow-x-auto p-0">
        <table className="w-full min-w-[640px] text-right">
          <thead>
            <tr className="border-b border-white/8 text-sm text-white/55">
              <th className="px-6 py-4 font-medium">شماره سفارش</th>
              <th className="px-6 py-4 font-medium">محصول</th>
              <th className="px-6 py-4 font-medium">مبلغ</th>
              <th className="px-6 py-4 font-medium">وضعیت</th>
              <th className="px-6 py-4 font-medium">تاریخ</th>
            </tr>
          </thead>
          <tbody>
            {orders.map((o) => (
              <tr key={o.id} className="border-b border-white/5 text-sm text-white/85 transition hover:bg-white/[0.03]">
                <td className="px-6 py-4 font-mono text-white/70">{o.id}</td>
                <td className="px-6 py-4">{o.product}</td>
                <td className="px-6 py-4">{o.amount}</td>
                <td className="px-6 py-4"><StatusBadge status={o.status} /></td>
                <td className="px-6 py-4 text-white/55">{o.date}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
    </div>
  );
}
