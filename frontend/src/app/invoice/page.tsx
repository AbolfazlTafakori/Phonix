"use client";

import { useEffect, useState, type ReactNode } from "react";
import { api } from "@/lib/api";
import type { Order } from "@/lib/types";
import { formatNumber, formatToman, toFa } from "@/lib/format";
import { orderStatusLabel } from "@/lib/labels";

export default function InvoicePage() {
  const [order, setOrder] = useState<Order | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    const id = Number(new URLSearchParams(window.location.search).get("id"));
    if (!id) {
      setError("سفارش نامعتبر است.");
      return;
    }
    api.orders
      .get(id)
      .then(setOrder)
      .catch((e) => setError(e instanceof Error ? e.message : "خطا در بارگذاری فاکتور"));
  }, []);

  if (error)
    return (
      <div className="mx-auto max-w-[800px] p-10 text-center text-rose-500">{error}</div>
    );
  if (!order)
    return (
      <div className="mx-auto max-w-[800px] p-10 text-center" style={{ color: "var(--chat-muted)" }}>
        <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-[var(--chat-border)] border-t-[#ff5a1f]" />
      </div>
    );

  const itemCount = order.items.reduce((n, it) => n + it.quantity, 0);

  return (
    <div dir="rtl" className="invoice-root mx-auto max-w-[800px] p-6" style={{ color: "var(--chat-ink)" }}>
      <style>{`
        .invoice-root { background: var(--chat-surface); }
        .invoice-num { font-variant-numeric: tabular-nums; }
        @media print {
          @page { size: A4; margin: 14mm; }
          .no-print { display: none !important; }
          html, body { background: #fff !important; }
          .invoice-root { background: #fff !important; color: #1a1a1a !important; max-width: none !important; padding: 0 !important; }
          .invoice-root * { color: inherit !important; border-color: #d9d9d9 !important; }
          .invoice-root thead { background: #f5f5f5 !important; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
          .invoice-root tr { break-inside: avoid; }
          .invoice-totals { break-inside: avoid; }
        }
      `}</style>

      <div className="mb-6 flex items-start justify-between pb-4" style={{ borderBottom: "2px solid var(--chat-border)" }}>
        <div>
          <h1 className="text-2xl font-bold" style={{ color: "var(--chat-ink)" }}>Phoenix Verify</h1>
          <p className="text-sm" style={{ color: "var(--chat-muted)" }}>فاکتور فروش</p>
        </div>
        <button
          onClick={() => window.print()}
          className="no-print rounded-xl px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
          style={{ background: "linear-gradient(135deg, #ef233c, #ff7a2e)" }}
        >
          چاپ / ذخیره PDF
        </button>
      </div>

      <div className="mb-6 grid grid-cols-2 gap-x-4 gap-y-3 text-sm sm:grid-cols-3">
        <Field label="شماره سفارش" value={<span className="invoice-num font-mono font-bold">{order.code}</span>} />
        <Field label="تاریخ" value={<span className="font-bold">{order.date}</span>} />
        <Field label="وضعیت" value={<span className="font-bold">{orderStatusLabel[order.status]}</span>} />
        <Field label="مشتری" value={<span className="font-bold">{order.userName}</span>} />
        {order.paymentMethod && (
          <Field label="روش پرداخت" value={<span className="font-bold">{order.paymentMethod}</span>} />
        )}
        <Field label="تعداد اقلام" value={<span className="font-bold">{toFa(itemCount)}</span>} />
      </div>

      <div className="overflow-x-auto">
        <table className="mb-6 w-full text-right text-sm">
          <thead>
            <tr style={{ borderBottom: "1px solid var(--chat-border)", color: "var(--chat-muted)" }}>
              <th className="py-2 pl-2 text-center font-medium">ردیف</th>
              <th className="py-2 font-medium">محصول</th>
              <th className="py-2 text-center font-medium">تعداد</th>
              <th className="py-2 text-left font-medium">قیمت واحد</th>
              <th className="py-2 text-left font-medium">جمع (تومان)</th>
            </tr>
          </thead>
          <tbody>
            {order.items.map((it, i) => (
              <tr key={i} style={{ borderBottom: "1px solid var(--chat-border)" }}>
                <td className="py-2.5 text-center align-top">{toFa(i + 1)}</td>
                <td className="py-2.5 align-top">
                  <span className="font-medium">{it.name}</span>
                  {it.plan && <span style={{ color: "var(--chat-muted)" }}> · {it.plan}</span>}
                  {it.customerNote && (
                    <span className="block text-xs" style={{ color: "var(--chat-muted)" }}>
                      {it.customerNote}
                    </span>
                  )}
                </td>
                <td className="invoice-num py-2.5 text-center align-top">{toFa(it.quantity)}</td>
                <td className="invoice-num py-2.5 text-left align-top">{formatNumber(it.unitPrice)}</td>
                <td className="invoice-num py-2.5 text-left align-top font-medium">{formatNumber(it.lineTotal)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="invoice-totals ml-auto max-w-[320px] space-y-2 text-sm">
        <Row label="جمع اقلام" value={formatToman(order.subtotal)} />
        {order.discountAmount > 0 && (
          <Row label={`تخفیف${order.discountCode ? ` (${order.discountCode})` : ""}`} value={`− ${formatToman(order.discountAmount)}`} />
        )}
        {(order.vatAmount ?? 0) > 0 && (
          <Row label="مالیات بر ارزش افزوده" value={`+ ${formatToman(order.vatAmount ?? 0)}`} />
        )}
        {order.feeAmount > 0 && <Row label="کارمزد درگاه" value={`+ ${formatToman(order.feeAmount)}`} />}
        {order.walletPaid > 0 && <Row label="پرداخت از کیف پول" value={`− ${formatToman(order.walletPaid)}`} />}
        <div className="flex items-center justify-between pt-2 text-base font-bold" style={{ borderTop: "2px solid var(--chat-border)" }}>
          <span>مبلغ کل</span>
          <span className="invoice-num">{formatToman(order.total)}</span>
        </div>
      </div>

      {order.note && (
        <div className="mt-8 rounded-xl p-4 text-sm" style={{ background: "var(--chat-surface-2, rgba(127,127,127,.06))" }}>
          <p className="mb-1 font-medium" style={{ color: "var(--chat-muted)" }}>توضیحات</p>
          <p style={{ color: "var(--chat-ink-2)" }}>{order.note}</p>
        </div>
      )}

      <p className="mt-10 text-center text-xs" style={{ color: "var(--chat-muted)" }}>
        این فاکتور به‌صورت الکترونیکی صادر شده و بدون مهر و امضا معتبر است · Phoenix Verify
      </p>
    </div>
  );
}

function Field({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div>
      <p style={{ color: "var(--chat-muted)" }}>{label}</p>
      <p>{value}</p>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between" style={{ color: "var(--chat-ink-2)" }}>
      <span>{label}</span>
      <span className="invoice-num">{value}</span>
    </div>
  );
}
