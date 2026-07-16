"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { TelegramSettings } from "@/lib/types";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";

type Note = { ok: boolean; text: string } | null;

// Dedicated section for the order-fulfillment bot. It uses its OWN Telegram bot token + chat — a third bot,
// separate from both the backup/alerts bot and the receipt bot — so the orders group is its own room and the
// two bots never read each other's button taps.
export default function OrderBotPage() {
  const [tg, setTg] = useState<TelegramSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);
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

  return (
    <div>
      <PageHeader
        title="ارسال سفارشات و ربات تلگرام"
        desc="پس از تأیید پرداخت، هر اکانت خریداری‌شده جداگانه در گروه سفارشات ارسال می‌شود؛ تصمیم شما همان‌جا در سایت ثبت می‌شود."
      />

      {loading ? (
        <div className="flex justify-center py-16"><Spinner /></div>
      ) : error ? (
        <p className="text-sm text-rose-400">{error}</p>
      ) : tg ? (
        <Card className="max-w-2xl p-6">
          <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
            <span className="text-sm font-bold text-white/85">
              فعال‌سازی ربات سفارشات
              <span className="mt-0.5 block text-xs font-normal text-white/45">با یک بات و گروه مستقل از ربات رسید و ربات بکاپ کار می‌کند.</span>
            </span>
            <Toggle checked={tg.orderBotEnabled} onChange={(v) => setField("orderBotEnabled", v)} />
          </label>

          {tg.orderBotEnabled && (
            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              <Field label="توکن بات سفارشات (از BotFather)">
                <input value={tg.orderBotToken} onChange={(e) => setField("orderBotToken", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="123456:ABC-DEF..." />
              </Field>
              <Field label="شناسهٔ عددی گروه سفارشات (Chat ID)">
                <input value={tg.orderChatId} onChange={(e) => setField("orderChatId", e.target.value.replace(/[^\d-]/g, ""))} dir="ltr" className={`${inputCls} text-left`} placeholder="-1001234567890" />
              </Field>
              <p className="text-xs text-white/45 sm:col-span-2">
                فقط عدد (آیدی کاربر) یا عددِ منفی (گروه/کانال). فقط تصمیم‌های ارسال‌شده از همین چت پذیرفته می‌شوند.
                برای جلوگیری از تداخل، حتماً یک بات و گروه <b>جدید و جدا</b> از ربات رسید بسازید.
              </p>
            </div>
          )}

          <div className="mt-5 rounded-xl border border-amber-500/25 bg-amber-500/[0.06] px-4 py-3">
            <p className="text-xs leading-6 text-amber-300/90">
              ⚠ پیام‌های تلگرام رمزنگاری نمی‌شوند. اطلاعات حساب کاربران (مثل ایمیل و رمز اکانتی که خودشان وارد کرده‌اند)
              در این گروه به‌صورت متن ساده دیده می‌شود، پس فقط افراد مورد اعتماد را عضو گروه کنید.
            </p>
          </div>

          <div className="mt-5 flex flex-wrap items-center gap-3">
            <button onClick={save} disabled={saving} className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50">
              {saving ? <Spinner /> : "ذخیره تنظیمات"}
            </button>
            {note && <span className={`text-sm ${note.ok ? "text-emerald-400" : "text-rose-400"}`}>{note.text}</span>}
          </div>
        </Card>
      ) : null}
    </div>
  );
}
