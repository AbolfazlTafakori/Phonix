"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { CustomerBotStatus } from "@/lib/types";
import { formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";

// The customer Telegram bot: customers link their account to it and get their account mail there too.
// Two switches on purpose — «فعال» runs the bot (so it can be tested), «نمایش به کاربران» is what puts the
// «اتصال به تلگرام» button in customers' accounts. Until the second is on, customers see nothing. A third,
// «فروشگاه داخل تلگرام», opens the site inside Telegram (Mini App) from the bot's menu button and /start.
export default function AdminCustomerBotPage() {
  const [status, setStatus] = useState<CustomerBotStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [token, setToken] = useState("");
  const [enabled, setEnabled] = useState(false);
  const [isPublic, setIsPublic] = useState(false);
  const [shop, setShop] = useState(false);
  const [sales, setSales] = useState(false);
  const [codeMinutes, setCodeMinutes] = useState("15");
  const [busy, setBusy] = useState<"" | "save" | "test" | "remove">("");
  const [note, setNote] = useState<{ ok: boolean; text: string } | null>(null);

  function apply(s: CustomerBotStatus) {
    setStatus(s);
    setEnabled(s.enabled);
    setIsPublic(s.public);
    setShop(s.shop);
    setSales(s.sales);
    setCodeMinutes(String(s.codeMinutes));
  }

  useEffect(() => {
    let cancelled = false;
    api.customerBot
      .get()
      .then((s) => { if (!cancelled) apply(s); })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : "خطا در بارگذاری"); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []);

  const minutes = Number(codeMinutes);
  const minutesValid = Number.isInteger(minutes) && minutes >= 1 && minutes <= 1440;

  async function save() {
    setBusy("save");
    setNote(null);
    try {
      const s = await api.customerBot.save({ enabled, public: isPublic, token: token.trim() || null, shop, codeMinutes: minutes, sales });
      apply(s);
      setToken("");
      // Saved either way; a warning means Telegram didn't take the menu button.
      setNote(s.warning ? { ok: false, text: `ذخیره شد، اما: ${s.warning}` } : { ok: true, text: "ذخیره شد." });
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
      const r = await api.customerBot.test();
      setNote({ ok: true, text: `اتصال به تلگرام برقرار است — ربات @${r.username}` });
      apply(await api.customerBot.get());
    } catch (e) {
      setNote({ ok: false, text: e instanceof Error ? e.message : "تست ناموفق بود." });
    } finally {
      setBusy("");
    }
  }

  async function remove() {
    if (!confirm("توکن ربات حذف شود؟ ربات خاموش می‌شود و دکمه‌ی اتصال از حساب کاربران برداشته می‌شود. اتصال‌های قبلی کاربران حفظ می‌شود.")) return;
    setBusy("remove");
    setNote(null);
    try {
      apply(await api.customerBot.removeToken());
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
        title="ربات تلگرام مشتریان"
        desc="کاربران حساب خود را به این ربات وصل می‌کنند و اطلاعات سفارش‌ها، تأیید پرداخت‌ها و پیام‌های پشتیبانی را در تلگرام هم دریافت می‌کنند."
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
                توکن پیش از ذخیره با تلگرام بررسی می‌شود و رمزنگاری‌شده نگه داشته می‌شود؛ بعد از ذخیره هرگز دوباره نمایش داده نمی‌شود.
                برای این ربات حتماً یک ربات <b>جدا</b> از ربات‌های رسید، سفارش و بکاپ بسازید.
              </p>

              <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
                <span>
                  <span className="block text-sm text-white/85">فعال</span>
                  <span className="block text-[11px] text-white/40">ربات روشن می‌شود و پیام‌ها را دریافت و ارسال می‌کند.</span>
                </span>
                <Toggle checked={enabled} onChange={(v) => { setEnabled(v); if (!v) { setIsPublic(false); setShop(false); setSales(false); } }} />
              </label>

              <label className={`flex items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3 ${enabled ? "cursor-pointer" : "opacity-50"}`}>
                <span>
                  <span className="block text-sm text-white/85">نمایش «اتصال به تلگرام» به کاربران</span>
                  <span className="block text-[11px] text-white/40">تا روشن نشود، کاربران هیچ دکمه‌ای در حساب خود نمی‌بینند. اول ربات را تست کنید.</span>
                </span>
                <Toggle checked={isPublic && enabled} onChange={(v) => enabled && setIsPublic(v)} />
              </label>

              <label className={`flex items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3 ${enabled && status.shopUrl ? "cursor-pointer" : "opacity-50"}`}>
                <span>
                  <span className="block text-sm text-white/85">فروشگاه داخل تلگرام (Mini App)</span>
                  <span className="block text-[11px] leading-5 text-white/40">
                    {status.shopUrl
                      ? "دکمه‌ی «🛒 فروشگاه» در منوی ربات و پیام /start اضافه می‌شود و خود سایت داخل تلگرام باز می‌شود. کاربری که تلگرامش را به حساب وصل کرده، خودکار وارد می‌شود."
                      : "آدرس سایت (PHONIX_FRONTEND_URL) https نیست؛ تلگرام فروشگاه را فقط روی https باز می‌کند."}
                  </span>
                </span>
                <Toggle checked={shop && enabled} onChange={(v) => enabled && !!status.shopUrl && setShop(v)} />
              </label>

              <label className={`flex items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3 ${enabled ? "cursor-pointer" : "opacity-50"}`}>
                <span>
                  <span className="block text-sm text-white/85">فروش داخل ربات</span>
                  <span className="block text-[11px] leading-5 text-white/40">
                    محصولاتی که در صفحه‌ی محصول «خرید در ربات تلگرام بدون ثبت‌نام» برایشان روشن است، داخل خود ربات و بدون حساب سایت
                    خریده می‌شوند. بقیه‌ی محصولات فقط با ثبت‌نام و اتصال حساب.
                    {status.salesCard ? ` کارت مقصد: ${status.salesCard}` : " ⚠️ هیچ روش پرداخت کارت‌به‌کارت فعالی نیست؛ تا یکی فعال نشود چیزی فروخته نمی‌شود."}
                  </span>
                </span>
                <Toggle checked={sales && enabled} onChange={(v) => enabled && setSales(v)} />
              </label>

              <Field label="اعتبار کد اتصال (دقیقه)">
                <input
                  type="number"
                  min={1}
                  max={1440}
                  value={codeMinutes}
                  onChange={(e) => setCodeMinutes(e.target.value)}
                  dir="ltr"
                  className={`${inputCls} text-left`}
                />
              </Field>
              <p className="-mt-3 text-[11px] leading-5 text-white/40">
                کاربر برای وصل شدن، یک کد ۲۴ رقمی یک‌بارمصرف به ایمیل تأییدشده‌اش می‌گیرد و آن را در ربات می‌فرستد. کد پس از این مدت (۱ تا ۱۴۴۰ دقیقه) باطل می‌شود.
                {!minutesValid && <span className="block text-rose-400">عددی بین ۱ تا ۱۴۴۰ وارد کنید.</span>}
              </p>

              {note && <p className={`text-sm ${note.ok ? "text-emerald-400" : "text-rose-400"}`}>{note.text}</p>}

              <div className="flex flex-wrap gap-3">
                <button
                  onClick={save}
                  disabled={busy !== "" || (enabled && !willHaveToken) || !minutesValid}
                  className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50"
                >
                  {busy === "save" ? <Spinner /> : "ذخیره"}
                </button>
                <button
                  onClick={test}
                  disabled={busy !== "" || !status.hasToken}
                  className="h-11 rounded-xl border border-white/10 px-6 text-sm font-bold text-white/80 transition hover:bg-white/5 disabled:opacity-50"
                >
                  {busy === "test" ? <Spinner /> : "تست اتصال"}
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
                <dt className="text-white/50">نمایش به کاربران</dt>
                <dd className={status.public ? "text-emerald-400" : "text-white/45"}>{status.public ? "بله" : "خیر"}</dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-white/50">فروش داخل ربات</dt>
                <dd className={status.sales ? "text-emerald-400" : "text-white/45"}>
                  {status.sales ? `روشن — ${formatNumber(status.salesProducts)} محصول` : "خاموش"}
                </dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-white/50">فروشگاه داخل تلگرام</dt>
                <dd className={status.shop ? "text-emerald-400" : "text-white/45"}>{status.shop ? "روشن" : "خاموش"}</dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-white/50">حساب‌های متصل</dt>
                <dd className="text-white/85">{formatNumber(status.linkedCount)}</dd>
              </div>
            </dl>
            <div className="mt-6 space-y-2 border-t border-white/8 pt-4 text-[12px] leading-6 text-white/45">
              <p className="font-bold text-white/60">راه‌اندازی</p>
              <p>۱. در تلگرام به ‎@BotFather پیام بدهید، ‎/newbot بزنید و توکن را بگیرید.</p>
              <p>۲. توکن را اینجا وارد کنید، «فعال» را روشن کنید و ذخیره کنید.</p>
              <p>۳. «تست اتصال» بزنید و ربات را خودتان در تلگرام باز کنید.</p>
              <p>۴. وقتی مطمئن شدید، «نمایش به کاربران» را روشن کنید.</p>
              <p className="pt-2 font-bold text-white/60">فروشگاه داخل تلگرام</p>
              <p>۱. «فروشگاه داخل تلگرام» را روشن و ذخیره کنید.</p>
              <p>۲. ربات را در تلگرام گوشی باز کنید (اگر باز بود، یک بار ببندید) و دکمه‌ی «🛒 فروشگاه» را بزنید.</p>
              <p>۳. یک بار وارد حساب شوید، از منوی حساب کاربری «اتصال به تلگرام» را بزنید و کدی را که به ایمیلتان می‌آید همان‌جا وارد کنید؛ دفعه‌ی بعد خودکار وارد می‌شوید.</p>
              <p>در تلگرام گوشی و دسکتاپ کار می‌کند؛ نسخه‌ی وب تلگرام (web.telegram.org) پشتیبانی نمی‌شود.</p>
              <p className="pt-2 font-bold text-white/60">فروش داخل ربات</p>
              <p>۱. در صفحه‌ی محصول، «خرید در ربات تلگرام بدون ثبت‌نام» را برای محصولات دلخواه روشن کنید.</p>
              <p>۲. اینجا «فروش داخل ربات» را روشن و ذخیره کنید.</p>
              <p>۳. هر کسی در ربات «🛍 خرید» را بزند، آن محصولات را می‌بیند، کارت‌به‌کارت می‌کند و عکس رسید را می‌فرستد؛ رسید مثل همیشه در ربات رسید بررسی می‌شود و اکانت در خود ربات تحویل می‌شود.</p>
              <p>برای امنیت: هر نفر هم‌زمان فقط یک خرید بررسی‌نشده و روزانه حداکثر ۵ خرید دارد، و قیمت تا ۳۰ دقیقه ثابت می‌ماند.</p>
            </div>
          </Card>
        </div>
      )}
    </div>
  );
}
