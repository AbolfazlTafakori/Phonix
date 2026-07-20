"use client";

import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import type { MailConversationDetail, MailThreadMessage } from "@/lib/types";
import { Card, Spinner } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";
import MailBody from "./MailBody";

const MAX_FILE_BYTES = 8 * 1024 * 1024;
const MAX_TOTAL_BYTES = 20 * 1024 * 1024;

// One conversation, rendered as a chat: the customer's messages and our replies stacked in time order, with
// a reply box pinned at the bottom. A reply threads onto the customer's most recent message (replyFolder /
// replyUid from the detail) so their client keeps it on the same thread.
export default function MailThread({
  id,
  onClose,
  onChanged,
}: {
  id: string;
  onClose: () => void;
  onChanged: () => void;
}) {
  const [detail, setDetail] = useState<MailConversationDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  const load = async () => {
    setLoading(true);
    setError("");
    try {
      const d = await api.mailbox.conversation(id);
      setDetail(d);
      // Collapse everything except the last message, the way Gmail opens a long thread — only the newest is
      // expanded, the rest are one-line stubs you can click open.
      setExpanded(new Set(d.messages.length ? [d.messages.length - 1] : []));
      onChanged(); // opening marked inbound messages read; refresh the list badges
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری گفتگو");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  function toggle(i: number) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(i)) next.delete(i);
      else next.add(i);
      return next;
    });
  }

  if (loading) {
    return (
      <Card className="grid h-full min-h-[320px] place-items-center p-12">
        <Spinner className="h-8 w-8" />
      </Card>
    );
  }
  if (error || !detail) {
    return <Card className="p-8 text-center text-sm leading-7 text-rose-400">{error || "گفتگو یافت نشد"}</Card>;
  }

  return (
    <Card className="flex h-full flex-col overflow-hidden">
      <div className="flex items-start justify-between gap-3 border-b border-white/8 p-5">
        <div className="min-w-0">
          <h3 className="truncate text-lg font-bold text-white">{detail.subject}</h3>
          <p className="mt-1 text-xs text-white/45">
            گفتگو با <span className="text-white/70">{detail.party.name || detail.party.address}</span>
            <span dir="ltr" className="mr-1 text-white/35">{detail.party.address}</span>
          </p>
        </div>
        <button
          onClick={onClose}
          className="grid h-9 w-9 shrink-0 place-items-center rounded-lg border border-white/10 text-white/55 transition hover:bg-white/5 hover:text-white xl:hidden"
          title="بازگشت"
        >
          →
        </button>
      </div>

      <div className="flex-1 space-y-3 overflow-auto p-4">
        {detail.messages.map((m, i) => (
          <ThreadMessage key={`${m.folder}-${m.uid}`} message={m} open={expanded.has(i)} onToggle={() => toggle(i)} />
        ))}
      </div>

      <ReplyBox detail={detail} onSent={load} />
    </Card>
  );
}

function ThreadMessage({ message, open, onToggle }: { message: MailThreadMessage; open: boolean; onToggle: () => void }) {
  const mine = !message.fromCustomer; // our reply
  return (
    <div className={`flex ${mine ? "justify-start" : "justify-end"}`}>
      <div
        className={`w-full max-w-[92%] overflow-hidden rounded-2xl border ${
          mine ? "border-[#3a64f2]/25 bg-[#1733d6]/[0.07]" : "border-white/10 bg-white/[0.03]"
        }`}
      >
        <button onClick={onToggle} className="flex w-full items-center gap-2 px-4 py-2.5 text-right">
          <span
            className={`grid h-7 w-7 shrink-0 place-items-center rounded-full text-[11px] font-bold ${
              mine ? "bg-[#3a64f2]/20 text-[#8aa6ff]" : "bg-white/10 text-white/60"
            }`}
          >
            {mine ? "ما" : (message.from.name || message.from.address || "?").trim().charAt(0).toUpperCase()}
          </span>
          <span className="min-w-0 flex-1">
            <span className="block truncate text-xs font-bold text-white/80">
              {mine ? "پشتیبانی" : message.from.name || message.from.address}
            </span>
            {!open && <span className="block truncate text-xs text-white/35">{message.textBody.slice(0, 90)}</span>}
          </span>
          {message.attachments.length > 0 && <AdminIcon name="paperclip" className="h-3.5 w-3.5 shrink-0 text-white/30" />}
          <span className="shrink-0 text-[11px] text-white/35">{new Date(message.date).toLocaleString("fa-IR")}</span>
        </button>

        {open && (
          <div className="px-4 pb-4">
            {message.hadRemoteContent && (
              <p className="mb-2 rounded-lg bg-amber-500/[0.08] px-3 py-2 text-[11px] text-amber-300/85">
                تصاویر این ایمیل برای حفظ حریم خصوصی بارگذاری نشدند.
              </p>
            )}
            <MailBody html={message.htmlBody} text={message.textBody} className="h-[46vh]" />
            {message.attachments.length > 0 && (
              <div className="mt-3 flex flex-wrap gap-2">
                {message.attachments.map((a) => (
                  <a
                    key={a.index}
                    href={api.mailbox.attachmentUrl(message.folder, message.uid, a.index)}
                    download={a.fileName}
                    className="flex max-w-full items-center gap-2 rounded-xl border border-white/10 px-3 py-2 text-xs text-white/70 transition hover:border-white/25 hover:bg-white/5"
                  >
                    <AdminIcon name="paperclip" className="h-3.5 w-3.5 shrink-0 text-white/40" />
                    <span className="truncate font-bold" dir="auto">{a.fileName}</span>
                  </a>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

function ReplyBox({ detail, onSent }: { detail: MailConversationDetail; onSent: () => void }) {
  const [body, setBody] = useState("");
  const [files, setFiles] = useState<File[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const picker = useRef<HTMLInputElement>(null);

  // Without a party address there is no one to reply to (e.g. a malformed sender) — hide the box rather than
  // offer a send that can only fail.
  if (!detail.party.address) {
    return <p className="border-t border-white/8 p-4 text-center text-xs text-white/35">این گفتگو آدرس معتبری برای پاسخ ندارد.</p>;
  }

  function addFiles(picked: FileList | null) {
    if (!picked) return;
    setError("");
    const next = [...files];
    for (const f of Array.from(picked)) {
      if (f.size > MAX_FILE_BYTES) { setError(`حجم فایل «${f.name}» بیش از ۸ مگابایت است.`); continue; }
      if (next.reduce((s, x) => s + x.size, 0) + f.size > MAX_TOTAL_BYTES) { setError("مجموع پیوست‌ها بیش از ۲۰ مگابایت است."); break; }
      next.push(f);
    }
    setFiles(next);
    if (picker.current) picker.current.value = "";
  }

  async function send() {
    if (!body.trim() && files.length === 0) return;
    setBusy(true);
    setError("");
    try {
      await api.mailbox.send({
        to: [detail.party.address],
        subject: detail.subject.toLowerCase().startsWith("re:") ? detail.subject : `Re: ${detail.subject}`,
        body,
        replyToFolder: detail.replyFolder ?? undefined,
        inReplyToUid: detail.replyUid ?? undefined,
        files,
      });
      setBody("");
      setFiles([]);
      onSent();
    } catch (e) {
      setError(e instanceof Error ? e.message : "ارسال پاسخ ناموفق بود");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="border-t border-white/8 p-3">
      {files.length > 0 && (
        <ul className="mb-2 flex flex-wrap gap-2">
          {files.map((f, i) => (
            <li key={`${f.name}-${i}`} className="flex max-w-full items-center gap-2 rounded-lg border border-white/10 bg-white/[0.03] px-2.5 py-1.5 text-xs text-white/70">
              <span className="truncate font-bold" dir="auto">{f.name}</span>
              <button onClick={() => setFiles((p) => p.filter((_, x) => x !== i))} className="shrink-0 text-white/35 hover:text-rose-300">✕</button>
            </li>
          ))}
        </ul>
      )}
      {error && <p className="mb-2 text-xs leading-6 text-rose-400">{error}</p>}
      <div className="flex items-end gap-2">
        <button
          onClick={() => picker.current?.click()}
          disabled={busy}
          title="پیوست فایل"
          className="grid h-11 w-11 shrink-0 place-items-center rounded-xl border border-white/10 text-white/55 transition hover:bg-white/5 hover:text-white disabled:opacity-60"
        >
          <AdminIcon name="paperclip" className="h-4 w-4" />
        </button>
        <input ref={picker} type="file" multiple hidden onChange={(e) => addFiles(e.target.files)} />
        <textarea
          value={body}
          onChange={(e) => setBody(e.target.value)}
          rows={2}
          dir="auto"
          placeholder={`پاسخ به ${detail.party.name || detail.party.address}...`}
          className="flex-1 resize-y rounded-xl border border-white/10 bg-[#0d0d15] px-3 py-2.5 text-sm leading-7 text-white outline-none transition focus:border-[#3a64f2]"
        />
        <button
          onClick={send}
          disabled={busy || (!body.trim() && files.length === 0)}
          className="flex h-11 shrink-0 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50"
        >
          {busy ? <Spinner /> : <AdminIcon name="send" className="h-4 w-4" />}
          پاسخ
        </button>
      </div>
    </div>
  );
}
