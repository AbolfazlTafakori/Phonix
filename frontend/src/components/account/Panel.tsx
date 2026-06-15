import type { ReactNode } from "react";

export function PageTitle({ title, desc }: { title: string; desc?: string }) {
  return (
    <div className="mb-6">
      <h1 className="text-2xl font-bold text-white">{title}</h1>
      {desc && <p className="mt-1 text-sm text-white/55">{desc}</p>}
    </div>
  );
}

export function Panel({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <div className={`rounded-2xl border border-white/8 bg-[#15151f]/80 p-6 ${className}`}>
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
    <div className="relative overflow-hidden rounded-2xl border border-white/8 bg-[#15151f]/80 p-6">
      <span
        className="absolute inset-y-0 right-0 w-1"
        style={{ background: accent }}
      />
      <p className="text-sm text-white/55">{label}</p>
      <p className="mt-2 text-2xl font-bold text-white">{value}</p>
    </div>
  );
}

const statusColors: Record<string, string> = {
  "پرداخت شده": "bg-emerald-500/15 text-emerald-400",
  "پاسخ داده شده": "bg-emerald-500/15 text-emerald-400",
  "در انتظار": "bg-amber-500/15 text-amber-400",
  باز: "bg-sky-500/15 text-sky-400",
  "لغو شده": "bg-rose-500/15 text-rose-400",
  "بسته شده": "bg-white/10 text-white/60",
};

export function StatusBadge({ status }: { status: string }) {
  return (
    <span className={`inline-block rounded-full px-3 py-1 text-xs font-medium ${statusColors[status] ?? "bg-white/10 text-white/70"}`}>
      {status}
    </span>
  );
}
