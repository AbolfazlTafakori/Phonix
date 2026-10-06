"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { SeatSubmission, SeatSubmissionEvent } from "@/lib/types";
import { formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner } from "@/components/admin/ui";

// The review queue for per-seat customer submissions: everything buyers filed for individual seats of shared
// accounts, newest first. One row per seat, so a five-user purchase shows five independent entries.
//
// Marking an entry reviewed freezes it for the customer; reopening hands it back with an optional message, so
// asking for a clearer picture is a single action rather than a support conversation. Rejecting goes further:
// it deletes what the buyer sent and asks them for it again, with the reason delivered by notification and
// email — so unusable details are cleared out rather than sitting in the queue being re-read.

type Action = "review" | "reopen" | "reject";
type Filter = "Pending" | "Reviewed" | "Rejected" | "all";

const filterLabel: Record<Filter, string> = {
  Pending: "در انتظار بررسی",
  Reviewed: "بررسی شده",
  Rejected: "رد شده",
  all: "همه",
};

const statusLabel: Record<SeatSubmission["status"], string> = {
  Pending: "در انتظار بررسی",
  Reviewed: "بررسی شده",
  Rejected: "رد شده — در انتظار ارسال دوباره",
};

const statusStyle: Record<SeatSubmission["status"], string> = {
  Pending: "bg-amber-500/15 text-amber-300",
  Reviewed: "bg-emerald-500/15 text-emerald-400",
  Rejected: "bg-rose-500/15 text-rose-300",
};

const promptFor: Record<Action, string> = {
  review: "یادداشت برای کاربر (اختیاری):",
  reopen: "پیام برای کاربر (اختیاری) — مثلاً چه چیزی باید اصلاح شود:",
  reject: "دلیل رد شدن (اختیاری) — برای کاربر ایمیل و اعلان می‌شود. متن و تصویر ارسالی او در سوابق می‌ماند و باید اطلاعات جدید ارسال کند:",
};

// Date and time — for the history, where two things can happen on the same day.
const faDateTime = (iso: string) =>
  new Date(iso).toLocaleString("fa-IR", { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });

const actionLabel: Record<SeatSubmissionEvent["action"], string> = {
  submitted: "ثبت اطلاعات",
  edited: "ویرایش اطلاعات",
  reviewed: "بررسی شد",
  reopened: "بازگشایی برای ویرایش",
  rejected: "رد شد",
};

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
      Rejected: items.filter((s) => s.status === "Rejected").length,
      all: items.length,
    }),
    [items],
  );
  const shown = filter === "all" ? items : items.filter((s) => s.status === filter);

  async function act(s: SeatSubmission, kind: Action) {
    const answer = prompt(promptFor[kind]);
    // Dismissing the prompt has to mean "don't": rejecting deletes the customer's picture and emails them,
    // which is not something to do because someone pressed Escape on a note box.
    if (answer === null && kind === "reject") return;
    const note = answer ?? "";
    setBusy(s.id);
    setError("");
    try {
      const updated = kind === "review"
        ? await api.seatInfo.review(s.id, note)
        : kind === "reopen"
          ? await api.seatInfo.reopen(s.id, note)
          : await api.seatInfo.reject(s.id, note);
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
        {(["Pending", "Reviewed", "Rejected", "all"] as Filter[]).map((f) => (
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
                <span className={`rounded-md px-2 py-0.5 font-bold ${statusStyle[s.status]}`}>
                  {statusLabel[s.status]}
                </span>
                {s.history?.length > 0 && (
                  <span className="rounded-md bg-amber-500/15 px-2 py-0.5 font-bold text-amber-300">
                    {s.status === "Rejected" ? "اطلاعات قبلی در سوابق" : `ویرایش‌شده · ${formatNumber(s.history.length)} نسخه‌ی قبلی`}
                  </span>
                )}
                <span className="mr-auto text-white/35">{faDate(s.updatedAtUtc)}</span>
              </div>

              {/* With earlier versions on record, say plainly which one this is. */}
              {s.history?.length > 0 && s.status !== "Rejected" && (
                <p className="mt-3 text-xs font-bold text-emerald-300">اطلاعات جدید (فعلی) · {faDateTime(s.updatedAtUtc)}</p>
              )}

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
                  {/* A rejected entry has nothing left to act on — it was emptied and is the customer's to
                      fill in again — so it offers no buttons, only what it is waiting for. */}
                  {s.status === "Rejected" ? (
                    <p className="text-xs text-white/40">
                      اطلاعات ارسالی به سوابق منتقل شد و به کاربر اطلاع داده شد؛ در انتظار ارسال دوباره از سوی اوست.
                    </p>
                  ) : (
                    <div className="flex flex-wrap items-center gap-2">
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
                      <button
                        onClick={() => act(s, "reject")}
                        disabled={busy === s.id}
                        className="rounded-lg border border-rose-500/30 px-3 py-1.5 text-xs font-bold text-rose-300 transition hover:bg-rose-500/10 disabled:opacity-50"
                      >
                        {busy === s.id ? "..." : "رد شد"}
                      </button>
                    </div>
                  )}
                </div>
              </div>

              {/* What this seat held before — the previous device stays on record. Open while the entry waits for
                  review, which is exactly when staff need to compare it with the new one. */}
              {s.history?.length > 0 && (
                <details open={s.status === "Pending"} className="mt-3 rounded-lg border border-amber-500/20 bg-amber-500/[0.03] p-3">
                  <summary className="cursor-pointer text-xs font-bold text-amber-200/80">
                    اطلاعات قبلی ({formatNumber(s.history.length)})
                  </summary>
                  <div className="mt-3 space-y-3">
                    {s.history.map((v, i) => (
                      <div key={i} className="grid gap-3 border-t border-white/5 pt-3 first:border-t-0 first:pt-0 sm:grid-cols-[100px_1fr]">
                        {v.imageId ? (
                          <button
                            type="button"
                            onClick={() => setZoom(api.seatInfo.imageSrc(v.imageId!))}
                            className="overflow-hidden rounded-lg border border-white/10 transition hover:border-[#3a64f2]/60"
                            title="نمایش در اندازه کامل"
                          >
                            {/* eslint-disable-next-line @next/next/no-img-element */}
                            <img src={api.seatInfo.imageSrc(v.imageId)} alt={`تصویر قبلی ${v.seatLabel}`} className="h-20 w-full object-cover" />
                          </button>
                        ) : (
                          <div className="grid h-20 place-items-center rounded-lg border border-dashed border-white/10 text-[11px] text-white/30">
                            بدون تصویر
                          </div>
                        )}
                        <div className="space-y-1">
                          <p className="text-[11px] text-white/35">
                            قبلی · ثبت‌شده در {faDateTime(v.submittedAtUtc)} · {statusLabel[v.status]}
                          </p>
                          <p className="whitespace-pre-wrap text-xs text-white/65">{v.text || "—"}</p>
                          {v.reviewNote && <p className="text-[11px] text-white/45">یادداشت: {v.reviewNote}</p>}
                        </div>
                      </div>
                    ))}
                  </div>
                </details>
              )}

              {/* Who did what, when — the customer's filings and edits next to staff's reviews, reopens and rejections. */}
              {s.events?.length > 0 && (
                <details className="mt-3 rounded-lg border border-white/8 bg-white/[0.02] p-3">
                  <summary className="cursor-pointer text-xs font-bold text-white/60">
                    تاریخچه‌ی اقدامات ({formatNumber(s.events.length)})
                  </summary>
                  <ol className="mt-3 space-y-2">
                    {[...s.events].reverse().map((e, i) => (
                      <li key={i} className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 border-r-2 border-white/10 pr-3 text-xs">
                        <span className="font-bold text-white/80">{actionLabel[e.action] ?? e.action}</span>
                        <span className={e.by ? "text-sky-300/80" : "text-amber-200/80"}>{e.by ? `توسط ${e.by}` : "توسط کاربر"}</span>
                        <span className="text-white/35">{faDateTime(e.atUtc)}</span>
                        {e.note && <span className="w-full whitespace-pre-wrap text-white/55">{e.note}</span>}
                      </li>
                    ))}
                  </ol>
                </details>
              )}
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
