import type { ReactNode } from "react";
import Background from "@/components/Background";
import SiteHeader from "@/components/home/SiteHeader";
import Footer from "@/components/Footer";

export default function ShopLayout({ children }: { children: ReactNode }) {
  return (
    <div className="relative min-h-screen text-white">
      <Background />
      <SiteHeader />
      <main>{children}</main>
      <Footer />
    </div>
  );
}
