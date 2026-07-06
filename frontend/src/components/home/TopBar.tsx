import Link from "next/link";

// Thin full-width promo bar pinned above the header.
export default function TopBar() {
  return (
    <div className="hl-grad text-white">
      <div className="mx-auto flex h-[42px] max-w-[1600px] items-center justify-center gap-4 px-16 text-[13px] font-bold">
        <p className="flex items-center gap-2 text-center">
          <span aria-hidden className="text-[15px]">🎉</span>
          جشنواره تابستانی فونیکس وریفای! تخفیف‌های ویژه تا ۳۰ درصد روی آیتم‌های محبوب خدمات
        </p>
        <Link
          href="/products"
          className="shrink-0 rounded-full bg-white/20 px-3.5 py-1 text-[12px] text-white transition hover:bg-white/30"
        >
          مشاهده تخفیف‌ها
        </Link>
      </div>
    </div>
  );
}
