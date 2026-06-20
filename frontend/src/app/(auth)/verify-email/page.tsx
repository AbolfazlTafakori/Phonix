"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import AuthCard from "@/components/auth/AuthCard";

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
    <AuthCard title="تأیید ایمیل">
      {state === "loading" && <p className="text-center text-sm text-white/70">در حال بررسی لینک...</p>}

      {state === "ok" && (
        <div className="text-center">
          <div className="mx-auto mb-4 grid h-14 w-14 place-items-center rounded-full bg-emerald-500/15 text-3xl text-emerald-400">✓</div>
          <p className="text-sm leading-7 text-white/80">ایمیل شما با موفقیت تأیید شد. اکنون می‌توانید خرید خود را ثبت کنید.</p>
          <Link href="/account" className="mt-6 inline-block rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 py-3 text-sm font-bold text-white transition hover:brightness-110">
            ورود به حساب
          </Link>
        </div>
      )}

      {state === "error" && (
        <div className="text-center">
          <div className="mx-auto mb-4 grid h-14 w-14 place-items-center rounded-full bg-rose-500/15 text-3xl text-rose-400">!</div>
          <p className="text-sm leading-7 text-white/80">لینک تأیید نامعتبر یا منقضی شده است. از حساب کاربری خود دوباره درخواست ارسال ایمیل تأیید بزنید.</p>
          <Link href="/account" className="mt-6 inline-block rounded-xl border border-white/10 px-8 py-3 text-sm font-bold text-white/85 transition hover:bg-white/5">
            رفتن به حساب
          </Link>
        </div>
      )}
    </AuthCard>
  );
}
