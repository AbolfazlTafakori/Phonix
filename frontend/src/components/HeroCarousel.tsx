"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { HeroSlide, TrustItem } from "@/lib/types";
import { formatNumber } from "@/lib/format";
import { heroTrustIconNode } from "./heroTrustIcons";
import { ArrowLeft, ArrowRight } from "./Icons";

const AUTOPLAY_MS = 6500;
const SWIPE_THRESHOLD = 45;

// Fallback trust badges, used when the admin hasn't configured any (e.g. a store whose saved site content
// predates this field). The admin can edit/add/remove these from the panel.
const DEFAULT_TRUST: TrustItem[] = [
  { icon: "bolt", label: "تحویل آنی" },
  { icon: "shield", label: "گارانتی کامل" },
  { icon: "lock", label: "پرداخت امن" },
  { icon: "headset", label: "پشتیبانی ۲۴/۷" },
];

export default function HeroCarousel({ slides }: { slides: HeroSlide[] }) {
  const [index, setIndex] = useState(0);
  const [paused, setPaused] = useState(false);
  const touchX = useRef<number | null>(null);

  const count = slides.length;
  const many = count > 1;

  const goTo = useCallback((i: number) => setIndex(((i % count) + count) % count), [count]);
  const go = useCallback((dir: number) => setIndex((i) => (i + dir + count) % count), [count]);

  // Auto-advance. Re-arms on every index change (so manual nav resets the countdown) and
  // halts while paused (pointer hover / hidden tab). Pure content rotation with a gentle
  // crossfade — acceptable under reduced motion, where the progress fill is frozen by CSS.
  useEffect(() => {
    if (!many || paused) return;
    const id = setTimeout(() => setIndex((i) => (i + 1) % count), AUTOPLAY_MS);
    return () => clearTimeout(id);
  }, [index, paused, many, count]);

  // Pause while the tab is hidden so slides don't silently race ahead in the background.
  useEffect(() => {
    const onVis = () => setPaused(document.visibilityState !== "visible");
    document.addEventListener("visibilitychange", onVis);
    return () => document.removeEventListener("visibilitychange", onVis);
  }, []);

  if (count === 0) return null;

  const slide = slides[index % count];
  const accent = slide.accentColor?.trim() || "#e60053";
  // Admin-tunable accent size; clamped so extreme values can't blow out or hide the visuals.
  const accentScale = Math.min(2, Math.max(0.5, slide.accentScale || 1));
  const trustItems = slide.trust && slide.trust.length > 0 ? slide.trust : DEFAULT_TRUST;
  const trustColor = slide.trustColor?.trim() || accent;
  const hasPrice = slide.priceFrom != null && slide.priceFrom > 0;
  const hasOld = slide.oldPrice != null && slide.priceFrom != null && slide.oldPrice > slide.priceFrom;
  const hasSecondary = slide.secondaryButtonText?.trim().length > 0;

  function onTouchStart(e: React.TouchEvent) {
    touchX.current = e.touches[0].clientX;
  }
  function onTouchEnd(e: React.TouchEvent) {
    if (touchX.current == null) return;
    const dx = e.changedTouches[0].clientX - touchX.current;
    if (Math.abs(dx) > SWIPE_THRESHOLD) go(dx < 0 ? 1 : -1);
    touchX.current = null;
  }

  return (
    <section className="mx-auto mt-8 max-w-[1320px] px-5" aria-roledescription="carousel" aria-label="معرفی محصولات">
      <div
        onMouseEnter={() => setPaused(true)}
        onMouseLeave={() => setPaused(false)}
        onTouchStart={onTouchStart}
        onTouchEnd={onTouchEnd}
        className="group relative overflow-hidden rounded-[28px] border border-white/10 px-5 py-9 sm:px-8 md:min-h-[400px] md:px-[88px] md:py-12"
        style={{
          background:
            "radial-gradient(120% 140% at 88% 0%, #241036 0%, #15131f 46%, #0d0d17 100%)",
        }}
      >
        {/* ambient layers */}
        <div
          aria-hidden
          className="hero-aura pointer-events-none absolute inset-0 z-0"
          style={{ background: `radial-gradient(${440 * accentScale}px ${380 * accentScale}px at 22% 92%, ${hexToRgba(accent, 0.28)}, transparent 70%)` }}
        />
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 z-0 opacity-50"
          style={{
            backgroundImage:
              "linear-gradient(rgba(255,255,255,0.04) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.04) 1px, transparent 1px)",
            backgroundSize: "44px 44px",
            maskImage: "radial-gradient(80% 80% at 70% 20%, #000, transparent 75%)",
            WebkitMaskImage: "radial-gradient(80% 80% at 70% 20%, #000, transparent 75%)",
          }}
        />

        <div className="relative z-10 grid items-center gap-8 md:grid-cols-2">
          {/* art / spotlight — first in DOM → right column in RTL, and on top when stacked on mobile */}
          <div className="relative flex min-h-[220px] items-center justify-center md:min-h-[280px]">
            <div
              aria-hidden
              className="absolute aspect-square rounded-full blur-[60px]"
              style={{ width: `${15 * accentScale}rem`, background: hexToRgba(accent, 0.4) }}
            />
            <div key={`a-${index}`} className="hero-anim-art relative z-10 flex items-center justify-center">
              {slide.badge?.trim() && (
                <span
                  className="absolute -top-2 right-2 z-20 rounded-lg px-3 py-1.5 text-xs font-bold text-white shadow-[0_10px_22px_-8px_rgba(0,0,0,0.7)]"
                  style={{ background: `linear-gradient(to left, ${accent}, ${hexToRgba(accent, 0.7)})` }}
                >
                  {slide.badge}
                </span>
              )}
              {slide.image && (
                <img
                  src={slide.image}
                  alt={slide.title}
                  className="hero-float relative w-[74%] max-w-[380px] drop-shadow-[0_30px_60px_rgba(0,0,0,0.6)] md:w-[82%]"
                />
              )}
              {slide.logo && (
                <img
                  src={slide.logo}
                  alt=""
                  aria-hidden
                  className="absolute bottom-2 left-4 z-20 w-16 drop-shadow-[0_10px_24px_rgba(0,0,0,0.6)] sm:w-20"
                />
              )}
            </div>
          </div>

          {/* text — second in DOM → left column in RTL, and below the art on mobile */}
          <div className="text-right md:pr-6">
            <div key={`t-${index}`} className="hero-anim-text">
              {slide.eyebrow?.trim() && (
                <span className="mb-4 inline-flex items-center gap-2 rounded-full border border-white/15 bg-white/5 px-3.5 py-1.5 text-xs font-medium text-[#dac8ff] sm:text-[13px]">
                  <svg viewBox="0 0 24 24" className="h-4 w-4 text-emerald-400" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M12 2 4 5v6c0 5 3.5 7.7 8 9 4.5-1.3 8-4 8-9V5l-8-3Z" />
                    <path d="m9 12 2 2 4-4" />
                  </svg>
                  {slide.eyebrow}
                </span>
              )}

              <h1 className="text-pretty text-3xl font-bold leading-[1.15] text-white sm:text-4xl md:text-[40px]">
                {slide.title}
              </h1>

              {slide.description?.trim() && (
                <p className="mt-3 line-clamp-2 max-w-[46ch] text-sm leading-7 text-white/65 sm:mt-4 sm:line-clamp-3">
                  {slide.description}
                </p>
              )}

              {hasPrice && (
                <div className="mt-5 flex items-baseline gap-2.5">
                  <span className="text-xs text-white/45">شروع از</span>
                  {hasOld && (
                    <span className="text-sm text-white/40 line-through" dir="ltr">
                      {formatNumber(slide.oldPrice!)}
                    </span>
                  )}
                  <span className="text-2xl font-bold text-white" dir="ltr">
                    {formatNumber(slide.priceFrom!)}
                  </span>
                  <span className="text-xs text-white/55">تومان</span>
                </div>
              )}

              <div className="mt-6 flex flex-wrap gap-3">
                <a
                  href={slide.buttonLink || "#"}
                  className="hero-cta rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-7 py-3 text-sm font-bold text-white shadow-[0_16px_38px_-14px_rgba(58,100,242,0.85)] transition hover:brightness-110"
                >
                  {slide.buttonText || "مشاهده"}
                </a>
                {hasSecondary && (
                  <a
                    href={slide.secondaryButtonLink || "#"}
                    className="rounded-xl border border-white/15 bg-white/[0.03] px-6 py-3 text-sm font-medium text-white/90 transition hover:border-white/30 hover:bg-white/10"
                  >
                    {slide.secondaryButtonText}
                  </a>
                )}
              </div>
            </div>

            {/* brand-level trust row — static across slides, admin-configurable. Pill chips read as a tidy
                group and wrap cleanly on every width; the icon picks up the slide's accent colour. */}
            <div className="mt-7 flex flex-wrap gap-2 border-t border-white/10 pt-5">
              {trustItems.map((t, i) => (
                <span
                  key={`${t.icon}-${i}`}
                  className="inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-xs font-medium text-white/80 sm:text-[13px]"
                  style={{ background: hexToRgba(trustColor, 0.1), borderColor: hexToRgba(trustColor, 0.28) }}
                >
                  <svg viewBox="0 0 24 24" className="h-[18px] w-[18px] shrink-0" style={{ color: trustColor }} fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
                    {heroTrustIconNode(t.icon)}
                  </svg>
                  {t.label}
                </span>
              ))}
            </div>
          </div>
        </div>

        {/* navigation arrows — vertically centered, glass, fill with brand on hover (desktop/tablet) */}
        {many && (
          <>
            <button
              onClick={() => go(-1)}
              aria-label="اسلاید قبلی"
              className="absolute right-3 top-1/2 z-20 hidden h-11 w-11 -translate-y-1/2 place-items-center rounded-full border border-white/15 bg-[#12121c]/50 text-white backdrop-blur-md transition hover:scale-110 hover:border-transparent hover:bg-gradient-to-br hover:from-[#e60053] hover:to-[#b41f4c] hover:shadow-[0_14px_34px_-10px_rgba(230,0,83,0.75)] md:grid md:right-5"
            >
              <ArrowRight className="h-5 w-5" />
            </button>
            <button
              onClick={() => go(1)}
              aria-label="اسلاید بعدی"
              className="absolute left-3 top-1/2 z-20 hidden h-11 w-11 -translate-y-1/2 place-items-center rounded-full border border-white/15 bg-[#12121c]/50 text-white backdrop-blur-md transition hover:scale-110 hover:border-transparent hover:bg-gradient-to-br hover:from-[#e60053] hover:to-[#b41f4c] hover:shadow-[0_14px_34px_-10px_rgba(230,0,83,0.75)] md:grid md:left-5"
            >
              <ArrowLeft className="h-5 w-5" />
            </button>
          </>
        )}

        {/* segmented progress / pagination */}
        {many && (
          <div className="absolute bottom-4 left-0 right-0 z-20 flex items-center justify-center gap-2">
            {slides.map((s, i) => (
              <button
                key={s.id}
                onClick={() => goTo(i)}
                aria-label={`رفتن به اسلاید ${i + 1}`}
                aria-current={i === index}
                className="group/bar p-2"
              >
                <span className="block h-1 w-8 overflow-hidden rounded-full bg-white/20 transition-all group-hover/bar:bg-white/35 sm:w-10">
                  {i === index ? (
                    <span
                      key={`fill-${index}`}
                      className="hero-bar-fill block h-full rounded-full"
                      style={{ background: "linear-gradient(to left, #e60053, #ff5c8a)", animationPlayState: paused ? "paused" : "running" }}
                    />
                  ) : null}
                </span>
              </button>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}

// Accept #rgb / #rrggbb (the admin saves hex) and fall back to the raw value for
// already-rgba()/named colors, so a malformed accent never throws.
function hexToRgba(color: string, alpha: number): string {
  const hex = color.trim();
  const m = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(hex);
  if (!m) return color;
  let h = m[1];
  if (h.length === 3) h = h.split("").map((c) => c + c).join("");
  const n = parseInt(h, 16);
  return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`;
}
