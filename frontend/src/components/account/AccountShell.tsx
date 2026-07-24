"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import Sidebar from "./Sidebar";
import { accountMenu } from "@/data/account";

/**
 * Account layout shell. On desktop it keeps the classic sidebar + content split. On mobile it behaves like a
 * marketplace app: `/account` is a hub (profile + menu), and each menu entry opens as its own full-screen
 * page with a back button to the hub — so the menu never sits alongside the content on a small screen.
 */
export default function AccountShell({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const isHub = pathname === "/account" || pathname === "/account/";
  // Title for the mobile back bar: the deepest menu entry whose route we're inside.
  const current = accountMenu.find((m) => m.href !== "/account" && pathname.startsWith(m.href));
  const title = current?.label ?? "حساب کاربری";

  return (
    <div className="grid gap-5 lg:grid-cols-[280px_1fr]">
      {/* menu hub — always on desktop; on mobile only on the account index */}
      <div className={isHub ? "" : "hidden lg:block"}>
        <Sidebar />
      </div>

      {/* page content — always on desktop; on mobile only on a sub-page, with a back bar to the hub */}
      <main className={`min-w-0 ${isHub ? "hidden lg:block" : ""}`}>
        {!isHub && (
          <div className="mb-4 flex items-center gap-2 lg:hidden">
            <Link
              href="/account"
              aria-label="بازگشت"
              className="grid h-9 w-9 shrink-0 place-items-center rounded-full border transition active:scale-95"
              style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-panel-bg)", color: "var(--ac-text)" }}
            >
              <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m9 6 6 6-6 6" /></svg>
            </Link>
            <h1 className="text-[16px] font-black" style={{ color: "var(--ac-title)" }}>{title}</h1>
          </div>
        )}
        {children}
      </main>
    </div>
  );
}
