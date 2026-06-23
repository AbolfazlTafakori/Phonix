"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { QRCodeSVG } from "qrcode.react";
import { api } from "@/lib/api";
import type { TwoFactorSetup } from "@/lib/types";
import { Card, PageHeader, Spinner, inputCls } from "@/components/admin/ui";

export default function TwoFactorSettingsPage() {
  const router = useRouter();
  const [enabled, setEnabled] = useState(false);
  const [loading, setLoading] = useState(true);
  const [setup, setSetup] = useState<TwoFactorSetup | null>(null);
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [saved, setSaved] = useState("");

  async function refresh() {
    try {
      const s = await api.auth.twoFactor.status();
      setEnabled(s.enabled);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    refresh();
  }, []);

  async function beginSetup() {
    setBusy(true);
    setError("");
    setSaved("");
    try {
      setSetup(await api.auth.twoFactor.setup());
      setCode("");
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در راه‌اندازی");
    } finally {
      setBusy(false);
    }
  }

  async function enable() {
    setBusy(true);
    setError("");
    try {
      await api.auth.twoFactor.enable(code.trim());
      setSetup(null);
      setCode("");
      setEnabled(true);
      setSaved("ورود دو‌مرحله‌ای فعال شد. در حال انتقال به پنل…");
      // they may have been forced here by the mandatory-setup gate; now that it's on, let them into the panel.
      router.replace("/admin");
    } catch (e) {
      setError(e instanceof Error ? e.message : "کد نادرست است");
    } finally {
      setBusy(false);
    }
  }

  async function disable() {
    setBusy(true);
    setError("");
    try {
      await api.auth.twoFactor.disable(code.trim());
      setCode("");
      setEnabled(false);
      setSaved("ورود دو‌مرحله‌ای غیرفعال شد.");
    } catch (e) {
      setError(e instanceof Error ? e.message : "کد نادرست است");
    } finally {
      setBusy(false);
    }
  }

  const codeInput = (
    <input
      value={code}
      onChange={(e) => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))}
      inputMode="numeric"
      dir="ltr"
      placeholder="------"
      className={`${inputCls} max-w-[200px] text-center text-lg tracking-[0.5em]`}
    />
  );

  return (
    <div>
      <PageHeader title="امنیت و ورود دو‌مرحله‌ای" desc="با فعال‌سازی کد یک‌بارمصرف (TOTP)، ورود حساب مدیریتی شما در برابر سرقت رمز ایمن می‌شود." />

      {!loading && !enabled && (
        <div className="mb-5 max-w-2xl rounded-xl border border-amber-500/30 bg-amber-500/[0.07] p-4 text-sm leading-7 text-amber-200/90">
          برای استفاده از پنل مدیریت، فعال‌سازی ورود دو‌مرحله‌ای <b>الزامی</b> است. تا زمانی که این مرحله را کامل نکنید، به سایر بخش‌ها دسترسی نخواهید داشت.
        </div>
      )}

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : (
        <Card className="max-w-2xl p-6">
          <div className="mb-5 flex items-center gap-3">
            <span className={`h-2.5 w-2.5 rounded-full ${enabled ? "bg-emerald-400" : "bg-white/30"}`} />
            <p className="text-sm font-bold text-white">
              وضعیت: {enabled ? "فعال" : "غیرفعال"}
            </p>
          </div>

          {!enabled && !setup && (
            <div className="space-y-4">
              <p className="text-sm leading-7 text-white/70">
                برای فعال‌سازی، یک برنامه‌ی احرازکننده مانند Google Authenticator یا Authy روی گوشی نصب کنید، سپس روی دکمه‌ی زیر بزنید تا کد QR ساخته شود.
              </p>
              <button
                onClick={beginSetup}
                disabled={busy}
                className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
              >
                {busy ? <Spinner /> : "شروع راه‌اندازی"}
              </button>
            </div>
          )}

          {!enabled && setup && (
            <div className="space-y-5">
              <ol className="list-inside list-decimal space-y-1.5 text-sm leading-7 text-white/70">
                <li>کد QR زیر را در برنامه‌ی احرازکننده اسکن کنید.</li>
                <li>کد ۶ رقمی نمایش‌داده‌شده را وارد و تأیید کنید.</li>
              </ol>

              <div className="flex flex-col items-center gap-4 sm:flex-row sm:items-start">
                <div className="shrink-0 rounded-2xl bg-white p-3">
                  <QRCodeSVG value={setup.otpAuthUri} size={168} />
                </div>
                <div className="min-w-0 flex-1 space-y-2">
                  <p className="text-xs text-white/50">اگر نمی‌توانید اسکن کنید، این کلید را دستی وارد کنید:</p>
                  <code className="block break-all rounded-lg border border-white/10 bg-[#0d0d15] px-3 py-2 text-left text-xs text-white/80" dir="ltr">
                    {setup.secret}
                  </code>
                </div>
              </div>

              <div className="space-y-3">
                <label className="block text-sm text-white/80">کد تأیید</label>
                <div className="flex flex-wrap items-center gap-3">
                  {codeInput}
                  <button
                    onClick={enable}
                    disabled={busy || code.length !== 6}
                    className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50"
                  >
                    {busy ? <Spinner /> : "تأیید و فعال‌سازی"}
                  </button>
                </div>
              </div>
            </div>
          )}

          {enabled && (
            <div className="space-y-3">
              <p className="text-sm leading-7 text-white/70">
                ورود دو‌مرحله‌ای برای حساب شما فعال است. برای غیرفعال‌سازی، کد فعلی برنامه‌ی احرازکننده را وارد کنید.
              </p>
              <div className="flex flex-wrap items-center gap-3">
                {codeInput}
                <button
                  onClick={disable}
                  disabled={busy || code.length !== 6}
                  className="flex h-11 items-center gap-2 rounded-xl border border-rose-500/40 px-6 text-sm font-bold text-rose-400 transition hover:bg-rose-500/10 disabled:opacity-50"
                >
                  {busy ? <Spinner /> : "غیرفعال‌سازی"}
                </button>
              </div>
            </div>
          )}

          {error && <p className="mt-4 text-sm text-rose-400">{error}</p>}
          {saved && <p className="mt-4 text-sm text-emerald-400">{saved}</p>}
        </Card>
      )}
    </div>
  );
}
