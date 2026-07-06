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

      <div className="mx-auto grid max-w-[1840px] grid-cols-[0.9fr_1.1fr] items-center gap-10 py-16 pl-2 pr-44">
        {/* illustration (right in RTL) */}
        <div className="hero-anim-art relative order-1 flex items-center justify-center">
          <div className="relative aspect-[4/3] w-full max-w-[640px]">
            <div aria-hidden className="hero-aura absolute inset-[6%] rounded-full bg-[#ff5a1f]/15 blur-3xl" />
            <img
              src="/figma/hero-phoenix.png"
              alt="فونیکس وریفای"
              className="hero-float absolute inset-0 h-full w-full object-contain drop-shadow-[0_24px_50px_rgba(239,35,60,0.22)]"
            />
          </div>
        </div>

        {/* text (left in RTL) */}
        <div className="hero-anim-text order-2 text-right">
          <h1 className="text-[48px] font-black leading-[1.25] text-[var(--hl-ink)]">
            <span className="block whitespace-nowrap">فروشگاه محصولات دیجیتال</span>
            <span className="block whitespace-nowrap">و خدمات مجازی <span className="hl-grad-text">فونیکس وریفای</span></span>
          </h1>
          <p className="mt-6 mr-0 max-w-[620px] text-[17px] leading-[1.9] text-[var(--hl-ink-2)]">
            مرجع خرید امن، سریع و مطمئن انواع اکانت و اشتراک، گیفت کارت، خدمات وریفای، شماره مجازی و نرم‌افزارهای اورجینال با پشتیبانی واقعی ۲۴ ساعته.
          </p>

          <div className="mt-8 flex max-w-[620px] items-center justify-center gap-4">
            <Link
              href="/products"
              className="hl-cta flex items-center gap-2 rounded-2xl px-7 py-3.5 text-[15px] font-bold text-white"
              style={{ background: "linear-gradient(95deg, #FF7A2E 0%, #F0392C 100%)" }}
            >
              مشاهده محصولات
              <ChevronLeft />
            </Link>
            <Link
              href="#about"
              className="flex items-center gap-2 rounded-2xl border-2 border-[#FF6A33] bg-white px-7 py-3.5 text-[15px] font-bold text-[var(--hl-ink)] transition hover:bg-[#fff6f2]"
            >
              درباره ما بیشتر بدانید
              <span className="text-[#FF6A33]"><ChevronLeft /></span>
            </Link>
          </div>

          <div className="mt-10 flex max-w-[620px] items-center justify-center gap-12">
            {trust.map((t) => (
              <div key={t.title} className="flex items-center gap-3.5">
                <img src={t.icon} alt="" aria-hidden className="h-14 w-14 shrink-0 object-contain" />
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
