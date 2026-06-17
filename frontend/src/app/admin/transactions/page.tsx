"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { Transaction, TxStatus } from "@/lib/types";
import { formatToman, formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, StatusBadge, DataTable, type Column } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

const statusLabel: Record<TxStatus, string> = { Pending: "در انتظار", Approved: "تایید شده", Rejected: "رد شده" };
type Filter = "all" | TxStatus;

export default function AdminTransactionsPage() {
  const [items, setItems] = useState<Transaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
  const [busy, setBusy] = useState<number | null>(null);

  useEffect(() => {
    (async () => {
      try {
        setItems(await api.transactions.list());
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری تراکنش‌ها");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const stats = useMemo(
    () => ({
      pending: items.filter((t) => t.status === "Pending").length,
      approvedSum: items.filter((t) => t.status === "Approved" && t.amount > 0).reduce((s, t) => s + t.amount, 0),
      rejected: items.filter((t) => t.status === "Rejected").length,
    }),
    [items],
  );

  const filtered = useMemo(() => (filter === "all" ? items : items.filter((t) => t.status === filter)), [items, filter]);

  async function act(t: Transaction, kind: "approve" | "reject") {
    setBusy(t.id);
    try {
      const updated = kind === "approve" ? await api.transactions.approve(t.id) : await api.transactions.reject(t.id);
      setItems((p) => p.map((x) => (x.id === t.id ? updated : x)));
    } finally {
      setBusy(null);
    }
  }

  const columns: Column<Transaction>[] = [
    { header: "شناسه", primary: true, td: "font-mono text-white/60", cell: (t) => t.code },
    { header: "کاربر", cell: (t) => t.userName },
    { header: "نوع", td: "text-white/65", cell: (t) => t.type },
    {
      header: "مبلغ",
      cell: (t) => (
        <span className={`font-bold ${t.amount >= 0 ? "text-emerald-400" : "text-rose-400"}`}>
          {t.amount >= 0 ? "+" : "−"}
          {formatToman(Math.abs(t.amount))}
        </span>
      ),
    },
    { header: "روش", td: "text-white/65", cell: (t) => t.method },
    { header: "وضعیت", cell: (t) => <StatusBadge status={statusLabel[t.status]} /> },
    {
      header: "عملیات",
      full: true,
      cell: (t) =>
        t.status === "Pending" ? (
          <div className="flex items-center gap-2">
            <button
              onClick={() => act(t, "approve")}
              disabled={busy === t.id}
              className="flex h-9 flex-1 items-center justify-center gap-1.5 rounded-lg bg-emerald-500/15 text-xs font-bold text-emerald-400 transition hover:bg-emerald-500/25 md:flex-none md:px-4"
            >
              {busy === t.id ? <Spinner /> : <><AdminIcon name="check" className="h-4 w-4" /> تایید</>}
            </button>
            <button
              onClick={() => act(t, "reject")}
              disabled={busy === t.id}
              className="flex h-9 flex-1 items-center justify-center gap-1.5 rounded-lg bg-rose-500/15 text-xs font-bold text-rose-400 transition hover:bg-rose-500/25 md:flex-none md:px-4"
            >
              <AdminIcon name="close" className="h-4 w-4" /> رد
            </button>
          </div>
        ) : (
          <span className="text-xs text-white/40">{t.approvedVia === "telegram" ? "از تلگرام" : "از سایت"}{t.note ? ` · ${t.note}` : ""}</span>
        ),
    },
  ];

  const filters: { key: Filter; label: string }[] = [
    { key: "all", label: "همه" },
    { key: "Pending", label: "در انتظار" },
    { key: "Approved", label: "تایید شده" },
    { key: "Rejected", label: "رد شده" },
  ];

  return (
    <div>
      <PageHeader title="تراکنش‌ها" desc="تأیید یا رد تراکنش‌ها — به‌صورت دستی از سایت یا تلگرام" />

      <div className="mb-6 grid gap-4 sm:grid-cols-3">
        <Card className="p-5">
          <p className="text-sm text-white/50">در انتظار تأیید</p>
          <p className="mt-2 text-2xl font-bold text-amber-400">{formatNumber(stats.pending)}</p>
        </Card>
        <Card className="p-5">
          <p className="text-sm text-white/50">مجموع تأییدشده</p>
          <p className="mt-2 text-2xl font-bold text-emerald-400">{formatToman(stats.approvedSum)}</p>
        </Card>
        <Card className="p-5">
          <p className="text-sm text-white/50">رد شده</p>
          <p className="mt-2 text-2xl font-bold text-rose-400">{formatNumber(stats.rejected)}</p>
        </Card>
      </div>

      <div className="mb-5 flex flex-wrap gap-2">
        {filters.map((f) => (
          <button
            key={f.key}
            onClick={() => setFilter(f.key)}
            className={`rounded-full border px-4 py-1.5 text-sm font-medium transition ${
              filter === f.key ? "border-transparent bg-white/10 text-white" : "border-white/10 text-white/60 hover:text-white"
            }`}
          >
            {f.label}
          </button>
        ))}
      </div>

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : (
        <Card className="overflow-hidden">
          <DataTable columns={columns} rows={filtered} rowKey={(t) => t.id} minWidth={780} empty="تراکنشی یافت نشد" />
        </Card>
      )}
    </div>
  );
}
