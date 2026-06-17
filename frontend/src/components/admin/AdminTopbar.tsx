"use client";

import { usePathname } from "next/navigation";
import { adminMenu } from "@/data/admin";
import AdminIcon from "./AdminIcon";

export default function AdminTopbar({ onMenu }: { onMenu: () => void }) {
  const pathname = usePathname();
  const current =
    [...adminMenu].sort((a, b) => b.href.length - a.href.length).find((m) =>
      m.href === "/admin" ? pathname === "/admin" : pathname.startsWith(m.href),
    ) ?? adminMenu[0];

  return (
    <header className="sticky top-0 z-20 flex h-[72px] items-center gap-4 border-b border-white/8 bg-[#0b0b12]/90 px-5 backdrop-blur lg:px-8">
      <button
        onClick={onMenu}
        aria-label="منو"
        className="grid h-10 w-10 shrink-0 place-items-center rounded-full border border-white/10 bg-white/5 text-white/70 transition hover:text-white lg:hidden"
      >
        <AdminIcon name="menu" className="h-5 w-5" />
      </button>

      <div>
        <h1 className="text-lg font-bold text-white">{current.label}</h1>
        <p className="text-xs text-white/40">پنل مدیریت فونیکس ورفای</p>
      </div>

      <div className="mr-auto flex items-center gap-3">
        <div className="hidden h-10 w-64 items-center gap-2 rounded-full border border-white/10 bg-white/5 px-4 text-white/50 md:flex">
          <AdminIcon name="search" className="h-4 w-4" />
          <input
            placeholder="جستجو..."
            className="w-full bg-transparent text-sm text-white placeholder:text-white/40 focus:outline-none"
          />
        </div>

        <button className="relative grid h-10 w-10 place-items-center rounded-full border border-white/10 bg-white/5 text-white/70 transition hover:text-white">
          <AdminIcon name="bell" className="h-5 w-5" />
          <span className="absolute right-2.5 top-2.5 h-2 w-2 rounded-full bg-[#e60053]" />
        </button>

        <div className="flex items-center gap-2.5">
          <div className="grid h-10 w-10 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-sm font-bold text-white">
            ا
          </div>
          <div className="hidden leading-tight sm:block">
            <p className="text-sm font-bold text-white">ادمین</p>
            <p className="text-xs text-white/40">مدیر کل</p>
          </div>
        </div>
      </div>
    </header>
  );
}
