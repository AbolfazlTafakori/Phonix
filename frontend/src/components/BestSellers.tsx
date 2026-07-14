import { getShowcase, getSiteContent } from "@/lib/content";
import SectionHeading from "./SectionHeading";

export default async function BestSellers() {
  const [items, content] = await Promise.all([getShowcase(), getSiteContent()]);

  return (
    <section className="mx-auto mt-16 max-w-[1320px] px-5 sm:mt-24">
      <SectionHeading title={content.sections.bestSellersTitle} />

      <div className="mt-12 grid grid-cols-2 gap-5 sm:grid-cols-3 lg:grid-cols-4">
        {items.map((product) => (
          <a
            key={product.id}
            href={product.href}
            className="group relative block overflow-hidden rounded-2xl border border-white/8 bg-[#0d0d14] transition duration-300 hover:-translate-y-1 hover:border-[#e60053]/40 hover:shadow-[0_28px_70px_-28px_rgba(230,0,83,0.55)]"
          >
            <div className="relative aspect-[3/4]">
              <img loading="lazy" decoding="async"
                src={product.image}
                alt={product.name}
                className="h-full w-full object-cover transition duration-500 group-hover:scale-105"
              />
              <div className="absolute inset-0 bg-gradient-to-t from-black/85 via-black/20 to-transparent" />

              <div className="absolute inset-x-0 bottom-0 flex flex-col items-center gap-1.5 p-4">
                {product.logo ? (
                  <img loading="lazy" decoding="async" src={product.logo} alt={product.name} className="max-h-10 w-auto max-w-[70%] object-contain" />
                ) : (
                  <span className="text-2xl font-extrabold tracking-tight text-[#1db954]">{product.name}</span>
                )}
                <span className="font-unna text-[11px] tracking-wide text-white/70">Phoenix Verify</span>
              </div>
            </div>
          </a>
        ))}
      </div>
    </section>
  );
}
