"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Order, OrderUnit } from "@/lib/types";
import { formatToman } from "@/lib/format";
import { Card, PageHeader, Spinner, Modal, Field, Toggle, inputCls } from "@/components/admin/ui";
import { Pagination, usePaged } from "@/components/admin/Pagination";
import AdminIcon from "@/components/admin/AdminIcon";

// Technical team: prepare and deliver each account of a paid order independently, so several admins can work
// the same order in parallel. Each unit has its own content, optional email, and a temporary save.
export default function OrderFulfillmentPage() {
  const [orders, setOrders] = useState<Order[]>([]);
  const [templates, setTemplates] = useState<Record<number, string>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  // active unit being worked on
  const [target, setTarget] = useState<{ order: Order; unit: OrderUnit } | null>(null);
  const [content, setContent] = useState("");
  const [sendEmail, setSendEmail] = useState(true);
  const [emailSubject, setEmailSubject] = useState("");
  const [emailBody, setEmailBody] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const [list, products] = await Promise.all([api.orders.list(), api.products.list()]);
        setOrders(list.filter((o) => o.status === "Preparing"));
        setTemplates(Object.fromEntries(products.map((p) => [p.id, p.deliveryTemplate])));
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const { page, setPage, totalPages, slice, total, pageSize } = usePaged(orders, 8);

  function open(order: Order, unit: OrderUnit) {
    setTarget({ order, unit });
    setContent(unit.deliveryContent?.trim() ? unit.deliveryContent : (templates[unit.productId] ?? "").trim());
    setSendEmail(true);
    setEmailSubject(`سفارش ${order.code} آماده شد`);
    setEmailBody("سرویس شما آماده شد. اطلاعات در حساب کاربری شما (بخش سفارش‌ها) قابل مشاهده است.");
  }

  // apply the order returned by the API: drop it from the list once it's completed (all units delivered).
  function applyOrder(updated: Order) {
    setOrders((p) => (updated.status === "Preparing" ? p.map((o) => (o.id === updated.id ? updated : o)) : p.filter((o) => o.id !== updated.id)));
  }

  async function saveDraft() {
    if (!target) return;
    setBusy(true);
    try {
      applyOrder(await api.orders.saveUnitDraft(target.order.id, target.unit.id, { content }));
      setTarget(null);
    } finally {
      setBusy(false);
    }
  }

  async function deliver() {
    if (!target) return;
    setBusy(true);
    try {
      applyOrder(await api.orders.deliverUnit(target.order.id, target.unit.id, { content, email: sendEmail, emailSubject, emailBody }));
      setTarget(null);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <PageHeader title="تحویل سفارش" desc="آماده‌سازی و تحویل هر اکانت به‌صورت جداگانه — قابل کار همزمان توسط چند نفر" />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : orders.length === 0 ? (
        <Card className="p-12 text-center text-white/40">سفارشی برای تحویل نیست</Card>
      ) : (
        <div className="space-y-4">
          {slice.map((o) => (
            <Card key={o.id} className="p-5">
              <div className="flex flex-wrap items-center justify-between gap-3 border-b border-white/8 pb-3">
                <div>
                  <p className="font-mono text-sm text-white/70">{o.code}</p>
                  <p className="text-xs text-white/40">{o.userName} · {o.date}</p>
                </div>
                <span className="text-xs text-white/45">
                  {o.units.filter((u) => u.delivered).length} از {o.units.length} اکانت تحویل شد
                </span>
              </div>

              <div className="mt-3 grid gap-3 lg:grid-cols-2">
                {o.units.map((u) => (
                  <div key={u.id} className={`rounded-xl border p-3 ${u.delivered ? "border-emerald-500/25 bg-emerald-500/[0.05]" : "border-white/10 bg-white/[0.02]"}`}>
                    <div className="flex items-center justify-between gap-2">
                      <span className="flex items-center gap-2 text-sm font-bold text-white">
                        <img src={u.image} alt={u.name} className="h-7 w-7 rounded object-cover" />
                        {u.name}{o.units.filter((x) => x.productId === u.productId).length > 1 ? ` — اکانت ${u.unitIndex}` : ""}
                      </span>
                      {u.delivered ? (
                        <span className="rounded-md bg-emerald-500/15 px-2 py-0.5 text-[11px] font-bold text-emerald-400">✓ تحویل شد</span>
                      ) : (
                        <span className="rounded-md bg-amber-500/15 px-2 py-0.5 text-[11px] font-bold text-amber-300">در انتظار</span>
                      )}
                    </div>
                    {u.plan && <p className="mt-1 text-[11px] text-white/40">{u.plan}</p>}

                    {(u.customerInputs.length > 0 || u.customerNote) && (
                      <div className="mt-2 space-y-1.5 rounded-md border border-white/8 bg-black/20 p-2">
                        {u.customerInputs.map((ci) => (
                          <div key={ci.label} className="flex items-center justify-between gap-2 text-xs">
                            <span className="shrink-0 text-white/45">{ci.label}:</span>
                            <span className="flex min-w-0 items-center gap-1.5">
                              <span className="truncate font-mono text-white/85" dir="ltr">{ci.value}</span>
                              <button onClick={() => navigator.clipboard?.writeText(ci.value)} className="shrink-0 text-white/40 transition hover:text-white" title="کپی">⧉</button>
                            </span>
                          </div>
                        ))}
                        {u.customerNote && (
                          <p className="border-t border-white/8 pt-1.5 text-xs leading-6 text-white/60"><span className="text-white/40">توضیحات: </span>{u.customerNote}</p>
                        )}
                      </div>
                    )}

                    {u.handledBy && !u.delivered && (
                      <p className="mt-2 text-[11px] text-amber-300/70">پیش‌نویس ذخیره‌شده توسط {u.handledBy}</p>
                    )}

                    <button
                      onClick={() => open(o, u)}
                      className={`mt-3 flex h-9 w-full items-center justify-center gap-1.5 rounded-lg text-xs font-bold transition active:scale-[0.98] ${
                        u.delivered ? "border border-white/10 text-white/70 hover:bg-white/5" : "bg-emerald-500/15 text-emerald-400 hover:bg-emerald-500/25"
                      }`}
                    >
                      <AdminIcon name={u.delivered ? "edit" : "check"} className="h-4 w-4" />
                      {u.delivered ? "ویرایش / ارسال مجدد" : "آماده‌سازی و تحویل"}
                    </button>
                  </div>
                ))}
              </div>

              <div className="mt-4 flex items-center justify-between border-t border-white/8 pt-3">
                <span className="text-sm text-white/60">مبلغ کل</span>
                <span className="font-bold text-emerald-400">{formatToman(o.total)}</span>
              </div>
            </Card>
          ))}
          <Pagination page={page} totalPages={totalPages} total={total} pageSize={pageSize} onPage={setPage} />
        </div>
      )}

      <Modal open={target !== null} onClose={() => setTarget(null)} title={`تحویل ${target?.unit.name ?? ""}${(target && target.order.units.filter((x) => x.productId === target.unit.productId).length > 1) ? ` — اکانت ${target.unit.unitIndex}` : ""}`}>
        <Field label="اطلاعات تحویل (در حساب کاربری مشتری نمایش داده می‌شود)">
          <textarea value={content} onChange={(e) => setContent(e.target.value)} rows={5} className={`${inputCls} resize-none`} placeholder="ایمیل، رمز، لینک دعوت یا هر چیزی که مشتری باید دریافت کند…" />
        </Field>

        <label className="mt-4 flex cursor-pointer items-center justify-between gap-2">
          <span className="text-sm text-white/80">ارسال ایمیل به کاربر</span>
          <Toggle checked={sendEmail} onChange={setSendEmail} />
        </label>
        {sendEmail && (
          <div className="mt-3 space-y-3">
            <Field label="موضوع ایمیل">
              <input value={emailSubject} onChange={(e) => setEmailSubject(e.target.value)} className={inputCls} />
            </Field>
            <Field label="متن ایمیل">
              <textarea value={emailBody} onChange={(e) => setEmailBody(e.target.value)} rows={3} className={`${inputCls} resize-none`} />
            </Field>
          </div>
        )}

        <div className="mt-5 flex flex-col gap-2 sm:flex-row sm:justify-end">
          <button onClick={saveDraft} disabled={busy} className="flex h-10 items-center justify-center gap-1.5 rounded-lg border border-white/12 px-4 text-sm font-bold text-white/75 transition hover:bg-white/5">
            <AdminIcon name="disk" className="h-4 w-4" /> سیو موقت
          </button>
          <button onClick={deliver} disabled={busy} className="flex h-10 items-center justify-center gap-1.5 rounded-lg bg-emerald-500/20 px-5 text-sm font-bold text-emerald-300 transition hover:bg-emerald-500/30">
            {busy ? <Spinner /> : <><AdminIcon name="check" className="h-4 w-4" /> تحویل این اکانت</>}
          </button>
        </div>
        <p className="mt-3 text-[11px] leading-6 text-white/40">«سیو موقت» اطلاعات را نگه می‌دارد و سفارش در همین لیست می‌ماند. «تحویل این اکانت» آن را تحویل‌شده ثبت می‌کند؛ وقتی همه‌ی اکانت‌ها تحویل شدند سفارش به «تکمیل‌شده» می‌رود.</p>
      </Modal>
    </div>
  );
}
