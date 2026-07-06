"use client";

import Link from "next/link";
import { useEffect, useLayoutEffect, useRef, useState } from "react";

const DURATION = 520;
// Ease-out curve: quick start, gentle settle — reads much smoother than plain "ease".
const EASE = "cubic-bezier(0.22, 1, 0.36, 1)";

export type CarouselCard = {
  key: string;
  name: string;
  categoryName: string;
  priceLabel: string;
  badge: string;
  image: string;
  href: string;
};

function Stars() {
  return (
    <div className="flex items-center gap-0.5 text-[#ffb020]">
      {Array.from({ length: 5 }).map((_, i) => (
        <svg key={i} viewBox="0 0 24 24" className="h-3.5 w-3.5" fill="currentColor">
          <path d="M12 2l2.9 6 6.6.9-4.8 4.6 1.1 6.5L12 17.8 6.2 20l1.1-6.5L2.5 8.9 9 8z" />
        </svg>
      ))}
    </div>
  );
}

function Card({ p }: { p: CarouselCard }) {
  return (
    <div
      data-card
      className="hl-card group flex w-[calc((100%-16px)/2)] shrink-0 flex-col overflow-hidden rounded-[18px] transition duration-200 hover:-translate-y-1 hover:shadow-[0_20px_44px_-18px_rgba(239,35,60,0.26)] sm:w-[calc((100%-32px)/3)] lg:w-[calc((100%-48px)/4)] xl:w-[calc((100%-76px)/5)]"
    >
      <Link href={p.href} className="relative block aspect-square bg-[#f7f8fa] p-5">
        <span
          className="absolute right-2.5 top-2.5 z-10 rounded-lg px-2.5 py-1.5 text-[12px] font-bold text-white shadow-[0_4px_12px_-4px_rgba(239,35,60,0.6)]"
          style={{ background: "linear-gradient(95deg, #FF7A2E 0%, #F0392C 100%)" }}
        >
          {p.badge}
        </span>
        <img src={p.image} alt={p.name} className="h-full w-full object-contain transition duration-300 group-hover:scale-105" />
      </Link>

      <div className="flex flex-1 flex-col p-4">
        <div className="flex items-center justify-between gap-2">
          <p className="truncate text-[15px] text-[var(--hl-muted)]">{p.categoryName}</p>
          <Stars />
        </div>
        <Link href={p.href} className="mt-1 line-clamp-1 text-[20px] font-bold text-[var(--hl-ink)] transition hover:text-[var(--hl-red)]">
          {p.name}
        </Link>
        <div className="mt-auto pt-3 text-center text-[18px] font-black text-[var(--hl-ink)]">{p.priceLabel}</div>
        <Link
          href={p.href}
          className="hl-cta mt-3 block rounded-xl py-2.5 text-center text-[20px] font-bold text-white"
          style={{ background: "linear-gradient(95deg, #FF7A2E 0%, #F0392C 100%)" }}
        >
          خرید
        </Link>
      </div>
    </div>
  );
}

function Arrow({ dir, onClick }: { dir: "prev" | "next"; onClick: () => void }) {
  // In RTL "next" (older items) sits on the left, "prev" on the right. They live in the frame's side
  // padding — beside the cards, never over them.
  const side = dir === "next" ? "left-1.5 sm:left-3" : "right-1.5 sm:right-3";
  return (
    <button
      type="button"
      aria-label={dir === "next" ? "بعدی" : "قبلی"}
      onClick={onClick}
      className={`absolute top-1/2 z-10 grid h-9 w-9 -translate-y-1/2 place-items-center rounded-full border border-[var(--hl-border)] bg-white text-[var(--hl-ink)] shadow-[0_10px_24px_-10px_rgba(20,20,20,0.35)] transition hover:border-[var(--hl-red)]/40 hover:text-[var(--hl-red)] sm:h-11 sm:w-11 ${side}`}
    >
      <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d={dir === "next" ? "M15 6l-6 6 6 6" : "M9 6l6 6-6 6"} />
      </svg>
    </button>
  );
}

export default function BestSellersCarousel({ products }: { products: CarouselCard[] }) {
  const trackRef = useRef<HTMLDivElement>(null);
  const [offset, setOffset] = useState(0);
  const [animate, setAnimate] = useState(true);
  const offsetRef = useRef(0);
  const animatingRef = useRef(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Triple the list and sit in the middle copy, so there's always a full copy
  // of buffer on both sides — head connects to tail with no blank flash.
  const loop = [...products, ...products, ...products];

  function stepPx() {
    const first = trackRef.current?.querySelector<HTMLElement>("[data-card]");
    // Fractional width (getBoundingClientRect, not the integer offsetWidth) so the offset stays
    // pixel-exact — an integer step accumulates a sub-pixel drift that clips the last card.
    return first ? first.getBoundingClientRect().width + 16 : 0; // card width + gap-4
  }

  // One full list-length in px. Derived from the card step (not scrollWidth)
  // so it's an exact multiple of the step and the loop never drifts.
  function listWidth() {
    return products.length * stepPx();
  }

  function apply(value: number, withAnimation: boolean) {
    offsetRef.current = value;
    setOffset(value);
    setAnimate(withAnimation);
  }

  // Positive offset shifts the track to the right (reveals items on the left = "next" in RTL).
  function move(dir: 1 | -1) {
    if (animatingRef.current) return; // ignore clicks mid-animation
    const step = stepPx();
    if (!step) return;
    animatingRef.current = true;
    apply(offsetRef.current + dir * step, true);
    // After the slide finishes, recenter into the middle copy if we crossed a
    // seam. Driven by a timer (not transitionend) so it fires reliably.
    timerRef.current = setTimeout(() => {
      animatingRef.current = false;
      const len = listWidth();
      if (!len) return;
      const v = offsetRef.current;
      if (v >= 2 * len) apply(v - len, false);
      else if (v < len) apply(v + len, false);
    }, DURATION + 20);
  }

  // Re-enable animation on the next frame after a seam jump.
  useLayoutEffect(() => {
    if (!animate) {
      const id = requestAnimationFrame(() => setAnimate(true));
      return () => cancelAnimationFrame(id);
    }
  }, [animate]);

  // Recenter into the middle copy on mount and whenever the viewport resizes, so the offset is always a
  // pixel-exact multiple of the current card step and every visible card stays whole (no clipped edges).
  useLayoutEffect(() => {
    const vp = trackRef.current?.parentElement;
    if (!vp) return;
    const ro = new ResizeObserver(() => apply(listWidth(), false));
    ro.observe(vp);
    return () => ro.disconnect();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => () => { if (timerRef.current) clearTimeout(timerRef.current); }, []);

  return (
    <div className="hl-offer-frame relative overflow-hidden rounded-[28px] border-2 border-[#ff5a1f] px-11 py-5 shadow-[0_18px_50px_-28px_rgba(239,35,60,0.45)] sm:px-14 sm:py-7 xl:px-20 xl:py-9">
      <Arrow dir="prev" onClick={() => move(-1)} />
      <Arrow dir="next" onClick={() => move(1)} />

      <div className="overflow-hidden">
        <div
          ref={trackRef}
          className="flex gap-4"
          style={{
            transform: `translateX(${offset}px)`,
            transition: animate ? `transform ${DURATION}ms ${EASE}` : "none",
          }}
        >
          {loop.map((p, i) => (
            <Card key={`${p.key}-${i}`} p={p} />
          ))}
        </div>
      </div>
    </div>
  );
}
