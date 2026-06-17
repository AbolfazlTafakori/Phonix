import Link from "next/link";
import { api } from "@/lib/api";
import { formatToman, formatNumber, toFa } from "@/lib/format";
import type { Product, Plan, Comment } from "@/lib/types";
import Stars from "@/components/Stars";
import ReviewForm from "@/components/ReviewForm";

export const dynamic = "force-dynamic";
export const metadata = { title: "جزئیات محصول | Phoenix Verify" };

const fallbackProduct: Product = {
  id: 0,
  name: "اشتراک نتفلیکس پریمیوم",
  categoryId: 0,
  categoryName: "فیلم و سریال",
  price: 290000,
  discountPercent: 0,
  finalPrice: 290000,
  stock: 1,
  isActive: true,
  featured: true,
  image: "/figma/prod-netflix.png",
  sku: "",
  description:
    "دسترسی کامل به کتابخانه‌ی نتفلیکس با کیفیت 4K، امکان تماشا روی چند دستگاه و تحویل آنی اطلاعات اکانت بلافاصله پس از پرداخت.",
  features: [
    { text: "تحویل آنی پس از پرداخت", included: true },
    { text: "کیفیت 4K Ultra HD", included: true },
    { text: "پشتیبانی ۲۴ ساعته", included: true },
    { text: "گارانتی بازگشت وجه", included: true },
  ],
};

export default async function ProductDetailPage({ searchParams }: { searchParams: Promise<{ id?: string }> }) {
  const { id } = await searchParams;

  let product = fallbackProduct;
  let plans: Plan[] = [];
  try {
    const products = await api.products.list();
    const wanted = id ? products.find((p) => p.id === Number(id)) : null;
    product = wanted ?? products.find((p) => p.isActive) ?? products[0] ?? fallbackProduct;
  } catch {
    // keep fallback
  }
  try {
    plans = await api.pricing.getPlans();
  } catch {
    // optional
  }

  let comments: Comment[] = [];
  try {
    comments = await api.comments.forProduct(product.id);
  } catch {
    // optional
  }
  const topLevel = comments.filter((c) => c.parentId == null);
  const rated = topLevel.filter((c) => c.rating > 0);
  const avg = rated.length ? rated.reduce((s, c) => s + c.rating, 0) / rated.length : 0;

  return (
    <div className="mx-auto max-w-[1320px] px-5 pb-20 pt-8">
      <nav className="mb-6 flex items-center gap-2 text-sm text-white/45">
        <Link href="/" className="hover:text-white">خانه</Link>
        <span>/</span>
        <Link href="/films" className="hover:text-white">{product.categoryName || "فروشگاه"}</Link>
        <span>/</span>
        <span className="text-white/70">{product.name}</span>
      </nav>

      <div className="grid gap-8 lg:grid-cols-2">
        {/* gallery */}
        <div className="relative overflow-hidden rounded-3xl border border-white/10 bg-[#0d0d14]">
          <div className="relative aspect-[4/3]">
            <img src={product.image} alt={product.name} className="h-full w-full object-cover" />
            <div className="absolute inset-0 bg-gradient-to-t from-black/60 to-transparent" />
          </div>
        </div>

        {/* info */}
        <div>
          <div className="mb-3 flex items-center gap-2">
            {product.featured && <span className="rounded-full bg-[#e60053]/15 px-3 py-1 text-xs font-medium text-[#e60053]">پرفروش‌ترین</span>}
            <span className={`rounded-full px-3 py-1 text-xs font-medium ${product.stock > 0 ? "bg-emerald-500/15 text-emerald-400" : "bg-rose-500/15 text-rose-400"}`}>
              {product.stock > 0 ? "موجود" : "ناموجود"}
            </span>
          </div>

          <h1 className="text-3xl font-bold text-white">{product.name}</h1>

          {rated.length > 0 && (
            <div className="mt-3 flex items-center gap-2">
              <Stars value={avg} />
              <span className="text-sm text-white/60">{toFa(avg.toFixed(1))} از ۵ · {formatNumber(rated.length)} نظر</span>
            </div>
          )}

          <p className="mt-4 text-sm leading-8 text-white/70">{product.description}</p>

          {plans.length > 0 && (
            <div className="mt-6 grid grid-cols-3 gap-3">
              {plans.slice(0, 3).map((p, i) => (
                <div key={p.id} className={`rounded-2xl border p-4 text-center transition ${i === 0 ? "border-[#3e3af2] bg-[#3e3af2]/10" : "border-white/10"}`}>
                  <p className="text-sm font-bold text-white">{p.label}</p>
                  <p className="mt-1 text-xs text-white/55">{formatToman(p.finalPrice)}</p>
                </div>
              ))}
            </div>
          )}

          <div className="mt-6 flex items-center justify-between rounded-2xl border border-white/8 bg-[#15151f]/80 p-5">
            <div>
              <p className="text-xs text-white/45">قیمت نهایی</p>
              <p className="text-2xl font-bold text-white">{formatToman(product.finalPrice)}</p>
            </div>
            <button className="h-12 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-10 text-base font-bold text-white shadow-[0_14px_40px_-12px_rgba(230,0,83,0.7)] transition hover:brightness-110">
              افزودن به سبد خرید
            </button>
          </div>

          {product.features.length > 0 && (
            <ul className="mt-6 grid grid-cols-2 gap-3">
              {product.features.map((f) => (
                <li key={f.text} className={`flex items-center gap-2 text-sm ${f.included ? "text-white/75" : "text-white/35 line-through"}`}>
                  <span className={f.included ? "text-emerald-400" : "text-rose-400/70"}>{f.included ? "✓" : "✕"}</span>
                  {f.text}
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>

      {/* description */}
      <div className="mt-12 rounded-2xl border border-white/8 bg-[#15151f]/80 p-8">
        <h2 className="mb-4 text-xl font-bold text-white">توضیحات محصول</h2>
        <p className="text-sm leading-8 text-white/70">{product.description}</p>
      </div>

      {/* reviews */}
      <section className="mt-12">
        <h2 className="mb-6 text-xl font-bold text-white">نظرات کاربران</h2>
        <div className="grid gap-6 lg:grid-cols-[1fr_380px]">
          <div className="space-y-4">
            {topLevel.length === 0 ? (
              <p className="rounded-2xl border border-white/8 bg-[#15151f]/60 p-6 text-sm text-white/45">
                هنوز نظری ثبت نشده است. اولین نفری باشید که نظر می‌دهد!
              </p>
            ) : (
              topLevel.map((c) => (
                <div key={c.id} className="rounded-2xl border border-white/8 bg-[#15151f]/80 p-5">
                  <div className="flex items-center justify-between gap-3">
                    <div className="flex items-center gap-3">
                      <span className="grid h-9 w-9 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-sm font-bold text-white">
                        {c.userName.charAt(0)}
                      </span>
                      <div>
                        <p className="text-sm font-bold text-white">{c.userName}</p>
                        <p className="text-xs text-white/40">{c.date}</p>
                      </div>
                    </div>
                    {c.rating > 0 && <Stars value={c.rating} />}
                  </div>
                  <p className="mt-3 text-sm leading-7 text-white/75">{c.body}</p>

                  {comments
                    .filter((r) => r.parentId === c.id)
                    .map((r) => (
                      <div key={r.id} className="mt-3 rounded-xl border-r-2 border-[#e60053]/40 bg-white/[0.03] p-4">
                        <p className="text-xs font-bold text-[#ff5a8a]">{r.userName}</p>
                        <p className="mt-1.5 text-sm leading-7 text-white/70">{r.body}</p>
                      </div>
                    ))}
                </div>
              ))
            )}
          </div>

          <ReviewForm productId={product.id} />
        </div>
      </section>
    </div>
  );
}
