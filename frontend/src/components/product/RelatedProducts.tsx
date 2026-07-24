import Link from "next/link";

export type RelatedCard = {
  key: string;
  name: string;
  categoryName: string;
  priceLabel: string;
  image: string;
  href: string;
};

/**
 * A quiet, scannable "similar products" strip for the product page — compact cards in a horizontal
 * snap-scroll row that reads the same on every screen size. Deliberately lighter than the home best-seller
 * carousel: small image, one-to-two-line name, a single price, no oversized call-to-action.
 */
export default function RelatedProducts({ products }: { products: RelatedCard[] }) {
  return (
    <div className="flex snap-x snap-mandatory gap-3 overflow-x-auto pb-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
      {products.map((p) => (
        <Link
          key={p.key}
          href={p.href}
          className="group flex w-[150px] shrink-0 snap-start flex-col overflow-hidden rounded-[16px] border bg-[var(--ac-panel-bg)] transition hover:-translate-y-0.5 hover:shadow-[0_16px_36px_-20px_rgba(20,20,20,0.4)] sm:w-[172px]"
          style={{ borderColor: "var(--ac-panel-border)" }}
        >
          <div className="aspect-square bg-[#f7f8fa] p-4">
            <img loading="lazy" decoding="async" src={p.image} alt={p.name} className="h-full w-full object-contain transition duration-300 group-hover:scale-105" />
          </div>
          <div className="flex flex-1 flex-col p-3">
            <p className="truncate text-[11px]" style={{ color: "var(--ac-muted)" }}>{p.categoryName}</p>
            <p className="mt-1 line-clamp-2 min-h-[34px] text-[13px] font-bold leading-[17px]" style={{ color: "var(--ac-title)" }}>{p.name}</p>
            <p className="mt-auto pt-2 text-[14px] font-black" style={{ color: "var(--ac-title)" }}>{p.priceLabel}</p>
          </div>
        </Link>
      ))}
    </div>
  );
}
