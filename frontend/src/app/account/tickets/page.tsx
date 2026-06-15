import { PageTitle, Panel, StatusBadge } from "@/components/account/Panel";
import { tickets } from "@/data/account";

export const metadata = { title: "تیکت پشتیبانی | Phoenix Verify" };

export default function TicketsPage() {
  return (
    <div>
      <div className="mb-6 flex items-center justify-between gap-4">
        <PageTitle title="تیکت پشتیبانی" />
        <button className="h-11 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-6 text-sm font-bold text-white transition hover:brightness-110">
          ثبت تیکت جدید
        </button>
      </div>

      <Panel className="overflow-x-auto p-0">
        <table className="w-full min-w-[640px] text-right">
          <thead>
            <tr className="border-b border-white/8 text-sm text-white/55">
              <th className="px-6 py-4 font-medium">شماره</th>
              <th className="px-6 py-4 font-medium">موضوع</th>
              <th className="px-6 py-4 font-medium">دپارتمان</th>
              <th className="px-6 py-4 font-medium">وضعیت</th>
              <th className="px-6 py-4 font-medium">تاریخ</th>
            </tr>
          </thead>
          <tbody>
            {tickets.map((t) => (
              <tr key={t.id} className="border-b border-white/5 text-sm text-white/85 transition hover:bg-white/[0.03]">
                <td className="px-6 py-4 font-mono text-white/70">{t.id}</td>
                <td className="px-6 py-4">{t.subject}</td>
                <td className="px-6 py-4 text-white/65">{t.department}</td>
                <td className="px-6 py-4"><StatusBadge status={t.status} /></td>
                <td className="px-6 py-4 text-white/55">{t.date}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
    </div>
  );
}
