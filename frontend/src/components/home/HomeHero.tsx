import Link from "next/link";

const trust = [
  { title: "تحویل آنی", sub: "در کمترین زمان", icon: "/figma/trust-delivery.png" },
  { title: "پشتیبانی ۲۴/۷", sub: "پاسخگوی شما هستیم", icon: "/figma/trust-support.png" },
  { title: "پرداخت امن", sub: "درگاه‌های معتبر", icon: "/figma/trust-secure.png" },
];

const ChevronLeft = () => (
  <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
    <path d="M15 6l-6 6 6 6" />
  </svg>
);

export default function HomeHero() {
  return (
    <section className="relative overflow-hidden">
      {/* soft warm background wash */}
      <div aria-hidden className="pointer-events-none absolute inset-0 -z-10">
        <div className="absolute -right-40 top-0 h-[560px] w-[560px] rounded-full bg-[#ff5a1f]/10 blur-[130px]" />
        <div className="absolute -left-32 top-20 h-[440px] w-[440px] rounded-full bg-[#ef233c]/10 blur-[130px]" />
      </div>

      <div className="mx-auto grid max-w-[1840px] grid-cols-1 items-center gap-8 px-4 py-10 sm:px-8 sm:py-14 lg:grid-cols-[0.9fr_1.1fr] lg:gap-10 lg:py-16 lg:pl-2 lg:pr-44">
        {/* illustration (right in RTL) */}
        <div className="hero-anim-art relative order-1 flex items-center justify-center">
          <div className="relative aspect-[4/3] w-full max-w-[360px] sm:max-w-[520px] lg:max-w-[640px]">
            <div aria-hidden className="hero-aura absolute inset-[6%] rounded-full bg-[#ff5a1f]/15 blur-3xl" />
            <img
              fetchPriority="high"
              src="/figma/hero-phoenix.png"
              alt="فونیکس وریفای"
              className="hero-float absolute inset-0 h-full w-full object-contain drop-shadow-[0_24px_50px_rgba(239,35,60,0.22)]"
            />
          </div>
        </div>

        {/* text (left in RTL) */}
        <div className="hero-anim-text order-2 text-center lg:text-right">
          <h1 className="text-[28px] font-black leading-[1.3] text-[var(--hl-ink)] sm:text-[38px] lg:text-[48px] lg:leading-[1.25]">
            <span className="block lg:whitespace-nowrap">فروشگاه محصولات دیجیتال</span>
            <span className="block lg:whitespace-nowrap">و خدمات مجازی <span className="hl-grad-text">فونیکس وریفای</span></span>
          </h1>
          <p className="mx-auto mt-5 max-w-[620px] text-[15px] leading-[1.9] text-[var(--hl-ink-2)] sm:mt-6 sm:text-[17px] lg:mx-0">
            مرجع خرید امن، سریع و مطمئن انواع اکانت و اشتراک، گیفت کارت، خدمات وریفای، شماره مجازی و نرم‌افزارهای اورجینال با پشتیبانی واقعی ۲۴ ساعته.
          </p>

          <div className="mx-auto mt-7 flex max-w-[620px] flex-col items-stretch justify-center gap-3 sm:mt-8 sm:flex-row sm:items-center sm:gap-4 lg:mx-0">
            <Link
              href="/products"
              className="hl-cta flex items-center justify-center gap-2 rounded-2xl px-7 py-3.5 text-[15px] font-bold text-white"
              style={{ background: "linear-gradient(95deg, #FF7A2E 0%, #F0392C 100%)" }}
            >
              مشاهده محصولات
              <ChevronLeft />
            </Link>
            <Link
              href="#about"
              className="flex items-center justify-center gap-2 rounded-2xl border-2 border-[#FF6A33] bg-white px-7 py-3.5 text-[15px] font-bold text-[var(--hl-ink)] transition hover:bg-[#fff6f2]"
            >
              درباره ما بیشتر بدانید
              <span className="text-[#FF6A33]"><ChevronLeft /></span>
            </Link>
          </div>

          <div className="mx-auto mt-8 flex max-w-[620px] flex-wrap items-center justify-center gap-x-8 gap-y-4 sm:mt-10 sm:gap-x-12 lg:mx-0">
            {trust.map((t) => (
              <div key={t.title} className="flex items-center gap-3.5">
                <img src={t.icon} alt="" aria-hidden className="h-12 w-12 shrink-0 object-contain sm:h-14 sm:w-14" />
                <div>
                  <div className="text-[17px] font-bold text-[var(--hl-ink)]">{t.title}</div>
                  <div className="text-[14px] text-[var(--hl-muted)]">{t.sub}</div>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
