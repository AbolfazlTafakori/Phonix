"use client";

import { useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import AuthShell from "@/components/auth/AuthShell";

const CHANGE_IMAGE = "/figma/auth-reset.png";

const gradBtn =
  "flex h-12 w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-l from-[#ef233c] to-[#ff7a2e] text-[14px] font-extrabold text-white shadow-[0_14px_30px_-12px_rgba(239,35,60,0.6)] transition hover:brightness-[1.06] disabled:cursor-not-allowed disabled:opacity-60";
const outlineBtn =
  "flex h-12 w-full items-center justify-center gap-2 rounded-xl border border-[#ff5a1f]/40 text-[14px] font-bold text-[var(--chat-ink)] transition hover:bg-[#ff5a1f]/10";

const Chevron = () => (
  <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><path d="M15 6l-6 6 6 6" /></svg>
);

const LockIcon = () => (
  <svg viewBox="0 0 24 24" className="h-[18px] w-[18px]" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><rect x="5" y="11" width="14" height="10" rx="2" /><path d="M8 11V7a4 4 0 0 1 8 0v4" /></svg>
);

export default function ChangePasswordPage() {
  const [current, setCurrent] = useState("");
  const [newPass, setNewPass] = useState("");
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (newPass !== confirm) {
      setError("گذرواژه‌ی جدید با تکرار آن مطابقت ندارد.");
      return;
    }
    if (newPass.length < 6) {
      setError("گذرواژه‌ی جدید باید حداقل ۶ کاراکتر باشد.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      await api.account.changePassword({ currentPassword: current, newPassword: newPass });
      setDone(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "خطا در تغییر گذرواژه");
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthShell image={CHANGE_IMAGE}>
      <div className="mb-6 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <img src="/figma/logo-phoenix.png" alt="Phoenix Verify" className="h-10 w-auto" />
          <span className="text-[16px] font-extrabold leading-[1.05] text-[var(--chat-ink)]">Phoenix<br />Verify</span>
        </div>
        <span className="flex items-center gap-1.5 rounded-full border border-[var(--chat-border)] px-3 py-1.5 text-[11px] font-bold text-[var(--chat-ink-2)]">
          <span className="grid h-4 w-4 place-items-center rounded-full bg-[#ff5a1f] text-[9px] text-white">🔒</span>
          امنیت حساب
        </span>
      </div>

      {done ? (
        <div className="py-6 text-center">
          <div className="mx-auto mb-4 grid h-14 w-14 place-items-center rounded-full bg-emerald-500/15 text-2xl text-emerald-500">✓</div>
          <h1 className="text-[18px] font-extrabold text-[var(--chat-ink)]">گذرواژه تغییر کرد</h1>
          <p className="mt-2 text-[13px] leading-7 text-[var(--chat-ink-2)]">گذرواژه‌ی شما با موفقیت تغییر یافت. سایر نشست‌های فعال شما نیز خارج شدند.</p>
          <Link href="/account" className={`mt-6 ${outlineBtn}`}>بازگشت به حساب<Chevron /></Link>
        </div>
      ) : (
        <>
          <h1 className="text-[24px] font-extrabold text-[var(--chat-ink)]">تغییر گذرواژه</h1>
          <p className="mt-2 text-[13px] leading-7 text-[var(--chat-ink-2)]">گذرواژه‌ی فعلی خود را وارد کنید و گذرواژه‌ی جدید را انتخاب نمایید.</p>

          <form onSubmit={submit} className="mt-6 flex flex-col gap-3">
            <div className="flex h-12 items-center gap-2.5 rounded-xl border border-[var(--chat-border)] bg-[var(--chat-surface)] px-3.5 transition focus-within:border-[#ff7a2e] focus-within:ring-2 focus-within:ring-[#ff7a2e]/15">
              <span className="shrink-0 text-[var(--chat-muted)]"><LockIcon /></span>
              <input value={current} onChange={(e) => setCurrent(e.target.value)} type="password" autoComplete="current-password" placeholder="گذرواژه‌ی فعلی" className="flex-1 bg-transparent text-left text-[13.5px] text-[var(--chat-ink)] outline-none placeholder:text-[var(--chat-muted)]" dir="ltr" />
            </div>

            <div className="flex h-12 items-center gap-2.5 rounded-xl border border-[var(--chat-border)] bg-[var(--chat-surface)] px-3.5 transition focus-within:border-[#ff7a2e] focus-within:ring-2 focus-within:ring-[#ff7a2e]/15">
              <span className="shrink-0 text-[var(--chat-muted)]"><LockIcon /></span>
              <input value={newPass} onChange={(e) => setNewPass(e.target.value)} type="password" autoComplete="new-password" placeholder="گذرواژه‌ی جدید" className="flex-1 bg-transparent text-left text-[13.5px] text-[var(--chat-ink)] outline-none placeholder:text-[var(--chat-muted)]" dir="ltr" />
            </div>

            <div className="flex h-12 items-center gap-2.5 rounded-xl border border-[var(--chat-border)] bg-[var(--chat-surface)] px-3.5 transition focus-within:border-[#ff7a2e] focus-within:ring-2 focus-within:ring-[#ff7a2e]/15">
              <span className="shrink-0 text-[var(--chat-muted)]"><LockIcon /></span>
              <input value={confirm} onChange={(e) => setConfirm(e.target.value)} type="password" autoComplete="new-password" placeholder="تکرار گذرواژه‌ی جدید" className="flex-1 bg-transparent text-left text-[13.5px] text-[var(--chat-ink)] outline-none placeholder:text-[var(--chat-muted)]" dir="ltr" />
            </div>

            {error && <p className="text-[13px] text-rose-500">{error}</p>}

            <button type="submit" disabled={busy || !current || !newPass || !confirm} className={`mt-1 ${gradBtn}`}>
              {busy ? "در حال تغییر..." : "تغییر گذرواژه"}
              {!busy && <Chevron />}
            </button>
            <Link href="/account" className={outlineBtn}>انصراف<Chevron /></Link>
          </form>

          <div className="mt-7 flex items-start gap-2.5 text-[12px] leading-6 text-[var(--chat-ink-2)]">
            <svg viewBox="0 0 24 24" className="mt-0.5 h-4 w-4 shrink-0 text-[#ff5a1f]" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="M12 3l8 4v5c0 5-3.5 8-8 10-4.5-2-8-5-8-10V7l8-4z" /></svg>
            پس از تغییر گذرواژه، تمام نشست‌های فعال شما بسته می‌شود و فقط نشست فعلی باقی می‌ماند.
          </div>
        </>
      )}
    </AuthShell>
  );
}
