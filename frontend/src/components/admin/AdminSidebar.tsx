"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { adminMenuGroups, adminTickets, adminOrders } from "@/data/admin";
import { toFa } from "@/lib/format";
import AdminIcon from "./AdminIcon";

const badges: Record<string, number> = {
  "/admin/tickets": adminTickets.filter((t) => t.status === "باز").length,
  "/admin/orders": adminOrders.filter((o) => o.status === "در انتظار").length,
};

const isActive = (href: string, pathname: string) =>
  href === "/admin" ? pathname === "/admin" : pathname.startsWith(href);

function ItemLink({ item, pathname, onNavigate }: { item: { label: string; href: string; icon: string }; pathname: string; onNavigate?: () => void }) {
  const active = isActive(item.href, pathname);
  const badge = badges[item.href];
  return (
    <Link
      href={item.href}
      onClick={onNavigate}
      className={`flex items-center gap-3 rounded-xl px-3.5 py-2.5 text-sm font-medium transition ${
        active
          ? "bg-gradient-to-l from-[#e60053]/90 to-[#9c0038]/80 text-white shadow-[0_10px_30px_-12px_rgba(230,0,83,0.7)]"
          : "text-white/60 hover:bg-white/5 hover:text-white"
      }`}
    >
      <AdminIcon name={item.icon} className="h-5 w-5 shrink-0" />
      <span className="flex-1">{item.label}</span>
      {badge ? (
        <span className={`grid h-5 min-w-5 place-items-center rounded-full px-1.5 text-[11px] font-bold ${active ? "bg-white/25 text-white" : "bg-[#e60053]/20 text-[#ff5a8a]"}`}>
          {toFa(badge)}
        </span>
      ) : null}
    </Link>
  );
}

function NavLinks({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();
  const activeGroup = adminMenuGroups.find((g) => g.title && g.items.some((it) => isActive(it.href, pathname)))?.title;
  const [open, setOpen] = useState<Record<string, boolean>>(() => (activeGroup ? { [activeGroup]: true } : {}));

  useEffect(() => {
    if (activeGroup) setOpen((p) => ({ ...p, [activeGroup]: true }));
  }, [activeGroup]);

  return (
    <nav className="flex-1 space-y-2 overflow-y-auto px-3 py-4">
      {adminMenuGroups.map((group, gi) => {
        if (!group.title) {
          return (
            <div key={gi} className="space-y-1">
              {group.items.map((item) => (
                <ItemLink key={item.href} item={item} pathname={pathname} onNavigate={onNavigate} />
              ))}
            </div>
          );
        }

        const isOpen = open[group.title] ?? false;
        const groupActive = group.items.some((it) => isActive(it.href, pathname));
        const groupBadge = group.items.reduce((sum, it) => sum + (badges[it.href] ?? 0), 0);

        return (
          <div key={group.title}>
            <button
              onClick={() => setOpen((p) => ({ ...p, [group.title]: !p[group.title] }))}
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
                <ItemLink key={item.href} item={item} pathname={pathname} onNavigate={onNavigate} />
              ))}
            </div>
          </div>
        );
      })}
    </nav>
  );
}

function SidebarBody({ onNavigate }: { onNavigate?: () => void }) {
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

      <NavLinks onNavigate={onNavigate} />

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
  return (
    <>
      <aside className="fixed inset-y-0 right-0 z-30 hidden w-64 flex-col border-l border-white/8 bg-[#0d0d14] lg:flex">
        <SidebarBody />
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
          <SidebarBody onNavigate={onClose} />
        </aside>
      </div>
    </>
  );
}
