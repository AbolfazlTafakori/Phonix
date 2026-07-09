"use client";

import { useState } from "react";
import ProductCardImage from "@/components/ProductCardImage";

export default function ProductGallery({
  image,
  gallery,
  name,
  featured,
  out,
}: {
  image: string;
  gallery: string[];
  name: string;
  featured?: boolean;
  out?: boolean;
}) {
  const allImages = [image, ...gallery.filter(Boolean)];
  const [active, setActive] = useState(0);

  return (
    <div>
      <div className="relative overflow-hidden rounded-[22px] border bg-white" style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}>
        <ProductCardImage src={allImages[active]} alt={name} className="aspect-[4/5] w-full object-cover" />
        {featured && (
          <span className="absolute right-4 top-4 rounded-full px-3 py-1.5 text-[11px] font-black" style={{ background: "var(--ac-stat-icon-orange-bg)", color: "#F2551F" }}>
            محبوب‌ترین
          </span>
        )}
        {out && (
          <div className="absolute inset-0 grid place-items-center bg-black/40 backdrop-blur-[2px]">
            <span className="-rotate-6 rounded-2xl border border-white/25 bg-black/55 px-7 py-3 text-xl font-black tracking-wide text-white shadow-2xl">
              ناموجود
            </span>
          </div>
        )}
      </div>

      {allImages.length > 1 && (
        <div className="mt-3 flex items-center gap-2">
          {allImages.slice(0, 4).map((src, i) => (
            <button
              key={i}
              type="button"
              onClick={() => setActive(i)}
              className={`relative h-16 w-20 shrink-0 overflow-hidden rounded-xl border-2 transition ${active === i ? "border-[var(--hl-red)]" : "border-[var(--hl-border)] hover:border-[var(--hl-red)]/40"}`}
            >
              <ProductCardImage src={src} alt="" className="h-full w-full object-cover" />
            </button>
          ))}
          {allImages.length > 4 && (
            <span className="flex h-16 w-20 shrink-0 items-center justify-center rounded-xl border-2 border-[var(--hl-border)] text-[14px] font-black" style={{ color: "var(--ac-muted)" }}>
              +{allImages.length - 4}
            </span>
          )}
        </div>
      )}
    </div>
  );
}
