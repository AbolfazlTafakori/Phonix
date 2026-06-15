import Link from "next/link";
import { products } from "@/data/home";

export const metadata = { title: "فیلم و سریال | Phoenix Verify" };

const filters = ["همه", "نتفلیکس", "سریال", "فیلم", "انیمیشن", "مستند"];

export default function FilmsPage() {
  const catalog = [...products, ...products].slice(0, 8);

  return (
    <div className="mx-auto max-w-[1320px] px-5 pb-20 pt-10">
      {/* banner */}
      <div className="relative mb-10 overflow-hidden rounded-3xl border border-white/10 bg-gradient-to-l from-[#e60053]/20 via-[#6b0a34]/10 to-transparent px-8 py-12">
        <h1 className="text-3xl font-bold text-white sm:text-4xl">فیلم و سریال</h1>
        <p className="mt-3 max-w-xl text-sm leading-7 text-white/70">
          اشتراک سرویس‌های استریم ویدئویی و دسترسی به هزاران فیلم و سریال روز دنیا، با بهترین قیمت و
          تحویل آنی.
        </p>
      </div>

      {/* filters */}
      <div className="mb-8 flex flex-wrap gap-3">
        {filters.map((f, i) => (
          <button
            key={f}
            className={`rounded-full border px-5 py-2 text-sm font-medium transition ${
              i === 0
                ? "border-transparent bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white"
                : "border-white/10 text-white/70 hover:bg-white/5 hover:text-white"
            }`}
          >
            {f}
          </button>
        ))}
      </div>

      {/* grid */}
      <div className="grid grid-cols-2 gap-5 sm:grid-cols-3 lg:grid-cols-4">
        {catalog.map((product, i) => (
          <Link
            key={i}
            href="/films/detail"
            className="group relative block overflow-hidden rounded-2xl border border-white/8 bg-[#0d0d14] transition duration-300 hover:-translate-y-1 hover:border-[#e60053]/40 hover:shadow-[0_28px_70px_-28px_rgba(230,0,83,0.55)]"
          >
            <div className="relative aspect-[3/4]">
              <img
                src={product.image}
                alt={product.name}
                className="h-full w-full object-cover transition duration-500 group-hover:scale-105"
              />
              <div className="absolute inset-0 bg-gradient-to-t from-black/85 via-black/20 to-transparent" />
              <div className="absolute inset-x-0 bottom-0 flex flex-col items-center gap-1.5 p-4">
                {product.logo ? (
                  <img src={product.logo} alt={product.name} className="max-h-9 w-auto max-w-[70%] object-contain" />
                ) : (
                  <span className="text-xl font-extrabold text-[#1db954]">Spotify</span>
                )}
                <span className="font-unna text-[11px] tracking-wide text-white/70">Phoenix Verify</span>
              </div>
            </div>
          </Link>
        ))}
      </div>
    </div>
  );
}
