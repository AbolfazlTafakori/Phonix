"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import type { MailboxSettings } from "@/lib/types";
import { Modal, Spinner, Toggle, inputCls } from "@/components/admin/ui";

export default function MailboxSettingsModal({
  initial,
  onClose,
  onSaved,
}: {
  initial: MailboxSettings;
  onClose: () => void;
  onSaved: (next: MailboxSettings) => void;
}) {
  const [form, setForm] = useState(initial);
  // Kept out of `form` because it is write-only: the server never sends it back, and an empty box means
  // "leave the stored password alone" rather than "clear it".
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [testing, setTesting] = useState(false);
  const [error, setError] = useState("");
  const [ok, setOk] = useState("");

  function set<K extends keyof MailboxSettings>(key: K, value: MailboxSettings[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
  }

  async function save(): Promise<MailboxSettings | null> {
    setError("");
    setOk("");
    if (form.enabled && (!form.imapHost.trim() || !form.username.trim())) {
      setError("برای فعال‌سازی، حداقل سرور IMAP و نام کاربری لازم است.");
      return null;
    }
    if (form.enabled && !initial.hasPassword && !password) {
      setError("گذرواژه صندوق را وارد کنید.");
      return null;
    }

    setBusy(true);
    try {
      const { hasPassword: _ignored, ...rest } = form;
      const next = await api.mailbox.settings.update({ ...rest, password: password || undefined });
      setPassword("");
      onSaved(next);
      setOk("تنظیمات ذخیره شد.");
      return next;
    } catch (e) {
      setError(e instanceof Error ? e.message : "ذخیره تنظیمات ناموفق بود.");
      return null;
    } finally {
      setBusy(false);
    }
  }

  // Testing saves first on purpose: the server tests the STORED configuration, so testing an unsaved form
  // would report on the old settings and be actively misleading.
  async function saveAndTest() {
    const saved = await save();
    if (!saved) return;
    setTesting(true);
    setError("");
    setOk("");
    try {
      await api.mailbox.settings.test();
      setOk("اتصال به صندوق موفق بود.");
    } catch (e) {
      setError(e instanceof Error ? e.message : "اتصال ناموفق بود.");
    } finally {
      setTesting(false);
    }
  }

  return (
    <Modal open onClose={busy || testing ? () => undefined : onClose} title="تنظیمات صندوق ایمیل" size="2xl">
      <div className="space-y-5">
        <div className="flex items-center justify-between rounded-xl border border-white/8 bg-white/[0.02] px-4 py-3">
          <div>
            <p className="text-sm font-bold text-white">فعال بودن صندوق</p>
            <p className="mt-0.5 text-xs text-white/45">تا فعال نشود، ایمیل‌های دریافتی در پنل خوانده نمی‌شوند.</p>
          </div>
          <Toggle checked={form.enabled} onChange={(v) => set("enabled", v)} />
        </div>

        <Section title="دریافت (IMAP)">
          <label className="block sm:col-span-2">
            <Label>سرور IMAP</Label>
            <input value={form.imapHost} onChange={(e) => set("imapHost", e.target.value)} dir="ltr" placeholder="mail.example.com" className={`${inputCls} text-left`} />
          </label>
          <label className="block">
            <Label>پورت</Label>
            <input type="number" value={form.imapPort} onChange={(e) => set("imapPort", Number(e.target.value))} dir="ltr" className={`${inputCls} text-left`} />
          </label>
          <div className="flex items-center justify-between rounded-xl border border-white/8 px-4">
            <span className="text-sm text-white/70">SSL/TLS</span>
            <Toggle checked={form.imapUseSsl} onChange={(v) => set("imapUseSsl", v)} />
          </div>
        </Section>

        <Section title="ارسال پاسخ (SMTP)">
          <label className="block sm:col-span-2">
            <Label>سرور SMTP</Label>
            <input value={form.smtpHost} onChange={(e) => set("smtpHost", e.target.value)} dir="ltr" placeholder="mail.example.com" className={`${inputCls} text-left`} />
          </label>
          <label className="block">
            <Label>پورت</Label>
            <input type="number" value={form.smtpPort} onChange={(e) => set("smtpPort", Number(e.target.value))} dir="ltr" className={`${inputCls} text-left`} />
          </label>
          <div className="flex items-center justify-between rounded-xl border border-white/8 px-4">
            <span className="text-sm text-white/70">STARTTLS</span>
            <Toggle checked={form.smtpUseSsl} onChange={(v) => set("smtpUseSsl", v)} />
          </div>
        </Section>

        <Section title="حساب صندوق">
          <label className="block">
            <Label>نام کاربری</Label>
            <input value={form.username} onChange={(e) => set("username", e.target.value)} dir="ltr" placeholder="support" className={`${inputCls} text-left`} />
          </label>
          <label className="block">
            <Label>گذرواژه</Label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              dir="ltr"
              autoComplete="new-password"
              placeholder={initial.hasPassword ? "برای تغییر، گذرواژه جدید را وارد کنید" : "گذرواژه صندوق"}
              className={`${inputCls} text-left`}
            />
          </label>
          <label className="block">
            <Label>آدرس ایمیل (فرستنده پاسخ‌ها)</Label>
            <input value={form.address} onChange={(e) => set("address", e.target.value)} dir="ltr" placeholder="support@example.com" className={`${inputCls} text-left`} />
          </label>
          <label className="block">
            <Label>نام نمایشی</Label>
            <input value={form.displayName} onChange={(e) => set("displayName", e.target.value)} className={inputCls} />
          </label>
        </Section>

        <p className="rounded-xl border border-white/8 bg-white/[0.02] px-4 py-3 text-xs leading-6 text-white/45">
          این حساب جدا از تنظیمات ایمیل خروجی فروشگاه است و فقط برای صندوق دریافتی استفاده می‌شود؛ تغییر آن
          روی ارسال ایمیل‌های سفارش و بازیابی گذرواژه اثری ندارد. گذرواژه به‌صورت رمزنگاری‌شده ذخیره می‌شود و
          هیچ‌گاه به پنل بازگردانده نمی‌شود.
        </p>

        {error && <p className="text-sm leading-7 text-rose-400">{error}</p>}
        {ok && <p className="text-sm leading-7 text-emerald-400">{ok}</p>}

        <div className="flex flex-wrap items-center gap-2 border-t border-white/8 pt-4">
          <button
            onClick={save}
            disabled={busy || testing}
            className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-7 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
          >
            {busy && <Spinner />}
            ذخیره
          </button>
          <button
            onClick={saveAndTest}
            disabled={busy || testing}
            className="flex h-11 items-center gap-2 rounded-xl border border-white/10 px-5 text-sm font-bold text-white/65 transition hover:bg-white/5 hover:text-white disabled:opacity-60"
          >
            {testing && <Spinner />}
            ذخیره و تست اتصال
          </button>
          <button
            onClick={onClose}
            disabled={busy || testing}
            className="mr-auto text-sm text-white/45 transition hover:text-white disabled:opacity-60"
          >
            بستن
          </button>
        </div>
      </div>
    </Modal>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="mb-2.5 text-sm font-bold text-white/80">{title}</p>
      <div className="grid gap-3 sm:grid-cols-2">{children}</div>
    </div>
  );
}

function Label({ children }: { children: React.ReactNode }) {
  return <span className="mb-1.5 block text-xs font-medium text-white/55">{children}</span>;
}
