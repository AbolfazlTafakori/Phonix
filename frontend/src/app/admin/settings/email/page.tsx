"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { EmailSettings } from "@/lib/types";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";

export default function EmailSettingsPage() {
  const [data, setData] = useState<EmailSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  const [testTo, setTestTo] = useState("");
  const [testing, setTesting] = useState(false);
  const [testMsg, setTestMsg] = useState("");
  const [testOk, setTestOk] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        setData(await api.emailSettings.get());
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const set = <K extends keyof EmailSettings>(key: K, value: EmailSettings[K]) =>
    setData((d) => (d ? { ...d, [key]: value } : d));

  async function save() {
    if (!data) return;
    setSaving(true);
    setSaved(false);
    try {
      setData(await api.emailSettings.update(data));
      setSaved(true);
    } finally {
      setSaving(false);
    }
  }

  async function sendTest() {
    if (!testTo.trim()) return;
    setTesting(true);
    setTestMsg("");
    try {
      await api.emailSettings.test(testTo.trim());
      setTestOk(true);
      setTestMsg("ایمیل آزمایشی ارسال شد. صندوق ورودی را بررسی کنید.");
    } catch (e) {
      setTestOk(false);
      setTestMsg(e instanceof Error ? e.message : "ارسال ناموفق بود.");
    } finally {
      setTesting(false);
    }
  }

  return (
    <div>
      <PageHeader
        title="تنظیمات ایمیل (SMTP)"
        desc="اطلاعات سرویس ایمیل مجموعه را وارد کنید تا پیام‌ها از طرف ایمیل شما برای کاربران ارسال شود."
        action={
          data && (
            <div className="flex items-center gap-3">
              {saved && <span className="text-sm font-medium text-emerald-400">✓ ذخیره شد</span>}
              <button onClick={save} disabled={saving} className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110">
                {saving ? <Spinner /> : "ذخیره تغییرات"}
              </button>
            </div>
          )
        }
      />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error || !data ? (
        <Card className="p-8 text-center text-rose-400">{error || "اطلاعات یافت نشد"}</Card>
      ) : (
        <div className="grid gap-6 lg:grid-cols-2">
          <Card className="p-6">
            <label className="mb-5 flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
              <span className="text-sm font-bold text-white/85">فعال‌سازی ارسال ایمیل</span>
              <Toggle checked={data.enabled} onChange={(v) => set("enabled", v)} />
            </label>
            <div className="grid gap-4">
              <Field label="آدرس فرستنده (ایمیل مجموعه)">
                <input value={data.fromEmail} onChange={(e) => set("fromEmail", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="info@yourdomain.com" />
              </Field>
              <Field label="نام فرستنده">
                <input value={data.fromName} onChange={(e) => set("fromName", e.target.value)} className={inputCls} placeholder="Phoenix Verify" />
              </Field>
              <div className="grid grid-cols-[1fr_120px] gap-4">
                <Field label="سرور SMTP (Host)">
                  <input value={data.host} onChange={(e) => set("host", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="smtp.gmail.com" />
                </Field>
                <Field label="پورت">
                  <input type="number" dir="ltr" value={data.port} onChange={(e) => set("port", Number(e.target.value))} className={`${inputCls} text-left`} placeholder="587" />
                </Field>
              </div>
              <Field label="نام کاربری SMTP">
                <input value={data.username} onChange={(e) => set("username", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="info@yourdomain.com" />
              </Field>
              <Field label="گذرواژه SMTP">
                <input type="password" value={data.password} onChange={(e) => set("password", e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
              </Field>
              <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
                <span className="text-sm text-white/80">استفاده از SSL/TLS</span>
                <Toggle checked={data.useSsl} onChange={(v) => set("useSsl", v)} />
              </label>
            </div>
          </Card>

          <div className="space-y-6">
            <Card className="p-6">
              <h3 className="mb-2 text-lg font-bold text-white">ارسال ایمیل آزمایشی</h3>
              <p className="mb-4 text-xs text-white/45">ابتدا تنظیمات بالا را ذخیره کنید، سپس یک ایمیل آزمایشی به خودتان بفرستید تا از درستی پیکربندی مطمئن شوید.</p>
              <div className="flex gap-2">
                <input value={testTo} onChange={(e) => setTestTo(e.target.value)} dir="ltr" placeholder="you@example.com" className={`${inputCls} flex-1 text-left`} />
                <button onClick={sendTest} disabled={testing || !testTo.trim()} className="h-11 shrink-0 rounded-xl border border-white/15 px-5 text-sm font-bold text-white/85 transition hover:bg-white/5 disabled:opacity-50">
                  {testing ? <Spinner /> : "ارسال"}
                </button>
              </div>
              {testMsg && <p className={`mt-3 text-sm ${testOk ? "text-emerald-400" : "text-rose-400"}`}>{testMsg}</p>}
            </Card>

            <Card className="p-6">
              <h3 className="mb-3 text-lg font-bold text-white">راهنما</h3>
              <ul className="space-y-2 text-sm leading-7 text-white/65">
                <li>• اطلاعات SMTP را از پنل سرویس ایمیل خود (هاست، جیمیل، Zoho و...) بگیرید.</li>
                <li>• پورت معمول: ۵۸۷ (TLS) یا ۴۶۵ (SSL).</li>
                <li>• تا وقتی این بخش فعال و درست تنظیم نشده، ایمیل‌ها فقط در لاگ سرور ثبت می‌شوند و ارسال نمی‌گردند.</li>
              </ul>
            </Card>
          </div>
        </div>
      )}
    </div>
  );
}
