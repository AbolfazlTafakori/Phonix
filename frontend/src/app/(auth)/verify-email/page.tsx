"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import AuthShell from "@/components/auth/AuthShell";
import Img from "@/components/ui/Img";

const VERIFY_IMAGE = "/figma/auth-reset.png";

const outlineBtn =
  "flex h-12 w-full items-center justify-center gap-2 rounded-xl border border-[#ff5a1f]/40 text-[14px] font-bold text-[var(--chat-ink)] transition hover:bg-[#ff5a1f]/10";

const Chevron = () => (
  <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><path d="M15 6l-6 6 6 6" /></svg>
);

type State = "loading" | "ok" | "error";

export default function VerifyEmailPage() {
  const [state, setState] = useState<State>("loading");

  useEffect(() => {
    const token = new URLSearchParams(window.location.search).get("token");
    if (!token) {
      setState("error");
      return;
    }
    api.auth
      .verifyEmail(token)
      .then(() => setState("ok"))
      .catch(() => setState("error"));
  }, []);

  return (
    <AuthShell image={VERIFY_IMAGE}>
      <div className="mb-6 flex items-center gap-2">
        <Img src="/figma/logo-phoenix.png" alt="Phoenix Verify" className="h-10 w-auto" sizes="240px" />
        <span className="text-[16px] font-extrabold leading-[1.05] text-[var(--chat-ink)]">Phoenix<br />Verify</span>
      </div>

      {state === "loading" && (
        <div className="flex flex-col items-center gap-4 py-12">
          <span className="inline-block h-8 w-8 animate-spin rounded-full border-2 border-[var(--chat-border)] border-t-[#ff5a1f]" />
          <p className="text-[13px] text-[var(--chat-ink-2)]">در حال بررسی لینک...</p>
        </div>
      )}

      {state === "ok" && (
        <div className="py-6 text-center">
          <div className="mx-auto mb-4 grid h-14 w-14 place-items-center rounded-full bg-emerald-500/15 text-2xl text-emerald-500">✓</div>
          <h1 className="text-[18px] font-extrabold text-[var(--chat-ink)]">ایمیل تأیید شد</h1>
          <p className="mt-2 text-[13px] leading-7 text-[var(--chat-ink-2)]">ایمیل شما با موفقیت تأیید شد. اکنون می‌توانید خرید خود را ثبت کنید.</p>
          <Link href="/account" className="mt-6 flex h-12 w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-l from-[#ef233c] to-[#ff7a2e] text-[14px] font-extrabold text-white shadow-[0_14px_30px_-12px_rgba(239,35,60,0.6)] transition hover:brightness-[1.06]">
            ورود به حساب
            <Chevron />
          </Link>
        </div>
      )}

      {state === "error" && (
        <div className="py-6 text-center">
          <div className="mx-auto mb-4 grid h-14 w-14 place-items-center rounded-full bg-rose-500/15 text-2xl text-rose-500">!</div>
          <h1 className="text-[18px] font-extrabold text-[var(--chat-ink)]">لینک نامعتبر</h1>
          <p className="mt-2 text-[13px] leading-7 text-[var(--chat-ink-2)]">لینک تأیید نامعتبر یا منقضی شده است. از حساب کاربری خود دوباره درخواست ارسال ایمیل تأیید بزنید.</p>
          <Link href="/account" className={`mt-6 ${outlineBtn}`}>رفتن به حساب<Chevron /></Link>
        </div>
      )}

      <div className="mt-7 flex items-center gap-2.5 border-t border-[var(--chat-border)] pt-4 text-[12px] text-[var(--chat-muted)]">
        <svg viewBox="0 0 24 24" className="h-4 w-4 shrink-0 text-emerald-500" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="M12 3l8 4v5c0 5-3.5 8-8 10-4.5-2-8-5-8-10V7l8-4z" /></svg>
        اطلاعات شما امن و محرمانه نگهداری می‌شود.
      </div>
    </AuthShell>
  );
}
