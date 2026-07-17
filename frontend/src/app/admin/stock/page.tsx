"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { StockAccount, StockItem, StockItemStatus, StockSummary } from "@/lib/types";
import { toFa } from "@/lib/format";
import { Card, PageHeader, Spinner, Modal, Field, Toggle, inputCls } from "@/components/admin/ui";

const statusMeta: Record<StockItemStatus, { label: string; cls: string }> = {
  Available: { label: "موجود", cls: "bg-emerald-500/15 text-emerald-400" },
  Reserved: { label: "رزرو شده", cls: "bg-amber-500/15 text-amber-300" },
  Delivered: { label: "تحویل شده", cls: "bg-sky-500/15 text-sky-300" },
  Disabled: { label: "غیرفعال", cls: "bg-white/10 text-white/50" },
};

// Virtual stock pool: pre-load ready-to-deliver items (credentials, gift codes, licenses) per product; the
// fulfillment flow pulls them manually, or automatically when the product's switch is on.
export default function AdminStockPage() {
  const [rows, setRows] = useState<StockSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [search, setSearch] = useState("");

  // opened product pool
  const [target, setTarget] = useState<StockSummary | null>(null);
  const [items, setItems] = useState<StockItem[]>([]);
  const [itemsLoading, setItemsLoading] = useState(false);
  const [bulk, setBulk] = useState("");
  const [busy, setBusy] = useState(false);
  const [revealed, setRevealed] = useState<Record<number, string>>({});
  const [modalError, setModalError] = useState("");

  const [reformatBusy, setReformatBusy] = useState(false);
  const [reformatDone, setReformatDone] = useState<string | null>(null);

  async function refreshSummary() {
    setRows(await api.stock.summary());
  }

  async function reformatDeliveries() {
    setReformatBusy(true);
    setReformatDone(null);
    try {
      const { updated } = await api.stock.reformatDeliveries();
      setReformatDone(`${toFa(updated)} سفارش به‌روزرسانی شد ✓`);
      setTimeout(() => setReformatDone(null), 4000);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در به‌روزرسانی فرمت");
    } finally {
      setReformatBusy(false);
    }
  }

  useEffect(() => {
    (async () => {
      try {
        await refreshSummary();
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const filtered = rows.filter((r) => !search.trim() || r.name.includes(search.trim()));

  async function open(row: StockSummary) {
    setTarget(row);
    setBulk("");
    setRevealed({});
    setModalError("");
    setItemsLoading(true);
    try {
      setItems(await api.stock.items(row.productId));
    } catch (e) {
      setModalError(e instanceof Error ? e.message : "خطا در بارگذاری آیتم‌ها");
    } finally {
      setItemsLoading(false);
    }
  }

  async function refreshItems(productId: number) {
    setItems(await api.stock.items(productId));
    await refreshSummary();
  }

  async function addBulk() {
    if (!target) return;
    const lines = bulk.split("\n").map((l) => l.trim()).filter(Boolean);
    if (lines.length === 0) return;
    setBusy(true);
    setModalError("");
    try {
      await api.stock.add(target.productId, lines);
      setBulk("");
      await refreshItems(target.productId);
    } catch (e) {
      setModalError(e instanceof Error ? e.message : "خطا در افزودن آیتم‌ها");
    } finally {
      setBusy(false);
    }
  }

  async function act(fn: () => Promise<void>) {
    if (!target) return;
    setBusy(true);
    setModalError("");
    try {
      await fn();
      await refreshItems(target.productId);
    } catch (e) {
      setModalError(e instanceof Error ? e.message : "خطا در انجام عملیات");
    } finally {
      setBusy(false);
    }
  }

  async function reveal(id: number) {
    if (revealed[id] !== undefined) {
      setRevealed((r) => {
        const next = { ...r };
        delete next[id];
        return next;
      });
      return;
    }
    try {
      const { content } = await api.stock.content(id);
      setRevealed((r) => ({ ...r, [id]: content }));
    } catch (e) {
      setModalError(e instanceof Error ? e.message : "خطا در نمایش محتوا");
    }
  }

  async function toggleAuto(row: StockSummary, enabled: boolean) {
    // optimistic flip; re-sync from the server either way.
    setRows((p) => p.map((r) => (r.productId === row.productId ? { ...r, autoDeliver: enabled } : r)));
    try {
      await api.stock.autoDeliver(row.productId, enabled);
    } finally {
      await refreshSummary();
    }
  }

  async function toggleSlots(row: StockSummary, enabled: boolean) {
    setRows((p) => p.map((r) => (r.productId === row.productId ? { ...r, slotFulfillment: enabled } : r)));
    try {
      await api.stock.slotFulfillment(row.productId, enabled);
    } finally {
      await refreshSummary();
    }
  }

  // ── Multi-user (slot) accounts ──────────────────────────────────────────────────────────────────
  const [accTarget, setAccTarget] = useState<StockSummary | null>(null);
  const [accounts, setAccounts] = useState<StockAccount[]>([]);
  const [accLoading, setAccLoading] = useState(false);
  const [accBusy, setAccBusy] = useState(false);
  const [accError, setAccError] = useState("");
  const [accRevealed, setAccRevealed] = useState<Record<number, string>>({});
  const emptyForm = { username: "", password: "", plan: "", planType: "", capacity: "10", months: "1" };
  const [form, setForm] = useState(emptyForm);
  const [serviceName, setServiceName] = useState("");
  const [serviceBusy, setServiceBusy] = useState(false);

  async function openAccounts(row: StockSummary) {
    setAccTarget(row);
    setForm({ ...emptyForm, planType: row.planTypes[0] ?? "" });
    setServiceName(row.serviceName);
    setAccRevealed({});
    setAccError("");
    setAccLoading(true);
    try {
      setAccounts(await api.stock.accounts(row.productId));
    } catch (e) {
      setAccError(e instanceof Error ? e.message : "خطا در بارگذاری اکانت‌ها");
    } finally {
      setAccLoading(false);
    }
  }

  async function saveServiceName() {
    if (!accTarget) return;
    setServiceBusy(true);
    setAccError("");
    try {
      await api.stock.serviceName(accTarget.productId, serviceName.trim());
      await refreshSummary();
    } catch (e) {
      setAccError(e instanceof Error ? e.message : "خطا در ذخیره‌ی اسم سرویس");
    } finally {
      setServiceBusy(false);
    }
  }

  async function accAct(fn: () => Promise<unknown>) {
    if (!accTarget) return;
    setAccBusy(true);
    setAccError("");
    try {
      await fn();
      setAccounts(await api.stock.accounts(accTarget.productId));
      await refreshSummary();
    } catch (e) {
      setAccError(e instanceof Error ? e.message : "خطا در انجام عملیات");
    } finally {
      setAccBusy(false);
    }
  }

  function addAccount() {
    if (!accTarget) return;
    return accAct(async () => {
      await api.stock.addAccount({
        productId: accTarget.productId,
        username: form.username.trim(),
        password: form.password,
        plan: form.plan.trim(),
        planType: form.planType,
        capacity: Number(form.capacity),
        months: Number(form.months),
      });
      setForm(emptyForm);
    });
  }

  async function revealAccount(id: number) {
    if (accRevealed[id] !== undefined) {
      setAccRevealed((r) => {
        const next = { ...r };
        delete next[id];
        return next;
      });
      return;
    }
    try {
      const { password } = await api.stock.accountContent(id);
      setAccRevealed((r) => ({ ...r, [id]: password }));
    } catch (e) {
      setAccError(e instanceof Error ? e.message : "خطا در نمایش گذرواژه");
    }
  }

  return (
    <div>
      <PageHeader
        title="انبار مجازی / استخر اکانت"
        desc="آیتم‌های آماده‌ی تحویل (اکانت، گیفت‌کد، لایسنس) را از قبل وارد کنید؛ تحویل دستی یا خودکار از همین استخر انجام می‌شود"
      />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : (
        <Card className="overflow-hidden">
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-white/8 p-4">
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="جستجوی محصول..."
              className={`${inputCls} max-w-xs`}
            />
            <button
              onClick={reformatDeliveries}
              disabled={reformatBusy}
              title="محتوای تحویل اکانت‌های ظرفیتیِ سفارش‌های قبلی را با فرمت جدید بازنویسی می‌کند"
              className="rounded-lg border border-white/15 px-4 py-2 text-xs font-bold text-white/80 transition hover:bg-white/10 disabled:opacity-50"
            >
              {reformatBusy ? "..." : reformatDone ?? "به‌روزرسانی فرمت سفارش‌های قبلی"}
            </button>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-white/8 text-right text-xs text-white/45">
                  <th className="p-4 font-medium">محصول</th>
                  <th className="p-4 font-medium">موجود</th>
                  <th className="p-4 font-medium">رزرو شده</th>
                  <th className="p-4 font-medium">تحویل شده</th>
                  <th className="p-4 font-medium">غیرفعال</th>
                  <th className="p-4 font-medium">تحویل خودکار</th>
                  <th className="p-4 font-medium">اکانت ظرفیتی</th>
                  <th className="p-4" />
                </tr>
              </thead>
              <tbody>
                {filtered.map((r) => (
                  <tr key={r.productId} className="border-b border-white/5 last:border-0">
                    <td className="p-4">
                      <span className="flex items-center gap-3">
                        <img src={r.image} alt={r.name} className="h-9 w-9 rounded-lg object-cover" />
                        <span className="font-bold text-white">{r.name}</span>
                      </span>
                    </td>
                    <td className={`p-4 font-bold ${r.available > 0 ? "text-emerald-400" : "text-rose-400"}`}>{toFa(r.available)}</td>
                    <td className="p-4 text-amber-300">{toFa(r.reserved)}</td>
                    <td className="p-4 text-sky-300">{toFa(r.delivered)}</td>
                    <td className="p-4 text-white/40">{toFa(r.disabled)}</td>
                    <td className="p-4">
                      <Toggle checked={r.autoDeliver} onChange={(v) => toggleAuto(r, v)} />
                    </td>
                    <td className="p-4">
                      <span className="flex items-center gap-2">
                        <Toggle checked={r.slotFulfillment} onChange={(v) => toggleSlots(r, v)} />
                        {r.accounts > 0 && (
                          <span className="text-[11px] text-white/40">
                            {toFa(r.accounts)} اکانت · {toFa(r.slotAvailable)} جای خالی
                          </span>
                        )}
                      </span>
                    </td>
                    <td className="p-4 text-left">
                      <span className="flex justify-end gap-1.5">
                        <button
                          onClick={() => openAccounts(r)}
                          className="rounded-lg border border-white/15 px-4 py-2 text-xs font-bold text-white/80 transition hover:bg-white/10"
                        >
                          اکانت‌ها
                        </button>
                        <button
                          onClick={() => open(r)}
                          className="rounded-lg border border-white/15 px-4 py-2 text-xs font-bold text-white/80 transition hover:bg-white/10"
                        >
                          مدیریت انبار
                        </button>
                      </span>
                    </td>
                  </tr>
                ))}
                {filtered.length === 0 && (
                  <tr><td colSpan={8} className="p-10 text-center text-white/40">محصولی یافت نشد</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      <Modal
        open={accTarget !== null}
        onClose={() => !accBusy && setAccTarget(null)}
        title={accTarget ? `اکانت‌های ظرفیتی «${accTarget.name}»` : ""}
      >
        {accTarget && (
          <div className="space-y-5">
            {/* Bare service name printed on the delivery message (blank → auto from the product name). */}
            <div className="rounded-xl border border-white/10 bg-white/[0.02] p-3">
              <Field label="اسم سرویس در پیام تحویل">
                <div className="flex gap-2">
                  <input value={serviceName} onChange={(e) => setServiceName(e.target.value)}
                    dir="ltr" className={inputCls} placeholder="مثلاً VYPRVPN (خالی = خودکار از اسم محصول)" />
                  <button onClick={saveServiceName} disabled={serviceBusy || serviceName.trim() === accTarget.serviceName}
                    className="shrink-0 rounded-xl border border-white/15 px-4 text-sm font-bold text-white/80 transition hover:bg-white/10 disabled:opacity-40">
                    {serviceBusy ? "..." : "ذخیره"}
                  </button>
                </div>
              </Field>
            </div>

            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
              <Field label="نام کاربری اکانت">
                <input value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })}
                  dir="ltr" className={inputCls} placeholder="user@mail.com" />
              </Field>
              <Field label="گذرواژه">
                <input value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })}
                  dir="ltr" className={inputCls} />
              </Field>
              <Field label="پلن">
                <input value={form.plan} onChange={(e) => setForm({ ...form, plan: e.target.value })}
                  dir="ltr" className={inputCls} placeholder="Premium" />
              </Field>
              {accTarget.planTypes.length > 0 && (
                <Field label="نوع پلن (مسیر تحویل)">
                  <select value={form.planType} onChange={(e) => setForm({ ...form, planType: e.target.value })}
                    className={inputCls}>
                    <option value="">همه‌ی انواع</option>
                    {accTarget.planTypes.map((t) => <option key={t} value={t}>{t}</option>)}
                  </select>
                </Field>
              )}
              <Field label="ظرفیت (تعداد کاربر)">
                <input value={form.capacity} onChange={(e) => setForm({ ...form, capacity: e.target.value })}
                  type="number" min={1} dir="ltr" className={inputCls} />
              </Field>
              <Field label="مدت اشتراک (ماه)">
                <input value={form.months} onChange={(e) => setForm({ ...form, months: e.target.value })}
                  type="number" min={1} dir="ltr" className={inputCls} />
              </Field>
            </div>
            <div className="flex items-center justify-between gap-3">
              <p className="text-xs text-white/40">
                جایگاه‌ها (A0، A1، …) پس از ذخیره به‌صورت خودکار برای کل ظرفیت ساخته می‌شوند؛ گذرواژه رمزنگاری‌شده ذخیره می‌شود.
              </p>
              <button
                onClick={addAccount}
                disabled={accBusy || !form.username.trim() || !form.password || Number(form.capacity) < 1}
                className="shrink-0 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50"
              >
                {accBusy ? "..." : "ساخت اکانت"}
              </button>
            </div>

            {accError && <p className="text-sm text-rose-400">{accError}</p>}

            {accLoading ? (
              <div className="grid place-items-center py-10"><Spinner /></div>
            ) : accounts.length === 0 ? (
              <p className="py-6 text-center text-sm text-white/40">هنوز اکانتی برای این محصول ثبت نشده است.</p>
            ) : (
              <div className="max-h-[45vh] space-y-3 overflow-y-auto pl-1">
                {accounts.map((a) => (
                  <div key={a.id} className="rounded-xl border border-white/8 bg-white/[0.02] p-3">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-xs text-white/40">#{toFa(a.id)}</span>
                      <span dir="ltr" className="font-mono text-xs font-bold text-white">{a.username}</span>
                      {a.plan && <span className="rounded-md bg-white/10 px-2 py-0.5 text-[11px] text-white/60">{a.plan}</span>}
                      {a.planType && <span className="rounded-md bg-sky-500/15 px-2 py-0.5 text-[11px] font-bold text-sky-300">{a.planType}</span>}
                      <span className="text-[11px] text-white/40">
                        ظرفیت {toFa(a.capacity)} · {toFa(a.months)} ماهه
                      </span>
                      {a.disabled && (
                        <span className="rounded-md bg-white/10 px-2 py-0.5 text-[11px] font-bold text-white/50">غیرفعال</span>
                      )}
                      <span className="mr-auto flex items-center gap-1.5">
                        <button
                          onClick={() => revealAccount(a.id)}
                          className="rounded-md border border-white/15 px-2.5 py-1 text-[11px] font-bold text-white/70 transition hover:bg-white/10"
                        >
                          {accRevealed[a.id] !== undefined ? "پنهان" : "گذرواژه"}
                        </button>
                        <button
                          onClick={() => accAct(() => (a.disabled ? api.stock.enableAccount(a.id) : api.stock.disableAccount(a.id)))}
                          disabled={accBusy}
                          className="rounded-md border border-white/15 px-2.5 py-1 text-[11px] font-bold text-white/70 transition hover:bg-white/10 disabled:opacity-50"
                        >
                          {a.disabled ? "فعال‌سازی" : "غیرفعال"}
                        </button>
                        {a.slots.every((s) => s.status !== "Delivered") && (
                          <button
                            onClick={() => accAct(() => api.stock.removeAccount(a.id))}
                            disabled={accBusy}
                            className="rounded-md border border-rose-500/30 px-2.5 py-1 text-[11px] font-bold text-rose-400 transition hover:bg-rose-500/10 disabled:opacity-50"
                          >
                            حذف
                          </button>
                        )}
                      </span>
                    </div>
                    {accRevealed[a.id] !== undefined && (
                      <pre dir="ltr" className="mt-2 overflow-x-auto whitespace-pre-wrap rounded-lg border border-white/8 bg-black/30 p-2.5 font-mono text-xs text-white/80">
                        {accRevealed[a.id]}
                      </pre>
                    )}
                    <div dir="ltr" className="mt-2 flex flex-wrap gap-1.5">
                      {a.slots.map((s) => (
                        <button
                          key={s.id}
                          disabled={accBusy}
                          title={
                            s.status === "Reserved" || s.status === "Delivered"
                              ? `${statusMeta[s.status].label} — سفارش #${s.orderId ?? "?"}`
                              : statusMeta[s.status].label
                          }
                          onClick={() => {
                            // one tap cycles the slot through its legal transitions; Delivered is final.
                            if (s.status === "Available") accAct(() => api.stock.slotAction(a.id, s.id, "disable"));
                            else if (s.status === "Disabled") accAct(() => api.stock.slotAction(a.id, s.id, "enable"));
                            else if (s.status === "Reserved") accAct(() => api.stock.slotAction(a.id, s.id, "release"));
                          }}
                          className={`rounded-md px-2 py-1 font-mono text-[11px] font-bold transition ${statusMeta[s.status].cls} ${
                            s.status === "Delivered" ? "cursor-default" : "hover:brightness-125"
                          }`}
                        >
                          {s.label}
                        </button>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </Modal>

      <Modal open={target !== null} onClose={() => !busy && setTarget(null)} title={target ? `انبار «${target.name}»` : ""}>
        {target && (
          <div className="space-y-5">
            <Field label="افزودن آیتم جدید (هر خط = یک آیتم / اکانت)">
              <textarea
                value={bulk}
                onChange={(e) => setBulk(e.target.value)}
                rows={4}
                dir="ltr"
                placeholder={"user1@mail.com : pass123\nGIFT-CODE-XXXX-YYYY"}
                className="w-full rounded-xl border border-white/10 bg-[#0d0d15] p-3 font-mono text-xs text-white outline-none transition focus:border-[#3a64f2]"
              />
            </Field>
            <div className="flex items-center justify-between gap-3">
              <p className="text-xs text-white/40">محتوای آیتم‌ها رمزنگاری‌شده ذخیره می‌شود و فقط با دکمه «نمایش» قابل مشاهده است.</p>
              <button
                onClick={addBulk}
                disabled={busy || !bulk.trim()}
                className="shrink-0 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50"
              >
                {busy ? "..." : "افزودن به انبار"}
              </button>
            </div>

            {modalError && <p className="text-sm text-rose-400">{modalError}</p>}

            {itemsLoading ? (
              <div className="grid place-items-center py-10"><Spinner /></div>
            ) : items.length === 0 ? (
              <p className="py-6 text-center text-sm text-white/40">انبار این محصول خالی است.</p>
            ) : (
              <div className="max-h-[45vh] space-y-2 overflow-y-auto pl-1">
                {items.map((it) => (
                  <div key={it.id} className="rounded-xl border border-white/8 bg-white/[0.02] p-3">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-xs text-white/40">#{toFa(it.id)}</span>
                      <span className={`rounded-md px-2 py-0.5 text-[11px] font-bold ${statusMeta[it.status].cls}`}>
                        {statusMeta[it.status].label}
                      </span>
                      {it.orderId !== null && (
                        <span className="text-[11px] text-white/40">سفارش #{toFa(it.orderId)} · اکانت {toFa(it.unitId ?? 0)}</span>
                      )}
                      {it.addedBy && <span className="text-[11px] text-white/30">افزوده توسط {it.addedBy}</span>}
                      <span className="mr-auto flex items-center gap-1.5">
                        <button
                          onClick={() => reveal(it.id)}
                          className="rounded-md border border-white/15 px-2.5 py-1 text-[11px] font-bold text-white/70 transition hover:bg-white/10"
                        >
                          {revealed[it.id] !== undefined ? "پنهان" : "نمایش"}
                        </button>
                        {it.status === "Available" && (
                          <button onClick={() => act(() => api.stock.disable(it.id))} disabled={busy}
                            className="rounded-md border border-white/15 px-2.5 py-1 text-[11px] font-bold text-white/70 transition hover:bg-white/10 disabled:opacity-50">
                            غیرفعال
                          </button>
                        )}
                        {it.status === "Disabled" && (
                          <button onClick={() => act(() => api.stock.enable(it.id))} disabled={busy}
                            className="rounded-md border border-emerald-500/30 px-2.5 py-1 text-[11px] font-bold text-emerald-400 transition hover:bg-emerald-500/10 disabled:opacity-50">
                            فعال‌سازی
                          </button>
                        )}
                        {it.status === "Reserved" && (
                          <button onClick={() => act(() => api.stock.release(it.id))} disabled={busy}
                            className="rounded-md border border-amber-500/30 px-2.5 py-1 text-[11px] font-bold text-amber-300 transition hover:bg-amber-500/10 disabled:opacity-50">
                            آزادسازی
                          </button>
                        )}
                        {it.status !== "Delivered" && (
                          <button onClick={() => act(() => api.stock.remove(it.id))} disabled={busy}
                            className="rounded-md border border-rose-500/30 px-2.5 py-1 text-[11px] font-bold text-rose-400 transition hover:bg-rose-500/10 disabled:opacity-50">
                            حذف
                          </button>
                        )}
                      </span>
                    </div>
                    {revealed[it.id] !== undefined && (
                      <pre dir="ltr" className="mt-2 overflow-x-auto whitespace-pre-wrap rounded-lg border border-white/8 bg-black/30 p-2.5 font-mono text-xs text-white/80">
                        {revealed[it.id]}
                      </pre>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </Modal>
    </div>
  );
}
