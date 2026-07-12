"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useAdminMenu } from "@/lib/adminMenu";
import { useAdminAuth } from "@/lib/adminAuth";
import { useMe } from "@/lib/useMe";
import AdminIcon from "./AdminIcon";

const roleLabel: Record<string, string> = { Admin: "مدیر کل", Support: "پشتیبانی" };

export default function AdminTopbar({ onMenu }: { onMenu: () => void }) {
  const pathname = usePathname();
  const router = useRouter();
  const { user, logout } = useAdminAuth();
  // Live profile of the signed-in staff member, so the avatar they set on the normal site shows here too.
  const { me } = useMe();

  const menu = useAdminMenu();
  const items = menu.flatMap((g) => g.items);
  // Page title comes from the same (role-filtered) menu the sidebar uses — one source of truth.
  const current = [...items]
    .sort((a, b) => b.route.length - a.route.length)
    .find((m) => (m.route === "/admin" ? pathname === "/admin" : pathname.startsWith(m.route)));
  const title = current?.title ?? "پنل مدیریت";

  // Security & 2FA entry pulled from the role-filtered menu (it lives in the "account" group, available
  // to every staff member) — so relocating it to this dropdown keeps it logic-driven, not hardcoded.
  const securityItem = items.find((it) => it.route === "/admin/settings/2fa");

  // Profile dropdown: click to toggle, close on outside-click / Escape / navigation.
  const [profileOpen, setProfileOpen] = useState(false);
  const profileRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!profileOpen) return;
    const onDoc = (e: MouseEvent) => {
      if (profileRef.current && !profileRef.current.contains(e.target as Node)) setProfileOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setProfileOpen(false);
    };
    document.addEventListener("mousedown", onDoc);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDoc);
      document.removeEventListener("keydown", onKey);
    };
  }, [profileOpen]);

  const initial = (me?.name || user?.name || user?.username || "ا").charAt(0);
  const displayName = me?.name || user?.name || user?.username || "";
  const displayRole = user ? roleLabel[user.role] ?? "مدیر" : "";

  return (
    <header className="sticky top-0 z-20 flex h-[72px] items-center gap-4 border-b border-white/8 bg-[#0b0b12]/90 px-4 backdrop-blur sm:px-5 lg:px-8">
      {/* Mobile menu toggle */}
      <button
        onClick={onMenu}
        aria-label="باز کردن منو"
        className="grid h-10 w-10 shrink-0 place-items-center rounded-full border border-white/10 bg-white/5 text-white/70 transition hover:text-white lg:hidden"
      >
        <AdminIcon name="menu" className="h-5 w-5" />
      </button>

      <div className="min-w-0">
        <h1 className="truncate text-base font-bold text-white sm:text-lg">{title}</h1>
        <p className="hidden text-xs text-white/40 sm:block">پنل مدیریت فونیکس وریفای</p>
      </div>

      <div className="mr-auto flex items-center gap-2 sm:gap-3">
        {/* Search */}
        <div className="hidden h-10 w-56 items-center gap-2 rounded-full border border-white/10 bg-white/5 px-4 text-white/50 md:flex lg:w-64">
          <AdminIcon name="search" className="h-4 w-4 shrink-0" />
          <input
            placeholder="جستجو..."
            className="w-full bg-transparent text-sm text-white placeholder:text-white/40 focus:outline-none"
          />
        </div>

        {/* Notifications (decorative indicator) */}
        <button
          aria-label="اعلان‌ها"
          className="relative grid h-10 w-10 shrink-0 place-items-center rounded-full border border-white/10 bg-white/5 text-white/70 transition hover:text-white"
        >
          <AdminIcon name="bell" className="h-5 w-5" />
          <span className="absolute right-2.5 top-2.5 h-2 w-2 rounded-full bg-[#e60053]" />
        </button>

        {/* Profile dropdown: user identity + Security/2FA + Logout */}
        <div className="relative" ref={profileRef}>
          <button
            type="button"
            onClick={() => setProfileOpen((o) => !o)}
            aria-haspopup="menu"
            aria-expanded={profileOpen}
            aria-label="حساب کاربری"
            className={`flex items-center gap-2.5 rounded-full border py-1 pl-2 pr-1 transition ${
              profileOpen ? "border-white/20 bg-white/10" : "border-white/10 bg-white/5 hover:bg-white/10"
            }`}
          >
            {me?.avatar ? (
              <img src={me.avatar} alt={displayName} className="h-9 w-9 shrink-0 rounded-full object-cover" />
            ) : (
              <div className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-sm font-bold text-white">
                {initial}
              </div>
            )}
            <div className="hidden leading-tight sm:block">
              <p className="max-w-[120px] truncate text-sm font-bold text-white">{displayName}</p>
              <p className="text-xs text-white/40">{displayRole}</p>
            </div>
            <svg
              className={`hidden h-4 w-4 shrink-0 text-white/40 transition-transform sm:block ${profileOpen ? "rotate-180" : ""}`}
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden
            >
              <path d="M6 9l6 6 6-6" />
            </svg>
          </button>

          {profileOpen && (
            <div
              role="menu"
              className="absolute left-0 top-full z-50 mt-2 w-60 overflow-hidden rounded-2xl border border-white/10 bg-[#14141c] shadow-[0_24px_60px_-20px_rgba(0,0,0,0.8)]"
            >
              {/* Identity header */}
              <div className="flex items-center gap-3 border-b border-white/8 px-4 py-3">
                {me?.avatar ? (
                  <img src={me.avatar} alt={displayName} className="h-10 w-10 shrink-0 rounded-full object-cover" />
                ) : (
                  <div className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-sm font-bold text-white">
                    {initial}
                  </div>
                )}
                <div className="min-w-0">
                  <p className="truncate text-sm font-bold text-white">{displayName}</p>
                  <p className="text-xs text-white/45">{displayRole}</p>
                </div>
              </div>

              <div className="p-1.5">
                {/* Security & 2FA — always available to every staff member (from the role-filtered menu). */}
                {securityItem && (
                  <Link
                    href={securityItem.route}
                    role="menuitem"
                    onClick={() => setProfileOpen(false)}
                    className="flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium text-white/75 transition hover:bg-white/5 hover:text-white"
                  >
                    <AdminIcon name={securityItem.icon || "shield"} className="h-[18px] w-[18px] shrink-0" />
                    {securityItem.title}
                  </Link>
                )}

                <button
                  type="button"
                  role="menuitem"
                  onClick={() => {
                    setProfileOpen(false);
                    logout();
                    router.replace("/admin/login");
                  }}
                  className="flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium text-rose-300 transition hover:bg-rose-500/10 hover:text-rose-200"
                >
                  <AdminIcon name="logout" className="h-[18px] w-[18px] shrink-0" />
                  خروج از حساب
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </header>
  );
}
