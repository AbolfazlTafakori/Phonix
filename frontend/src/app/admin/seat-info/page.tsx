"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { SeatSubmission } from "@/lib/types";
import { formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner } from "@/components/admin/ui";

// The review queue for per-seat customer submissions: everything buyers filed for individual seats of shared
// accounts, newest first. One row per seat, so a five-user purchase shows five independent entries.
//
// Marking an entry reviewed freezes it for the customer; reopening hands it back with an optional message, so
// asking for a clearer picture is a single action rather than a support conversation.

type Filter = "Pending" | "Reviewed" | "all";
const filterLabel: Record<Filter, string> = { Pending: "در انتظار بررسی", Reviewed: "بررسی شده", all: "همه" };

const faDate = (iso: string) =>
  new Date(iso).toLocaleDateString("fa-IR", { year: "numeric", month: "2-digit", day: "2-digit" });

export default function AdminSeatInfoPage() {
  const [items, setItems] = useState<SeatSubmission[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [filter, setFilter] = useState<Filter>("Pending");
  const [busy, setBusy] = useState<number | null>(null);
  // Which submission's picture is open full-size — the list stays scannable with thumbnails.
  const [zoom, setZoom] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        setItems(await api.seatInfo.all());
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const counts = useMemo(
    () => ({
      Pending: items.filter((s) => s.status === "Pending").length,
      Reviewed: items.filter((s) => s.status === "Reviewed").length,
      all: items.length,
    }),
    [items],
  );
  const shown = filter === "all" ? items : items.filter((s) => s.status === filter);

  async function act(s: SeatSubmission, kind: "review" | "reopen") {
    const note = kind === "reopen"
      ? prompt("پیام برای کاربر (اختیاری) — مثلاً چه چیزی باید اصلاح شود:") ?? ""
      : prompt("یادداشت برای کاربر (اختیاری):") ?? "";
    setBusy(s.id);
    setError("");
    try {
      const updated = kind === "review"
        ? await api.seatInfo.review(s.id, note)
        : await api.seatInfo.reopen(s.id, note);
      setItems((p) => p.map((x) => (x.id === s.id ? updated : x)));
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در انجام عملیات");
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <PageHeader
        title="اطلاعات کاربران اکانت‌ها"
        desc="اطلاعاتی که خریداران برای هر پروفایل از اکانت‌های اشتراکی ارسال کرده‌اند"
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        {(["Pending", "Reviewed", "all"] as Filter[]).map((f) => (
          <button
            key={f}
            onClick={() => setFilter(f)}
            className={`rounded-xl px-4 py-2 text-xs font-bold transition ${
              filter === f ? "bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-white" : "border border-white/15 text-white/70 hover:bg-white/10"
            }`}
          >
            {filterLabel[f]} ({formatNumber(counts[f])})
          </button>
        ))}
      </div>

      {error && <Card className="mb-4 p-4 text-sm text-rose-400">{error}</Card>}

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : shown.length === 0 ? (
        <Card className="p-10 text-center text-sm text-white/40">موردی برای نمایش نیست.</Card>
      ) : (
        <div className="space-y-3">
          {shown.map((s) => (
            <Card key={s.id} className="p-4">
              <div className="flex flex-wrap items-center gap-2 text-xs">
                <span className="font-mono font-bold text-white/80">{s.orderCode}</span>
                <span className="text-white/60">{s.userName}</span>
                <span className="rounded-md bg-white/10 px-2 py-0.5 text-white/60">{s.productName}</span>
                <span dir="ltr" className="rounded-md bg-sky-500/15 px-2 py-0.5 font-bold text-sky-300" style={{ unicodeBidi: "isolate" }}>
                  {s.seatLabel || `#${s.seatIndex + 1}`}
                </span>
                <span
                  className={`rounded-md px-2 py-0.5 font-bold ${
                    s.status === "Pending" ? "bg-amber-500/15 text-amber-300" : "bg-emerald-500/15 text-emerald-400"
                  }`}
                >
                  {s.status === "Pending" ? "در انتظار بررسی" : "بررسی شده"}
                </span>
                <span className="mr-auto text-white/35">{faDate(s.updatedAtUtc)}</span>
              </div>

              <div className="mt-3 grid gap-3 sm:grid-cols-[160px_1fr]">
                {s.imageId ? (
                  <button
                    type="button"
                    onClick={() => setZoom(api.seatInfo.imageSrc(s.imageId!))}
                    className="overflow-hidden rounded-lg border border-white/10 transition hover:border-[#3a64f2]/60"
                    title="نمایش در اندازه کامل"
                  >
                    {/* eslint-disable-next-line @next/next/no-img-element */}
                    <img src={api.seatInfo.imageSrc(s.imageId)} alt={`تصویر ${s.seatLabel}`} className="h-32 w-full object-cover" />
                  </button>
                ) : (
                  <div className="grid h-32 place-items-center rounded-lg border border-dashed border-white/10 text-[11px] text-white/30">
                    بدون تصویر
                  </div>
                )}
                <div className="space-y-2">
                  <p className="whitespace-pre-wrap rounded-lg bg-white/[0.03] p-3 text-sm text-white/80">
                    {s.text || "—"}
                  </p>
                  {s.reviewNote && (
                    <p className="rounded-lg border border-sky-500/25 bg-sky-500/[0.06] p-2 text-xs text-sky-200">
                      یادداشت: {s.reviewNote}
                      {s.reviewedBy ? ` — ${s.reviewedBy}` : ""}
                    </p>
                  )}
                  <div className="flex items-center gap-2">
                    {s.status === "Pending" ? (
                      <button
                        onClick={() => act(s, "review")}
                        disabled={busy === s.id}
                        className="rounded-lg border border-emerald-500/30 px-3 py-1.5 text-xs font-bold text-emerald-400 transition hover:bg-emerald-500/10 disabled:opacity-50"
                      >
                        {busy === s.id ? "..." : "بررسی شد"}
                      </button>
                    ) : (
                      <button
                        onClick={() => act(s, "reopen")}
                        disabled={busy === s.id}
                        className="rounded-lg border border-amber-500/30 px-3 py-1.5 text-xs font-bold text-amber-300 transition hover:bg-amber-500/10 disabled:opacity-50"
                      >
                        {busy === s.id ? "..." : "بازگشایی برای ویرایش کاربر"}
                      </button>
                    )}
                  </div>
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      {/* full-size picture — click anywhere to dismiss */}
      {zoom && (
        <div
          role="button"
          tabIndex={0}
          onClick={() => setZoom(null)}
          onKeyDown={(e) => { if (e.key === "Escape" || e.key === "Enter") setZoom(null); }}
          className="fixed inset-0 z-50 grid cursor-zoom-out place-items-center bg-black/80 p-6"
        >
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={zoom} alt="تصویر ارسالی کاربر" className="max-h-full max-w-full rounded-xl object-contain" />
        </div>
      )}
    </div>
  );
}
