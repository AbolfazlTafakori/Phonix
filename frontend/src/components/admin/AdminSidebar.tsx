"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAdminMenu } from "@/lib/adminMenu";
import type { AdminNavGroup, AdminNavItem } from "@/lib/types";
import { toFa } from "@/lib/format";
import AdminIcon from "./AdminIcon";

// The "account" group (personal Security & 2FA) is relocated to the topbar profile dropdown, so it is
// excluded from the sidebar here. RBAC is untouched: the backend still decides which groups a role
// receives (the Admin-only DevOps group is never sent to Support) — this is presentation only.
const HIDDEN_GROUP_KEYS = new Set(["account"]);

const isActive = (route: string, pathname: string) =>
  route === "/admin" ? pathname === "/admin" : pathname.startsWith(route);

function BadgePill({ value, active }: { value: number; active?: boolean }) {
  if (!value) return null;
  const label = value > 99 ? `${toFa(99)}+` : toFa(value);
  return (
    <span
      className={`grid h-5 min-w-5 place-items-center rounded-full px-1.5 text-[11px] font-bold ${
        active ? "bg-white/25 text-white" : "bg-[#e60053]/20 text-[#ff5a8a]"
      }`}
    >
      {label}
    </span>
  );
}

function ItemRow({ item, pathname, onNavigate }: { item: AdminNavItem; pathname: string; onNavigate?: () => void }) {
  // FUTURE FEATURE placeholder: visible for discoverability but not navigable until the page exists.
  if (item.comingSoon) {
    return (
      <div
        className="flex cursor-not-allowed items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium text-white/25"
        title="به‌زودی"
      >
        <AdminIcon name={item.icon} className="h-[18px] w-[18px] shrink-0" />
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
      aria-current={active ? "page" : undefined}
      className={`flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition ${
        active
          ? "bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white shadow-[0_10px_28px_-14px_rgba(230,0,83,0.8)]"
          : "text-white/60 hover:bg-white/5 hover:text-white"
      }`}
    >
      <AdminIcon name={item.icon} className="h-[18px] w-[18px] shrink-0" />
      <span className="flex-1">{item.title}</span>
      <BadgePill value={item.badge} active={active} />
    </Link>
  );
}

function AccordionGroup({
  group,
  pathname,
  open,
  onToggle,
  onNavigate,
}: {
  group: AdminNavGroup;
  pathname: string;
  open: boolean;
  onToggle: () => void;
  onNavigate?: () => void;
}) {
  const groupActive = group.items.some((it) => !it.comingSoon && isActive(it.route, pathname));
  // Sum of child badges → shown on the parent so pending work is visible even while the group is collapsed.
  const groupBadge = group.items.reduce((sum, it) => sum + it.badge, 0);

  return (
    <div className={`rounded-xl ${open ? "bg-white/[0.03]" : ""}`}>
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={open}
        className={`flex w-full items-center gap-2 rounded-xl px-3 py-2.5 text-[12px] font-bold tracking-wide transition ${
          groupActive || open ? "text-white" : "text-white/45 hover:text-white/75"
        }`}
      >
        <span className="flex-1 text-right">{group.title}</span>
        {/* Parent badge stays visible whether the accordion is open or closed. */}
        <BadgePill value={groupBadge} />
        <svg
          className={`h-4 w-4 shrink-0 text-white/40 transition-transform duration-200 ${open ? "rotate-180" : ""}`}
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

      {/* Smooth accordion open/close via grid-rows trick, so it animates instead of snapping. */}
      <div className={`grid transition-[grid-template-rows] duration-200 ease-out ${open ? "grid-rows-[1fr]" : "grid-rows-[0fr]"}`}>
        <div className="overflow-hidden">
          <div className="space-y-1 px-1.5 pb-2">
            {group.items.map((item) => (
              <ItemRow key={item.key} item={item} pathname={pathname} onNavigate={onNavigate} />
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

function NavLinks({ groups, onNavigate }: { groups: AdminNavGroup[]; onNavigate?: () => void }) {
  const pathname = usePathname();
  const visibleGroups = groups.filter((g) => !HIDDEN_GROUP_KEYS.has(g.key));
  const activeGroup = visibleGroups.find((g) => g.items.some((it) => !it.comingSoon && isActive(it.route, pathname)))?.key;
  const [open, setOpen] = useState<Record<string, boolean>>({});

  // Auto-open the accordion that contains the current page.
  useEffect(() => {
    if (activeGroup) setOpen((p) => ({ ...p, [activeGroup]: true }));
  }, [activeGroup]);

  return (
    <nav className="flex-1 space-y-1.5 overflow-y-auto px-2.5 py-4">
      {visibleGroups.map((group) => (
        <AccordionGroup
          key={group.key}
          group={group}
          pathname={pathname}
          open={open[group.key] ?? false}
          onToggle={() => setOpen((p) => ({ ...p, [group.key]: !p[group.key] }))}
          onNavigate={onNavigate}
        />
      ))}
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

      <div className="border-t border-white/8 p-3">
        <Link
          href="/"
          onClick={onNavigate}
          className="flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium text-white/55 transition hover:bg-white/5 hover:text-white"
        >
          <AdminIcon name="logout" className="h-[18px] w-[18px]" />
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
      {/* Desktop: fixed sidebar on the right (RTL). */}
      <aside className="fixed inset-y-0 right-0 z-30 hidden w-64 flex-col border-l border-white/8 bg-[#0d0d14] lg:flex">
        <SidebarBody groups={groups} />
      </aside>

      {/* Mobile: slide-in drawer from the right with a dimmed backdrop. */}
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
