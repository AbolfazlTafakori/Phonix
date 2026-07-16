"use client";

import { useEffect, useState } from "react";

function MoonIcon({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" />
    </svg>
  );
}

function SunIcon({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
    </svg>
  );
}

// The theme defaults to the OS/browser preference (the boot script in layout.tsx applies it and keeps
// following it while nothing is stored). This button is a plain two-state switch: one tap flips to the other
// theme and stores it as an explicit choice, which from then on wins over the OS.
function applyTheme() {
  const w = window as Window & { __phonixApplyTheme?: () => void };
  w.__phonixApplyTheme?.();
}

export default function ThemeToggle() {
  // Starts false (SSR-safe) and is read from the applied class on mount, so server and client markup agree.
  const [dark, setDark] = useState(false);

  useEffect(() => {
    const sync = () => setDark(document.documentElement.classList.contains("home-dark"));
    sync();
    // Until the user picks a side, the OS drives the theme — the boot script re-applies the class on an OS
    // flip, so re-read it here to keep the icon honest.
    const mq = window.matchMedia("(prefers-color-scheme: dark)");
    mq.addEventListener("change", sync);
    return () => mq.removeEventListener("change", sync);
  }, []);

  function toggle() {
    const next = !dark;
    try {
      localStorage.setItem("phonix-theme", next ? "dark" : "light");
    } catch {
      /* storage unavailable — theme still applies for this session */
    }
    setDark(next);
    applyTheme();
  }

  return (
    <button
      type="button"
      onClick={toggle}
      aria-label={dark ? "روشن کردن تم روشن" : "روشن کردن تم تیره"}
      title={dark ? "روشن کردن تم روشن" : "روشن کردن تم تیره"}
      className="grid h-11 w-11 place-items-center rounded-full text-[var(--hl-ink)] transition hover:text-[var(--hl-red)]"
    >
      {dark ? <SunIcon className="h-5 w-5" /> : <MoonIcon className="h-5 w-5" />}
    </button>
  );
}
