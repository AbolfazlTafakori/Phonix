"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "./api";
import type { User } from "./types";

// Live snapshot of the signed-in user for the dashboard. Re-fetches on window focus / tab visibility (and a
// slow interval), so when an admin lowers or changes the user's verification level it propagates into the
// active session at once — no re-login, no restart. The store mutates in-memory, so a refetch sees it instantly.
export function useMe(): { me: User | null; refresh: () => Promise<void> } {
  const [me, setMe] = useState<User | null>(null);

  const refresh = useCallback(async () => {
    try {
      setMe(await api.account.me());
    } catch {
      /* keep the last good snapshot on a transient error */
    }
  }, []);

  useEffect(() => {
    let alive = true;
    const load = () => {
      if (alive) refresh();
    };
    load();
    window.addEventListener("focus", load);
    document.addEventListener("visibilitychange", load);
    const id = setInterval(load, 30000);
    return () => {
      alive = false;
      window.removeEventListener("focus", load);
      document.removeEventListener("visibilitychange", load);
      clearInterval(id);
    };
  }, [refresh]);

  return { me, refresh };
}

// Identity-tier badge styling shared across the dashboard (0 red, 1 amber, 2 green).
export const levelBadge = (n: number): { label: string; cls: string } => {
  const map: Record<number, { label: string; cls: string }> = {
    0: { label: "سطح ۰", cls: "bg-rose-500/15 text-rose-400" },
    1: { label: "سطح ۱", cls: "bg-amber-500/15 text-amber-300" },
    2: { label: "سطح ۲", cls: "bg-emerald-500/15 text-emerald-400" },
  };
  return map[n] ?? map[0];
};
