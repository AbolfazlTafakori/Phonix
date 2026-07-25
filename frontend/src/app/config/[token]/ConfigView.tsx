"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { QRCodeSVG } from "qrcode.react";
import { api } from "@/lib/api";
import { toFa } from "@/lib/format";
import type { V2RayConfig, V2RayConfigLine } from "@/lib/types";

// Live usage is polled, so the page tracks the customer's real consumption without a manual refresh.
const REFRESH_MS = 20000;

function formatBytes(bytes: number): string {
  if (bytes <= 0) return "۰ مگابایت";
  const units = ["بایت", "کیلوبایت", "مگابایت", "گیگابایت", "ترابایت"];
  const i = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  const value = bytes / Math.pow(1024, i);
  return `${toFa(value >= 10 || i === 0 ? Math.round(value) : Number(value.toFixed(2)))} ${units[i]}`;
}

function faDate(iso: string | null): string {
  if (!iso) return "—";
  try { return new Date(iso).toLocaleDateString("fa-IR", { year: "numeric", month: "long", day: "numeric" }); }
  catch { return "—"; }
}

function useCopy(): [boolean, (text: string) => void] {
  const [done, setDone] = useState(false);
  const t = useRef<ReturnType<typeof setTimeout> | null>(null);
  const copy = useCallback((text: string) => {
    navigator.clipboard?.writeText(text).then(() => {
      setDone(true);
      if (t.current) clearTimeout(t.current);
      t.current = setTimeout(() => setDone(false), 1800);
    }).catch(() => {});
  }, []);
  useEffect(() => () => { if (t.current) clearTimeout(t.current); }, []);
  return [done, copy];
}

function CopyBtn({ value, label }: { value: string; label?: string }) {
  const [done, copy] = useCopy();
  return (
    <button
      type="button"
      onClick={() => copy(value)}
      aria-label={label ?? "کپی"}
      className="grid h-9 w-9 shrink-0 place-items-center rounded-lg border transition active:scale-95"
      style={{ borderColor: "var(--ac-panel-border)", background: done ? "#22B573" : "var(--ac-panel-bg)", color: done ? "#fff" : "var(--ac-text)" }}
    >
      {done ? (
        <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><path d="M20 6 9 17l-5-5" /></svg>
      ) : (
        <svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round"><rect x="9" y="9" width="11" height="11" rx="2" /><path d="M5 15V5a2 2 0 0 1 2-2h10" /></svg>
      )}
    </button>
  );
}

function QrBtn({ onClick }: { onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label="نمایش QR"
      className="grid h-9 w-9 shrink-0 place-items-center rounded-lg border transition active:scale-95"
      style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)", color: "var(--ac-text)" }}
    >
      <svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <rect x="3" y="3" width="7" height="7" rx="1" /><rect x="14" y="3" width="7" height="7" rx="1" /><rect x="3" y="14" width="7" height="7" rx="1" /><path d="M14 14h3v3M20 14v0M17 20h0M20 17v3" />
      </svg>
    </button>
  );
}

function Stat({ label, value, hint, danger }: { label: string; value: string; hint?: string; danger?: boolean }) {
  return (
    <div className="rounded-2xl border p-4 text-center" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>
      <p className="text-[11px]" style={{ color: "var(--ac-muted)" }}>{label}</p>
      <p className="mt-1.5 text-[18px] font-black leading-none" style={{ color: danger ? "#E05050" : "var(--ac-title)" }}>{value}</p>
      {hint && <p className="mt-1.5 text-[11px]" style={{ color: "var(--ac-muted)" }}>{hint}</p>}
    </div>
  );
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <div className="mb-3 flex items-center gap-3">
      <span className="h-px flex-1" style={{ background: "var(--ac-divider)" }} />
      <span className="text-[13px] font-black" style={{ color: "var(--ac-title)" }}>{children}</span>
      <span className="h-px flex-1" style={{ background: "var(--ac-divider)" }} />
    </div>
  );
}

const PROTO_COLOR: Record<string, string> = { vless: "#4C8DFF", vmess: "#8A52FF", trojan: "#22B573", ss: "#F4A43A" };

function QrModal({ value, title, onClose }: { value: string; title: string; onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);
  return (
    <div className="fixed inset-0 z-[80] grid place-items-center p-4" dir="rtl">
      <div onClick={onClose} className="absolute inset-0 bg-black/70 backdrop-blur-sm" />
      <div className="relative w-full max-w-[320px] rounded-3xl border p-6 text-center" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>
        <p className="mb-4 truncate text-[13px] font-black" style={{ color: "var(--ac-title)" }}>{title}</p>
        <div className="mx-auto w-fit rounded-2xl bg-white p-4">
          <QRCodeSVG value={value} size={210} level="M" />
        </div>
        <p className="mt-4 text-[11px] leading-6" style={{ color: "var(--ac-muted)" }}>با دوربین برنامه‌ی خود این کد را اسکن کنید.</p>
        <button type="button" onClick={onClose} className="mt-4 h-10 w-full rounded-xl text-[13px] font-black text-white transition active:scale-95" style={{ background: "var(--ac-btn)" }}>بستن</button>
      </div>
    </div>
  );
}

function ConfigRow({ line, onQr }: { line: V2RayConfigLine; onQr: () => void }) {
  return (
    <div className="flex items-center gap-2 rounded-2xl border p-3" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>
      <div className="flex min-w-0 flex-1 items-center gap-2">
        {line.protocol && (
          <span className="shrink-0 rounded-md px-2 py-0.5 text-[10px] font-black uppercase" style={{ background: `${PROTO_COLOR[line.protocol.toLowerCase()] ?? "#8c8075"}22`, color: PROTO_COLOR[line.protocol.toLowerCase()] ?? "var(--ac-text)" }}>{line.protocol}</span>
        )}
        {line.network && (
          <span className="shrink-0 rounded-md px-2 py-0.5 text-[10px] font-black uppercase" style={{ background: "var(--ac-menu-hover)", color: "var(--ac-muted)" }}>{line.network}</span>
        )}
        <span className="truncate text-[12px] font-bold" style={{ color: "var(--ac-title)" }}>{line.remark || "کانفیگ"}</span>
      </div>
      <CopyBtn value={line.uri} label="کپی کانفیگ" />
      <QrBtn onClick={onQr} />
    </div>
  );
}

export default function ConfigView({ token }: { token: string }) {
  const [config, setConfig] = useState<V2RayConfig | null>(null);
  const [state, setState] = useState<"loading" | "ready" | "missing">("loading");
  const [qr, setQr] = useState<{ value: string; title: string } | null>(null);

  useEffect(() => {
    let alive = true;
    const load = (first: boolean) => api.v2rayConfig.get(token)
      .then((c) => { if (alive) { setConfig(c); setState("ready"); } })
      .catch(() => { if (alive && first) setState("missing"); });

    load(true);
    // Only poll while the page is actually being looked at — a link left open in a background tab shouldn't
    // keep asking the subscription server for numbers nobody is reading. Coming back refreshes at once.
    const tick = () => { if (document.visibilityState === "visible") load(false); };
    const id = setInterval(tick, REFRESH_MS);
    const onVisible = () => { if (document.visibilityState === "visible") load(false); };
    document.addEventListener("visibilitychange", onVisible);
    return () => { alive = false; clearInterval(id); document.removeEventListener("visibilitychange", onVisible); };
  }, [token]);

  if (state === "loading") {
    return <div className="grid min-h-[70vh] place-items-center"><span className="h-8 w-8 animate-spin rounded-full border-2 border-[rgba(166,102,45,0.25)] border-t-[#FF5A1F]" /></div>;
  }

  if (state === "missing" || !config) {
    return (
      <div className="grid min-h-[70vh] place-items-center px-4 text-center">
        <div>
          <p className="text-[18px] font-black" style={{ color: "var(--ac-title)" }}>این سرویس پیدا نشد</p>
          <p className="mt-2 text-[13px] leading-7" style={{ color: "var(--ac-muted)" }}>ممکن است لینک ناقص کپی شده باشد یا این سرویس دیگر فعال نباشد.</p>
        </div>
      </div>
    );
  }

  // Defensive: this page is a public link, so a response missing the list must degrade rather than blank out.
  const configs = config.configs ?? [];
  const unlimited = config.totalBytes <= 0;
  const usedRatio = unlimited ? 0 : Math.min(1, config.usedBytes / config.totalBytes);
  const remaining = unlimited ? 0 : Math.max(0, config.totalBytes - config.usedBytes);
  const expired = config.remainingDays !== null && config.remainingDays <= 0;
  const live = config.active && !expired;

  return (
    <div className="mx-auto w-full max-w-[560px] px-4 py-6 sm:py-9">
      {/* header */}
      <div className="rounded-[24px] border p-5 sm:p-6" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)", boxShadow: "var(--ac-panel-shadow)" }}>
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="text-[12px]" style={{ color: "var(--ac-muted)" }}>سرویس شما</p>
            <h1 className="mt-1 truncate text-[22px] font-black" style={{ color: "var(--ac-title)" }}>{config.server}{config.flag ? ` ${config.flag}` : ""}</h1>
          </div>
          <span className="shrink-0 rounded-full px-3 py-1.5 text-[12px] font-black" style={live ? { background: "rgba(34,181,115,0.14)", color: "#22B573" } : { background: "rgba(224,80,80,0.14)", color: "#E05050" }}>
            {expired ? "منقضی شده" : config.active ? "فعال" : "غیرفعال"}
          </span>
        </div>

        {/* usage bar */}
        <div className="mt-5">
          <div className="flex items-end justify-between gap-3">
            <p className="text-[13px]" style={{ color: "var(--ac-muted)" }}>
              <span className="text-[20px] font-black" style={{ color: "var(--ac-title)" }}>{formatBytes(config.usedBytes)}</span>
              <span className="mr-1">مصرف شده</span>
            </p>
            <p className="text-[12px] font-bold" style={{ color: "var(--ac-muted)" }}>{unlimited ? "حجم نامحدود" : `از ${formatBytes(config.totalBytes)}`}</p>
          </div>
          {!unlimited && (
            <div className="mt-2 h-2.5 overflow-hidden rounded-full" style={{ background: "var(--ac-divider)" }}>
              <div className="h-full rounded-full transition-[width] duration-700" style={{ width: `${Math.round(usedRatio * 100)}%`, background: usedRatio > 0.9 ? "#E05050" : "linear-gradient(90deg,#FF8A2B,#FF3D2E)" }} />
            </div>
          )}
        </div>

        <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Stat label="باقی‌مانده" value={unlimited ? "نامحدود" : formatBytes(remaining)} />
          <Stat label="دانلود" value={formatBytes(config.downBytes)} />
          <Stat label="زمان باقی‌مانده" value={config.remainingDays === null ? "نامحدود" : `${toFa(config.remainingDays)} روز`} danger={config.remainingDays !== null && config.remainingDays <= 3} hint={config.expiresAtUtc ? faDate(config.expiresAtUtc) : undefined} />
          <Stat label="محدودیت کاربر" value={config.ipLimit > 0 ? `${toFa(config.ipLimit)} کاربر` : "نامحدود"} />
        </div>

        {!config.statsLive && (
          <p className="mt-4 rounded-xl border border-amber-500/30 bg-amber-500/[0.08] px-3.5 py-2.5 text-[12px] leading-6 text-amber-600">
            در حال حاضر آمار زنده‌ی مصرف در دسترس نیست؛ مشخصات پلن شما نمایش داده می‌شود.
          </p>
        )}
      </div>

      {/* subscription */}
      {config.subUrl && (
        <div className="mt-5">
          <SectionTitle>لینک اشتراک</SectionTitle>
          <div className="flex items-center gap-2 rounded-2xl border p-3.5" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>
            <span className="shrink-0 rounded-md px-2 py-0.5 text-[10px] font-black text-white" style={{ background: "#22B573" }}>SUB</span>
            <span dir="ltr" className="min-w-0 flex-1 truncate font-mono text-[12px]" style={{ color: "var(--ac-text)" }}>{config.subId || config.subUrl}</span>
            <CopyBtn value={config.subUrl} label="کپی لینک اشتراک" />
            <QrBtn onClick={() => setQr({ value: config.subUrl, title: "لینک اشتراک" })} />
          </div>
          <p className="mt-2 px-1 text-[11px] leading-6" style={{ color: "var(--ac-muted)" }}>
            بهترین راه: این لینک را در برنامه وارد کنید تا همه‌ی کانفیگ‌ها یکجا و همیشه به‌روز بمانند.
          </p>
        </div>
      )}

      {/* configs */}
      {configs.length > 0 && (
        <div className="mt-5">
          <SectionTitle>کانفیگ‌ها</SectionTitle>
          <div className="mb-2 flex items-center gap-2 rounded-2xl border p-3.5" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)" }}>
            <span className="min-w-0 flex-1 text-[12px] font-black" style={{ color: "var(--ac-title)" }}>کپی همه‌ی کانفیگ‌ها</span>
            <CopyBtn value={configs.map((c) => c.uri).join("\n")} label="کپی همه" />
          </div>
          <div className="space-y-2">
            {configs.map((line, i) => (
              <ConfigRow key={`${line.uri}-${i}`} line={line} onQr={() => setQr({ value: line.uri, title: line.remark || "کانفیگ" })} />
            ))}
          </div>
        </div>
      )}

      <p className="mt-6 text-center text-[11px]" style={{ color: "var(--ac-muted)" }}>تاریخ خرید: {faDate(config.createdAtUtc)}</p>

      {qr && <QrModal value={qr.value} title={qr.title} onClose={() => setQr(null)} />}
    </div>
  );
}
