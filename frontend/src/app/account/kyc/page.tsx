"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import type { KycRequest } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { PageTitle, Panel } from "@/components/account/Panel";
import ImageField from "@/components/admin/ImageField";

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3e3af2]";

const LEVELS = [
  { n: 0, title: "ثبت‌نام", desc: "حساب ساخته شد" },
  { n: 1, title: "کارت بانکی", desc: "امکان خرید محصولات پایه" },
  { n: 2, title: "کارت ملی", desc: "دسترسی کامل به همه محصولات" },
];

export default function KycPage() {
  const { user } = useAuth();
  const [level, setLevel] = useState<number | null>(null);
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
        const [me, k] = await Promise.all([api.account.me(), api.kyc.getForUser(user.id)]);
        setLevel(me.verificationLevel);
        setKyc(k);
        setFullName(k?.fullName || me.name || "");
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
      const k = await api.kyc.submit({ fullName: fullName.trim(), nationalId: nationalId.trim(), birthDate: birthDate.trim(), cardImage, selfieImage });
      setKyc(k);
    } catch (err) {
      setError(err instanceof Error ? err.message : "خطا در ارسال");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div>
      <PageTitle title="احراز هویت" desc="سطح حساب خود را ارتقا دهید تا به محصولات بیشتری دسترسی پیدا کنید." />

      {loading || level === null ? (
        <Panel>
          <div className="grid h-32 place-items-center">
            <span className="inline-block h-7 w-7 animate-spin rounded-full border-2 border-white/20 border-t-[#e60053]" />
          </div>
        </Panel>
      ) : (
        <>
          {/* level stepper */}
          <Panel className="mb-6">
            <div className="flex items-center justify-between gap-2">
              {LEVELS.map((l, i) => {
                const done = level >= l.n;
                const current = level === l.n;
                return (
                  <div key={l.n} className="flex flex-1 items-center">
                    <div className="flex flex-col items-center text-center">
                      <div className={`grid h-11 w-11 place-items-center rounded-full text-sm font-bold transition ${done ? "bg-gradient-to-br from-[#1733d6] to-[#3a64f2] text-white" : "border border-white/15 text-white/40"} ${current ? "ring-2 ring-[#3a64f2]/60 ring-offset-2 ring-offset-[#15151f]" : ""}`}>
                        {done ? "✓" : l.n}
                      </div>
                      <p className={`mt-2 text-xs font-bold ${done ? "text-white" : "text-white/45"}`}>سطح {l.n}</p>
                      <p className="text-[11px] text-white/35">{l.title}</p>
                    </div>
                    {i < LEVELS.length - 1 && <div className={`mx-1 h-0.5 flex-1 rounded ${level > l.n ? "bg-[#3a64f2]" : "bg-white/10"}`} />}
                  </div>
                );
              })}
            </div>
            <p className="mt-5 text-center text-sm text-white/55">
              سطح فعلی شما: <span className="font-bold text-white">سطح {level}</span> — {LEVELS[level]?.desc}
            </p>
          </Panel>

          {/* next step */}
          {level === 0 ? (
            <Panel>
              <div className="flex flex-col items-center gap-3 py-6 text-center">
                <div className="grid h-16 w-16 place-items-center rounded-full bg-[#3a64f2]/15 text-3xl text-[#6f93ff]">۱</div>
                <h2 className="text-lg font-bold text-white">ارتقا به سطح ۱ — ثبت کارت بانکی</h2>
                <p className="max-w-md text-sm leading-7 text-white/55">
                  برای خرید در سایت، ابتدا یک کارت بانکی به نام خودتان ثبت کنید. پس از تأیید توسط پشتیبانی، حساب شما به سطح ۱ ارتقا می‌یابد.
                </p>
                <Link href="/account/cards" className="mt-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 py-3 text-sm font-bold text-white transition hover:brightness-110">
                  ثبت کارت بانکی
                </Link>
              </div>
            </Panel>
          ) : level >= 2 ? (
            <Panel>
              <div className="flex flex-col items-center gap-3 py-8 text-center">
                <div className="grid h-16 w-16 place-items-center rounded-full bg-emerald-500/15 text-3xl text-emerald-400">✓</div>
                <h2 className="text-xl font-bold text-white">احراز هویت کامل (سطح ۲)</h2>
                <p className="text-sm text-white/60">حساب شما در بالاترین سطح است و به همه‌ی محصولات دسترسی دارید.</p>
              </div>
            </Panel>
          ) : kyc?.status === "Pending" ? (
            <Panel>
              <div className="mb-6 flex items-center gap-3 rounded-xl border border-amber-500/20 bg-amber-500/10 px-4 py-3 text-sm text-amber-300">
                <span>⏳</span>
                مدارک کارت ملی شما ارسال شد و در حال بررسی توسط تیم پشتیبانی است. پس از تأیید به سطح ۲ ارتقا می‌یابید.
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
              <h2 className="mb-1 text-lg font-bold text-white">ارتقا به سطح ۲ — احراز هویت با کارت ملی</h2>
              <p className="mb-5 text-sm text-white/45">برای دسترسی به همه‌ی محصولات، اطلاعات کارت ملی خود را ثبت کنید.</p>

              {kyc?.status === "Rejected" && (
                <div className="mb-6 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-300">
                  مدارک شما تأیید نشد{kyc.note ? `: ${kyc.note}` : "."} لطفاً اطلاعات را اصلاح و دوباره ارسال کنید.
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

                <ImageField label="تصویر کارت ملی" aspect="wide" value={cardImage} onChange={setCardImage} uploader={api.kyc.upload} srcFor={api.kyc.imageSrc} />
                <ImageField label="عکس سلفی با کارت ملی (اختیاری)" aspect="wide" value={selfieImage} onChange={setSelfieImage} uploader={api.kyc.upload} srcFor={api.kyc.imageSrc} />

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
        </>
      )}
    </div>
  );
}
