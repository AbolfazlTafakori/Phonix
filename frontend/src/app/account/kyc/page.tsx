"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { KycRequest } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { PageTitle, Panel } from "@/components/account/Panel";
import ImageField from "@/components/admin/ImageField";

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3e3af2]";

export default function KycPage() {
  const { user } = useAuth();
  const [kyc, setKyc] = useState<KycRequest | null>(null);
  const [loading, setLoading] = useState(true);

  const [fullName, setFullName] = useState("");
  const [nationalId, setNationalId] = useState("");
  const [birthDate, setBirthDate] = useState("");
  const [cardImage, setCardImage] = useState("");
  const [selfieImage, setSelfieImage] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!user) return;
    (async () => {
      try {
        const k = await api.kyc.getForUser(user.id);
        setKyc(k);
        setFullName(k?.fullName || user.name || "");
        setNationalId(k?.nationalId || "");
        setBirthDate(k?.birthDate || "");
        setCardImage(k?.cardImage || "");
        setSelfieImage(k?.selfieImage || "");
      } finally {
        setLoading(false);
      }
    })();
  }, [user]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!user) return;
    if (!fullName.trim() || !nationalId.trim()) {
      setError("نام کامل و کد ملی الزامی است.");
      return;
    }
    if (!cardImage) {
      setError("تصویر کارت ملی را آپلود کنید.");
      return;
    }
    setSubmitting(true);
    setError("");
    try {
      const k = await api.kyc.submit({ userId: user.id, fullName: fullName.trim(), nationalId: nationalId.trim(), birthDate: birthDate.trim(), cardImage, selfieImage });
      setKyc(k);
    } catch (err) {
      setError(err instanceof Error ? err.message : "خطا در ارسال");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div>
      <PageTitle title="احراز هویت" desc="برای استفاده از همه‌ی امکانات، هویت خود را تأیید کنید." />

      {loading ? (
        <Panel>
          <div className="grid h-32 place-items-center">
            <span className="inline-block h-7 w-7 animate-spin rounded-full border-2 border-white/20 border-t-[#e60053]" />
          </div>
        </Panel>
      ) : kyc?.status === "Approved" ? (
        <Panel>
          <div className="flex flex-col items-center gap-3 py-8 text-center">
            <div className="grid h-16 w-16 place-items-center rounded-full bg-emerald-500/15 text-3xl text-emerald-400">✓</div>
            <h2 className="text-xl font-bold text-white">هویت شما تأیید شده است</h2>
            <p className="text-sm text-white/60">حساب شما احراز هویت شده و به همه‌ی امکانات دسترسی دارید.</p>
          </div>
        </Panel>
      ) : kyc?.status === "Pending" ? (
        <Panel>
          <div className="mb-6 flex items-center gap-3 rounded-xl border border-amber-500/20 bg-amber-500/10 px-4 py-3 text-sm text-amber-300">
            <span>⏳</span>
            مدارک شما ارسال شد و در حال بررسی توسط تیم پشتیبانی است.
          </div>
          <div className="grid gap-3 text-sm text-white/70 sm:grid-cols-2">
            <p>نام: <span className="text-white">{kyc.fullName}</span></p>
            <p>کد ملی: <span className="text-white" dir="ltr">{kyc.nationalId}</span></p>
            <p>تاریخ تولد: <span className="text-white">{kyc.birthDate || "—"}</span></p>
            <p>تاریخ ارسال: <span className="text-white">{kyc.date}</span></p>
          </div>
        </Panel>
      ) : (
        <Panel>
          {kyc?.status === "Rejected" && (
            <div className="mb-6 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-300">
              مدارک شما تأیید نشد{kyc.note ? `: ${kyc.note}` : "."} لطفاً اطلاعات را اصلاح و دوباره ارسال کنید.
            </div>
          )}
          {!kyc && (
            <div className="mb-6 flex items-center gap-3 rounded-xl border border-amber-500/20 bg-amber-500/10 px-4 py-3 text-sm text-amber-300">
              <span>⚠</span>
              حساب شما هنوز تأیید نشده است. لطفاً اطلاعات زیر را تکمیل کنید.
            </div>
          )}

          <form onSubmit={submit} className="grid gap-5 sm:grid-cols-2">
            <div>
              <label className="mb-2 block text-sm text-white/80">نام و نام خانوادگی</label>
              <input value={fullName} onChange={(e) => setFullName(e.target.value)} className={inputCls} />
            </div>
            <div>
              <label className="mb-2 block text-sm text-white/80">کد ملی</label>
              <input value={nationalId} onChange={(e) => setNationalId(e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
            </div>
            <div>
              <label className="mb-2 block text-sm text-white/80">تاریخ تولد</label>
              <input value={birthDate} onChange={(e) => setBirthDate(e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="۱۳۷۵/۰۵/۱۲" />
            </div>
            <div className="hidden sm:block" />

            <ImageField label="تصویر کارت ملی" aspect="wide" value={cardImage} onChange={setCardImage} />
            <ImageField label="عکس سلفی با کارت ملی (اختیاری)" aspect="wide" value={selfieImage} onChange={setSelfieImage} />

            <div className="sm:col-span-2">
              {error && <p className="mb-3 text-sm text-rose-400">{error}</p>}
              <button
                type="submit"
                disabled={submitting}
                className="h-12 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-10 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
              >
                {submitting ? "در حال ارسال..." : "ارسال برای بررسی"}
              </button>
            </div>
          </form>
        </Panel>
      )}
    </div>
  );
}
