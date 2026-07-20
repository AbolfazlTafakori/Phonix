"use client";

import { useRef, useState } from "react";
import { api } from "@/lib/api";
import { Modal, Spinner, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

export type ComposeDraft = {
  to: string[];
  cc: string[];
  subject: string;
  body: string;
  // Present only for a reply; carried through so the server can thread it onto the original.
  replyToFolder?: string;
  inReplyToUid?: number;
};

// Mirrors the server's per-file and total caps so an oversized attachment is refused here, with a clear
// message, instead of after the whole upload has been pushed across the wire.
const MAX_FILE_BYTES = 8 * 1024 * 1024;
const MAX_TOTAL_BYTES = 20 * 1024 * 1024;

export default function MailComposer({
  draft,
  onClose,
  onSent,
}: {
  draft: ComposeDraft;
  onClose: () => void;
  onSent: () => void;
}) {
  const [to, setTo] = useState(draft.to.join("، "));
  const [cc, setCc] = useState(draft.cc.join("، "));
  const [showCc, setShowCc] = useState(draft.cc.length > 0);
  const [subject, setSubject] = useState(draft.subject);
  const [body, setBody] = useState(draft.body);
  const [files, setFiles] = useState<File[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const filePicker = useRef<HTMLInputElement>(null);

  const isReply = draft.inReplyToUid !== undefined;

  function addFiles(picked: FileList | null) {
    if (!picked) return;
    setError("");
    const next = [...files];
    for (const file of Array.from(picked)) {
      if (file.size > MAX_FILE_BYTES) {
        setError(`حجم فایل «${file.name}» بیش از ۸ مگابایت است.`);
        continue;
      }
      if (next.reduce((sum, f) => sum + f.size, 0) + file.size > MAX_TOTAL_BYTES) {
        setError("مجموع حجم پیوست‌ها نمی‌تواند بیش از ۲۰ مگابایت باشد.");
        break;
      }
      next.push(file);
    }
    setFiles(next);
    // Reset the picker so choosing the same file twice in a row still fires a change event.
    if (filePicker.current) filePicker.current.value = "";
  }

  async function send() {
    setError("");
    const recipients = splitAddresses(to);
    if (recipients.length === 0) {
      setError("حداقل یک گیرنده وارد کنید.");
      return;
    }
    const invalid = recipients.find((a) => !looksLikeEmail(a));
    if (invalid) {
      setError(`آدرس «${invalid}» معتبر نیست.`);
      return;
    }
    if (!subject.trim() && !body.trim()) {
      setError("موضوع یا متن ایمیل را بنویسید.");
      return;
    }

    setBusy(true);
    try {
      await api.mailbox.send({
        to: recipients,
        cc: splitAddresses(cc),
        subject: subject.trim(),
        body,
        replyToFolder: draft.replyToFolder,
        inReplyToUid: draft.inReplyToUid,
        files,
      });
      onSent();
    } catch (e) {
      setError(e instanceof Error ? e.message : "ارسال ایمیل ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal open onClose={busy ? () => undefined : onClose} title={isReply ? "پاسخ به ایمیل" : "ایمیل جدید"} size="2xl">
      <div className="space-y-4">
        <label className="block">
          <span className="mb-1.5 block text-xs font-medium text-white/55">گیرنده</span>
          <input
            value={to}
            onChange={(e) => setTo(e.target.value)}
            dir="ltr"
            placeholder="name@example.com"
            className={`${inputCls} text-left`}
          />
          <button onClick={() => setShowCc((v) => !v)} className="mt-1.5 text-xs text-[#6f93ff] hover:underline">
            {showCc ? "بستن رونوشت" : "افزودن رونوشت (Cc)"}
          </button>
        </label>

        {showCc && (
          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-white/55">رونوشت</span>
            <input value={cc} onChange={(e) => setCc(e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
          </label>
        )}

        <label className="block">
          <span className="mb-1.5 block text-xs font-medium text-white/55">موضوع</span>
          <input value={subject} onChange={(e) => setSubject(e.target.value)} className={inputCls} />
        </label>

        <label className="block">
          <span className="mb-1.5 block text-xs font-medium text-white/55">متن ایمیل</span>
          <textarea
            value={body}
            onChange={(e) => setBody(e.target.value)}
            rows={11}
            dir="auto"
            className="w-full resize-y rounded-xl border border-white/10 bg-[#0d0d15] px-4 py-3 text-sm leading-8 text-white outline-none transition focus:border-[#3a64f2]"
          />
        </label>

        {files.length > 0 && (
          <ul className="flex flex-wrap gap-2">
            {files.map((file, i) => (
              <li
                key={`${file.name}-${i}`}
                className="flex max-w-full items-center gap-2 rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2 text-xs text-white/70"
              >
                <AdminIcon name="paperclip" className="h-3.5 w-3.5 shrink-0 text-white/40" />
                <span className="truncate font-bold" dir="auto">{file.name}</span>
                <button
                  onClick={() => setFiles((prev) => prev.filter((_, index) => index !== i))}
                  className="shrink-0 text-white/35 transition hover:text-rose-300"
                  title="حذف پیوست"
                >
                  ✕
                </button>
              </li>
            ))}
          </ul>
        )}

        {error && <p className="text-sm leading-7 text-rose-400">{error}</p>}

        <div className="flex flex-wrap items-center gap-2 border-t border-white/8 pt-4">
          <button
            onClick={send}
            disabled={busy}
            className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-7 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
          >
            {busy ? <Spinner /> : <AdminIcon name="send" className="h-4 w-4" />}
            ارسال
          </button>
          <button
            onClick={() => filePicker.current?.click()}
            disabled={busy}
            className="flex h-11 items-center gap-2 rounded-xl border border-white/10 px-4 text-sm font-bold text-white/65 transition hover:bg-white/5 hover:text-white disabled:opacity-60"
          >
            <AdminIcon name="paperclip" className="h-4 w-4" />
            پیوست فایل
          </button>
          <input ref={filePicker} type="file" multiple hidden onChange={(e) => addFiles(e.target.files)} />
          <button
            onClick={onClose}
            disabled={busy}
            className="mr-auto text-sm text-white/45 transition hover:text-white disabled:opacity-60"
          >
            انصراف
          </button>
        </div>
      </div>
    </Modal>
  );
}

// Accepts the separators a Persian keyboard actually produces (، and ؛) alongside the Latin ones, so a
// pasted list of addresses does not silently become one invalid recipient.
function splitAddresses(input: string): string[] {
  return input
    .split(/[,،;؛\s]+/)
    .map((s) => s.trim())
    .filter(Boolean);
}

// Deliberately loose: the server parses addresses properly with MimeKit and rejects what it cannot use.
// This only catches the obvious typo before a round-trip.
function looksLikeEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
}
