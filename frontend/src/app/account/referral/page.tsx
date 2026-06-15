import { PageTitle, Panel, StatCard } from "@/components/account/Panel";
import { referralRows } from "@/data/account";

export const metadata = { title: "گزارش درآمد معرف | Phoenix Verify" };

export default function ReferralPage() {
  return (
    <div>
      <PageTitle title="گزارش درآمد معرف" desc="درآمد حاصل از معرفی دوستان خود را دنبال کنید." />

      <div className="mb-6 grid gap-4 sm:grid-cols-3">
        <StatCard label="مجموع درآمد" value="۸۵,۰۰۰ تومان" accent="#e60053" />
        <StatCard label="تعداد معرفی" value="۴ نفر" accent="#3a64f2" />
        <StatCard label="در انتظار تسویه" value="۲۱,۰۰۰ تومان" accent="#f59e0b" />
      </div>

      <Panel className="overflow-x-auto p-0">
        <table className="w-full min-w-[680px] text-right">
          <thead>
            <tr className="border-b border-white/8 text-sm text-white/55">
              <th className="px-6 py-4 font-medium">نام کاربر</th>
              <th className="px-6 py-4 font-medium">شماره سفارش</th>
              <th className="px-6 py-4 font-medium">مبلغ سفارش</th>
              <th className="px-6 py-4 font-medium">میزان پورسانت</th>
              <th className="px-6 py-4 font-medium">تاریخ</th>
            </tr>
          </thead>
          <tbody>
            {referralRows.map((r, i) => (
              <tr key={i} className="border-b border-white/5 text-sm text-white/85 transition hover:bg-white/[0.03]">
                <td className="px-6 py-4">{r.user}</td>
                <td className="px-6 py-4 font-mono text-white/70">{r.orderId}</td>
                <td className="px-6 py-4">{r.amount}</td>
                <td className="px-6 py-4 font-bold text-emerald-400">{r.commission}</td>
                <td className="px-6 py-4 text-white/55">{r.date}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
    </div>
  );
}
