"use client";

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
  // Live profile of the signed-in staff member, so the avatar they set on the normal site shows here too
  // (and updates on focus without a re-login).
  const { me } = useMe();
  // Page title comes from the same (role-filtered) menu the sidebar uses — one source of truth.
  const items = useAdminMenu().flatMap((g) => g.items);
  const current = [...items]
    .sort((a, b) => b.route.length - a.route.length)
    .find((m) => (m.route === "/admin" ? pathname === "/admin" : pathname.startsWith(m.route)));
  const title = current?.title ?? "پنل مدیریت";

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
        <h1 className="text-lg font-bold text-white">{title}</h1>
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
          {me?.avatar ? (
            <img src={me.avatar} alt={me.name || me.username} className="h-10 w-10 shrink-0 rounded-full object-cover" />
          ) : (
            <div className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-sm font-bold text-white">
              {(me?.name || user?.name || user?.username || "ا").charAt(0)}
            </div>
          )}
          <div className="hidden leading-tight sm:block">
            <p className="text-sm font-bold text-white">{me?.name || user?.name || user?.username}</p>
            <p className="text-xs text-white/40">{user ? roleLabel[user.role] ?? "مدیر" : ""}</p>
          </div>
        </div>

        <button
          onClick={() => { logout(); router.replace("/admin/login"); }}
          aria-label="خروج"
          title="خروج"
          className="grid h-10 w-10 place-items-center rounded-full border border-white/10 bg-white/5 text-white/60 transition hover:border-rose-500/50 hover:text-rose-400"
        >
          <AdminIcon name="logout" className="h-5 w-5" />
        </button>
      </div>
    </header>
  );
}
