import { PageTitle, Panel, StatCard } from "@/components/account/Panel";

export const metadata = { title: "دعوت دوستان | Phoenix Verify" };

export default function InvitePage() {
  return (
    <div>
      <PageTitle title="دعوت دوستان" desc="با دعوت دوستان خود، از هر خرید آن‌ها پورسانت بگیرید." />

      <div className="mb-6 grid gap-4 sm:grid-cols-2">
        <StatCard label="کد معرف شما" value="PHX-USER-2024" accent="#e60053" />
        <StatCard label="درآمد کل از معرفی" value="۸۵,۰۰۰ تومان" accent="#22c55e" />
      </div>

      <Panel className="mb-6">
        <h2 className="mb-4 text-lg font-bold text-white">لینک دعوت اختصاصی شما</h2>
        <div className="flex flex-col gap-3 sm:flex-row">
          <input
            readOnly
            dir="ltr"
            value="https://phoenixverify.com/r/PHX-USER-2024"
            className="h-12 flex-1 rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-left text-sm text-white/80 outline-none"
          />
          <button className="h-12 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 text-sm font-bold text-white transition hover:brightness-110">
            کپی لینک
          </button>
        </div>
      </Panel>

      <Panel>
        <h2 className="mb-4 text-lg font-bold text-white">چطور کار می‌کند؟</h2>
        <ol className="space-y-4">
          {[
            "لینک دعوت اختصاصی خود را برای دوستانتان ارسال کنید.",
            "دوستان شما با این لینک ثبت‌نام کرده و خرید می‌کنند.",
            "از هر خرید آن‌ها، ۱۰٪ پورسانت به کیف پول شما اضافه می‌شود.",
          ].map((step, i) => (
            <li key={i} className="flex items-start gap-3 text-sm leading-7 text-white/75">
              <span className="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-[#e60053]/15 text-sm font-bold text-[#e60053]">
                {i + 1}
              </span>
              {step}
            </li>
          ))}
        </ol>
      </Panel>
    </div>
  );
}
