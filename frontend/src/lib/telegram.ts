"use client";

import { useSyncExternalStore } from "react";

// The shop inside Telegram (a Telegram Mini App). Telegram opens the site in its own webview and hands over the
// launch data — who opened it, signed by Telegram — in the URL fragment (#tgWebAppData=…).
//
// Telegram's own helper script is served from telegram.org, which is filtered in Iran even when the Telegram app
// itself works through a proxy, so it is NOT loaded. The little this site needs (ready, full height, the back
// button, header colour) is spoken directly in Telegram's documented web-events protocol:
// https://core.telegram.org/api/web-events. The site refuses to be framed, so only the apps' native webviews
// (phone and desktop) apply — not web.telegram.org.

const LAUNCH_KEY = "phonix_tg_launch";
// Set on sign-out inside Telegram, so the shop doesn't sign the customer straight back in.
const NO_AUTO_KEY = "phonix_tg_noauto";

let launch: string | null | undefined;

function readLaunch(): string | null {
  if (launch !== undefined) return launch;
  let value: string | null = null;
  try {
    const hash = window.location.hash.slice(1);
    if (hash.includes("tgWebAppData=")) {
      value = new URLSearchParams(hash).get("tgWebAppData");
      if (value) sessionStorage.setItem(LAUNCH_KEY, value);
    }
    // A reload inside the same webview keeps the session but not necessarily the fragment.
    if (!value) value = sessionStorage.getItem(LAUNCH_KEY);
  } catch {
    // storage blocked: only the fragment counts
  }
  launch = value || null;
  return launch;
}

const noSubscribe = () => () => {};

// Telegram's signed launch data while the site runs inside Telegram, null everywhere else (and on the server).
export function useTelegramLaunch(): string | null {
  return useSyncExternalStore(noSubscribe, readLaunch, () => null);
}

// The fragment carries the signed launch data: keep it out of any link the customer copies or shares.
export function stripLaunchFragment() {
  if (window.location.hash.includes("tgWebAppData=")) {
    window.history.replaceState(window.history.state, "", window.location.pathname + window.location.search);
  }
}

export function suppressTelegramAutoSignIn() {
  try { sessionStorage.setItem(NO_AUTO_KEY, "1"); } catch { /* ignore */ }
}

export function telegramAutoSignInSuppressed(): boolean {
  try { return sessionStorage.getItem(NO_AUTO_KEY) === "1"; } catch { return false; }
}

// ── the bridge ──

type TelegramWindow = {
  TelegramWebviewProxy?: { postEvent: (eventType: string, eventData?: string) => void };
  Telegram?: { WebView?: { receiveEvent?: (eventType: string, eventData: unknown) => void } };
  TelegramGameProxy?: { receiveEvent: (eventType: string, eventData: unknown) => void };
  TelegramGameProxy_receiveEvent?: (eventType: string, eventData: unknown) => void;
};

export function telegramPost(eventType: string, eventData?: Record<string, unknown>) {
  try {
    (window as unknown as TelegramWindow).TelegramWebviewProxy?.postEvent(
      eventType,
      eventData === undefined ? undefined : JSON.stringify(eventData),
    );
  } catch {
    // an older client that doesn't know this event
  }
}

const listeners = new Map<string, Set<(data: unknown) => void>>();

function receive(eventType: string, eventData: unknown) {
  listeners.get(eventType)?.forEach((fn) => fn(eventData));
}

let installed = false;

// Telegram calls back into the page through these globals (the newer name, and the older ones some clients use).
function install() {
  if (installed) return;
  installed = true;
  const w = window as unknown as TelegramWindow;
  w.Telegram = { ...w.Telegram, WebView: { ...w.Telegram?.WebView, receiveEvent: receive } };
  w.TelegramGameProxy = { receiveEvent: receive };
  w.TelegramGameProxy_receiveEvent = receive;
}

export function onTelegramEvent(eventType: string, fn: (data: unknown) => void): () => void {
  install();
  let set = listeners.get(eventType);
  if (!set) listeners.set(eventType, (set = new Set()));
  set.add(fn);
  return () => { set.delete(fn); };
}
