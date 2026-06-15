"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { accountMenu } from "@/data/account";
import MenuIcon from "./MenuIcon";

export default function Sidebar() {
  const pathname = usePathname();

  return (
    <aside className="h-fit rounded-2xl border border-white/8 bg-[#15151f]/80 p-5 lg:sticky lg:top-24">
      <div className="mb-6 flex items-center gap-3 border-b border-white/8 pb-5">
        <div className="grid h-12 w-12 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-lg font-bold text-white">
          ف
        </div>
        <div>
          <p className="text-sm font-bold text-white">کاربر فونیکس</p>
          <p className="text-xs text-white/50">@phoenix_user</p>
        </div>
      </div>

      <nav className="flex flex-col gap-1">
        {accountMenu.map((item) => {
          const active = pathname === item.href;
          return (
            <Link
              key={item.href}
              href={item.href}
              className={`flex items-center gap-3 rounded-xl px-4 py-3 text-sm font-medium transition ${
                active
                  ? "bg-gradient-to-l from-[#e60053]/90 to-[#9c0038]/80 text-white shadow-[0_10px_30px_-12px_rgba(230,0,83,0.7)]"
                  : "text-white/70 hover:bg-white/5 hover:text-white"
              }`}
            >
              <MenuIcon name={item.icon} className="h-5 w-5" />
              {item.label}
            </Link>
          );
        })}

        <button className="mt-3 flex items-center gap-3 rounded-xl px-4 py-3 text-sm font-medium text-white/55 transition hover:bg-white/5 hover:text-white">
          <MenuIcon name="logout" className="h-5 w-5" />
          خروج از حساب
        </button>
      </nav>
    </aside>
  );
}
