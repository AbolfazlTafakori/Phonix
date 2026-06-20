"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { api } from "@/lib/api";
import AuthCard from "@/components/auth/AuthCard";

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition placeholder:text-white/35 focus:border-[#3e3af2] focus:ring-2 focus:ring-[#3e3af2]/20";

export default function ResetPasswordPage() {
  const router = useRouter();
  const [token, setToken] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [done, setDone] = useState(false);

  useEffect(() => {
    setToken(new URLSearchParams(window.location.search).get("token") ?? "");
  }, []);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (password.length < 8 || !/[a-zA-Z]/.test(password) || !/\d/.test(password)) {
      setError("گذرواژه باید حداقل ۸ کاراکتر و ترکیبی از حروف و اعداد باشد.");
      return;
    }
    if (password !== confirm) {
      setError("تکرار گذرواژه مطابقت ندارد.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      await api.auth.resetPassword(token, password);
      setDone(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "خطا در بازنشانی گذرواژه");
    } finally {
      setBusy(false);
    }
  }

  if (done) {
    return (
      <AuthCard title="بازنشانی گذرواژه">
        <div className="text-center">
          <div className="mx-auto mb-4 grid h-14 w-14 place-items-center rounded-full bg-emerald-500/15 text-3xl text-emerald-400">✓</div>
          <p className="text-sm leading-7 text-white/80">گذرواژه شما با موفقیت تغییر کرد.</p>
          <button onClick={() => router.push("/login")} className="mt-6 inline-block rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 py-3 text-sm font-bold text-white transition hover:brightness-110">
            ورود به حساب
          </button>
        </div>
      </AuthCard>
    );
  }

  return (
    <AuthCard title="تعیین گذرواژه جدید">
      {!token ? (
        <div className="text-center">
          <p className="text-sm leading-7 text-white/80">لینک نامعتبر است. از صفحه‌ی فراموشی گذرواژه دوباره اقدام کنید.</p>
          <Link href="/forgot-password" className="mt-6 inline-block rounded-xl border border-white/10 px-8 py-3 text-sm font-bold text-white/85 transition hover:bg-white/5">
            فراموشی گذرواژه
          </Link>
        </div>
      ) : (
        <form onSubmit={submit}>
          <div className="mb-5">
            <label className="mb-2 block text-sm font-medium text-white/85">گذرواژه جدید</label>
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} className={inputCls} />
            <p className="mt-1.5 text-xs text-white/45">حداقل ۸ کاراکتر و ترکیبی از حروف و اعداد.</p>
          </div>
          <div className="mb-5">
            <label className="mb-2 block text-sm font-medium text-white/85">تکرار گذرواژه جدید</label>
            <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} className={inputCls} />
          </div>
          {error && <p className="mb-4 text-sm text-rose-400">{error}</p>}
          <button type="submit" disabled={busy} className="mt-3 h-12 w-full rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-base font-bold text-white transition hover:brightness-110 disabled:opacity-60">
            {busy ? "در حال ثبت..." : "تغییر گذرواژه"}
          </button>
        </form>
      )}
    </AuthCard>
  );
}
