"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { accountMenu } from "@/data/account";
import { useAuth } from "@/lib/auth";
import { useMe } from "@/lib/useMe";
import { formatToman, formatNumber } from "@/lib/format";
import MenuIcon from "./MenuIcon";

function KycBadge({ level }: { level: number }) {
  if (level >= 2) return (
    <span className="inline-flex items-center gap-1.5 rounded-full border border-[#E4D3FF] bg-[#F2E9FF] px-3 py-1.5 text-xs font-bold text-[#8A52FF]">
      <svg viewBox="0 0 16 16" fill="currentColor" className="h-3.5 w-3.5"><path d="M8 1l1.9 3.8 4.1.6-3 2.9.7 4.1L8 10.4l-3.7 2 .7-4.1-3-2.9 4.1-.6z"/></svg>
      سطح ۲ · تأیید شده
    </span>
  );
  if (level === 1) return (
    <span className="inline-flex items-center gap-1.5 rounded-full border border-amber-200 bg-amber-50 px-3 py-1.5 text-xs font-bold text-amber-600">
      سطح ۱ · در حال بررسی
    </span>
  );
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full border border-rose-200 bg-rose-50 px-3 py-1.5 text-xs font-bold text-rose-500">
      سطح ۰ · احراز نشده
    </span>
  );
}

export default function Sidebar() {
  const pathname = usePathname();
  const router = useRouter();
  const { user, logout } = useAuth();
  const { me } = useMe();

  const name = me?.name || user?.name || "کاربر";
  const username = me?.username || user?.username || "";
  const avatar = me?.avatar || "";
  const level = me?.verificationLevel ?? 0;
  const wallet = me?.wallet ?? 0;
  const initials = (name || username || "ک").charAt(0);

  function handleLogout() {
    logout();
    router.replace("/login");
  }

  return (
    <div className="flex flex-col gap-[18px]">
      {/* Profile + navigation card */}
      <aside
        className="h-fit p-[22px_18px] lg:sticky lg:top-24"
        style={{
          background: "var(--ac-sidebar-bg)",
          border: "1px solid var(--ac-sidebar-border)",
          borderRadius: "22px",
          boxShadow: "0 14px 38px rgba(166,102,45,0.08)",
        }}
      >
        {/* Avatar */}
        <div className="mb-4 flex flex-col items-center">
          <div
            className="mb-3 grid h-[92px] w-[92px] shrink-0 place-items-center overflow-hidden rounded-full"
            style={{
              border: "2px solid #FF6A2B",
              background: "#FFF1E8",
              boxShadow: "0 10px 26px rgba(255,106,43,0.18)",
            }}
          >
            {avatar ? (
              <img src={avatar} alt={name} className="h-full w-full object-cover" />
            ) : (
              <svg viewBox="0 0 92 92" fill="none" xmlns="http://www.w3.org/2000/svg" className="h-full w-full">
                <circle cx="46" cy="46" r="46" fill="#FFF1E8" />
                <ellipse cx="46" cy="38" rx="16" ry="16" fill="#FFD0B0" />
                <ellipse cx="46" cy="80" rx="26" ry="18" fill="#FFD0B0" />
              </svg>
            )}
          </div>

          <p className="text-[18px] font-black" style={{ color: "var(--ac-title)" }}>{name}</p>
          {username && (
            <p className="mt-0.5 text-[13px]" dir="ltr" style={{ color: "var(--ac-muted)" }}>@{username}</p>
          )}
          <div className="mt-3">
            <KycBadge level={level} />
          </div>
        </div>

        <div className="my-5" style={{ height: "1px", background: "var(--ac-divider)" }} />

        {/* Navigation */}
        <nav className="flex flex-col gap-0.5">
          {accountMenu.map((item) => {
            const active = pathname === item.href;
            return active ? (
              <Link
                key={item.href}
                href={item.href}
                className="flex h-[46px] items-center gap-3 rounded-xl px-[14px] text-[14px] font-bold transition-all"
                style={{
                  background: "var(--ac-menu-active-bg)",
                  borderRight: "3px solid var(--ac-menu-active-border)",
                  color: "var(--ac-menu-active-text)",
                }}
              >
                <MenuIcon name={item.icon} className="h-[18px] w-[18px]" />
                {item.label}
              </Link>
            ) : (
              <Link
                key={item.href}
                href={item.href}
                className="flex h-[46px] items-center gap-3 rounded-xl px-[14px] text-[14px] font-semibold transition-all"
                style={{ color: "var(--ac-menu-text)" }}
                onMouseEnter={(e) => {
                  (e.currentTarget as HTMLElement).style.background = "var(--ac-menu-hover)";
                  (e.currentTarget as HTMLElement).style.color = "#F2551F";
                }}
                onMouseLeave={(e) => {
                  (e.currentTarget as HTMLElement).style.background = "";
                  (e.currentTarget as HTMLElement).style.color = "var(--ac-menu-text)";
                }}
              >
                <MenuIcon name={item.icon} className="h-[18px] w-[18px]" style={{ color: "var(--ac-icon)" }} />
                {item.label}
              </Link>
            );
          })}

          <div className="mt-3 pt-3" style={{ borderTop: "1px solid var(--ac-divider)" }}>
            <button
              onClick={handleLogout}
              className="flex h-[48px] w-full items-center justify-center gap-2 rounded-xl border text-[14px] font-bold transition-all"
              style={{
                background: "#FFF8F3",
                border: "1px solid #F4CFC0",
                color: "#F2551F",
              }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLElement).style.background = "#FFF1E8";
                (e.currentTarget as HTMLElement).style.borderColor = "#FF6A2B";
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLElement).style.background = "#FFF8F3";
                (e.currentTarget as HTMLElement).style.borderColor = "#F4CFC0";
              }}
            >
              <MenuIcon name="logout" className="h-4 w-4" />
              خروج از حساب
            </button>
          </div>
        </nav>
      </aside>

      {/* Wallet card */}
      <div
        className="rounded-[22px] p-[22px_18px] lg:sticky lg:top-[calc(24rem+18px)]"
        style={{
          background: "var(--ac-panel-bg)",
          border: "1px solid var(--ac-panel-border)",
          boxShadow: "var(--ac-panel-shadow)",
        }}
      >
        <div className="mb-5 flex items-center gap-2">
          <MenuIcon name="wallet" className="h-5 w-5" style={{ color: "#4B4037" }} />
          <span className="text-[17px] font-black" style={{ color: "var(--ac-title)" }}>کیف پول من</span>
        </div>

        <p className="mb-1 text-center text-[13px]" style={{ color: "var(--ac-muted)" }}>موجودی فعلی</p>
        <p className="text-center text-[30px] font-black leading-tight" style={{ color: "var(--ac-title)" }}>
          {formatNumber(wallet)}
        </p>
        <p className="mb-6 text-center text-[13px]" style={{ color: "#6C5F54" }}>تومان</p>

        <Link
          href="/account/wallet"
          className="flex h-[48px] w-full items-center justify-center gap-2 rounded-xl text-[14px] font-black text-white transition hover:brightness-105"
          style={{ background: "var(--ac-btn)" }}
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4">
            <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
          </svg>
          افزایش موجودی
        </Link>

        <Link
          href="/account/wallet"
          className="mt-[10px] flex h-[46px] w-full items-center justify-center gap-2 rounded-xl border text-[14px] font-bold transition"
          style={{ background: "#FFFFFF", border: "1px solid #F4A58A", color: "#F2551F" }}
          onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = "#FFF8F5"; }}
          onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = "#FFFFFF"; }}
        >
          برداشت
        </Link>

        <Link
          href="/account/wallet"
          className="mt-[10px] flex h-[46px] w-full items-center justify-center gap-2 rounded-xl border text-[14px] font-bold transition"
          style={{ background: "#FFFFFF", border: "1px solid #F4A58A", color: "#F2551F" }}
          onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = "#FFF8F5"; }}
          onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = "#FFFFFF"; }}
        >
          تراکنش‌ها
        </Link>

        <div className="mt-4 text-center">
          <Link href="/account/wallet" className="text-[13px] font-bold transition hover:text-[#FF3D2E]" style={{ color: "#F2551F" }}>
            مشاهده همه تراکنش‌ها ‹
          </Link>
        </div>
      </div>
    </div>
  );
}
