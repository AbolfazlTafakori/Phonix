"use client";

import { useEffect, useState } from "react";
import StatCountUp from "@/components/StatCountUp";

type Stat = { value: string; label: string; icon: string };

// Above-the-fold on this page (unlike the home trust bar), so it counts up on mount rather than waiting for
// a scroll trigger — same motion language as TrustStats, just fired the moment the page is ready.
export default function ProductsHeroStats({ stats }: { stats: Stat[] }) {
  const [animate, setAnimate] = useState(false);
  useEffect(() => setAnimate(true), []);

  return (
    <div className="mt-7 flex flex-nowrap items-center justify-center gap-x-4 sm:gap-x-6 lg:justify-start">
      {stats.map((s) => (
        <div key={s.label} className="flex shrink-0 items-center gap-2">
          <img loading="lazy" decoding="async" src={s.icon} alt="" aria-hidden className="h-9 w-9 shrink-0 object-contain sm:h-12 sm:w-12" />
          <div className="text-right">
            <div className="whitespace-nowrap text-[17px] font-black leading-none text-[var(--hl-ink)] sm:text-[25px]">
              <StatCountUp value={s.value} animate={animate} />
            </div>
            <div className="mt-1 whitespace-nowrap text-[12px] font-bold text-[var(--hl-muted)] sm:text-[15px]">{s.label}</div>
          </div>
        </div>
      ))}
    </div>
  );
}
