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

// Auto (system) mode is the default: the OS/browser preference decides and keeps deciding. Tapping cycles
// system → light → dark → system, so an explicit override is always reversible back to following the OS.
type Mode = "system" | "light" | "dark";

const NEXT: Record<Mode, Mode> = { system: "light", light: "dark", dark: "system" };
const LABEL: Record<Mode, string> = {
  system: "تم خودکار (هماهنگ با سیستم) — برای تم روشن کلیک کنید",
  light: "تم روشن — برای تم تیره کلیک کنید",
  dark: "تم تیره — برای تم خودکار کلیک کنید",
};

function readMode(): Mode {
  try {
    const m = localStorage.getItem("phonix-theme");
    return m === "light" || m === "dark" ? m : "system";
  } catch {
    return "system";
  }
}

// The boot script in layout.tsx owns the apply logic (and the OS-change listener); reuse it so the rule for
// "what does this mode mean" lives in exactly one place.
function applyTheme() {
  const w = window as Window & { __phonixApplyTheme?: () => void };
  w.__phonixApplyTheme?.();
}

export default function ThemeToggle() {
  // Both start at their SSR-safe defaults and are read on mount, so the server and client markup agree.
  const [mode, setMode] = useState<Mode>("system");
  const [systemDark, setSystemDark] = useState(false);

  useEffect(() => {
    setMode(readMode());
    const mq = window.matchMedia("(prefers-color-scheme: dark)");
    setSystemDark(mq.matches);
    // While on auto, an OS flip changes the icon too, not just the colors.
    const sync = () => setSystemDark(mq.matches);
    mq.addEventListener("change", sync);
    return () => mq.removeEventListener("change", sync);
  }, []);

  function cycle() {
    const next = NEXT[mode];
    try {
      localStorage.setItem("phonix-theme", next);
    } catch {
      /* storage unavailable — theme still applies for this session */
    }
    setMode(next);
    applyTheme();
  }

  const showingDark = mode === "dark" || (mode === "system" && systemDark);

  return (
    <button
      type="button"
      onClick={cycle}
      aria-label={LABEL[mode]}
      title={LABEL[mode]}
      className="relative grid h-11 w-11 place-items-center rounded-full text-[var(--hl-ink)] transition hover:text-[var(--hl-red)]"
    >
      {showingDark ? <MoonIcon className="h-5 w-5" /> : <SunIcon className="h-5 w-5" />}
      {/* a dot marks auto mode, so «following the system» is visible at a glance */}
      {mode === "system" && <span className="absolute bottom-1.5 h-1 w-1 rounded-full bg-[var(--hl-red)]" />}
    </button>
  );
}
