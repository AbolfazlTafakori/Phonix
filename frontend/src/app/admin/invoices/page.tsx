"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Order } from "@/lib/types";
import { formatToman, formatNumber, toFa } from "@/lib/format";
import { Card, PageHeader, Spinner, DataTable, inputCls, type Column } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

const PAGE_SIZE = 20;

export default function AdminInvoicesPage() {
  const [rows, setRows] = useState<Order[]>([]);
  const [total, setTotal] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [page, setPage] = useState(1);
  const [query, setQuery] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async (q: string, p: number) => {
    setLoading(true);
    setError("");
    try {
      const res = await api.orders.invoices({ q: q.trim() || undefined, page: p, pageSize: PAGE_SIZE });
      setRows(res.items);
      setTotal(res.total);
      setTotalPages(res.totalPages);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری فاکتورها");
    } finally {
      setLoading(false);
    }
  }, []);

  // Debounced so typing a 16-digit number doesn't fire a request per keystroke.
  useEffect(() => {
    const t = setTimeout(() => load(query, page), 300);
    return () => clearTimeout(t);
  }, [query, page, load]);

  function search(v: string) {
    setQuery(v);
    setPage(1); // a new term always starts from the first page
  }

  const columns: Column<Order>[] = [
    {
      header: "شماره فاکتور",
      primary: true,
      cell: (o) => <span className="font-mono font-bold text-white" dir="ltr">{o.invoiceNumber}</span>,
    },
    { header: "شماره سفارش", td: "text-white/65", cell: (o) => <span className="font-mono" dir="ltr">{o.code}</span> },
    { header: "مشتری", td: "text-white/80", cell: (o) => o.userName },
    { header: "تاریخ تحویل", td: "text-white/65", cell: (o) => o.deliveredAt || o.date },
    { header: "اقلام", td: "text-white/65", cell: (o) => `${toFa(o.items.reduce((n, i) => n + i.quantity, 0))} قلم` },
    { header: "مبلغ کل", cell: (o) => formatToman(o.total) },
    {
      header: "عملیات",
      full: true,
      cell: (o) => (
        <a
          href={`/invoice?id=${o.id}`}
          target="_blank"
          rel="noreferrer"
          title="مشاهده فاکتور"
          className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-white/60 transition hover:border-[#3a64f2]/50 hover:text-[#6f93ff]"
        >
          <AdminIcon name="news" className="h-4 w-4" />
        </a>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="مدیریت فاکتورها"
        desc={`${formatNumber(total)} فاکتور صادرشده`}
      />

      <Card className="mb-4">
        <div className="p-4">
          <div className="relative">
            <span className="pointer-events-none absolute right-3.5 top-1/2 -translate-y-1/2 text-white/35">
              <AdminIcon name="search" className="h-4 w-4" />
            </span>
            <input
              value={query}
              onChange={(e) => search(e.target.value)}
              placeholder="جستجوی شماره فاکتور، شماره سفارش یا نام مشتری..."
              className={`${inputCls} pr-10`}
            />
          </div>
          <p className="mt-2 text-xs text-white/40">
            فاکتور فقط برای سفارش‌های تکمیل‌شده صادر می‌شود؛ شماره فاکتور ۱۶ رقمی و یکتاست.
          </p>
        </div>
      </Card>

      <Card>
        {loading ? (
          <div className="grid place-items-center py-16"><Spinner /></div>
        ) : error ? (
          <p className="px-6 py-16 text-center text-sm text-rose-400">{error}</p>
        ) : (
          <DataTable
            columns={columns}
            rows={rows}
            rowKey={(o) => o.id}
            empty={query.trim() ? "فاکتوری با این مشخصات یافت نشد" : "هنوز فاکتوری صادر نشده است"}
          />
        )}
      </Card>

      {totalPages > 1 && !loading && (
        <div className="mt-4 flex items-center justify-center gap-3">
          <button
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page <= 1}
            className="h-9 rounded-lg border border-white/10 px-4 text-sm font-bold text-white/80 transition hover:bg-white/5 disabled:opacity-40"
          >
            قبلی
          </button>
          <span className="text-sm text-white/60">صفحه {toFa(page)} از {toFa(totalPages)}</span>
          <button
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page >= totalPages}
            className="h-9 rounded-lg border border-white/10 px-4 text-sm font-bold text-white/80 transition hover:bg-white/5 disabled:opacity-40"
          >
            بعدی
          </button>
        </div>
      )}
    </div>
  );
}
