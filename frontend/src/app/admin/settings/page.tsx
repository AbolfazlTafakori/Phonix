import { Card, PageHeader } from "@/components/admin/ui";

export const metadata = { title: "تنظیمات | پنل مدیریت" };

function Field({ label, value, type = "text" }: { label: string; value?: string; type?: string }) {
  return (
    <div>
      <label className="mb-2 block text-sm text-white/70">{label}</label>
      <input
        type={type}
        defaultValue={value}
        className="h-11 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3a64f2]"
      />
    </div>
  );
}

function Toggle({ label, on = false }: { label: string; on?: boolean }) {
  return (
    <label className="flex cursor-pointer items-center justify-between py-3">
      <span className="text-sm text-white/80">{label}</span>
      <span className={`relative h-6 w-11 rounded-full transition ${on ? "bg-[#e60053]" : "bg-white/15"}`}>
        <span className={`absolute top-0.5 h-5 w-5 rounded-full bg-white transition-all ${on ? "right-0.5" : "right-[22px]"}`} />
      </span>
    </label>
  );
}

export default function AdminSettingsPage() {
  return (
    <div>
      <PageHeader title="تنظیمات" desc="پیکربندی فروشگاه و حساب مدیر" />

      <div className="grid gap-6 lg:grid-cols-2">
        <Card className="p-6">
          <h3 className="mb-5 text-lg font-bold text-white">اطلاعات فروشگاه</h3>
          <div className="grid gap-5">
            <Field label="نام فروشگاه" value="Phoenix Verify" />
            <Field label="ایمیل پشتیبانی" value="support@phoenixverify.com" type="email" />
            <Field label="شماره تماس" value="۰۲۱-۱۲۳۴۵۶۷۸" type="tel" />
            <Field label="درصد پورسانت معرف" value="۱۰" />
          </div>
          <button className="mt-6 h-11 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 text-sm font-bold text-white transition hover:brightness-110">
            ذخیره تغییرات
          </button>
        </Card>

        <div className="space-y-6">
          <Card className="p-6">
            <h3 className="mb-3 text-lg font-bold text-white">حساب مدیر</h3>
            <div className="grid gap-5">
              <Field label="نام کاربری" value="admin" />
              <Field label="رمز عبور جدید" type="password" />
            </div>
            <button className="mt-6 h-11 rounded-xl border border-white/10 px-8 text-sm font-bold text-white/85 transition hover:bg-white/5">
              تغییر رمز
            </button>
          </Card>

          <Card className="p-6">
            <h3 className="mb-2 text-lg font-bold text-white">اعلان‌ها</h3>
            <div className="divide-y divide-white/8">
              <Toggle label="اعلان سفارش جدید" on />
              <Toggle label="اعلان تیکت پشتیبانی" on />
              <Toggle label="گزارش فروش هفتگی" />
              <Toggle label="هشدار اتمام موجودی" on />
            </div>
          </Card>
        </div>
      </div>
    </div>
  );
}
