"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Order } from "@/lib/types";
import { formatToman } from "@/lib/format";
import { Card, PageHeader, Spinner, StatusBadge, Modal, inputCls } from "@/components/admin/ui";
import { Pagination, usePaged } from "@/components/admin/Pagination";
import AdminIcon from "@/components/admin/AdminIcon";

// Financial team: review the deposit receipt of orders awaiting payment approval, then approve (→ moves to
// fulfillment) or reject (→ cancels and restores stock). Distinct from the technical delivery section.
export default function OrderReceiptsPage() {
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState<number | null>(null);
  const [rejecting, setRejecting] = useState<Order | null>(null);
  const [reason, setReason] = useState("");

  useEffect(() => {
    (async () => {
      try {
        setOrders((await api.orders.list()).filter((o) => o.status === "PendingApproval"));
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const { page, setPage, totalPages, slice, total, pageSize } = usePaged(orders, 10);
  const drop = (id: number) => setOrders((p) => p.filter((o) => o.id !== id));

  async function approve(o: Order) {
    setBusy(o.id);
    try {
      await api.orders.approve(o.id);
      drop(o.id);
    } finally {
      setBusy(null);
    }
  }

  async function doReject() {
    if (!rejecting) return;
    setBusy(rejecting.id);
    try {
      await api.orders.reject(rejecting.id, reason.trim() || undefined);
      drop(rejecting.id);
      setRejecting(null);
      setReason("");
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <PageHeader title="تأیید رسید واریز" desc="بررسی رسید پرداخت سفارش‌های در انتظار تأیید — تأیید یا رد" />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : orders.length === 0 ? (
        <Card className="p-12 text-center text-white/40">رسیدی در انتظار تأیید نیست</Card>
      ) : (
        <div className="space-y-4">
          {slice.map((o) => (
            <Card key={o.id} className="p-5">
              <div className="flex flex-wrap items-center justify-between gap-3 border-b border-white/8 pb-3">
                <div>
                  <p className="font-mono text-sm text-white/70">{o.code}</p>
                  <p className="text-xs text-white/40">{o.userName} · {o.date} · {o.paymentMethod}</p>
                </div>
                <div className="flex items-center gap-3">
                  {o.receiptUrl && (
                    <a href={api.transactions.receiptSrc(o.receiptUrl)} target="_blank" rel="noreferrer" className="text-xs font-bold text-[#6f93ff] transition hover:underline">
                      مشاهده رسید
                    </a>
                  )}
                  <StatusBadge status="در انتظار تأیید" />
                </div>
              </div>

              <div className="mt-3 grid gap-2 sm:grid-cols-2">
                {o.items.map((it) => (
                  <div key={`${it.productId}:${it.plan ?? ""}`} className="flex items-center gap-3 rounded-lg bg-white/[0.03] p-2">
                    <img src={it.image} alt={it.name} className="h-9 w-9 rounded object-cover" />
                    <span className="flex-1 text-sm text-white/80">
                      {it.name} × {it.quantity}
                      {it.plan && <span className="text-white/45"> · {it.plan}</span>}
                    </span>
                    <span className="text-xs text-white/55">{formatToman(it.lineTotal)}</span>
                  </div>
                ))}
              </div>

              <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-white/8 pt-4">
                <span className="font-bold text-emerald-400">{formatToman(o.total)}</span>
                <div className="flex flex-wrap items-center gap-2 sm:justify-end">
                  <button onClick={() => approve(o)} disabled={busy === o.id} className="flex h-10 items-center gap-1.5 rounded-lg bg-emerald-500/15 px-4 text-xs font-bold text-emerald-400 transition hover:bg-emerald-500/25 active:scale-[0.98] md:h-9">
                    {busy === o.id ? <Spinner /> : <><AdminIcon name="check" className="h-4 w-4" /> تأیید رسید</>}
                  </button>
                  <button onClick={() => { setRejecting(o); setReason(""); }} disabled={busy === o.id} className="flex h-10 items-center gap-1.5 rounded-lg bg-rose-500/15 px-4 text-xs font-bold text-rose-400 transition hover:bg-rose-500/25 active:scale-[0.98] md:h-9">
                    <AdminIcon name="trash" className="h-4 w-4" /> رد رسید
                  </button>
                </div>
              </div>
            </Card>
          ))}
          <Pagination page={page} totalPages={totalPages} total={total} pageSize={pageSize} onPage={setPage} />
        </div>
      )}

      <Modal open={rejecting !== null} onClose={() => setRejecting(null)} title={`رد رسید سفارش ${rejecting?.code ?? ""}`}>
        <p className="text-sm text-white/60">با رد رسید، سفارش لغو می‌شود و موجودی به انبار برمی‌گردد.</p>
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          rows={3}
          placeholder="دلیل رد (اختیاری)…"
          className={`${inputCls} mt-3 resize-none`}
        />
        <div className="mt-4 flex justify-end gap-2">
          <button onClick={() => setRejecting(null)} className="rounded-lg border border-white/10 px-4 py-2 text-sm font-bold text-white/70 transition hover:bg-white/5">انصراف</button>
          <button onClick={doReject} disabled={busy === rejecting?.id} className="flex items-center gap-1.5 rounded-lg bg-rose-500/20 px-4 py-2 text-sm font-bold text-rose-300 transition hover:bg-rose-500/30">
            {busy === rejecting?.id ? <Spinner /> : "رد و لغو سفارش"}
          </button>
        </div>
      </Modal>
    </div>
  );
}
