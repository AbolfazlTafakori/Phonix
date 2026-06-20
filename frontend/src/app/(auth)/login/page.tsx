"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { api } from "@/lib/api";
import { setCurrentUser } from "@/lib/auth";
import AuthCard from "@/components/auth/AuthCard";

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition placeholder:text-white/35 focus:border-[#3e3af2] focus:ring-2 focus:ring-[#3e3af2]/20";

export default function LoginPage() {
  const router = useRouter();
  const [identifier, setIdentifier] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const { user } = await api.auth.login({ identifier, password });
      setCurrentUser({ id: user.id, name: user.name, username: user.username, email: user.email });
      router.push("/account");
    } catch (err) {
      setError(err instanceof Error ? err.message : "خطا در ورود");
    } finally {
      setBusy(false);
    }
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
