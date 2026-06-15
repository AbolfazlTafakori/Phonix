import { ArrowLeft, ArrowRight } from "./Icons";

export default function Hero() {
  return (
    <section className="mx-auto mt-8 max-w-[1320px] px-5">
      <div className="relative overflow-hidden rounded-[28px] border border-white/10 bg-gradient-to-bl from-[#2a1330] via-[#16141f] to-[#1b1b3c] px-6 py-8 sm:px-10 md:py-10">
        <div className="grid items-center gap-8 md:grid-cols-2">
          {/* illustration */}
          <div className="relative order-1 flex min-h-[260px] items-center justify-center">
            <div className="absolute h-72 w-72 rounded-full bg-[#e60053]/30 blur-[90px]" />
            <img
              src="/figma/hero-tv.png"
              alt="نتفلیکس"
              className="relative z-10 w-[78%] max-w-[420px] drop-shadow-[0_30px_60px_rgba(0,0,0,0.6)]"
            />
            <img
              src="/figma/hero-netflix-n.png"
              alt=""
              aria-hidden
              className="absolute bottom-4 left-8 z-20 w-20 drop-shadow-[0_10px_24px_rgba(230,0,83,0.6)]"
            />
          </div>

          {/* text */}
          <div className="order-2 text-right md:pl-16">
            <h1 className="mb-4 text-4xl font-bold text-white sm:text-5xl">نتفلیکس</h1>
            <p className="mb-7 text-[14px] font-medium leading-7 text-white/75">
              از ۲۰۰۷ که نتفلیکس با پیشرفت ارتباطات در دنیا تبدیل به نتفلیکس امروزی شده پیوسته در حال
              پیشرفت و بهتر کردن تجربه تماشا و امکانات خود بوده است. ساخت سریال‌های موفق بزرگی چون
              چیزهای عجیب (Stranger Things)، تاریک (Dark)، ویچر (The Witcher)، خانه کاغذی (Money
              Heist)، بازی مرکب (Squid Games) و… گوشه‌ای از فعالیت‌های خود کمپانی بوده. این را نیز
              بگوییم که فعالیت این سرویس فقط در سریال نیست و فیلم‌های معروفی نظیر تصنیف باستر اسکراگز
              (The Ballad of Buster Scruggs)، داستان ازدواج (Marriage Story)، شازده کوچولو (The
              Little Prince) و… هم در کارنامه این کمپانی به چشم می‌آید.
            </p>
            <button className="w-full rounded-full bg-gradient-to-l from-[#ef5a82] via-[#d8396a] to-[#b41f4c] px-10 py-3.5 text-lg font-bold tracking-[0.15em] text-white shadow-[0_14px_40px_-12px_rgba(230,0,83,0.7)] transition hover:brightness-110">
              مطالعه بیشتر
            </button>
          </div>
        </div>

        {/* carousel arrows */}
        <button className="absolute left-5 top-1/2 z-10 hidden h-10 w-10 -translate-y-1/2 place-items-center rounded-full border border-white/15 bg-white/5 text-white/80 transition hover:bg-white/10 md:grid">
          <ArrowLeft className="h-5 w-5" />
        </button>
        <button className="absolute right-5 top-1/2 z-10 hidden h-10 w-10 -translate-y-1/2 place-items-center rounded-full border border-white/15 bg-white/10 text-white/90 transition hover:bg-white/20 md:grid">
          <ArrowRight className="h-5 w-5" />
        </button>
      </div>
    </section>
  );
}
