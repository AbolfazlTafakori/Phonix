"use client";

import { useEffect, useState } from "react";
import type { UserRole } from "./types";
import { api } from "./api";

export type AdminUser = { id: number; name: string; username: string; role: UserRole };

const KEY = "phonix_admin";
const EVENT = "phonix-admin-change";

export const adminRoles: UserRole[] = ["Admin", "Support"];

export function getAdminUser(): AdminUser | null {
  if (typeof window === "undefined") return null;
  try {
    const raw = localStorage.getItem(KEY);
    return raw ? (JSON.parse(raw) as AdminUser) : null;
  } catch {
    return null;
  }
}

export function setAdminUser(user: AdminUser) {
  localStorage.setItem(KEY, JSON.stringify(user));
  window.dispatchEvent(new Event(EVENT));
}

export function clearAdminUser() {
  api.auth.logout().catch(() => {});
  localStorage.removeItem(KEY);
  window.dispatchEvent(new Event(EVENT));
}

export function useAdminAuth() {
  const [user, setUser] = useState<AdminUser | null>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    const sync = () => setUser(getAdminUser());
    sync();
    setReady(true);
    window.addEventListener(EVENT, sync);
    window.addEventListener("storage", sync);
    return () => {
      window.removeEventListener(EVENT, sync);
      window.removeEventListener("storage", sync);
    };
  }, []);

  return { user, ready, login: setAdminUser, logout: clearAdminUser };
}
