"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { StockItem, StockItemStatus, StockSummary } from "@/lib/types";
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

  async function refreshSummary() {
    setRows(await api.stock.summary());
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
          <div className="border-b border-white/8 p-4">
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="جستجوی محصول..."
              className={`${inputCls} max-w-xs`}
            />
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
                    <td className="p-4 text-left">
                      <button
                        onClick={() => open(r)}
                        className="rounded-lg border border-white/15 px-4 py-2 text-xs font-bold text-white/80 transition hover:bg-white/10"
                      >
                        مدیریت انبار
                      </button>
                    </td>
                  </tr>
                ))}
                {filtered.length === 0 && (
                  <tr><td colSpan={7} className="p-10 text-center text-white/40">محصولی یافت نشد</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </Card>
      )}

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
