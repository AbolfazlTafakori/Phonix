"use client";

import { useEffect, useState, type CSSProperties } from "react";
import type { Comment } from "@/lib/types";
import Stars from "./Stars";

// Home-page reviews: a 3D cover-flow of approved, admin-selected comments. The active card faces front and
// the rest rotate back; it auto-advances every `autoplaySeconds` (0 disables auto-advance).
export default function TestimonialsCoverflow({
  comments,
  autoplaySeconds,
  title,
}: {
  comments: Comment[];
  autoplaySeconds: number;
  title: string;
}) {
  const [active, setActive] = useState(0);
  const [paused, setPaused] = useState(false);

  const n = comments.length;
  const autoplay = autoplaySeconds > 0 && n > 1;

  useEffect(() => {
    if (!autoplay || paused) return;
    const id = setTimeout(() => setActive((i) => (i + 1) % n), autoplaySeconds * 1000);
    return () => clearTimeout(id);
  }, [active, paused, autoplay, autoplaySeconds, n]);

  if (n === 0) return null;

  function styleFor(i: number): CSSProperties {
    let d = i - active;
    if (d > n / 2) d -= n;
    if (d < -n / 2) d += n;
    const a = Math.abs(d);
    if (a > 2) {
      return {
        opacity: 0,
        pointerEvents: "none",
        transform: `translateX(-50%) translateX(${d > 0 ? 520 : -520}px) rotateY(${-d * 42}deg) scale(0.55)`,
      };
    }
    return {
      opacity: d === 0 ? 1 : a === 1 ? 0.55 : 0.26,
      zIndex: 30 - a,
      transform: `translateX(-50%) translateX(${d * 168}px) translateZ(${-a * 150}px) rotateY(${-d * 30}deg) scale(${d === 0 ? 1 : 0.84})`,
    };
  }

  return (
    <section
      className="mx-auto mt-16 max-w-[1320px] px-5 sm:mt-24"
      aria-roledescription="carousel"
      aria-label={title}
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
    >
      <div className="mb-3 flex items-center gap-3">
        <h2 className="text-xl font-bold text-white sm:text-2xl">{title}</h2>
        <span aria-hidden className="h-px flex-1 bg-gradient-to-l from-white/15 to-transparent" />
      </div>

      <div className="relative" style={{ height: 300, perspective: "1300px" }}>
        <div
          aria-hidden
          className="pointer-events-none absolute left-1/2 top-1/2 h-[200px] w-[320px] -translate-x-1/2 -translate-y-1/2 rounded-full blur-[34px]"
          style={{ background: "radial-gradient(circle, rgba(230,0,83,0.2), rgba(62,58,242,0.13) 52%, transparent 72%)" }}
        />
        {comments.map((c, i) => (
          <button
            key={c.id}
            onClick={() => setActive(i)}
            aria-current={i === active}
            className="absolute left-1/2 top-3 w-[300px] max-w-[calc(100%-2.5rem)] cursor-pointer rounded-[18px] border border-white/10 bg-[#10101a] p-[22px] text-right transition-[transform,opacity] duration-[600ms] ease-out"
            style={{ ...styleFor(i), willChange: "transform, opacity" }}
          >
            <span aria-hidden className="absolute left-[18px] top-3 font-serif text-[40px] leading-none text-white/[0.08]">”</span>
            <div className="mb-3.5 flex items-center gap-3">
              <span className="grid h-[46px] w-[46px] shrink-0 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-lg font-bold text-white">
                {c.userName.charAt(0)}
              </span>
              <div className="min-w-0">
                <p className="truncate text-sm font-medium text-white">{c.userName}</p>
                {c.date && <p className="mt-0.5 text-[11.5px] text-white/45">{c.date}</p>}
              </div>
            </div>
            {c.rating > 0 && (
              <div className="mb-2.5">
                <Stars value={c.rating} />
              </div>
            )}
            <p className="line-clamp-4 text-[13px] leading-loose text-white/70">{c.body}</p>
          </button>
        ))}
      </div>

      {n > 1 && (
        <div className="mt-4 flex items-center justify-center gap-2">
          {comments.map((c, i) => (
            <button
              key={c.id}
              onClick={() => setActive(i)}
              aria-label={`نظر ${i + 1}`}
              aria-current={i === active}
              className="p-1.5"
            >
              <span
                className={`block h-1.5 rounded-full transition-all ${i === active ? "w-5 bg-[#e60053]" : "w-1.5 bg-white/25"}`}
              />
            </button>
          ))}
        </div>
      )}
    </section>
  );
}
