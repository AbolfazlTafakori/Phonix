"use client";

import { useEffect, useState } from "react";
import { api } from "./api";
import type { AdminNavGroup } from "./types";

// Single source of truth for the admin sidebar/topbar: one shared fetch + poll across every component
// that needs it (module-level cache + subscriber set), so badges stay fresh without N duplicate requests.
let cache: AdminNavGroup[] = [];
const subscribers = new Set<(g: AdminNavGroup[]) => void>();
let timer: ReturnType<typeof setInterval> | null = null;

function refresh() {
  api.admin
    .menu()
    .then((groups) => {
      cache = groups;
      subscribers.forEach((notify) => notify(groups));
    })
    .catch(() => {
      /* keep the last good menu on transient errors */
    });
}

export function useAdminMenu(): AdminNavGroup[] {
  const [groups, setGroups] = useState<AdminNavGroup[]>(cache);

  useEffect(() => {
    subscribers.add(setGroups);
    if (cache.length) setGroups(cache);
    refresh(); // refresh on mount so badge counts are current

    if (!timer) {
      timer = setInterval(refresh, 45000); // periodic badge refresh
      window.addEventListener("focus", refresh);
    }

    return () => {
      subscribers.delete(setGroups);
      if (subscribers.size === 0 && timer) {
        clearInterval(timer);
        timer = null;
        window.removeEventListener("focus", refresh);
      }
    };
  }, []);

  return groups;
}
