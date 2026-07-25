"use client";

import { useSyncExternalStore } from "react";
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

// Read through React's external-store API rather than mirrored into state by an effect. The snapshot has to
// return the SAME object while the stored text is unchanged, or each render would look like a change.
let cachedRaw: string | null = null;
let cachedAdmin: AdminUser | null = null;

function adminSnapshot(): AdminUser | null {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw !== cachedRaw) {
      cachedRaw = raw;
      cachedAdmin = raw ? (JSON.parse(raw) as AdminUser) : null;
    }
    return cachedAdmin;
  } catch {
    return null;
  }
}

function subscribeAdmin(onChange: () => void): () => void {
  window.addEventListener(EVENT, onChange);
  window.addEventListener("storage", onChange);
  return () => {
    window.removeEventListener(EVENT, onChange);
    window.removeEventListener("storage", onChange);
  };
}

// The server renders as signed-out and not-yet-ready; React swaps in the real values after hydration, and
// `ready` is what keeps a guarded page from redirecting during that first pass.
const serverAdmin = () => null;
const serverReady = () => false;
const clientReady = () => true;

export function useAdminAuth() {
  const user = useSyncExternalStore(subscribeAdmin, adminSnapshot, serverAdmin);
  const ready = useSyncExternalStore(subscribeAdmin, clientReady, serverReady);

  return { user, ready, login: setAdminUser, logout: clearAdminUser };
}
