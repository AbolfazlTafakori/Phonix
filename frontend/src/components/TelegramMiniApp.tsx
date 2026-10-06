"use client";

import { useEffect } from "react";
import { usePathname, useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import {
  onTelegramEvent,
  stripLaunchFragment,
  telegramAutoSignInSuppressed,
  telegramPost,
  useTelegramLaunch,
} from "@/lib/telegram";

// Light/dark header colours — the same pair the browser chrome gets (layout.tsx `themeColor`).
const LIGHT = "#f8f1ea";
const DARK = "#0e0e12";

// Once per page load: a customer who isn't linked gets one try, not one per render.
let signInTried = false;

// Runs the site as the shop inside Telegram. Outside Telegram it does nothing at all. Inside, it tells Telegram
// the page is ready and full-height, matches Telegram's header to the site's theme, drives Telegram's own back
// button, and signs in the customer linked to this Telegram.
export default function TelegramMiniApp() {
  const launch = useTelegramLaunch();
  const pathname = usePathname();
  const router = useRouter();
  const { user, ready, login } = useAuth();

  useEffect(() => {
    if (!launch) return;
    stripLaunchFragment();
    telegramPost("web_app_ready");
    telegramPost("web_app_expand");

    const paint = () => {
      const color = document.documentElement.classList.contains("home-dark") ? DARK : LIGHT;
      telegramPost("web_app_set_header_color", { color });
      telegramPost("web_app_set_background_color", { color });
    };
    paint();
    // The theme switch flips a class on <html>; follow it.
    const observer = new MutationObserver(paint);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
    return () => observer.disconnect();
  }, [launch]);

  // Telegram's back button stands in for the browser's, which a webview doesn't have.
  useEffect(() => {
    if (!launch) return;
    telegramPost("web_app_setup_back_button", { is_visible: pathname !== "/" });
  }, [launch, pathname]);

  useEffect(() => {
    if (!launch) return;
    return onTelegramEvent("back_button_pressed", () => {
      if (window.history.length > 1) router.back();
      else router.push("/");
    });
  }, [launch, router]);

  useEffect(() => {
    if (!launch || !ready || user || signInTried || telegramAutoSignInSuppressed()) return;
    signInTried = true;
    api.auth
      .telegram(launch)
      .then((res) => {
        if (!res.linked || !res.user) return;
        const u = res.user;
        login({ id: u.id, name: u.name, username: u.username, email: u.email, avatar: u.avatar });
        if (pathname === "/login" || pathname === "/signup") router.replace("/account");
      })
      .catch(() => {
        // Not linked, stale launch data, shop switched off: the site works as usual, with its own sign-in.
      });
  }, [launch, ready, user, login, pathname, router]);

  return null;
}
