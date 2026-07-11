import Link from "next/link";
import { productPath } from "@/lib/seo";
import { api } from "@/lib/api";
import { formatToman } from "@/lib/format";
import type { Product } from "@/lib/types";
import BestSellersCarousel, { type CarouselCard } from "./BestSellersCarousel";

// Featured products for the home showcase. Falls back to a curated set when the
// backend is unreachable so the section never ships empty.
async function getFeatured(): Promise<Product[]> {
  try {
    const all = (await api.products.list()).filter((p) => p.isActive);
    const featured = all.filter((p) => p.featured);
    const picked = [...featured, ...all.filter((p) => !p.featured)].slice(0, 6);
    if (picked.length) return picked;
  } catch {
    // fall through to the static set
  }
  return fallback;
}

const fallback = [
  { id: -1, name: "اکانت نتفلیکس", categoryName: "اشتراک ۱ ماهه", finalPrice: 190000, image: "/figma/prod-netflix.png", badge: "پرفروش" },
  { id: -2, name: "اسپاتیفای پریمیوم", categoryName: "اشتراک شخصی ۱ ماهه", finalPrice: 240000, image: "/figma/prod-spotify.png", badge: "تحویل فوری" },
  { id: -3, name: "Apple Music", categoryName: "اشتراک ۱ ماهه", finalPrice: 190000, image: "/figma/prod-applemusic.png", badge: "پرفروش" },
  { id: -4, name: "NordVPN", categoryName: "اشتراک ۱ ماهه", finalPrice: 230000, image: "/figma/prod-canva.png", badge: "تحویل فوری" },
  { id: -5, name: "گیفت کارت گوگل پلی", categoryName: "کارت ۱۰ دلاری", finalPrice: 620000, image: "/figma/prod-binance.png", badge: "پرفروش" },
  { id: -6, name: "اکانت PS Plus", categoryName: "اشتراک ۱ ماهه", finalPrice: 250000, image: "/figma/prod-bybit.png", badge: "تحویل فوری" },
] as unknown as (Product & { badge: string })[];

export default async function HomeBestSellers() {
  const products = await getFeatured();
  const cards: CarouselCard[] = products.map((p) => {
    const badge = (p as Product & { badge?: string }).badge ?? (p.featured ? "پرفروش" : "تحویل فوری");
    return {
      key: String(p.id),
      name: p.name,
      categoryName: p.categoryName,
      priceLabel: formatToman(p.finalPrice),
      badge,
      image: p.image,
      href: p.id > 0 ? productPath(p) : "/products",
    };
  });

  return (
    <section className="mx-auto max-w-[1840px] px-4 sm:px-8 xl:px-16 py-4">
      <div className="mb-8 flex items-end justify-between">
        <div className="flex items-start gap-2">
          <span className="mt-2.5 h-6 w-1.5 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f]" />
          <div>
            <h2 className="text-[22px] sm:text-[26px] xl:text-[30px] font-black text-[var(--hl-ink)]">پرفروش‌ترین محصولات</h2>
            <p className="mt-1.5 text-[15px] text-[var(--hl-ink-2)]">محبوب‌ترین انتخاب‌های کاربران فونیکس وریفای</p>
          </div>
        </div>
        <Link
          href="/products"
          className="shrink-0 rounded-xl border border-[var(--hl-border)] bg-white px-4 py-2 text-[16px] font-bold text-[var(--hl-red)] transition hover:bg-[#fff6f2]"
        >
          مشاهده همه
        </Link>
      </div>

      <BestSellersCarousel products={cards} />
    </section>
  );
}
