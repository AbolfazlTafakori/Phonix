"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import type { Transaction, TxStatus, PaymentMethod, BankCard } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { formatToman, parseNumber } from "@/lib/format";
import { PageTitle, Panel, StatCard } from "@/components/account/Panel";
import { CardToCardForm, emptyCardToCard, isCardToCardComplete, type CardToCardValue } from "@/components/account/CardToCardForm";

const statusLabel: Record<TxStatus, string> = { Pending: "در انتظار", Approved: "تایید شده", Rejected: "رد شده" };

const inputCls =
  "h-12 w-full rounded-xl border border-[#E8DDD2] bg-white px-4 text-sm text-[#1F1A17] outline-none transition focus:border-[#FF7A2F] placeholder:text-[#8C8075]";

const QUICK: { value: number; label: string }[] = [
  { value: 500_000, label: "۵۰۰ هزار" },
  { value: 1_000_000, label: "۱ میلیون" },
  { value: 5_000_000, label: "۵ میلیون" },
  { value: 25_000_000, label: "۲۵ میلیون" },
];

export default function WalletPage() {
  const { user } = useAuth();
  const [balance, setBalance] = useState(0);
  const [txs, setTxs] = useState<Transaction[]>([]);
  const [methods, setMethods] = useState<PaymentMethod[]>([]);
  const [cards, setCards] = useState<BankCard[]>([]);
  const [minWithdraw, setMinWithdraw] = useState(0);
  const [minCharge, setMinCharge] = useState(0);
  const [level, setLevel] = useState(0);
  const [loading, setLoading] = useState(true);

  const [tab, setTab] = useState<"toman" | "crypto">("toman");
  const [amount, setAmount] = useState("");
  const [pay, setPay] = useState<CardToCardValue>(emptyCardToCard);
  const [charging, setCharging] = useState(false);
  const [note, setNote] = useState<{ ok: boolean; text: string } | null>(null);

  const [wAmount, setWAmount] = useState("");
  const [wDest, setWDest] = useState("");
  const [withdrawing, setWithdrawing] = useState(false);
  const [wNote, setWNote] = useState<{ ok: boolean; text: string } | null>(null);

  const patchPay = (p: Partial<CardToCardValue>) => setPay((cur) => ({ ...cur, ...p }));

  async function load() {
    if (!user) return;
    try {
      const [u, mine, pm, myCards, settings] = await Promise.all([
        api.account.me(),
        api.account.transactions(),
        api.paymentMethods.list().catch(() => [] as PaymentMethod[]),
        api.cards.forUser(user.id).catch(() => [] as BankCard[]),
        api.pricing.getSettings().catch(() => null),
      ]);
      setBalance(u.wallet);
      setLevel(u.verificationLevel);
      setTxs(mine);
      setMethods(pm.filter((m) => m.isActive));
      const approved = myCards.filter((c) => c.status === "Approved");
      setCards(approved);
      setPay((cur) => (cur.cardId === null && approved[0] ? { ...cur, cardId: approved[0].id } : cur));
      if (settings) {
        setMinWithdraw(settings.minWithdraw);
        setMinCharge(settings.minWalletCharge);
      }
    } catch {
      // keep current values if the wallet can't be refreshed
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => {
    load();
    const sync = () => load();
    window.addEventListener("focus", sync);
    document.addEventListener("visibilitychange", sync);
    return () => {
      window.removeEventListener("focus", sync);
      document.removeEventListener("visibilitychange", sync);
    };
  }, [user]);

  const totals = useMemo(() => {
    const charged = txs.filter((t) => t.status === "Approved" && t.type === "شارژ کیف پول").reduce((s, t) => s + t.amount, 0);
    const referral = txs.filter((t) => t.status === "Approved" && t.type === "پورسانت").reduce((s, t) => s + t.amount, 0);
    return { charged, referral };
  }, [txs]);

  const amountValue = parseNumber(amount);
  const wAmountValue = parseNumber(wAmount);
  const destMethod = methods.find((m) => m.type === "Card") ?? methods[0];

  async function charge() {
    if (amountValue <= 0) return setNote({ ok: false, text: "مبلغ معتبر وارد کنید." });
    if (minCharge > 0 && amountValue < minCharge) return setNote({ ok: false, text: `حداقل مبلغ شارژ ${formatToman(minCharge)} است.` });
    if (pay.cardId === null) return setNote({ ok: false, text: "یک کارت بانکی ثبت‌شده انتخاب کنید." });
    if (!pay.tracking.trim()) return setNote({ ok: false, text: "شماره پیگیری را وارد کنید." });
    if (!pay.payDate.trim()) return setNote({ ok: false, text: "تاریخ پرداخت را وارد کنید." });
    if (!pay.receiptUrl) return setNote({ ok: false, text: "تصویر فیش بانکی را بارگذاری کنید." });
    setCharging(true);
    setNote(null);
    try {
      await api.transactions.create({
        amount: amountValue,
        cardId: pay.cardId,
        method: destMethod?.title,
        receiptUrl: pay.receiptUrl,
        trackingNumber: pay.tracking.trim(),
        paymentDate: pay.payDate.trim(),
        description: pay.desc.trim() || null,
      });
      setAmount("");
      setPay((cur) => ({ ...emptyCardToCard, cardId: cur.cardId }));
      await load();
      setNote({ ok: true, text: "درخواست واریز ثبت شد. پس از تأیید پشتیبانی، مبلغ به کیف پول شما افزوده می‌شود." });
    } catch (e) {
      setNote({ ok: false, text: e instanceof Error ? e.message : "ثبت درخواست ناموفق بود." });
    } finally {
      setCharging(false);
    }
  }

  async function withdraw() {
    if (wAmountValue <= 0) return setWNote({ ok: false, text: "مبلغ معتبر وارد کنید." });
    if (minWithdraw > 0 && wAmountValue < minWithdraw) return setWNote({ ok: false, text: `حداقل مبلغ برداشت ${formatToman(minWithdraw)} است.` });
    if (wAmountValue > balance) return setWNote({ ok: false, text: "موجودی کیف پول برای این برداشت کافی نیست." });
    if (!wDest.trim()) return setWNote({ ok: false, text: "شماره کارت یا شبای مقصد را وارد کنید." });
    setWithdrawing(true);
    setWNote(null);
    try {
      await api.transactions.withdraw({ amount: wAmountValue, destination: wDest.trim() });
      setWAmount("");
      setWDest("");
      await load();
      setWNote({ ok: true, text: "درخواست برداشت ثبت شد و مبلغ از موجودی شما کسر شد. پس از تأیید پشتیبانی، وجه به حساب شما واریز می‌شود." });
    } catch (e) {
      setWNote({ ok: false, text: e instanceof Error ? e.message : "ثبت درخواست برداشت ناموفق بود." });
    } finally {
      setWithdrawing(false);
    }
  }

  const amountSlot = (
    <div>
      <label className="mb-2 block text-sm font-bold" style={{ color: "var(--ac-text)" }}>مبلغ (تومان) *</label>
      <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="numeric" placeholder="مثلاً ۵۰۰٬۰۰۰" className={inputCls} />
      <div className="mt-2 flex flex-wrap gap-2">
        {QUICK.map((q) => (
          <button
            key={q.value}
            type="button"
            onClick={() => setAmount(String(q.value))}
            className="rounded-lg border border-[#EADFD4] px-3 py-1.5 text-xs font-bold transition hover:border-[#FF7A2F] hover:text-[#FF5A1F]"
            style={{ color: "var(--ac-text)" }}
          >
            {q.label}
          </button>
        ))}
      </div>
    </div>
  );

  return (
    <div>
      <PageTitle title="کیف پول" desc="موجودی و تراکنش‌های حساب خود را مدیریت کنید." />

      <div className="mb-6 grid gap-4 sm:grid-cols-3">
        <StatCard label="موجودی فعلی" value={formatToman(balance)} accent="#3a64f2" />
        <StatCard label="مجموع شارژ" value={formatToman(totals.charged)} accent="#22c55e" />
        <StatCard label="درآمد معرفی" value={formatToman(totals.referral)} accent="#e60053" />
      </div>

      {level === 0 ? (
        <Panel className="mb-6">
          <div className="rounded-xl border border-amber-200 bg-amber-50 p-5">
            <p className="font-bold text-amber-700">برای واریز و برداشت ابتدا احراز هویت کنید</p>
            <p className="mt-1.5 text-sm leading-7 text-amber-600">
              حساب شما در سطح ۰ است. برای مشاهده‌ی اطلاعات واریز و امکان شارژ یا برداشت کیف پول، ابتدا کارت بانکی خود را ثبت و تأیید کنید تا به سطح ۱ ارتقا یابید.
            </p>
            <Link href="/account/kyc" className="mt-4 inline-block rounded-xl px-6 py-3 text-sm font-bold text-white transition hover:brightness-110" style={{ background: "var(--ac-btn)" }}>
              رفتن به احراز هویت
            </Link>
          </div>
        </Panel>
      ) : (
        <>
          <Panel className="mb-6">
            <h2 className="mb-4 text-lg font-bold" style={{ color: "var(--ac-title)" }}>واریز و افزایش موجودی</h2>

            {/* Tab switcher */}
            <div className="mb-5 grid grid-cols-2 gap-2 rounded-2xl border border-[#EADFD4] bg-[#FFF8F2] p-1.5">
              <button
                onClick={() => setTab("toman")}
                className={`h-11 rounded-xl text-sm font-bold transition ${tab === "toman" ? "bg-white shadow-sm" : "hover:bg-white/50"}`}
                style={{ color: tab === "toman" ? "var(--ac-title)" : "var(--ac-muted)" }}
              >
                واریز تومان
              </button>
              <button
                onClick={() => setTab("crypto")}
                className={`flex h-11 items-center justify-center gap-2 rounded-xl text-sm font-bold transition ${tab === "crypto" ? "bg-white shadow-sm" : "hover:bg-white/50"}`}
                style={{ color: tab === "crypto" ? "var(--ac-title)" : "var(--ac-muted)" }}
              >
                واریز رمزارز
                <span className="rounded-md bg-[#EADFD4] px-1.5 py-0.5 text-[10px] font-bold" style={{ color: "var(--ac-muted)" }}>به‌زودی</span>
              </button>
            </div>

            {tab === "crypto" ? (
              <div className="rounded-xl border border-[#EADFD4] bg-[#FFF8F2] px-4 py-10 text-center text-sm" style={{ color: "var(--ac-muted)" }}>
                واریز رمزارز به‌زودی فعال می‌شود.
              </div>
            ) : (
              <div className="space-y-4">
                <div className="grid gap-2">
                  <div className="flex items-center justify-between rounded-xl border border-[#EADFD4] bg-[#F7F0E8] px-4 py-3 opacity-60">
                    <span className="flex items-center gap-2 text-sm font-bold" style={{ color: "var(--ac-muted)" }}>
                      <span className="grid h-5 w-5 place-items-center rounded-full border border-[#EADFD4]" />
                      واریز آنلاین
                    </span>
                    <span className="rounded-md bg-[#EADFD4] px-2 py-0.5 text-[11px] font-bold" style={{ color: "var(--ac-muted)" }}>به‌زودی</span>
                  </div>
                  <div className="flex items-center justify-between rounded-xl border border-[#FF6A2B]/40 bg-[#FFF1E8] px-4 py-3">
                    <span className="flex items-center gap-2 text-sm font-bold" style={{ color: "var(--ac-title)" }}>
                      <span className="grid h-5 w-5 place-items-center rounded-full border-[5px] border-[#FF6A2B]" />
                      واریز آفلاین (کارت‌به‌کارت)
                    </span>
                    <span className="rounded-md bg-[#EADFD4] px-2 py-0.5 text-[11px] font-bold" style={{ color: "var(--ac-muted)" }}>حداکثر ۱۰ دقیقه</span>
                  </div>
                </div>

                <CardToCardForm
                  destMethod={destMethod}
                  cards={cards}
                  amountSlot={amountSlot}
                  value={pay}
                  onChange={patchPay}
                  onError={(m) => setNote({ ok: false, text: m })}
                />

                <button
                  onClick={charge}
                  disabled={charging || amountValue <= 0 || !isCardToCardComplete(pay)}
                  className="h-12 rounded-xl px-10 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
                  style={{ background: "var(--ac-btn)" }}
                >
                  {charging ? "در حال ثبت..." : "ثبت درخواست واریز"}
                </button>
                {note && <p className={`text-sm ${note.ok ? "text-emerald-600" : "text-rose-600"}`}>{note.text}</p>}
              </div>
            )}
          </Panel>

          <Panel className="mb-6">
            <h2 className="mb-1 text-lg font-bold" style={{ color: "var(--ac-title)" }}>برداشت وجه</h2>
            <p className="mb-4 text-sm" style={{ color: "var(--ac-muted)" }}>
              مبلغ و شماره کارت/شبای مقصد را وارد کنید. مبلغ بلافاصله از موجودی شما کسر و درخواست برای تأیید ارسال می‌شود؛ پس از تأیید پشتیبانی، وجه واریز می‌گردد.
              {minWithdraw > 0 && <> حداقل مبلغ برداشت {formatToman(minWithdraw)} است.</>}
            </p>

            <label className="mb-2 block text-sm font-bold" style={{ color: "var(--ac-text)" }}>مبلغ (تومان)</label>
            <input value={wAmount} onChange={(e) => setWAmount(e.target.value)} inputMode="numeric" placeholder="مثلاً ۲۰۰٬۰۰۰" className={`${inputCls} mb-4`} />

            <label className="mb-2 block text-sm font-bold" style={{ color: "var(--ac-text)" }}>شماره کارت یا شبای مقصد</label>
            <input value={wDest} onChange={(e) => setWDest(e.target.value)} dir="ltr" placeholder="6037-xxxx-xxxx-xxxx" className={`${inputCls} mb-5 text-left`} />

            <button
              onClick={withdraw}
              disabled={withdrawing || wAmountValue <= 0 || !wDest.trim()}
              className="h-12 rounded-xl border border-[#EADFD4] px-8 text-sm font-bold transition hover:bg-[#FFF7F1] disabled:opacity-60"
              style={{ color: "var(--ac-text)" }}
            >
              {withdrawing ? "در حال ثبت..." : "ثبت درخواست برداشت"}
            </button>
            {wNote && <p className={`mt-3 text-sm ${wNote.ok ? "text-emerald-600" : "text-rose-600"}`}>{wNote.text}</p>}
          </Panel>
        </>
      )}

      <Panel>
        <h2 className="mb-4 text-lg font-bold" style={{ color: "var(--ac-title)" }}>تراکنش‌های اخیر</h2>
        {loading ? (
          <div className="grid h-24 place-items-center">
            <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-[rgba(166,102,45,0.2)] border-t-[#FF5A1F]" />
          </div>
        ) : txs.length === 0 ? (
          <p className="py-8 text-center text-sm" style={{ color: "var(--ac-muted)" }}>هنوز تراکنشی ندارید.</p>
        ) : (
          <ul className="divide-y divide-[#EADFD4]">
            {txs.map((t) => (
              <li key={t.id} className="flex items-center justify-between gap-3 py-4">
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium" style={{ color: "var(--ac-title)" }}>{t.type}</p>
                  <p className="text-xs" style={{ color: "var(--ac-muted)" }}>
                    {t.date} · {statusLabel[t.status]}
                    {t.method ? ` · ${t.method}` : ""}
                  </p>
                </div>
                <div className="flex shrink-0 items-center gap-3">
                  {t.status === "Pending" && (
                    <span className="rounded-lg bg-amber-100 px-2.5 py-1 text-xs font-bold text-amber-700">در انتظار تأیید</span>
                  )}
                  <span className={`text-sm font-bold ${t.amount >= 0 ? "text-emerald-600" : "text-rose-600"}`}>
                    {t.amount >= 0 ? "+" : "−"}
                    {formatToman(Math.abs(t.amount))}
                  </span>
                </div>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </div>
  );
}
