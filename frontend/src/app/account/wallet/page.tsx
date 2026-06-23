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
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3e3af2] placeholder:text-white/35";

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

  // deposit (واریز)
  const [tab, setTab] = useState<"toman" | "crypto">("toman");
  const [amount, setAmount] = useState("");
  const [pay, setPay] = useState<CardToCardValue>(emptyCardToCard);
  const [charging, setCharging] = useState(false);
  const [note, setNote] = useState<{ ok: boolean; text: string } | null>(null);

  // withdrawal (برداشت)
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
    // instant KYC propagation: re-check the level (and balance) when the tab regains focus, so an admin
    // lowering the user to level 0 immediately re-gates the wallet within the active session.
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
      <label className="mb-2 block text-sm font-bold text-white/85">مبلغ (تومان) *</label>
      <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="numeric" placeholder="مثلاً ۵۰۰٬۰۰۰" className={inputCls} />
      <div className="mt-2 flex flex-wrap gap-2">
        {QUICK.map((q) => (
          <button
            key={q.value}
            type="button"
            onClick={() => setAmount(String(q.value))}
            className="rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/65 transition hover:border-[#3e3af2] hover:text-white"
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
          {/* KYC gate: a level-0 account never sees destination card numbers or deposit details. */}
          <div className="rounded-xl border border-amber-500/30 bg-amber-500/[0.08] p-5">
            <p className="font-bold text-amber-300">برای واریز و برداشت ابتدا احراز هویت کنید</p>
            <p className="mt-1.5 text-sm leading-7 text-amber-100/80">
              حساب شما در سطح ۰ است. برای مشاهده‌ی اطلاعات واریز و امکان شارژ یا برداشت کیف پول، ابتدا کارت بانکی خود را ثبت و تأیید کنید تا به سطح ۱ ارتقا یابید.
            </p>
            <Link href="/account/kyc" className="mt-4 inline-block rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 py-3 text-sm font-bold text-white transition hover:brightness-110">
              رفتن به احراز هویت
            </Link>
          </div>
        </Panel>
      ) : (
        <>
      <Panel className="mb-6">
        <h2 className="mb-4 text-lg font-bold text-white">واریز و افزایش موجودی</h2>

        <div className="mb-5 grid grid-cols-2 gap-2 rounded-2xl border border-white/8 bg-[#0d0d15] p-1.5">
          <button
            onClick={() => setTab("toman")}
            className={`h-11 rounded-xl text-sm font-bold transition ${tab === "toman" ? "bg-[#3a64f2]/20 text-[#8fa9ff]" : "text-white/55 hover:text-white"}`}
          >
            واریز تومان
          </button>
          <button
            onClick={() => setTab("crypto")}
            className={`flex h-11 items-center justify-center gap-2 rounded-xl text-sm font-bold transition ${tab === "crypto" ? "bg-[#3a64f2]/20 text-[#8fa9ff]" : "text-white/55 hover:text-white"}`}
          >
            واریز رمزارز
            <span className="rounded-md bg-white/10 px-1.5 py-0.5 text-[10px] font-bold text-white/50">به‌زودی</span>
          </button>
        </div>

        {tab === "crypto" ? (
          <div className="rounded-xl border border-white/8 bg-white/[0.02] px-4 py-10 text-center text-sm text-white/55">
            واریز رمزارز به‌زودی فعال می‌شود.
          </div>
        ) : (
          <div className="space-y-4">
            <div className="grid gap-2">
              <div className="flex items-center justify-between rounded-xl border border-white/8 bg-white/[0.02] px-4 py-3 opacity-60">
                <span className="flex items-center gap-2 text-sm font-bold text-white/55">
                  <span className="grid h-5 w-5 place-items-center rounded-full border border-white/20" />
                  واریز آنلاین
                </span>
                <span className="rounded-md bg-white/10 px-2 py-0.5 text-[11px] font-bold text-white/45">به‌زودی</span>
              </div>
              <div className="flex items-center justify-between rounded-xl border border-[#e60053]/40 bg-[#e60053]/10 px-4 py-3">
                <span className="flex items-center gap-2 text-sm font-bold text-white">
                  <span className="grid h-5 w-5 place-items-center rounded-full border-[5px] border-[#e60053]" />
                  واریز آفلاین (کارت‌به‌کارت)
                </span>
                <span className="rounded-md bg-white/10 px-2 py-0.5 text-[11px] font-bold text-white/55">حداکثر ۱۰ دقیقه</span>
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
              className="h-12 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-10 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
            >
              {charging ? "در حال ثبت..." : "ثبت درخواست واریز"}
            </button>
            {note && <p className={`text-sm ${note.ok ? "text-emerald-400" : "text-rose-400"}`}>{note.text}</p>}
          </div>
        )}
      </Panel>

      <Panel className="mb-6">
        <h2 className="mb-1 text-lg font-bold text-white">برداشت وجه</h2>
        <p className="mb-4 text-sm text-white/45">
          مبلغ و شماره کارت/شبای مقصد را وارد کنید. مبلغ بلافاصله از موجودی شما کسر و درخواست برای تأیید ارسال می‌شود؛ پس از تأیید پشتیبانی، وجه واریز می‌گردد.
          {minWithdraw > 0 && <> حداقل مبلغ برداشت {formatToman(minWithdraw)} است.</>}
        </p>

        <label className="mb-2 block text-sm font-bold text-white/85">مبلغ (تومان)</label>
        <input value={wAmount} onChange={(e) => setWAmount(e.target.value)} inputMode="numeric" placeholder="مثلاً ۲۰۰٬۰۰۰" className={`${inputCls} mb-4`} />

        <label className="mb-2 block text-sm font-bold text-white/85">شماره کارت یا شبای مقصد</label>
        <input value={wDest} onChange={(e) => setWDest(e.target.value)} dir="ltr" placeholder="6037-xxxx-xxxx-xxxx" className={`${inputCls} mb-5 text-left`} />

        <button
          onClick={withdraw}
          disabled={withdrawing || wAmountValue <= 0 || !wDest.trim()}
          className="h-12 rounded-xl border border-white/15 bg-white/[0.04] px-8 text-sm font-bold text-white transition hover:bg-white/10 disabled:opacity-60"
        >
          {withdrawing ? "در حال ثبت..." : "ثبت درخواست برداشت"}
        </button>
        {wNote && <p className={`mt-3 text-sm ${wNote.ok ? "text-emerald-400" : "text-rose-400"}`}>{wNote.text}</p>}
      </Panel>
        </>
      )}

      <Panel>
        <h2 className="mb-4 text-lg font-bold text-white">تراکنش‌های اخیر</h2>
        {loading ? (
          <div className="grid h-24 place-items-center">
            <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-white/20 border-t-[#e60053]" />
          </div>
        ) : txs.length === 0 ? (
          <p className="py-8 text-center text-sm text-white/45">هنوز تراکنشی ندارید.</p>
        ) : (
          <ul className="divide-y divide-white/8">
            {txs.map((t) => (
              <li key={t.id} className="flex items-center justify-between gap-3 py-4">
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium text-white">{t.type}</p>
                  <p className="text-xs text-white/45">
                    {t.date} · {statusLabel[t.status]}
                    {t.method ? ` · ${t.method}` : ""}
                  </p>
                </div>
                <div className="flex shrink-0 items-center gap-3">
                  {t.status === "Pending" && (
                    <span className="rounded-lg bg-amber-500/15 px-2.5 py-1 text-xs font-bold text-amber-300">در انتظار تأیید</span>
                  )}
                  <span className={`text-sm font-bold ${t.amount >= 0 ? "text-emerald-400" : "text-rose-400"}`}>
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
