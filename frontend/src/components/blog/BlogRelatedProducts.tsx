import Link from "next/link";
import { productPath } from "@/lib/seo";
import { formatToman, toFa } from "@/lib/format";
import type { Product } from "@/lib/types";

type Props = {
  tag?: string;
  title: string;
  slug: string;
  allProducts: Product[];
};

export default function BlogRelatedProducts({ tag = "", title = "", slug = "", allProducts }: Props) {
  if (!allProducts || allProducts.length === 0) return null;

  const active = allProducts.filter((p) => p.isActive);
  const textToSearch = `${tag} ${title} ${slug}`.toLowerCase();

  // Find products matching relevant keywords
  const matched = active.filter((p) => {
    const pName = p.name.toLowerCase();
    const pCat = (p.categoryName || "").toLowerCase();
    const pSku = (p.sku || "").toLowerCase();

    if (textToSearch.includes("chatgpt") || textToSearch.includes("چت جی پی تی") || textToSearch.includes("gpt")) {
      return pName.includes("chatgpt") || pSku.includes("chatgpt");
    }
    if (textToSearch.includes("claude") || textToSearch.includes("کلود")) {
      return pName.includes("claude");
    }
    if (textToSearch.includes("gemini") || textToSearch.includes("جمینای") || textToSearch.includes("جیمینای")) {
      return pName.includes("gemini");
    }
    if (textToSearch.includes("هوش مصنوعی") || textToSearch.includes("ai")) {
      return pCat.includes("هوش") || pName.includes("chatgpt") || pName.includes("claude") || pName.includes("gemini") || pName.includes("kling");
    }
    if (textToSearch.includes("netflix") || textToSearch.includes("نتفلیکس") || textToSearch.includes("فیلم") || textToSearch.includes("سریال")) {
      return pName.includes("netflix") || pName.includes("نتفلیکس") || pCat.includes("فیلم") || pCat.includes("استریم");
    }
    if (textToSearch.includes("youtube") || textToSearch.includes("یوتیوب")) {
      return pName.includes("youtube") || pName.includes("یوتیوب");
    }
    if (textToSearch.includes("spotify") || textToSearch.includes("اسپاتیفای") || textToSearch.includes("موزیک") || textToSearch.includes("apple music")) {
      return pCat.includes("موسیقی") || pName.includes("spotify") || pName.includes("اسپاتیفای") || pName.includes("apple");
    }
    if (textToSearch.includes("vpn") || textToSearch.includes("فیلترشکن") || textToSearch.includes("وی پی ان") || textToSearch.includes("v2ray") || textToSearch.includes("ip اختصاصی")) {
      return pCat.includes("فیلترشکن") || pName.includes("vpn") || pName.includes("v2ray") || pName.includes("express") || pName.includes("nord");
    }
    if (textToSearch.includes("telegram") || textToSearch.includes("تلگرام")) {
      return pName.includes("telegram") || pName.includes("تلگرام");
    }
    if (textToSearch.includes("discord") || textToSearch.includes("دیسکورد")) {
      return pName.includes("discord") || pName.includes("دیسکورد");
    }
    if (textToSearch.includes("canva") || textToSearch.includes("طراحی") || textToSearch.includes("گرافیک") || textToSearch.includes("capcut") || textToSearch.includes("picsart")) {
      return pCat.includes("گرافیک") || pName.includes("canva") || pName.includes("capcut") || pName.includes("picsart");
    }
    if (textToSearch.includes("زبان") || textToSearch.includes("آموزش") || textToSearch.includes("duolingo") || textToSearch.includes("grammarly") || textToSearch.includes("elsa")) {
      return pCat.includes("آموزش") || pName.includes("duolingo") || pName.includes("grammarly") || pName.includes("elsa");
    }
    if (textToSearch.includes("gift") || textToSearch.includes("گیفت")) {
      return pCat.includes("گیفت") || pName.includes("gift") || pName.includes("گیفت");
    }
    return false;
  });

  // Pick matched products, fallback to featured or top products
  let displayProducts = matched.slice(0, 3);
  if (displayProducts.length === 0) {
    const featured = active.filter((p) => p.featured);
    displayProducts = (featured.length >= 3 ? featured : active).slice(0, 3);
  }

  if (displayProducts.length === 0) return null;

  return (
    <section className="my-10 rounded-2xl border border-[var(--hl-border)] bg-gradient-to-b from-[var(--hl-surface)] to-[var(--hl-tint)] p-5 sm:p-6">
      <div className="mb-4 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <span className="h-4 w-1 rounded-full bg-[var(--hl-red)]" />
          <h3 className="text-base font-black text-[var(--hl-ink)] sm:text-lg">
            محصولات و اشتراک‌های مرتبط
          </h3>
        </div>
        <Link
          href="/products"
          className="text-xs font-bold text-[var(--hl-red-text)] transition hover:underline sm:text-sm"
        >
          مشاهده همه محصولات ←
        </Link>
      </div>

      <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2 lg:grid-cols-3">
        {displayProducts.map((p) => {
          const activePlans = (p.plans ?? []).filter((pl) => pl.isActive);
          const prices = activePlans.map((pl) => pl.finalPrice).filter((n) => n > 0);
          const minPrice = prices.length > 0 ? Math.min(...prices) : p.finalPrice;

          return (
            <Link
              key={p.id}
              href={productPath(p)}
              className="group flex flex-col justify-between rounded-xl border border-[var(--hl-border)] bg-[var(--hl-surface)] p-3.5 transition duration-200 hover:-translate-y-0.5 hover:border-[var(--hl-red)]/50 hover:shadow-md"
            >
              <div className="flex items-center gap-3">
                {p.image ? (
                  <img
                    src={p.image}
                    alt={p.name}
                    className="h-12 w-12 shrink-0 rounded-lg object-cover"
                    loading="lazy"
                    decoding="async"
                  />
                ) : (
                  <div className="grid h-12 w-12 shrink-0 place-items-center rounded-lg bg-[var(--hl-border)] text-xl">
                    ⚡
                  </div>
                )}
                <div className="min-w-0 flex-1">
                  <h4 className="truncate text-sm font-bold text-[var(--hl-ink)] transition group-hover:text-[var(--hl-red-text)]">
                    {p.name}
                  </h4>
                  <span className="mt-0.5 inline-block text-[11px] text-[var(--hl-muted)]">
                    تحویل آنی و ضمانت بازگشت
                  </span>
                </div>
              </div>

              <div className="mt-3 flex items-center justify-between border-t border-[var(--hl-border)]/60 pt-2.5">
                <div className="text-right">
                  <span className="block text-[10px] text-[var(--hl-muted)]">قیمت از</span>
                  <span className="text-xs font-black text-[var(--hl-ink)] sm:text-sm">
                    {minPrice > 0 ? `${formatToman(minPrice)} تومان` : "استعلام قیمت"}
                  </span>
                </div>
                <span className="rounded-lg bg-[var(--hl-red)] px-3 py-1.5 text-xs font-bold text-white transition group-hover:brightness-110">
                  خرید آنلاین
                </span>
              </div>
            </Link>
          );
        })}
      </div>
    </section>
  );
}
