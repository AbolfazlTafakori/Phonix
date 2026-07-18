"use client";

import { useEffect, useState } from "react";

const DURATION_MS = 1400;

// Splits "+10,000" into a prefix ("+"), the numeric target (10000), and a suffix (""). A value with no
// digits at all (e.g. a "۲۴/۷ support" stat) has no numeric target and is rendered as static text unchanged.
function parseStat(raw: string): { prefix: string; target: number; suffix: string } | null {
  const m = raw.match(/^(\D*)([\d,]+)(\D*)$/);
  if (!m) return null;
  const target = Number(m[2].replace(/,/g, ""));
  if (!Number.isFinite(target)) return null;
  return { prefix: m[1], target, suffix: m[3] };
}

// Counts a stat value up from 0 once `animate` turns true. Always starts as the real, final value — that's
// what the server renders, so a crawler or a no-JS browser sees the true stat immediately; only after JS
// confirms the trigger condition does the effect below dip it to 0 and count back up. Shared by every stat
// strip on the site (home trust bar, products-page hero) so the motion reads identically everywhere.
export default function StatCountUp({ value, animate }: { value: string; animate: boolean }) {
  const parsed = parseStat(value);
  const [display, setDisplay] = useState(value);

  useEffect(() => {
    if (!parsed || !animate) return;
    let frame: number;
    const start = performance.now();
    setDisplay(`${parsed.prefix}0${parsed.suffix}`);
    const tick = (now: number) => {
      const progress = Math.min(1, (now - start) / DURATION_MS);
      const eased = 1 - Math.pow(1 - progress, 3); // ease-out cubic
      const current = Math.round(parsed.target * eased);
      setDisplay(`${parsed.prefix}${current.toLocaleString("en-US")}${parsed.suffix}`);
      if (progress < 1) frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [animate]);

  return <span dir="ltr" className="[unicode-bidi:isolate]">{display}</span>;
}
