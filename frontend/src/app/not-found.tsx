import Link from "next/link";

export const metadata = {
  title: "صفحه پیدا نشد",
  description: "صفحه مورد نظر شما در فونیکس وریفای پیدا نشد.",
};

export default function NotFound() {
  return (
    <div className="grid min-h-[70vh] place-items-center px-4 py-24">
      <div className="w-full max-w-[560px] rounded-[22px] border bg-[var(--ac-panel-bg)] p-8 text-center sm:p-12" style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}>
        <p className="text-[64px] font-black leading-none sm:text-[84px]" style={{ color: "var(--hl-red, #ef233c)" }}>۴۰۴</p>
        <h1 className="mt-4 text-[20px] font-black sm:text-[24px]" style={{ color: "var(--ac-title)" }}>صفحه‌ای که دنبالش بودید پیدا نشد</h1>
        <p className="mt-3 text-[13px] leading-7 sm:text-[14px]" style={{ color: "var(--ac-muted)" }}>
          ممکن است آدرس تغییر کرده یا محصول مورد نظر حذف شده باشد.
        </p>
        <div className="mt-8 flex flex-wrap items-center justify-center gap-3">
          <Link href="/" className="rounded-xl px-6 py-3 text-[13px] font-black text-white transition hover:brightness-105 sm:px-8 sm:text-[14px]" style={{ background: "var(--ac-btn, #ef233c)" }}>
            بازگشت به صفحه اصلی
          </Link>
          <Link href="/products" className="rounded-xl border px-6 py-3 text-[13px] font-bold transition hover:bg-[var(--ac-menu-hover)] sm:px-8 sm:text-[14px]" style={{ borderColor: "var(--ac-panel-border)", color: "var(--ac-title)" }}>
            مشاهده محصولات
          </Link>
        </div>
      </div>
    </div>
  );
}
