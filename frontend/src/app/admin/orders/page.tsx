"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { Order, OrderStatus } from "@/lib/types";
import { formatToman, formatNumber } from "@/lib/format";
import { orderStatusLabel } from "@/lib/labels";
import { Card, PageHeader, Spinner, StatusBadge, Modal, Field, Toggle, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

type Filter = "all" | OrderStatus;

export default function AdminOrdersPage() {
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
  const [busy, setBusy] = useState<number | null>(null);

  const [deliverOrder, setDeliverOrder] = useState<Order | null>(null);
  const [content, setContent] = useState("");
  const [sendEmail, setSendEmail] = useState(true);
  const [emailSubject, setEmailSubject] = useState("");
  const [emailBody, setEmailBody] = useState("");
  const [delivering, setDelivering] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        setOrders(await api.orders.list());
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const counts = useMemo(
    () => ({
      PendingApproval: orders.filter((o) => o.status === "PendingApproval").length,
      Preparing: orders.filter((o) => o.status === "Preparing").length,
      Completed: orders.filter((o) => o.status === "Completed").length,
      Cancelled: orders.filter((o) => o.status === "Cancelled").length,
    }),
    [orders],
  );
  const shown = filter === "all" ? orders : orders.filter((o) => o.status === filter);

  async function act(o: Order, kind: "approve" | "cancel") {
    setBusy(o.id);
    try {
      const updated = kind === "approve" ? await api.orders.approve(o.id) : await api.orders.cancel(o.id);
      setOrders((p) => p.map((x) => (x.id === o.id ? updated : x)));
    } finally {
      setBusy(null);
    }
  }

  function openDeliver(o: Order) {
    setDeliverOrder(o);
    setContent(o.deliveryContent ?? "");
    setSendEmail(true);
    setEmailSubject(`سفارش ${o.code} آماده شد`);
    setEmailBody("سفارش شما آماده شد. برای مشاهده به حساب کاربری خود، بخش سفارش‌ها مراجعه کنید.");
  }
  async function doDeliver() {
    if (!deliverOrder) return;
    setDelivering(true);
    try {
      const updated = await api.orders.deliver(deliverOrder.id, { content, email: sendEmail, emailSubject, emailBody });
      setOrders((p) => p.map((x) => (x.id === deliverOrder.id ? updated : x)));
      setDeliverOrder(null);
    } finally {
      setDelivering(false);
    }
  }

  const filters: { key: Filter; label: string; count?: number }[] = [
    { key: "all", label: "همه" },
    { key: "PendingApproval", label: "در انتظار تأیید", count: counts.PendingApproval },
    { key: "Preparing", label: "در حال آماده‌سازی", count: counts.Preparing },
    { key: "Completed", label: "تکمیل شده", count: counts.Completed },
    { key: "Cancelled", label: "لغو شده", count: counts.Cancelled },
  ];

  return (
    <div>
      <PageHeader title="سفارش‌ها" desc="تأیید نهایی، آماده‌سازی و تکمیل سفارش‌ها" />

      <div className="mb-5 flex flex-wrap gap-2">
        {filters.map((f) => (
          <button
            key={f.key}
            onClick={() => setFilter(f.key)}
            className={`flex items-center gap-2 rounded-full border px-4 py-1.5 text-sm font-medium transition ${
              filter === f.key ? "border-transparent bg-white/10 text-white" : "border-white/10 text-white/60 hover:text-white"
            }`}
          >
            {f.label}
            {f.count ? <span className="rounded-full bg-[#e60053]/20 px-1.5 text-[11px] font-bold text-[#ff5a8a]">{formatNumber(f.count)}</span> : null}
          </button>
        ))}
      </div>

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : shown.length === 0 ? (
        <Card className="p-12 text-center text-white/40">سفارشی در این وضعیت نیست</Card>
      ) : (
        <div className="space-y-4">
          {shown.map((o) => (
            <Card key={o.id} className="p-5">
              <div className="flex flex-wrap items-center justify-between gap-3 border-b border-white/8 pb-3">
                <div>
                  <p className="font-mono text-sm text-white/70">{o.code}</p>
                  <p className="text-xs text-white/40">{o.userName} · {o.date} · {o.paymentMethod}</p>
                </div>
                <StatusBadge status={orderStatusLabel[o.status]} />
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
                <div className="flex items-center gap-2">
                  {o.status === "PendingApproval" && (
                    <button onClick={() => act(o, "approve")} disabled={busy === o.id} className="flex h-9 items-center gap-1.5 rounded-lg bg-sky-500/15 px-4 text-xs font-bold text-sky-400 transition hover:bg-sky-500/25">
                      {busy === o.id ? <Spinner /> : <><AdminIcon name="check" className="h-4 w-4" /> تأیید نهایی (آماده‌سازی)</>}
                    </button>
                  )}
                  {o.status === "Preparing" && (
                    <button onClick={() => openDeliver(o)} className="flex h-9 items-center gap-1.5 rounded-lg bg-emerald-500/15 px-4 text-xs font-bold text-emerald-400 transition hover:bg-emerald-500/25">
                      <AdminIcon name="check" className="h-4 w-4" /> تکمیل و تحویل
                    </button>
                  )}
                  {o.status === "Completed" && (
                    <button onClick={() => openDeliver(o)} className="flex h-9 items-center gap-1.5 rounded-lg border border-white/10 px-4 text-xs font-bold text-white/70 transition hover:bg-white/5">
                      <AdminIcon name="edit" className="h-4 w-4" /> {o.deliveryContent ? "ویرایش/ارسال مجدد" : "ثبت تحویل"}
                    </button>
                  )}
                  {(o.status === "PendingApproval" || o.status === "Preparing") && (
                    <button onClick={() => act(o, "cancel")} disabled={busy === o.id} className="flex h-9 items-center gap-1.5 rounded-lg bg-rose-500/15 px-4 text-xs font-bold text-rose-400 transition hover:bg-rose-500/25">
                      <AdminIcon name="close" className="h-4 w-4" /> لغو
                    </button>
                  )}
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      <Modal open={deliverOrder !== null} onClose={() => setDeliverOrder(null)} title={`تحویل سفارش ${deliverOrder?.code ?? ""}`} size="xl">
        <div className="grid gap-5">
          <Field label="محتوای تحویل (در حساب کاربر نمایش داده می‌شود)">
            <textarea
              rows={5}
              value={content}
              onChange={(e) => setContent(e.target.value)}
              placeholder="مثلاً ایمیل و رمز اکانت، کد فعال‌سازی، یا توضیحات سرویس..."
              className={`${inputCls} h-auto py-3 font-mono`}
            />
          </Field>

          <div className="rounded-xl border border-white/8 p-4">
            <label className="flex cursor-pointer items-center justify-between">
              <span className="text-sm font-bold text-white">ارسال ایمیل به کاربر</span>
              <Toggle checked={sendEmail} onChange={setSendEmail} />
            </label>
            {sendEmail && (
              <div className="mt-4 grid gap-4">
                <Field label="موضوع ایمیل">
                  <input value={emailSubject} onChange={(e) => setEmailSubject(e.target.value)} className={inputCls} />
                </Field>
                <Field label="متن ایمیل">
                  <textarea rows={3} value={emailBody} onChange={(e) => setEmailBody(e.target.value)} className={`${inputCls} h-auto py-3`} />
                </Field>
                <p className="text-xs text-amber-300/80">توجه: تا زمانی که سرویس ایمیل (SMTP) تنظیم نشده، ایمیل ارسال نمی‌شود و فقط در لاگ سرور ثبت می‌گردد.</p>
              </div>
            )}
          </div>

          <div className="flex gap-3">
            <button onClick={doDeliver} disabled={delivering} className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60">
              {delivering ? <Spinner /> : "ثبت تحویل"}
            </button>
            <button onClick={() => setDeliverOrder(null)} className="h-11 rounded-xl border border-white/10 px-8 text-sm font-bold text-white/80 transition hover:bg-white/5">انصراف</button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
