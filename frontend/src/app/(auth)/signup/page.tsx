"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { api } from "@/lib/api";
import { setCurrentUser } from "@/lib/auth";
import AuthCard from "@/components/auth/AuthCard";

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition placeholder:text-white/35 focus:border-[#3e3af2] focus:ring-2 focus:ring-[#3e3af2]/20";

const hint = "mt-1.5 text-xs text-white/45";

function randomUsername() {
  return "Phonix" + Math.random().toString(36).slice(2, 7);
}

export default function SignupPage() {
  const router = useRouter();
  const [name, setName] = useState("");
  const [username, setUsername] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [agree, setAgree] = useState(false);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);

  function validate(): string {
    if (!username.trim()) return "نام کاربری را وارد کنید.";
    if (!email.trim() || !email.includes("@")) return "یک ایمیل معتبر وارد کنید.";
    if (!password) return "گذرواژه را وارد کنید.";
    if (!agree) return "لطفاً قوانین را تأیید کنید.";
    return "";
  }

  function openConfirm(e: React.FormEvent) {
    e.preventDefault();
    const v = validate();
    if (v) {
      setError(v);
      return;
    }
    setError("");
    setConfirmOpen(true);
  }

  async function confirmRegister() {
    setBusy(true);
    setError("");
    try {
      const u = await api.auth.register({
        name: name.trim(),
        username: username.trim(),
        email: email.trim(),
        phone: "",
        password,
      });
      setCurrentUser({ id: u.id, name: u.name, username: u.username, email: u.email });
      router.push("/account");
    } catch (err) {
      setConfirmOpen(false);
      setError(err instanceof Error ? err.message : "خطا در ثبت‌نام");
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthCard title="عضویت در سایت">
      <form onSubmit={openConfirm}>
        <div className="mb-5">
          <label className="mb-2 block text-sm font-medium text-white/85">نام <span className="text-white/45">(اختیاری)</span></label>
          <input value={name} onChange={(e) => setName(e.target.value)} className={inputCls} />
        </div>

        <div className="mb-5">
          <div className="mb-2 flex items-center justify-between gap-3">
            <label className="text-sm font-medium text-white/85">نام کاربری</label>
            <button
              type="button"
              onClick={() => setUsername(randomUsername())}
              className="text-xs font-bold text-[#e60053] transition hover:underline"
            >
              ساخت خودکار
            </button>
          </div>
          <input value={username} onChange={(e) => setUsername(e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="Phonix..." />
          <p className={hint}>این نام در نظرات شما نمایش داده می‌شود. می‌توانید روی «ساخت خودکار» بزنید.</p>
        </div>

        <div className="mb-5">
          <label className="mb-2 block text-sm font-medium text-white/85">ایمیل</label>
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="you@example.com" />
          <p className={hint}>ایمیل را دقیق وارد کنید؛ اطلاعات حساب و اکانت‌ها به این آدرس ارسال می‌شود.</p>
        </div>

        <div className="mb-5">
          <label className="mb-2 block text-sm font-medium text-white/85">گذرواژه</label>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} className={inputCls} />
          <p className={hint}>گذرواژه‌ای امن انتخاب کنید و آن را به‌خاطر بسپارید.</p>
        </div>

        <label className="mb-6 mt-1 flex items-start gap-3 text-sm leading-7 text-white/70">
          <input
            type="checkbox"
            checked={agree}
            onChange={(e) => setAgree(e.target.checked)}
            className="mt-1 h-4 w-4 shrink-0 rounded border-white/20 bg-[#0d0d15] accent-[#e60053]"
          />
          <span>
            تایید کردن این فرم به منزله‌ی <span className="font-bold text-[#e60053]">تایید</span> تمامی سیاست حفظ حریم خصوصی می‌باشد.
          </span>
        </label>

        {error && <p className="mb-4 text-sm text-rose-400">{error}</p>}

        <button
          type="submit"
          disabled={!agree}
          className="mt-3 h-12 w-full rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-base font-bold text-white shadow-[0_14px_36px_-14px_rgba(58,100,242,0.8)] transition hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-40 disabled:shadow-none"
        >
          عضویت
        </button>

        <p className="mt-6 text-center text-sm text-white/60">
          قبلاً ثبت‌نام کرده‌اید؟{" "}
          <Link href="/login" className="font-bold text-[#e60053] hover:underline">ورود</Link>
        </p>
      </form>

      {confirmOpen && (
        <div className="fixed inset-0 z-50 grid place-items-center p-4">
          <div onClick={() => !busy && setConfirmOpen(false)} className="absolute inset-0 bg-black/70 backdrop-blur-sm" />
          <div className="relative w-full max-w-sm rounded-2xl border border-white/10 bg-[#16161f] p-6 text-center shadow-2xl">
            <div className="mx-auto mb-4 grid h-12 w-12 place-items-center rounded-full bg-amber-500/15 text-2xl text-amber-400">!</div>
            <h3 className="text-lg font-bold text-white">قبل از تأیید مطمئن شوید</h3>
            <p className="mt-2 text-sm leading-7 text-white/70">
              لطفاً از درست بودن <span className="font-bold text-white">ایمیل</span> و <span className="font-bold text-white">گذرواژه</span> خود مطمئن شوید. اطلاعات حساب به ایمیل شما ارسال خواهد شد.
            </p>
            <div dir="ltr" className="mt-3 rounded-xl bg-white/[0.04] px-4 py-2 text-sm text-white/80">{email}</div>

            <div className="mt-6 flex gap-3">
              <button
                onClick={confirmRegister}
                disabled={busy}
                className="h-11 flex-1 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
              >
                {busy ? "در حال ثبت..." : "تأیید و ثبت‌نام"}
              </button>
              <button
                onClick={() => setConfirmOpen(false)}
                disabled={busy}
                className="h-11 flex-1 rounded-xl border border-white/10 text-sm font-bold text-white/80 transition hover:bg-white/5"
              >
                ویرایش
              </button>
            </div>
          </div>
        </div>
      )}
    </AuthCard>
  );
}
