import type { ReactNode } from "react";
import Background from "@/components/Background";
import Navbar from "@/components/Navbar";
import Footer from "@/components/Footer";

export default function ShopLayout({ children }: { children: ReactNode }) {
  return (
    <div className="relative min-h-screen text-white">
      <Background />
      <Navbar />
      <main>{children}</main>
      <Footer />
    </div>
  );
}
