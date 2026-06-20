"use client";

import { useEffect, useState, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useAdminAuth } from "@/lib/adminAuth";
import AdminSidebar from "./AdminSidebar";
import AdminTopbar from "./AdminTopbar";

export default function AdminShell({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const pathname = usePathname();
  const router = useRouter();
  const { user, ready } = useAdminAuth();
  const isLogin = pathname === "/admin/login";

  useEffect(() => {
    if (ready && !user && !isLogin) router.replace("/admin/login");
  }, [ready, user, isLogin, router]);

  // login page renders standalone, without the admin chrome
  if (isLogin) return <>{children}</>;

  // block protected content until the admin session is confirmed
  if (!ready || !user) {
    return (
      <div className="grid min-h-screen place-items-center bg-[#0b0b12]">
        <span className="inline-block h-8 w-8 animate-spin rounded-full border-2 border-white/20 border-t-[#e60053]" />
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-[#0b0b12] text-white lg:pr-64">
      <AdminSidebar open={open} onClose={() => setOpen(false)} />
      <AdminTopbar onMenu={() => setOpen(true)} />
      <main className="p-5 lg:p-8">{children}</main>
    </div>
  );
}
