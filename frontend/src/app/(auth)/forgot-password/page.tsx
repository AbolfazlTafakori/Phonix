"use client";

import { useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import AuthShell from "@/components/auth/AuthShell";
import Img from "@/components/ui/Img";

const RESET_IMAGE = "/figma/auth-reset.png";

const gradBtn =
  "flex h-12 w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-l from-[#ef233c] to-[#ff7a2e] text-[14px] font-extrabold text-white shadow-[0_14px_30px_-12px_rgba(239,35,60,0.6)] transition hover:brightness-[1.06] disabled:cursor-not-allowed disabled:opacity-60";
const outlineBtn =
  "flex h-12 w-full items-center justify-center gap-2 rounded-xl border border-[#ff5a1f]/40 text-[14px] font-bold text-[var(--chat-ink)] transition hover:bg-[#ff5a1f]/10";

const Chevron = () => (
  <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><path d="M15 6l-6 6 6 6" /></svg>
);

export default function ForgotPasswordPage() {
  const [identifier, setIdentifier] = useState("");
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      await api.auth.forgot(identifier.trim());
      setSent(true);
    } catch {
      setSent(true);
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthShell image={RESET_IMAGE}>
      {/* header: brand + step badge */}
      <div className="mb-6 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Img src="/figma/logo-phoenix.png" alt="Phoenix Verify" className="h-10 w-auto" sizes="240px" />
          <span className="text-[16px] font-extrabold leading-[1.05] text-[var(--chat-ink)]">Phoenix<br />Verify</span>
        </div>
        <span className="flex items-center gap-1.5 rounded-full border border-[var(--chat-border)] px-3 py-1.5 text-[11px] font-bold text-[var(--chat-ink-2)]">
          <span className="grid h-4 w-4 place-items-center rounded-full bg-[#ff5a1f] text-[9px] text-white">۱</span>
          ورود اطلاعات
        </span>
      </div>

      {sent ? (
        <div className="py-6 text-center">
          <div className="mx-auto mb-4 grid h-14 w-14 place-items-center rounded-full bg-emerald-500/15 text-2xl text-emerald-500">✓</div>
          <h1 className="text-[18px] font-extrabold text-[var(--chat-ink)]">لینک ارسال شد</h1>
          <p className="mt-2 text-[13px] leading-7 text-[var(--chat-ink-2)]">اگر این ایمیل/شماره در سیستم ثبت شده باشد، لینک یا کد بازیابی برای شما ارسال خواهد شد.</p>
          <Link href="/login" className={`mt-6 ${outlineBtn}`}>بازگشت به صفحه ورود<Chevron /></Link>
        </div>
      ) : (
        <>
          <h1 className="text-[24px] font-extrabold text-[var(--chat-ink)]">فراموشی رمز عبور</h1>
          <p className="mt-2 text-[13px] leading-7 text-[var(--chat-ink-2)]">برای بازیابی حساب، ایمیل یا شماره موبایل خود را وارد کنید تا لینک یا کد بازیابی برای شما ارسال شود.</p>

          <form onSubmit={submit} className="mt-6 flex flex-col gap-3">
            <div className="flex h-12 items-center gap-2.5 rounded-xl border border-[var(--chat-border)] bg-[var(--chat-surface)] px-3.5 transition focus-within:border-[#ff7a2e] focus-within:ring-2 focus-within:ring-[#ff7a2e]/15">
              <span className="shrink-0 text-[#9aa0ab]">
                <svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="5" width="18" height="14" rx="2" /><path d="m3 7 9 6 9-6" /></svg>
              </span>
              <input value={identifier} onChange={(e) => setIdentifier(e.target.value)} dir="ltr" autoComplete="off" placeholder="ایمیل یا شماره موبایل" className="flex-1 bg-transparent text-left text-[13.5px] text-[var(--chat-ink)] outline-none placeholder:text-[var(--chat-muted)]" />
            </div>

            <button type="submit" disabled={busy} className={`mt-1 ${gradBtn}`}>
              {busy ? "در حال ارسال..." : "ارسال لینک بازیابی"}
              {!busy && <Chevron />}
            </button>
            <Link href="/login" className={outlineBtn}>بازگشت به صفحه ورود<Chevron /></Link>
          </form>

          <div className="mt-7 flex items-start gap-2.5 text-[12px] leading-6 text-[var(--chat-ink-2)]">
            <svg viewBox="0 0 24 24" className="mt-0.5 h-4 w-4 shrink-0 text-[#ff5a1f]" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="M4 13a8 8 0 0 1 16 0" /><path d="M4 13v3a2 2 0 0 0 2 2h1v-5H6a2 2 0 0 0-2 2Zm16 0v3a2 2 0 0 1-2 2h-1v-5h1a2 2 0 0 1 2 2Z" /></svg>
            اگر به حساب خود دسترسی ندارید، با پشتیبانی ۲۴/۷ تماس بگیرید.
          </div>
          <div className="mt-3 flex items-center gap-2.5 border-t border-[var(--chat-border)] pt-4 text-[12px] text-[var(--chat-muted)]">
            <svg viewBox="0 0 24 24" className="h-4 w-4 shrink-0 text-emerald-500" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="M12 3l8 4v5c0 5-3.5 8-8 10-4.5-2-8-5-8-10V7l8-4z" /></svg>
            اطلاعات شما امن و محرمانه نگهداری می‌شود.
          </div>
        </>
      )}
    </AuthShell>
  );
}
