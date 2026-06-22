"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { BankCard, BankCardStatus } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { PageTitle, Panel } from "@/components/account/Panel";
import ImageField from "@/components/admin/ImageField";
import CardGuideImage from "@/components/account/CardGuideImage";

const statusBadge: Record<BankCardStatus, { label: string; cls: string }> = {
  Pending: { label: "در انتظار تأیید", cls: "bg-amber-500/15 text-amber-300" },
  Approved: { label: "تأیید شده", cls: "bg-emerald-500/15 text-emerald-400" },
  Rejected: { label: "رد شده", cls: "bg-rose-500/15 text-rose-400" },
};

function formatCard(raw: string): string {
  const digits = (raw || "").replace(/\D/g, "").slice(0, 16);
  return digits.replace(/(.{4})/g, "$1-").replace(/-$/, "").replace(/\d/g, (d) => "۰۱۲۳۴۵۶۷۸۹"[Number(d)]);
}

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3e3af2] placeholder:text-white/35";

export default function CardsPage() {
  const { user } = useAuth();
  const [cards, setCards] = useState<BankCard[]>([]);
  const [loading, setLoading] = useState(true);
  const [note, setNote] = useState<{ ok: boolean; text: string } | null>(null);

  // add-card modal — step 0 = read the verification guide, step 1 = the form (card number + name + photo).
  const [open, setOpen] = useState(false);
  const [step, setStep] = useState<0 | 1>(0);
  const [number, setNumber] = useState("");
  const [holder, setHolder] = useState("");
  const [cardImage, setCardImage] = useState("");
  const [adding, setAdding] = useState(false);
  const [modalErr, setModalErr] = useState("");

  async function load() {
    if (!user) return;
    try {
      setCards(await api.cards.forUser(user.id));
    } catch {
      // keep current
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => {
    load();
  }, [user]);

  const digits = number.replace(/\D/g, "");

  function openModal() {
    setStep(0);
    setNumber("");
    setHolder(user?.name || "");
    setCardImage("");
    setModalErr("");
    setOpen(true);
  }

  async function submit() {
    if (digits.length !== 16) {
      setModalErr("شماره کارت باید ۱۶ رقم باشد.");
      return;
    }
    if (!holder.trim()) {
      setModalErr("نام روی کارت را وارد کنید.");
      return;
    }
    if (!cardImage) {
      setModalErr("تصویر کارت بانکی را بارگذاری کنید.");
      return;
    }
    setAdding(true);
    setModalErr("");
    try {
      await api.cards.add({ cardNumber: digits, holderName: holder.trim(), cardImage });
      setOpen(false);
      await load();
      setNote({ ok: true, text: "کارت ثبت شد و برای تأیید به پشتیبانی ارسال شد." });
    } catch (e) {
      setModalErr(e instanceof Error ? e.message : "ثبت کارت ناموفق بود.");
    } finally {
      setAdding(false);
    }
  }

  return (
    <div>
      <PageTitle title="کارت‌های من" desc="کارت‌های بانکی خود را ثبت کنید تا بتوانید پرداخت و واریز انجام دهید." />

      <Panel>
        <div className="mb-4 flex items-center justify-between gap-3">
          <h2 className="text-lg font-bold text-white">کارت‌های ثبت‌شده</h2>
          <button
            onClick={openModal}
            className="h-11 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 text-sm font-bold text-white transition hover:brightness-110"
          >
            ثبت کارت جدید
          </button>
        </div>

        {note && <p className={`mb-4 text-sm ${note.ok ? "text-emerald-400" : "text-rose-400"}`}>{note.text}</p>}

        <p className="mb-4 text-xs leading-6 text-white/40">
          پس از ثبت کارت، امکان حذف آن توسط شما وجود ندارد؛ در صورت نیاز به حذف یا تغییر کارت با پشتیبانی در تماس باشید.
        </p>


        {loading ? (
          <div className="grid h-24 place-items-center">
            <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-white/20 border-t-[#e60053]" />
          </div>
        ) : cards.length === 0 ? (
          <p className="py-8 text-center text-sm text-white/45">هنوز کارتی ثبت نکرده‌اید.</p>
        ) : (
          <ul className="space-y-3">
            {cards.map((c) => (
              <li key={c.id} className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-white/8 bg-white/[0.03] px-4 py-3">
                <div className="min-w-0">
                  <p className="font-mono text-sm font-bold text-white" dir="ltr">{formatCard(c.cardNumber)}</p>
                  <p className="mt-1 text-xs text-white/45">
                    {c.holderName}
                    {c.bank ? ` · ${c.bank}` : ""}
                    {c.status === "Rejected" && c.note ? ` · ${c.note}` : ""}
                  </p>
                </div>
                <div className="flex shrink-0 items-center gap-3">
                  <span className={`rounded-lg px-2.5 py-1 text-xs font-bold ${statusBadge[c.status].cls}`}>{statusBadge[c.status].label}</span>
                </div>
              </li>
            ))}
          </ul>
        )}
      </Panel>

      {open && (
        <div className="fixed inset-0 z-50 grid place-items-center p-4">
          <div onClick={() => !adding && setOpen(false)} className="absolute inset-0 bg-black/70 backdrop-blur-sm" />
          <div className="relative max-h-[88vh] w-full max-w-md overflow-y-auto rounded-2xl border border-white/10 bg-[#16161f] p-6 shadow-2xl">
            <div className="mb-4 flex items-center justify-between">
              <h3 className="text-lg font-bold text-white">افزودن کارت بانکی</h3>
              <button onClick={() => !adding && setOpen(false)} className="text-white/50 transition hover:text-white">✕</button>
            </div>

            {step === 0 ? (
              <>
                {/* level-1 verification guide: a sample card image + the rules the buyer must follow when
                    they send a photo of their physical card. */}
                <CardGuideImage />
                <div className="space-y-3 text-sm leading-7 text-white/75">
                  <p className="text-base font-bold text-white">👤 احراز هویت سطح یک</p>
                  <p>💠 تمامی کاربران در صورت پرداخت با ریال (کارت به کارت) باید از نسخه‌ی فیزیکی کارت بانکی خود طبق شرایط زیر عکس ارسال کنند:</p>
                  <p>🔰 تمام قسمت‌های کارت باید قابل رؤیت باشد. در صورت تمایل می‌توانید بعد از گرفتن عکس و هنگام ارسال روی CVV2 و تاریخ انقضا خط بکشید، اما بقیه‌ی قسمت‌های کارت از جمله (شماره کارت، نام و نام خانوادگی، شماره شبا و...) باید مشخص باشد؛ مثل نمونه‌ای که در تصویر می‌بینید‼️</p>
                  <p>⚠️ اگر قبلاً خرید کرده‌اید و برای خرید مجدد با همان کارت قبلی پرداخت می‌کنید، نیازی به ارسال مجدد عکس نیست؛ در صورتی که با کارت دیگری پرداخت می‌کنید، عکس کارت جدید خود را ارسال کنید ❤️</p>
                  <p>💢 ما برای حریم شخصی و امنیت شما ارزش بالایی قائل هستیم و قصد ایجاد نگرانی نداریم؛ اما به‌خاطر مشکلات پیش‌آمده، برای حفظ امنیت سرویس‌ها و جلوگیری از مشکلات، این کار بدون هیچ استثنایی انجام می‌شود ✅</p>
                </div>
                <button
                  onClick={() => setStep(1)}
                  className="mt-6 h-12 w-full rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-sm font-bold text-white transition hover:brightness-110"
                >
                  مطالعه کردم، ادامه می‌دهم
                </button>
              </>
            ) : (
              <>
                <div className="mb-5 rounded-xl border border-amber-500/25 bg-amber-500/[0.07] px-4 py-3 text-center text-sm text-amber-200/90">
                  کارت بانکی باید به نام صاحب حساب کاربری باشد.
                </div>

                <label className="mb-2 block text-sm font-bold text-white/85">شماره کارت</label>
                <input
                  value={number}
                  onChange={(e) => setNumber(e.target.value)}
                  inputMode="numeric"
                  dir="ltr"
                  placeholder="6037-xxxx-xxxx-xxxx"
                  className={`${inputCls} mb-4 text-left`}
                  autoFocus
                />

                <label className="mb-2 block text-sm font-bold text-white/85">نام روی کارت</label>
                <input value={holder} onChange={(e) => setHolder(e.target.value)} placeholder="نام و نام خانوادگی صاحب کارت" className={`${inputCls} mb-4`} />

                <ImageField label="تصویر کارت بانکی" aspect="wide" value={cardImage} onChange={setCardImage} uploader={api.cards.upload} srcFor={api.cards.imageSrc} />

                {modalErr && <p className="mt-3 text-sm text-rose-400">{modalErr}</p>}
                <div className="mt-5 flex gap-3">
                  <button
                    onClick={submit}
                    disabled={adding || digits.length !== 16 || !holder.trim() || !cardImage}
                    className="h-12 flex-1 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
                  >
                    {adding ? "در حال ثبت..." : "ثبت کارت"}
                  </button>
                  <button onClick={() => setStep(0)} disabled={adding} className="h-12 rounded-xl border border-white/10 px-6 text-sm font-bold text-white/80 transition hover:bg-white/5 disabled:opacity-60">
                    بازگشت
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
