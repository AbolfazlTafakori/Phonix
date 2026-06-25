"use client";

import { useEffect, useMemo, useRef, useState } from "react";

// Self-contained Jalali (Shamsi) date field — no external dependency. Users can either pick a day from the
// popover calendar or type manually; when typing, slashes are inserted automatically between year/month/day.
// The value is stored as a Persian-digit string "۱۴۰۳/۰۳/۲۲" to match how the rest of the app shows dates.

const FA_DIGITS = "۰۱۲۳۴۵۶۷۸۹";
const toFa = (s: string | number) => String(s).replace(/\d/g, (d) => FA_DIGITS[Number(d)]);
const toLatin = (s: string) => s.replace(/[۰-۹]/g, (d) => String(FA_DIGITS.indexOf(d)));

// --- jalaali <-> gregorian conversion (algorithm from jalaali-js, trimmed) ---
// Integer division MUST truncate toward zero (not floor): the leap-year math relies on negative
// intermediates, where Math.floor would round the wrong way and shift dates by a day for some years.
const div = (a: number, b: number) => Math.trunc(a / b);
const mod = (a: number, b: number) => a - Math.trunc(a / b) * b;

function jalCal(jy: number) {
  const breaks = [-61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178];
  const gy = jy + 621;
  let leapJ = -14;
  let jp = breaks[0];
  let jump = 0;
  for (let i = 1; i < breaks.length; i += 1) {
    const jm = breaks[i];
    jump = jm - jp;
    if (jy < jm) break;
    leapJ = leapJ + div(jump, 33) * 8 + div(mod(jump, 33), 4);
    jp = jm;
  }
  let n = jy - jp;
  leapJ = leapJ + div(n, 33) * 8 + div(mod(n, 33) + 3, 4);
  if (mod(jump, 33) === 4 && jump - n === 4) leapJ += 1;
  const leapG = div(gy, 4) - div((div(gy, 100) + 1) * 3, 4) - 150;
  const march = 20 + leapJ - leapG;
  if (jump - n < 6) n = n - jump + div(jump + 4, 33) * 33;
  let leap = mod(mod(n + 1, 33) - 1, 4);
  if (leap === -1) leap = 4;
  return { leap, gy, march };
}

function g2d(gy: number, gm: number, gd: number) {
  let d = div((gy + div(gm - 8, 6) + 100100) * 1461, 4) + div(153 * mod(gm + 9, 12) + 2, 5) + gd - 34840408;
  d = d - div(div(gy + 100100 + div(gm - 8, 6), 100) * 3, 4) + 752;
  return d;
}
function d2g(jdn: number) {
  let j = 4 * jdn + 139361631;
  j = j + div(div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
  const i = div(mod(j, 1461), 4) * 5 + 308;
  const gd = div(mod(i, 153), 5) + 1;
  const gm = mod(div(i, 153), 12) + 1;
  const gy = div(j, 1461) - 100100 + div(8 - gm, 6);
  return { gy, gm, gd };
}
function j2d(jy: number, jm: number, jd: number) {
  const r = jalCal(jy);
  return g2d(r.gy, 3, r.march) + (jm - 1) * 31 - div(jm, 7) * (jm - 7) + jd - 1;
}
function toJalaali(gy: number, gm: number, gd: number) {
  const jdn = g2d(gy, gm, gd);
  const gyy = d2g(jdn).gy;
  let jy = gyy - 621;
  const r = jalCal(jy);
  const jdn1f = g2d(gyy, 3, r.march);
  let k = jdn - jdn1f;
  if (k >= 0) {
    if (k <= 185) return { jy, jm: 1 + div(k, 31), jd: mod(k, 31) + 1 };
    k -= 186;
  } else {
    jy -= 1;
    k += 179;
    if (r.leap === 1) k += 1;
  }
  return { jy, jm: 7 + div(k, 30), jd: mod(k, 30) + 1 };
}
const isLeap = (jy: number) => jalCal(jy).leap === 0;
const monthLen = (jy: number, jm: number) => (jm <= 6 ? 31 : jm <= 11 ? 30 : isLeap(jy) ? 30 : 29);

// weekday column for a Jalali date, Saturday = 0 (matching the Persian week shown below).
function weekdayCol(jy: number, jm: number, jd: number) {
  const g = d2g(j2d(jy, jm, jd));
  const jsDay = new Date(Date.UTC(g.gy, g.gm - 1, g.gd)).getUTCDay(); // Sun=0..Sat=6
  return (jsDay + 1) % 7; // Sat -> 0
}

const WEEK = ["ش", "ی", "د", "س", "چ", "پ", "ج"];
const MONTHS = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

function parse(value: string): { jy: number; jm: number; jd: number } | null {
  const parts = toLatin(value).split("/").map((p) => parseInt(p, 10));
  if (parts.length === 3 && parts.every((n) => Number.isFinite(n)) && parts[1] >= 1 && parts[1] <= 12 && parts[2] >= 1 && parts[2] <= 31) {
    return { jy: parts[0], jm: parts[1], jd: parts[2] };
  }
  return null;
}

// Inserts slashes as the user types: digits-only -> ۱۴۰۳/۰۳/۲۲ (max 8 digits).
function autoFormat(raw: string): string {
  const digits = toLatin(raw).replace(/\D/g, "").slice(0, 8);
  const y = digits.slice(0, 4);
  const m = digits.slice(4, 6);
  const d = digits.slice(6, 8);
  let out = y;
  if (digits.length > 4) out += "/" + m;
  if (digits.length > 6) out += "/" + d;
  return toFa(out);
}

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3e3af2] placeholder:text-white/35";

export default function JalaliDatePicker({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);

  const today = useMemo(() => {
    const n = new Date();
    return toJalaali(n.getFullYear(), n.getMonth() + 1, n.getDate());
  }, []);
  const selected = parse(value);
  const [view, setView] = useState(() => selected ?? today);

  useEffect(() => {
    if (!open) return;
    function onDoc(e: MouseEvent) {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, [open]);

  function openCalendar() {
    setView(parse(value) ?? today);
    setOpen((v) => !v);
  }

  function move(delta: number) {
    let { jy, jm } = view;
    jm += delta;
    if (jm < 1) { jm = 12; jy -= 1; }
    if (jm > 12) { jm = 1; jy += 1; }
    setView({ jy, jm, jd: 1 });
  }

  function pick(jd: number) {
    onChange(`${toFa(view.jy)}/${toFa(String(view.jm).padStart(2, "0"))}/${toFa(String(jd).padStart(2, "0"))}`);
    setOpen(false);
  }

  const lead = weekdayCol(view.jy, view.jm, 1);
  const days = monthLen(view.jy, view.jm);
  const cells: (number | null)[] = [...Array(lead).fill(null), ...Array.from({ length: days }, (_, i) => i + 1)];

  return (
    <div ref={wrapRef} className="relative">
      <div className="relative">
        <input
          value={value}
          onChange={(e) => onChange(autoFormat(e.target.value))}
          dir="ltr"
          inputMode="numeric"
          placeholder="۱۴۰۳/۰۳/۲۲"
          className={`${inputCls} text-left pl-11`}
        />
        <button
          type="button"
          onClick={openCalendar}
          aria-label="انتخاب از تقویم"
          className="absolute left-2 top-1/2 grid h-8 w-8 -translate-y-1/2 place-items-center rounded-lg text-white/55 transition hover:bg-white/10 hover:text-white"
        >
          <svg viewBox="0 0 24 24" fill="none" className="h-5 w-5" stroke="currentColor" strokeWidth="1.8">
            <rect x="3" y="4.5" width="18" height="16" rx="2.5" />
            <path d="M3 9h18M8 2.5v4M16 2.5v4" strokeLinecap="round" />
          </svg>
        </button>
      </div>

      {open && (
        <div className="absolute z-30 mt-2 w-[290px] rounded-2xl border border-white/10 bg-[#15151f] p-3 shadow-[0_20px_50px_-12px_rgba(0,0,0,0.7)]">
          <div className="mb-2 flex items-center justify-between">
            <button type="button" onClick={() => move(-1)} className="grid h-8 w-8 place-items-center rounded-lg text-white/60 transition hover:bg-white/10 hover:text-white">‹</button>
            <span className="text-sm font-bold text-white">{MONTHS[view.jm - 1]} {toFa(view.jy)}</span>
            <button type="button" onClick={() => move(1)} className="grid h-8 w-8 place-items-center rounded-lg text-white/60 transition hover:bg-white/10 hover:text-white">›</button>
          </div>
          <div className="mb-1 grid grid-cols-7 gap-1 text-center text-[11px] font-bold text-white/40">
            {WEEK.map((w) => <span key={w}>{w}</span>)}
          </div>
          <div className="grid grid-cols-7 gap-1">
            {cells.map((jd, i) => {
              if (jd === null) return <span key={i} />;
              const isToday = today.jy === view.jy && today.jm === view.jm && today.jd === jd;
              const isSel = selected && selected.jy === view.jy && selected.jm === view.jm && selected.jd === jd;
              return (
                <button
                  key={i}
                  type="button"
                  onClick={() => pick(jd)}
                  className={`grid h-8 place-items-center rounded-lg text-sm transition ${
                    isSel ? "bg-gradient-to-l from-[#1733d6] to-[#3a64f2] font-bold text-white"
                      : isToday ? "border border-[#3a64f2]/60 text-white"
                      : "text-white/75 hover:bg-white/10"
                  }`}
                >
                  {toFa(jd)}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
