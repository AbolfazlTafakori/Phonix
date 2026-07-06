import Link from "next/link";

// Thin full-width promo bar pinned above the header.
export default function TopBar() {
  return (
    <div className="hl-grad text-white">
      <div className="mx-auto flex h-[44px] max-w-[1840px] items-center justify-center gap-4 px-4 text-[12.5px] font-bold sm:h-[48px] sm:px-8 sm:text-[15px] xl:px-16">
        <p className="flex min-w-0 items-center gap-2 text-center">
          <span aria-hidden className="shrink-0 text-[15px] sm:text-[17px]">🎉</span>
          <span className="truncate">جشنواره تابستانی فونیکس وریفای! تخفیف‌های ویژه تا ۳۰ درصد روی آیتم‌های محبوب خدمات</span>
        </p>
        <Link
          href="/products"
          className="hidden shrink-0 rounded-full bg-white/20 px-3.5 py-1 text-[14px] text-white transition hover:bg-white/30 sm:inline-block"
        >
          مشاهده تخفیف‌ها
        </Link>
      </div>
    </div>
  );
}
