"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { BankCard, BankCardStatus } from "@/lib/types";
import { formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, StatusBadge, DataTable, type Column } from "@/components/admin/ui";
import { Pagination, usePaged } from "@/components/admin/Pagination";
import AdminIcon from "@/components/admin/AdminIcon";

const statusLabel: Record<BankCardStatus, string> = { Pending: "در انتظار", Approved: "تایید شده", Rejected: "رد شده" };
type Filter = "all" | BankCardStatus;

function formatCard(raw: string): string {
  const digits = (raw || "").replace(/\D/g, "").slice(0, 16);
  const grouped = digits.replace(/(.{4})/g, "$1-").replace(/-$/, "");
  return grouped.replace(/\d/g, (d) => "۰۱۲۳۴۵۶۷۸۹"[Number(d)]);
}

export default function AdminCardsPage() {
  const [items, setItems] = useState<BankCard[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
  const [busy, setBusy] = useState<number | null>(null);

  useEffect(() => {
    (async () => {
      try {
        setItems(await api.cards.list());
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری کارت‌ها");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const stats = useMemo(
    () => ({
      pending: items.filter((c) => c.status === "Pending").length,
      approved: items.filter((c) => c.status === "Approved").length,
      rejected: items.filter((c) => c.status === "Rejected").length,
    }),
    [items],
  );

  const filtered = useMemo(() => (filter === "all" ? items : items.filter((c) => c.status === filter)), [items, filter]);
  const { page, setPage, totalPages, slice, total, pageSize } = usePaged(filtered, 12);

  async function act(c: BankCard, kind: "approve" | "reject") {
    if (kind === "reject" && !confirm(`کارت «${formatCard(c.cardNumber)}» رد شود؟`)) return;
    setBusy(c.id);
    try {
      const updated = kind === "approve" ? await api.cards.approve(c.id) : await api.cards.reject(c.id);
      setItems((p) => p.map((x) => (x.id === c.id ? updated : x)));
    } finally {
      setBusy(null);
    }
  }

  async function del(c: BankCard) {
    if (!confirm(`کارت «${formatCard(c.cardNumber)}» برای همیشه حذف شود؟`)) return;
    setBusy(c.id);
    try {
      await api.cards.remove(c.id);
      setItems((p) => p.filter((x) => x.id !== c.id));
    } finally {
      setBusy(null);
    }
  }

  const columns: Column<BankCard>[] = [
    { header: "کاربر", primary: true, cell: (c) => c.userName },
    { header: "شماره کارت", td: "font-mono text-white/70", cell: (c) => <span dir="ltr">{formatCard(c.cardNumber)}</span> },
    {
      header: "صاحب کارت",
      td: "text-white/65",
      cell: (c) => (
        <div className="flex items-center gap-2">
          <span>{c.holderName}</span>
          {c.cardImage && (
            <a href={api.cards.imageSrc(c.cardImage)} target="_blank" rel="noreferrer" className="shrink-0 text-xs font-bold text-[#6f93ff] transition hover:underline">
              تصویر
            </a>
          )}
        </div>
      ),
    },
    { header: "بانک", td: "text-white/65", cell: (c) => c.bank || "—" },
    { header: "وضعیت", cell: (c) => <StatusBadge status={statusLabel[c.status]} /> },
    {
      header: "عملیات",
      full: true,
      cell: (c) => (
        <div className="flex items-center gap-2">
          {c.status === "Pending" ? (
            <>
              <button
                onClick={() => act(c, "approve")}
                disabled={busy === c.id}
                className="flex h-10 flex-1 items-center justify-center gap-1.5 rounded-lg bg-emerald-500/15 text-xs font-bold text-emerald-400 transition hover:bg-emerald-500/25 active:scale-[0.98] lg:h-9 lg:flex-none lg:px-4"
              >
                {busy === c.id ? <Spinner /> : <><AdminIcon name="check" className="h-4 w-4" /> تایید</>}
              </button>
              <button
                onClick={() => act(c, "reject")}
                disabled={busy === c.id}
                className="flex h-10 flex-1 items-center justify-center gap-1.5 rounded-lg bg-rose-500/15 text-xs font-bold text-rose-400 transition hover:bg-rose-500/25 active:scale-[0.98] lg:h-9 lg:flex-none lg:px-4"
              >
                <AdminIcon name="close" className="h-4 w-4" /> رد
              </button>
            </>
          ) : (
            <span className="flex-1 text-xs text-white/40 lg:flex-none">{c.status === "Rejected" && c.note ? c.note : c.date}</span>
          )}
          <button
            onClick={() => del(c)}
            disabled={busy === c.id}
            title="حذف کارت"
            className="grid h-10 w-10 shrink-0 place-items-center rounded-lg border border-white/10 text-white/45 transition hover:border-rose-500/50 hover:text-rose-400 disabled:opacity-60 md:h-9 md:w-9"
          >
            <AdminIcon name="trash" className="h-4 w-4" />
          </button>
        </div>
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
      <PageHeader title="کارت‌های بانکی" desc="تأیید یا رد کارت‌های بانکی ثبت‌شده توسط کاربران" />

      <div className="mb-6 grid gap-4 sm:grid-cols-3">
        <Card className="p-5">
          <p className="text-sm text-white/50">در انتظار تأیید</p>
          <p className="mt-2 text-2xl font-bold text-amber-400">{formatNumber(stats.pending)}</p>
        </Card>
        <Card className="p-5">
          <p className="text-sm text-white/50">تأییدشده</p>
          <p className="mt-2 text-2xl font-bold text-emerald-400">{formatNumber(stats.approved)}</p>
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
        <>
          <Card className="overflow-hidden">
            <DataTable columns={columns} rows={slice} rowKey={(c) => c.id} minWidth={720} empty="کارتی یافت نشد" />
          </Card>
          <Pagination page={page} totalPages={totalPages} total={total} pageSize={pageSize} onPage={setPage} />
        </>
      )}
    </div>
  );
}
