import Link from "next/link";
import { api } from "@/lib/api";
import { formatNumber } from "@/lib/format";
import type { Category } from "@/lib/types";
import HomeNewsletter from "@/components/home/HomeNewsletter";
import HomeFaq from "@/components/home/HomeFaq";

export const dynamic = "force-dynamic";
export const metadata = { title: "دسته‌بندی محصولات | Phoenix Verify" };

const stats = [
  { value: "۱۰,۰۰۰+", label: "سفارش موفق", icon: "📦" },
  { value: "۵,۰۰۰+", label: "مشتری فعال", icon: "👥" },
  { value: "۹۹٪", label: "رضایت مشتریان", icon: "⭐" },
  { value: "آنی", label: "تحویل فوری", icon: "⚡" },
];

const heroFeatures = [
  { text: "تحویل سریع و آنی", icon: "🚀" },
  { text: "پشتیبانی ۲۴ ساعته", icon: "🎧" },
  { text: "قیمت رقابتی", icon: "💰" },
  { text: "تضمین کیفیت", icon: "✅" },
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
      <section className="relative overflow-hidden bg-gradient-to-l from-[#1a0a1e] via-[#2d0a2e] to-[#0f0c29]">
        <div className="absolute inset-0 opacity-20" style={{ background: "radial-gradient(ellipse at 30% 50%, #ef233c 0%, transparent 70%)" }} />
        <div className="relative mx-auto flex max-w-[1840px] flex-col gap-8 px-4 py-16 sm:px-8 lg:flex-row lg:items-center lg:justify-between lg:py-24 xl:px-16">
          <div className="max-w-2xl text-center lg:text-right">
            <h1 className="text-3xl font-black leading-[1.5] text-white sm:text-4xl xl:text-5xl">
              دسته‌بندی خدمات و محصولات
              <br />
              <span className="text-[#ff5a1f]">فونیکس وریفای</span>
            </h1>
            <p className="mt-4 text-[15px] leading-8 text-white/70 sm:text-[17px]">
              تمامی خدمات و محصولات دیجیتال را در یک نگاه مشاهده کنید. از اشتراک سرویس‌های استریم تا شماره مجازی و گیفت کارت.
            </p>
          </div>
          <div className="mx-auto w-full max-w-sm rounded-2xl border border-white/10 bg-white/5 p-6 backdrop-blur-md lg:mx-0">
            <h3 className="mb-4 text-center text-[17px] font-bold text-white lg:text-right">چرا فونیکس وریفای؟</h3>
            <div className="grid grid-cols-2 gap-3">
              {heroFeatures.map((f) => (
                <div key={f.text} className="flex items-center gap-2 rounded-xl bg-white/10 px-3 py-3 text-[13px] font-medium text-white/90">
                  <span className="text-lg">{f.icon}</span>
                  <span>{f.text}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </section>

      {/* ── Stats ── */}
      <section className="mx-auto -mt-8 max-w-[1840px] px-4 sm:px-8 xl:px-16">
        <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
          {stats.map((s) => (
            <div key={s.label} className="hl-card flex items-center gap-3 rounded-2xl p-5">
              <span className="grid h-12 w-12 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-[#ef233c]/10 to-[#ff5a1f]/10 text-2xl">{s.icon}</span>
              <div>
                <p className="text-[20px] font-black text-[var(--hl-ink)]">{s.value}</p>
                <p className="text-[13px] text-[var(--hl-muted)]">{s.label}</p>
              </div>
            </div>
          ))}
        </div>
      </section>

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
            return (
              <Link
                key={cat.id}
                href={`/products?cat=${cat.id}`}
                className="hl-card group flex flex-col items-center rounded-2xl p-5 text-center transition duration-200 hover:-translate-y-1 hover:border-[#ff5a1f]/60"
              >
                <div className="mb-4 flex h-24 w-24 items-center justify-center sm:h-28 sm:w-28">
                  {logo ? (
                    <img src={logo} alt={cat.name} className="max-h-full max-w-full object-contain transition duration-200 group-hover:scale-110" />
                  ) : (
                    <span className="text-5xl">📌</span>
                  )}
                </div>
                <h3 className="text-[16px] font-bold text-[var(--hl-ink)] transition group-hover:text-[var(--hl-red)] sm:text-[18px]">{cat.name}</h3>
                <p className="mt-1.5 line-clamp-2 text-[12px] leading-5 text-[var(--hl-muted)] sm:text-[13px] sm:leading-6">{meta.desc}</p>
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

      {/* ── Promo Banners ── */}
      <section className="mx-auto grid max-w-[1840px] grid-cols-1 gap-6 px-4 sm:grid-cols-3 sm:px-8 xl:px-16">
        {banners.map((b) => (
          <Link
            key={b.img}
            href={b.href}
            className="group block overflow-hidden rounded-[22px] transition duration-200 hover:-translate-y-1 hover:shadow-[0_20px_44px_-18px_rgba(20,20,20,0.28)]"
          >
            <img src={b.img} alt={b.alt} className="aspect-[3/1] w-full scale-[1.03] object-cover transition duration-300 group-hover:scale-[1.06]" />
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
              <img src={s.logo} alt={s.name} className="h-10 w-10 object-contain transition group-hover:scale-110" />
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
              <img src={n.img} alt={n.alt} className="aspect-[41/24] w-full scale-[1.03] object-cover transition duration-300 group-hover:scale-[1.06]" />
            </Link>
          ))}
        </div>
      </section>

      {/* ── FAQ ── */}
      <HomeFaq />

      {/* ── Newsletter ── */}
      <HomeNewsletter />
    </>
  );
}
