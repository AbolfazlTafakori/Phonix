"use client";

import { useState } from "react";
import Img from "@/components/ui/Img";

// Product image with a graceful fallback: if the src is empty or fails to load, show a clean branded
// placeholder instead of the broken-image icon, so a missing banner never looks broken on the storefront.
export default function ProductCardImage({ src, alt, className }: { src: string; alt: string; className?: string }) {
  const [failed, setFailed] = useState(false);

  if (!src || failed) {
    return (
      <div className={`grid place-items-center bg-gradient-to-br from-[#1b1b2a] to-[#0d0d14] ${className ?? ""}`}>
        <svg viewBox="0 0 24 24" className="h-10 w-10 text-white/25" fill="none" stroke="currentColor" strokeWidth="1.5">
          <rect x="3" y="3" width="18" height="18" rx="2" />
          <circle cx="8.5" cy="8.5" r="1.5" />
          <path d="m21 15-5-5L5 21" />
        </svg>
      </div>
    );
  }

  return (
    <Img
      src={src}
      alt={alt}
      sizes="(max-width: 640px) 100vw, (max-width: 1024px) 50vw, 33vw"
      className={className}
      onError={() => setFailed(true)}
    />
  );
}
