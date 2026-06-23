"use client";

import { useState } from "react";
import type { OrderStatusHistory } from "@/lib/types";
import { orderStatusLabel } from "@/lib/labels";
import { formatNumber } from "@/lib/format";
import { StatusBadge } from "./ui";
import AdminIcon from "./AdminIcon";

function formatWhen(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString("fa-IR", { dateStyle: "short", timeStyle: "short" });
}

export default function OrderHistory({ history }: { history: OrderStatusHistory[] }) {
  const [open, setOpen] = useState(false);
  if (!history || history.length === 0) return null;
  const rows = [...history].sort((a, b) => b.id - a.id);

  return (
    <div className="mt-3 border-t border-white/8 pt-3">
      <button
        onClick={() => setOpen((v) => !v)}
        className="flex w-full items-center gap-2 text-xs font-bold text-white/55 transition hover:text-white"
      >
        <AdminIcon name="search" className="h-4 w-4 shrink-0" />
        <span>تاریخچه‌ی تغییرات سفارش ({formatNumber(history.length)})</span>
        <svg
          className={`mr-auto h-4 w-4 shrink-0 transition-transform duration-200 ${open ? "rotate-180" : ""}`}
          viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"
        >
          <path d="M6 9l6 6 6-6" />
        </svg>
      </button>

      {open && (
        <ul className="mt-3 space-y-2.5">
          {rows.map((h) => (
            <li
              key={h.id}
              className="flex flex-col gap-2 rounded-xl border border-white/8 bg-white/[0.02] p-3 sm:flex-row sm:items-center sm:justify-between sm:gap-3"
            >
              <div className="flex flex-wrap items-center gap-x-2 gap-y-1.5 text-xs">
                <StatusBadge status={orderStatusLabel[h.fromStatus]} />
                <AdminIcon name="logout" className="h-3.5 w-3.5 rotate-180 text-white/30" />
                <StatusBadge status={orderStatusLabel[h.toStatus]} />
                {h.reason && <span className="text-white/55">· {h.reason}</span>}
              </div>
              <div className="flex shrink-0 items-center gap-1.5 text-[11px] text-white/40">
                <span className="font-medium text-white/55">{h.changedByUsername}</span>
                <span>·</span>
                <span dir="ltr">{formatWhen(h.changedAtUtc)}</span>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
