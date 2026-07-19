"use client";

import { useRef, useState } from "react";
import { api } from "@/lib/api";
import type { SeatSubmission } from "@/lib/types";

// The information a customer files for ONE seat of a shared account: a picture and a note. Some services can't
// be set up without something from the buyer, and on a multi-seat purchase every person on the account needs to
// send their OWN details — so this form is scoped to the seat currently selected above it, and each seat keeps
// its own picture, text and review state.
//
// Editable until staff review the seat; after that it renders read-only with the reviewer's note (if any), so a
// customer can still see what they sent but can't change what's already being worked on.

const MAX_TEXT = 2000;
const MAX_IMAGE_MB = 6; // matches LocalFileStorageService.MaxBytes — reject before the round-trip

export default function SeatInfoForm({
  orderId,
  unitId,
  seatIndex,
  seatLabel,
  hint,
  submission,
  onSaved,
}: {
  orderId: number;
  unitId: number;
  seatIndex: number;
  seatLabel: string;
  // The plan's own wording for what to send; blank falls back to a generic instruction.
  hint?: string;
  submission?: SeatSubmission;
  onSaved: (s: SeatSubmission) => void;
}) {
  const [text, setText] = useState(submission?.text ?? "");
  // The picture chosen but not yet sent: previewed locally so the customer sees it before saving.
  const [pending, setPending] = useState<{ file: File; preview: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [saved, setSaved] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const locked = submission ? !submission.editable : false;
  // A seat with nothing sent yet, or with changes the customer hasn't saved.
  const dirty = !submission || text !== submission.text || pending !== null;

  function pick(file: File | undefined) {
    setError("");
    if (!file) return;
    if (!file.type.startsWith("image/")) return setError("فقط فایل تصویری قابل ارسال است.");
    if (file.size > MAX_IMAGE_MB * 1024 * 1024) return setError(`حجم تصویر باید کمتر از ${MAX_IMAGE_MB} مگابایت باشد.`);
    setPending({ file, preview: URL.createObjectURL(file) });
  }

  async function save() {
    setBusy(true);
    setError("");
    try {
      // Upload first: the submission stores only the returned id, never the bytes.
      const imageId = pending ? await api.seatInfo.upload(pending.file) : null;
      const result = await api.seatInfo.save({ orderId, unitId, seatIndex, seatLabel, imageId, text: text.trim() });
      onSaved(result);
      setPending(null);
      setSaved(true);
      setTimeout(() => setSaved(false), 2500);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در ذخیره‌ی اطلاعات");
    } finally {
      setBusy(false);
    }
  }

  // What the footer tells the customer about changing this later. Before the first approval editing is free;
  // afterwards it costs one of the allowances the plan granted, and each change re-enters the review queue.
  const approved = submission?.status === "Reviewed" || (submission?.editsUsed ?? 0) > 0;
  const statusNote = !submission
    ? ""
    : !approved
      ? "تا پیش از بررسی، قابل ویرایش است."
      : submission.editsLeft > 0
        ? `${submission.editsLeft} بار دیگر می‌توانید این اطلاعات را تغییر دهید؛ هر تغییر دوباره بررسی می‌شود.`
        : "";

  // What to show in the image slot: a freshly picked file wins, otherwise whatever is already on file.
  const imageSrc = pending?.preview ?? (submission?.imageId ? api.seatInfo.imageSrc(submission.imageId) : null);

  return (
    <div className="space-y-3 rounded-xl p-3" style={{ background: "var(--ac-panel-bg)", border: "1px solid var(--ac-panel-border)" }}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-sm font-bold" style={{ color: "var(--ac-title)" }}>
          اطلاعات این پروفایل
        </span>
        <span className="flex items-center gap-2 text-[11px]">
          <span dir="ltr" className="rounded px-1.5 py-0.5 font-bold" style={{ background: "var(--ac-menu-hover)", color: "var(--ac-muted)", unicodeBidi: "isolate" }}>
            {seatLabel}
          </span>
          {submission && (
            <span
              className="rounded px-1.5 py-0.5 font-bold"
              style={
                locked
                  ? { background: "rgba(16,185,129,0.14)", color: "#059669" }
                  : { background: "rgba(245,158,11,0.14)", color: "#b45309" }
              }
            >
              {locked ? "بررسی شد" : "در انتظار بررسی"}
            </span>
          )}
        </span>
      </div>

      <p className="whitespace-pre-wrap text-[11px] leading-5" style={{ color: "var(--ac-muted)" }}>
        {hint?.trim()
          ? hint.trim()
          : "برای راه‌اندازی این پروفایل به اطلاعات شما نیاز داریم. تصویر و توضیح مربوط به همین پروفایل را وارد کنید؛ اگر اشتراک شما چند کاربره است، برای هر پروفایل جداگانه این کار را انجام دهید."}
      </p>

      {/* image slot — a tap opens the picker; the current or freshly chosen picture previews in place */}
      <div className="space-y-2">
        <span className="text-[11px] font-bold" style={{ color: "var(--ac-muted)" }}>تصویر</span>
        <input
          ref={fileRef}
          type="file"
          accept="image/*"
          className="hidden"
          onChange={(e) => pick(e.target.files?.[0])}
        />
        {imageSrc ? (
          <div className="space-y-2">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={imageSrc}
              alt={`تصویر پروفایل ${seatLabel}`}
              className="max-h-56 w-full rounded-lg object-contain"
              style={{ background: "var(--ac-menu-hover)", border: "1px solid var(--ac-panel-border)" }}
            />
            {!locked && (
              <button
                type="button"
                onClick={() => fileRef.current?.click()}
                className="rounded-lg px-3 py-1.5 text-xs font-bold transition hover:brightness-105"
                style={{ background: "var(--ac-menu-hover)", border: "1px solid var(--ac-panel-border)", color: "var(--ac-text)" }}
              >
                تغییر تصویر
              </button>
            )}
          </div>
        ) : locked ? (
          <p className="text-xs" style={{ color: "var(--ac-muted)" }}>تصویری ارسال نشده است.</p>
        ) : (
          <button
            type="button"
            onClick={() => fileRef.current?.click()}
            className="flex w-full flex-col items-center gap-1 rounded-lg px-3 py-6 text-xs font-bold transition hover:brightness-105"
            style={{ background: "var(--ac-menu-hover)", border: "1px dashed var(--ac-panel-border)", color: "var(--ac-muted)" }}
          >
            <span className="text-lg" aria-hidden>＋</span>
            انتخاب تصویر
            <span className="font-normal">حداکثر {MAX_IMAGE_MB} مگابایت</span>
          </button>
        )}
      </div>

      {/* note */}
      <div className="space-y-2">
        <span className="text-[11px] font-bold" style={{ color: "var(--ac-muted)" }}>توضیحات</span>
        {locked ? (
          <p className="whitespace-pre-wrap rounded-lg px-3 py-2 text-sm" style={{ background: "var(--ac-menu-hover)", border: "1px solid var(--ac-panel-border)", color: "var(--ac-text)" }}>
            {submission?.text || "—"}
          </p>
        ) : (
          <textarea
            value={text}
            onChange={(e) => setText(e.target.value.slice(0, MAX_TEXT))}
            rows={3}
            placeholder="اطلاعات لازم برای این پروفایل را بنویسید…"
            className="w-full resize-y rounded-lg px-3 py-2 text-sm outline-none"
            style={{ background: "var(--ac-menu-hover)", border: "1px solid var(--ac-panel-border)", color: "var(--ac-text)" }}
          />
        )}
      </div>

      {submission?.reviewNote && (
        <p className="rounded-lg px-3 py-2 text-xs leading-5" style={{ background: "rgba(59,130,246,0.10)", border: "1px solid rgba(59,130,246,0.28)", color: "var(--ac-text)" }}>
          <b>پیام پشتیبانی:</b> {submission.reviewNote}
        </p>
      )}

      {error && <p className="text-xs font-bold" style={{ color: "#dc2626" }}>{error}</p>}

      {locked ? (
        <p className="text-[11px]" style={{ color: "var(--ac-muted)" }}>
          این اطلاعات بررسی شده و دیگر قابل ویرایش نیست. برای تغییر با پشتیبانی تماس بگیرید.
        </p>
      ) : (
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="text-[11px]" style={{ color: "var(--ac-muted)" }}>
            {saved ? "ذخیره شد ✓" : statusNote}
          </span>
          <button
            type="button"
            onClick={save}
            disabled={busy || !dirty || (text.trim() === "" && !pending && !submission?.imageId)}
            className="rounded-lg px-4 py-2 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50"
            style={{ background: "linear-gradient(to left, #1733d6, #3a64f2)" }}
          >
            {busy ? "در حال ارسال…" : submission ? "ذخیره‌ی تغییرات" : "ارسال"}
          </button>
        </div>
      )}
    </div>
  );
}
