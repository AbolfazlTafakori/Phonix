"use client";

import { useEffect, useState, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useAdminAuth, adminRoles, clearAdminUser } from "@/lib/adminAuth";
import { api } from "@/lib/api";
import AdminSidebar from "./AdminSidebar";
import AdminTopbar from "./AdminTopbar";

export default function AdminShell({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const [verified, setVerified] = useState(false);
  const pathname = usePathname();
  const router = useRouter();
  const { user, ready } = useAdminAuth();
  const isLogin = pathname === "/admin/login";

  useEffect(() => {
    if (isLogin || !ready) return;
    if (!user) {
      router.replace("/admin/login");
      return;
    }
    // Don't trust the stored admin marker alone: re-check the live session's role with the server so a
    // hand-edited localStorage entry can't even render the panel. (Every admin API call is gated too;
    // this just stops a non-staff user from entering the section at all.)
    let cancelled = false;
    api.account
      .me()
      .then((me) => {
        if (cancelled) return;
        if (adminRoles.includes(me.role)) setVerified(true);
        else {
          clearAdminUser();
          router.replace("/admin/login");
        }
      })
      .catch(() => {
        if (!cancelled) router.replace("/admin/login");
      });
    return () => {
      cancelled = true;
    };
  }, [ready, user, isLogin, router]);

  // login page renders standalone, without the admin chrome
  if (isLogin) return <>{children}</>;

  // block protected content until the admin session is confirmed against the server
  if (!ready || !user || !verified) {
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
