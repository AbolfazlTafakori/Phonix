"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import type { TelegramLinkStatus } from "@/lib/types";

// «اتصال به تلگرام» on the account page. Renders nothing at all unless staff have switched the customer bot on
// for customers — while it is being set up, nobody sees a button that leads nowhere.
//
// Linking: the site mints a one-time t.me link, the customer opens it and presses Start, and the bot ties that
// chat to this account. The card watches for the link to land so it updates on its own.
export default function TelegramLinkCard() {
  const [status, setStatus] = useState<TelegramLinkStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const [waiting, setWaiting] = useState(false);
  const [error, setError] = useState("");
  const poll = useRef<ReturnType<typeof setInterval> | null>(null);

  const load = useCallback(async () => {
    try {
      const s = await api.accountTelegram.get();
      setStatus(s);
      return s;
    } catch {
      return null;
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    api.accountTelegram
      .get()
      .then((s) => { if (!cancelled) setStatus(s); })
      .catch(() => { /* not available: the card stays hidden */ });
    return () => {
      cancelled = true;
      if (poll.current) clearInterval(poll.current);
    };
  }, []);

  async function connect() {
    setError("");
    setBusy(true);
    // Opened before the request so a popup blocker sees a click, not a script, opening it.
    const win = window.open("", "_blank");
    try {
      const { url } = await api.accountTelegram.link();
      if (win) win.location.href = url;
      else window.location.href = url;
      // Wait (up to 15 minutes, the link's lifetime) for the customer to press Start in Telegram.
      setWaiting(true);
      const started = Date.now();
      if (poll.current) clearInterval(poll.current);
      poll.current = setInterval(async () => {
        const s = await load();
        if (s?.linked || Date.now() - started > 15 * 60 * 1000) {
          if (poll.current) clearInterval(poll.current);
          setWaiting(false);
        }
      }, 3000);
    } catch (e) {
      win?.close();
      setError(e instanceof Error ? e.message : "اتصال ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function unlink() {
    if (!confirm("اتصال حساب شما به تلگرام قطع شود؟")) return;
    setBusy(true);
    setError("");
    try {
      await api.accountTelegram.unlink();
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "قطع اتصال ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function toggleNotify(notify: boolean) {
    setStatus((s) => (s ? { ...s, notify } : s));
    try {
      await api.accountTelegram.setNotify(notify);
    } catch {
      await load();
    }
  }

  if (!status?.available) return null;

  return (
    <div
      className="flex flex-col gap-4 rounded-[18px] p-5 sm:flex-row sm:items-center sm:justify-between"
      style={{ background: "var(--ac-panel-bg)", border: "1px solid var(--ac-panel-border)" }}
    >
      <div className="flex items-start gap-3">
        <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-[#229ED9] text-white" aria-hidden>
          <svg viewBox="0 0 24 24" className="h-6 w-6" fill="currentColor">
            <path d="M21.9 4.6 18.7 19.7c-.2 1-.9 1.3-1.8.8l-4.9-3.6-2.4 2.3c-.3.3-.5.5-1 .5l.4-5 9.1-8.2c.4-.4-.1-.6-.6-.2L6.3 13.4l-4.8-1.5c-1-.3-1.1-1 .2-1.5L20.5 3.2c.9-.3 1.6.2 1.4 1.4z" />
          </svg>
        </span>
        <div>
          <p className="text-[15px] font-bold" style={{ color: "var(--ac-title)" }}>
            {status.linked ? "حساب شما به تلگرام وصل است ✅" : "اطلاعات سفارش‌ها را در تلگرام بگیرید"}
          </p>
          <p className="mt-1 text-[13px] leading-6" style={{ color: "var(--ac-muted)" }}>
            {status.linked
              ? `اطلاعات سفارش‌ها، تأیید پرداخت‌ها و پیام‌های پشتیبانی به تلگرام${status.telegramUsername ? ` (@${status.telegramUsername})` : ""} شما هم ارسال می‌شود.`
              : waiting
                ? "در تلگرام روی «Start» بزنید؛ این صفحه بعد از اتصال خودکار به‌روز می‌شود."
                : "با یک کلیک حسابتان را به ربات تلگرام فونیکس وصل کنید تا پیام‌ها و اطلاعات سفارش‌ها را همان‌جا هم دریافت کنید."}
          </p>
          {status.linked && (
            <label className="mt-2 flex cursor-pointer items-center gap-2 text-[13px]" style={{ color: "var(--ac-text)" }}>
              <input type="checkbox" checked={status.notify} onChange={(e) => toggleNotify(e.target.checked)} className="h-4 w-4 accent-[#229ED9]" />
              ارسال پیام‌ها به تلگرام
            </label>
          )}
          {error && <p className="mt-2 text-[13px] text-rose-600">{error}</p>}
        </div>
      </div>
      <div className="flex shrink-0 gap-2">
        {status.linked ? (
          <button
            onClick={unlink}
            disabled={busy}
            className="h-10 rounded-xl border px-4 text-[13px] font-bold transition hover:bg-[color:var(--ac-menu-hover)] disabled:opacity-60"
            style={{ color: "var(--ac-text)", borderColor: "var(--ac-panel-border)" }}
          >
            قطع اتصال
          </button>
        ) : (
          <button
            onClick={connect}
            disabled={busy}
            className="h-10 rounded-xl bg-[#229ED9] px-5 text-[13px] font-bold text-white transition hover:brightness-110 disabled:opacity-60"
          >
            {busy ? "..." : waiting ? "باز کردن دوباره‌ی تلگرام" : "اتصال به تلگرام"}
          </button>
        )}
      </div>
    </div>
  );
}
