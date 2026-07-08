"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatToman, toFa } from "@/lib/format";
import { orderStatusLabel } from "@/lib/labels";
import { PageTitle, Panel } from "@/components/account/Panel";
import { StatusBadge } from "@/components/admin/ui";
import type { Order } from "@/lib/types";

const cancellable = (status: Order["status"]) => status === "PendingApproval" || status === "Preparing";

export default function OrdersPage() {
  const { user } = useAuth();
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [penalty, setPenalty] = useState(0);
  const [cancelling, setCancelling] = useState<number | null>(null);
  const [confirmOrder, setConfirmOrder] = useState<Order | null>(null);

  useEffect(() => {
    if (!user) return;
    (async () => {
      try {
        const [list] = await Promise.all([
          api.orders.forUser(user.id),
          api.pricing.getSettings().then((s) => setPenalty(s.cancellationPenaltyPercent)).catch(() => {}),
        ]);
        setOrders(list);
        setError("");
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری سفارش‌ها. لطفاً بعداً دوباره تلاش کنید.");
      } finally {
        setLoading(false);
      }
    })();
  }, [user]);

  async function doCancel() {
    if (!confirmOrder) return;
    const id = confirmOrder.id;
    setCancelling(id);
    try {
      const updated = await api.orders.cancel(id);
      setOrders((prev) => prev.map((x) => (x.id === id ? updated : x)));
      setConfirmOrder(null);
    } catch (e) {
      alert(e instanceof Error ? e.message : "خطا در لغو سفارش");
    } finally {
      setCancelling(null);
    }
  }

  const collected = confirmOrder ? (confirmOrder.status === "Preparing" ? confirmOrder.total : confirmOrder.walletPaid) : 0;
  const penaltyAmount = Math.round((collected * penalty) / 100);
  const refundAmount = collected - penaltyAmount;

  return (
    <div>
      <PageTitle title="سفارش‌های من" desc="وضعیت سفارش‌ها و تاریخچه‌ی خرید شما." />

      {loading ? (
        <Panel>
          <div className="grid h-24 place-items-center">
            <span className="inline-block h-7 w-7 animate-spin rounded-full border-2 border-[rgba(166,102,45,0.2)] border-t-[#FF5A1F]" />
          </div>
        </Panel>
      ) : error ? (
        <Panel>
          <div className="py-8 text-center">
            <p className="text-rose-600">{error}</p>
            <button onClick={() => location.reload()} className="mt-4 inline-block rounded-xl border border-[#EADFD4] px-6 py-2.5 text-sm font-bold transition hover:bg-[#FFF7F1]" style={{ color: "var(--ac-text)" }}>
              تلاش مجدد
            </button>
          </div>
        </Panel>
      ) : orders.length === 0 ? (
        <Panel>
          <div className="py-8 text-center">
            <p style={{ color: "var(--ac-muted)" }}>هنوز سفارشی ثبت نکرده‌اید.</p>
            <Link href="/products" className="mt-4 inline-block rounded-xl px-6 py-2.5 text-sm font-bold text-white transition hover:brightness-110" style={{ background: "var(--ac-btn)" }}>
              شروع خرید
            </Link>
          </div>
        </Panel>
      ) : (
        <div className="space-y-4">
          {orders.map((o) => (
            <Panel key={o.id}>
              <div className="flex flex-wrap items-center justify-between gap-3 border-b pb-3" style={{ borderColor: "var(--ac-divider)" }}>
                <div>
                  <p className="font-mono text-sm" style={{ color: "var(--ac-text)" }}>{o.code}</p>
                  <p className="text-xs" style={{ color: "var(--ac-muted)" }}>{o.date} · {o.paymentMethod}</p>
                </div>
                <StatusBadge status={orderStatusLabel[o.status]} />
              </div>

              <div className="mt-3 space-y-2">
                {o.items.map((it) => (
                  <div key={`${it.productId}:${it.plan ?? ""}`} className="flex items-center gap-3">
                    <img src={it.image} alt={it.name} className="h-10 w-10 rounded-lg object-cover" />
                    <span className="flex-1 text-sm" style={{ color: "var(--ac-text)" }}>
                      {it.name} × {it.quantity}
                      {it.plan && <span style={{ color: "var(--ac-muted)" }}> · {it.plan}</span>}
                    </span>
                    <span className="text-sm" style={{ color: "var(--ac-muted)" }}>{formatToman(it.lineTotal)}</span>
                  </div>
                ))}
              </div>

              <div className="mt-3 flex items-center justify-between border-t pt-3" style={{ borderColor: "var(--ac-divider)" }}>
                <span className="text-sm" style={{ color: "var(--ac-muted)" }}>مبلغ کل</span>
                <span className="font-bold text-emerald-600">{formatToman(o.total)}</span>
              </div>

              {(() => {
                const deliveredUnits = (o.units ?? []).filter((u) => u.delivered && u.deliveryContent.trim());
                const multi = deliveredUnits.length > 1;
                if (deliveredUnits.length > 0) {
                  return (
                    <div className="mt-4 space-y-3">
                      {deliveredUnits.map((u) => (
                        <div key={u.id} className="rounded-xl border border-emerald-200 bg-emerald-50 p-4">
                          <div className="mb-2 flex items-center gap-2">
                            <span className="grid h-6 w-6 place-items-center rounded-full bg-emerald-100 text-sm text-emerald-600">✓</span>
                            <span className="text-sm font-bold text-emerald-700">
                              اطلاعات سرویس شما{multi ? ` — اکانت ${u.unitIndex}` : ""}
                            </span>
                            {u.deliveredAt && <span className="text-xs" style={{ color: "var(--ac-muted)" }}>· تحویل {u.deliveredAt}</span>}
                          </div>
                          <pre className="whitespace-pre-wrap break-words font-sans text-sm leading-7 text-emerald-800">{u.deliveryContent}</pre>
                        </div>
                      ))}
                    </div>
                  );
                }
                if (o.deliveryContent) {
                  return (
                    <div className="mt-4 rounded-xl border border-emerald-200 bg-emerald-50 p-4">
                      <div className="mb-2 flex items-center gap-2">
                        <span className="grid h-6 w-6 place-items-center rounded-full bg-emerald-100 text-sm text-emerald-600">✓</span>
                        <span className="text-sm font-bold text-emerald-700">اطلاعات سرویس شما</span>
                        {o.deliveredAt && <span className="text-xs" style={{ color: "var(--ac-muted)" }}>· تحویل {o.deliveredAt}</span>}
                      </div>
                      <pre className="whitespace-pre-wrap break-words font-sans text-sm leading-7 text-emerald-800">{o.deliveryContent}</pre>
                    </div>
                  );
                }
                return null;
              })()}

              <div className="mt-3 flex flex-wrap items-center justify-end gap-2">
                {o.status === "PendingApproval" && o.total - o.walletPaid > 0 && (
                  <span className="rounded-xl bg-amber-100 px-4 py-2 text-sm font-bold text-amber-700">
                    در انتظار تأیید پرداخت ({formatToman(o.total - o.walletPaid)})
                  </span>
                )}
                <a
                  href={`/invoice?id=${o.id}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="rounded-xl border border-[#EADFD4] px-4 py-2 text-sm font-bold transition hover:bg-[#FFF7F1]"
                  style={{ color: "var(--ac-text)" }}
                >
                  فاکتور
                </a>
                {cancellable(o.status) && (
                  <button
                    onClick={() => setConfirmOrder(o)}
                    disabled={cancelling === o.id}
                    className="rounded-xl border border-rose-300 px-4 py-2 text-sm font-bold text-rose-600 transition hover:bg-rose-50 disabled:opacity-60"
                  >
                    {cancelling === o.id ? "در حال لغو..." : "لغو سفارش"}
                  </button>
                )}
              </div>
            </Panel>
          ))}
        </div>
      )}

      {confirmOrder && (
        <div className="fixed inset-0 z-50 grid place-items-center p-4">
          <div onClick={() => cancelling === null && setConfirmOrder(null)} className="absolute inset-0 bg-black/50 backdrop-blur-sm" />
          <div className="relative w-full max-w-md rounded-2xl p-6 shadow-2xl" style={{ background: "var(--ac-panel-bg)", border: "1px solid var(--ac-panel-border)" }}>
            <div className="mx-auto mb-4 grid h-12 w-12 place-items-center rounded-full bg-rose-100 text-2xl text-rose-600">!</div>
            <h3 className="text-center text-lg font-bold" style={{ color: "var(--ac-title)" }}>لغو سفارش {confirmOrder.code}</h3>

            {collected > 0 ? (
              <>
                <p className="mt-2 text-center text-sm leading-7" style={{ color: "var(--ac-text)" }}>
                  در صورت لغو این سفارش، جریمه‌ی لغو از مبلغ پرداخت‌شده کسر و باقیمانده به کیف پولتان بازمی‌گردد:
                </p>
                <div className="mt-4 space-y-2 rounded-xl bg-[#FFF8F2] p-4 text-sm">
                  <div className="flex items-center justify-between">
                    <span style={{ color: "var(--ac-muted)" }}>مبلغ پرداخت‌شده</span>
                    <span style={{ color: "var(--ac-title)" }}>{formatToman(collected)}</span>
                  </div>
                  <div className="flex items-center justify-between">
                    <span style={{ color: "var(--ac-muted)" }}>جریمه‌ی لغو ({toFa(penalty)}٪)</span>
                    <span className="text-rose-600">− {formatToman(penaltyAmount)}</span>
                  </div>
                  <div className="flex items-center justify-between border-t pt-2" style={{ borderColor: "var(--ac-divider)" }}>
                    <span className="font-bold" style={{ color: "var(--ac-title)" }}>بازگشت به کیف پول</span>
                    <span className="font-bold text-emerald-600">{formatToman(refundAmount)}</span>
                  </div>
                </div>
              </>
            ) : (
              <p className="mt-3 text-center text-sm leading-7" style={{ color: "var(--ac-text)" }}>
                هنوز مبلغی برای این سفارش از کیف پول شما کسر نشده است. آیا از لغو آن مطمئن هستید؟
              </p>
            )}

            <div className="mt-6 flex gap-3">
              <button
                onClick={doCancel}
                disabled={cancelling !== null}
                className="h-11 flex-1 rounded-xl text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
                style={{ background: "linear-gradient(135deg, #e60053, #9c0038)" }}
              >
                {cancelling !== null ? "در حال لغو..." : "بله، لغو کن"}
              </button>
              <button
                onClick={() => setConfirmOrder(null)}
                disabled={cancelling !== null}
                className="h-11 flex-1 rounded-xl border border-[#EADFD4] text-sm font-bold transition hover:bg-[#FFF7F1] disabled:opacity-60"
                style={{ color: "var(--ac-text)" }}
              >
                انصراف
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
