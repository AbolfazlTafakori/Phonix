"use client";

import { Suspense, useCallback, useEffect, useMemo, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { api } from "@/lib/api";
import type { AuditAction, AuditLog, AuditLogPage } from "@/lib/types";
import { toFa } from "@/lib/format";
import { Card, PageHeader, Spinner, DataTable, type Column } from "@/components/admin/ui";
import { Pagination } from "@/components/admin/Pagination";

const PAGE_SIZE = 20;

// Action → badge palette + Persian label. Create=green, Update=amber, Delete=red (per the design brief).
const actionMeta: Record<AuditAction, { label: string; cls: string }> = {
  Create: { label: "ایجاد", cls: "bg-emerald-500/15 text-emerald-400" },
  Update: { label: "ویرایش", cls: "bg-amber-500/15 text-amber-400" },
  Delete: { label: "حذف", cls: "bg-rose-500/15 text-rose-400" },
  Other: { label: "سایر", cls: "bg-white/10 text-white/60" },
};

// Best-effort Persian names for the resource segment; unknown entities fall back to the raw value.
const entityLabels: Record<string, string> = {
  products: "محصولات",
  "plan-types": "نوع سرویس",
  plans: "پلن‌ها",
  categories: "دسته‌بندی‌ها",
  orders: "سفارش‌ها",
  transactions: "تراکنش‌ها",
  cards: "کارت بانکی",
  kyc: "احراز هویت",
  users: "کاربران",
  staff: "کارکنان",
  tickets: "تیکت‌ها",
  comments: "نظرات",
  discounts: "کدهای تخفیف",
  notifications: "اعلان‌ها",
  content: "محتوای سایت",
  banners: "اسلایدر",
  payments: "روش‌های پرداخت",
  "email-settings": "تنظیمات ایمیل",
  backup: "پشتیبان‌گیری",
  chat: "گفتگوی زنده",
  "server-status": "وضعیت سرور",
  twofactor: "ورود دو‌مرحله‌ای",
  account: "حساب کاربری",
};

const actionOptions: { value: "" | AuditAction; label: string }[] = [
  { value: "", label: "همه فعالیت‌ها" },
  { value: "Create", label: "ایجاد" },
  { value: "Update", label: "ویرایش" },
  { value: "Delete", label: "حذف" },
  { value: "Other", label: "سایر" },
];

const KNOWN_ACTIONS: readonly AuditAction[] = ["Create", "Update", "Delete", "Other"];

function parseAction(value: string | null): "" | AuditAction {
  return value && (KNOWN_ACTIONS as readonly string[]).includes(value) ? (value as AuditAction) : "";
}

function parsePage(value: string | null): number {
  const n = Number(value);
  return Number.isFinite(n) && n >= 1 ? Math.floor(n) : 1;
}

// fa-IR uses the Jalali (Persian) calendar; pin it explicitly so every browser/engine agrees on the output.
const fmtDate = new Intl.DateTimeFormat("fa-IR", {
  calendar: "persian",
  dateStyle: "short",
  timeStyle: "short",
});

const selectCls =
  "h-11 rounded-xl border border-white/10 bg-[#0d0d15] px-3 text-sm text-white outline-none transition focus:border-[#e60053]";
const inputCls =
  "h-11 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition placeholder:text-white/35 focus:border-[#e60053]";

function ActionBadge({ action }: { action: AuditAction }) {
  const m = actionMeta[action] ?? actionMeta.Other;
  return <span className={`inline-block rounded-md px-2.5 py-1 text-xs font-bold ${m.cls}`}>{m.label}</span>;
}

function AuditLogsView() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  // Initialise every filter from the URL so a refresh, deep-link, or share restores the exact view.
  const [searchInput, setSearchInput] = useState(() => searchParams.get("search") ?? "");
  const [search, setSearch] = useState(() => searchParams.get("search") ?? "");
  const [action, setAction] = useState<"" | AuditAction>(() => parseAction(searchParams.get("action")));
  const [from, setFrom] = useState(() => searchParams.get("from") ?? "");
  const [to, setTo] = useState(() => searchParams.get("to") ?? "");
  const [page, setPage] = useState(() => parsePage(searchParams.get("page")));

  const [data, setData] = useState<AuditLogPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  // Debounce free-text search so typing doesn't fire a request per keystroke; any new term resets to page 1.
  useEffect(() => {
    const id = setTimeout(() => {
      setSearch(searchInput.trim());
      setPage(1);
    }, 400);
    return () => clearTimeout(id);
  }, [searchInput]);

  // Reactive query-parameter sync: reflect the committed filters back into the URL (one-way, state → URL),
  // so the address bar always describes what's on screen without ever feeding back into a render loop.
  useEffect(() => {
    const params = new URLSearchParams();
    if (search) params.set("search", search);
    if (action) params.set("action", action);
    if (from) params.set("from", from);
    if (to) params.set("to", to);
    if (page > 1) params.set("page", String(page));
    const query = params.toString();
    router.replace(query ? `${pathname}?${query}` : pathname, { scroll: false });
  }, [search, action, from, to, page, pathname, router]);

  // Server-side fetch: re-runs whenever a filter or the page changes.
  useEffect(() => {
    let activeReq = true;
    setLoading(true);
    api.auditLogs
      .list({
        search: search || undefined,
        action: action || undefined,
        from: from || undefined,
        to: to || undefined,
        page,
        pageSize: PAGE_SIZE,
      })
      .then((res) => {
        if (!activeReq) return;
        setData(res);
        setError("");
      })
      .catch((e: unknown) => {
        if (activeReq) setError(e instanceof Error ? e.message : "خطا در بارگذاری لاگ‌ها");
      })
      .finally(() => {
        if (activeReq) setLoading(false);
      });
    return () => {
      activeReq = false;
    };
  }, [search, action, from, to, page]);

  const onFilter = useCallback((fn: () => void) => {
    fn();
    setPage(1);
  }, []);

  const columns: Column<AuditLog>[] = useMemo(
    () => [
      {
        header: "فعالیت",
        primary: true,
        cell: (l) => (
          <div className="flex items-center gap-2">
            <ActionBadge action={l.actionType} />
            <span className="font-medium text-white/90">{entityLabels[l.entity] ?? l.entity}</span>
            {l.entityId && <span className="font-mono text-xs text-white/40">#{toFa(l.entityId)}</span>}
          </div>
        ),
      },
      {
        header: "کاربر",
        cell: (l) => (
          <div className="min-w-0">
            <p className="truncate text-white/85">{l.actorName || "—"}</p>
            <p className="text-xs text-white/40">
              {l.actorRole === "Admin" ? "مدیر" : "پشتیبان"}
              {l.actorId != null && <> · #{toFa(l.actorId)}</>}
            </p>
          </div>
        ),
      },
      {
        header: "مسیر",
        cell: (l) => (
          <span dir="ltr" className="block text-left font-mono text-xs text-white/55">
            <span className="text-white/40">{l.method}</span> {l.path}
          </span>
        ),
      },
      {
        header: "IP",
        cell: (l) => <span dir="ltr" className="block text-left font-mono text-xs text-white/55">{l.ip || "—"}</span>,
      },
      {
        header: "وضعیت",
        cell: (l) => (
          <span
            className={`inline-block rounded-md px-2 py-0.5 text-xs font-bold ${
              l.success ? "bg-emerald-500/15 text-emerald-400" : "bg-rose-500/15 text-rose-400"
            }`}
          >
            {toFa(l.statusCode)}
          </span>
        ),
      },
      {
        header: "زمان",
        cell: (l) => <span className="whitespace-nowrap text-xs text-white/60">{fmtDate.format(new Date(l.timestamp))}</span>,
      },
    ],
    [],
  );

  return (
    <div>
      <PageHeader title="لاگ‌های ممیزی سیستم" desc="ثبت خودکار همه تغییرات انجام‌شده توسط کارکنان پنل مدیریت" />

      <Card className="mb-6 p-4">
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="جستجو در کاربر، بخش، مسیر یا IP…"
            className={inputCls}
          />
          <select
            value={action}
            onChange={(e) => onFilter(() => setAction(e.target.value as "" | AuditAction))}
            className={selectCls}
          >
            {actionOptions.map((o) => (
              <option key={o.value} value={o.value} className="bg-[#15151f]">
                {o.label}
              </option>
            ))}
          </select>
          <label className="flex items-center gap-2 rounded-xl border border-white/10 bg-[#0d0d15] px-3">
            <span className="shrink-0 text-xs text-white/40">از</span>
            <input
              type="date"
              value={from}
              onChange={(e) => onFilter(() => setFrom(e.target.value))}
              dir="ltr"
              className="h-11 w-full bg-transparent text-left text-sm text-white outline-none [color-scheme:dark]"
            />
          </label>
          <label className="flex items-center gap-2 rounded-xl border border-white/10 bg-[#0d0d15] px-3">
            <span className="shrink-0 text-xs text-white/40">تا</span>
            <input
              type="date"
              value={to}
              onChange={(e) => onFilter(() => setTo(e.target.value))}
              dir="ltr"
              className="h-11 w-full bg-transparent text-left text-sm text-white outline-none [color-scheme:dark]"
            />
          </label>
        </div>
      </Card>

      <Card className="overflow-hidden">
        {loading && !data ? (
          <div className="grid place-items-center py-24">
            <Spinner className="h-8 w-8" />
          </div>
        ) : error ? (
          <p className="px-6 py-16 text-center text-sm text-rose-400">{error}</p>
        ) : (
          <div className={loading ? "opacity-60 transition-opacity" : "transition-opacity"}>
            <DataTable
              columns={columns}
              rows={data?.items ?? []}
              rowKey={(l) => l.id}
              minWidth={920}
              empty="هیچ فعالیتی مطابق فیلترها ثبت نشده است."
            />
          </div>
        )}
      </Card>

      {data && data.totalPages > 1 && (
        <Pagination
          page={data.page}
          totalPages={data.totalPages}
          total={data.total}
          pageSize={data.pageSize}
          onPage={setPage}
        />
      )}
    </div>
  );
}

// useSearchParams() requires a Suspense boundary in the App Router; wrap the view so the page never
// de-opts to fully client-side rendering and the build stays clean.
export default function AuditLogsPage() {
  return (
    <Suspense
      fallback={
        <div className="grid place-items-center py-24">
          <Spinner className="h-8 w-8" />
        </div>
      }
    >
      <AuditLogsView />
    </Suspense>
  );
}
