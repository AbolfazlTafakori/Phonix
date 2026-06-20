"use client";

import { useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import AuthCard from "@/components/auth/AuthCard";

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition placeholder:text-white/35 focus:border-[#3e3af2] focus:ring-2 focus:ring-[#3e3af2]/20";

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      await api.auth.forgot(email.trim());
      setSent(true);
    } catch {
      setSent(true);
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthCard title="فراموشی رمز ورود">
      {sent ? (
        <div className="text-center">
          <div className="mx-auto mb-4 grid h-14 w-14 place-items-center rounded-full bg-emerald-500/15 text-2xl text-emerald-400">✓</div>
          <p className="text-sm leading-7 text-white/75">
            اگر این ایمیل در سیستم ثبت شده باشد، لینک بازیابی گذرواژه برای شما ارسال خواهد شد.
          </p>
          <Link href="/login" className="mt-6 inline-block font-bold text-[#e60053] hover:underline">بازگشت به ورود</Link>
        </div>
      ) : (
        <>
          <p className="mb-7 text-center text-sm leading-7 text-white/70">
            ایمیل خود را وارد کنید تا لینک ساخت گذرواژه‌ی جدید برایتان ارسال شود.
          </p>
          <form onSubmit={submit}>
            <div className="mb-5">
              <label className="mb-2 block text-sm font-medium text-white/85">ایمیل</label>
              <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="you@example.com" />
            </div>
            <button
              type="submit"
              disabled={busy}
              className="mt-2 h-12 w-full rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-base font-bold text-white transition hover:brightness-110 disabled:opacity-60"
            >
              {busy ? "در حال ارسال..." : "بازگردانی گذرواژه"}
            </button>
            <p className="mt-6 text-center text-sm text-white/60">
              <Link href="/login" className="font-bold text-[#e60053] hover:underline">بازگشت به ورود</Link>
            </p>
          </form>
        </>
      )}
    </AuthCard>
  );
}
