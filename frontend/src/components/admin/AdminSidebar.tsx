"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAdminMenu } from "@/lib/adminMenu";
import type { AdminNavGroup, AdminNavItem } from "@/lib/types";
import { toFa } from "@/lib/format";
import AdminIcon from "./AdminIcon";

const isActive = (route: string, pathname: string) =>
  route === "/admin" ? pathname === "/admin" : pathname.startsWith(route);

function BadgePill({ value, active }: { value: number; active?: boolean }) {
  if (!value) return null;
  const label = value > 99 ? `${toFa(99)}+` : toFa(value);
  return (
    <span className={`grid h-5 min-w-5 place-items-center rounded-full px-1.5 text-[11px] font-bold ${active ? "bg-white/25 text-white" : "bg-[#e60053]/20 text-[#ff5a8a]"}`}>
      {label}
    </span>
  );
}

function ItemRow({ item, pathname, onNavigate }: { item: AdminNavItem; pathname: string; onNavigate?: () => void }) {
  // FUTURE FEATURE placeholder: visible for discoverability but not navigable until the page exists.
  if (item.comingSoon) {
    return (
      <div className="flex cursor-not-allowed items-center gap-3 rounded-xl px-3.5 py-2.5 text-sm font-medium text-white/30" title="به‌زودی">
        <AdminIcon name={item.icon} className="h-5 w-5 shrink-0" />
        <span className="flex-1">{item.title}</span>
        <span className="rounded-md bg-white/8 px-1.5 py-0.5 text-[10px] font-bold text-white/40">به‌زودی</span>
      </div>
    );
  }

  const active = isActive(item.route, pathname);
  return (
    <Link
      href={item.route}
      onClick={onNavigate}
      className={`flex items-center gap-3 rounded-xl px-3.5 py-2.5 text-sm font-medium transition ${
        active
          ? "bg-gradient-to-l from-[#e60053]/90 to-[#9c0038]/80 text-white shadow-[0_10px_30px_-12px_rgba(230,0,83,0.7)]"
          : "text-white/60 hover:bg-white/5 hover:text-white"
      }`}
    >
      <AdminIcon name={item.icon} className="h-5 w-5 shrink-0" />
      <span className="flex-1">{item.title}</span>
      <BadgePill value={item.badge} active={active} />
    </Link>
  );
}

function NavLinks({ groups, onNavigate }: { groups: AdminNavGroup[]; onNavigate?: () => void }) {
  const pathname = usePathname();
  const activeGroup = groups.find((g) => g.items.some((it) => !it.comingSoon && isActive(it.route, pathname)))?.key;
  const [open, setOpen] = useState<Record<string, boolean>>({});

  useEffect(() => {
    if (activeGroup) setOpen((p) => ({ ...p, [activeGroup]: true }));
  }, [activeGroup]);

  return (
    <nav className="flex-1 space-y-2 overflow-y-auto px-3 py-4">
      {groups.map((group) => {
        const isOpen = open[group.key] ?? false;
        const groupActive = group.items.some((it) => !it.comingSoon && isActive(it.route, pathname));
        const groupBadge = group.items.reduce((sum, it) => sum + it.badge, 0);

        return (
          <div key={group.key}>
            <button
              onClick={() => setOpen((p) => ({ ...p, [group.key]: !p[group.key] }))}
              className={`flex w-full items-center gap-2 rounded-xl px-3.5 py-2.5 text-[11px] font-bold tracking-wider transition ${
                groupActive ? "text-white" : "text-white/40 hover:text-white/70"
              }`}
            >
              <span className="flex-1 text-right">{group.title}</span>
              {!isOpen && groupBadge > 0 && (
                <span className="grid h-4 min-w-4 place-items-center rounded-full bg-[#e60053]/20 px-1 text-[10px] font-bold text-[#ff5a8a]">
                  {toFa(groupBadge)}
                </span>
              )}
              <svg
                className={`h-4 w-4 shrink-0 transition-transform duration-200 ${isOpen ? "rotate-180" : ""}`}
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <path d="M6 9l6 6 6-6" />
              </svg>
            </button>

            <div className={`mt-1 space-y-1 overflow-hidden pr-1 ${isOpen ? "block" : "hidden"}`}>
              {group.items.map((item) => (
                <ItemRow key={item.key} item={item} pathname={pathname} onNavigate={onNavigate} />
              ))}
            </div>
          </div>
        );
      })}
    </nav>
  );
}

function SidebarBody({ groups, onNavigate }: { groups: AdminNavGroup[]; onNavigate?: () => void }) {
  return (
    <>
      <Link href="/admin" onClick={onNavigate} className="flex h-[72px] items-center gap-2.5 border-b border-white/8 px-6">
        <img src="/figma/logo-phoenix.png" alt="Phoenix Verify" className="h-9 w-auto" />
        <span className="font-bigshot text-[13px] leading-[1.05] text-white">
          Phoenix
          <br />
          Verify
        </span>
        <span className="mr-auto rounded-md bg-[#e60053]/15 px-2 py-0.5 text-[11px] font-bold text-[#e60053]">ادمین</span>
      </Link>

      <NavLinks groups={groups} onNavigate={onNavigate} />

      <div className="border-t border-white/8 p-4">
        <Link
          href="/"
          onClick={onNavigate}
          className="flex items-center gap-3 rounded-xl px-3.5 py-2.5 text-sm font-medium text-white/55 transition hover:bg-white/5 hover:text-white"
        >
          <AdminIcon name="logout" className="h-5 w-5" />
          خروج به سایت
        </Link>
      </div>
    </>
  );
}

export default function AdminSidebar({ open, onClose }: { open: boolean; onClose: () => void }) {
  const groups = useAdminMenu();

  return (
    <>
      <aside className="fixed inset-y-0 right-0 z-30 hidden w-64 flex-col border-l border-white/8 bg-[#0d0d14] lg:flex">
        <SidebarBody groups={groups} />
      </aside>

      <div className={`fixed inset-0 z-40 lg:hidden ${open ? "" : "pointer-events-none"}`}>
        <div
          onClick={onClose}
          className={`absolute inset-0 bg-black/60 backdrop-blur-sm transition-opacity duration-300 ${open ? "opacity-100" : "opacity-0"}`}
        />
        <aside
          className={`absolute inset-y-0 right-0 flex w-72 max-w-[82%] flex-col border-l border-white/8 bg-[#0d0d14] shadow-2xl transition-transform duration-300 ${
            open ? "translate-x-0" : "translate-x-full"
          }`}
        >
          <SidebarBody groups={groups} onNavigate={onClose} />
        </aside>
      </div>
    </>
  );
}
