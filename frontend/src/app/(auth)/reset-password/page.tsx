"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { api } from "@/lib/api";
import AuthShell from "@/components/auth/AuthShell";

const RESET_IMAGE = "/figma/auth-reset.png";

const gradBtn =
  "flex h-12 w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-l from-[#ef233c] to-[#ff7a2e] text-[14px] font-extrabold text-white shadow-[0_14px_30px_-12px_rgba(239,35,60,0.6)] transition hover:brightness-[1.06] disabled:cursor-not-allowed disabled:opacity-60";
const outlineBtn =
  "flex h-12 w-full items-center justify-center gap-2 rounded-xl border border-[#ff5a1f]/40 text-[14px] font-bold text-[var(--chat-ink)] transition hover:bg-[#ff5a1f]/10";

function PwField({ value, onChange, placeholder, show, onToggle }: { value: string; onChange: (v: string) => void; placeholder: string; show: boolean; onToggle: () => void }) {
  return (
    <div className="flex h-12 items-center gap-2.5 rounded-xl border border-[var(--chat-border)] bg-[var(--chat-surface)] px-3.5 transition focus-within:border-[#ff7a2e] focus-within:ring-2 focus-within:ring-[#ff7a2e]/15">
      <span className="shrink-0 text-[#9aa0ab]"><svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><rect x="4" y="11" width="16" height="10" rx="2" /><path d="M8 11V8a4 4 0 0 1 8 0v3" /></svg></span>
      <input type={show ? "text" : "password"} value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} autoComplete="off" className="flex-1 bg-transparent text-[13.5px] text-[var(--chat-ink)] outline-none placeholder:text-[var(--chat-muted)]" />
      <button type="button" onClick={onToggle} tabIndex={-1} aria-label="نمایش رمز" className="shrink-0 text-[#b6bac2] transition hover:text-[#8a8f99]">
        <svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">{show ? <><path d="M9.9 5A9.8 9.8 0 0 1 12 5c6 0 10 7 10 7a15 15 0 0 1-3 3.5M6.1 6.1A15 15 0 0 0 2 12s4 7 10 7a9.8 9.8 0 0 0 4-.8" /><path d="M3 3l18 18" /></> : <><path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></>}</svg>
      </button>
    </div>
  );
}

export default function ResetPasswordPage() {
  const router = useRouter();
  const [token, setToken] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [show, setShow] = useState(false);
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

  return (
    <AuthShell image={RESET_IMAGE}>
      <div className="mb-6 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <img src="/figma/logo-phoenix.png" alt="Phoenix Verify" className="h-10 w-auto" />
          <span className="text-[16px] font-extrabold leading-[1.05] text-[var(--chat-ink)]">Phoenix<br />Verify</span>
        </div>
        <span className="flex items-center gap-1.5 rounded-full border border-[var(--chat-border)] px-3 py-1.5 text-[11px] font-bold text-[var(--chat-ink-2)]">
          <span className="grid h-4 w-4 place-items-center rounded-full bg-[#ff5a1f] text-[9px] text-white">۲</span>
          رمز جدید
        </span>
      </div>

      {done ? (
        <div className="py-6 text-center">
          <div className="mx-auto mb-4 grid h-14 w-14 place-items-center rounded-full bg-emerald-500/15 text-3xl text-emerald-500">✓</div>
          <h1 className="text-[18px] font-extrabold text-[var(--chat-ink)]">گذرواژه تغییر کرد</h1>
          <p className="mt-2 text-[13px] leading-7 text-[var(--chat-ink-2)]">گذرواژه شما با موفقیت تغییر کرد. حالا می‌توانید وارد شوید.</p>
          <button onClick={() => router.push("/login")} className={`mt-6 ${gradBtn}`}>ورود به حساب</button>
        </div>
      ) : !token ? (
        <div className="py-6 text-center">
          <h1 className="text-[18px] font-extrabold text-[var(--chat-ink)]">لینک نامعتبر است</h1>
          <p className="mt-2 text-[13px] leading-7 text-[var(--chat-ink-2)]">این لینک نامعتبر یا منقضی شده است. از صفحه‌ی فراموشی گذرواژه دوباره اقدام کنید.</p>
          <Link href="/forgot-password" className={`mt-6 ${outlineBtn}`}>فراموشی گذرواژه</Link>
        </div>
      ) : (
        <>
          <h1 className="text-[24px] font-extrabold text-[var(--chat-ink)]">تعیین گذرواژه جدید</h1>
          <p className="mt-2 text-[13px] leading-7 text-[var(--chat-ink-2)]">یک گذرواژه‌ی جدید و امن برای حساب خود انتخاب کنید.</p>

          <form onSubmit={submit} className="mt-6 flex flex-col gap-3">
            <PwField value={password} onChange={setPassword} placeholder="گذرواژه جدید" show={show} onToggle={() => setShow((s) => !s)} />
            <PwField value={confirm} onChange={setConfirm} placeholder="تکرار گذرواژه جدید" show={show} onToggle={() => setShow((s) => !s)} />
            <p className="text-[11px] text-[var(--chat-muted)]">حداقل ۸ کاراکتر و ترکیبی از حروف و اعداد.</p>
            {error && <p className="text-[12.5px] text-[#ef233c]">{error}</p>}
            <button type="submit" disabled={busy} className={`mt-1 ${gradBtn}`}>{busy ? "در حال ثبت..." : "تغییر گذرواژه"}</button>
            <Link href="/login" className={outlineBtn}>بازگشت به صفحه ورود</Link>
          </form>
        </>
      )}
    </AuthShell>
  );
}
