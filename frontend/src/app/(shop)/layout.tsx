import type { ReactNode } from "react";
import Background from "@/components/Background";
import SiteHeader from "@/components/home/SiteHeader";
import SiteFooter from "@/components/home/SiteFooter";

export default function ShopLayout({ children }: { children: ReactNode }) {
  return (
    <div className="relative min-h-screen text-white">
      <Background />
      <SiteHeader />
      <main>{children}</main>
      <SiteFooter />
    </div>
  );
}
