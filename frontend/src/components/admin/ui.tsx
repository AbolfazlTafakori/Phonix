import type { ReactNode } from "react";
import AdminIcon from "./AdminIcon";

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

export function SalesChart({ data }: { data: { label: string; value: number }[] }) {
  const max = Math.max(...data.map((d) => d.value));
  return (
    <div className="flex h-56 items-end gap-2 sm:gap-3">
      {data.map((d) => (
        <div key={d.label} className="group flex flex-1 flex-col items-center gap-2">
          <div className="relative flex w-full flex-1 items-end">
            <div
              className="w-full rounded-t-md bg-gradient-to-t from-[#6d28d9] to-[#e60053] transition-all duration-300 group-hover:brightness-125"
              style={{ height: `${(d.value / max) * 100}%` }}
            />
          </div>
          <span className="truncate text-[10px] text-white/40">{d.label}</span>
        </div>
      ))}
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
    <div className="fixed inset-0 z-50 grid place-items-center p-4">
      <div onClick={onClose} className="absolute inset-0 bg-black/60 backdrop-blur-sm" />
      <div className={`relative w-full ${modalSizes[size]} overflow-hidden rounded-2xl border border-white/10 bg-[#15151f] shadow-2xl`}>
        <div className="flex items-center justify-between border-b border-white/8 px-6 py-4">
          <h3 className="text-lg font-bold text-white">{title}</h3>
          <button
            onClick={onClose}
            className="grid h-9 w-9 place-items-center rounded-full border border-white/10 text-white/60 transition hover:text-white"
          >
            ✕
          </button>
        </div>
        <div className="p-6">{children}</div>
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
      <div className="hidden overflow-x-auto md:block">
        <table className="w-full text-right" style={{ minWidth }}>
          <thead>
            <tr className="border-b border-white/8 text-sm text-white/45">
              {columns.map((c, i) => (
                <th key={i} className={`px-6 py-4 font-medium ${c.th ?? ""}`}>{c.header}</th>
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
                  <td key={i} className={`px-6 py-3 ${c.td ?? ""}`}>{c.cell(row)}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="divide-y divide-white/5 md:hidden">
        {rows.map((row) => (
          <div
            key={rowKey(row)}
            onClick={onRowClick ? () => onRowClick(row) : undefined}
            className={`p-4 ${onRowClick ? "cursor-pointer active:bg-white/[0.03]" : ""}`}
          >
            {primary && <div className="mb-3">{primary.cell(row)}</div>}
            <div className="grid gap-x-4 gap-y-2.5">
              {secondary.map((c, i) =>
                c.full ? (
                  <div key={i} className="pt-1">{c.cell(row)}</div>
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
