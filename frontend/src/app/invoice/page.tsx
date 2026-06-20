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

  if (error) return <div className="mx-auto max-w-[800px] p-10 text-center text-rose-500">{error}</div>;
  if (!order) return <div className="mx-auto max-w-[800px] p-10 text-center text-gray-400">در حال بارگذاری...</div>;

  return (
    <div dir="rtl" className="invoice-root mx-auto max-w-[800px] p-6 text-gray-800">
      <style>{`
        .invoice-root { background: #fff; }
        @media print { .no-print { display: none !important; } body { background: #fff; } }
      `}</style>

      <div className="mb-6 flex items-center justify-between border-b-2 border-gray-200 pb-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Phoenix Verify</h1>
          <p className="text-sm text-gray-500">فاکتور فروش</p>
        </div>
        <button onClick={() => window.print()} className="no-print rounded-lg bg-[#3a64f2] px-5 py-2.5 text-sm font-bold text-white">
          چاپ / ذخیره PDF
        </button>
      </div>

      <div className="mb-6 grid grid-cols-2 gap-4 text-sm">
        <div>
          <p className="text-gray-500">شماره سفارش</p>
          <p className="font-mono font-bold">{order.code}</p>
        </div>
        <div>
          <p className="text-gray-500">تاریخ</p>
          <p className="font-bold">{order.date}</p>
        </div>
        <div>
          <p className="text-gray-500">مشتری</p>
          <p className="font-bold">{order.userName}</p>
        </div>
        <div>
          <p className="text-gray-500">وضعیت</p>
          <p className="font-bold">{orderStatusLabel[order.status]}</p>
        </div>
      </div>

      <table className="mb-6 w-full text-right text-sm">
        <thead>
          <tr className="border-b border-gray-300 text-gray-500">
            <th className="py-2 font-medium">ردیف</th>
            <th className="py-2 font-medium">محصول</th>
            <th className="py-2 font-medium">تعداد</th>
            <th className="py-2 font-medium">قیمت واحد</th>
            <th className="py-2 font-medium">جمع</th>
          </tr>
        </thead>
        <tbody>
          {order.items.map((it, i) => (
            <tr key={i} className="border-b border-gray-100">
              <td className="py-2">{toFa(i + 1)}</td>
              <td className="py-2">
                {it.name}
                {it.plan && <span className="text-gray-500"> · {it.plan}</span>}
              </td>
              <td className="py-2">{toFa(it.quantity)}</td>
              <td className="py-2">{formatToman(it.unitPrice)}</td>
              <td className="py-2">{formatToman(it.lineTotal)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="ml-auto max-w-[300px] space-y-1.5 text-sm">
        <Row label="جمع اقلام" value={formatToman(order.subtotal)} />
        {order.discountAmount > 0 && <Row label={`تخفیف${order.discountCode ? ` (${order.discountCode})` : ""}`} value={`− ${formatToman(order.discountAmount)}`} />}
        {order.feeAmount > 0 && <Row label="کارمزد درگاه" value={`+ ${formatToman(order.feeAmount)}`} />}
        {order.walletPaid > 0 && <Row label="پرداخت از کیف پول" value={`− ${formatToman(order.walletPaid)}`} />}
        <div className="flex items-center justify-between border-t-2 border-gray-300 pt-2 text-base font-bold">
          <span>مبلغ کل</span>
          <span>{formatToman(order.total)}</span>
        </div>
      </div>

      <p className="mt-10 text-center text-xs text-gray-400">این فاکتور به‌صورت الکترونیکی صادر شده است · Phoenix Verify</p>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between text-gray-600">
      <span>{label}</span>
      <span>{value}</span>
    </div>
  );
}
