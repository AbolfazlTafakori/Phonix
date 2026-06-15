import { PageTitle, Panel } from "@/components/account/Panel";

export const metadata = { title: "احراز هویت | Phoenix Verify" };

export default function KycPage() {
  return (
    <div>
      <PageTitle title="احراز هویت" desc="برای استفاده از همه‌ی امکانات، هویت خود را تأیید کنید." />

      <Panel>
        <div className="mb-6 flex items-center gap-3 rounded-xl border border-amber-500/20 bg-amber-500/10 px-4 py-3 text-sm text-amber-300">
          <span>⚠</span>
          حساب شما هنوز تأیید نشده است. لطفاً اطلاعات زیر را تکمیل کنید.
        </div>

        <form className="grid gap-5 sm:grid-cols-2">
          <div>
            <label className="mb-2 block text-sm text-white/80">نام و نام خانوادگی</label>
            <input className="h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none focus:border-[#3e3af2]" />
          </div>
          <div>
            <label className="mb-2 block text-sm text-white/80">کد ملی</label>
            <input className="h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none focus:border-[#3e3af2]" />
          </div>
          <div className="sm:col-span-2">
            <label className="mb-2 block text-sm text-white/80">تصویر کارت ملی</label>
            <div className="flex h-32 cursor-pointer items-center justify-center rounded-xl border border-dashed border-white/15 bg-[#0d0d15] text-sm text-white/45 transition hover:border-[#3e3af2]/50">
              برای آپلود کلیک کنید یا فایل را اینجا بکشید
            </div>
          </div>

          <div className="sm:col-span-2">
            <button className="h-12 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-10 text-sm font-bold text-white transition hover:brightness-110">
              ارسال برای بررسی
            </button>
          </div>
        </form>
      </Panel>
    </div>
  );
}
