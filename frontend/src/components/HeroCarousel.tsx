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
  // halts while paused (pointer hover / hidden tab).
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
        {/* ambient layers — shared across slides (kept brand-neutral so they never jump on a switch) */}
        <div
          aria-hidden
          className="hero-aura pointer-events-none absolute inset-0 z-0"
          style={{ background: "radial-gradient(460px 380px at 22% 92%, rgba(62,58,242,0.22), transparent 70%)" }}
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

        {/* slides stage — every slide (and its image) is rendered up front so switching never pops.
            The active slide is in flow and drives the height; the rest are layered behind it at opacity 0
            and cross-fade in. The stage is `relative` so the layered slides align to the padded content box. */}
        <div className="relative z-10">
          {slides.map((s, i) => {
            const accent = s.accentColor?.trim() || "#e60053";
            const accentScale = Math.min(2, Math.max(0.5, s.accentScale || 1));
            const trustItems = s.trust && s.trust.length > 0 ? s.trust : DEFAULT_TRUST;
            const trustColor = s.trustColor?.trim() || accent;
            const hasPrice = s.priceFrom != null && s.priceFrom > 0;
            const hasOld = s.oldPrice != null && s.priceFrom != null && s.oldPrice > s.priceFrom;
            const hasSecondary = s.secondaryButtonText?.trim().length > 0;
            const active = i === index;

            return (
              <div
                key={s.id}
                inert={!active}
                className={`transition-opacity duration-700 ease-out ${
                  active ? "relative opacity-100" : "absolute inset-0 opacity-0"
                }`}
              >
                <div className="grid items-center gap-8 md:grid-cols-2">
                  {/* art / spotlight — right column in RTL, on top when stacked on mobile */}
                  <div className="relative flex min-h-[220px] items-center justify-center md:min-h-[280px]">
                    <div
                      aria-hidden
                      className="absolute aspect-square rounded-full blur-[60px]"
                      style={{ width: `${15 * accentScale}rem`, background: hexToRgba(accent, 0.4) }}
                    />
                    <div className="relative z-10 flex items-center justify-center">
                      {s.badge?.trim() && (
                        <span
                          className="absolute -top-2 right-2 z-20 rounded-lg px-3 py-1.5 text-xs font-bold text-white shadow-[0_10px_22px_-8px_rgba(0,0,0,0.7)]"
                          style={{ background: `linear-gradient(to left, ${accent}, ${hexToRgba(accent, 0.7)})` }}
                        >
                          {s.badge}
                        </span>
                      )}
                      {s.image && (
                        <img
                          src={s.image}
                          alt={s.title}
                          className="hero-float relative w-[74%] max-w-[380px] drop-shadow-[0_30px_60px_rgba(0,0,0,0.6)] md:w-[82%]"
                        />
                      )}
                      {s.logo && (
                        <img
                          src={s.logo}
                          alt=""
                          aria-hidden
                          className="absolute bottom-2 left-4 z-20 w-16 drop-shadow-[0_10px_24px_rgba(0,0,0,0.6)] sm:w-20"
                        />
                      )}
                    </div>
                  </div>

                  {/* text — left column in RTL, below the art on mobile */}
                  <div className="text-right md:pr-6">
                    <div>
                      {s.eyebrow?.trim() && (
                        <span className="mb-4 inline-flex items-center gap-2 rounded-full border border-white/15 bg-white/5 px-3.5 py-1.5 text-xs font-medium text-[#dac8ff] sm:text-[13px]">
                          <svg viewBox="0 0 24 24" className="h-4 w-4 text-emerald-400" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <path d="M12 2 4 5v6c0 5 3.5 7.7 8 9 4.5-1.3 8-4 8-9V5l-8-3Z" />
                            <path d="m9 12 2 2 4-4" />
                          </svg>
                          {s.eyebrow}
                        </span>
                      )}

                      <h1 className="text-pretty text-3xl font-bold leading-[1.15] text-white sm:text-4xl md:text-[40px]">
                        {s.title}
                      </h1>

                      {s.description?.trim() && (
                        <p className="mt-3 line-clamp-2 max-w-[46ch] text-sm leading-7 text-white/65 sm:mt-4 sm:line-clamp-3">
                          {s.description}
                        </p>
                      )}

                      {hasPrice && (
                        <div className="mt-5 flex items-baseline gap-2.5">
                          <span className="text-xs text-white/45">شروع از</span>
                          {hasOld && (
                            <span className="text-sm text-white/40 line-through" dir="ltr">
                              {formatNumber(s.oldPrice!)}
                            </span>
                          )}
                          <span className="text-2xl font-bold text-white" dir="ltr">
                            {formatNumber(s.priceFrom!)}
                          </span>
                          <span className="text-xs text-white/55">تومان</span>
                        </div>
                      )}

                      <div className="mt-6 flex flex-wrap gap-3">
                        <a
                          href={s.buttonLink || "#"}
                          className="hero-cta rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-7 py-3 text-sm font-bold text-white shadow-[0_16px_38px_-14px_rgba(58,100,242,0.85)] transition hover:brightness-110"
                        >
                          {s.buttonText || "مشاهده"}
                        </a>
                        {hasSecondary && (
                          <a
                            href={s.secondaryButtonLink || "#"}
                            className="rounded-xl border border-white/15 bg-white/[0.03] px-6 py-3 text-sm font-medium text-white/90 transition hover:border-white/30 hover:bg-white/10"
                          >
                            {s.secondaryButtonText}
                          </a>
                        )}
                      </div>
                    </div>

                    {/* brand-level trust row — admin-configurable pill chips that wrap cleanly on every width. */}
                    <div className="mt-7 flex flex-wrap gap-2 border-t border-white/10 pt-5">
                      {trustItems.map((t, ti) => (
                        <span
                          key={`${t.icon}-${ti}`}
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
              </div>
            );
          })}
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
