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
  const isTwoFactorPage = pathname === "/admin/settings/2fa";

  useEffect(() => {
    if (isLogin || !ready) return;
    if (!user) {
      router.replace("/admin/login");
      return;
    }
    // The panel requires an ADMIN-SCOPED session (panel login + 2FA), not just any logged-in admin. A plain
    // main-site session — even an admin's — fails admin-context (403), so pasting a panel URL bounces back to
    // the panel login. This is also the server's rule; every admin API call is gated the same way.
    let cancelled = false;
    api.auth
      .adminContext()
      .then(async (ctx) => {
        if (cancelled) return;
        if (!adminRoles.includes(ctx.role)) {
          clearAdminUser();
          router.replace("/admin/login");
          return;
        }
        // Mandatory 2FA: a staff member who hasn't enrolled is forced onto the security page and can do
        // nothing else until it's active (the server gate enforces this too — this is the UX side).
        const { enabled } = await api.auth.twoFactor.status();
        if (cancelled) return;
        if (!enabled && !isTwoFactorPage) {
          router.replace("/admin/settings/2fa");
          return;
        }
        setVerified(true);
      })
      .catch(() => {
        if (!cancelled) router.replace("/admin/login");
      });
    return () => {
      cancelled = true;
    };
  }, [ready, user, isLogin, isTwoFactorPage, router]);

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
    <div className="min-h-screen bg-[#0b0b12] text-white transition-[padding] duration-300 ease-out lg:pr-64">
      <AdminSidebar open={open} onClose={() => setOpen(false)} />
      <AdminTopbar onMenu={() => setOpen(true)} />
      <main className="mx-auto w-full max-w-[1600px] p-4 sm:p-6 lg:p-8">{children}</main>
    </div>
  );
}
