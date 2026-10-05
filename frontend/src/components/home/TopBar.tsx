import Link from "next/link";
import type { PromoBar } from "@/lib/types";

// Thin full-width promo bar pinned above the header. Its wording comes from the panel (هدر و منو); switched
// off or left without text, it renders nothing rather than an empty coloured strip.
export default function TopBar({ promo }: { promo: PromoBar }) {
  const text = promo.text?.trim();
  if (!promo.enabled || !text) return null;
  const label = promo.buttonLabel?.trim();
  const link = promo.buttonLink?.trim();
  const external = !!link && /^https?:\/\//i.test(link);

  return (
    <div className="hl-grad text-white">
      {/* Grows to two lines on a phone so the offer stays readable; a single truncated line on ≥sm. */}
      <div className="mx-auto flex min-h-[44px] max-w-[1840px] items-center justify-center gap-4 px-4 py-1.5 text-[12px] font-bold leading-snug sm:h-[48px] sm:py-0 sm:px-8 sm:text-[15px] xl:px-16">
        <p className="flex min-w-0 items-center gap-2 text-center">
          {promo.emoji?.trim() && <span aria-hidden className="shrink-0 text-[15px] sm:text-[17px]">{promo.emoji.trim()}</span>}
          <span className="line-clamp-2 sm:line-clamp-none sm:truncate">{text}</span>
        </p>
        {label && link && (
          <Link
            href={link}
            {...(external ? { target: "_blank", rel: "noopener noreferrer" } : {})}
            className="hidden shrink-0 rounded-full bg-white/20 px-3.5 py-1 text-[14px] text-white transition hover:bg-white/30 sm:inline-block"
          >
            {label}
          </Link>
        )}
      </div>
    </div>
  );
}
