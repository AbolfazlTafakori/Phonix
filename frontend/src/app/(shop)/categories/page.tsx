import Link from "next/link";
import { api } from "@/lib/api";
import { formatNumber } from "@/lib/format";
import type { Category } from "@/lib/types";
import HomeNewsletter from "@/components/home/HomeNewsletter";
import TrustStats from "@/components/home/TrustStats";
import CategoriesFaq from "@/components/categories/CategoriesFaq";

export const dynamic = "force-dynamic";
export const metadata = {
  title: "دسته‌بندی محصولات",
  description: "دسته‌بندی کامل خدمات و محصولات دیجیتال فونیکس وریفای: استریم، وریفای، گیفت‌کارت، VPN، شماره مجازی و نرم‌افزار.",
};

const heroFeatures = [
  "تحویل آنی و خودکار",
  "پشتیبانی ۲۴/۷",
  "قیمت‌های رقابتی",
  "تضمین کیفیت و اصالت",
];

const categoryMeta: Record<string, { desc: string; logo: string }> = {
  "اشتراک‌های استریم": { desc: "نتفلیکس، یوتیوب و سایر پلتفرم‌های محبوب", logo: "/figma/catpage-stream.png" },
  "اپل موزیک و اسپاتیفای": { desc: "اشتراک‌های اپل موزیک، اسپاتیفای و پادکست‌های برتر", logo: "/figma/catpage-music.png" },
  "فیلترشکن / VPN": { desc: "سرورهای پرسرعت و امن وی‌پی‌ان‌های معتبر", logo: "/figma/catpage-vpn.png" },
  "گیفت کارت": { desc: "گیفت کارت‌های بین‌المللی برندهای معتبر", logo: "/figma/catpage-giftcard.png" },
  "تایید و وریفای حساب": { desc: "تایید حساب‌های بین‌المللی سریع و مطمئن", logo: "/figma/catpage-verify.png" },
  "شماره مجازی": { desc: "شماره مجازی از بیش از ۱۰۰ کشور دنیا", logo: "/figma/catpage-number.png" },
  "بازی و سرگرمی": { desc: "اکانت بازی، اشتراک‌ها و محتواهای گیمینگ", logo: "/figma/catpage-game.png" },
  "نرم‌افزارها و ابزارها": { desc: "لایسنس نرم‌افزارهای پریمیوم و ابزارهای کاربردی", logo: "/figma/catpage-software.png" },
  "نرم‌افزارها": { desc: "لایسنس نرم‌افزارهای پریمیوم و ابزارهای کاربردی", logo: "/figma/catpage-software.png" },
  "نتفلیکس": { desc: "اکانت و اشتراک نتفلیکس اورجینال", logo: "/figma/catpage-stream.png" },
  "شبکه‌های اجتماعی": { desc: "خدمات شبکه‌های اجتماعی", logo: "/figma/catpage-verify.png" },
};

const defaultMeta = { desc: "محصولات و خدمات متنوع", logo: "" };

const popularSubs = [
  { name: "Netflix", logo: "/figma/prod-netflix.png" },
  { name: "Spotify", logo: "/figma/prod-spotify.png" },
  { name: "Apple Music", logo: "/figma/prod-applemusic.png" },
  { name: "YouTube Premium", logo: "/figma/cat-stream.png" },
  { name: "Disney+", logo: "/figma/cat-stream.png" },
  { name: "Google Play", logo: "/figma/cat-giftcard.png" },
  { name: "Steam", logo: "/figma/cat-game.png" },
  { name: "PlayStation", logo: "/figma/cat-game.png" },
];

const needCards = [
  { img: "/figma/catpage-need-finance.png", href: "/products", alt: "خدمات مالی" },
  { img: "/figma/catpage-need-social.png", href: "/products", alt: "شبکه‌های اجتماعی" },
  { img: "/figma/catpage-need-vpn.png", href: "/products", alt: "امنیت و VPN" },
  { img: "/figma/catpage-need-stream.png", href: "/products", alt: "سرگرمی و استریم" },
];

const banners = [
  { img: "/figma/catpage-banner-discount.png", href: "/products", alt: "تخفیف ویژه اشتراک‌ها" },
  { img: "/figma/catpage-banner-number.png", href: "/products", alt: "شماره مجازی برای همه کشورها" },
  { img: "/figma/catpage-banner-gamers.png", href: "/products", alt: "ویژه گیمرها" },
];

export default async function CategoriesPage() {
  let categories: Category[] = [];
  try {
    categories = (await api.categories.list()).filter((c) => c.isActive);
  } catch {}

  return (
    <>
      {/* ── Hero ── */}
      <section className="overflow-hidden border-b border-[var(--hl-border)] bg-[var(--hl-surface)]">
        <div className="mx-auto max-w-[1840px] px-4 sm:px-8 xl:px-16">
          <nav className="flex items-center justify-start gap-2 pb-2 pt-6 text-[13px] text-[var(--hl-muted)]">
            <Link href="/" className="flex items-center gap-1 transition hover:text-[var(--hl-red)]">
              <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 9.5L12 3l9 6.5V20a1 1 0 01-1 1H4a1 1 0 01-1-1z" /><path d="M9 22V12h6v10" /></svg>
              خانه
            </Link>
            <span>/</span>
            <span className="font-medium text-[var(--hl-ink)]">دسته‌بندی‌ها</span>
          </nav>

          <div className="flex flex-col items-center gap-7 pb-10 pt-2 sm:pt-4 xl:flex-row-reverse xl:items-center xl:gap-10">
            {/* Below xl the shield reads best centered & sized; only at xl+ is there room for the
                three-column row, where the image is capped so the middle text column never crushes. */}
            <div className="shrink-0 xl:w-[38%] xl:max-w-[520px]">
              <img fetchPriority="high" decoding="async" src="/figma/catpage-hero-shield.png" alt="" className="mx-auto h-auto w-56 max-w-full object-contain sm:w-72 xl:w-full xl:max-w-[520px] xl:-translate-x-8" />
            </div>

            <div className="flex-1 text-center xl:text-right">
              <p className="text-[26px] font-black text-[var(--hl-red)] sm:text-[34px] xl:text-[40px]">دسته‌بندی</p>
              <h1 className="mt-1 text-[22px] font-extrabold leading-[1.5] text-[var(--hl-ink)] sm:text-[30px] xl:text-[36px]">
                خدمات و محصولات فونیکس وریفای
              </h1>
              <p className="mx-auto mt-3 max-w-md text-[14px] leading-7 text-[var(--hl-muted)] sm:mt-4 sm:text-[16px] sm:leading-8 xl:mx-0">
                همه خدمات دیجیتال موردنیازت را در دسته‌بندی‌های متنوع
                <br className="hidden sm:inline" />
                به صورت امن، سریع و با بهترین قیمت پیدا کن.
              </p>
            </div>

            {/* The feature card only reads well in the desktop three-column row; below xl it stacked awkwardly
                under the text, so it is shown at xl+ only and the hero stays shield + heading on phone/tablet. */}
            <div className="hidden shrink-0 rounded-[22px] border border-[var(--hl-border)] bg-[var(--hl-tint)] p-6 shadow-sm xl:block xl:w-[300px]">
              <h3 className="text-center text-[20px] font-black leading-[1.6] text-[var(--hl-ink)]">دسترسی آسان به<br /><span className="text-[24px]">خدمات دیجیتال</span></h3>
              <div className="mt-6 flex items-end gap-1">
                <ul className="flex-1 space-y-6">
                  {heroFeatures.map((text) => (
                    <li key={text} className="flex items-center gap-2 whitespace-nowrap text-[15px] font-bold text-[var(--hl-ink)]">
                      <svg viewBox="0 0 24 24" className="h-6 w-6 shrink-0 text-[var(--hl-red)]" fill="currentColor">
                        <path d="M12 2a10 10 0 110 20 10 10 0 010-20zm-1.7 14.3l6.4-6.4-1.4-1.4-5 5-2.3-2.3-1.4 1.4 3.7 3.7z" />
                      </svg>
                      {text}
                    </li>
                  ))}
                </ul>
                <img loading="lazy" decoding="async" src="/figma/catpage-hero-support.png" alt="" className="-mr-2 -mb-1 h-28 w-28 shrink-0 object-contain" />
              </div>
              <Link
                href="/products"
                className="mt-4 flex items-center justify-center gap-2 rounded-xl py-3 text-[16px] font-bold text-white"
                style={{ background: "linear-gradient(95deg, #FF7A2E 0%, #F0392C 100%)" }}
              >
                مشاهده راهنما
                <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M9 6l6 6-6 6" />
                </svg>
              </Link>
            </div>
          </div>
        </div>
      </section>

      {/* ── Stats ── */}
      <div className="py-8">
        <TrustStats />
      </div>

      {/* ── Category Grid ── */}
      <section className="mx-auto max-w-[1840px] px-4 py-16 sm:px-8 xl:px-16">
        <div className="mb-8 flex items-start gap-2">
          <span className="mt-2.5 h-6 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f]" />
          <div>
            <h2 className="text-[22px] font-black text-[var(--hl-ink)] sm:text-[26px] xl:text-[30px]">دسته‌بندی‌ها</h2>
            <p className="mt-1.5 text-[15px] text-[var(--hl-ink-2)]">دسته‌بندی موردنظر خود را انتخاب کنید</p>
          </div>
        </div>

        <div className="grid grid-cols-2 gap-5 sm:grid-cols-2 lg:grid-cols-4">
          {categories.map((cat) => {
            const meta = categoryMeta[cat.name] ?? defaultMeta;
            const logo = cat.icon || meta.logo;
            const desc = cat.description?.trim() || meta.desc;
            return (
              <Link
                key={cat.id}
                href={`/products?cat=${cat.id}`}
                className="hl-card group flex flex-col items-center rounded-2xl p-5 text-center transition duration-200 hover:-translate-y-1 hover:border-[#ff5a1f]/60"
              >
                <div className="mb-4 flex h-24 w-24 items-center justify-center sm:h-28 sm:w-28">
                  {logo ? (
                    <img loading="lazy" decoding="async" src={logo} alt={cat.name} className="max-h-full max-w-full object-contain transition duration-200 group-hover:scale-110" />
                  ) : (
                    <span className="text-5xl">📌</span>
                  )}
                </div>
                <h3 className="text-[16px] font-bold text-[var(--hl-ink)] transition group-hover:text-[var(--hl-red)] sm:text-[18px]">{cat.name}</h3>
                <p className="mt-1.5 line-clamp-2 text-[12px] leading-5 text-[var(--hl-muted)] sm:text-[13px] sm:leading-6">{desc}</p>
                <div className="mt-3 flex items-center gap-2">
                  <span className="text-[13px] font-medium text-[var(--hl-muted)]">{formatNumber(cat.productCount)} محصول</span>
                  <span className="grid h-6 w-6 place-items-center rounded-lg bg-[var(--hl-red)]/10 text-[var(--hl-red)] transition group-hover:bg-[var(--hl-red)] group-hover:text-white">
                    <svg viewBox="0 0 24 24" className="h-3.5 w-3.5" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M15 6l-6 6 6 6" />
                    </svg>
                  </span>
                </div>
              </Link>
            );
          })}
        </div>
      </section>

      {/* ── Promo Banners (desktop/tablet only — wide baked-in images look cramped on phones) ── */}
      <section className="mx-auto hidden max-w-[1840px] grid-cols-1 gap-6 px-4 sm:grid sm:grid-cols-3 sm:px-8 xl:px-16">
        {banners.map((b) => (
          <Link
            key={b.img}
            href={b.href}
            className="group block overflow-hidden rounded-[22px] transition duration-200 hover:-translate-y-1 hover:shadow-[0_20px_44px_-18px_rgba(20,20,20,0.28)]"
          >
            <img loading="lazy" decoding="async" src={b.img} alt={b.alt} className="aspect-[3/1] w-full scale-[1.03] object-cover transition duration-300 group-hover:scale-[1.06]" />
          </Link>
        ))}
      </section>

      {/* ── Popular Subcategories ── */}
      <section className="mx-auto max-w-[1840px] px-4 py-16 sm:px-8 xl:px-16">
        <div className="mb-8 flex items-start gap-2">
          <span className="mt-2.5 h-6 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f]" />
          <div>
            <h2 className="text-[22px] font-black text-[var(--hl-ink)] sm:text-[26px] xl:text-[30px]">زیردسته‌بندی‌های محبوب</h2>
            <p className="mt-1.5 text-[15px] text-[var(--hl-ink-2)]">محبوب‌ترین سرویس‌ها و برندها</p>
          </div>
        </div>
        <div className="flex flex-wrap gap-4">
          {popularSubs.map((s) => (
            <Link
              key={s.name}
              href="/products"
              className="hl-card group flex items-center gap-3 rounded-2xl px-5 py-3 transition hover:-translate-y-0.5 hover:border-[#ff5a1f]/60"
            >
              <img loading="lazy" decoding="async" src={s.logo} alt={s.name} className="h-10 w-10 object-contain transition group-hover:scale-110" />
              <span className="text-[15px] font-bold text-[var(--hl-ink)]">{s.name}</span>
            </Link>
          ))}
        </div>
      </section>

      {/* ── Based on Your Needs ── */}
      <section className="mx-auto max-w-[1840px] px-4 pb-16 sm:px-8 xl:px-16">
        <div className="mb-8 flex items-start gap-2">
          <span className="mt-2.5 h-6 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f]" />
          <div>
            <h2 className="text-[22px] font-black text-[var(--hl-ink)] sm:text-[26px] xl:text-[30px]">بر اساس نیاز شما</h2>
            <p className="mt-1.5 text-[15px] text-[var(--hl-ink-2)]">مناسب‌ترین خدمات بر اساس نیاز شما</p>
          </div>
        </div>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {needCards.map((n) => (
            <Link
              key={n.alt}
              href={n.href}
              className="group block overflow-hidden rounded-[14px] transition duration-200 hover:-translate-y-1 hover:shadow-xl"
            >
              <img loading="lazy" decoding="async" src={n.img} alt={n.alt} className="aspect-[41/24] w-full scale-[1.03] object-cover transition duration-300 group-hover:scale-[1.06]" />
            </Link>
          ))}
        </div>
      </section>

      {/* ── FAQ ── */}
      <CategoriesFaq />

      {/* ── Newsletter ── */}
      <HomeNewsletter />
    </>
  );
}
