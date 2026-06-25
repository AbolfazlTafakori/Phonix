"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useCart, clearCart, removeFromCart } from "@/lib/cart";
import { formatToman } from "@/lib/format";
import type { PaymentMethod, BankCard, DiscountResult } from "@/lib/types";
import { CardToCardForm, emptyCardToCard, isCardToCardComplete, type CardToCardValue } from "@/components/account/CardToCardForm";

export default function CheckoutPage() {
  const { user, ready } = useAuth();
  const { items, total, ready: cartReady } = useCart();
  const [methods, setMethods] = useState<PaymentMethod[]>([]);
  const [methodId, setMethodId] = useState<number | null>(null);
  const [cards, setCards] = useState<BankCard[]>([]);
  const [pay, setPay] = useState<CardToCardValue>(emptyCardToCard);
  const [wallet, setWallet] = useState<number | null>(null);
  const [useWallet, setUseWallet] = useState(false);
  const [emailVerified, setEmailVerified] = useState(true);
  const [resending, setResending] = useState(false);
  const [resent, setResent] = useState(false);
  const [placing, setPlacing] = useState(false);
  const [error, setError] = useState("");
  const [doneCode, setDoneCode] = useState("");
  const [donePaid, setDonePaid] = useState(false);
  // identity-level gate: the user's level + each product's required level (productId → requiredLevel).
  const [userLevel, setUserLevel] = useState(0);
  const [levelMap, setLevelMap] = useState<Record<number, number>>({});
  const [levelModal, setLevelModal] = useState(false);

  const [codeInput, setCodeInput] = useState("");
  const [discount, setDiscount] = useState<DiscountResult | null>(null);
  const [applyingCode, setApplyingCode] = useState(false);
  const [codeError, setCodeError] = useState("");

  const payable = discount?.valid ? discount.finalTotal : total;
  const walletBalance = wallet ?? 0;
  const walletUse = useWallet ? Math.min(walletBalance, payable) : 0;
  const remainder = payable - walletUse;
  const needsMethod = remainder > 0;
  const selectedMethod = methods.find((m) => m.id === methodId);
  const feePercent = needsMethod ? selectedMethod?.feePercent ?? 0 : 0;
  const fee = Math.round((remainder * feePercent) / 100);
  const finalPayable = remainder + fee;

  // cart lines whose product needs a higher identity level than the user currently has.
  const overLevelItems = items.filter((i) => (levelMap[i.productId] ?? 1) > userLevel);

  const patchPay = (p: Partial<CardToCardValue>) => setPay((cur) => ({ ...cur, ...p }));

  useEffect(() => {
    (async () => {
      try {
        const [m, me, prods] = await Promise.all([
          api.paymentMethods.list(),
          api.account.me().catch(() => null),
          api.products.list().catch(() => []),
        ]);
        setMethods(m.filter((x) => x.isActive));
        setLevelMap(Object.fromEntries(prods.map((p) => [p.id, p.requiredLevel])));
        if (me) {
          setWallet(me.wallet);
          setEmailVerified(me.emailVerified);
          setUserLevel(me.verificationLevel);
          const myCards = await api.cards.forUser(me.id).catch(() => [] as BankCard[]);
          const approved = myCards.filter((c) => c.status === "Approved");
          setCards(approved);
          setPay((cur) => (cur.cardId === null && approved[0] ? { ...cur, cardId: approved[0].id } : cur));
        }
        // no auto-selection of a method: when a remainder is due the buyer consciously picks one.
      } catch {
        // ignore
      }
    })();
  }, []);

  // once the user removes every over-level item, dismiss the upgrade modal automatically.
  useEffect(() => {
    if (levelModal && overLevelItems.length === 0) setLevelModal(false);
  }, [levelModal, overLevelItems.length]);

  async function applyCode() {
    const code = codeInput.trim();
    if (!code) return;
    setApplyingCode(true);
    setCodeError("");
    try {
      const result = await api.discounts.validate(code, total);
      if (result.valid) {
        setDiscount(result);
      } else {
        setDiscount(null);
        setCodeError(result.message ?? "کد تخفیف نامعتبر است.");
      }
    } catch {
      setCodeError("خطا در بررسی کد تخفیف");
    } finally {
      setApplyingCode(false);
    }
  }

  function removeCode() {
    setDiscount(null);
    setCodeInput("");
    setCodeError("");
  }

  async function resendVerification() {
    setResending(true);
    try {
      await api.auth.resendVerification();
      setResent(true);
    } finally {
      setResending(false);
    }
  }

  async function placeOrder() {
    if (!user || items.length === 0) return;
    // identity-level gate: if any cart item needs a higher level, show the upgrade/remove modal.
    if (overLevelItems.length > 0) {
      setLevelModal(true);
      return;
    }
    // a remainder is due → the buyer must pick a method and complete the card-to-card payment (card,
    // tracking, date, receipt) before the order can be filed; staff then verify and approve it.
    if (needsMethod) {
      if (methodId === null) {
        setError("یک روش پرداخت برای مبلغ باقیمانده انتخاب کنید.");
        return;
      }
      if (!isCardToCardComplete(pay)) {
        setError("اطلاعات پرداخت باقیمانده (کارت، شماره پیگیری، تاریخ و فیش) را کامل کنید.");
        return;
      }
    }
    setPlacing(true);
    setError("");
    try {
      const methodLabel = selectedMethod?.title ?? "نامشخص";
      const paymentMethod = remainder === 0 ? "کیف پول" : walletUse > 0 ? `کیف پول + ${methodLabel}` : methodLabel;
      const order = await api.orders.place({
        items: items.map((i) => ({ productId: i.productId, quantity: i.quantity, planId: i.planId ?? null })),
        paymentMethod,
        fromWallet: useWallet,
        discountCode: discount?.valid ? codeInput.trim() : null,
        paymentMethodId: needsMethod ? methodId : null,
        cardId: needsMethod ? pay.cardId : null,
        receiptUrl: needsMethod ? pay.receiptUrl : null,
        trackingNumber: needsMethod ? pay.tracking.trim() : null,
        paymentDate: needsMethod ? pay.payDate.trim() : null,
        description: needsMethod ? pay.desc.trim() || null : null,
      });
      // wallet covered the whole order → already paid; otherwise the payment is now pending review.
      setDonePaid(order.status === "Preparing");
      clearCart();
      setDoneCode(order.code);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در ثبت سفارش");
      setPlacing(false);
    }
  }

  if (doneCode) {
    return (
      <div className="mx-auto max-w-[640px] px-5 py-20 text-center">
        <div className="mx-auto mb-4 grid h-16 w-16 place-items-center rounded-full bg-emerald-500/15 text-3xl text-emerald-400">✓</div>
        <h1 className="text-2xl font-bold text-white">سفارش شما ثبت شد</h1>
        <p className="mt-2 text-sm leading-7 text-white/70">
          شماره سفارش: <span className="font-mono text-white">{doneCode}</span>
          <br />
          {donePaid
            ? "مبلغ از کیف پول شما کسر شد و سفارش به مرحله‌ی آماده‌سازی رفت."
            : "سفارش شما ثبت شد و رسید پرداخت شما در انتظار تأیید پشتیبانی است؛ پس از تأیید، سفارش به مرحله‌ی آماده‌سازی می‌رود."}
        </p>
        <Link href="/account/orders" className="mt-6 inline-block rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-8 py-3 text-sm font-bold text-white transition hover:brightness-110">
          مشاهده سفارش‌های من
        </Link>
      </div>
    );
  }

  if (ready && !user) {
    return (
      <div className="mx-auto max-w-[640px] px-5 py-20 text-center">
        <h1 className="text-2xl font-bold text-white">ابتدا وارد شوید</h1>
        <p className="mt-2 text-sm text-white/70">برای ثبت سفارش باید وارد حساب کاربری خود شوید.</p>
        <Link href="/login" className="mt-6 inline-block rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-8 py-3 text-sm font-bold text-white transition hover:brightness-110">
          ورود / ثبت‌نام
        </Link>
      </div>
    );
  }

  if (cartReady && items.length === 0) {
    return (
      <div className="mx-auto max-w-[640px] px-5 py-20 text-center">
        <h1 className="text-2xl font-bold text-white">سبد خرید خالی است</h1>
        <Link href="/products" className="mt-6 inline-block rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-8 py-3 text-sm font-bold text-white transition hover:brightness-110">
          مشاهده محصولات
        </Link>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-[900px] px-5 pb-20 pt-10">
      <h1 className="mb-6 text-2xl font-bold text-white">پرداخت و ثبت سفارش</h1>

      <div className="grid gap-6 lg:grid-cols-[1fr_320px]">
        <div className="space-y-6">
          <div className="rounded-2xl border border-white/8 bg-[#15151f]/80 p-5">
            <h3 className="mb-4 text-lg font-bold text-white">اقلام سفارش</h3>
            <div className="space-y-3">
              {items.map((i) => (
                <div key={`${i.productId}:${i.planId ?? ""}`} className="flex items-center gap-3">
                  <img src={i.image} alt={i.name} className="h-11 w-11 rounded-lg object-cover" />
                  <span className="flex-1 text-sm text-white/85">
                    {i.name} × {i.quantity}
                    {i.plan && <span className="text-white/45"> · {i.plan}</span>}
                  </span>
                  <span className="text-sm text-white/70">{formatToman(i.price * i.quantity)}</span>
                </div>
              ))}
            </div>
          </div>

          <div className="rounded-2xl border border-white/8 bg-[#15151f]/80 p-5">
            <h3 className="mb-4 text-lg font-bold text-white">روش پرداخت</h3>

            {overLevelItems.length > 0 ? (
              /* identity-level gate: hide every payment destination (card number, wallet
                 address, gateway) until the account reaches the level the cart requires. */
              <div className="space-y-4">
                <div className="rounded-xl border border-amber-500/30 bg-amber-500/[0.08] p-4">
                  <p className="font-bold text-amber-300">نیاز به ارتقای سطح حساب</p>
                  <p className="mt-1 text-sm leading-7 text-amber-100/80">
                    {overLevelItems.length === 1 ? (
                      <>
                        محصول <span className="font-bold text-amber-200">«{overLevelItems[0].name}»</span> برای سطح فعلی حساب شما قابل خرید نیست.
                      </>
                    ) : (
                      "محصول‌های زیر برای سطح فعلی حساب شما قابل خرید نیستند."
                    )}{" "}
                    تا زمانی که سطح حساب خود را ارتقا ندهید، اطلاعات پرداخت (شماره کارت، آدرس کیف پول و درگاه) نمایش داده نمی‌شود. {overLevelItems.length === 1 ? "این محصول را" : "این محصول‌ها را"} از سبد حذف کنید یا سطح حساب خود را ارتقا دهید.
                  </p>
                </div>
                <ul className="space-y-2">
                  {overLevelItems.map((i) => (
                    <li key={`${i.productId}:${i.planId ?? ""}`} className="flex items-center justify-between gap-3 rounded-xl border border-white/8 bg-white/[0.03] px-4 py-2.5">
                      <div className="flex min-w-0 items-center gap-3">
                        <img src={i.image} alt={i.name} className="h-9 w-9 rounded-lg object-cover" />
                        <span className="truncate text-sm text-white/85">{i.name}</span>
                      </div>
                      <button
                        onClick={() => removeFromCart(i.productId, i.planId ?? null)}
                        className="shrink-0 rounded-lg border border-rose-500/40 px-3 py-1.5 text-xs font-bold text-rose-400 transition hover:bg-rose-500/10"
                      >
                        حذف از سبد
                      </button>
                    </li>
                  ))}
                </ul>
                <Link href="/account/kyc" className="grid h-11 place-items-center rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-sm font-bold text-white transition hover:brightness-110">
                  ارتقای سطح حساب
                </Link>
              </div>
            ) : (
              <>
            {wallet !== null && walletBalance > 0 && (
              <button
                type="button"
                onClick={() => setUseWallet((v) => !v)}
                className={`mb-3 flex w-full items-center gap-3 rounded-xl border px-4 py-3 text-right transition ${useWallet ? "border-[#e60053]/50 bg-[#e60053]/10" : "border-white/10 hover:bg-white/5"}`}
              >
                <span className={`relative h-6 w-11 shrink-0 rounded-full transition ${useWallet ? "bg-[#e60053]" : "bg-white/15"}`}>
                  <span className={`absolute top-0.5 h-5 w-5 rounded-full bg-white transition-all ${useWallet ? "right-0.5" : "right-[22px]"}`} />
                </span>
                <span className="flex-1">
                  <span className="block text-sm font-bold text-white">استفاده از موجودی کیف پول</span>
                  <span className="block text-xs text-white/45">موجودی: {formatToman(walletBalance)}</span>
                </span>
                {useWallet && walletUse > 0 && <span className="text-xs font-bold text-emerald-400">− {formatToman(walletUse)}</span>}
              </button>
            )}

            {needsMethod ? (
              <div className="space-y-4">
                <div className="space-y-2">
                  <p className="text-xs text-white/45">{walletUse > 0 ? "مبلغ باقیمانده را با یکی از روش‌های زیر پرداخت کنید:" : "روش پرداخت را انتخاب کنید:"}</p>
                  {methods.length === 0 ? (
                    <p className="text-sm text-white/45">روش پرداختی تعریف نشده است.</p>
                  ) : (
                    methods.map((m) => (
                      <label key={m.id} className={`flex cursor-pointer items-center gap-3 rounded-xl border px-4 py-3 transition ${methodId === m.id ? "border-[#e60053]/50 bg-[#e60053]/10" : "border-white/10 hover:bg-white/5"}`}>
                        <input type="radio" name="method" checked={methodId === m.id} onChange={() => setMethodId(m.id)} className="accent-[#e60053]" />
                        <div>
                          <p className="text-sm font-bold text-white">{m.title}</p>
                          {m.instructions && <p className="text-xs text-white/45">{m.instructions}</p>}
                        </div>
                      </label>
                    ))
                  )}
                </div>

                {selectedMethod && (
                  <CardToCardForm
                    destMethod={selectedMethod}
                    cards={cards}
                    amountSlot={
                      <div className="flex items-center justify-between rounded-xl border border-white/8 bg-white/[0.02] px-4 py-3 text-sm">
                        <span className="text-white/55">مبلغ قابل پرداخت</span>
                        <span className="font-bold text-emerald-400">{formatToman(finalPayable)}</span>
                      </div>
                    }
                    value={pay}
                    onChange={patchPay}
                    onError={setError}
                  />
                )}
              </div>
            ) : (
              <div className="rounded-xl border border-emerald-500/30 bg-emerald-500/10 px-4 py-3 text-sm text-emerald-300">
                کل مبلغ از کیف پول شما پرداخت می‌شود.
              </div>
            )}
              </>
            )}
          </div>
        </div>

        <div className="h-fit rounded-2xl border border-white/8 bg-[#15151f]/80 p-6">
          <div className="mb-4">
            <label className="mb-2 block text-sm font-bold text-white">کد تخفیف</label>
            {discount?.valid ? (
              <div className="flex items-center justify-between rounded-xl border border-emerald-500/40 bg-emerald-500/10 px-4 py-3">
                <span className="font-mono text-sm font-bold text-emerald-400" dir="ltr">{codeInput.trim().toUpperCase()}</span>
                <button onClick={removeCode} className="text-xs font-bold text-white/60 transition hover:text-rose-400">حذف</button>
              </div>
            ) : (
              <div className="flex gap-2">
                <input
                  value={codeInput}
                  onChange={(e) => setCodeInput(e.target.value)}
                  dir="ltr"
                  placeholder="مثلاً WELCOME10"
                  className="h-11 flex-1 rounded-xl border border-white/10 bg-[#0d0d15] px-3 text-left text-sm text-white outline-none transition focus:border-[#3e3af2] placeholder:text-white/35"
                />
                <button
                  onClick={applyCode}
                  disabled={applyingCode || !codeInput.trim()}
                  className="h-11 shrink-0 rounded-xl border border-white/15 px-4 text-sm font-bold text-white/85 transition hover:bg-white/5 disabled:opacity-50"
                >
                  {applyingCode ? "..." : "اعمال"}
                </button>
              </div>
            )}
            {codeError && <p className="mt-2 text-xs text-rose-400">{codeError}</p>}
          </div>

          <div className="space-y-2 border-t border-white/8 pt-3 text-sm">
            <div className="flex items-center justify-between text-white/70">
              <span>مبلغ کل</span>
              <span className="text-white">{formatToman(total)}</span>
            </div>
            {discount?.valid && (
              <div className="flex items-center justify-between text-emerald-400">
                <span>تخفیف</span>
                <span>− {formatToman(discount.amount)}</span>
              </div>
            )}
            {walletUse > 0 && (
              <div className="flex items-center justify-between text-emerald-400">
                <span>پرداخت از کیف پول</span>
                <span>− {formatToman(walletUse)}</span>
              </div>
            )}
            {fee > 0 && (
              <div className="flex items-center justify-between text-amber-300/90">
                <span>کارمزد/مالیات درگاه</span>
                <span>+ {formatToman(fee)}</span>
              </div>
            )}
            <div className="flex items-center justify-between border-t border-white/8 pt-2">
              <span className="font-bold text-white/85">مبلغ قابل پرداخت</span>
              <span className="text-lg font-bold text-emerald-400">{formatToman(finalPayable)}</span>
            </div>
          </div>
          {!emailVerified && (
            <div className="mt-4 rounded-xl border border-amber-500/30 bg-amber-500/[0.08] p-4 text-sm">
              <p className="font-bold text-amber-300">تأیید ایمیل لازم است</p>
              <p className="mt-1 leading-7 text-amber-100/80">برای ثبت سفارش ابتدا ایمیل خود را تأیید کنید. لینک تأیید به ایمیل شما ارسال شده است.</p>
              {resent ? (
                <p className="mt-2 text-emerald-400">ایمیل تأیید دوباره ارسال شد.</p>
              ) : (
                <button onClick={resendVerification} disabled={resending} className="mt-2 rounded-lg border border-amber-500/40 px-3 py-1.5 text-xs font-bold text-amber-300 transition hover:bg-amber-500/10 disabled:opacity-60">
                  {resending ? "در حال ارسال..." : "ارسال مجدد ایمیل تأیید"}
                </button>
              )}
            </div>
          )}
          {error && <p className="mt-3 text-sm text-rose-400">{error}</p>}
          <button
            onClick={placeOrder}
            disabled={placing || items.length === 0 || !emailVerified || (needsMethod && (methodId === null || !isCardToCardComplete(pay)))}
            className="mt-5 flex h-12 w-full items-center justify-center rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
          >
            {placing ? "در حال ثبت..." : "پرداخت و ثبت سفارش"}
          </button>
        </div>
      </div>

      {levelModal && (
        <div className="fixed inset-0 z-50 grid place-items-center p-4">
          <div onClick={() => setLevelModal(false)} className="absolute inset-0 bg-black/70 backdrop-blur-sm" />
          <div className="relative w-full max-w-md rounded-2xl border border-white/10 bg-[#16161f] p-6 shadow-2xl">
            <div className="mx-auto mb-4 grid h-12 w-12 place-items-center rounded-full bg-amber-500/15 text-2xl text-amber-300">!</div>
            <h3 className="text-center text-lg font-bold text-white">نیاز به ارتقای سطح حساب</h3>
            <p className="mt-2 text-center text-sm leading-7 text-white/60">
              این محصول(ها) برای سطح فعلی حساب شما در دسترس نیستند. می‌توانید آن‌ها را از سبد حذف کنید یا سطح حساب خود را ارتقا دهید:
            </p>
            <ul className="mt-4 space-y-2">
              {overLevelItems.map((i) => (
                <li key={`${i.productId}:${i.planId ?? ""}`} className="flex items-center justify-between gap-3 rounded-xl border border-white/8 bg-white/[0.03] px-4 py-2.5">
                  <div className="flex min-w-0 items-center gap-3">
                    <img src={i.image} alt={i.name} className="h-9 w-9 rounded-lg object-cover" />
                    <span className="truncate text-sm text-white/85">{i.name}</span>
                  </div>
                  <button
                    onClick={() => removeFromCart(i.productId, i.planId ?? null)}
                    className="shrink-0 rounded-lg border border-rose-500/40 px-3 py-1.5 text-xs font-bold text-rose-400 transition hover:bg-rose-500/10"
                  >
                    حذف از سبد
                  </button>
                </li>
              ))}
            </ul>
            <div className="mt-6 flex gap-3">
              <Link href="/account/kyc" className="grid h-11 flex-1 place-items-center rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-sm font-bold text-white transition hover:brightness-110">
                ارتقای سطح حساب
              </Link>
              <button onClick={() => setLevelModal(false)} className="h-11 rounded-xl border border-white/10 px-6 text-sm font-bold text-white/80 transition hover:bg-white/5">
                بستن
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
