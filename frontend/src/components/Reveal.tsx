"use client";

import { useEffect, useRef, useState } from "react";

// Fade+slide-up wrapper for content as it scrolls into view. Mirrors HomeHero's own animation philosophy:
// the server-rendered markup carries no hidden state, so a crawler or a no-JS browser always sees the full
// content immediately. Only after hydration does this add the `reveal` class (which is what actually hides
// it), so real browsers get a brief entrance animation instead of a permanently-invisible one. Used site-wide
// (home sections, blog cards, product lists) so scroll motion reads identically everywhere.
export default function Reveal({
  children,
  className = "",
  delayMs = 0,
}: {
  children: React.ReactNode;
  className?: string;
  delayMs?: number;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const [ready, setReady] = useState(false);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    setReady(true);
    const el = ref.current;
    if (!el) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) {
          setVisible(true);
          observer.disconnect();
        }
      },
      { threshold: 0.12, rootMargin: "0px 0px -80px 0px" },
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  return (
    <div
      ref={ref}
      className={[className, ready ? "reveal" : "", visible ? "is-visible" : ""].filter(Boolean).join(" ")}
      style={delayMs ? { transitionDelay: `${delayMs}ms` } : undefined}
    >
      {children}
    </div>
  );
}
