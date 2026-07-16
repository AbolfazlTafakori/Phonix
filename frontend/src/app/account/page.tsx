"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useMe } from "@/lib/useMe";
import { formatToman, formatNumber, toFa } from "@/lib/format";
import type { Order, Ticket } from "@/lib/types";

// ─── helpers ────────────────────────────────────────────────────────────────

function orderStatusBadge(s: Order["status"]): { text: string; bg: string; color: string } {
  const m: Record<Order["status"], { text: string; bg: string; color: string }> = {
    // Translucent tints so status pills read correctly on both the light and dark theme.
    Completed:       { text: "تحویل شده",      bg: "rgba(34,181,115,0.14)", color: "#22B573" },
    Preparing:       { text: "در حال پردازش",  bg: "rgba(76,141,255,0.14)", color: "#4C8DFF" },
    PendingApproval: { text: "در انتظار تأیید", bg: "rgba(244,164,58,0.16)", color: "#F4A43A" },
    Cancelled:       { text: "لغو شده",         bg: "rgba(224,80,80,0.14)",  color: "#E05050" },
  };
  return m[s] ?? { text: s, bg: "rgba(140,140,140,0.15)", color: "var(--ac-muted)" };
}

function ticketStatusBadge(s: Ticket["status"]): { text: string; bg: string; color: string } {
  const m: Record<Ticket["status"], { text: string; bg: string; color: string }> = {
    Open:     { text: "در حال بررسی",     bg: "rgba(244,164,58,0.16)", color: "#F4A43A" },
    Answered: { text: "پاسخ داده شده",    bg: "rgba(34,181,115,0.14)", color: "#22B573" },
    Closed:   { text: "بسته شده",          bg: "rgba(140,140,140,0.15)", color: "var(--ac-muted)" },
  };
  return m[s] ?? { text: s, bg: "rgba(140,140,140,0.15)", color: "var(--ac-muted)" };
}

const PRODUCT_LOGOS: Record<string, string> = {
  netflix:     "/figma/logo-netflix.png",
  wise:        "/figma/logo-wise.png",
  binance:     "/figma/logo-binance.png",
  bybit:       "/figma/logo-bybit.png",
  canva:       "/figma/logo-canva.png",
  freelancer:  "/figma/logo-freelancer.png",
  "apple music": "/figma/logo-applemusic.png",
};

function productLogo(name: string): string | null {
  const lower = name.toLowerCase();
  for (const [key, src] of Object.entries(PRODUCT_LOGOS)) {
    if (lower.includes(key)) return src;
  }
  return null;
}

function ProductAvatar({ name }: { name: string }) {
  const src = productLogo(name);
  if (src) return <img loading="lazy" decoding="async" src={src} alt={name} className="h-[26px] w-[26px] rounded-[6px] object-contain" />;
  return (
    <div className="grid h-[26px] w-[26px] shrink-0 place-items-center rounded-[6px] text-[10px] font-black"
      style={{ background: "var(--ac-stat-icon-orange-bg)", color: "#FF6A2B" }}>
      {name.charAt(0)}
    </div>
  );
}

// ─── reusable card shell ────────────────────────────────────────────────────

function Card({ className = "", style = {}, children }: { className?: string; style?: React.CSSProperties; children: React.ReactNode }) {
  return (
    <div
      className={`rounded-[18px] border transition-all duration-200 ${className}`}
      style={{
        background: "var(--ac-panel-bg)",
        border: "1px solid var(--ac-panel-border)",
        boxShadow: "var(--ac-panel-shadow)",
        ...style,
      }}
      onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.boxShadow = "var(--ac-panel-shadow-hover)"; }}
      onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.boxShadow = "var(--ac-panel-shadow)"; }}
    >
      {children}
    </div>
  );
}

// ─── stat card ───────────────────────────────────────────────────────────────

function StatCard({
  label, value, unit, link, linkText, iconBg, iconSrc,
}: {
  label: string; value: string; unit: string; link: string; linkText: string;
  iconBg: string; iconSrc: string;
}) {
  return (
    <Link
      href={link}
      className="group flex flex-col gap-4 rounded-[18px] border p-5 transition-all duration-200"
      style={{
        background: "var(--ac-panel-bg)",
        border: "1px solid var(--ac-panel-border)",
        boxShadow: "var(--ac-panel-shadow)",
        minHeight: "160px",
      }}
      onMouseEnter={(e) => {
        (e.currentTarget as HTMLElement).style.boxShadow = "var(--ac-panel-shadow-hover)";
        (e.currentTarget as HTMLElement).style.transform = "translateY(-2px)";
      }}
      onMouseLeave={(e) => {
        (e.currentTarget as HTMLElement).style.boxShadow = "var(--ac-panel-shadow)";
        (e.currentTarget as HTMLElement).style.transform = "";
      }}
    >
      {/* logo (right) + label (left, next to it) */}
      <div className="flex items-center gap-3">
        <img loading="lazy" decoding="async" src={iconSrc} alt="" className="h-[48px] w-[48px] shrink-0 object-contain" />
        <span className="text-[14px] font-bold leading-snug" style={{ color: "var(--ac-text)" }}>{label}</span>
      </div>

      {/* value + unit */}
      <div className="flex-1 text-center">
        <p className="text-[28px] font-black leading-none" style={{ color: "var(--ac-title)" }}>{value}</p>
        <p className="mt-1 text-[12px]" style={{ color: "var(--ac-muted)" }}>{unit}</p>
      </div>

      {/* link */}
      <span className="flex items-center justify-center gap-1.5 text-[13px] font-bold transition-opacity group-hover:opacity-60" style={{ color: "#F2551F" }}>
        {linkText}
        <span className="text-[17px] leading-none" style={{ direction: "ltr" }}>‹</span>
      </span>
    </Link>
  );
}

// ─── icon paths ─────────────────────────────────────────────────────────────

const I = {
  wallet:   ["M3 9h18M3 9V19a2 2 0 002 2h14a2 2 0 002-2V9M3 9l2-5h14l2 5", "M16 13h.01M12 13h.01"],
  bag:      ["M6 2L3 6v14a2 2 0 002 2h14a2 2 0 002-2V6l-3-4zM3 6h18", "M16 10a4 4 0 01-8 0"],
  headset:  ["M3 18v-6a9 9 0 0118 0v6", "M21 19a2 2 0 01-2 2h-1a2 2 0 01-2-2v-3a2 2 0 012-2h3z", "M3 19a2 2 0 002 2h1a2 2 0 002-2v-3a2 2 0 00-2-2H3z"],
  shield:   ["M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"],
  shieldOk: ["M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z", "M9 12l2 2 4-4"],
  edit:     ["M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7", "M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z"],
  user:     ["M20 21v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2", "M12 11a4 4 0 100-8 4 4 0 000 8z"],
  heart:    ["M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z"],
  users:    ["M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2", "M9 11a4 4 0 100-8 4 4 0 000 8z", "M23 21v-2a4 4 0 00-3-3.87", "M16 3.13a4 4 0 010 7.75"],
  copy:     ["M8 17.929H6c-1.105 0-2-.912-2-2.036V5.036C4 3.91 4.895 3 6 3h8c1.105 0 2 .911 2 2.036v1.866", "M10 20.929h8c1.105 0 2-.911 2-2.036V9.107c0-1.124-.895-2.036-2-2.036h-8c-1.105 0-2 .912-2 2.036v9.786c0 1.125.895 2.036 2 2.036z"],
  phone:    ["M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07 19.5 19.5 0 01-6-6 19.79 19.79 0 01-3.07-8.67A2 2 0 014.11 2h3a2 2 0 012 1.72c.127.96.361 1.903.7 2.81a2 2 0 01-.45 2.11L8.09 9.91a16 16 0 006 6l1.27-1.27a2 2 0 012.11-.45c.907.339 1.85.573 2.81.7A2 2 0 0122 16.92z"],
  lock:     ["M19 11H5a2 2 0 00-2 2v7a2 2 0 002 2h14a2 2 0 002-2v-7a2 2 0 00-2-2z", "M7 11V7a5 5 0 0110 0v4"],
  delivery: ["M5 17H3a2 2 0 01-2-2V5a2 2 0 012-2h11a2 2 0 012 2v3", "m-4 12a2 2 0 100-4 2 2 0 000 4z", "m6 0a2 2 0 100-4 2 2 0 000 4z", "M3 12h10M13 5h5l3 5v4h-8V5z"],
  check:    ["M20 6L9 17 4 12"],
  plus:     ["M12 5v14M5 12h14"],
};

function Ico({ paths, stroke = "currentColor", className = "" }: { paths: string[]; stroke?: string; className?: string }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke={stroke} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" className={className}>
      {paths.map((d, i) => <path key={i} d={d} />)}
    </svg>
  );
}

// ─── main ────────────────────────────────────────────────────────────────────

// Account details card: shows username / email / phone and lets the user edit them inline (uses the
// existing PUT /account/me). Brought back from the previous theme, placed above the completion card.
type ProfileFields = { name: string; username: string; email: string; phone: string; emailVerified?: boolean };
function AccountInfoCard({ me, onSaved }: { me: ProfileFields | null; onSaved: () => Promise<void> }) {
  const [editing, setEditing] = useState(false);
  const [saving, setSaving]   = useState(false);
  const [err, setErr]         = useState("");
  const [form, setForm]       = useState({ name: "", username: "", email: "", phone: "" });

  useEffect(() => {
    if (me && !editing) setForm({ name: me.name, username: me.username, email: me.email, phone: me.phone });
  }, [me, editing]);

  async function save() {
    setSaving(true); setErr("");
    try {
      await api.account.updateMe(form);
      await onSaved();
      setEditing(false);
    } catch (e) {
      setErr(e instanceof Error ? e.message : "ذخیرهٔ اطلاعات ناموفق بود.");
    } finally {
      setSaving(false);
    }
  }

  const inputCls = "h-10 w-full rounded-lg border px-3 text-[13px] outline-none";
  const inputStyle = { borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)", color: "var(--ac-title)" } as React.CSSProperties;

  const rows: { label: string; key: keyof typeof form; value: string; ltr?: boolean; badge?: React.ReactNode }[] = [
    { label: "نام و نام خانوادگی", key: "name", value: me?.name || "—" },
    { label: "نام کاربری", key: "username", value: me?.username ? `@${me.username}` : "—", ltr: true },
    {
      label: "ایمیل", key: "email", value: me?.email || "—", ltr: true,
      badge: me?.email ? (
        <span className={`rounded-md px-2 py-0.5 text-[10px] font-bold ${me.emailVerified ? "bg-emerald-500/15 text-emerald-600" : "bg-amber-500/15 text-amber-600"}`}>
          {me.emailVerified ? "تأییدشده" : "تأیید نشده"}
        </span>
      ) : null,
    },
    { label: "شماره تماس", key: "phone", value: me?.phone || "—", ltr: true },
  ];

  return (
    <Card>
      <div className="p-6">
        <div className="mb-4 flex items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <Ico paths={I.user} stroke="#FF6A2B" className="h-5 w-5 shrink-0" />
            <h3 className="text-[18px] font-black md:text-[20px]" style={{ color: "var(--ac-title)" }}>مشخصات حساب</h3>
          </div>
          {!editing && (
            <button onClick={() => setEditing(true)} className="flex h-9 items-center gap-1.5 rounded-lg px-3 text-[13px] font-bold text-white transition hover:brightness-105" style={{ background: "var(--ac-btn)" }}>
              <Ico paths={I.edit} className="h-4 w-4" /> ویرایش
            </button>
          )}
        </div>

        {editing ? (
          <div className="grid gap-3 sm:grid-cols-2">
            {(["name", "username", "email", "phone"] as const).map((k) => (
              <label key={k} className="block">
                <span className="mb-1 block text-[12px] font-bold" style={{ color: "var(--ac-muted)" }}>
                  {k === "name" ? "نام و نام خانوادگی" : k === "username" ? "نام کاربری" : k === "email" ? "ایمیل" : "شماره تماس"}
                </span>
                <input value={form[k]} onChange={(e) => setForm((f) => ({ ...f, [k]: e.target.value }))} dir={k === "name" ? "rtl" : "ltr"} className={inputCls} style={inputStyle} />
              </label>
            ))}
            {err && <p className="text-[12px] font-bold text-rose-500 sm:col-span-2">{err}</p>}
            <div className="flex gap-2 sm:col-span-2">
              <button onClick={save} disabled={saving} className="flex h-10 items-center gap-2 rounded-lg px-6 text-[13px] font-bold text-white transition hover:brightness-105 disabled:opacity-60" style={{ background: "var(--ac-btn)" }}>
                {saving ? "در حال ذخیره…" : "ذخیره تغییرات"}
              </button>
              <button onClick={() => { setEditing(false); setErr(""); }} className="h-10 rounded-lg border px-6 text-[13px] font-bold" style={{ borderColor: "var(--ac-panel-border)", color: "var(--ac-text)" }}>انصراف</button>
            </div>
          </div>
        ) : (
          <div className="grid gap-x-6 gap-y-3.5 sm:grid-cols-2">
            {rows.map((r) => (
              <div key={r.label} className="flex items-center justify-between gap-3 border-b pb-2.5" style={{ borderColor: "var(--ac-divider)" }}>
                <span className="text-[12px] font-bold" style={{ color: "var(--ac-muted)" }}>{r.label}</span>
                <span className="flex items-center gap-2 truncate text-[13px] font-bold" style={{ color: "var(--ac-title)" }}>
                  {r.badge}
                  <span dir={r.ltr ? "ltr" : "rtl"} className="truncate">{r.value}</span>
                </span>
              </div>
            ))}
          </div>
        )}
      </div>
    </Card>
  );
}

export default function AccountDashboard() {
  const { user } = useAuth();
  const { me, refresh } = useMe();
  const [orders, setOrders]     = useState<Order[]>([]);
  const [tickets, setTickets]   = useState<Ticket[]>([]);
  const [favCount, setFavCount] = useState(0);
  const [copied, setCopied]     = useState(false);
  // Read on the client only: window.location.origin has no server-side equivalent here.
  const [origin, setOrigin]     = useState("");

  useEffect(() => setOrigin(window.location.origin), []);

  useEffect(() => {
    if (!user) return;
    api.orders.list().then((o) => setOrders(o.slice(0, 4))).catch(() => {});
    api.tickets.list().then((t) => setTickets(t.slice(0, 3))).catch(() => {});
    api.favorites.ids(user.id).then((ids) => setFavCount(ids.length)).catch(() => {});
  }, [user]);

  const displayName = me?.name  || user?.name     || "کاربر";
  const username    = me?.username || user?.username || "";
  const wallet      = me?.wallet  ?? 0;
  const totalOrders = me?.orders  ?? orders.length;
  const kycLevel    = me?.verificationLevel ?? 0;
  const openTickets = tickets.filter((t) => t.status === "Open").length;

  // Real profile-completion signals (was hardcoded 70%). The security card is dismissed entirely once the
  // user has BOTH verified their email AND completed identity verification — the two security milestones.
  const emailVerified    = me?.emailVerified ?? false;
  const identityVerified = kycLevel >= 2;
  const completionChecks = [Boolean(me?.avatar), Boolean(me?.phone?.trim()), emailVerified, identityVerified];
  const completionPct    = Math.round((completionChecks.filter(Boolean).length / completionChecks.length) * 100);
  const profileComplete  = emailVerified && identityVerified;

  // The same link /account/invite hands out — the copy button puts this on the clipboard, not the bare code,
  // so what a friend receives is something they can click straight through to signup.
  const inviteLink = username && origin ? `${origin}/signup?ref=${username}` : "";

  function copyInviteLink() {
    if (!inviteLink) return;
    navigator.clipboard?.writeText(inviteLink).catch(() => {});
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  // Shared border style for dividers that follow the theme
  const dividerStyle = { borderColor: "var(--ac-divider)" };

  return (
    <div className="flex flex-col gap-5">

      {/* ── Page title ── */}
      <div>
        <h1 className="text-[28px] font-black leading-snug md:text-[32px]" style={{ color: "var(--ac-title)" }}>
          پنل حساب کاربری
        </h1>
        <p className="mt-1.5 text-[14px] md:text-[15px]" style={{ color: "var(--ac-text)" }}>
          مدیریت حساب کاربری، سفارش‌ها، کیف پول و پشتیبانی در یک نگاه
        </p>
      </div>

      {/* ── Welcome banner ── */}
      <Card>
        <div className="flex flex-col-reverse items-center gap-5 p-6 md:grid md:grid-cols-[1fr_200px] md:gap-7">
          <div>
            <h2 className="mb-3 text-[20px] font-black md:text-[24px]" style={{ color: "var(--ac-title)" }}>
              👋 خوش آمدید، {displayName} عزیز!
            </h2>
            <p className="text-[14px] leading-loose md:text-[15px]" style={{ color: "var(--ac-text)" }}>
              از اینکه به خانواده{" "}
              <span className="font-black" style={{ color: "#F2551F" }}>Phoenix Verify</span>{" "}
              پیوسته‌اید، خوشحالیم.
            </p>
            <p className="text-[14px] leading-loose md:text-[15px]" style={{ color: "var(--ac-text)" }}>
              اکنون می‌توانید از خدمات متنوع و پشتیبانی سریع ما بهره‌مند شوید.
            </p>
          </div>
          <div className="flex justify-center">
            <img loading="lazy" decoding="async"
              src="/figma/account-welcome.png"
              alt="Phoenix Verify"
              className="h-[145px] w-[215px] object-contain md:h-[165px] md:w-[240px]"
              onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = "none"; }}
            />
          </div>
        </div>
      </Card>

      {/* ── Stat cards ── */}
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatCard
          label="سطح احراز هویت" value={`سطح ${toFa(kycLevel)}`} unit="تأیید شده"
          link="/account/kyc" linkText="مشاهده جزئیات"
          iconBg="var(--ac-stat-icon-purple-bg)" iconSrc="/figma/icon-shield.png"
        />
        <StatCard
          label="تیکت‌های باز" value={toFa(openTickets)} unit="تیکت"
          link="/account/tickets" linkText="مشاهده تیکت‌ها"
          iconBg="var(--ac-stat-icon-green-bg)" iconSrc="/figma/icon-headset.png"
        />
        <StatCard
          label="کل سفارش‌ها" value={toFa(totalOrders)} unit="سفارش"
          link="/account/orders" linkText="مشاهده سفارش‌ها"
          iconBg="var(--ac-stat-icon-orange-bg)" iconSrc="/figma/icon-bag.png"
        />
        <StatCard
          label="موجودی کیف پول" value={formatNumber(wallet)} unit="تومان"
          link="/account/wallet" linkText="مشاهده کیف پول"
          iconBg="var(--ac-stat-icon-orange-bg)" iconSrc="/figma/icon-wallet.png"
        />
      </div>

      {/* ── Account details (username / email / phone) ── */}
      <AccountInfoCard me={me} onSaved={refresh} />

      {/* ── Security / profile completion — hidden once email + identity are both verified ── */}
      {!profileComplete && (
      <Card>
        <div className="flex flex-col-reverse items-center gap-6 p-6 md:grid md:grid-cols-[1fr_160px] md:gap-7">
          <div className="w-full">
            <div className="mb-2 flex items-center gap-2">
              <Ico paths={I.shield} stroke="#FF6A2B" className="h-5 w-5 shrink-0" />
              <h3 className="text-[18px] font-black md:text-[20px]" style={{ color: "var(--ac-title)" }}>
                تکمیل پروفایل و امنیت حساب
              </h3>
            </div>
            <p className="mb-5 text-[13px] leading-loose md:text-[14px]" style={{ color: "var(--ac-text)" }}>
              برای دسترسی کامل به امکانات و افزایش امنیت حساب، اطلاعات خود را تکمیل کنید.
            </p>

            <div className="mb-2 flex items-center justify-between text-[13px] font-bold">
              <span style={{ color: "var(--ac-text)" }}>پیشرفت تکمیل پروفایل</span>
              <span style={{ color: "#F2551F" }}>{toFa(completionPct)}٪ تکمیل شده</span>
            </div>
            <div className="mb-5 h-[8px] overflow-hidden rounded-full" style={{ background: "var(--ac-divider)" }}>
              <div className="h-full rounded-full transition-[width] duration-500" style={{ width: `${completionPct}%`, background: "linear-gradient(90deg, #FF8A2B 0%, #FF3D2E 100%)" }} />
            </div>

            <div className="flex flex-wrap items-center gap-3">
              <Link
                href="/account"
                className="flex h-11 min-w-[160px] items-center justify-center gap-2 rounded-xl text-[14px] font-bold text-white transition hover:brightness-105"
                style={{ background: "var(--ac-btn)" }}
              >
                <Ico paths={I.edit} className="h-4 w-4" />
                تکمیل اطلاعات
              </Link>
              <Link
                href="/account/kyc"
                className="flex h-11 min-w-[140px] items-center justify-center gap-2 rounded-xl border text-[14px] font-semibold transition"
                style={{ background: "var(--ac-btn-secondary-bg)", border: "1px solid var(--ac-btn-secondary-border)", color: "var(--ac-menu-text)" }}
                onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = "var(--ac-menu-hover)"; }}
                onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = "var(--ac-btn-secondary-bg)"; }}
              >
                <Ico paths={I.user} className="h-4 w-4" />
                احراز هویت
              </Link>
            </div>
          </div>

          {/* Shield visual */}
          <div className="flex items-center justify-center">
            <div
              className="flex h-[100px] w-[100px] items-center justify-center rounded-full md:h-[120px] md:w-[120px]"
              style={{ background: "var(--ac-stat-icon-orange-bg)", border: "2px solid var(--ac-panel-border)" }}
            >
              <Ico paths={I.shieldOk} stroke="#FF6A2B" className="h-12 w-12" />
            </div>
          </div>
        </div>
      </Card>
      )}

      {/* ── Recent orders ── */}
      <Card className="overflow-hidden">
        {/* header */}
        <div className="flex items-center justify-between border-b px-5 py-4 md:px-6" style={dividerStyle}>
          <h2 className="text-[16px] font-black md:text-[18px]" style={{ color: "var(--ac-title)" }}>سفارش‌های اخیر</h2>
          <Link href="/account/orders" className="text-[13px] font-bold transition hover:opacity-70" style={{ color: "#F2551F" }}>
            مشاهده همه سفارش‌ها ‹
          </Link>
        </div>

        {orders.length === 0 ? (
          <p className="py-10 text-center text-[13px]" style={{ color: "var(--ac-muted)" }}>سفارشی ثبت نشده است.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[560px] border-collapse text-right">
              <thead>
                <tr style={{ background: "var(--ac-stat-icon-orange-bg)" }}>
                  {["کد سفارش", "محصول", "تاریخ", "وضعیت", "مبلغ"].map((h) => (
                    <th key={h} className="px-4 py-3 text-[12px] font-bold md:px-5 md:py-[14px] md:text-[13px]" style={{ color: "var(--ac-text)" }}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {orders.map((order) => {
                  const badge = orderStatusBadge(order.status);
                  const productName = order.items[0]?.name ?? "—";
                  return (
                    <tr
                      key={order.id}
                      className="border-t transition"
                      style={{ borderColor: "var(--ac-divider)" }}
                      onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = "var(--ac-menu-hover)"; }}
                      onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = ""; }}
                    >
                      <td className="px-4 py-3 font-mono text-[12px] font-medium md:px-5 md:py-[14px] md:text-[13px]" style={{ color: "var(--ac-muted)" }}>{order.code}</td>
                      <td className="px-4 py-3 md:px-5 md:py-[14px]">
                        <div className="flex items-center gap-2">
                          <ProductAvatar name={productName} />
                          <span className="text-[12px] font-medium md:text-[13px]" style={{ color: "var(--ac-title)" }}>{productName}</span>
                        </div>
                      </td>
                      <td className="px-4 py-3 text-[12px] md:px-5 md:py-[14px] md:text-[13px]" style={{ color: "var(--ac-text)" }}>{order.date}</td>
                      <td className="px-4 py-3 md:px-5 md:py-[14px]">
                        <span className="inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-bold" style={{ background: badge.bg, color: badge.color }}>
                          {badge.text}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-[12px] font-medium md:px-5 md:py-[14px] md:text-[13px]" style={{ color: "var(--ac-title)" }}>
                        {formatToman(order.total)}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* ── Bottom 3-column row ── */}
      <div className="grid grid-cols-1 gap-4 md:grid-cols-3">

        {/* Tickets */}
        <Card className="flex flex-col p-5" style={{ minHeight: "230px" }}>
          <div className="mb-4 flex items-center gap-2">
            <Ico paths={I.headset} stroke="#F2551F" className="h-5 w-5" />
            <h3 className="text-[16px] font-black" style={{ color: "var(--ac-title)" }}>تیکت‌های اخیر</h3>
          </div>

          <div className="flex-1">
            {tickets.length === 0 ? (
              <p className="py-4 text-[13px]" style={{ color: "var(--ac-muted)" }}>تیکتی ثبت نشده است.</p>
            ) : (
              tickets.map((t) => {
                const b = ticketStatusBadge(t.status);
                return (
                  <div key={t.id} className="border-b py-2.5 last:border-0" style={dividerStyle}>
                    <div className="flex items-start justify-between gap-2">
                      <p className="text-[12px] font-semibold leading-snug" style={{ color: "var(--ac-title)" }}>{t.subject}</p>
                      <span className="shrink-0 rounded-full px-2 py-0.5 text-[10px] font-bold" style={{ background: b.bg, color: b.color }}>{b.text}</span>
                    </div>
                    <p className="mt-0.5 font-mono text-[11px]" style={{ color: "var(--ac-muted)" }}>#{t.code}</p>
                  </div>
                );
              })
            )}
          </div>

          <Link
            href="/account/tickets"
            className="mt-4 flex h-11 w-full items-center justify-center gap-2 rounded-xl border text-[13px] font-bold transition"
            style={{ background: "var(--ac-btn-secondary-bg)", border: "1px solid var(--ac-btn-secondary-border)", color: "#F2551F" }}
            onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = "var(--ac-menu-hover)"; }}
            onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = "var(--ac-btn-secondary-bg)"; }}
          >
            + ارسال تیکت جدید
          </Link>
        </Card>

        {/* Favorites */}
        <Card className="p-5" style={{ minHeight: "230px" }}>
          <div className="mb-4 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <svg viewBox="0 0 24 24" fill="#F2551F" stroke="#F2551F" strokeWidth="1.5" className="h-5 w-5">
                {I.heart.map((d, i) => <path key={i} d={d} />)}
              </svg>
              <h3 className="text-[16px] font-black" style={{ color: "var(--ac-title)" }}>علاقه‌مندی‌های من</h3>
            </div>
            <Link href="/account/favorites" className="text-[13px] font-bold transition hover:opacity-70" style={{ color: "#F2551F" }}>
              مشاهده همه
            </Link>
          </div>

          {favCount === 0 ? (
            <div className="flex flex-col items-center justify-center py-6 text-center">
              <svg viewBox="0 0 24 24" fill="none" stroke="var(--ac-divider)" strokeWidth="1.5" className="mb-3 h-11 w-11">
                {I.heart.map((d, i) => <path key={i} d={d} />)}
              </svg>
              <p className="text-[13px]" style={{ color: "var(--ac-muted)" }}>هنوز محصولی ذخیره نکرده‌اید.</p>
              <Link href="/products" className="mt-3 text-[13px] font-bold hover:opacity-70" style={{ color: "#F2551F" }}>مشاهده محصولات ‹</Link>
            </div>
          ) : (
            <div className="grid grid-cols-2 gap-2.5">
              {[
                { name: "Netflix Premium 4K", logo: "/figma/logo-netflix.png" },
                { name: "Spotify Premium",    logo: "/figma/logo-applemusic.png" },
                { name: "NordVPN 1 Year",     logo: null },
                { name: "Google Play $25",    logo: null },
              ].slice(0, Math.min(4, favCount)).map((p, i) => (
                <div key={i} className="relative rounded-[12px] border p-2.5" style={{ background: "var(--ac-panel-bg)", border: "1px solid var(--ac-panel-border)", minHeight: "72px" }}>
                  <div className="flex items-start gap-2">
                    {p.logo ? (
                      <img loading="lazy" decoding="async" src={p.logo} alt={p.name} className="h-[30px] w-[30px] rounded-[8px] object-contain" />
                    ) : (
                      <div className="grid h-[30px] w-[30px] shrink-0 place-items-center rounded-[8px] text-[11px] font-black" style={{ background: "var(--ac-stat-icon-orange-bg)", color: "#FF6A2B" }}>{p.name.charAt(0)}</div>
                    )}
                    <p className="text-[11px] font-bold leading-snug" style={{ color: "var(--ac-title)" }}>{p.name}</p>
                  </div>
                  <span className="absolute bottom-2 left-2 text-[13px]" style={{ color: "#F2551F" }}>♥</span>
                </div>
              ))}
            </div>
          )}
        </Card>

        {/* Referral */}
        <Card className="p-5" style={{ minHeight: "230px" }}>
          <div className="mb-3 flex items-center gap-2">
            <Ico paths={I.users} stroke="#F2551F" className="h-5 w-5" />
            <h3 className="text-[16px] font-black" style={{ color: "var(--ac-title)" }}>دعوت از دوستان</h3>
          </div>
          <p className="text-[13px] leading-loose" style={{ color: "var(--ac-text)" }}>
            دوستان خود را دعوت کنید و امتیاز بگیرید.
          </p>
          <p className="mb-4 text-[13px] leading-loose" style={{ color: "var(--ac-text)" }}>
            به ازای هر ثبت‌نام فعال،{" "}
            <span className="font-black" style={{ color: "#F2551F" }}>۵۰,۰۰۰ تومان</span>{" "}
            اعتبار دریافت کنید.
          </p>

          <div
            className="relative rounded-[14px] border p-4 text-center"
            style={{ background: "var(--ac-stat-icon-orange-bg)", borderColor: "var(--ac-btn-secondary-border)", borderStyle: "dashed" }}
          >
            <p className="mb-1 text-[11px]" style={{ color: "var(--ac-muted)" }}>کد دعوت شما</p>
            <p className="text-[20px] font-black uppercase tracking-wider" dir="ltr" style={{ color: "#F2551F" }}>
              {username.toUpperCase() || "PHONIX2024"}
            </p>
            {/* The link the copy button actually copies, shown so it is clear what lands on the clipboard. */}
            {inviteLink && (
              <p dir="ltr" title={inviteLink} className="mt-1.5 truncate px-8 text-[11px]" style={{ color: "var(--ac-muted)" }}>
                {inviteLink}
              </p>
            )}
            <button
              onClick={copyInviteLink}
              className="absolute left-3 top-1/2 grid h-8 w-8 -translate-y-1/2 place-items-center rounded-[8px] text-white transition hover:brightness-110"
              style={{ background: "#FF6A2B" }}
              title="کپی لینک دعوت"
            >
              {copied
                ? <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" className="h-3.5 w-3.5"><polyline points="20 6 9 17 4 12" /></svg>
                : <Ico paths={I.copy} className="h-3.5 w-3.5" />
              }
            </button>
          </div>

          <div className="mt-3 text-center">
            <Link href="/account/invite" className="text-[13px] font-bold transition hover:opacity-70" style={{ color: "#F2551F" }}>
              مشاهده جزئیات و شرایط ‹
            </Link>
          </div>
        </Card>
      </div>

      {/* ── Support banner ── */}
      <div
        className="flex flex-col items-center gap-5 overflow-hidden rounded-[18px] p-5 text-center transition-all duration-200
                   md:grid md:h-[112px] md:grid-cols-[220px_1fr_260px] md:p-[18px_24px] md:text-right"
        style={{
          background: "var(--ac-panel-bg)",
          border: "1.5px solid var(--ac-panel-border)",
          borderRadius: "18px",
          boxShadow: "var(--ac-panel-shadow)",
        }}
      >
        {/* Right: text */}
        <div>
          <h3 className="text-[20px] font-black md:text-[22px]" style={{ color: "var(--ac-title)" }}>
            نیاز به کمک دارید؟
          </h3>
          <p className="mt-0.5 text-[13px] md:text-[14px]" style={{ color: "var(--ac-muted)" }}>
            تیم پشتیبانی ما ۲۴/۷ آماده پاسخگویی به شماست.
          </p>
        </div>

        {/* Center: icon with wave ring */}
        <div className="flex justify-center">
          <div className="relative flex items-center justify-center">
            {/* outer wave ring */}
            <div
              className="absolute h-[86px] w-[86px] rounded-full"
              style={{ background: "rgba(255,106,43,0.10)" }}
            />
            {/* inner circle */}
            <div
              className="relative flex h-[70px] w-[70px] items-center justify-center rounded-full"
              style={{ background: "var(--ac-stat-icon-orange-bg)" }}
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="#FF6A2B" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" className="h-8 w-8">
                <path d="M3 18v-6a9 9 0 0118 0v6" />
                <path d="M21 19a2 2 0 01-2 2h-1a2 2 0 01-2-2v-3a2 2 0 012-2h3z" />
                <path d="M3 19a2 2 0 002 2h1a2 2 0 002-2v-3a2 2 0 00-2-2H3z" />
              </svg>
            </div>
          </div>
        </div>

        {/* Left: button */}
        <div className="flex justify-center md:justify-end">
          <Link
            href="/account/tickets"
            className="group flex h-12 min-w-[180px] items-center justify-center gap-2 rounded-xl border text-[15px] font-bold transition-all duration-200"
            style={{ background: "var(--ac-btn-secondary-bg)", border: "1.5px solid var(--ac-panel-border)", color: "#F2551F" }}
            onMouseEnter={(e) => {
              const el = e.currentTarget as HTMLElement;
              el.style.background = "var(--ac-menu-hover)";
              el.style.borderColor = "#FF9A73";
              el.style.color = "#FF3D2E";
            }}
            onMouseLeave={(e) => {
              const el = e.currentTarget as HTMLElement;
              el.style.background = "var(--ac-btn-secondary-bg)";
              el.style.borderColor = "var(--ac-panel-border)";
              el.style.color = "#F2551F";
            }}
          >
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4">
              <path d="M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07 19.5 19.5 0 01-6-6 19.79 19.79 0 01-3.07-8.67A2 2 0 014.11 2h3a2 2 0 012 1.72c.127.96.361 1.903.7 2.81a2 2 0 01-.45 2.11L8.09 9.91a16 16 0 006 6l1.27-1.27a2 2 0 012.11-.45c.907.339 1.85.573 2.81.7A2 2 0 0122 16.92z" />
            </svg>
            تماس با پشتیبانی
          </Link>
        </div>
      </div>

      {/* ── Trust row ── */}
      <div className="flex flex-wrap items-center justify-center gap-4 pb-2 md:gap-0">
        {[
          { paths: I.lock,     text: "پرداخت امن و مطمئن" },
          { paths: I.headset,  text: "پشتیبانی ۲۴/۷"       },
          { paths: I.wallet,   text: "ضمانت بازگشت وجه"    },
          { paths: I.delivery, text: "تحویل آنی محصولات"   },
        ].map((item, i) => (
          <div key={i} className="flex items-center">
            {i > 0 && <div className="mx-6 hidden h-5 w-px shrink-0 md:block" style={{ background: "var(--ac-divider)" }} />}
            <Ico paths={item.paths} stroke="var(--ac-icon)" className="ml-2 h-4 w-4 shrink-0" />
            <span className="text-[13px] font-semibold" style={{ color: "var(--ac-text)" }}>{item.text}</span>
          </div>
        ))}
      </div>

    </div>
  );
}
