"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { SeatRow, SeatSubmission, SeatSubmissionEvent } from "@/lib/types";
import { formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, Modal, Field, inputCls } from "@/components/admin/ui";
import SeatRoster from "@/components/admin/SeatRoster";

// The review queue for per-seat customer submissions: everything buyers filed for individual seats of shared
// accounts, newest first. One row per seat, so a five-user purchase shows five independent entries.
//
// Marking an entry reviewed freezes it for the customer; reopening hands it back with an optional message, so
// asking for a clearer picture is a single action rather than a support conversation. Rejecting goes further:
// it deletes what the buyer sent and asks them for it again, with the reason delivered by notification and
// email — so unusable details are cleared out rather than sitting in the queue being re-read.

// Beside the queue there are three more views over the same seats: «ثبت نشده‌ها» (the plan asks for details
// and the seat is waiting on the BUYER — nothing ever filed, or reopened and untouched since), «خارج از
// گارانتی» (closed seats, where the only action left is handing the warranty back), and a search that reaches
// any purchased seat by order code, buyer, device, service, block label or the shared account's own login.
// Warnings and warranty decisions live there rather than here, because they are per SEAT: on a shared account
// one user can be closed without touching the other people on the same credentials.

type Action = "review" | "reopen" | "reject";
type Tab = "queue" | "unregistered" | "revoked" | "search";
type Filter = "Pending" | "Reviewed" | "Rejected" | "all";

const tabLabel: Record<Tab, string> = {
  queue: "صف بررسی",
  unregistered: "ثبت نشده‌ها",
  revoked: "خارج از گارانتی",
  search: "جست‌وجو",
};

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
  NotFiled: "ثبت نشده",
};

const statusStyle: Record<SeatSubmission["status"], string> = {
  Pending: "bg-amber-500/15 text-amber-300",
  Reviewed: "bg-emerald-500/15 text-emerald-400",
  Rejected: "bg-rose-500/15 text-rose-300",
  NotFiled: "bg-white/10 text-white/60",
};

// What the dialog asks for, per action. "message" is a free note to the customer about this seat.
type DialogKind = Action | "message";

const dialogTitle: Record<DialogKind, string> = {
  review: "بررسی شد",
  reopen: "بازگشایی برای ویرایش کاربر",
  reject: "رد اطلاعات",
  message: "پیام به کاربر",
};

const dialogHint: Record<DialogKind, string> = {
  review: "یادداشت برای کاربر (اختیاری) — در فرم او نمایش داده می‌شود.",
  reopen: "این متن برای کاربر هم اعلان و هم ایمیل می‌شود. می‌توانید آن را تغییر دهید یا متن دیگری بنویسید.",
  reject: "دلیل رد (اختیاری) — متن کامل برای کاربر ایمیل و اعلان می‌شود. اطلاعات فعلی او در سوابق می‌ماند و باید اطلاعات جدید ارسال کند.",
  message: "این پیام برای کاربر هم اعلان و هم ایمیل می‌شود.",
};

// Same wording as the API's OrderNotices.SeatInfoReopenDefault — what goes out if the admin leaves it as is.
const REOPEN_DEFAULT =
  "لطفاً مشخصات دستگاه جدید خود را برای این پروفایل ثبت کنید. در صورت ثبت نکردن مشخصات، اکانت شما از گارانتی خارج می‌شود.";

// Date and time — for the history, where two things can happen on the same day.
const faDateTime = (iso: string) =>
  new Date(iso).toLocaleString("fa-IR", { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });

const actionLabel: Record<SeatSubmissionEvent["action"], string> = {
  submitted: "ثبت اطلاعات",
  edited: "ویرایش اطلاعات",
  reviewed: "بررسی شد",
  reopened: "بازگشایی برای ویرایش",
  rejected: "رد شد",
  warned: "هشدار ارسال شد",
  revoked: "خروج از گارانتی",
  restored: "بازگشایی گارانتی",
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
  // The open action dialog, and what is typed into it.
  const [dialog, setDialog] = useState<{ s: SeatSubmission; kind: DialogKind } | null>(null);
  const [dialogText, setDialogText] = useState("");
  const [messageTitle, setMessageTitle] = useState("");
  const [dialogError, setDialogError] = useState("");
  const [sent, setSent] = useState<number | null>(null);
  // ── the three roster views ───────────────────────────────────────────────────────────────────────
  const [tab, setTab] = useState<Tab>("queue");
  const [rows, setRows] = useState<SeatRow[]>([]);
  const [rowsLoading, setRowsLoading] = useState(false);
  // The term that was actually searched, kept apart from the box so typing doesn't fire a request per letter.
  const [term, setTerm] = useState("");
  const [query, setQuery] = useState("");

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

  // One loader for the three roster views: whichever tab is open decides what to ask for.
  const loadRows = useCallback(async (which: Tab, q: string) => {
    if (which === "queue") return;
    setRowsLoading(true);
    setError("");
    try {
      setRows(
        which === "unregistered"
          ? await api.seatInfo.unregistered()
          : which === "revoked"
            ? await api.seatInfo.revokedSeats()
            : q.trim().length === 0
              ? []
              : await api.seatInfo.seats(q.trim()),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      setRows([]);
    } finally {
      setRowsLoading(false);
    }
  }, []);

  // Switching tab (or searching) is an event, not derived state, so the fetch hangs off the click rather than
  // an effect — nothing reloads behind the operator's back.
  function go(next: Tab, q = query) {
    setTab(next);
    setQuery(q);
    void loadRows(next, q);
  }

  const counts = useMemo(
    () => ({
      Pending: items.filter((s) => s.status === "Pending").length,
      Reviewed: items.filter((s) => s.status === "Reviewed").length,
      Rejected: items.filter((s) => s.status === "Rejected").length,
      // Staff-side stubs (NotFiled) carry warnings and warranty, not something a buyer sent, so they are no
      // part of the queue or its totals.
      all: items.filter((s) => s.status !== "NotFiled").length,
    }),
    [items],
  );
  const queue = items.filter((s) => s.status !== "NotFiled");
  const shown = filter === "all" ? queue : queue.filter((s) => s.status === filter);

  // Every action opens a dialog first; closing it (✕, Escape, «انصراف») does nothing — rejecting and reopening
  // both email the customer, which shouldn't happen because a note box was dismissed.
  function open(s: SeatSubmission, kind: DialogKind) {
    setDialog({ s, kind });
    setDialogText(kind === "reopen" ? REOPEN_DEFAULT : "");
    setMessageTitle(kind === "message" ? `درباره‌ی سرویس «${s.productName}» — سفارش ${s.orderCode}` : "");
    setDialogError("");
  }

  async function confirm() {
    if (!dialog) return;
    const { s, kind } = dialog;
    const note = dialogText.trim();
    if (kind === "message" && (!messageTitle.trim() || !note)) {
      setDialogError("عنوان و متن پیام را وارد کنید.");
      return;
    }
    setBusy(s.id);
    setDialogError("");
    setError("");
    try {
      if (kind === "message") {
        await api.notifications.send({ userId: s.userId, title: messageTitle.trim(), body: note, link: "/account/orders", sendEmail: true });
        setSent(s.id);
        setTimeout(() => setSent((v) => (v === s.id ? null : v)), 2500);
      } else {
        const updated = kind === "review"
          ? await api.seatInfo.review(s.id, note)
          : kind === "reopen"
            ? await api.seatInfo.reopen(s.id, note)
            : await api.seatInfo.reject(s.id, note);
        setItems((p) => p.map((x) => (x.id === s.id ? updated : x)));
      }
      setDialog(null);
    } catch (e) {
      setDialogError(e instanceof Error ? e.message : "خطا در انجام عملیات");
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <PageHeader
        title="اطلاعات کاربران اکانت‌ها"
        desc="اطلاعاتی که خریداران برای هر پروفایل از اکانت‌های اشتراکی ارسال کرده‌اند، و وضعیت گارانتی هر پروفایل"
      />

      {/* search — the way into any purchased seat, whichever tab is open */}
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <input
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          onKeyDown={(e) => { if (e.key === "Enter") go("search", term); }}
          placeholder="جست‌وجو: کد سفارش، نام یا ایمیل کاربر، مدل دستگاه، نام سرویس، بلوک (A - 2) یا یوزرنیم اکانت"
          className={`${inputCls} sm:max-w-xl`}
        />
        <button
          onClick={() => go("search", term)}
          className="rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 py-2.5 text-xs font-bold text-white transition hover:brightness-110"
        >
          جست‌وجو
        </button>
        {query && (
          <button
            onClick={() => { setTerm(""); go("queue", ""); }}
            className="rounded-xl border border-white/15 px-4 py-2.5 text-xs font-bold text-white/70 transition hover:bg-white/10"
          >
            پاک کردن
          </button>
        )}
      </div>

      <div className="mb-4 flex flex-wrap items-center gap-2">
        {(["queue", "unregistered", "revoked", "search"] as Tab[]).map((t) => (
          <button
            key={t}
            onClick={() => go(t)}
            className={`rounded-xl px-4 py-2 text-xs font-bold transition ${
              tab === t ? "bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-white" : "border border-white/15 text-white/70 hover:bg-white/10"
            }`}
          >
            {tabLabel[t]}
            {t === "queue" ? ` (${formatNumber(counts.Pending)})` : ""}
          </button>
        ))}
      </div>

      {tab === "queue" && (
        <div className="mb-4 flex flex-wrap items-center gap-2">
          {(["Pending", "Reviewed", "Rejected", "all"] as Filter[]).map((f) => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              className={`rounded-xl border px-3 py-1.5 text-[11px] font-bold transition ${
                filter === f ? "border-[#3a64f2]/60 bg-[#3a64f2]/10 text-white" : "border-white/10 text-white/55 hover:bg-white/5"
              }`}
            >
              {filterLabel[f]} ({formatNumber(counts[f])})
            </button>
          ))}
        </div>
      )}

      {error && <Card className="mb-4 p-4 text-sm text-rose-400">{error}</Card>}

      {tab !== "queue" ? (
        <SeatRoster
          rows={rows}
          loading={rowsLoading}
          empty={
            tab === "unregistered"
              ? "همه‌ی اکانت‌های فروخته‌شده اطلاعاتشان ثبت شده است."
              : tab === "revoked"
                ? "هیچ سرویسی خارج از گارانتی نیست."
                : query
                  ? "موردی با این عبارت پیدا نشد."
                  : "عبارتی را جست‌وجو کنید تا سفارش‌های مربوط به آن بیاید."
          }
          onChanged={() => void loadRows(tab, query)}
        />
      ) : loading ? (
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
                {s.revoked && (
                  <span className="rounded-md bg-rose-500/15 px-2 py-0.5 font-bold text-rose-300">خارج از گارانتی</span>
                )}
                {s.warningsSent > 0 && (
                  <span className="rounded-md bg-orange-500/15 px-2 py-0.5 font-bold text-orange-300">
                    {s.warningsSent === 2 ? "هشدار ۱ و ۲" : "هشدار ۱"} ارسال شده
                  </span>
                )}
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
                  {/* A rejected entry has nothing left to decide — it was emptied and is the customer's to fill in
                      again — so it only offers a message to them, and says what it is waiting for. */}
                  {s.status === "Rejected" ? (
                    <div className="space-y-2">
                      <p className="text-xs text-white/40">
                        اطلاعات ارسالی به سوابق منتقل شد و به کاربر اطلاع داده شد؛ در انتظار ارسال دوباره از سوی اوست.
                      </p>
                      <button
                        onClick={() => open(s, "message")}
                        disabled={busy === s.id}
                        className="rounded-lg border border-sky-500/30 px-3 py-1.5 text-xs font-bold text-sky-300 transition hover:bg-sky-500/10 disabled:opacity-50"
                      >
                        {sent === s.id ? "ارسال شد ✓" : "پیام به کاربر"}
                      </button>
                    </div>
                  ) : (
                    <div className="flex flex-wrap items-center gap-2">
                      {s.status === "Pending" && (
                        <button
                          onClick={() => open(s, "review")}
                          disabled={busy === s.id}
                          className="rounded-lg border border-emerald-500/30 px-3 py-1.5 text-xs font-bold text-emerald-400 transition hover:bg-emerald-500/10 disabled:opacity-50"
                        >
                          {busy === s.id ? "..." : "بررسی شد"}
                        </button>
                      )}
                      {/* The customer can't change a filed seat on their own, pending or not — a reopen is the
                          only way in, and one is enough until they've used it. */}
                      {s.editable ? (
                        <span className="rounded-lg border border-amber-500/20 px-3 py-1.5 text-xs text-amber-300/80">
                          باز برای ویرایش — در انتظار کاربر
                        </span>
                      ) : (
                        <button
                          onClick={() => open(s, "reopen")}
                          disabled={busy === s.id}
                          className="rounded-lg border border-amber-500/30 px-3 py-1.5 text-xs font-bold text-amber-300 transition hover:bg-amber-500/10 disabled:opacity-50"
                        >
                          {busy === s.id ? "..." : "بازگشایی برای ویرایش کاربر"}
                        </button>
                      )}
                      <button
                        onClick={() => open(s, "reject")}
                        disabled={busy === s.id}
                        className="rounded-lg border border-rose-500/30 px-3 py-1.5 text-xs font-bold text-rose-300 transition hover:bg-rose-500/10 disabled:opacity-50"
                      >
                        {busy === s.id ? "..." : "رد شد"}
                      </button>
                      <button
                        onClick={() => open(s, "message")}
                        disabled={busy === s.id}
                        className="rounded-lg border border-sky-500/30 px-3 py-1.5 text-xs font-bold text-sky-300 transition hover:bg-sky-500/10 disabled:opacity-50"
                      >
                        {sent === s.id ? "ارسال شد ✓" : "پیام به کاربر"}
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

      <Modal
        open={dialog !== null}
        onClose={() => busy === null && setDialog(null)}
        title={dialog ? `${dialogTitle[dialog.kind]} — ${dialog.s.userName} · ${dialog.s.seatLabel || `#${dialog.s.seatIndex + 1}`}` : ""}
      >
        {dialog && (
          <div className="grid gap-4">
            <p className="text-xs leading-6 text-white/55">{dialogHint[dialog.kind]}</p>
            {dialog.kind === "message" && (
              <Field label="عنوان پیام">
                <input value={messageTitle} onChange={(e) => setMessageTitle(e.target.value)} maxLength={150} className={inputCls} />
              </Field>
            )}
            <Field label={dialog.kind === "reject" ? "دلیل رد" : dialog.kind === "review" ? "یادداشت" : "متن پیام"}>
              <textarea
                value={dialogText}
                onChange={(e) => setDialogText(e.target.value)}
                rows={5}
                maxLength={2000}
                autoFocus
                className={`${inputCls} h-auto resize-y py-3 leading-7`}
              />
            </Field>
            {dialogError && <p className="text-sm text-rose-300">{dialogError}</p>}
            <div className="flex justify-end gap-2">
              <button
                onClick={() => setDialog(null)}
                disabled={busy !== null}
                className="rounded-lg border border-white/10 px-4 py-2 text-sm font-bold text-white/70 transition hover:bg-white/5 disabled:opacity-50"
              >
                انصراف
              </button>
              <button
                onClick={confirm}
                disabled={busy !== null}
                className={`flex items-center gap-1.5 rounded-lg px-4 py-2 text-sm font-bold transition disabled:opacity-50 ${
                  dialog.kind === "reject" ? "bg-rose-500/20 text-rose-300 hover:bg-rose-500/30" : "bg-[#3a64f2]/25 text-white hover:bg-[#3a64f2]/40"
                }`}
              >
                {busy !== null ? <Spinner /> : dialog.kind === "message" ? "ارسال اعلان و ایمیل" : dialog.kind === "reopen" ? "بازگشایی و ارسال برای کاربر" : dialog.kind === "reject" ? "رد و اطلاع به کاربر" : "ثبت"}
              </button>
            </div>
          </div>
        )}
      </Modal>

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
