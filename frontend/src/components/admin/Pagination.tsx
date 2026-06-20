"use client";

import { useState } from "react";
import { formatNumber } from "@/lib/format";

// Client-side pager over an already-loaded (and filtered) list. The page index is clamped to the
// available range, so shrinking the list via a filter never strands the user on an empty page.
export function usePaged<T>(items: T[], pageSize = 10) {
  const [page, setPage] = useState(1);
  const totalPages = Math.max(1, Math.ceil(items.length / pageSize));
  const current = Math.min(page, totalPages);
  const slice = items.slice((current - 1) * pageSize, current * pageSize);
  return { page: current, setPage, totalPages, slice, total: items.length, pageSize };
}

// Compact page list: first, last, and a small window around the current page with ellipses.
function pageWindow(page: number, total: number): (number | "…")[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
  const out: (number | "…")[] = [1];
  const start = Math.max(2, page - 1);
  const end = Math.min(total - 1, page + 1);
  if (start > 2) out.push("…");
  for (let i = start; i <= end; i++) out.push(i);
  if (end < total - 1) out.push("…");
  out.push(total);
  return out;
}

export function Pagination({
  page,
  totalPages,
  total,
  pageSize,
  onPage,
}: {
  page: number;
  totalPages: number;
  total: number;
  pageSize: number;
  onPage: (p: number) => void;
}) {
  if (totalPages <= 1) return null;
  const from = (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, total);
  const btn = "grid h-9 min-w-9 place-items-center rounded-lg border px-2.5 text-sm font-medium transition disabled:opacity-40";
  const idle = "border-white/10 text-white/60 hover:text-white hover:bg-white/5";
  const active = "border-transparent bg-white/10 text-white";

  return (
    <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
      <p className="text-xs text-white/40">
        نمایش {formatNumber(from)}–{formatNumber(to)} از {formatNumber(total)}
      </p>
      <div className="flex items-center gap-1.5">
        <button onClick={() => onPage(page - 1)} disabled={page <= 1} className={`${btn} ${idle}`}>
          قبلی
        </button>
        {pageWindow(page, totalPages).map((p, i) =>
          p === "…" ? (
            <span key={`gap-${i}`} className="px-1 text-white/30">…</span>
          ) : (
            <button key={p} onClick={() => onPage(p)} className={`${btn} ${p === page ? active : idle}`}>
              {formatNumber(p)}
            </button>
          ),
        )}
        <button onClick={() => onPage(page + 1)} disabled={page >= totalPages} className={`${btn} ${idle}`}>
          بعدی
        </button>
      </div>
    </div>
  );
}
