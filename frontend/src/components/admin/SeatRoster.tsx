"use client";

import { useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { SeatRow } from "@/lib/types";
import { Card, Modal, Spinner, inputCls } from "./ui";

// Purchased seats, grouped by the account they were delivered on — the view staff work from when they are
// chasing missing details or closing a service.
//
// One card per delivered account, one line per seat inside it, so «which block is this and whose account is
// it» is answered by looking: the card carries the order, the buyer and the shared account's own login, and
// each line carries its own block label («A - 2») with what was filed for it.
//
// Every action is per SEAT, selected by checkbox. That is the whole point on a shared account: one person
// breaking the terms must not cost the other users of the same account their service. A one-seat account has
// a single line, which is pre-selected, so the same machinery reads as a plain confirmation there.

type Action = "warn1" | "warn2" | "revoke" | "restore";

const actionTitle: Record<Action, string> = {
  warn1: "ارسال هشدار اول",
  warn2: "ارسال هشدار دوم",
  revoke: "خروج از گارانتی",
  restore: "بازگشایی گارانتی",
};

const faDate = (iso?: string | null) =>
  iso ? new Date(iso).toLocaleDateString("fa-IR", { year: "numeric", month: "2-digit", day: "2-digit" }) : "";

const seatName = (r: SeatRow) => r.seatLabel || `پروفایل ${r.seatIndex + 1}`;

// What the buyer is told, ready to send and open to editing — staff change the wording per case, which is why
// this is a starting point rather than a fixed message.
function warningTemplate(level: 1 | 2, rows: SeatRow[]) {
  const first = rows[0];
  const seats = rows.map(seatName).join("، ");
  const who = `«${first.productName}» (${seats}) در سفارش ${first.orderCode}`;
  return level === 1
    ? `کاربر گرامی، سلام.\nمشخصات ${who} هنوز ثبت نشده است. لطفاً از بخش «سفارش‌های من» در حساب کاربری خود، اطلاعات این سرویس را تا ۴۸ ساعت آینده ثبت کنید.\nدر صورت ثبت نشدن اطلاعات، سرویس از گارانتی خارج خواهد شد.`
    : `کاربر گرامی، سلام.\nاین دومین و آخرین هشدار است: مشخصات ${who} همچنان ثبت نشده است. در صورت ثبت نشدن اطلاعات تا ۲۴ ساعت آینده، سرویس شما از گارانتی خارج می‌شود و دسترسی به اطلاعات آن بسته خواهد شد.`;
}

function StatusChips({ row }: { row: SeatRow }) {
  const s = row.submission;
  const chips: { text: string; cls: string }[] = [];
  if (s?.revoked) chips.push({ text: "خارج از گارانتی", cls: "bg-rose-500/15 text-rose-300" });
  if (!s || s.status === "NotFiled") chips.push({ text: "ثبت نشده", cls: "bg-white/10 text-white/60" });
  else if (s.status === "Pending")
    chips.push(
      // Reopened and not saved since (the buyer's next save clears the flag) = waiting on them, not on staff.
      s.reopenedForEdit
        ? { text: "بازگشایی شده — در انتظار کاربر", cls: "bg-amber-500/15 text-amber-300" }
        : { text: "در انتظار بررسی", cls: "bg-amber-500/15 text-amber-300" },
    );
  else if (s.status === "Reviewed") chips.push({ text: "بررسی شده", cls: "bg-emerald-500/15 text-emerald-400" });
  else if (s.status === "Rejected") chips.push({ text: "رد شده", cls: "bg-rose-500/15 text-rose-300" });
  if (s?.warning1AtUtc) chips.push({ text: "هشدار ۱ ✓", cls: "bg-orange-500/15 text-orange-300" });
  if (s?.warning2AtUtc) chips.push({ text: "هشدار ۲ ✓", cls: "bg-orange-500/20 text-orange-200" });
  return (
    <>
      {chips.map((c) => (
        <span key={c.text} className={`rounded-md px-2 py-0.5 text-[11px] font-bold ${c.cls}`}>
          {c.text}
        </span>
      ))}
    </>
  );
}

export default function SeatRoster({
  rows,
  loading,
  empty,
  onChanged,
}: {
  rows: SeatRow[];
  loading?: boolean;
  empty: string;
  // Called after any action lands, so the page reloads the list it is showing.
  onChanged: () => void;
}) {
  // Selection is keyed by order:unit:seat, so it survives regrouping and never collides across accounts.
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [action, setAction] = useState<{ kind: Action; rows: SeatRow[] } | null>(null);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [zoom, setZoom] = useState<string | null>(null);

  const key = (r: SeatRow) => `${r.orderId}:${r.unitId}:${r.seatIndex}`;

  // Seats grouped by the account they were delivered on, newest order first (the API already orders them).
  const units = useMemo(() => {
    const map = new Map<string, SeatRow[]>();
    for (const r of rows) {
      const k = `${r.orderId}:${r.unitId}`;
      const list = map.get(k);
      if (list) list.push(r);
      else map.set(k, [r]);
    }
    for (const list of map.values()) list.sort((a, b) => a.seatIndex - b.seatIndex);
    return [...map.entries()];
  }, [rows]);

  function toggle(r: SeatRow) {
    setPicked((p) => {
      const next = new Set(p);
      const k = key(r);
      if (next.has(k)) next.delete(k);
      else next.add(k);
      return next;
    });
  }

  // Which seats of this account an action applies to: the ones ticked, or — when nothing is ticked and the
  // account has a single seat — that seat, because "which user" has only one answer there.
  function targets(list: SeatRow[]) {
    const chosen = list.filter((r) => picked.has(key(r)));
    if (chosen.length > 0) return chosen;
    return list.length === 1 ? list : [];
  }

  function open(kind: Action, list: SeatRow[]) {
    const chosen = targets(list);
    if (chosen.length === 0) {
      setError("ابتدا پروفایل مورد نظر را انتخاب کنید.");
      return;
    }
    setError("");
    setMessage(kind === "warn1" ? warningTemplate(1, chosen) : kind === "warn2" ? warningTemplate(2, chosen) : "");
    setAction({ kind, rows: chosen });
  }

  async function confirm() {
    if (action === null) return;
    const { kind, rows: chosen } = action;
    const body = {
      orderId: chosen[0].orderId,
      unitId: chosen[0].unitId,
      seatIndexes: chosen.map((r) => r.seatIndex),
    };
    setBusy(true);
    setError("");
    try {
      if (kind === "warn1" || kind === "warn2")
        await api.seatInfo.warn({ ...body, level: kind === "warn1" ? 1 : 2, message: message.trim() });
      else if (kind === "revoke") await api.seatInfo.revoke({ ...body, reason: message.trim() });
      else await api.seatInfo.restore(body);
      setAction(null);
      setPicked(new Set());
      onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در انجام عملیات");
    } finally {
      setBusy(false);
    }
  }

  if (loading) return <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>;
  if (rows.length === 0) return <Card className="p-10 text-center text-sm text-white/40">{empty}</Card>;

  return (
    <div className="space-y-3">
      {error && <Card className="p-4 text-sm text-rose-400">{error}</Card>}

      {units.map(([k, list]) => {
        const head = list[0];
        const chosen = targets(list);
        const anyRevoked = chosen.some((r) => r.submission?.revoked);
        const allRevoked = chosen.length > 0 && chosen.every((r) => r.submission?.revoked);
        return (
          <Card key={k} className="p-4">
            {/* which order, which buyer, which account */}
            <div className="flex flex-wrap items-center gap-2 text-xs">
              <span className="font-mono font-bold text-white/80">{head.orderCode}</span>
              <span className="text-white/60">{head.userName}</span>
              {head.userEmail && <span dir="ltr" className="text-white/35" style={{ unicodeBidi: "isolate" }}>{head.userEmail}</span>}
              {head.userPhone && <span dir="ltr" className="text-white/35" style={{ unicodeBidi: "isolate" }}>{head.userPhone}</span>}
              <span className="rounded-md bg-white/10 px-2 py-0.5 text-white/60">{head.productName}</span>
              {head.orderStatus === "Cancelled" && (
                /* Searchable, never chased: the purchase was cancelled and refunded. */
                <span className="rounded-md bg-rose-500/10 px-2 py-0.5 font-bold text-rose-300/80">سفارش لغو شده</span>
              )}
              {head.legacyOrderDelivery && (
                /* An old order whose delivery sits on the ORDER, not on an account of it. */
                <span className="rounded-md bg-white/5 px-2 py-0.5 text-white/45">سفارش قدیمی — بدون تفکیک اکانت</span>
              )}
              {head.plan && <span className="rounded-md bg-white/5 px-2 py-0.5 text-white/45">{head.plan}</span>}
              {head.unitCount > 1 && <span className="text-white/35">اکانت {head.unitIndex}</span>}
              <span className="mr-auto text-white/35">{head.deliveredAt || faDate(head.submission?.createdAtUtc)}</span>
            </div>

            <div className="mt-2 flex flex-wrap items-center gap-2 text-[11px]">
              {head.accountUsername && (
                <span dir="ltr" className="rounded-md bg-sky-500/10 px-2 py-0.5 font-bold text-sky-300" style={{ unicodeBidi: "isolate" }}>
                  {`account: ${head.accountUsername}`}
                </span>
              )}
              <span className="text-white/35">{list.length} پروفایل</span>
              {!head.collectsInfo && <span className="text-white/30">این پلن اطلاعاتی از کاربر نمی‌خواهد</span>}
              {/* what the buyer entered at checkout for this account — the other place a device model lives */}
              {head.deviceInfo.map((d, i) => (
                <span key={i} className="rounded-md bg-white/5 px-2 py-0.5 text-white/50">
                  {d.label}: <span dir="ltr" style={{ unicodeBidi: "isolate" }}>{d.value}</span>
                </span>
              ))}
            </div>

            {/* one line per seat */}
            <div className="mt-3 space-y-2">
              {list.map((r) => {
                const s = r.submission;
                return (
                  <div
                    key={key(r)}
                    className={`rounded-xl border p-3 transition ${
                      picked.has(key(r)) ? "border-[#3a64f2]/60 bg-[#3a64f2]/[0.06]" : "border-white/8 bg-white/[0.02]"
                    }`}
                  >
                    <div className="flex flex-wrap items-center gap-2 text-xs">
                      <input
                        type="checkbox"
                        checked={picked.has(key(r))}
                        onChange={() => toggle(r)}
                        className="h-4 w-4 accent-[#3a64f2]"
                        aria-label={`انتخاب ${seatName(r)}`}
                      />
                      <span dir="ltr" className="rounded-md bg-sky-500/15 px-2 py-0.5 font-bold text-sky-300" style={{ unicodeBidi: "isolate" }}>
                        {seatName(r)}
                      </span>
                      <StatusChips row={r} />
                    </div>

                    {(s?.text || s?.imageId) && (
                      <div className="mt-2 flex items-start gap-3">
                        {s.imageId && (
                          <button
                            type="button"
                            onClick={() => setZoom(api.seatInfo.imageSrc(s.imageId!))}
                            className="shrink-0 overflow-hidden rounded-lg border border-white/10 transition hover:border-[#3a64f2]/60"
                            title="نمایش در اندازه کامل"
                          >
                            {/* eslint-disable-next-line @next/next/no-img-element */}
                            <img src={api.seatInfo.imageSrc(s.imageId)} alt={`تصویر ${seatName(r)}`} className="h-20 w-28 object-cover" />
                          </button>
                        )}
                        {s.text && (
                          <p className="whitespace-pre-wrap rounded-lg bg-white/[0.03] p-2 text-xs text-white/75">{s.text}</p>
                        )}
                      </div>
                    )}

                    {s?.revoked && (
                      <p className="mt-2 rounded-lg border border-rose-500/25 bg-rose-500/[0.07] p-2 text-[11px] text-rose-200">
                        <b>دلیل خروج از گارانتی:</b> {s.revokeReason}
                        {s.revokedBy ? ` — ${s.revokedBy}` : ""} {faDate(s.revokedAtUtc)}
                      </p>
                    )}
                    {s?.lastWarningText && !s.revoked && (
                      <p className="mt-2 whitespace-pre-wrap rounded-lg border border-orange-500/20 bg-orange-500/[0.05] p-2 text-[11px] text-orange-200/90">
                        <b>آخرین هشدار ارسالی:</b> {s.lastWarningText}
                      </p>
                    )}
                  </div>
                );
              })}
            </div>

            {/* actions — they apply to the ticked seats (or to the only seat, if there is just one) */}
            <div className="mt-3 flex flex-wrap items-center gap-2">
              <span className="text-[11px] text-white/35">
                {chosen.length > 0 ? `${chosen.length} پروفایل انتخاب شده` : "پروفایل را انتخاب کنید"}
              </span>
              {!allRevoked && (
                <>
                  <button
                    onClick={() => open("warn1", list)}
                    className="rounded-lg border border-orange-500/30 px-3 py-1.5 text-xs font-bold text-orange-300 transition hover:bg-orange-500/10"
                  >
                    هشدار ۱
                  </button>
                  <button
                    onClick={() => open("warn2", list)}
                    className="rounded-lg border border-orange-500/40 px-3 py-1.5 text-xs font-bold text-orange-200 transition hover:bg-orange-500/10"
                  >
                    هشدار ۲
                  </button>
                  <button
                    onClick={() => open("revoke", list)}
                    className="rounded-lg border border-rose-500/30 px-3 py-1.5 text-xs font-bold text-rose-300 transition hover:bg-rose-500/10"
                  >
                    خروج از گارانتی
                  </button>
                </>
              )}
              {anyRevoked && (
                <button
                  onClick={() => open("restore", list)}
                  className="rounded-lg border border-emerald-500/30 px-3 py-1.5 text-xs font-bold text-emerald-400 transition hover:bg-emerald-500/10"
                >
                  بازگشایی گارانتی
                </button>
              )}
            </div>
          </Card>
        );
      })}

      {/* confirmation — a warning is sent as written, a revocation needs a reason the buyer will read */}
      <Modal open={action !== null} onClose={() => (busy ? null : setAction(null))} title={action ? actionTitle[action.kind] : ""} size="lg">
        {action && (
          <div className="space-y-4">
            <div className="rounded-xl border border-white/10 bg-white/[0.03] p-3 text-xs text-white/70">
              <div className="font-bold text-white/85">{action.rows[0].productName} — سفارش {action.rows[0].orderCode}</div>
              <div className="mt-1">خریدار: {action.rows[0].userName}</div>
              <div className="mt-1" dir="ltr" style={{ unicodeBidi: "isolate" }}>
                {action.rows.map(seatName).join(" · ")}
              </div>
            </div>

            {action.kind === "restore" ? (
              <p className="text-sm text-white/70">
                گارانتی این پروفایل‌ها باز می‌شود؛ کاربر دوباره اطلاعات اکانت را می‌بیند و می‌تواند مشخصات را ثبت کند.
                به او ایمیل و اعلان اطلاع‌رسانی می‌شود.
              </p>
            ) : (
              <label className="block">
                <span className="mb-1.5 block text-xs font-medium text-white/55">
                  {action.kind === "revoke"
                    ? "دلیل خروج از گارانتی (الزامی) — این متن به‌صورت بولد جای اطلاعات اکانت به کاربر نشان داده می‌شود"
                    : "متن پیام — همین متن با ایمیل و اعلان برای کاربر ارسال می‌شود"}
                </span>
                <textarea
                  value={message}
                  onChange={(e) => setMessage(e.target.value)}
                  rows={action.kind === "revoke" ? 4 : 7}
                  className={`${inputCls} h-auto py-3 leading-7`}
                  placeholder={action.kind === "revoke" ? "مثلاً: تغییر گذرواژه‌ی اکانت برخلاف قوانین" : ""}
                />
              </label>
            )}

            {action.kind === "revoke" && (
              <p className="rounded-xl border border-rose-500/25 bg-rose-500/[0.07] p-3 text-xs text-rose-200">
                با تأیید، دسترسی این پروفایل‌ها به اطلاعات اکانت بسته می‌شود، لوگوی سرویس در داشبورد کاربر
                خاکستری می‌شود و به او ایمیل و اعلان ارسال می‌گردد. دیگر پروفایل‌های همین اکانت دست‌نخورده می‌مانند.
              </p>
            )}

            {error && <p className="text-sm text-rose-400">{error}</p>}

            <div className="flex items-center justify-end gap-2">
              <button
                onClick={() => setAction(null)}
                disabled={busy}
                className="rounded-xl border border-white/15 px-4 py-2 text-sm font-bold text-white/70 transition hover:bg-white/10 disabled:opacity-50"
              >
                انصراف
              </button>
              <button
                onClick={confirm}
                disabled={busy || (action.kind !== "restore" && message.trim().length === 0)}
                className="rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 py-2 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50"
              >
                {busy ? "..." : "تأیید"}
              </button>
            </div>
          </div>
        )}
      </Modal>

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
