import { PageTitle, Panel, StatCard } from "@/components/account/Panel";

export const metadata = { title: "کیف پول | Phoenix Verify" };

const transactions = [
  { title: "شارژ کیف پول", amount: "+۵۰۰,۰۰۰ تومان", date: "۱۴۰۳/۰۳/۲۲", positive: true },
  { title: "خرید اشتراک نتفلیکس", amount: "−۲۹۰,۰۰۰ تومان", date: "۱۴۰۳/۰۳/۲۲", positive: false },
  { title: "پورسانت معرفی", amount: "+۲۹,۰۰۰ تومان", date: "۱۴۰۳/۰۳/۱۸", positive: true },
  { title: "خرید Spotify Premium", amount: "−۱۸۵,۰۰۰ تومان", date: "۱۴۰۳/۰۳/۱۸", positive: false },
];

export default function WalletPage() {
  return (
    <div>
      <PageTitle title="کیف پول" desc="موجودی و تراکنش‌های حساب خود را مدیریت کنید." />

      <div className="mb-6 grid gap-4 sm:grid-cols-3">
        <StatCard label="موجودی فعلی" value="۵۴,۰۰۰ تومان" accent="#3a64f2" />
        <StatCard label="مجموع شارژ" value="۵۰۰,۰۰۰ تومان" accent="#22c55e" />
        <StatCard label="درآمد معرفی" value="۸۵,۰۰۰ تومان" accent="#e60053" />
      </div>

      <Panel className="mb-6">
        <h2 className="mb-4 text-lg font-bold text-white">افزایش موجودی</h2>
        <div className="flex flex-col gap-3 sm:flex-row">
          <input
            placeholder="مبلغ مورد نظر (تومان)"
            className="h-12 flex-1 rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3e3af2] placeholder:text-white/35"
          />
          <button className="h-12 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 text-sm font-bold text-white transition hover:brightness-110">
            پرداخت و شارژ
          </button>
        </div>
      </Panel>

      <Panel>
        <h2 className="mb-4 text-lg font-bold text-white">تراکنش‌های اخیر</h2>
        <ul className="divide-y divide-white/8">
          {transactions.map((t, i) => (
            <li key={i} className="flex items-center justify-between py-4">
              <div>
                <p className="text-sm font-medium text-white">{t.title}</p>
                <p className="text-xs text-white/45">{t.date}</p>
              </div>
              <span className={`text-sm font-bold ${t.positive ? "text-emerald-400" : "text-rose-400"}`}>
                {t.amount}
              </span>
            </li>
          ))}
        </ul>
      </Panel>
    </div>
  );
}
