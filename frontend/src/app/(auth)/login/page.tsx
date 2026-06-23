"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { api } from "@/lib/api";
import { setCurrentUser } from "@/lib/auth";
import AuthCard from "@/components/auth/AuthCard";
import { useCaptcha, CaptchaField } from "@/components/auth/Captcha";

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition placeholder:text-white/35 focus:border-[#3e3af2] focus:ring-2 focus:ring-[#3e3af2]/20";

export default function LoginPage() {
  const router = useRouter();
  const [identifier, setIdentifier] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [challengeToken, setChallengeToken] = useState<string | null>(null);
  const [otp, setOtp] = useState("");
  const captcha = useCaptcha();

  function complete(user: NonNullable<Awaited<ReturnType<typeof api.auth.login>>["user"]>) {
    setCurrentUser({ id: user.id, name: user.name, username: user.username, email: user.email, avatar: user.avatar });
    router.push("/account");
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const res = await api.auth.login({ identifier, password, captchaId: captcha.id, captchaText: captcha.text });
      if (res.requiresTwoFactor && res.challengeToken) {
        setChallengeToken(res.challengeToken);
      } else if (res.user) {
        complete(res.user);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "خطا در ورود");
      captcha.refresh(); // a used/failed challenge is single-use; issue a fresh image
    } finally {
      setBusy(false);
    }
  }

  async function verifyOtp(e: React.FormEvent) {
    e.preventDefault();
    if (!challengeToken) return;
    setBusy(true);
    setError("");
    try {
      const res = await api.auth.verifyTwoFactor(challengeToken, otp.trim());
      if (res.user) complete(res.user);
    } catch (err) {
      setError(err instanceof Error ? err.message : "کد تأیید نادرست است");
    } finally {
      setBusy(false);
    }
  }

  if (challengeToken) {
    return (
      <AuthCard title="تأیید دو‌مرحله‌ای">
        <form onSubmit={verifyOtp}>
          <p className="mb-5 text-sm leading-7 text-white/70">
            کد ۶ رقمی نمایش‌داده‌شده در برنامه‌ی احرازکننده (Google Authenticator) را وارد کنید.
          </p>
          <input
            value={otp}
            onChange={(e) => setOtp(e.target.value.replace(/\D/g, "").slice(0, 6))}
            inputMode="numeric"
            autoFocus
            dir="ltr"
            placeholder="------"
            className={`${inputCls} text-center text-lg tracking-[0.5em]`}
          />

          {error && <p className="mb-4 mt-4 text-sm text-rose-400">{error}</p>}

          <button
            type="submit"
            disabled={busy || otp.length !== 6}
            className="mt-6 h-12 w-full rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-base font-bold text-white shadow-[0_14px_36px_-14px_rgba(58,100,242,0.8)] transition hover:brightness-110 disabled:opacity-60"
          >
            {busy ? "در حال بررسی..." : "تأیید و ورود"}
          </button>
          <button
            type="button"
            onClick={() => { setChallengeToken(null); setOtp(""); setError(""); }}
            className="mt-3 h-11 w-full rounded-xl border border-white/10 text-sm font-medium text-white/60 transition hover:text-white"
          >
            بازگشت
          </button>
        </form>
      </AuthCard>
    );
  }

  return (
    <AuthCard title="ورود به حساب کاربری">
      <form onSubmit={submit}>
        <div className="mb-5">
          <label className="mb-2 block text-sm font-medium text-white/85">نام کاربری، شماره موبایل یا ایمیل</label>
          <input value={identifier} onChange={(e) => setIdentifier(e.target.value)} className={inputCls} />
        </div>

        <div className="mb-5">
          <div className="mb-2 flex items-center justify-between gap-3">
            <label className="text-sm font-medium text-white/85">گذرواژه</label>
            <Link href="/forgot-password" className="text-xs text-white/55 transition hover:text-white">
              کلمه عبور خود را فراموش کرده‌اید؟
            </Link>
          </div>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} className={inputCls} />
        </div>

        <CaptchaField captcha={captcha} />

        {error && <p className="mb-4 text-sm text-rose-400">{error}</p>}

        <p className="mb-6 mt-1 text-sm text-white/70">
          اگر حساب کاربری ندارید روی{" "}
          <Link href="/signup" className="font-bold text-[#e60053] hover:underline">ثبت نام</Link>{" "}
          کلیک کنید.
        </p>

        <button
          type="submit"
          disabled={busy}
          className="mt-3 h-12 w-full rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-base font-bold text-white shadow-[0_14px_36px_-14px_rgba(58,100,242,0.8)] transition hover:brightness-110 disabled:opacity-60"
        >
          {busy ? "در حال ورود..." : "ورود"}
        </button>
      </form>
    </AuthCard>
  );
}
