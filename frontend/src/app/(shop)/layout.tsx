import type { ReactNode } from "react";
import SiteHeader from "@/components/home/SiteHeader";
import SiteFooter from "@/components/home/SiteFooter";
import MobileTabBar from "@/components/home/MobileTabBar";
import FooterVisibility from "@/components/home/FooterVisibility";

export default function ShopLayout({ children }: { children: ReactNode }) {
  return (
    // pad the bottom on mobile so the fixed tab bar never covers the footer
    <div className="home-light relative flex min-h-screen flex-col pb-[60px] lg:pb-0">
      <SiteHeader />
      <main className="flex-1">{children}</main>
      <FooterVisibility><SiteFooter /></FooterVisibility>
      <MobileTabBar />
    </div>
  );
}
