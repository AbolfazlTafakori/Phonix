import type { ReactNode } from "react";
import SiteHeader from "@/components/home/SiteHeader";
import SiteFooter from "@/components/home/SiteFooter";
import AccountGuard from "@/components/account/AccountGuard";
import AccountShell from "@/components/account/AccountShell";
import MobileTabBar from "@/components/home/MobileTabBar";

export default function AccountLayout({ children }: { children: ReactNode }) {
  return (
    <div className="home-light relative flex min-h-screen flex-col pb-[60px] lg:pb-0" style={{ background: "var(--ac-page-bg)" }}>
      <SiteHeader />
      <div className="mx-auto w-full max-w-[1320px] flex-1 px-4 py-8 md:px-6 md:py-10">
        <AccountGuard>
          <AccountShell>{children}</AccountShell>
        </AccountGuard>
      </div>
      <SiteFooter />
      <MobileTabBar />
    </div>
  );
}
