"use client";

import { useRef } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import JalaliDatePicker from "./JalaliDatePicker";
import type { PaymentMethod, BankCard } from "@/lib/types";

const inputCls =
  "h-12 w-full rounded-xl border border-[#E8DDD2] bg-white px-4 text-sm text-[#1F1A17] outline-none transition focus:border-[#FF7A2F] placeholder:text-[#8C8075]";

export type CardToCardValue = {
  cardId: number | null;
  tracking: string;
  payDate: string;
  receiptUrl: string;
  desc: string;
  uploading: boolean;
};

export const emptyCardToCard: CardToCardValue = { cardId: null, tracking: "", payDate: "", receiptUrl: "", desc: "", uploading: false };

// every required field is present and no upload is in flight.
export const isCardToCardComplete = (v: CardToCardValue) =>
  v.cardId !== null && v.tracking.trim() !== "" && v.payDate.trim() !== "" && v.receiptUrl !== "" && !v.uploading;

export function formatCard(raw: string): string {
  const digits = (raw || "").replace(/\D/g, "").slice(0, 16);
  return digits.replace(/(.{4})/g, "$1-").replace(/-$/, "").replace(/\d/g, (d) => "۰۱۲۳۴۵۶۷۸۹"[Number(d)]);
}

// Receipts are bank-transfer proofs → uploaded to protected storage (authenticated, owner-scoped), never
// to the public uploads folder. Returns the opaque id stored as receiptUrl.
const upload = (file: File): Promise<string> => api.transactions.uploadReceipt(file);

export function CardToCardForm({
  destMethod,
  cards,
  amountSlot,
  value,
  onChange,
  onError,
  noCardsHref = "/account/cards",
}: {
  destMethod?: PaymentMethod;
  cards: BankCard[];
  amountSlot?: React.ReactNode;
  value: CardToCardValue;
  onChange: (patch: Partial<CardToCardValue>) => void;
  onError?: (msg: string) => void;
  noCardsHref?: string;
}) {
  const fileRef = useRef<HTMLInputElement>(null);

  async function pick(file: File | undefined) {
    if (!file) return;
    onChange({ uploading: true });
    try {
      const url = await upload(file);
      onChange({ receiptUrl: url, uploading: false });
    } catch (e) {
      onChange({ uploading: false });
      onError?.(e instanceof Error ? e.message : "آپلود رسید ناموفق بود.");
    }
  }

  const isCrypto = destMethod?.type === "Crypto";

  return (
    <div className="space-y-4">
      {destMethod && (
        <div className="rounded-xl border border-[#EADFD4] bg-[#FFF8F2] p-4 text-sm">
          <p className="mb-3 text-xs font-bold" style={{ color: "var(--ac-muted)" }}>واریز به حساب زیر و سپس ثبت اطلاعات فیش:</p>
          <dl className="grid gap-2">
            {destMethod.holder && <Row label="نام صاحب حساب" value={destMethod.holder} />}
            {destMethod.sheba && <Row label="شماره شبا" value={destMethod.sheba} mono />}
            {destMethod.accountNumber && <Row label="شماره حساب" value={destMethod.accountNumber} mono />}
            {destMethod.value && <Row label={isCrypto ? "آدرس کیف پول" : "شماره کارت"} value={destMethod.value} mono />}
            {destMethod.network && <Row label={isCrypto ? "شبکه" : "نام بانک"} value={destMethod.network} />}
          </dl>
        </div>
      )}

      {amountSlot}

      <div>
        <label className="mb-2 block text-sm font-bold" style={{ color: "var(--ac-text)" }}>شماره کارت (مبدأ پرداخت) *</label>
        {cards.length === 0 ? (
          <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-700">
            کارت بانکی تأییدشده‌ای ندارید. ابتدا از بخش{" "}
            <Link href={noCardsHref} className="font-bold text-amber-800 underline">کارت‌های من</Link>{" "}
            کارت خود را ثبت و منتظر تأیید بمانید.
          </div>
        ) : (
          <select value={value.cardId ?? ""} onChange={(e) => onChange({ cardId: Number(e.target.value) })} className={inputCls} dir="ltr">
            <option value="" disabled className="bg-white text-[#8C8075]">انتخاب کارت</option>
            {cards.map((c) => (
              <option key={c.id} value={c.id} className="bg-white text-[#1F1A17]">
                {formatCard(c.cardNumber)}{c.bank ? ` — ${c.bank}` : ""}
              </option>
            ))}
          </select>
        )}
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <label className="mb-2 block text-sm font-bold" style={{ color: "var(--ac-text)" }}>شماره پیگیری *</label>
          <input value={value.tracking} onChange={(e) => onChange({ tracking: e.target.value })} dir="ltr" placeholder="کد پیگیری تراکنش" className={`${inputCls} text-left`} />
        </div>
        <div>
          <label className="mb-2 block text-sm font-bold" style={{ color: "var(--ac-text)" }}>تاریخ پرداخت *</label>
          <JalaliDatePicker value={value.payDate} onChange={(v) => onChange({ payDate: v })} />
        </div>
      </div>

      <div>
        <label className="mb-2 block text-sm font-bold" style={{ color: "var(--ac-text)" }}>بارگذاری عکس فیش بانکی *</label>
        <div className="flex flex-wrap items-center gap-3">
          <button
            type="button"
            onClick={() => fileRef.current?.click()}
            disabled={value.uploading}
            className="h-12 rounded-xl border border-[#E8DDD2] px-5 text-sm font-bold transition hover:bg-[#FFF7F1] disabled:opacity-60"
            style={{ color: "var(--ac-text)" }}
          >
            {value.uploading ? "در حال بارگذاری..." : value.receiptUrl ? "تغییر تصویر فیش" : "انتخاب تصویر فیش"}
          </button>
          {value.receiptUrl && (
            <a href={api.transactions.receiptSrc(value.receiptUrl)} target="_blank" rel="noreferrer" className="inline-flex items-center gap-2">
              <img src={api.transactions.receiptSrc(value.receiptUrl)} alt="فیش بانکی" className="h-12 w-12 rounded-lg border border-[#EADFD4] object-cover" />
              <span className="text-xs font-bold text-emerald-600">✓ بارگذاری شد</span>
            </a>
          )}
        </div>
        <input ref={fileRef} type="file" accept="image/*" className="hidden" onChange={(e) => pick(e.target.files?.[0])} />
      </div>

      <div>
        <label className="mb-2 block text-sm font-bold" style={{ color: "var(--ac-text)" }}>توضیحات</label>
        <textarea
          value={value.desc}
          onChange={(e) => onChange({ desc: e.target.value })}
          rows={2}
          placeholder="اختیاری"
          className="w-full rounded-xl border border-[#E8DDD2] bg-white px-4 py-3 text-sm text-[#1F1A17] outline-none transition focus:border-[#FF7A2F] placeholder:text-[#8C8075]"
        />
      </div>
    </div>
  );
}

function Row({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex items-start justify-between gap-3">
      <dt className="shrink-0" style={{ color: "var(--ac-muted)" }}>{label}</dt>
      <dd className={`min-w-0 break-all text-left ${mono ? "font-mono" : ""}`} style={{ color: "var(--ac-title)" }} dir={mono ? "ltr" : undefined}>{value}</dd>
    </div>
  );
}
