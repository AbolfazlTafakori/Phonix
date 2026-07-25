"use client";

import { useSyncExternalStore } from "react";
import { api } from "./api";

export type CurrentUser = { id: number; name: string; username: string; email: string; phone?: string; avatar?: string };

const KEY = "phonix_user";
export const AUTH_EVENT = "phonix-auth-change";

export function getCurrentUser(): CurrentUser | null {
  if (typeof window === "undefined") return null;
  try {
    const raw = localStorage.getItem(KEY);
    return raw ? (JSON.parse(raw) as CurrentUser) : null;
  } catch {
    return null;
  }
}

export function setCurrentUser(user: CurrentUser) {
  localStorage.setItem(KEY, JSON.stringify(user));
  window.dispatchEvent(new Event(AUTH_EVENT));
}

export function clearCurrentUser() {
  api.auth.logout().catch(() => {});
  localStorage.removeItem(KEY);
  window.dispatchEvent(new Event(AUTH_EVENT));
}

// The signed-in user lives in localStorage, which is an external store — so it is read through React's own
// API for that rather than mirrored into state by an effect. The snapshot must return the SAME object while
// the stored text is unchanged, or every render would look like a change and loop.
let cachedRaw: string | null = null;
let cachedUser: CurrentUser | null = null;

function userSnapshot(): CurrentUser | null {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw !== cachedRaw) {
      cachedRaw = raw;
      cachedUser = raw ? (JSON.parse(raw) as CurrentUser) : null;
    }
    return cachedUser;
  } catch {
    return null;
  }
}

function subscribeAuth(onChange: () => void): () => void {
  window.addEventListener(AUTH_EVENT, onChange);
  window.addEventListener("storage", onChange);
  return () => {
    window.removeEventListener(AUTH_EVENT, onChange);
    window.removeEventListener("storage", onChange);
  };
}

// The server cannot know who is signed in, so it renders as signed-out and not-yet-ready; React swaps in the
// real values right after hydration. `ready` is what stops a guarded page redirecting during that first pass.
const serverUser = () => null;
const serverReady = () => false;
const clientReady = () => true;

export function useAuth() {
  const user = useSyncExternalStore(subscribeAuth, userSnapshot, serverUser);
  const ready = useSyncExternalStore(subscribeAuth, clientReady, serverReady);

  return { user, ready, login: setCurrentUser, logout: clearCurrentUser };
}
