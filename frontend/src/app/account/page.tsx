import { PageTitle, Panel } from "@/components/account/Panel";

export const metadata = { title: "پروفایل من | Phoenix Verify" };

const fields = [
  { label: "نام و نام خانوادگی", value: "کاربر فونیکس", type: "text" },
  { label: "ایمیل", value: "user@phoenixverify.com", type: "email" },
  { label: "شماره موبایل", value: "۰۹۱۲۳۴۵۶۷۸۹", type: "tel" },
  { label: "نام کاربری", value: "phoenix_user", type: "text" },
];

export default function ProfilePage() {
  return (
    <div>
      <PageTitle title="پروفایل من" desc="اطلاعات حساب کاربری خود را مشاهده و ویرایش کنید." />

      <Panel>
        <div className="mb-8 flex items-center gap-4">
          <div className="grid h-20 w-20 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-2xl font-bold text-white">
            ف
          </div>
          <div>
            <p className="text-lg font-bold text-white">کاربر فونیکس</p>
            <p className="text-sm text-white/50">عضو از خرداد ۱۴۰۳</p>
          </div>
          <button className="mr-auto rounded-xl border border-white/10 px-4 py-2 text-sm text-white/80 transition hover:bg-white/5">
            تغییر تصویر
          </button>
        </div>

        <form className="grid gap-5 sm:grid-cols-2">
          {fields.map((f) => (
            <div key={f.label}>
              <label className="mb-2 block text-sm text-white/80">{f.label}</label>
              <input
                type={f.type}
                defaultValue={f.value}
                className="h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3e3af2]"
              />
            </div>
          ))}

          <div className="sm:col-span-2">
            <button className="h-12 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-10 text-sm font-bold text-white transition hover:brightness-110">
              ذخیره تغییرات
            </button>
          </div>
        </form>
      </Panel>
    </div>
  );
}
