"use client";

import { useEffect, useState, type ReactNode } from "react";
import { api } from "@/lib/api";
import type { Invoice } from "@/lib/types";
import { toEn } from "@/lib/format";

// The customer invoice, laid out as a real commercial document: seller and buyer blocks, a priced line table,
// and a totals panel that foots against the money that actually moved.
//
// It bills what was DELIVERED. When part of an order is cancelled those units are refunded, so they are left
// off the lines entirely and reported once as "this many items weren't delivered and this much came back" —
// the buyer is never invoiced for something they got their money back for. All the arithmetic comes from the
// server (see InvoiceBuilder), so nothing here can drift from the order's real figures.

// An invoice reads as a financial document, so every figure on it is set in Latin digits — grouped, tabular,
// and isolated from the RTL flow so the browser never reorders a number or a code.
const num = (n: number) => Math.round(n).toLocaleString("en-US");

// Dates arrive from the store already rendered in Persian digits; the document sets them in Latin like
// every other figure.
function N({ children }: { children: ReactNode }) {
  return <span className="inv-n">{children}</span>;
}

// One label/value row. The label sits right, the value left, so every value shares one vertical edge.
function Row({ label, value, strong = false }: { label: ReactNode; value: ReactNode; strong?: boolean }) {
  return (
    <div className="inv-row">
      <span className="inv-row-k">{label}</span>
      <span className={`inv-row-v${strong ? " inv-strong" : ""}`}>{value}</span>
    </div>
  );
}

export default function InvoicePage() {
  const [invoice, setInvoice] = useState<Invoice | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    const id = Number(new URLSearchParams(window.location.search).get("id"));
    if (!id) {
      setError("سفارش نامعتبر است.");
      return;
    }
    api.orders
      .invoice(id)
      .then(setInvoice)
      .catch((e) => setError(e instanceof Error ? e.message : "خطا در بارگذاری فاکتور"));
  }, []);

  if (error)
    return <div className="mx-auto max-w-[820px] p-10 text-center text-sm text-rose-500">{error}</div>;
  if (!invoice)
    return (
      <div className="mx-auto max-w-[820px] p-10 text-center">
        <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-[var(--chat-border)] border-t-[#ff5a1f]" />
      </div>
    );

  const inv = invoice;

  return (
    <div dir="rtl" className="inv-page">
      <style>{`
        /* The document is set on the site's own Persian face; figures fall back to a Latin face with real
           tabular numerals so every column of digits shares one vertical rhythm. */
        .inv-page{--ink:#111318;--ink-2:#40464f;--muted:#666d78;--rule:#e7e9ed;--rule-2:#c8cdd5;--band:#f8f9fb;--accent:#ef233c;
          background:#eaecf0;padding:30px 14px;color:var(--ink);
          font-family:var(--font-vazir),Tahoma,system-ui,sans-serif;
          font-size:12.5px;line-height:1.7;-webkit-font-smoothing:antialiased;text-rendering:optimizeLegibility}
        .inv-page *{box-sizing:border-box}
        .inv-sheet{width:210mm;max-width:100%;margin:0 auto;background:#fff;padding:16mm 14mm;
          border-top:3px solid var(--accent);box-shadow:0 1px 3px rgba(17,19,24,.10),0 8px 28px rgba(17,19,24,.07)}
        .inv-n{direction:ltr;unicode-bidi:isolate;font-family:"Segoe UI",Inter,system-ui,sans-serif;
          font-variant-numeric:tabular-nums;font-feature-settings:"tnum" 1,"lnum" 1;letter-spacing:.01em}

        .inv-top{display:flex;justify-content:space-between;align-items:baseline;gap:20px}
        .inv-kind{font-size:16px;font-weight:700;text-align:right;letter-spacing:-.005em}
        .inv-mark{font-family:"Segoe UI",Inter,system-ui,sans-serif;font-size:27px;font-weight:700;
          letter-spacing:-.025em;line-height:1.1;text-align:left;direction:ltr;unicode-bidi:isolate;flex:none}
        .inv-rule{height:1.5px;background:var(--ink);margin-top:11px}
        .inv-tag{margin:8px 0 0;font-size:11.5px;color:var(--muted);text-align:right;letter-spacing:.005em}

        .inv-meta{display:grid;grid-template-columns:repeat(2,1fr);gap:0 30px;margin-top:16px}
        .inv-row{display:flex;justify-content:space-between;align-items:baseline;gap:14px;
          padding:7px 0;border-bottom:1px solid var(--rule)}
        .inv-row-k{color:var(--muted);font-size:11.5px;white-space:nowrap}
        .inv-row-v{font-weight:600;font-size:12.5px;text-align:left}

        .inv-parties{display:grid;grid-template-columns:1fr 1fr;gap:14px;margin-top:18px}
        .inv-party{border:1px solid var(--rule-2)}
        .inv-party h2{margin:0;padding:8px 12px;font-size:11px;font-weight:700;letter-spacing:.015em;
          background:var(--band);border-bottom:1px solid var(--rule-2);text-align:right;color:var(--ink-2)}
        .inv-party-body{padding:2px 12px 9px}
        .inv-party-body .inv-row:last-child{border-bottom:0}

        .inv-tablewrap{margin-top:20px}
        .inv-page table{width:100%;border-collapse:collapse;font-size:12.5px}
        .inv-page thead th{background:var(--ink);color:#fff;padding:10px 11px;font-weight:600;font-size:11.5px;
          letter-spacing:.01em;white-space:nowrap;-webkit-print-color-adjust:exact;print-color-adjust:exact}
        .inv-r{text-align:right}.inv-c{text-align:center}.inv-l{text-align:left}
        .inv-page tbody td{padding:11px;border-bottom:1px solid var(--rule);vertical-align:middle}
        .inv-page tbody tr:last-child td{border-bottom:1px solid var(--rule-2)}
        .inv-idx{color:var(--muted);font-size:12px}
        .inv-nm{font-weight:600;line-height:1.5}
        .inv-sub{color:var(--muted);font-size:11px;margin-top:3px;letter-spacing:.005em}

        .inv-note{margin-top:12px;border:1px solid var(--rule-2);background:var(--band);padding:10px 13px;
          font-size:11.5px;color:var(--ink-2);line-height:1.9;text-align:right}

        .inv-close{display:flex;justify-content:space-between;align-items:stretch;gap:26px;margin-top:22px}
        .inv-sums{width:330px;flex:none}
        .inv-sums .inv-row-v{font-weight:500}
        .inv-grand{display:flex;justify-content:space-between;align-items:baseline;gap:14px;margin-top:10px;
          padding:12px 13px;background:var(--ink);color:#fff;
          -webkit-print-color-adjust:exact;print-color-adjust:exact}
        .inv-grand span:first-child{font-size:12.5px;font-weight:600}
        .inv-grand span:last-child{font-size:16px;font-weight:700;text-align:left;letter-spacing:-.01em}
        .inv-closing{flex:1;display:flex;flex-direction:column;justify-content:space-between;gap:14px;min-width:0}
        .inv-thanks{margin:0;font-size:13.5px;font-weight:700;text-align:right;line-height:1.65}
        .inv-terms{margin:7px 0 0;font-size:11px;color:var(--muted);text-align:right;line-height:2;max-width:46ch}
        .inv-seal{width:158px;height:158px;object-fit:contain;align-self:flex-end}

        .inv-foot{margin-top:24px;padding-top:11px;border-top:1px solid var(--rule);display:flex;
          justify-content:space-between;align-items:center;gap:16px;font-size:10.5px;color:var(--muted)}
        .inv-print{position:fixed;top:18px;left:18px;border-radius:10px;padding:10px 18px;font-size:13px;
          font-weight:600;color:#fff;background:var(--ink);border:0;cursor:pointer}
        .inv-print:hover{background:#000}

        @media (max-width:660px){
          .inv-page{padding:12px 7px;font-size:13px}
          .inv-sheet{padding:18px 14px}
          .inv-mark{font-size:20px}.inv-kind{font-size:15px}
          .inv-meta,.inv-parties{grid-template-columns:1fr}
          .inv-parties{gap:12px}
          .inv-tablewrap{overflow-x:auto;-webkit-overflow-scrolling:touch}
          .inv-page table{min-width:480px}
          .inv-close{flex-direction:column;align-items:stretch;gap:18px}
          .inv-sums{width:100%}
          .inv-terms{max-width:none}
          .inv-seal{width:124px;height:124px}
          .inv-foot{flex-direction:column;align-items:flex-start;gap:5px}
          .inv-print{position:static;display:block;width:100%;margin-bottom:10px}
        }
        @media print{
          @page{size:A4;margin:12mm}
          .inv-page{background:#fff;padding:0}
          .inv-sheet{width:auto;box-shadow:none;padding:0;border-top:0}
          .inv-print{display:none}
          .inv-page tr,.inv-close,.inv-grand{break-inside:avoid}
        }
      `}</style>

      <button type="button" className="inv-print" onClick={() => window.print()}>چاپ / ذخیره PDF</button>

      <div className="inv-sheet">
        <div className="inv-top">
          <div className="inv-kind">فاکتور مشتری</div>
          <div className="inv-mark">Phoenix Verify</div>
        </div>
        <div className="inv-rule" />
        <p className="inv-tag">فونیکس ورفای · ارائه‌دهنده اشتراک و سرویس‌های دیجیتال</p>

        <div className="inv-meta">
          {inv.invoiceNumber && <Row label="شماره فاکتور" value={<N>{toEn(inv.invoiceNumber)}</N>} />}
          {inv.issuedAt && <Row label="تاریخ صدور" value={<N>{toEn(inv.issuedAt)}</N>} />}
          <Row label="شماره سفارش" value={<N>{inv.orderCode}</N>} />
          <Row label="تاریخ سفارش" value={<N>{toEn(inv.date)}</N>} />
          {inv.paymentMethod && <Row label="روش پرداخت" value={inv.paymentMethod} />}
          <Row label="وضعیت" value="تحویل شده" />
        </div>

        <div className="inv-parties">
          <div className="inv-party">
            <h2>مشخصات فروشنده</h2>
            <div className="inv-party-body">
              <Row label="نام" value="فونیکس ورفای" />
              <Row label="وب‌سایت" value={<N>phoenixverify.com</N>} />
              <Row label="پشتیبانی" value={<N>support@phoenixverify.com</N>} />
            </div>
          </div>
          <div className="inv-party">
            <h2>مشخصات خریدار</h2>
            <div className="inv-party-body">
              <Row label="نام" value={inv.customerName} />
              {inv.customerCode && <Row label="کد کاربری" value={<N>{inv.customerCode}</N>} />}
              {inv.customerEmail && <Row label="ایمیل" value={<N>{inv.customerEmail}</N>} />}
            </div>
          </div>
        </div>

        <div className="inv-tablewrap">
          <table>
            <thead>
              <tr>
                <th className="inv-c" style={{ width: 38 }}>ردیف</th>
                <th className="inv-r">شرح کالا / خدمات</th>
                <th className="inv-c" style={{ width: 56 }}>تعداد</th>
                <th className="inv-l" style={{ width: 104 }}>مبلغ واحد</th>
                <th className="inv-l" style={{ width: 112 }}>مبلغ کل</th>
              </tr>
            </thead>
            <tbody>
              {inv.lines.map((line, i) => (
                <tr key={i}>
                  <td className="inv-c inv-idx"><N>{i + 1}</N></td>
                  <td className="inv-r">
                    <div className="inv-nm">{line.name}</div>
                    {line.plan && <div className="inv-sub">{line.plan}</div>}
                  </td>
                  <td className="inv-c"><N>{line.quantity}</N></td>
                  <td className="inv-l"><N>{num(line.unitPrice)}</N></td>
                  <td className="inv-l inv-strong" style={{ fontWeight: 700 }}><N>{num(line.lineTotal)}</N></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* Cancelled units are never itemized — the buyer was refunded, so they aren't part of this bill. */}
        {inv.excludedCount > 0 && (
          <div className="inv-note">
            این فاکتور فقط شامل اقلام تحویل‌شده است. <N>{inv.excludedCount}</N> قلم از این سفارش تحویل نشد
            {inv.excludedRefund > 0 && <> و مبلغ آن، <N>{num(inv.excludedRefund)}</N> تومان، به کیف پول خریدار بازگردانده شد</>}.
          </div>
        )}

        <div className="inv-close">
          <div className="inv-sums">
            <Row label="جمع اقلام تحویل‌شده" value={<N>{num(inv.subtotal)}</N>} />
            {inv.discountAmount > 0 && (
              <Row
                label={<>تخفیف{inv.discountCode ? <> (<N>{inv.discountCode}</N>)</> : null}</>}
                value={<N>{num(inv.discountAmount)} −</N>}
              />
            )}
            {inv.vatAmount > 0 && <Row label="مالیات بر ارزش افزوده" value={<N>{num(inv.vatAmount)} +</N>} />}
            {inv.feeAmount > 0 && <Row label="کارمزد درگاه" value={<N>{num(inv.feeAmount)} +</N>} />}
            <div className="inv-grand">
              <span>مبلغ قابل پرداخت</span>
              <span className="inv-n">{num(inv.total)} تومان</span>
            </div>
          </div>

          <div className="inv-closing">
            <div>
              <p className="inv-thanks">از اعتماد و خرید شما سپاسگزاریم.</p>
              <p className="inv-terms">
                خریدار با ثبت این سفارش، قوانین و مقررات فونیکس ورفای را مطالعه کرده و پذیرفته است.
                پشتیبانی این سرویس، در چهارچوب شرایط تأییدشده توسط کاربر، بر عهده‌ی تیم پشتیبانی مجموعه می‌باشد.
              </p>
            </div>
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img className="inv-seal" src="/figma/invoice-seal.png" alt="مهر فونیکس ورفای" />
          </div>
        </div>

        <div className="inv-foot">
          <span>این فاکتور به‌صورت الکترونیکی صادر شده و بدون مهر و امضای فیزیکی معتبر است.</span>
          <N>phoenixverify.com</N>
        </div>
      </div>
    </div>
  );
}
