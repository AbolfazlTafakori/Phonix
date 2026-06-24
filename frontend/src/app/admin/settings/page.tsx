"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { api } from "@/lib/api";
import type { User } from "@/lib/types";
import { Card, PageHeader, Spinner, Field, inputCls } from "@/components/admin/ui";

// Settings is split across sibling routes; this sub-nav keeps them reachable from one place
// (the general/advanced pages were previously orphaned with no link into them).
const settingsTabs: { href: string; label: string }[] = [
  { href: "/admin/settings", label: "حساب مدیر" },
  { href: "/admin/settings/advanced", label: "تنظیمات پیشرفته" },
  { href: "/admin/settings/email", label: "ایمیل و پیامک" },
  { href: "/admin/settings/2fa", label: "ورود دو‌مرحله‌ای" },
];

function SettingsTabs() {
  const pathname = usePathname();
  return (
    <div className="mb-6 flex flex-wrap gap-2">
      {settingsTabs.map((t) => {
        const active = pathname === t.href;
        return (
          <Link
            key={t.href}
            href={t.href}
            className={`rounded-xl border px-5 py-2 text-sm font-bold transition ${
              active
                ? "border-transparent bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white"
                : "border-white/10 text-white/60 hover:text-white"
            }`}
          >
            {t.label}
          </Link>
        );
      })}
    </div>
  );
}

export default function AdminSettingsPage() {
  const [me, setMe] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [savingProfile, setSavingProfile] = useState(false);
  const [profileMsg, setProfileMsg] = useState("");

  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [savingPw, setSavingPw] = useState(false);
  const [pwMsg, setPwMsg] = useState("");
  const [pwError, setPwError] = useState("");

  useEffect(() => {
    api.account
      .me()
      .then((u) => {
        setMe(u);
        setName(u.name);
        setEmail(u.email);
        setPhone(u.phone);
      })
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  async function saveProfile() {
    setSavingProfile(true);
    setProfileMsg("");
    try {
      const updated = await api.account.updateMe({ name, email, phone });
      setMe(updated);
      setProfileMsg("اطلاعات ذخیره شد.");
    } finally {
      setSavingProfile(false);
    }
  }

  async function changePassword() {
    setPwMsg("");
    setPwError("");
    if (next !== confirm) {
      setPwError("تکرار گذرواژه مطابقت ندارد.");
      return;
    }
    setSavingPw(true);
    try {
      await api.account.changePassword({ currentPassword: current, newPassword: next });
      setCurrent("");
      setNext("");
      setConfirm("");
      setPwMsg("گذرواژه با موفقیت تغییر کرد.");
    } catch (e) {
      setPwError(e instanceof Error ? e.message : "خطا در تغییر گذرواژه");
    } finally {
      setSavingPw(false);
    }
  }

  if (loading) {
    return (
      <div>
        <PageHeader title="تنظیمات" desc="حساب مدیر" />
        <SettingsTabs />
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      </div>
    );
  }

  return (
    <div>
      <PageHeader title="تنظیمات" desc="مدیریت حساب کاربری مدیر" />
      <SettingsTabs />

      <div className="grid gap-6 lg:grid-cols-2">
        <Card className="p-6">
          <h3 className="mb-5 text-lg font-bold text-white">اطلاعات حساب</h3>
          <div className="grid gap-5">
            <Field label="نام و نام خانوادگی">
              <input value={name} onChange={(e) => setName(e.target.value)} className={inputCls} />
            </Field>
            <Field label="نام کاربری">
              <input value={me?.username ?? ""} dir="ltr" disabled className={`${inputCls} text-left opacity-60`} />
            </Field>
            <Field label="ایمیل">
              <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
            </Field>
            <Field label="شماره تماس">
              <input type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
            </Field>
          </div>
          <div className="mt-6 flex items-center gap-3">
            <button onClick={saveProfile} disabled={savingProfile} className="flex h-11 items-center rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60">
              {savingProfile ? <Spinner /> : "ذخیره تغییرات"}
            </button>
            {profileMsg && <span className="text-sm font-medium text-emerald-400">{profileMsg}</span>}
          </div>
        </Card>

        <Card className="p-6">
          <h3 className="mb-5 text-lg font-bold text-white">تغییر گذرواژه</h3>
          <div className="grid gap-5">
            <Field label="گذرواژه فعلی">
              <input type="password" value={current} onChange={(e) => setCurrent(e.target.value)} className={inputCls} />
            </Field>
            <Field label="گذرواژه جدید">
              <input type="password" value={next} onChange={(e) => setNext(e.target.value)} className={inputCls} />
            </Field>
            <Field label="تکرار گذرواژه جدید">
              <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} className={inputCls} />
            </Field>
            <p className="text-xs text-white/45">حداقل ۸ کاراکتر و ترکیبی از حروف و اعداد.</p>
          </div>
          <div className="mt-6 flex items-center gap-3">
            <button onClick={changePassword} disabled={savingPw || !current || !next} className="flex h-11 items-center rounded-xl border border-white/10 px-8 text-sm font-bold text-white/85 transition hover:bg-white/5 disabled:opacity-60">
              {savingPw ? <Spinner /> : "تغییر گذرواژه"}
            </button>
            {pwMsg && <span className="text-sm font-medium text-emerald-400">{pwMsg}</span>}
            {pwError && <span className="text-sm text-rose-400">{pwError}</span>}
          </div>
        </Card>
      </div>
    </div>
  );
}
