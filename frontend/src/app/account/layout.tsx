import type { ReactNode } from "react";
import SiteHeader from "@/components/home/SiteHeader";
import SiteFooter from "@/components/home/SiteFooter";
import Sidebar from "@/components/account/Sidebar";
import AccountGuard from "@/components/account/AccountGuard";

export default function AccountLayout({ children }: { children: ReactNode }) {
  return (
    <div className="home-light relative flex min-h-screen flex-col">
      <SiteHeader />
      <div className="mx-auto w-full max-w-[1320px] flex-1 px-5 py-10">
        <AccountGuard>
          <div className="grid gap-6 lg:grid-cols-[280px_1fr]">
            <Sidebar />
            <main className="min-w-0">{children}</main>
          </div>
        </AccountGuard>
      </div>
      <SiteFooter />
    </div>
  );
}
