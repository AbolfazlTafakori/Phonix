"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { TelegramSettings } from "@/lib/types";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";

type Note = { ok: boolean; text: string } | null;

// Dedicated section for the deposit-receipt approval bot. It uses its OWN Telegram bot token + chat, fully
// separate from the backup/alerts bot on the backup page, so the two never share a chat or interfere.
export default function ReceiptBotPage() {
  const [tg, setTg] = useState<TelegramSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [note, setNote] = useState<Note>(null);

  useEffect(() => {
    (async () => {
      try {
        setTg(await api.backup.telegram.get());
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const setField = <K extends keyof TelegramSettings>(key: K, value: TelegramSettings[K]) =>
    setTg((d) => (d ? { ...d, [key]: value } : d));

  async function save() {
    if (!tg) return;
    setSaving(true);
    setNote(null);
    try {
      setTg(await api.backup.telegram.update(tg));
      setNote({ ok: true, text: "تنظیمات ذخیره شد." });
    } catch (e) {
      setNote({ ok: false, text: e instanceof Error ? e.message : "ذخیره ناموفق بود." });
    } finally {
      setSaving(false);
    }
  }

  // Real sends are fire-and-forget and only log, so this is the only way to see WHY the bot isn't posting.
  async function test() {
    setTesting(true);
    setNote(null);
    try {
      await api.backup.testBot("receipt");
      setNote({ ok: true, text: "پیام تست ارسال شد — چت را ببینید." });
    } catch (e) {
      setNote({ ok: false, text: e instanceof Error ? e.message : "ارسال تست ناموفق بود." });
    } finally {
      setTesting(false);
    }
  }

  return (
    <div>
      <PageHeader
        title="تأیید رسید خودکار و ربات تلگرام"
        desc="رسید هر واریز جدید با دکمه‌های «تأیید» و «رد» به تلگرام ارسال می‌شود؛ تصمیم شما همان‌جا در سایت ثبت می‌شود."
      />

      {loading ? (
        <div className="flex justify-center py-16"><Spinner /></div>
      ) : error ? (
        <p className="text-sm text-rose-400">{error}</p>
      ) : tg ? (
        <Card className="max-w-2xl p-6">
          <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
            <span className="text-sm font-bold text-white/85">
              فعال‌سازی ربات تأیید رسید
              <span className="mt-0.5 block text-xs font-normal text-white/45">با یک بات و چت مستقل از ربات بکاپ کار می‌کند.</span>
            </span>
            <Toggle checked={tg.receiptBotEnabled} onChange={(v) => setField("receiptBotEnabled", v)} />
          </label>

          {tg.receiptBotEnabled && (
            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              <Field label="توکن بات رسید (از BotFather)">
                <input value={tg.receiptBotToken} onChange={(e) => setField("receiptBotToken", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="123456:ABC-DEF..." />
              </Field>
              <Field label="شناسهٔ عددی چت رسید (Chat ID)">
                <input value={tg.receiptChatId} onChange={(e) => setField("receiptChatId", e.target.value.replace(/[^\d-]/g, ""))} dir="ltr" className={`${inputCls} text-left`} placeholder="-1001234567890" />
              </Field>
              <p className="text-xs text-white/45 sm:col-span-2">
                فقط عدد (آیدی کاربر) یا عددِ منفی (گروه/کانال). فقط تصمیم‌های ارسال‌شده از همین چت پذیرفته می‌شوند.
                برای جلوگیری از تداخل، حتماً یک بات <b>جدید و جدا</b> از ربات بکاپ بسازید.
              </p>
            </div>
          )}

          <div className="mt-5 flex flex-wrap items-center gap-3">
            <button onClick={save} disabled={saving} className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50">
              {saving ? <Spinner /> : "ذخیره تنظیمات"}
            </button>
            <button onClick={test} disabled={testing} className="flex h-11 items-center gap-2 rounded-xl border border-white/10 px-6 text-sm font-bold text-white/80 transition hover:bg-white/5 disabled:opacity-50">
              {testing ? <Spinner /> : "ارسال پیام تست"}
            </button>
            {note && <span className={`text-sm ${note.ok ? "text-emerald-400" : "text-rose-400"}`}>{note.text}</span>}
          </div>
        </Card>
      ) : null}
    </div>
  );
}
