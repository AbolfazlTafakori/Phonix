import type { ReactNode } from "react";
import Background from "@/components/Background";
import Navbar from "@/components/Navbar";
import Sidebar from "@/components/account/Sidebar";

export default function AccountLayout({ children }: { children: ReactNode }) {
  return (
    <div className="relative min-h-screen text-white">
      <Background />
      <Navbar />
      <div className="mx-auto max-w-[1320px] px-5 py-10">
        <div className="grid gap-6 lg:grid-cols-[280px_1fr]">
          <Sidebar />
          <main className="min-w-0">{children}</main>
        </div>
      </div>
    </div>
  );
}
