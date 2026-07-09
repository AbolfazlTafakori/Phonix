"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Order } from "@/lib/types";
import { formatToman, toFa } from "@/lib/format";
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

  return (
    <div dir="rtl" className="invoice-root mx-auto max-w-[800px] p-6" style={{ color: "var(--chat-ink)" }}>
      <style>{`
        .invoice-root { background: var(--chat-surface); }
        @media print {
          .no-print { display: none !important; }
          body { background: #fff !important; }
          .invoice-root { background: #fff !important; color: #1a1a1a !important; }
          .invoice-root * { color: inherit !important; border-color: #e0e0e0 !important; }
        }
      `}</style>

      <div className="mb-6 flex items-center justify-between pb-4" style={{ borderBottom: "2px solid var(--chat-border)" }}>
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

      <div className="mb-6 grid grid-cols-2 gap-4 text-sm">
        <div>
          <p style={{ color: "var(--chat-muted)" }}>شماره سفارش</p>
          <p className="font-mono font-bold">{order.code}</p>
        </div>
        <div>
          <p style={{ color: "var(--chat-muted)" }}>تاریخ</p>
          <p className="font-bold">{order.date}</p>
        </div>
        <div>
          <p style={{ color: "var(--chat-muted)" }}>مشتری</p>
          <p className="font-bold">{order.userName}</p>
        </div>
        <div>
          <p style={{ color: "var(--chat-muted)" }}>وضعیت</p>
          <p className="font-bold">{orderStatusLabel[order.status]}</p>
        </div>
      </div>

      <div className="overflow-x-auto">
        <table className="mb-6 w-full text-right text-sm">
          <thead>
            <tr style={{ borderBottom: "1px solid var(--chat-border)", color: "var(--chat-muted)" }}>
              <th className="py-2 font-medium">ردیف</th>
              <th className="py-2 font-medium">محصول</th>
              <th className="py-2 font-medium">تعداد</th>
              <th className="py-2 font-medium">قیمت واحد</th>
              <th className="py-2 font-medium">جمع</th>
            </tr>
          </thead>
          <tbody>
            {order.items.map((it, i) => (
              <tr key={i} style={{ borderBottom: "1px solid var(--chat-border)" }}>
                <td className="py-2.5">{toFa(i + 1)}</td>
                <td className="py-2.5">
                  {it.name}
                  {it.plan && <span style={{ color: "var(--chat-muted)" }}> · {it.plan}</span>}
                </td>
                <td className="py-2.5">{toFa(it.quantity)}</td>
                <td className="py-2.5">{formatToman(it.unitPrice)}</td>
                <td className="py-2.5">{formatToman(it.lineTotal)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="ml-auto max-w-[300px] space-y-2 text-sm">
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
          <span>{formatToman(order.total)}</span>
        </div>
      </div>

      <p className="mt-10 text-center text-xs" style={{ color: "var(--chat-muted)" }}>
        این فاکتور به‌صورت الکترونیکی صادر شده است · Phoenix Verify
      </p>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between" style={{ color: "var(--chat-ink-2)" }}>
      <span>{label}</span>
      <span>{value}</span>
    </div>
  );
}
