import Link from "next/link";

export const metadata = { title: "نتفلیکس پریمیوم | Phoenix Verify" };

const plans = [
  { label: "۱ ماهه", price: "۲۹۰,۰۰۰ تومان", active: true },
  { label: "۳ ماهه", price: "۷۹۰,۰۰۰ تومان", active: false },
  { label: "۶ ماهه", price: "۱,۵۰۰,۰۰۰ تومان", active: false },
];

const features = ["تحویل آنی پس از پرداخت", "کیفیت 4K Ultra HD", "پشتیبانی ۲۴ ساعته", "گارانتی بازگشت وجه"];

export default function ProductDetailPage() {
  return (
    <div className="mx-auto max-w-[1320px] px-5 pb-20 pt-8">
      <nav className="mb-6 flex items-center gap-2 text-sm text-white/45">
        <Link href="/" className="hover:text-white">خانه</Link>
        <span>/</span>
        <Link href="/films" className="hover:text-white">فیلم و سریال</Link>
        <span>/</span>
        <span className="text-white/70">نتفلیکس پریمیوم</span>
      </nav>

      <div className="grid gap-8 lg:grid-cols-2">
        {/* gallery */}
        <div className="relative overflow-hidden rounded-3xl border border-white/10 bg-[#0d0d14]">
          <div className="relative aspect-[4/3]">
            <img src="/figma/prod-netflix.png" alt="نتفلیکس پریمیوم" className="h-full w-full object-cover" />
            <div className="absolute inset-0 bg-gradient-to-t from-black/60 to-transparent" />
            <img src="/figma/logo-netflix.png" alt="Netflix" className="absolute bottom-6 right-6 h-10 w-auto" />
          </div>
        </div>

        {/* info */}
        <div>
          <div className="mb-3 flex items-center gap-2">
            <span className="rounded-full bg-[#e60053]/15 px-3 py-1 text-xs font-medium text-[#e60053]">پرفروش‌ترین</span>
            <span className="rounded-full bg-emerald-500/15 px-3 py-1 text-xs font-medium text-emerald-400">موجود</span>
          </div>

          <h1 className="text-3xl font-bold text-white">اشتراک نتفلیکس پریمیوم</h1>
          <p className="mt-4 text-sm leading-8 text-white/70">
            دسترسی کامل به کتابخانه‌ی نتفلیکس با کیفیت 4K، امکان تماشا روی چند دستگاه و تحویل آنی
            اطلاعات اکانت بلافاصله پس از پرداخت.
          </p>

          <div className="mt-6 grid grid-cols-3 gap-3">
            {plans.map((p) => (
              <button
                key={p.label}
                className={`rounded-2xl border p-4 text-center transition ${
                  p.active
                    ? "border-[#3e3af2] bg-[#3e3af2]/10"
                    : "border-white/10 hover:border-white/25"
                }`}
              >
                <p className="text-sm font-bold text-white">{p.label}</p>
                <p className="mt-1 text-xs text-white/55">{p.price}</p>
              </button>
            ))}
          </div>

          <div className="mt-6 flex items-center justify-between rounded-2xl border border-white/8 bg-[#15151f]/80 p-5">
            <div>
              <p className="text-xs text-white/45">قیمت نهایی</p>
              <p className="text-2xl font-bold text-white">۲۹۰,۰۰۰ تومان</p>
            </div>
            <button className="h-12 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-10 text-base font-bold text-white shadow-[0_14px_40px_-12px_rgba(230,0,83,0.7)] transition hover:brightness-110">
              افزودن به سبد خرید
            </button>
          </div>

          <ul className="mt-6 grid grid-cols-2 gap-3">
            {features.map((f) => (
              <li key={f} className="flex items-center gap-2 text-sm text-white/75">
                <span className="text-emerald-400">✓</span>
                {f}
              </li>
            ))}
          </ul>
        </div>
      </div>

      {/* description */}
      <div className="mt-12 rounded-2xl border border-white/8 bg-[#15151f]/80 p-8">
        <h2 className="mb-4 text-xl font-bold text-white">توضیحات محصول</h2>
        <p className="text-sm leading-8 text-white/70">
          نتفلیکس بزرگ‌ترین سرویس استریم ویدئویی جهان است که با ارائه‌ی هزاران فیلم، سریال، مستند و
          انیمیشن، تجربه‌ی بی‌نظیری از تماشای محتوای روز دنیا را فراهم می‌کند. با خرید اشتراک پریمیوم،
          از کیفیت 4K Ultra HD و امکان تماشای هم‌زمان روی چند دستگاه بهره‌مند می‌شوید. تمامی اکانت‌ها
          دارای گارانتی و پشتیبانی کامل هستند.
        </p>
      </div>
    </div>
  );
}
