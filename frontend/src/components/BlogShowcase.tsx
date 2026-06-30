"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import type { BlogPost } from "@/lib/types";

// Home-page blog section: one large featured post that cross-fades between the selected posts, with the
// others listed beside it. Clicking a side card promotes it; auto-switch runs every `autoplaySeconds`
// (0 disables it, leaving manual selection only).
export default function BlogShowcase({
  posts,
  autoplaySeconds,
  title,
}: {
  posts: BlogPost[];
  autoplaySeconds: number;
  title: string;
}) {
  const [active, setActive] = useState(0);
  const [paused, setPaused] = useState(false);

  const n = posts.length;
  const autoplay = autoplaySeconds > 0 && n > 1;

  // Re-arms on every change so a manual pick resets the countdown; halts while hovered.
  useEffect(() => {
    if (!autoplay || paused) return;
    const id = setTimeout(() => setActive((i) => (i + 1) % n), autoplaySeconds * 1000);
    return () => clearTimeout(id);
  }, [active, paused, autoplay, autoplaySeconds, n]);

  if (n === 0) return null;

  const others = posts.map((p, i) => ({ p, i })).filter((o) => o.i !== active);
  const nextIndex = (active + 1) % n;

  return (
    <section
      className="mx-auto mt-16 max-w-[1320px] px-5 sm:mt-24"
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
    >
      <div className="mb-8 flex items-center gap-3">
        <h2 className="text-xl font-bold text-white sm:text-2xl">{title}</h2>
        <span aria-hidden className="h-px flex-1 bg-gradient-to-l from-white/15 to-transparent" />
        <Link href="/blog" className="shrink-0 text-sm text-white/45 transition hover:text-white">
          مشاهده همه ←
        </Link>
      </div>

      <div className="grid gap-4 md:grid-cols-[1.5fr_1fr]">
        {/* featured — all posts rendered up front (images preloaded) and cross-faded, so switching never pops */}
        <div className="relative min-h-[300px]">
          {posts.map((p, i) => {
            const on = i === active;
            return (
              <Link
                key={p.id}
                href={`/blog/${p.slug}`}
                aria-hidden={!on}
                tabIndex={on ? undefined : -1}
                className={`group flex flex-col overflow-hidden rounded-[18px] border border-white/10 bg-[#0d0d15] transition-opacity duration-500 ease-out ${
                  on ? "relative opacity-100" : "pointer-events-none absolute inset-0 opacity-0"
                }`}
              >
                <div className="relative h-[180px] overflow-hidden sm:h-[210px]">
                  <img src={p.image} alt={p.title} className="h-full w-full object-cover transition duration-500 group-hover:scale-105" />
                  <div className="absolute inset-0 bg-gradient-to-t from-[#0d0d15] via-[#0d0d15]/25 to-transparent" />
                  {p.tag && (
                    <span className="absolute right-4 top-4 inline-flex rounded-full border border-white/15 bg-black/40 px-3 py-1 text-[11px] text-[#dac8ff] backdrop-blur-sm">
                      {p.tag}
                    </span>
                  )}
                </div>
                <div className="p-5">
                  <h3 className="text-lg font-bold leading-8 text-white sm:text-xl">{p.title}</h3>
                  {p.excerpt && <p className="mt-2 line-clamp-2 text-sm leading-7 text-white/55">{p.excerpt}</p>}
                  {p.date && <p className="mt-4 text-xs text-white/40">{p.date}</p>}
                </div>
              </Link>
            );
          })}
        </div>

        {/* the other posts — selectable; the next-up card shows an auto-switch progress bar */}
        <div className="flex flex-col gap-3">
          {others.map(({ p, i }) => (
            <button
              key={p.id}
              onClick={() => setActive(i)}
              className="group relative flex gap-3 overflow-hidden rounded-[13px] border border-white/8 bg-[#0b0b12] p-2.5 text-right transition hover:-translate-y-0.5 hover:border-white/20"
            >
              <img src={p.image} alt="" className="h-16 w-16 shrink-0 rounded-[10px] object-cover" />
              <span className="flex min-w-0 flex-col justify-center">
                {p.tag && <span className="mb-1 text-[10px] text-[#9b8cff]">{p.tag.split("|")[0].trim()}</span>}
                <span className="line-clamp-2 text-[13px] font-medium leading-6 text-white">{p.title}</span>
              </span>
              {autoplay && !paused && i === nextIndex && (
                <span aria-hidden className="absolute inset-x-0 bottom-0 h-[2px] overflow-hidden bg-white/10">
                  <span
                    key={`fill-${active}`}
                    className="hero-bar-fill block h-full"
                    style={{ background: "linear-gradient(to left, #e60053, #ff5c8a)", ["--hero-dur" as string]: `${autoplaySeconds}s` }}
                  />
                </span>
              )}
            </button>
          ))}
        </div>
      </div>
    </section>
  );
}
