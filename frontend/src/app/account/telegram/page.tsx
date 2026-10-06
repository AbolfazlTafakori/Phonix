"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useTelegramLaunch } from "@/lib/telegram";
import { PageTitle, Panel } from "@/components/account/Panel";
import type { TelegramLinkStatus } from "@/lib/types";

const CODE_LENGTH = 24;
const inputCls =
  "h-12 w-full rounded-xl border border-[color:var(--ac-input-border)] bg-[color:var(--ac-input-bg)] px-4 text-sm text-[color:var(--ac-title)] outline-none transition focus:border-[color:var(--ac-input-focus)] placeholder:text-[color:var(--ac-muted)]";
const primaryBtn =
  "flex h-11 items-center justify-center gap-2 rounded-xl px-6 text-[13px] font-bold text-white transition hover:brightness-110 disabled:opacity-60";
const secondaryBtn =
  "flex h-11 items-center justify-center rounded-xl border px-5 text-[13px] font-bold transition hover:bg-[color:var(--ac-menu-hover)] disabled:opacity-60";

// Persian or Arabic digits → ASCII, everything else dropped: the code as the server reads it.
function codeDigits(value: string): string {
  return value
    .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
    .replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(/\D/g, "");
}

function toFa(n: number): string {
  return n.toLocaleString("fa-IR");
}

type Sent = { botUrl: string; email: string; minutes: number };

// «اتصال به تلگرام»: the customer has a one-time code mailed to their account's verified address and sends it to
// the bot, which ties that Telegram to the account. Inside the shop in Telegram the code is typed right here
// instead — Telegram itself says which Telegram it is. Shown only while staff offer it.
export default function TelegramPage() {
  const { user } = useAuth();
  const launch = useTelegramLaunch();
  const [status, setStatus] = useState<TelegramLinkStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [sent, setSent] = useState<Sent | null>(null);
  const [cooldown, setCooldown] = useState(false);
  const [code, setCode] = useState("");
  const [verifySent, setVerifySent] = useState(false);
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
    if (!user) return;
    let alive = true;
    api.accountTelegram
      .get()
      .then((s) => { if (alive) setStatus(s); })
      .catch(() => {})
      .finally(() => { if (alive) setLoading(false); });
    return () => {
      alive = false;
      if (poll.current) clearInterval(poll.current);
    };
  }, [user]);

  // Outside Telegram the link lands in the bot, so the page watches for it to update on its own.
  function watchForLink(minutes: number) {
    if (poll.current) clearInterval(poll.current);
    const until = Date.now() + minutes * 60 * 1000;
    poll.current = setInterval(async () => {
      const s = await load();
      if (s?.linked || Date.now() > until) {
        if (poll.current) clearInterval(poll.current);
        if (s?.linked) setSent(null);
      }
    }, 3000);
  }

  async function sendCode() {
    setError("");
    setBusy(true);
    // Opened before the request so a popup blocker sees a click, not a script, opening it.
    const win = launch ? null : window.open("", "_blank");
    try {
      const result = await api.accountTelegram.sendCode();
      setSent(result);
      setCode("");
      setCooldown(true);
      setTimeout(() => setCooldown(false), 60_000);
      if (!launch) {
        if (win) win.location.href = result.botUrl;
        else window.location.href = result.botUrl;
        watchForLink(result.minutes);
      }
    } catch (e) {
      win?.close();
      setError(e instanceof Error ? e.message : "ارسال کد ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function linkHere() {
    if (!launch) return;
    setError("");
    setBusy(true);
    try {
      await api.accountTelegram.linkFromShop(launch, codeDigits(code));
      setSent(null);
      setCode("");
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "اتصال ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function unlink() {
    // Telegram's webview doesn't reliably show confirm(); unlinking is undone with a new code anyway.
    if (!launch && !confirm("اتصال حساب شما به تلگرام قطع شود؟")) return;
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

  async function resendVerification() {
    setError("");
    try {
      await api.auth.resendVerification();
      setVerifySent(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : "ارسال ایمیل تأیید ناموفق بود.");
    }
  }

  const offered = status ? (launch ? status.shop : status.available) : false;
  const bot = status?.botUsername ? `@${status.botUsername}` : "ربات تلگرام ما";

  return (
    <div>
      <PageTitle
        title="اتصال به تلگرام"
        desc="اطلاعات سفارش‌ها، تأیید پرداخت‌ها و پیام‌های پشتیبانی را در تلگرام هم دریافت کنید."
      />

      {loading ? (
        <Panel>
          <div className="grid h-24 place-items-center">
            <span className="inline-block h-7 w-7 animate-spin rounded-full border-2 border-[rgba(166,102,45,0.2)] border-t-[#FF5A1F]" />
          </div>
        </Panel>
      ) : !status || !offered ? (
        <Panel>
          <p className="py-8 text-center" style={{ color: "var(--ac-muted)" }}>این امکان در حال حاضر فعال نیست.</p>
        </Panel>
      ) : status.linked ? (
        <Panel>
          <div className="flex items-start gap-3">
            <TelegramBadge />
            <div className="min-w-0">
              <p className="text-[15px] font-bold" style={{ color: "var(--ac-title)" }}>حساب شما به تلگرام وصل است ✅</p>
              <p className="mt-1 text-[13px] leading-6" style={{ color: "var(--ac-muted)" }}>
                اطلاعات سفارش‌ها، تأیید پرداخت‌ها و پیام‌های پشتیبانی به تلگرام
                {status.telegramUsername ? <span dir="ltr"> (@{status.telegramUsername})</span> : null} شما هم ارسال می‌شود
                {status.shop ? " و فروشگاه داخل تلگرام بدون ورود دوباره باز می‌شود." : "."}
              </p>
            </div>
          </div>
          <label className="mt-5 flex cursor-pointer items-center gap-2 text-[13px]" style={{ color: "var(--ac-text)" }}>
            <input type="checkbox" checked={status.notify} onChange={(e) => toggleNotify(e.target.checked)} className="h-4 w-4 accent-[#229ED9]" />
            ارسال پیام‌ها به تلگرام
          </label>
          {error && <p className="mt-3 text-[13px] text-rose-600">{error}</p>}
          <div className="mt-5 flex flex-wrap gap-2">
            {!launch && status.botUsername && (
              <a href={`https://t.me/${status.botUsername}`} target="_blank" rel="noopener noreferrer" className={`${primaryBtn} bg-[#229ED9]`}>
                باز کردن ربات
              </a>
            )}
            <button onClick={unlink} disabled={busy} className={secondaryBtn} style={{ color: "var(--ac-text)", borderColor: "var(--ac-panel-border)" }}>
              قطع اتصال
            </button>
          </div>
        </Panel>
      ) : (
        <Panel>
          <div className="flex items-start gap-3">
            <TelegramBadge />
            <div className="min-w-0">
              <p className="text-[15px] font-bold" style={{ color: "var(--ac-title)" }}>
                {launch ? "اتصال همین تلگرام به حساب شما" : "اتصال حساب به تلگرام"}
              </p>
              <p className="mt-1 text-[13px] leading-6" style={{ color: "var(--ac-muted)" }}>
                برای امنیت حساب، اتصال با یک کد {toFa(CODE_LENGTH)} رقمی یک‌بارمصرف انجام می‌شود که فقط به ایمیل حساب شما ارسال می‌شود.
                {status.shop ? " بعد از اتصال، فروشگاه داخل تلگرام هم بدون ورود دوباره باز می‌شود." : ""}
              </p>
            </div>
          </div>

          <ol className="mt-5 space-y-2 text-[13px] leading-6" style={{ color: "var(--ac-text)" }}>
            <li>۱. {launch ? "«ارسال کد به ایمیل» را بزنید." : "«ارسال کد و باز کردن ربات» را بزنید."}</li>
            <li>
              ۲. کد به ایمیل <span dir="ltr" className="font-bold">{status.email || "—"}</span> ارسال می‌شود.
            </li>
            <li>۳. {launch ? "کد را از ایمیل کپی کنید و همین‌جا وارد کنید." : <>کد را در ربات <span dir="ltr">{bot}</span> بفرستید.</>}</li>
          </ol>

          {!status.emailVerified ? (
            <div className="mt-5 rounded-xl px-4 py-3 text-[13px] leading-6" style={{ background: "rgba(245,158,11,0.10)", border: "1px solid rgba(245,158,11,0.3)", color: "var(--ac-text)" }}>
              <b>ابتدا ایمیل حساب خود را تأیید کنید.</b> کد اتصال فقط به ایمیل تأییدشده ارسال می‌شود.
              <div className="mt-2">
                {verifySent ? (
                  <span className="font-bold text-emerald-600">ایمیل تأیید ارسال شد؛ صندوق ایمیل خود را ببینید.</span>
                ) : (
                  <button onClick={resendVerification} className="rounded-lg border border-amber-500/40 px-3 py-1.5 text-xs font-bold text-amber-600 transition hover:bg-amber-500/10">
                    ارسال ایمیل تأیید
                  </button>
                )}
              </div>
            </div>
          ) : (
            <>
              {sent && (
                <div className="mt-5 rounded-xl px-4 py-3 text-[13px] leading-6" style={{ background: "rgba(34,158,217,0.08)", border: "1px solid rgba(34,158,217,0.28)", color: "var(--ac-text)" }}>
                  کد به ایمیل <span dir="ltr" className="font-bold">{sent.email}</span> ارسال شد و تا {toFa(sent.minutes)} دقیقه، فقط یک بار معتبر است.
                  {launch ? " آن را در کادر زیر وارد کنید." : <> آن را در ربات <span dir="ltr">{bot}</span> بفرستید؛ این صفحه بعد از اتصال خودکار به‌روز می‌شود.</>}
                  <br />
                  اگر ایمیل را نمی‌بینید، پوشه‌ی اسپم را هم نگاه کنید.
                </div>
              )}

              {launch && sent && (
                <div className="mt-4 flex flex-col gap-2 sm:flex-row">
                  <input
                    value={code}
                    onChange={(e) => setCode(e.target.value.slice(0, 60))}
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    dir="ltr"
                    placeholder={`کد ${toFa(CODE_LENGTH)} رقمی`}
                    className={`${inputCls} text-center font-mono tracking-wider`}
                  />
                  <button
                    onClick={linkHere}
                    disabled={busy || codeDigits(code).length !== CODE_LENGTH}
                    className={`${primaryBtn} shrink-0 bg-[#229ED9]`}
                  >
                    {busy ? "..." : "تأیید و اتصال"}
                  </button>
                </div>
              )}

              {error && <p className="mt-3 text-[13px] text-rose-600">{error}</p>}

              <div className="mt-5 flex flex-wrap gap-2">
                <button
                  onClick={sendCode}
                  disabled={busy || cooldown}
                  className={sent ? secondaryBtn : `${primaryBtn} bg-[#229ED9]`}
                  style={sent ? { color: "var(--ac-text)", borderColor: "var(--ac-panel-border)" } : undefined}
                >
                  {busy && !code ? "..." : sent ? (cooldown ? "ارسال دوباره (کمی صبر کنید)" : "ارسال دوباره‌ی کد") : launch ? "ارسال کد به ایمیل" : "ارسال کد و باز کردن ربات"}
                </button>
                {!launch && sent && (
                  <a href={sent.botUrl} target="_blank" rel="noopener noreferrer" className={`${primaryBtn} bg-[#229ED9]`}>
                    باز کردن ربات
                  </a>
                )}
              </div>
            </>
          )}

          <p className="mt-5 text-[12px] leading-6" style={{ color: "var(--ac-muted)" }}>
            کد را به هیچ‌کس ندهید؛ هر کس آن را داشته باشد می‌تواند تلگرام خودش را به حساب شما وصل کند. پشتیبانی هیچ‌وقت این کد را از شما نمی‌خواهد.
          </p>
        </Panel>
      )}
    </div>
  );
}

function TelegramBadge() {
  return (
    <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-[#229ED9] text-white" aria-hidden>
      <svg viewBox="0 0 24 24" className="h-6 w-6" fill="currentColor">
        <path d="M21.9 4.6 18.7 19.7c-.2 1-.9 1.3-1.8.8l-4.9-3.6-2.4 2.3c-.3.3-.5.5-1 .5l.4-5 9.1-8.2c.4-.4-.1-.6-.6-.2L6.3 13.4l-4.8-1.5c-1-.3-1.1-1 .2-1.5L20.5 3.2c.9-.3 1.6.2 1.4 1.4z" />
      </svg>
    </span>
  );
}
