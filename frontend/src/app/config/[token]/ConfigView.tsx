"use client";

import { useEffect, useState } from "react";
import { QRCodeSVG } from "qrcode.react";
import { api } from "@/lib/api";
import { toFa } from "@/lib/format";
import type { V2RayConfig } from "@/lib/types";

// Bytes the panel counts, in the units a customer reads.
function formatBytes(bytes: number): string {
  if (bytes <= 0) return "۰";
  const units = ["بایت", "کیلوبایت", "مگابایت", "گیگابایت", "ترابایت"];
  const i = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  const value = bytes / Math.pow(1024, i);
  return `${toFa(value >= 10 || i === 0 ? Math.round(value) : Number(value.toFixed(1)))} ${units[i]}`;
}

function faDate(iso: string | null): string {
  if (!iso) return "—";
  try { return new Date(iso).toLocaleDateString("fa-IR", { year: "numeric", month: "long", day: "numeric" }); }
  catch { return "—"; }
}

/** A labelled figure — the page is mostly these, so they share one shape. */
function Stat({ label, value, hint, accent }: { label: string; value: string; hint?: string; accent?: boolean }) {
  return (
    <div className="rounded-2xl border p-4 text-center" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>
      <p className="text-[11px]" style={{ color: "var(--ac-muted)" }}>{label}</p>
      <p className="mt-1.5 text-[19px] font-black leading-none" style={{ color: accent ? "#F2551F" : "var(--ac-title)" }}>{value}</p>
      {hint && <p className="mt-1.5 text-[11px]" style={{ color: "var(--ac-muted)" }}>{hint}</p>}
    </div>
  );
}

/** A value the customer needs to copy, with the copy affordance built in. */
function CopyRow({ label, value, mono = true }: { label: string; value: string; mono?: boolean }) {
  const [copied, setCopied] = useState(false);
  if (!value) return null;
  return (
    <div className="rounded-2xl border p-4" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>
      <div className="flex items-center justify-between gap-3">
        <p className="text-[12px] font-bold" style={{ color: "var(--ac-muted)" }}>{label}</p>
        <button
          type="button"
          onClick={() => { navigator.clipboard?.writeText(value).then(() => { setCopied(true); setTimeout(() => setCopied(false), 1800); }).catch(() => {}); }}
          className="shrink-0 rounded-lg px-3 py-1.5 text-[11px] font-black text-white transition active:scale-95"
          style={{ background: copied ? "#22B573" : "var(--ac-btn)" }}
        >
          {copied ? "کپی شد ✓" : "کپی"}
        </button>
      </div>
      <p dir="ltr" className={`mt-2 break-all text-[12px] leading-6 ${mono ? "font-mono" : ""}`} style={{ color: "var(--ac-text)" }}>{value}</p>
    </div>
  );
}

const APPS: { os: string; names: string[] }[] = [
  { os: "اندروید", names: ["v2rayNG", "Hiddify", "NekoBox"] },
  { os: "آی‌او‌اس", names: ["Streisand", "FoXray", "V2Box"] },
  { os: "ویندوز", names: ["v2rayN", "Hiddify", "NekoRay"] },
];

export default function ConfigView({ token }: { token: string }) {
  const [config, setConfig] = useState<V2RayConfig | null>(null);
  const [state, setState] = useState<"loading" | "ready" | "missing">("loading");

  useEffect(() => {
    let alive = true;
    api.v2rayConfig.get(token)
      .then((c) => { if (alive) { setConfig(c); setState("ready"); } })
      .catch(() => { if (alive) setState("missing"); });
    return () => { alive = false; };
  }, [token]);

  if (state === "loading") {
    return (
      <div className="grid min-h-[60vh] place-items-center">
        <span className="h-8 w-8 animate-spin rounded-full border-2 border-[rgba(166,102,45,0.25)] border-t-[#FF5A1F]" />
      </div>
    );
  }

  if (state === "missing" || !config) {
    return (
      <div className="grid min-h-[60vh] place-items-center px-4 text-center">
        <div>
          <p className="text-[18px] font-black" style={{ color: "var(--ac-title)" }}>این سرویس پیدا نشد</p>
          <p className="mt-2 text-[13px] leading-7" style={{ color: "var(--ac-muted)" }}>
            ممکن است لینک ناقص کپی شده باشد یا این سرویس دیگر فعال نباشد.
          </p>
        </div>
      </div>
    );
  }

  const unlimitedVolume = config.totalBytes <= 0;
  const usedRatio = unlimitedVolume ? 0 : Math.min(1, config.usedBytes / config.totalBytes);
  const expired = config.remainingDays !== null && config.remainingDays <= 0;
  const live = config.active && !expired;

  return (
    <div className="mx-auto w-full max-w-[760px] px-4 py-6 sm:py-10">
      {/* identity + state */}
      <div className="rounded-[22px] border p-5 sm:p-6" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)", boxShadow: "var(--ac-panel-shadow)" }}>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="min-w-0">
            <p className="text-[12px]" style={{ color: "var(--ac-muted)" }}>سرویس شما</p>
            <h1 className="mt-1 truncate text-[20px] font-black sm:text-[24px]" style={{ color: "var(--ac-title)" }}>
              {config.server}{config.flag ? ` ${config.flag}` : ""}
            </h1>
          </div>
          <span
            className="shrink-0 rounded-full px-3 py-1.5 text-[12px] font-black"
            style={live
              ? { background: "rgba(34,181,115,0.14)", color: "#22B573" }
              : { background: "rgba(224,80,80,0.14)", color: "#E05050" }}
          >
            {expired ? "منقضی شده" : config.active ? "فعال" : "غیرفعال"}
          </span>
        </div>

        <div className="mt-4 flex flex-wrap gap-2 text-[11px]">
          {config.protocol && <span className="rounded-lg px-2.5 py-1 font-bold" style={{ background: "var(--ac-menu-hover)", color: "var(--ac-text)" }}>{config.protocol}</span>}
          {config.network && <span className="rounded-lg px-2.5 py-1 font-bold" style={{ background: "var(--ac-menu-hover)", color: "var(--ac-text)" }}>{config.network}</span>}
          {config.online && <span className="rounded-lg px-2.5 py-1 font-black" style={{ background: "rgba(34,181,115,0.14)", color: "#22B573" }}>هم‌اکنون متصل</span>}
        </div>

        {!config.statsLive && (
          <p className="mt-4 rounded-xl border border-amber-500/30 bg-amber-500/[0.08] px-3.5 py-2.5 text-[12px] leading-6 text-amber-600">
            در حال حاضر آمار زنده‌ی مصرف در دسترس نیست؛ مشخصات پلن شما نمایش داده می‌شود.
          </p>
        )}
      </div>

      {/* usage */}
      <div className="mt-4 rounded-[22px] border p-5 sm:p-6" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)", boxShadow: "var(--ac-panel-shadow)" }}>
        <div className="flex items-end justify-between gap-3">
          <div>
            <p className="text-[12px]" style={{ color: "var(--ac-muted)" }}>مصرف شده</p>
            <p className="mt-1 text-[24px] font-black leading-none" style={{ color: "var(--ac-title)" }}>{formatBytes(config.usedBytes)}</p>
          </div>
          <p className="text-[13px] font-bold" style={{ color: "var(--ac-muted)" }}>
            {unlimitedVolume ? "از حجم نامحدود" : `از ${formatBytes(config.totalBytes)}`}
          </p>
        </div>

        {!unlimitedVolume && (
          <div className="mt-3 h-2.5 overflow-hidden rounded-full" style={{ background: "var(--ac-divider)" }}>
            <div
              className="h-full rounded-full transition-[width] duration-700"
              style={{ width: `${Math.round(usedRatio * 100)}%`, background: usedRatio > 0.9 ? "#E05050" : "linear-gradient(90deg,#FF8A2B,#FF3D2E)" }}
            />
          </div>
        )}

        <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Stat label="دانلود" value={formatBytes(config.downBytes)} />
          <Stat label="آپلود" value={formatBytes(config.upBytes)} />
          <Stat
            label="زمان باقی‌مانده"
            value={config.remainingDays === null ? "نامحدود" : `${toFa(config.remainingDays)} روز`}
            accent={config.remainingDays !== null && config.remainingDays <= 3}
            hint={config.expiresAtUtc ? faDate(config.expiresAtUtc) : undefined}
          />
          <Stat label="محدودیت کاربر" value={config.ipLimit > 0 ? `${toFa(config.ipLimit)} کاربر` : "نامحدود"} />
        </div>
      </div>

      {/* connect */}
      {config.subUrl && (
        <div className="mt-4 rounded-[22px] border p-5 sm:p-6" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)", boxShadow: "var(--ac-panel-shadow)" }}>
          <h2 className="text-[16px] font-black" style={{ color: "var(--ac-title)" }}>اتصال</h2>
          <p className="mt-1 text-[12px] leading-6" style={{ color: "var(--ac-muted)" }}>
            لینک زیر را در برنامه‌ی خود وارد کنید، یا کد را با دوربین برنامه اسکن کنید.
          </p>

          <div className="mt-4 flex flex-col items-center gap-4 sm:flex-row sm:items-start">
            <div className="shrink-0 rounded-2xl bg-white p-3">
              <QRCodeSVG value={config.subUrl} size={148} level="M" />
            </div>
            <div className="w-full space-y-3">
              <CopyRow label="لینک اشتراک" value={config.subUrl} />
              <CopyRow label="شناسه اکانت" value={config.uuid} />
            </div>
          </div>

          <div className="mt-5 grid gap-3 sm:grid-cols-3">
            {APPS.map((a) => (
              <div key={a.os} className="rounded-2xl border p-3.5" style={{ borderColor: "var(--ac-panel-border)" }}>
                <p className="text-[12px] font-black" style={{ color: "var(--ac-title)" }}>{a.os}</p>
                <p className="mt-1.5 text-[12px] leading-6" style={{ color: "var(--ac-muted)" }}>{a.names.join(" · ")}</p>
              </div>
            ))}
          </div>
        </div>
      )}

      <p className="mt-5 text-center text-[11px]" style={{ color: "var(--ac-muted)" }}>
        تاریخ خرید: {faDate(config.createdAtUtc)}
      </p>
    </div>
  );
}
