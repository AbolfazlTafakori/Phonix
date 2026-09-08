import type { ReactNode } from "react";
import AdminIcon from "./AdminIcon";
import { formatNumber } from "@/lib/format";

export function Card({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <div className={`rounded-2xl border border-white/8 bg-[#15151f]/80 ${className}`}>{children}</div>
  );
}

export function PageHeader({ title, desc, action }: { title: string; desc?: string; action?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-center justify-between gap-4">
      <div>
        <h2 className="text-2xl font-bold text-white">{title}</h2>
        {desc && <p className="mt-1 text-sm text-white/50">{desc}</p>}
      </div>
      {action}
    </div>
  );
}

export function KpiCard({
  label,
  value,
  delta,
  up,
  icon,
  accent,
}: {
  label: string;
  value: string;
  delta: string;
  up: boolean;
  icon: string;
  accent: string;
}) {
  return (
    <Card className="p-5">
      <div className="flex items-start justify-between">
        <div
          className="grid h-11 w-11 place-items-center rounded-xl"
          style={{ background: `${accent}1f`, color: accent }}
        >
          <AdminIcon name={icon} className="h-5 w-5" />
        </div>
        <span className={`flex items-center gap-1 text-xs font-bold ${up ? "text-emerald-400" : "text-rose-400"}`}>
          {up ? "▲" : "▼"} {delta}
        </span>
      </div>
      <p className="mt-4 text-2xl font-bold text-white">{value}</p>
      <p className="mt-1 text-sm text-white/50">{label}</p>
    </Card>
  );
}

const statusColors: Record<string, string> = {
  "پرداخت شده": "bg-emerald-500/15 text-emerald-400",
  "پاسخ داده شده": "bg-emerald-500/15 text-emerald-400",
  فعال: "bg-emerald-500/15 text-emerald-400",
  "در انتظار": "bg-amber-500/15 text-amber-400",
  باز: "bg-sky-500/15 text-sky-400",
  "لغو شده": "bg-rose-500/15 text-rose-400",
  مسدود: "bg-rose-500/15 text-rose-400",
  ناموجود: "bg-rose-500/15 text-rose-400",
  "بسته شده": "bg-white/10 text-white/60",
  "تایید شده": "bg-emerald-500/15 text-emerald-400",
  "رد شده": "bg-rose-500/15 text-rose-400",
  "در انتظار تأیید": "bg-amber-500/15 text-amber-400",
  "در حال آماده‌سازی": "bg-sky-500/15 text-sky-400",
  "تکمیل شده": "bg-emerald-500/15 text-emerald-400",
  مدیر: "bg-[#a855f7]/15 text-[#c98bff]",
  پشتیبانی: "bg-[#3a64f2]/15 text-[#6f93ff]",
  کاربر: "bg-white/10 text-white/70",
};

export function StatusBadge({ status }: { status: string }) {
  return (
    <span className={`inline-block whitespace-nowrap rounded-full px-3 py-1 text-xs font-medium ${statusColors[status] ?? "bg-white/10 text-white/70"}`}>
      {status}
    </span>
  );
}

// Horizontal bars, one row per item. The vertical version this replaces was unreadable with real data: its
// columns never got a height (the row was items-end, so nothing stretched to the h-56 and every bar resolved
// its percentage against an auto height of zero), and product names — which run long in Persian — sat under
// 10px-wide columns with nothing to truncate against, so they spilled straight out of the card. Reading down
// a list also puts the numbers where they can actually be read, which is the point of the panel.
export function SalesChart({
  data,
  visibleRows = 5,
}: {
  data: { label: string; value: number; caption?: string }[];
  visibleRows?: number;
}) {
  // An empty set renders nothing; an all-zero set would otherwise divide by zero and give every bar NaN%.
  const max = Math.max(1, ...data.map((d) => d.value));
  // Past `visibleRows` the list scrolls INSIDE the card instead of growing it: the panel keeps its height in
  // the dashboard grid however many products sell. The scrollbar itself is hidden — the height stops mid-row
  // on purpose, and that half-visible row is what says there is more below. overscroll-contain keeps the
  // wheel in this list rather than handing the gesture on to the page once the end is reached.
  const scrolls = data.length > visibleRows;
  // One row is a 18px name line + 6px gap + 8px bar = 32px, and rows sit 16px apart.
  const maxHeight = visibleRows * 32 + (visibleRows - 1) * 16 + 26;
  return (
    <div
      className={scrolls ? "overflow-y-auto overscroll-contain [scrollbar-width:none] [&::-webkit-scrollbar]:hidden" : ""}
      style={scrolls ? { maxHeight } : undefined}
    >
      <ul className="flex flex-col gap-4">
        {data.map((d) => (
          <li key={d.label} className="group">
            <div className="mb-1.5 flex items-baseline justify-between gap-3">
              {/* min-w-0 is what lets truncate actually clip a long name inside a flex row */}
              <span className="min-w-0 flex-1 truncate text-[13px] text-white/75" title={d.label}>{d.label}</span>
              <span className="shrink-0 text-[13px] font-bold text-white">{formatNumber(d.value)}</span>
            </div>
            <div className="flex items-center gap-3">
              <div className="h-2 flex-1 overflow-hidden rounded-full bg-white/[0.06]">
                <div
                  className="h-full rounded-full bg-gradient-to-l from-[#6d28d9] to-[#e60053] transition-all duration-500 group-hover:brightness-125"
                  style={{ width: `${Math.max(2, (d.value / max) * 100)}%` }}
                />
              </div>
              {d.caption && <span className="shrink-0 text-[11px] text-white/40">{d.caption}</span>}
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}

export const inputCls =
  "h-11 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition focus:border-[#3a64f2]";

export function Field({ label, children, className = "" }: { label: string; children: ReactNode; className?: string }) {
  return (
    <label className={`block ${className}`}>
      <span className="mb-1.5 block text-xs font-medium text-white/55">{label}</span>
      {children}
    </label>
  );
}

export function Toggle({ checked, onChange }: { checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      type="button"
      onClick={() => onChange(!checked)}
      className={`relative h-6 w-11 shrink-0 rounded-full transition ${checked ? "bg-[#e60053]" : "bg-white/15"}`}
    >
      <span className={`absolute top-0.5 h-5 w-5 rounded-full bg-white transition-all ${checked ? "right-0.5" : "right-[22px]"}`} />
    </button>
  );
}

export function Spinner({ className = "" }: { className?: string }) {
  return (
    <span
      className={`inline-block h-5 w-5 animate-spin rounded-full border-2 border-white/20 border-t-[#e60053] ${className}`}
    />
  );
}

export function Drawer({
  open,
  onClose,
  title,
  children,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
}) {
  return (
    <div className={`fixed inset-0 z-50 ${open ? "" : "pointer-events-none"}`}>
      <div
        onClick={onClose}
        className={`absolute inset-0 bg-black/60 backdrop-blur-sm transition-opacity duration-300 ${open ? "opacity-100" : "opacity-0"}`}
      />
      <div
        className={`absolute inset-y-0 left-0 flex w-full max-w-md flex-col border-r border-white/8 bg-[#0d0d14] shadow-2xl transition-transform duration-300 ${open ? "translate-x-0" : "-translate-x-full"}`}
      >
        <div className="flex h-[68px] items-center justify-between border-b border-white/8 px-6">
          <h3 className="text-lg font-bold text-white">{title}</h3>
          <button
            onClick={onClose}
            className="grid h-9 w-9 place-items-center rounded-full border border-white/10 text-white/60 transition hover:text-white"
          >
            ✕
          </button>
        </div>
        <div className="flex-1 overflow-y-auto p-6">{children}</div>
      </div>
    </div>
  );
}

const modalSizes = {
  lg: "max-w-lg",
  xl: "max-w-xl",
  "2xl": "max-w-2xl",
  "3xl": "max-w-3xl",
} as const;

export function Modal({
  open,
  onClose,
  title,
  children,
  size = "lg",
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  size?: keyof typeof modalSizes;
}) {
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 grid place-items-center p-3 sm:p-4">
      <div onClick={onClose} className="absolute inset-0 bg-black/60 backdrop-blur-sm" />
      {/* cap the height and scroll the body so a tall modal (product form, KYC) never clips on short screens. */}
      <div className={`relative flex max-h-[92dvh] w-full ${modalSizes[size]} flex-col overflow-hidden rounded-2xl border border-white/10 bg-[#15151f] shadow-2xl`}>
        <div className="flex shrink-0 items-center justify-between border-b border-white/8 px-5 py-4 sm:px-6">
          <h3 className="text-lg font-bold text-white">{title}</h3>
          <button
            onClick={onClose}
            className="grid h-9 w-9 shrink-0 place-items-center rounded-full border border-white/10 text-white/60 transition hover:text-white"
          >
            ✕
          </button>
        </div>
        <div className="overflow-y-auto overscroll-contain p-5 sm:p-6">{children}</div>
      </div>
    </div>
  );
}

export type Column<T> = {
  header: string;
  cell: (row: T) => ReactNode;
  th?: string;
  td?: string;
  primary?: boolean;
  full?: boolean;
  hideLabel?: boolean;
};

export function DataTable<T>({
  columns,
  rows,
  rowKey,
  minWidth = 720,
  onRowClick,
  empty = "موردی یافت نشد",
}: {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string | number;
  minWidth?: number;
  onRowClick?: (row: T) => void;
  empty?: string;
}) {
  if (rows.length === 0) {
    return <p className="px-6 py-16 text-center text-sm text-white/40">{empty}</p>;
  }

  const primary = columns.find((c) => c.primary);
  const secondary = columns.filter((c) => c !== primary);

  return (
    <>
      {/* Table only from lg up — that's where the sidebar appears and there's real horizontal room.
          Below lg (tablets incl. iPad portrait) we fall back to cards so columns never overflow. */}
      <div className="hidden overflow-x-auto overscroll-x-contain lg:block">
        <table className="w-full text-right" style={{ minWidth }}>
          <thead>
            <tr className="border-b border-white/8 text-sm text-white/45">
              {columns.map((c, i) => (
                <th key={i} className={`px-4 py-4 font-medium xl:px-6 ${c.th ?? ""}`}>{c.header}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr
                key={rowKey(row)}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
                className={`border-b border-white/5 text-sm text-white/85 transition hover:bg-white/[0.03] ${
                  onRowClick ? "cursor-pointer" : ""
                }`}
              >
                {columns.map((c, i) => (
                  <td key={i} className={`px-4 py-3.5 xl:px-6 ${c.td ?? ""}`}>{c.cell(row)}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="divide-y divide-white/5 lg:hidden">
        {rows.map((row) => (
          <div
            key={rowKey(row)}
            onClick={onRowClick ? () => onRowClick(row) : undefined}
            className={`p-4 sm:p-5 ${onRowClick ? "cursor-pointer active:bg-white/[0.03]" : ""}`}
          >
            {primary && <div className="mb-3">{primary.cell(row)}</div>}
            <div className="grid gap-x-4 gap-y-2.5">
              {secondary.map((c, i) =>
                c.full ? (
                  <div key={i} className="pt-2">{c.cell(row)}</div>
                ) : (
                  <div key={i} className="flex items-center justify-between gap-3">
                    {!c.hideLabel && <span className="shrink-0 text-xs text-white/40">{c.header}</span>}
                    <span className="text-sm text-white/85">{c.cell(row)}</span>
                  </div>
                ),
              )}
            </div>
          </div>
        ))}
      </div>
    </>
  );
}
