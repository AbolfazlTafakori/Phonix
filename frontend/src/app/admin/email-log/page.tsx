"use client";

import { Suspense, useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { SentEmail, SentEmailPage } from "@/lib/types";
import { toFa } from "@/lib/format";
import { Card, PageHeader, Spinner, DataTable, inputCls, type Column } from "@/components/admin/ui";
import { Pagination } from "@/components/admin/Pagination";

const PAGE_SIZE = 20;

type StatusFilter = "" | "sent" | "failed";

// The record of what the shop has sent. `info@` is send-only — nothing is delivered back to it — so when a
// customer says "I never got my account", this page is the only place that can answer whether the email
// actually left the server, and if not, why.
//
// Bodies are deliberately absent: delivery emails carry live credentials, and a second copy of those would be
// a second thing to protect. Recipient, subject and outcome answer the operational question on their own.
function EmailLogView() {
  const [data, setData] = useState<SentEmailPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<StatusFilter>("");
  const [page, setPage] = useState(1);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      setData(await api.emailLog.list({
        search: search.trim() || undefined,
        status: status || undefined,
        page,
        pageSize: PAGE_SIZE,
      }));
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری");
    } finally {
      setLoading(false);
    }
  }, [search, status, page]);

  useEffect(() => {
    load();
  }, [load]);

  // Any filter change restarts paging, otherwise page 3 of a filtered set can land past the end.
  function applyFilter(next: () => void) {
    setPage(1);
    next();
  }

  const columns: Column<SentEmail>[] = [
    {
      header: "گیرنده",
      primary: true,
      cell: (e) => (
        <span dir="ltr" className="font-mono text-xs text-white" style={{ unicodeBidi: "isolate" }}>
          {e.to || "—"}
        </span>
      ),
    },
    { header: "موضوع", td: "text-white/70", cell: (e) => e.subject || "—" },
    {
      header: "وضعیت",
      cell: (e) => (
        <span
          className={`rounded-md px-2 py-0.5 text-[11px] font-bold ${
            e.success ? "bg-emerald-500/15 text-emerald-400" : "bg-rose-500/15 text-rose-400"
          }`}
        >
          {e.success ? "ارسال شد" : "ناموفق"}
        </span>
      ),
    },
    {
      header: "علت خطا",
      td: "text-white/50",
      cell: (e) => (e.error ? <span className="text-xs">{e.error}</span> : <span className="text-white/25">—</span>),
    },
    {
      header: "زمان",
      td: "text-white/50",
      cell: (e) => (
        <span dir="ltr" className="text-xs" style={{ unicodeBidi: "isolate" }}>
          {new Date(e.sentAt).toLocaleString("fa-IR")}
        </span>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="ایمیل‌های ارسال‌شده"
        desc="سابقه‌ی ایمیل‌هایی که سایت فرستاده است — برای پاسخ به «ایمیل من نرسید»"
      />

      <Card className="overflow-hidden">
        <div className="flex flex-wrap items-center gap-3 border-b border-white/8 p-4">
          <input
            value={search}
            onChange={(e) => applyFilter(() => setSearch(e.target.value))}
            placeholder="جست‌وجو در گیرنده یا موضوع…"
            className={`${inputCls} max-w-xs`}
          />
          <div className="flex items-center gap-1.5">
            {([
              ["", "همه"],
              ["failed", "ناموفق"],
              ["sent", "ارسال‌شده"],
            ] as [StatusFilter, string][]).map(([value, label]) => (
              <button
                key={value || "all"}
                onClick={() => applyFilter(() => setStatus(value))}
                className={`rounded-lg px-3 py-1.5 text-xs font-bold transition ${
                  status === value
                    ? "bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-white"
                    : "border border-white/15 text-white/70 hover:bg-white/10"
                }`}
              >
                {label}
              </button>
            ))}
          </div>

          {/* The failure count is the number worth surfacing — a working mail setup shows zero here. */}
          {data && data.failed > 0 && (
            <span className="mr-auto rounded-lg bg-rose-500/15 px-3 py-1.5 text-xs font-bold text-rose-400">
              {toFa(data.failed)} ارسال ناموفق
            </span>
          )}
        </div>

        {loading ? (
          <div className="grid place-items-center py-24">
            <Spinner className="h-8 w-8" />
          </div>
        ) : error ? (
          <p className="p-8 text-center text-sm text-rose-400">{error}</p>
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={data?.items ?? []}
              rowKey={(e) => e.id}
              minWidth={860}
              empty="هنوز ایمیلی ارسال نشده است."
            />
            {data && (
              <Pagination
                page={data.page}
                totalPages={data.totalPages}
                total={data.total}
                pageSize={data.pageSize}
                onPage={setPage}
              />
            )}
          </>
        )}
      </Card>
    </div>
  );
}

export default function EmailLogPage() {
  return (
    <Suspense
      fallback={
        <div className="grid place-items-center py-24">
          <Spinner className="h-8 w-8" />
        </div>
      }
    >
      <EmailLogView />
    </Suspense>
  );
}
