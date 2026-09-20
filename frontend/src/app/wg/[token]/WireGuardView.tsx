"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { QRCodeSVG } from "qrcode.react";
import { api } from "@/lib/api";
import { toFa, formatToman } from "@/lib/format";
import { addToCart } from "@/lib/cart";
import { useAuth } from "@/lib/auth";
import { productPath } from "@/lib/seo";
import type { V2RayStatus, WireGuardConfig, WireGuardProfile, WireGuardRenewalPlan, WireGuardRenewals } from "@/lib/types";

// The customer's page for one W-UI service, laid out the way the panel's own subscription page is — a
// branded card head, a hero with the live dot, three ring stats, the details table, the usage bar with its
// chips, the subscription row, the files grouped by tunnel with one line per device, and the app menus —
// in Phonix's own light/orange system, with the renew panel the shop adds.

const REFRESH_MS = 20000;

const STATUS: Record<V2RayStatus, { label: string; tone: "ok" | "warn" | "bad"; note: string }> = {
  active: { label: "فعال", tone: "ok", note: "" },
  expired: { label: "پایان اعتبار زمانی", tone: "bad", note: "مدت این سرویس به پایان رسیده است. با تمدید، همین کانفیگ‌ها دوباره فعال می‌شوند." },
  depleted: { label: "اتمام حجم", tone: "bad", note: "حجم این سرویس تمام شده است. با تمدید، حجم تازه روی همین کانفیگ‌ها اعمال می‌شود." },
  disabled: { label: "غیرفعال", tone: "warn", note: "این سرویس روی سرور غیرفعال است. اگر تمدید کرده‌اید، چند دقیقه صبر کنید یا با پشتیبانی تماس بگیرید." },
  removed: { label: "حذف شده", tone: "bad", note: "این سرویس پس از پایان مهلت تمدید از سرور حذف شده است و دیگر قابل تمدید نیست." },
};

// One tone per protocol, used for the tag on each tunnel group and the dot in the apps menu.
const PROTO: Record<string, { label: string; color: string; bg: string }> = {
  wireguard: { label: "WireGuard", color: "#0958d9", bg: "#e6f4ff" },
  amneziawg: { label: "AmneziaWG", color: "#531dab", bg: "#f9f0ff" },
  openvpn: { label: "OpenVPN", color: "#d46b08", bg: "#fff7e6" },
};
const protoOf = (p: string) => PROTO[p] ?? { label: p || "کانفیگ", color: "#6b6169", bg: "#f4f5f7" };

const OK = "#22B573";
const WARN = "#C77A11";
const BAD = "#E05050";

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

// "۱۲ روز" / "۵ ساعت" / "منقضی" — the chip beside the usage bar, as the panel shows it.
function expiryChip(iso: string | null): { text: string; tone: "ok" | "warn" | "bad" } | null {
  if (!iso) return null;
  const ms = new Date(iso).getTime() - Date.now();
  if (ms <= 0) return { text: "منقضی", tone: "bad" };
  const hours = ms / 3_600_000;
  if (hours < 24) return { text: `${toFa(Math.max(1, Math.round(hours)))} ساعت`, tone: "bad" };
  const days = Math.ceil(hours / 24);
  return { text: `${toFa(days)} روز`, tone: days <= 3 ? "warn" : "ok" };
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

// ── small parts ──────────────────────────────────────────────────────────────────────────────────

function Icon({ name, className = "h-4 w-4" }: { name: "copy" | "check" | "qr" | "download" | "chevron" | "android" | "apple" | "windows" | "mac" | "clock" | "bolt" | "brand"; className?: string }) {
  const p = { viewBox: "0 0 24 24", className, fill: "none", stroke: "currentColor", strokeWidth: 1.9, strokeLinecap: "round" as const, strokeLinejoin: "round" as const };
  switch (name) {
    case "copy": return <svg {...p}><rect x="9" y="9" width="11" height="11" rx="2" /><path d="M5 15V5a2 2 0 0 1 2-2h10" /></svg>;
    case "check": return <svg {...p} strokeWidth={2.5}><path d="M20 6 9 17l-5-5" /></svg>;
    case "qr": return <svg {...p}><rect x="3" y="3" width="7" height="7" rx="1" /><rect x="14" y="3" width="7" height="7" rx="1" /><rect x="3" y="14" width="7" height="7" rx="1" /><path d="M14 14h3v3M20 14v0M17 20h0M20 17v3" /></svg>;
    case "download": return <svg {...p}><path d="M12 3v12M6 11l6 6 6-6" /><path d="M5 21h14" /></svg>;
    case "chevron": return <svg {...p}><path d="m9 6 6 6-6 6" /></svg>;
    case "clock": return <svg {...p}><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></svg>;
    case "bolt": return <svg {...p}><path d="M13 2 4 14h7l-1 8 9-12h-7l1-8z" /></svg>;
    case "android": return <svg {...p}><path d="M6 10a6 6 0 0 1 12 0v7a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1v-7z" /><path d="M8 5 7 3M16 5l1-2M9 21v-3M15 21v-3M3 11v5M21 11v5" /><circle cx="9.5" cy="9.5" r=".6" fill="currentColor" /><circle cx="14.5" cy="9.5" r=".6" fill="currentColor" /></svg>;
    case "apple": return <svg {...p}><path d="M16 3c-1 1.3-2.5 2-3.6 1.8.1-1.4.9-2.7 2-3.5.3.7.5 1.3 1.6 1.7z" /><path d="M12.5 6.5c1.4 0 2.3-.9 4-.8 1.6.1 2.8.9 3.5 2.2-3 1.9-2.5 6 .5 7.2-.7 2-2.1 4.3-3.8 5.5-1.1.8-2.4-.4-3.9-.4s-2.9 1.3-4 .4C6 18.7 3.6 14.4 4.1 10.7c.3-2.7 2.3-4.7 4.6-4.9 1.4 0 2.6.7 3.8.7z" /></svg>;
    case "windows": return <svg {...p}><path d="M3 5.5 11 4.4v7.1H3zM12 4.2 21 3v8.5h-9zM3 12.5h8v7.1L3 18.5zM12 12.5h9V21l-9-1.2z" /></svg>;
    case "mac": return <svg {...p}><rect x="3" y="4" width="18" height="12" rx="2" /><path d="M8 20h8M12 16v4" /></svg>;
    case "brand": return <svg {...p}><path d="M12 3c2 3 5 4 5 8a5 5 0 0 1-10 0c0-2 1-3 1-3s.5 2 2 2c0-3-1-4 2-7z" /></svg>;
  }
}

function Tag({ tone = "grey", children, dir }: { tone?: "grey" | "green" | "red" | "orange" | "purple" | "blue" | "cyan"; children: React.ReactNode; dir?: "ltr" | "rtl" }) {
  const map: Record<string, [string, string, string]> = {
    grey: ["#f4f5f7", "#dcdee3", "var(--ac-text)"],
    green: ["#f6ffed", "#b7eb8f", "#389e0d"],
    red: ["#fff2f0", "#ffccc7", "#cf1322"],
    orange: ["#fff7e6", "#ffd591", "#d46b08"],
    purple: ["#f9f0ff", "#d3adf7", "#531dab"],
    blue: ["#e6f4ff", "#91caff", "#0958d9"],
    cyan: ["#e6fffb", "#87e8de", "#08979c"],
  };
  const [bg, line, ink] = map[tone];
  return (
    <span dir={dir} className="inline-flex items-center gap-1 whitespace-nowrap rounded-md border px-2 text-[11px] font-bold leading-5" style={{ background: bg, borderColor: line, color: ink }}>
      {children}
    </span>
  );
}

function SmBtn({ onClick, label, done, href, download, children }: { onClick?: () => void; label: string; done?: boolean; href?: string; download?: string; children: React.ReactNode }) {
  const cls = "grid h-8 w-8 shrink-0 place-items-center rounded-lg border transition active:scale-95";
  const style = { borderColor: done ? OK : "var(--ac-panel-border)", background: done ? OK : "var(--ac-panel-bg)", color: done ? "#fff" : "var(--ac-text)" };
  if (href) return <a href={href} download={download} aria-label={label} title={label} className={cls} style={style}>{children}</a>;
  return <button type="button" onClick={onClick} aria-label={label} title={label} className={cls} style={style}>{children}</button>;
}

function CopyBtn({ value, label = "کپی" }: { value: string; label?: string }) {
  const [done, copy] = useCopy();
  return <SmBtn onClick={() => copy(value)} label={label} done={done}>{done ? <Icon name="check" /> : <Icon name="copy" />}</SmBtn>;
}

function Ring({ percent, color, children }: { percent: number; color: string; children: React.ReactNode }) {
  const p = Math.max(0, Math.min(100, percent));
  return (
    <div className="relative grid h-12 w-12 shrink-0 place-items-center rounded-full text-[11px] font-black" style={{ background: `conic-gradient(${color} ${p}%, var(--ac-divider) 0)` }}>
      <span className="absolute inset-[6px] rounded-full" style={{ background: "var(--ac-panel-bg)" }} />
      <span className="relative" dir="ltr">{children}</span>
    </div>
  );
}

function Quick({ ring, k, v, s }: { ring: React.ReactNode; k: string; v: React.ReactNode; s: React.ReactNode }) {
  return (
    <div className="flex min-w-0 flex-1 items-center gap-3 rounded-2xl border px-3.5 py-3" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)" }}>
      {ring}
      <div className="min-w-0">
        <p className="text-[10px] font-bold uppercase tracking-wider" style={{ color: "var(--ac-muted)" }}>{k}</p>
        <p className="mt-0.5 truncate text-[15px] font-black" style={{ color: "var(--ac-title)" }}>{v}</p>
        <p className="truncate text-[11px]" style={{ color: "var(--ac-muted)" }}>{s}</p>
      </div>
    </div>
  );
}

function Divider({ children }: { children: React.ReactNode }) {
  return (
    <div className="my-4 flex items-center gap-3 text-[13px] font-black" style={{ color: "var(--ac-title)" }}>
      <span className="h-px flex-1" style={{ background: "var(--ac-divider)" }} />
      <span>{children}</span>
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
  return (
    <div className="fixed inset-0 z-[80] grid place-items-center p-4" dir="rtl" onClick={onClose}>
      <div className="absolute inset-0 bg-black/55 backdrop-blur-sm" />
      <div onClick={(e) => e.stopPropagation()} className="relative flex w-full max-w-[320px] flex-col items-center gap-3 rounded-2xl border p-4" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>
        <Tag tone="green">{title}</Tag>
        <div className="rounded-lg bg-white p-2"><QRCodeSVG value={value} size={240} level="L" /></div>
        <p className="text-[11px]" style={{ color: "var(--ac-muted)" }}>در برنامه «+» را بزنید و «اسکن QR» را انتخاب کنید · برای بستن بیرون کادر بزنید</p>
      </div>
    </div>
  );
}

// ── the files, grouped by tunnel, one collapsible line per device ────────────────────────────────

function DeviceRow({ profile, onQr }: { profile: WireGuardProfile; onQr: () => void }) {
  const [open, setOpen] = useState(false);
  const isOvpn = profile.protocol === "openvpn";
  const fileHref = useMemo(() => `data:text/plain;charset=utf-8,${encodeURIComponent(profile.body)}`, [profile.body]);
  return (
    <div className="border-t first:border-t-0" style={{ borderColor: "var(--ac-divider)" }}>
      <div className="flex items-center gap-2 px-4 py-2.5">
        <span className="min-w-0 flex-1 truncate text-[13px] font-black" style={{ color: "var(--ac-title)" }}>
          {profile.deviceName || "دستگاه"}
          {isOvpn && profile.username && <span dir="ltr" className="mr-2 font-mono text-[11px] font-normal" style={{ color: "var(--ac-muted)" }}>{profile.username}</span>}
        </span>
        <div className="flex shrink-0 gap-1">
          <CopyBtn value={profile.body} label="کپی کانفیگ" />
          <SmBtn href={fileHref} download={profile.filename || `${profile.deviceName || "device"}.conf`} label="دانلود فایل"><Icon name="download" /></SmBtn>
          {!isOvpn && <SmBtn onClick={onQr} label="QR"><Icon name="qr" /></SmBtn>}
          <SmBtn onClick={() => setOpen((v) => !v)} label={open ? "بستن" : "نمایش"}>
            <span className={`inline-block transition-transform ${open ? "rotate-90" : ""}`}><Icon name="chevron" /></span>
          </SmBtn>
        </div>
      </div>
      {open && (
        <div className="px-4 pb-3.5">
          {isOvpn && profile.password && (
            <div className="mb-2 flex items-center gap-2 rounded-xl px-3 py-2 text-[12px]" style={{ background: "var(--ac-menu-hover)" }}>
              <span style={{ color: "var(--ac-muted)" }}>گذرواژه</span>
              <span dir="ltr" className="min-w-0 flex-1 truncate font-mono" style={{ color: "var(--ac-text)" }}>{profile.password}</span>
              <CopyBtn value={profile.password} label="کپی گذرواژه" />
            </div>
          )}
          <pre dir="ltr" className="overflow-x-auto whitespace-pre-wrap break-all rounded-xl p-3 text-left font-mono text-[11px] leading-5" style={{ background: "var(--ac-menu-hover)", color: "var(--ac-text)" }}>{profile.body}</pre>
        </div>
      )}
    </div>
  );
}

function TunnelGroup({ name, protocol, devices, onQr }: { name: string; protocol: string; devices: WireGuardProfile[]; onQr: (p: WireGuardProfile) => void }) {
  const [open, setOpen] = useState(true);
  const proto = protoOf(protocol);
  return (
    <div className="rounded-xl border" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)" }}>
      <button type="button" onClick={() => setOpen((v) => !v)} className="flex w-full items-center gap-2 px-4 py-3 text-right">
        <span className={`inline-block transition-transform ${open ? "rotate-90" : ""}`} style={{ color: "var(--ac-muted)" }}><Icon name="chevron" className="h-3.5 w-3.5" /></span>
        <span className="rounded-md border px-2 text-[11px] font-black leading-5" style={{ background: proto.bg, borderColor: proto.color + "55", color: proto.color }}>{proto.label}</span>
        {name && <span className="min-w-0 truncate text-[12px]" style={{ color: "var(--ac-muted)" }} dir="ltr">{name}</span>}
        <span className="mr-auto text-[11px]" style={{ color: "var(--ac-muted)" }}>{devices.length > 1 ? `${toFa(devices.length)} دستگاه` : "۱ دستگاه"}</span>
      </button>
      {open && <div className="border-t" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>{devices.map((d) => <DeviceRow key={d.deviceId} profile={d} onQr={() => onQr(d)} />)}</div>}
    </div>
  );
}

// ── the app menus at the foot ────────────────────────────────────────────────────────────────────

type AppLink = { name: string; href: string; proto: string };
const APPS: Record<string, { icon: "android" | "apple" | "windows" | "mac"; label: string; links: AppLink[] }> = {
  android: { icon: "android", label: "Android", links: [
    { name: "WireGuard", href: "https://play.google.com/store/apps/details?id=com.wireguard.android", proto: "wireguard" },
    { name: "AmneziaWG", href: "https://play.google.com/store/apps/details?id=org.amnezia.awg", proto: "amneziawg" },
    { name: "AmneziaVPN", href: "https://play.google.com/store/apps/details?id=org.amnezia.vpn", proto: "amneziawg" },
    { name: "OpenVPN Connect", href: "https://play.google.com/store/apps/details?id=net.openvpn.openvpn", proto: "openvpn" },
  ] },
  ios: { icon: "apple", label: "iOS", links: [
    { name: "WireGuard", href: "https://apps.apple.com/app/wireguard/id1441195209", proto: "wireguard" },
    { name: "AmneziaWG", href: "https://apps.apple.com/app/amneziawg/id6478942365", proto: "amneziawg" },
    { name: "AmneziaVPN", href: "https://apps.apple.com/app/amneziavpn/id1600529900", proto: "amneziawg" },
    { name: "OpenVPN Connect", href: "https://apps.apple.com/app/openvpn-connect/id590379981", proto: "openvpn" },
  ] },
  windows: { icon: "windows", label: "Windows", links: [
    { name: "WireGuard", href: "https://www.wireguard.com/install/", proto: "wireguard" },
    { name: "AmneziaVPN", href: "https://amnezia.org/en/downloads", proto: "amneziawg" },
    { name: "OpenVPN Connect", href: "https://openvpn.net/client/", proto: "openvpn" },
  ] },
  mac: { icon: "mac", label: "macOS", links: [
    { name: "WireGuard", href: "https://apps.apple.com/app/wireguard/id1451685025", proto: "wireguard" },
    { name: "AmneziaVPN", href: "https://amnezia.org/en/downloads", proto: "amneziawg" },
    { name: "OpenVPN Connect", href: "https://openvpn.net/client/", proto: "openvpn" },
  ] },
};

function AppMenu({ id, protocols }: { id: keyof typeof APPS; protocols: Set<string> }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, [open]);
  const app = APPS[id];
  // Only the apps that open something this service actually hands out.
  const links = app.links.filter((l) => protocols.has(l.proto));
  if (links.length === 0) return null;
  return (
    <div ref={ref} className="relative flex-1 basis-[calc(50%-4px)]">
      <button type="button" onClick={() => setOpen((v) => !v)} className="flex h-11 w-full items-center justify-center gap-2 rounded-xl text-[13px] font-black text-white transition active:scale-[.98]" style={{ background: "var(--ac-btn)" }}>
        <Icon name={app.icon} className="h-[18px] w-[18px]" />
        {app.label}
        <span className={`inline-block transition-transform ${open ? "-rotate-90" : "rotate-90"}`}><Icon name="chevron" className="h-3.5 w-3.5" /></span>
      </button>
      {open && (
        <div className="absolute bottom-[calc(100%+6px)] left-1/2 z-10 min-w-[220px] -translate-x-1/2 rounded-xl border p-1 shadow-xl" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)" }}>
          {links.map((l) => (
            <a key={l.name} href={l.href} target="_blank" rel="noopener noreferrer" className="flex items-center gap-2 rounded-lg px-3 py-2 text-[13px] transition hover:bg-black/5" style={{ color: "var(--ac-title)" }}>
              <span className="h-2 w-2 rounded-full" style={{ background: protoOf(l.proto).color }} />
              {l.name}
            </a>
          ))}
        </div>
      )}
    </div>
  );
}

// ── renew ────────────────────────────────────────────────────────────────────────────────────────

function RenewPanel({ token, renewals }: { token: string; renewals: WireGuardRenewals }) {
  const { user, ready } = useAuth();
  const router = useRouter();
  const [picked, setPicked] = useState<number | null>(renewals.plans[0]?.id ?? null);

  if (!renewals.renewable) {
    return <p className="rounded-xl border p-4 text-center text-[12px] leading-6" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)", color: "var(--ac-muted)" }}>{renewals.reason || "تمدید این سرویس در حال حاضر ممکن نیست."}</p>;
  }
  const plan = renewals.plans.find((p) => p.id === picked) ?? renewals.plans[0];
  function renew() {
    if (!plan) return;
    addToCart({ productId: renewals.productId, name: `تمدید ${renewals.productName}`, image: "", price: plan.finalPrice, planId: plan.id, plan: plan.title, renewToken: token });
    router.push("/cart");
  }
  return (
    <div className="rounded-xl border p-4" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)" }}>
      <p className="text-[12px] leading-6" style={{ color: "var(--ac-muted)" }}>
        با تمدید، <b style={{ color: "var(--ac-title)" }}>همین کانفیگ‌ها</b> ادامه پیدا می‌کنند؛ کلیدها و لینک اشتراک عوض نمی‌شوند و چیزی را دوباره وارد نمی‌کنید.
      </p>
      <div className="mt-3 space-y-2">
        {renewals.plans.map((p) => <PlanOption key={p.id} plan={p} selected={p.id === plan?.id} onSelect={() => setPicked(p.id)} />)}
      </div>
      {ready && !user ? (
        <Link href="/login" className="mt-4 grid h-11 place-items-center rounded-xl text-[13px] font-black text-white" style={{ background: "var(--ac-btn)" }}>برای تمدید وارد حساب خود شوید</Link>
      ) : (
        <button type="button" onClick={renew} disabled={!plan || !ready} className="mt-4 h-11 w-full rounded-xl text-[13px] font-black text-white transition active:scale-[.98] disabled:opacity-60" style={{ background: "var(--ac-btn)" }}>
          {plan ? `تمدید سرویس — ${formatToman(plan.finalPrice)}` : "تمدید سرویس"}
        </button>
      )}
      <Link href={productPath({ id: renewals.productId, name: renewals.productName })} className="mt-2 block text-center text-[11px] underline-offset-4 hover:underline" style={{ color: "var(--ac-muted)" }}>مشاهده‌ی همه‌ی پلن‌ها</Link>
    </div>
  );
}

function PlanOption({ plan, selected, onSelect }: { plan: WireGuardRenewalPlan; selected: boolean; onSelect: () => void }) {
  const volume = plan.volumeGb > 0 ? `${toFa(plan.volumeGb)} گیگابایت` : "حجم نامحدود";
  const duration = plan.durationDays > 0 ? `${toFa(plan.durationDays)} روز` : "بدون محدودیت زمانی";
  return (
    <button type="button" onClick={onSelect} aria-pressed={selected} className="flex w-full items-center gap-3 rounded-xl border p-3 text-right transition" style={{ borderColor: selected ? OK : "var(--ac-panel-border)", background: selected ? "rgba(34,181,115,0.08)" : "var(--ac-panel-bg)" }}>
      <span className="grid h-4 w-4 shrink-0 place-items-center rounded-full border-2" style={{ borderColor: selected ? OK : "var(--ac-divider)" }}>{selected && <span className="h-2 w-2 rounded-full" style={{ background: OK }} />}</span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-[13px] font-black" style={{ color: "var(--ac-title)" }}>{plan.title}</span>
        <span className="block text-[11px]" style={{ color: "var(--ac-muted)" }}>{volume} · {duration} · {toFa(plan.deviceLimit)} دستگاه</span>
      </span>
      <span className="shrink-0 text-[12px] font-black" style={{ color: "var(--ac-title)" }}>{formatToman(plan.finalPrice)}</span>
    </button>
  );
}

// ── the page ─────────────────────────────────────────────────────────────────────────────────────

export default function WireGuardView({ token }: { token: string }) {
  const [config, setConfig] = useState<WireGuardConfig | null>(null);
  const [state, setState] = useState<"loading" | "ready" | "missing">("loading");
  const [qr, setQr] = useState<{ value: string; title: string } | null>(null);
  const [renewals, setRenewals] = useState<WireGuardRenewals | null>(null);
  const [subCopied, copySub] = useCopy();
  const [allCopied, copyAll] = useCopy();

  useEffect(() => {
    let alive = true;
    api.wireguardConfig.renewals(token).then((r) => { if (alive) setRenewals(r); }).catch(() => {});
    return () => { alive = false; };
  }, [token]);

  useEffect(() => {
    let alive = true;
    const load = (first: boolean) => api.wireguardConfig.get(token)
      .then((c) => { if (alive) { setConfig(c); setState("ready"); } })
      .catch(() => { if (alive && first) setState("missing"); });
    load(true);
    const id = setInterval(() => { if (document.visibilityState === "visible") load(false); }, REFRESH_MS);
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
  const percent = unlimited ? 0 : Math.min(100, (config.usedBytes / config.totalBytes) * 100);
  const remaining = unlimited ? 0 : Math.max(0, config.totalBytes - config.usedBytes);
  const status = STATUS[config.status] ?? STATUS.disabled;
  const live = config.status === "active";
  const removed = config.status === "removed";
  const barColor = percent >= 90 ? BAD : percent >= 75 ? WARN : OK;
  const chip = expiryChip(config.expiresAtUtc);
  const chipTone = chip?.tone === "bad" ? "red" : chip?.tone === "warn" ? "orange" : "green";
  const statusTag = live ? (unlimited ? <Tag tone="purple">نامحدود</Tag> : <Tag tone="green">فعال</Tag>) : <Tag tone={status.tone === "warn" ? "orange" : "red"}>{status.label}</Tag>;

  // Files grouped by tunnel, in the order the panel listed them.
  const groups = Array.from(profiles.reduce((m, p) => {
    const key = `${p.interfaceName}|${p.protocol}`;
    if (!m.has(key)) m.set(key, { name: p.interfaceName, protocol: p.protocol, devices: [] as WireGuardProfile[] });
    m.get(key)!.devices.push(p);
    return m;
  }, new Map<string, { name: string; protocol: string; devices: WireGuardProfile[] }>()).values());
  const protocols = new Set(profiles.map((p) => p.protocol));

  return (
    <div className="mx-auto w-full max-w-[600px] px-3 py-6 sm:py-9">
      <div className="overflow-hidden rounded-[20px] border" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)", boxShadow: "var(--ac-panel-shadow)" }}>
        {/* card head */}
        <div className="flex min-h-[52px] items-center gap-2 border-b px-4 sm:px-6" style={{ borderColor: "var(--ac-divider)" }}>
          <span className="grid h-7 w-7 place-items-center rounded-lg text-white" style={{ background: "var(--ac-btn)" }}><Icon name="brand" className="h-4 w-4" /></span>
          <span className="truncate text-[15px] font-black" style={{ color: "var(--ac-title)" }}>Phoenix Verify</span>
          <span className="hidden sm:inline"><Tag dir="ltr">{config.subId || config.name}</Tag></span>
          <span className="mr-auto">{statusTag}</span>
        </div>

        <div className="p-4 sm:p-6">
          {/* hero */}
          <div className="mb-4 flex items-center gap-3.5">
            <div className="grid h-[52px] w-[52px] shrink-0 place-items-center rounded-2xl text-[22px]" style={{ background: "var(--ac-menu-hover)", boxShadow: "inset 0 0 0 1px var(--ac-panel-border)" }}>
              {config.flag ? <span>{config.flag}</span> : <Icon name="bolt" className="h-6 w-6" />}
            </div>
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-2 text-[10px] font-bold uppercase tracking-widest" style={{ color: "var(--ac-muted)" }}>
                <span>اشتراک</span>
                <span className="inline-flex items-center gap-1.5 normal-case tracking-normal" style={{ color: live ? OK : "var(--ac-muted)" }}>
                  <i className={`h-[7px] w-[7px] rounded-full ${live && config.onlineNow > 0 ? "animate-pulse" : ""}`} style={{ background: live ? OK : "var(--ac-divider)" }} />
                  {live ? (config.onlineNow > 0 ? `${toFa(config.onlineNow)} دستگاه متصل` : "آماده") : "خاموش"}
                </span>
              </div>
              <h1 className="mt-0.5 truncate text-[21px] font-black" style={{ color: "var(--ac-title)" }}>{config.server}</h1>
              <p dir="ltr" className="truncate text-right text-[12px]" style={{ color: "var(--ac-muted)" }}>{config.name}</p>
            </div>
          </div>

          {status.note && <p className="mb-4 rounded-xl px-3.5 py-2.5 text-[12px] leading-6" style={{ background: "#fff7e6", color: "#8a4b08", border: "1px solid #ffd591" }}>{status.note}</p>}

          {/* quick stats */}
          <div className="mb-4 flex flex-col gap-2.5 sm:flex-row">
            <Quick
              ring={<Ring percent={100} color={live ? OK : BAD}>{toFa(profiles.length || config.deviceLimit)}</Ring>}
              k="وضعیت" v={live ? (unlimited ? "نامحدود" : "فعال") : status.label}
              s={`${toFa(Math.max(1, config.deviceLimit))} دستگاه · ${toFa(profiles.length)} کانفیگ`}
            />
            <Quick
              ring={<Ring percent={unlimited ? 100 : percent} color={unlimited ? "#531dab" : barColor}>{unlimited ? "∞" : `${toFa(Math.round(percent))}٪`}</Ring>}
              k="حجم" v={unlimited ? formatBytes(config.usedBytes) : formatBytes(remaining)}
              s={unlimited ? "مصرف · نامحدود" : `${formatBytes(config.usedBytes)} از ${formatBytes(config.totalBytes)}`}
            />
            <Quick
              ring={<Ring percent={100} color={chip?.tone === "bad" ? BAD : chip?.tone === "warn" ? WARN : "#FF5A1F"}><Icon name="clock" className="h-4 w-4" /></Ring>}
              k="زمان" v={chip ? chip.text : "∞"}
              s={config.expiresAtUtc ? faDate(config.expiresAtUtc) : "بدون انقضا"}
            />
          </div>

          {/* details table */}
          <table className="w-full overflow-hidden rounded-xl border text-[13px]" style={{ borderColor: "var(--ac-panel-border)", borderCollapse: "separate", borderSpacing: 0 }}>
            <tbody>
              {([
                ["شناسه اشتراک", <span key="s" dir="ltr">{config.subId || "—"}</span>],
                ["نام سرویس", <span key="n" dir="ltr">{config.name}</span>],
                ["وضعیت", statusTag],
                ["دانلود", <span key="d" dir="ltr">{formatBytes(config.downBytes)}</span>],
                ["آپلود", <span key="u" dir="ltr">{formatBytes(config.upBytes)}</span>],
                ["مصرف", <span key="m" dir="ltr">{formatBytes(config.usedBytes)}</span>],
                ["حجم کل", unlimited ? "نامحدود" : <span key="t" dir="ltr">{formatBytes(config.totalBytes)}</span>],
                ...(unlimited ? [] : [["باقی‌مانده", <span key="r" dir="ltr">{formatBytes(remaining)}</span>] as [string, React.ReactNode]]),
                ["تعداد دستگاه", `${toFa(Math.max(1, config.deviceLimit))} دستگاه`],
                ["انقضا", config.expiresAtUtc ? faDate(config.expiresAtUtc) : "بدون انقضا"],
                ["تاریخ خرید", faDate(config.createdAtUtc)],
                ...(config.renewCount > 0 ? [["تمدید", `${toFa(config.renewCount)} بار${config.lastRenewedAtUtc ? ` · آخرین: ${faDate(config.lastRenewedAtUtc)}` : ""}`] as [string, React.ReactNode]] : []),
              ] as [string, React.ReactNode][]).map(([k, v], i, arr) => (
                <tr key={k}>
                  <th className="w-[1%] whitespace-nowrap px-3.5 py-2 text-right text-[12px] font-medium" style={{ background: "var(--ac-menu-hover)", color: "var(--ac-muted)", borderBottom: i < arr.length - 1 ? "1px solid var(--ac-divider)" : undefined, borderLeft: "1px solid var(--ac-divider)" }}>{k}</th>
                  <td className="px-3.5 py-2 text-right" style={{ color: "var(--ac-title)", borderBottom: i < arr.length - 1 ? "1px solid var(--ac-divider)" : undefined }}>{v}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {/* usage bar */}
          <div className={`mt-3 rounded-2xl border px-4 py-3.5 ${live ? "" : "opacity-80"}`} style={{ borderColor: live ? "var(--ac-panel-border)" : "#ffccc7", background: "var(--ac-menu-hover)" }}>
            <div className="mb-2 flex items-center justify-between gap-3">
              <div dir="ltr" className="flex items-baseline gap-1.5 tabular-nums">
                <span className="text-[17px] font-black" style={{ color: "var(--ac-title)" }}>{formatBytes(config.usedBytes)}</span>
                <span style={{ color: "var(--ac-muted)" }}>/</span>
                <span className="text-[13px] font-bold" style={{ color: "var(--ac-muted)" }}>{unlimited ? "∞" : formatBytes(config.totalBytes)}</span>
              </div>
              <div className="flex items-center gap-1.5">
                {unlimited && <Tag tone="purple"><Icon name="bolt" className="h-3 w-3" />نامحدود</Tag>}
                {chip && <Tag tone={chipTone} dir="ltr"><Icon name="clock" className="h-3 w-3" />{chip.text}</Tag>}
              </div>
            </div>
            {!unlimited && (
              <div className="mb-1.5 h-2.5 overflow-hidden rounded-full" style={{ background: "var(--ac-divider)" }}>
                <div className="h-full rounded-full transition-[width] duration-700" style={{ width: `${Math.round(percent)}%`, background: percent >= 90 ? "linear-gradient(90deg,#ff7875,#ff4d4f)" : percent >= 75 ? "linear-gradient(90deg,#ffc53d,#fa8c16)" : "linear-gradient(90deg,#5fc983,#36b37e)" }} />
              </div>
            )}
            {!unlimited && (
              <div className="flex items-center justify-between text-[11px] tabular-nums" style={{ color: "var(--ac-muted)" }}>
                <span dir="ltr">{formatBytes(remaining)} باقی‌مانده</span>
                <span dir="ltr" className="font-bold">{toFa(Math.round(percent))}٪</span>
              </div>
            )}
            {!config.statsLive && !removed && <p className="mt-2 text-[11px]" style={{ color: WARN }}>آمار زنده در دسترس نیست؛ مشخصات پلن نمایش داده می‌شود.</p>}
          </div>

          {/* renew */}
          {renewals && !removed && (
            <>
              <Divider>تمدید سرویس</Divider>
              <RenewPanel token={token} renewals={renewals} />
            </>
          )}

          {/* subscription */}
          {config.subUrl && !removed && (
            <>
              <Divider>لینک اشتراک</Divider>
              <div className="flex items-center gap-2 rounded-xl border px-3 py-2" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)" }}>
                <Tag tone="green">SUB</Tag>
                <a href={config.subUrl} target="_blank" rel="noopener noreferrer" dir="ltr" className="min-w-0 flex-1 truncate text-[12px] hover:underline" style={{ color: "var(--ac-title)" }}>{config.subId || config.subUrl}</a>
                <div className="flex gap-1">
                  <SmBtn onClick={() => copySub(config.subUrl)} label="کپی لینک" done={subCopied}>{subCopied ? <Icon name="check" /> : <Icon name="copy" />}</SmBtn>
                  <SmBtn onClick={() => setQr({ value: config.subUrl, title: "لینک اشتراک" })} label="QR"><Icon name="qr" /></SmBtn>
                </div>
              </div>
              <p className="mt-2 px-1 text-[11px] leading-5" style={{ color: "var(--ac-muted)" }}>برای برنامه‌هایی که لینک اشتراک می‌گیرند (V2Box، Hiddify). برنامه‌ی رسمی WireGuard لینک نمی‌گیرد؛ برای آن QR یا فایل هر دستگاه را از پایین بردارید.</p>
            </>
          )}

          {/* files by tunnel */}
          {groups.length > 0 && !removed && (
            <>
              <Divider>کانفیگ‌ها</Divider>
              <div className="space-y-2">
                <div className="flex items-center gap-2 rounded-xl border px-3 py-2" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)" }}>
                  <span className="min-w-0 flex-1 text-[12px] font-black" style={{ color: "var(--ac-title)" }}>کپی همه‌ی کانفیگ‌ها</span>
                  <SmBtn onClick={() => copyAll(profiles.map((p) => p.body).join("\n\n"))} label="کپی همه" done={allCopied}>{allCopied ? <Icon name="check" /> : <Icon name="copy" />}</SmBtn>
                </div>
                {groups.map((g) => <TunnelGroup key={`${g.name}|${g.protocol}`} name={g.name} protocol={g.protocol} devices={g.devices} onQr={(p) => setQr({ value: p.body, title: `${p.deviceName || "دستگاه"} · ${protoOf(p.protocol).label}` })} />)}
              </div>
              <p className="mt-2 px-1 text-[11px] leading-5" style={{ color: "var(--ac-muted)" }}>هر کانفیگ برای یک دستگاه است؛ یک فایل را روی دو دستگاه هم‌زمان نگذارید. AmneziaWG فقط با برنامه‌های Amnezia باز می‌شود.</p>
            </>
          )}

          {/* apps */}
          {protocols.size > 0 && !removed && (
            <div className="mt-6 flex flex-wrap gap-2">
              <AppMenu id="android" protocols={protocols} />
              <AppMenu id="ios" protocols={protocols} />
              <AppMenu id="windows" protocols={protocols} />
              <AppMenu id="mac" protocols={protocols} />
            </div>
          )}
        </div>
      </div>

      {qr && <QrModal value={qr.value} title={qr.title} onClose={() => setQr(null)} />}
    </div>
  );
}
