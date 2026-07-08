import type { ReactNode } from "react";

export function PageTitle({ title, desc }: { title: string; desc?: string }) {
  return (
    <div className="mb-6">
      <h1 className="text-2xl font-bold" style={{ color: "var(--ac-title)" }}>{title}</h1>
      {desc && <p className="mt-1 text-sm" style={{ color: "var(--ac-muted)" }}>{desc}</p>}
    </div>
  );
}

export function Panel({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <div
      className={`rounded-[18px] p-6 ${className}`}
      style={{
        background: "var(--ac-panel-bg)",
        border: "1px solid var(--ac-panel-border)",
        boxShadow: "var(--ac-panel-shadow)",
      }}
    >
      {children}
    </div>
  );
}

export function StatCard({
  label,
  value,
  accent = "#3a64f2",
}: {
  label: string;
  value: string;
  accent?: string;
}) {
  return (
    <div
      className="relative overflow-hidden rounded-[16px] p-6"
      style={{
        background: "var(--ac-panel-bg)",
        border: "1px solid var(--ac-panel-border)",
        boxShadow: "var(--ac-panel-shadow)",
      }}
    >
      <span
        className="absolute inset-y-0 right-0 w-1"
        style={{ background: accent }}
      />
      <p className="text-sm" style={{ color: "var(--ac-muted)" }}>{label}</p>
      <p className="mt-2 text-2xl font-bold" style={{ color: "var(--ac-title)" }}>{value}</p>
    </div>
  );
}

const statusColors: Record<string, string> = {
  "پرداخت شده":      "bg-emerald-100 text-emerald-700",
  "پاسخ داده شده":   "bg-emerald-100 text-emerald-700",
  "در انتظار":       "bg-amber-100 text-amber-700",
  باز:               "bg-sky-100 text-sky-700",
  "لغو شده":         "bg-rose-100 text-rose-600",
  "بسته شده":        "bg-[#f3ece6] text-[#8c8075]",
};

export function StatusBadge({ status }: { status: string }) {
  return (
    <span className={`inline-block rounded-full px-3 py-1 text-xs font-medium ${statusColors[status] ?? "bg-[#f3ece6] text-[#8c8075]"}`}>
      {status}
    </span>
  );
}
