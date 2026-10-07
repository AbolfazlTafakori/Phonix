"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { SupportBotStatus } from "@/lib/types";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";

// The support Telegram bot: new tickets, customer replies and live-chat messages go to a staff group, and staff
// answer them there by replying to the bot's message. Answers given on the site show up in the group too.
export default function AdminSupportBotPage() {
  const [status, setStatus] = useState<SupportBotStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [token, setToken] = useState("");
  const [chatId, setChatId] = useState("");
  const [enabled, setEnabled] = useState(false);
  const [busy, setBusy] = useState<"" | "save" | "test" | "remove">("");
  const [note, setNote] = useState<{ ok: boolean; text: string } | null>(null);

  function apply(s: SupportBotStatus) {
    setStatus(s);
    setEnabled(s.enabled);
    setChatId(s.chatId);
  }

  useEffect(() => {
    let cancelled = false;
    api.supportBot
      .get()
      .then((s) => { if (!cancelled) apply(s); })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : "خطا در بارگذاری"); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []);

  async function save() {
    setBusy("save");
    setNote(null);
    try {
      apply(await api.supportBot.save({ enabled, token: token.trim() || null, chatId: chatId.trim() }));
      setToken("");
      setNote({ ok: true, text: "ذخیره شد." });
    } catch (e) {
      setNote({ ok: false, text: e instanceof Error ? e.message : "ذخیره ناموفق بود." });
    } finally {
      setBusy("");
    }
  }

  async function test() {
    setBusy("test");
    setNote(null);
    try {
      await api.supportBot.test();
      setNote({ ok: true, text: "پیام آزمایشی در گروه ارسال شد." });
    } catch (e) {
      setNote({ ok: false, text: e instanceof Error ? e.message : "تست ناموفق بود." });
    } finally {
      setBusy("");
    }
  }

  async function remove() {
    if (!confirm("توکن ربات پشتیبانی حذف شود؟ ربات خاموش می‌شود و دیگر چیزی در گروه ارسال نمی‌شود.")) return;
    setBusy("remove");
    setNote(null);
    try {
      apply(await api.supportBot.removeToken());
      setNote({ ok: true, text: "توکن حذف شد و ربات خاموش شد." });
    } catch (e) {
      setNote({ ok: false, text: e instanceof Error ? e.message : "حذف ناموفق بود." });
    } finally {
      setBusy("");
    }
  }

  const willHaveToken = (status?.hasToken ?? false) || token.trim().length > 0;

  return (
    <div>
      <PageHeader
        title="ربات تلگرام پشتیبانی"
        desc="تیکت‌ها و گفت‌وگوهای آنلاین در یک گروه تلگرام ارسال می‌شوند و پشتیبان‌ها همان‌جا با «Reply» پاسخ می‌دهند."
      />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error || !status ? (
        <Card className="p-8 text-center text-rose-400">{error || "یافت نشد"}</Card>
      ) : (
        <div className="grid gap-6 lg:grid-cols-3">
          <Card className="p-6 lg:col-span-2">
            <h3 className="mb-5 text-lg font-bold text-white">تنظیمات ربات</h3>
            <div className="grid gap-5">
              <Field label={status.hasToken ? `توکن ربات (ذخیره‌شده: ${status.tokenHint})` : "توکن ربات (از BotFather)"}>
                <input
                  type="password"
                  autoComplete="off"
                  value={token}
                  onChange={(e) => setToken(e.target.value)}
                  dir="ltr"
                  placeholder={status.hasToken ? "برای تعویض، توکن جدید را وارد کنید" : "123456789:AA..."}
                  className={`${inputCls} text-left font-mono`}
                />
              </Field>
              <p className="-mt-3 text-[11px] leading-5 text-white/40">
                برای پشتیبانی یک ربات <b>جدا</b> بسازید؛ توکن ربات‌های رسید، سفارش، مشتریان و بکاپ قبول نمی‌شود. توکن پیش از ذخیره با
                تلگرام بررسی و رمزنگاری‌شده نگه داشته می‌شود و دیگر نمایش داده نمی‌شود.
              </p>

              <Field label="شناسه‌ی گروه پشتیبانی">
                <input
                  value={chatId}
                  onChange={(e) => setChatId(e.target.value)}
                  dir="ltr"
                  placeholder="-1001234567890"
                  className={`${inputCls} text-left font-mono`}
                />
              </Field>
              <p className="-mt-3 text-[11px] leading-5 text-white/40">
                ربات را به گروه اضافه کنید و داخل گروه <span dir="ltr" className="font-mono">/id</span> بفرستید؛ ربات شناسه را جواب می‌دهد.
                (برای این کار باید توکن ذخیره و «فعال» روشن باشد.) هر عضو این گروه می‌تواند به کاربران پاسخ دهد، پس فقط پشتیبان‌ها را عضو کنید.
              </p>

              <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
                <span>
                  <span className="block text-sm text-white/85">فعال</span>
                  <span className="block text-[11px] text-white/40">ربات روشن می‌شود، پیام‌ها را در گروه می‌فرستد و پاسخ‌ها را دریافت می‌کند.</span>
                </span>
                <Toggle checked={enabled} onChange={setEnabled} />
              </label>

              {note && <p className={`text-sm ${note.ok ? "text-emerald-400" : "text-rose-400"}`}>{note.text}</p>}

              <div className="flex flex-wrap gap-3">
                <button
                  onClick={save}
                  disabled={busy !== "" || (enabled && !willHaveToken)}
                  className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50"
                >
                  {busy === "save" ? <Spinner /> : "ذخیره"}
                </button>
                <button
                  onClick={test}
                  disabled={busy !== "" || !status.hasToken || !status.chatId}
                  className="h-11 rounded-xl border border-white/10 px-6 text-sm font-bold text-white/80 transition hover:bg-white/5 disabled:opacity-50"
                >
                  {busy === "test" ? <Spinner /> : "ارسال پیام آزمایشی"}
                </button>
                {status.hasToken && (
                  <button
                    onClick={remove}
                    disabled={busy !== ""}
                    className="h-11 rounded-xl border border-rose-500/30 px-6 text-sm font-bold text-rose-300 transition hover:bg-rose-500/10 disabled:opacity-50"
                  >
                    {busy === "remove" ? <Spinner /> : "حذف توکن"}
                  </button>
                )}
              </div>
            </div>
          </Card>

          <Card className="p-6">
            <h3 className="mb-5 text-lg font-bold text-white">وضعیت</h3>
            <dl className="grid gap-3 text-sm">
              <div className="flex justify-between gap-3">
                <dt className="text-white/50">ربات</dt>
                <dd dir="ltr" className="font-mono text-white/85">
                  {status.username ? (
                    <a href={`https://t.me/${status.username}`} target="_blank" rel="noopener noreferrer" className="text-[#6f93ff] hover:underline">
                      @{status.username}
                    </a>
                  ) : "—"}
                </dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-white/50">وضعیت اجرا</dt>
                <dd className={status.polling ? "text-emerald-400" : "text-white/45"}>{status.polling ? "در حال اجرا" : "خاموش"}</dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-white/50">گروه</dt>
                <dd dir="ltr" className="font-mono text-white/85">{status.chatId || "—"}</dd>
              </div>
            </dl>
            <div className="mt-6 space-y-2 border-t border-white/8 pt-4 text-[12px] leading-6 text-white/45">
              <p className="font-bold text-white/60">راه‌اندازی</p>
              <p>۱. در ‎@BotFather یک ربات جدید بسازید و توکنش را اینجا وارد کنید، «فعال» را روشن و ذخیره کنید.</p>
              <p>۲. ربات را به گروه پشتیبانی اضافه کنید و در گروه ‎/id بفرستید.</p>
              <p>۳. شناسه را اینجا وارد و ذخیره کنید، بعد «ارسال پیام آزمایشی» بزنید.</p>
              <p className="pt-2 font-bold text-white/60">کار با ربات</p>
              <p>هر تیکت یا پیام گفت‌وگو با برچسبی مثل ‎#ticket_12 در گروه می‌آید؛ برای پاسخ روی همان پیام «Reply» بزنید. پاسخ با نام «پشتیبانی فونیکس» برای کاربر ارسال می‌شود. دکمه‌ی «بستن» تیکت یا گفت‌وگو را می‌بندد.</p>
            </div>
          </Card>
        </div>
      )}
    </div>
  );
}
