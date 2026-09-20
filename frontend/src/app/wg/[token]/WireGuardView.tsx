"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { QRCodeSVG } from "qrcode.react";
import { api } from "@/lib/api";
import { toFa, formatToman } from "@/lib/format";
import { addToCart } from "@/lib/cart";
import { useAuth } from "@/lib/auth";
import { productPath } from "@/lib/seo";
import type { V2RayStatus, WireGuardConfig, WireGuardProfile, WireGuardRenewalPlan, WireGuardRenewals } from "@/lib/types";

// The customer's page for one W-UI service: the twin of /config/[token] for V2Ray. Same layout, same
// polling, same renew panel; what differs is what the service hands out — one configuration FILE per device
// (imported or scanned as a QR) instead of a list of share URIs.

const REFRESH_MS = 20000;

const STATUS: Record<V2RayStatus, { label: string; tone: "ok" | "warn" | "bad"; note: string }> = {
  active: { label: "فعال", tone: "ok", note: "" },
  expired: { label: "پایان اعتبار زمانی", tone: "bad", note: "مدت این سرویس به پایان رسیده است. با تمدید، همین کانفیگ‌ها دوباره فعال می‌شوند." },
  depleted: { label: "اتمام حجم", tone: "bad", note: "حجم این سرویس تمام شده است. با تمدید، حجم تازه روی همین کانفیگ‌ها اعمال می‌شود." },
  disabled: { label: "غیرفعال", tone: "warn", note: "این سرویس روی سرور غیرفعال است. اگر تمدید کرده‌اید، چند دقیقه صبر کنید یا با پشتیبانی تماس بگیرید." },
  removed: { label: "حذف شده", tone: "bad", note: "این سرویس پس از پایان مهلت تمدید از سرور حذف شده است و دیگر قابل تمدید نیست." },
};

// Which app opens which file. AmneziaWG is WireGuard with obfuscation, and the official WireGuard app does
// not understand it — the single most common support question, so the page says so up front.
const PROTO: Record<string, { label: string; color: string; apps: string }> = {
  wireguard: { label: "WireGuard", color: "#4C8DFF", apps: "WireGuard (رسمی)، V2Box، Hiddify" },
  amneziawg: { label: "AmneziaWG", color: "#8A52FF", apps: "AmneziaVPN (برنامه‌ی رسمی WireGuard این کانفیگ را نمی‌شناسد)" },
  openvpn: { label: "OpenVPN", color: "#F4A43A", apps: "OpenVPN Connect" },
};

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

function IconBtn({ onClick, label, done, children }: { onClick: () => void; label: string; done?: boolean; children: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      className="grid h-9 w-9 shrink-0 place-items-center rounded-lg border transition active:scale-95"
      style={{ borderColor: "var(--ac-panel-border)", background: done ? "#22B573" : "var(--ac-panel-bg)", color: done ? "#fff" : "var(--ac-text)" }}
    >
      {children}
    </button>
  );
}

function CopyBtn({ value, label }: { value: string; label?: string }) {
  const [done, copy] = useCopy();
  return (
    <IconBtn onClick={() => copy(value)} label={label ?? "کپی"} done={done}>
      {done ? (
        <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><path d="M20 6 9 17l-5-5" /></svg>
      ) : (
        <svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round"><rect x="9" y="9" width="11" height="11" rx="2" /><path d="M5 15V5a2 2 0 0 1 2-2h10" /></svg>
      )}
    </IconBtn>
  );
}

function QrBtn({ onClick }: { onClick: () => void }) {
  return (
    <IconBtn onClick={onClick} label="نمایش QR">
      <svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <rect x="3" y="3" width="7" height="7" rx="1" /><rect x="14" y="3" width="7" height="7" rx="1" /><rect x="3" y="14" width="7" height="7" rx="1" /><path d="M14 14h3v3M20 14v0M17 20h0M20 17v3" />
      </svg>
    </IconBtn>
  );
}

// Downloads the profile as the file the panel named it — what a desktop app or a router wants.
function DownloadBtn({ profile }: { profile: WireGuardProfile }) {
  function download() {
    const blob = new Blob([profile.body], { type: "text/plain;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = profile.filename || `${profile.deviceName || "device"}.conf`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  return (
    <IconBtn onClick={download} label="دانلود فایل کانفیگ">
      <svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round"><path d="M12 3v12M6 11l6 6 6-6" /><path d="M5 21h14" /></svg>
    </IconBtn>
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

function QrModal({ value, title, onClose }: { value: string; title: string; onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);
  // A whole WireGuard profile is a few hundred bytes of text; the QR carries it directly, which is exactly
  // what the WireGuard and Amnezia apps' "scan from QR" expects.
  return (
    <div className="fixed inset-0 z-[80] grid place-items-center p-4" dir="rtl">
      <div onClick={onClose} className="absolute inset-0 bg-black/70 backdrop-blur-sm" />
      <div className="relative w-full max-w-[340px] rounded-3xl border p-6 text-center" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>
        <p className="mb-4 truncate text-[13px] font-black" style={{ color: "var(--ac-title)" }}>{title}</p>
        <div className="mx-auto w-fit rounded-2xl bg-white p-4">
          <QRCodeSVG value={value} size={240} level="L" />
        </div>
        <p className="mt-4 text-[11px] leading-6" style={{ color: "var(--ac-muted)" }}>در برنامه «+» را بزنید و «اسکن QR» را انتخاب کنید.</p>
        <button type="button" onClick={onClose} className="mt-4 h-10 w-full rounded-xl text-[13px] font-black text-white transition active:scale-95" style={{ background: "var(--ac-btn)" }}>بستن</button>
      </div>
    </div>
  );
}

function ProfileRow({ profile, onQr }: { profile: WireGuardProfile; onQr: () => void }) {
  const proto = PROTO[profile.protocol] ?? { label: profile.protocol || "کانفیگ", color: "#8c8075", apps: "" };
  const isOvpn = profile.protocol === "openvpn";
  return (
    <div className="rounded-2xl border p-3" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>
      <div className="flex items-center gap-2">
        <div className="flex min-w-0 flex-1 items-center gap-2">
          <span className="shrink-0 rounded-md px-2 py-0.5 text-[10px] font-black" style={{ background: `${proto.color}22`, color: proto.color }}>{proto.label}</span>
          <span className="truncate text-[12px] font-bold" style={{ color: "var(--ac-title)" }}>
            {profile.deviceName || "دستگاه"}{profile.interfaceName ? ` · ${profile.interfaceName}` : ""}
          </span>
        </div>
        <CopyBtn value={profile.body} label="کپی کانفیگ" />
        <DownloadBtn profile={profile} />
        {/* An OpenVPN profile is a certificate bundle far past what a phone camera reads; it is downloaded. */}
        {!isOvpn && <QrBtn onClick={onQr} />}
      </div>
      {isOvpn && (profile.username || profile.password) && (
        <div className="mt-2 grid grid-cols-2 gap-2">
          <div className="flex items-center gap-2 rounded-xl px-3 py-2" style={{ background: "var(--ac-menu-hover)" }}>
            <span className="text-[10px]" style={{ color: "var(--ac-muted)" }}>نام کاربری</span>
            <span dir="ltr" className="min-w-0 flex-1 truncate font-mono text-[12px]" style={{ color: "var(--ac-text)" }}>{profile.username}</span>
            <CopyBtn value={profile.username} label="کپی نام کاربری" />
          </div>
          <div className="flex items-center gap-2 rounded-xl px-3 py-2" style={{ background: "var(--ac-menu-hover)" }}>
            <span className="text-[10px]" style={{ color: "var(--ac-muted)" }}>گذرواژه</span>
            <span dir="ltr" className="min-w-0 flex-1 truncate font-mono text-[12px]" style={{ color: "var(--ac-text)" }}>{profile.password}</span>
            <CopyBtn value={profile.password} label="کپی گذرواژه" />
          </div>
        </div>
      )}
      {proto.apps && <p className="mt-2 px-1 text-[11px]" style={{ color: "var(--ac-muted)" }}>برنامه: {proto.apps}</p>}
    </div>
  );
}

function RenewPanel({ token, renewals }: { token: string; renewals: WireGuardRenewals }) {
  const { user, ready } = useAuth();
  const router = useRouter();
  const [picked, setPicked] = useState<number | null>(renewals.plans[0]?.id ?? null);

  if (!renewals.renewable) {
    return (
      <p className="rounded-2xl border p-4 text-center text-[12px] leading-6" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)", color: "var(--ac-muted)" }}>
        {renewals.reason || "تمدید این سرویس در حال حاضر ممکن نیست."}
      </p>
    );
  }

  const plan = renewals.plans.find((p) => p.id === picked) ?? renewals.plans[0];

  function renew() {
    if (!plan) return;
    // The same cart line as a V2Ray renewal: the checkout tells the two tokens apart.
    addToCart({
      productId: renewals.productId,
      name: `تمدید ${renewals.productName}`,
      image: "",
      price: plan.finalPrice,
      planId: plan.id,
      plan: plan.title,
      renewToken: token,
    });
    router.push("/cart");
  }

  return (
    <div className="rounded-2xl border p-4" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>
      <p className="text-[12px] leading-6" style={{ color: "var(--ac-muted)" }}>
        با تمدید، <b style={{ color: "var(--ac-title)" }}>همین کانفیگ‌ها</b> ادامه پیدا می‌کنند؛ کلیدها و لینک اشتراک شما عوض نمی‌شوند و نیازی به وارد کردن دوباره‌ی آن‌ها در برنامه نیست.
      </p>

      <div className="mt-3 space-y-2">
        {renewals.plans.map((p) => (
          <PlanOption key={p.id} plan={p} selected={p.id === plan?.id} onSelect={() => setPicked(p.id)} />
        ))}
      </div>

      {ready && !user ? (
        <Link href="/login" className="mt-4 grid h-11 place-items-center rounded-xl text-[13px] font-black text-white transition active:scale-95" style={{ background: "var(--ac-btn)" }}>
          برای تمدید وارد حساب خود شوید
        </Link>
      ) : (
        <button
          type="button"
          onClick={renew}
          disabled={!plan || !ready}
          className="mt-4 h-11 w-full rounded-xl text-[13px] font-black text-white transition active:scale-95 disabled:opacity-60"
          style={{ background: "var(--ac-btn)" }}
        >
          {plan ? `تمدید سرویس — ${formatToman(plan.finalPrice)}` : "تمدید سرویس"}
        </button>
      )}

      <Link href={productPath({ id: renewals.productId, name: renewals.productName })} className="mt-2 block text-center text-[11px] underline-offset-4 hover:underline" style={{ color: "var(--ac-muted)" }}>
        مشاهده‌ی همه‌ی پلن‌ها
      </Link>
    </div>
  );
}

function PlanOption({ plan, selected, onSelect }: { plan: WireGuardRenewalPlan; selected: boolean; onSelect: () => void }) {
  const volume = plan.volumeGb > 0 ? `${toFa(plan.volumeGb)} گیگابایت` : "حجم نامحدود";
  const duration = plan.durationDays > 0 ? `${toFa(plan.durationDays)} روز` : "بدون محدودیت زمانی";
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-pressed={selected}
      className="flex w-full items-center gap-3 rounded-xl border p-3 text-right transition"
      style={{
        borderColor: selected ? "#22B573" : "var(--ac-panel-border)",
        background: selected ? "rgba(34,181,115,0.08)" : "var(--ac-menu-hover)",
      }}
    >
      <span className="grid h-4 w-4 shrink-0 place-items-center rounded-full border-2" style={{ borderColor: selected ? "#22B573" : "var(--ac-divider)" }}>
        {selected && <span className="h-2 w-2 rounded-full" style={{ background: "#22B573" }} />}
      </span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-[13px] font-black" style={{ color: "var(--ac-title)" }}>{plan.title}</span>
        <span className="block text-[11px]" style={{ color: "var(--ac-muted)" }}>{volume} · {duration} · {toFa(plan.deviceLimit)} دستگاه</span>
      </span>
      <span className="shrink-0 text-[12px] font-black" style={{ color: "var(--ac-title)" }}>{formatToman(plan.finalPrice)}</span>
    </button>
  );
}

export default function WireGuardView({ token }: { token: string }) {
  const [config, setConfig] = useState<WireGuardConfig | null>(null);
  const [state, setState] = useState<"loading" | "ready" | "missing">("loading");
  const [qr, setQr] = useState<{ value: string; title: string } | null>(null);
  const [renewals, setRenewals] = useState<WireGuardRenewals | null>(null);

  useEffect(() => {
    let alive = true;
    api.wireguardConfig.renewals(token)
      .then((r) => { if (alive) setRenewals(r); })
      .catch(() => { /* the page still works without the renew panel */ });
    return () => { alive = false; };
  }, [token]);

  useEffect(() => {
    let alive = true;
    const load = (first: boolean) => api.wireguardConfig.get(token)
      .then((c) => { if (alive) { setConfig(c); setState("ready"); } })
      .catch(() => { if (alive && first) setState("missing"); });

    load(true);
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

  const profiles = config.profiles ?? [];
  const unlimited = config.totalBytes <= 0;
  const usedRatio = unlimited ? 0 : Math.min(1, config.usedBytes / config.totalBytes);
  const remaining = unlimited ? 0 : Math.max(0, config.totalBytes - config.usedBytes);
  const status = STATUS[config.status] ?? STATUS.disabled;
  const live = config.status === "active";
  const badgeStyle = live
    ? { background: "rgba(34,181,115,0.14)", color: "#22B573" }
    : status.tone === "warn"
      ? { background: "rgba(244,164,58,0.16)", color: "#C77A11" }
      : { background: "rgba(224,80,80,0.14)", color: "#E05050" };

  return (
    <div className="mx-auto w-full max-w-[560px] px-4 py-6 sm:py-9">
      <div className="rounded-[24px] border p-5 sm:p-6" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)", boxShadow: "var(--ac-panel-shadow)" }}>
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="text-[12px]" style={{ color: "var(--ac-muted)" }}>سرویس شما</p>
            <h1 className="mt-1 truncate text-[22px] font-black" style={{ color: "var(--ac-title)" }}>{config.server}{config.flag ? ` ${config.flag}` : ""}</h1>
            {live && (
              <p className="mt-1.5 flex items-center gap-1.5 text-[11px] font-bold" style={{ color: config.onlineNow > 0 ? "#22B573" : "var(--ac-muted)" }}>
                <span className="h-1.5 w-1.5 rounded-full" style={{ background: config.onlineNow > 0 ? "#22B573" : "var(--ac-divider)" }} />
                {config.onlineNow > 0 ? `هم‌اکنون ${toFa(config.onlineNow)} دستگاه متصل` : "در حال حاضر متصل نیست"}
              </p>
            )}
          </div>
          <span className="shrink-0 rounded-full px-3 py-1.5 text-[12px] font-black" style={badgeStyle}>{status.label}</span>
        </div>

        {status.note && (
          <p className="mt-4 rounded-xl px-3.5 py-2.5 text-[12px] leading-6" style={{ background: "var(--ac-menu-hover)", color: "var(--ac-text)" }}>{status.note}</p>
        )}

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

        <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3">
          <Stat label="باقی‌مانده" value={unlimited ? "نامحدود" : formatBytes(remaining)} danger={!unlimited && remaining <= 0} />
          <Stat
            label="زمان باقی‌مانده"
            value={config.remainingDays === null ? "نامحدود" : config.remainingDays <= 0 ? "به پایان رسید" : `${toFa(config.remainingDays)} روز`}
            danger={config.remainingDays !== null && config.remainingDays <= 3}
            hint={config.expiresAtUtc ? faDate(config.expiresAtUtc) : undefined}
          />
          <Stat label="تعداد دستگاه" value={`${toFa(Math.max(1, config.deviceLimit))} دستگاه`} />
        </div>

        {config.renewCount > 0 && (
          <p className="mt-3 text-center text-[11px]" style={{ color: "var(--ac-muted)" }}>
            {toFa(config.renewCount)} بار تمدید شده{config.lastRenewedAtUtc ? ` · آخرین تمدید: ${faDate(config.lastRenewedAtUtc)}` : ""}
          </p>
        )}

        {!config.statsLive && config.status !== "removed" && (
          <p className="mt-4 rounded-xl border border-amber-500/30 bg-amber-500/[0.08] px-3.5 py-2.5 text-[12px] leading-6 text-amber-600">
            در حال حاضر آمار زنده‌ی مصرف در دسترس نیست؛ مشخصات پلن شما نمایش داده می‌شود.
          </p>
        )}
      </div>

      {renewals && config.status !== "removed" && (
        <div className="mt-5">
          <SectionTitle>تمدید سرویس</SectionTitle>
          <RenewPanel token={token} renewals={renewals} />
        </div>
      )}

      {config.subUrl && config.status !== "removed" && (
        <div className="mt-5">
          <SectionTitle>لینک اشتراک</SectionTitle>
          <div className="flex items-center gap-2 rounded-2xl border p-3.5" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>
            <span className="shrink-0 rounded-md px-2 py-0.5 text-[10px] font-black text-white" style={{ background: "#22B573" }}>SUB</span>
            <span dir="ltr" className="min-w-0 flex-1 truncate font-mono text-[12px]" style={{ color: "var(--ac-text)" }}>{config.subId || config.subUrl}</span>
            <CopyBtn value={config.subUrl} label="کپی لینک اشتراک" />
            <QrBtn onClick={() => setQr({ value: config.subUrl, title: "لینک اشتراک" })} />
          </div>
          <p className="mt-2 px-1 text-[11px] leading-6" style={{ color: "var(--ac-muted)" }}>
            این لینک را در برنامه‌هایی که لینک اشتراک می‌پذیرند (V2Box، Hiddify) وارد کنید تا کانفیگ‌ها همیشه به‌روز بمانند. برنامه‌ی رسمی WireGuard لینک نمی‌گیرد؛ برای آن از QR یا فایل هر دستگاه در پایین استفاده کنید.
          </p>
        </div>
      )}

      {profiles.length > 0 && config.status !== "removed" && (
        <div className="mt-5">
          <SectionTitle>کانفیگ دستگاه‌ها</SectionTitle>
          <div className="space-y-2">
            {profiles.map((p) => (
              <ProfileRow key={p.deviceId} profile={p} onQr={() => setQr({ value: p.body, title: p.deviceName || "کانفیگ" })} />
            ))}
          </div>
          <p className="mt-2 px-1 text-[11px] leading-6" style={{ color: "var(--ac-muted)" }}>
            هر کانفیگ برای یک دستگاه است؛ یک کانفیگ را روی دو دستگاه هم‌زمان نگذارید.
          </p>
        </div>
      )}

      <p className="mt-6 text-center text-[11px]" style={{ color: "var(--ac-muted)" }}>تاریخ خرید: {faDate(config.createdAtUtc)}</p>

      {qr && <QrModal value={qr.value} title={qr.title} onClose={() => setQr(null)} />}
    </div>
  );
}
