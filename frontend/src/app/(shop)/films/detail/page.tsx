import Link from "next/link";
import { api } from "@/lib/api";
import { formatNumber, toFa } from "@/lib/format";
import type { Product, Comment } from "@/lib/types";
import Stars from "@/components/Stars";
import ReviewForm from "@/components/ReviewForm";
import ProductPurchase from "@/components/ProductPurchase";
import FavoriteButton from "@/components/FavoriteButton";

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
  warning: "",
  requiredLevel: 1,
  deliveryTemplate: "",
  features: [
    { text: "تحویل آنی پس از پرداخت", included: true },
    { text: "کیفیت 4K Ultra HD", included: true },
    { text: "پشتیبانی ۲۴ ساعته", included: true },
    { text: "گارانتی بازگشت وجه", included: true },
  ],
  plans: [],
};

export default async function ProductDetailPage({ searchParams }: { searchParams: Promise<{ id?: string }> }) {
  const { id } = await searchParams;

  let product = fallbackProduct;
  try {
    const products = await api.products.list();
    const wanted = id ? products.find((p) => p.id === Number(id)) : null;
    product = wanted ?? products.find((p) => p.isActive) ?? products[0] ?? fallbackProduct;
  } catch {
    // keep fallback
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
        <div className="self-start overflow-hidden rounded-3xl border border-white/10">
          <img src={product.image} alt={product.name} className="block w-full" />
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

          <ProductPurchase product={product} />

          <div className="mt-4">
            <FavoriteButton productId={product.id} />
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

      {/* mandatory reading / warning */}
      {product.warning && (
        <div className="mt-6 rounded-2xl border border-amber-500/30 bg-amber-500/[0.07] p-8">
          <h2 className="mb-4 flex items-center gap-2 text-xl font-bold text-amber-300">
            <span>⚠</span> مطالعه اجباری
          </h2>
          <p className="text-sm leading-8 text-amber-100/80">{product.warning}</p>
        </div>
      )}

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
