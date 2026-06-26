"use client";

import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import type { TelegramSettings } from "@/lib/types";
import { Card, PageHeader, Spinner, Toggle, Field, Modal, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

type Note = { ok: boolean; text: string } | null;

export default function BackupPage() {
  const [tg, setTg] = useState<TelegramSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [downloading, setDownloading] = useState(false);
  const [downloadNote, setDownloadNote] = useState<Note>(null);

  const fileRef = useRef<HTMLInputElement>(null);
  const [restoreFile, setRestoreFile] = useState<File | null>(null);
  const [restoring, setRestoring] = useState(false);
  const [restoreNote, setRestoreNote] = useState<Note>(null);
  const [restoreModal, setRestoreModal] = useState(false);
  const [restoreKey, setRestoreKey] = useState("");
  const [restoreOtp, setRestoreOtp] = useState("");

  const [savingTg, setSavingTg] = useState(false);
  const [savedTg, setSavedTg] = useState(false);
  const [testingTg, setTestingTg] = useState(false);
  const [testingAlert, setTestingAlert] = useState(false);
  const [tgNote, setTgNote] = useState<Note>(null);

  // per-section backup
  type Section = { key: string; label: string };
  type Hist = { section: string; target: string; ok: boolean; error: string; atUtc: string };
  const [sections, setSections] = useState<Section[]>([]);
  const [history, setHistory] = useState<Hist[]>([]);
  const [busyKey, setBusyKey] = useState<string>("");
  const [instantBusy, setInstantBusy] = useState(false);
  const [secNote, setSecNote] = useState<Note>(null);
  const [restoreSectionKey, setRestoreSectionKey] = useState<string | null>(null);
  const secFileRef = useRef<HTMLInputElement>(null);
  const pendingSection = useRef<string | null>(null);

  async function loadSections() {
    try {
      const d = await api.backup.sections();
      setSections(d.sections);
      setHistory(d.history);
    } catch { /* keep */ }
  }

  useEffect(() => {
    (async () => {
      try {
        setTg(await api.backup.telegram.get());
        await loadSections();
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  async function downloadSection(key: string) {
    setBusyKey(`dl:${key}`);
    setSecNote(null);
    try {
      const { blob, filename } = await api.backup.downloadSection(key);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url; a.download = filename; document.body.appendChild(a); a.click(); a.remove();
      URL.revokeObjectURL(url);
      await loadSections();
    } catch (e) {
      setSecNote({ ok: false, text: e instanceof Error ? e.message : "دانلود ناموفق بود." });
    } finally {
      setBusyKey("");
    }
  }

  async function sendSection(key: string) {
    setBusyKey(`tg:${key}`);
    setSecNote(null);
    try {
      await api.backup.sendSection(key);
      setSecNote({ ok: true, text: "این بخش به تلگرام ارسال شد." });
      await loadSections();
    } catch (e) {
      setSecNote({ ok: false, text: e instanceof Error ? e.message : "ارسال ناموفق بود." });
    } finally {
      setBusyKey("");
    }
  }

  async function instantBackup() {
    setInstantBusy(true);
    setSecNote(null);
    try {
      await api.backup.sendAll();
      setSecNote({ ok: true, text: "بکاپ لحظه‌ای همهٔ بخش‌ها به تلگرام ارسال شد." });
      await loadSections();
    } catch (e) {
      setSecNote({ ok: false, text: e instanceof Error ? e.message : "ارسال ناموفق بود." });
    } finally {
      setInstantBusy(false);
    }
  }

  function pickSectionRestore(key: string) {
    pendingSection.current = key;
    secFileRef.current?.click();
  }
  function onSectionFile(file: File | undefined) {
    if (!file || !pendingSection.current) return;
    setRestoreFile(file);
    setRestoreSectionKey(pendingSection.current);
    setRestoreKey("");
    setRestoreOtp("");
    setRestoreNote(null);
    setRestoreModal(true);
    if (secFileRef.current) secFileRef.current.value = "";
  }

  const setField = <K extends keyof TelegramSettings>(key: K, value: TelegramSettings[K]) =>
    setTg((d) => (d ? { ...d, [key]: value } : d));

  async function downloadBackup() {
    setDownloading(true);
    setDownloadNote(null);
    try {
      const { blob, filename } = await api.backup.download();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
      setDownloadNote({ ok: true, text: "نسخه پشتیبان دانلود شد." });
    } catch (e) {
      setDownloadNote({ ok: false, text: e instanceof Error ? e.message : "دانلود ناموفق بود." });
    } finally {
      setDownloading(false);
    }
  }

  function openRestore() {
    if (!restoreFile) return;
    setRestoreSectionKey(null); // full restore
    setRestoreNote(null);
    setRestoreKey("");
    setRestoreOtp("");
    setRestoreModal(true);
  }

  async function confirmRestore() {
    if (!restoreFile) return;
    if (!restoreKey.trim() || restoreOtp.trim().length !== 6) {
      setRestoreNote({ ok: false, text: "کلید پشتیبان و کد ۶ رقمی دو‌مرحله‌ای الزامی است." });
      return;
    }
    setRestoring(true);
    setRestoreNote(null);
    try {
      if (restoreSectionKey) {
        await api.backup.restoreSection(restoreSectionKey, restoreFile, restoreKey.trim(), restoreOtp.trim());
      } else {
        await api.backup.restore(restoreFile, restoreKey.trim(), restoreOtp.trim());
      }
      setRestoreModal(false);
      setRestoreNote({ ok: true, text: "بازیابی با موفقیت انجام شد. برای دیدن داده‌های جدید، صفحه را تازه‌سازی کنید." });
      setRestoreFile(null);
      setRestoreKey("");
      setRestoreOtp("");
      setRestoreSectionKey(null);
      if (fileRef.current) fileRef.current.value = "";
      await loadSections();
    } catch (e) {
      setRestoreNote({ ok: false, text: e instanceof Error ? e.message : "بازیابی ناموفق بود." });
    } finally {
      setRestoring(false);
    }
  }

  async function saveTg() {
    if (!tg) return;
    setSavingTg(true);
    setSavedTg(false);
    setTgNote(null);
    try {
      setTg(await api.backup.telegram.update(tg));
      setSavedTg(true);
    } catch (e) {
      setTgNote({ ok: false, text: e instanceof Error ? e.message : "ذخیره ناموفق بود." });
    } finally {
      setSavingTg(false);
    }
  }

  async function testTg() {
    setTestingTg(true);
    setTgNote(null);
    try {
      await api.backup.telegram.test();
      setTgNote({ ok: true, text: "پشتیبان آزمایشی به تلگرام ارسال شد. چت مقصد را بررسی کنید." });
      setTg(await api.backup.telegram.get());
    } catch (e) {
      setTgNote({ ok: false, text: e instanceof Error ? e.message : "ارسال ناموفق بود." });
    } finally {
      setTestingTg(false);
    }
  }

  async function testAlertTg() {
    setTestingAlert(true);
    setTgNote(null);
    try {
      await api.backup.telegram.testAlert();
      setTgNote({ ok: true, text: "هشدار آزمایشی به تلگرام ارسال شد. چت مقصد را بررسی کنید." });
    } catch (e) {
      setTgNote({ ok: false, text: e instanceof Error ? e.message : "ارسال ناموفق بود." });
    } finally {
      setTestingAlert(false);
    }
  }

  const lastBackup = tg?.lastBackupAtUtc
    ? new Date(tg.lastBackupAtUtc).toLocaleString("fa-IR", { dateStyle: "medium", timeStyle: "short" })
    : "هنوز انجام نشده";

  return (
    <div>
      <PageHeader title="پشتیبان‌گیری و بازیابی" desc="از کل داده‌های فروشگاه نسخه پشتیبان بگیرید، بازیابی کنید، یا ارسال خودکار به تلگرام را تنظیم کنید." />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error || !tg ? (
        <Card className="p-8 text-center text-rose-400">{error || "اطلاعات یافت نشد"}</Card>
      ) : (
        <div className="grid gap-6 lg:grid-cols-2">
          {/* manual backup + restore */}
          <div className="space-y-6">
            <Card className="p-6">
              <div className="mb-3 flex items-center gap-3">
                <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-emerald-500/15 text-emerald-400"><AdminIcon name="disk" className="h-5 w-5" /></span>
                <h3 className="text-lg font-bold text-white">دانلود نسخه پشتیبان</h3>
              </div>
              <p className="mb-4 text-sm leading-7 text-white/55">یک فایل کامل از همه‌ی داده‌ها (محصولات، کاربران، سفارش‌ها، تنظیمات و...) دانلود می‌شود. آن را جای امنی نگه دارید.</p>
              <button onClick={downloadBackup} disabled={downloading} className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-emerald-600 to-emerald-500 px-6 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50">
                {downloading ? <Spinner /> : "دانلود فایل پشتیبان"}
              </button>
              {downloadNote && <p className={`mt-3 text-sm ${downloadNote.ok ? "text-emerald-400" : "text-rose-400"}`}>{downloadNote.text}</p>}
            </Card>

            <Card className="border-rose-500/20 p-6">
              <div className="mb-3 flex items-center gap-3">
                <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-rose-500/15 text-rose-400"><AdminIcon name="shield" className="h-5 w-5" /></span>
                <h3 className="text-lg font-bold text-white">بازیابی از فایل</h3>
              </div>
              <p className="mb-4 text-sm leading-7 text-amber-300/80">⚠ بازیابی، تمام داده‌های فعلی را با فایل انتخاب‌شده جایگزین می‌کند. این کار قابل بازگشت نیست — ابتدا یک پشتیبان از وضعیت فعلی بگیرید.</p>
              <input
                ref={fileRef}
                type="file"
                accept="application/json,.json,.phxbak"
                onChange={(e) => { setRestoreFile(e.target.files?.[0] ?? null); setRestoreNote(null); }}
                className="block w-full text-sm text-white/70 file:ml-3 file:rounded-lg file:border-0 file:bg-white/10 file:px-4 file:py-2 file:text-sm file:font-bold file:text-white hover:file:bg-white/15"
              />
              <button onClick={openRestore} disabled={!restoreFile} className="mt-4 flex h-11 items-center gap-2 rounded-xl border border-rose-500/40 bg-rose-500/10 px-6 text-sm font-bold text-rose-300 transition hover:bg-rose-500/20 disabled:opacity-40">
                بازیابی و جایگزینی داده‌ها
              </button>
              {restoreNote && <p className={`mt-3 text-sm ${restoreNote.ok ? "text-emerald-400" : "text-rose-400"}`}>{restoreNote.text}</p>}
            </Card>
          </div>

          {/* telegram auto-backup */}
          <div className="space-y-6">
            <Card className="p-6">
              <div className="mb-4 flex items-center justify-between">
                <h3 className="text-lg font-bold text-white">پشتیبان خودکار تلگرام</h3>
                {savedTg && <span className="text-sm font-medium text-emerald-400">✓ ذخیره شد</span>}
              </div>
              <label className="mb-3 flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
                <span className="text-sm font-bold text-white/85">ارسال خودکار پشتیبان به تلگرام</span>
                <Toggle checked={tg.backupEnabled} onChange={(v) => setField("backupEnabled", v)} />
              </label>
              <label className="mb-5 flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
                <span className="text-sm font-bold text-white/85">هشدار خطا و راه‌اندازی سرور در تلگرام</span>
                <Toggle checked={tg.alertsEnabled} onChange={(v) => setField("alertsEnabled", v)} />
              </label>
              <div className="grid gap-4">
                <Field label="توکن بات (از BotFather)">
                  <input value={tg.botToken} onChange={(e) => setField("botToken", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="123456:ABC-DEF..." />
                </Field>
                <Field label="شناسهٔ عددی چت مقصد (Chat ID)">
                  <input value={tg.chatId} onChange={(e) => setField("chatId", e.target.value.replace(/[^\d-]/g, ""))} dir="ltr" className={`${inputCls} text-left`} placeholder="-1001234567890" />
                  <p className="mt-1.5 text-xs text-white/45">فقط عدد (آیدی کاربر) یا عددِ منفی (گروه/کانال). بکاپ‌ها فقط به همین یک چت ارسال می‌شوند؛ ربات به هیچ چت دیگری چیزی نمی‌فرستد.</p>
                </Field>
                <Field label="فاصله‌ی زمانی هر بکاپ (ساعت)">
                  <input type="number" dir="ltr" min={1} value={tg.intervalHours} onChange={(e) => setField("intervalHours", Math.max(1, Number(e.target.value)))} className={`${inputCls} text-left`} />
                </Field>
              </div>

              <div className="mt-5 flex flex-wrap gap-3">
                <button onClick={saveTg} disabled={savingTg} className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50">
                  {savingTg ? <Spinner /> : "ذخیره تنظیمات"}
                </button>
                <button onClick={testTg} disabled={testingTg} className="flex h-11 items-center gap-2 rounded-xl border border-white/15 px-6 text-sm font-bold text-white/85 transition hover:bg-white/5 disabled:opacity-50">
                  {testingTg ? <Spinner /> : "ارسال پشتیبان آزمایشی"}
                </button>
                <button onClick={testAlertTg} disabled={testingAlert} className="flex h-11 items-center gap-2 rounded-xl border border-white/15 px-6 text-sm font-bold text-white/85 transition hover:bg-white/5 disabled:opacity-50">
                  {testingAlert ? <Spinner /> : "ارسال هشدار آزمایشی"}
                </button>
              </div>
              {tgNote && <p className={`mt-3 text-sm ${tgNote.ok ? "text-emerald-400" : "text-rose-400"}`}>{tgNote.text}</p>}

              <div className="mt-5 grid gap-2 border-t border-white/8 pt-4 text-sm">
                <div className="flex items-center justify-between">
                  <span className="text-white/45">آخرین پشتیبان موفق</span>
                  <span className="text-white/80">{lastBackup}</span>
                </div>
                {tg.lastBackupError && (
                  <div className="flex items-start justify-between gap-3">
                    <span className="shrink-0 text-white/45">آخرین خطا</span>
                    <span className="text-left text-rose-400">{tg.lastBackupError}</span>
                  </div>
                )}
              </div>
            </Card>

            <Card className="p-6">
              <h3 className="mb-3 text-lg font-bold text-white">راهنما</h3>
              <ul className="space-y-2 text-sm leading-7 text-white/65">
                <li>• یک بات از <span dir="ltr">@BotFather</span> بسازید و توکن آن را اینجا وارد کنید.</li>
                <li>• بات را به گروه/کانال مقصد اضافه کنید و شناسه‌ی آن چت را وارد کنید (برای دریافت Chat ID می‌توانید از <span dir="ltr">@userinfobot</span> کمک بگیرید).</li>
                <li>• پس از ذخیره، با «ارسال پشتیبان آزمایشی» از درستی تنظیمات مطمئن شوید.</li>
                <li>• ارسال خودکار طبق فاصله‌ی زمانی تعیین‌شده توسط سرور انجام می‌شود.</li>
                <li>• با فعال‌کردن «هشدار خطا و راه‌اندازی»، هر خطای داخلی سرور و هر بار راه‌اندازی مجدد به همین چت اطلاع داده می‌شود (هشدارهای تکراری تا چند دقیقه یک‌بار ارسال می‌شوند).</li>
              </ul>
            </Card>
          </div>

          {/* per-section backup — full width */}
          <div className="lg:col-span-2 space-y-6">
            <Card className="p-6">
              <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
                <div>
                  <h3 className="text-lg font-bold text-white">پشتیبان بخش‌بخش</h3>
                  <p className="mt-1 text-sm text-white/55">هر بخش جدا دانلود/ارسال/بازیابی می‌شود؛ همه رمزنگاری‌شده و فقط به همان چت تلگرامِ تعیین‌شده می‌رود.</p>
                </div>
                <button onClick={instantBackup} disabled={instantBusy} className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-emerald-600 to-emerald-500 px-6 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50">
                  {instantBusy ? <Spinner /> : "📦 بکاپ لحظه‌ای کامل به تلگرام"}
                </button>
              </div>

              <input ref={secFileRef} type="file" accept="application/json,.json,.phxbak" className="hidden" onChange={(e) => onSectionFile(e.target.files?.[0])} />

              <div className="divide-y divide-white/6">
                {sections.map((s) => (
                  <div key={s.key} className="flex flex-wrap items-center justify-between gap-3 py-3">
                    <span className="text-sm font-bold text-white">{s.label}</span>
                    <div className="flex flex-wrap items-center gap-2">
                      <button onClick={() => downloadSection(s.key)} disabled={busyKey === `dl:${s.key}`} className="flex h-9 items-center gap-1.5 rounded-lg border border-white/10 px-3 text-xs font-bold text-white/80 transition hover:bg-white/5 disabled:opacity-50">
                        {busyKey === `dl:${s.key}` ? <Spinner /> : "دانلود"}
                      </button>
                      <button onClick={() => sendSection(s.key)} disabled={busyKey === `tg:${s.key}`} className="flex h-9 items-center gap-1.5 rounded-lg border border-[#3a64f2]/40 bg-[#3a64f2]/10 px-3 text-xs font-bold text-[#9db4ff] transition hover:bg-[#3a64f2]/20 disabled:opacity-50">
                        {busyKey === `tg:${s.key}` ? <Spinner /> : "ارسال تلگرام"}
                      </button>
                      <button onClick={() => pickSectionRestore(s.key)} className="flex h-9 items-center gap-1.5 rounded-lg border border-rose-500/30 px-3 text-xs font-bold text-rose-300 transition hover:bg-rose-500/10">
                        بازیابی
                      </button>
                    </div>
                  </div>
                ))}
              </div>
              {secNote && <p className={`mt-3 text-sm ${secNote.ok ? "text-emerald-400" : "text-rose-400"}`}>{secNote.text}</p>}
            </Card>

            <Card className="p-6">
              <h3 className="mb-3 text-lg font-bold text-white">تاریخچهٔ بکاپ‌ها</h3>
              {history.length === 0 ? (
                <p className="text-sm text-white/45">هنوز بکاپی ثبت نشده است.</p>
              ) : (
                <div className="max-h-72 space-y-1.5 overflow-y-auto">
                  {history.map((h, i) => (
                    <div key={i} className="flex items-center justify-between gap-3 rounded-lg bg-white/[0.02] px-3 py-2 text-xs">
                      <span className="flex items-center gap-2">
                        <span className={h.ok ? "text-emerald-400" : "text-rose-400"}>{h.ok ? "✓" : "✕"}</span>
                        <span className="font-bold text-white/85">{h.section}</span>
                        <span className="text-white/40">· {h.target}</span>
                        {!h.ok && h.error && <span className="text-rose-400/80">— {h.error}</span>}
                      </span>
                      <span className="shrink-0 text-white/35" dir="ltr">{new Date(h.atUtc).toLocaleString("fa-IR", { dateStyle: "short", timeStyle: "short" })}</span>
                    </div>
                  ))}
                </div>
              )}
            </Card>
          </div>
        </div>
      )}

      <Modal open={restoreModal} onClose={() => !restoring && setRestoreModal(false)} title="تأیید امنیتی بازیابی">
        <div className="space-y-4">
          <p className="rounded-xl border border-rose-500/20 bg-rose-500/5 p-3 text-sm leading-7 text-amber-300/85">
            ⚠ {restoreSectionKey
              ? `این عملیات فقط بخش «${sections.find((s) => s.key === restoreSectionKey)?.label ?? restoreSectionKey}» را با فایل انتخاب‌شده جایگزین می‌کند و بقیهٔ داده‌ها دست‌نخورده می‌ماند.`
              : "این عملیات تمام داده‌های فعلی را برای همیشه با فایل انتخاب‌شده جایگزین می‌کند."} برای ادامه، کلید پشتیبان سرور و کد دو‌مرحله‌ای فعلی خود را وارد کنید.
          </p>
          {restoreFile && (
            <p dir="ltr" className="truncate text-left font-mono text-xs text-white/50">{restoreFile.name}</p>
          )}
          <Field label="کلید پشتیبان (PHONIX_BACKUP_KEY)">
            <input
              type="password"
              value={restoreKey}
              onChange={(e) => setRestoreKey(e.target.value)}
              dir="ltr"
              autoComplete="off"
              placeholder="کلید ذخیره‌شده‌ی آفلاین"
              className={`${inputCls} text-left`}
            />
          </Field>
          <Field label="کد دو‌مرحله‌ای (۶ رقم)">
            <input
              value={restoreOtp}
              onChange={(e) => setRestoreOtp(e.target.value.replace(/\D/g, "").slice(0, 6))}
              inputMode="numeric"
              dir="ltr"
              autoComplete="one-time-code"
              placeholder="------"
              className={`${inputCls} text-center tracking-[0.5em]`}
            />
          </Field>
          {restoreNote && !restoreNote.ok && <p className="text-sm text-rose-400">{restoreNote.text}</p>}
          <div className="flex justify-end gap-3 pt-1">
            <button
              onClick={() => setRestoreModal(false)}
              disabled={restoring}
              className="h-11 rounded-xl border border-white/10 px-5 text-sm font-medium text-white/70 transition hover:text-white disabled:opacity-50"
            >
              انصراف
            </button>
            <button
              onClick={confirmRestore}
              disabled={restoring || !restoreKey.trim() || restoreOtp.length !== 6}
              className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-6 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-40"
            >
              {restoring ? <Spinner /> : "تأیید و بازیابی"}
            </button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
