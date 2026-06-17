"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth";

export default function AccountGuard({ children }: { children: React.ReactNode }) {
  const { user, ready } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (ready && !user) router.replace("/login");
  }, [ready, user, router]);

  if (!ready || !user) {
    return (
      <div className="grid min-h-[50vh] place-items-center">
        <span className="inline-block h-8 w-8 animate-spin rounded-full border-2 border-white/20 border-t-[#e60053]" />
      </div>
    );
  }

  return <>{children}</>;
}
