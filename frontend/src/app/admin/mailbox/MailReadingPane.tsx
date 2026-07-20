"use client";

import { api } from "@/lib/api";
import type { MailFolder, MailMessage } from "@/lib/types";
import { Card } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";
import MailBody from "./MailBody";

type Props = {
  message: MailMessage;
  folder: string;
  folderKind: MailFolder["kind"];
  targets: { archive?: string; spam?: string; trash?: string };
  onClose: () => void;
  onReply: () => void;
  onForward: () => void;
  onMarkUnread: () => void;
  onMove: (target: string | undefined, label: string) => void;
};

export default function MailReadingPane({
  message,
  folder,
  folderKind,
  targets,
  onClose,
  onReply,
  onForward,
  onMarkUnread,
  onMove,
}: Props) {
  const canReply = Boolean(message.from.address);

  return (
    <Card className="flex h-full flex-col overflow-hidden">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-1.5 border-b border-white/8 p-3">
        <button
          onClick={onClose}
          className="grid h-9 w-9 place-items-center rounded-lg border border-white/10 text-white/55 transition hover:bg-white/5 hover:text-white xl:hidden"
          title="بازگشت"
        >
          →
        </button>
        <ToolbarButton icon="reply" label="پاسخ" onClick={onReply} disabled={!canReply} primary />
        <ToolbarButton icon="forward" label="ارسال به دیگری" onClick={onForward} />
        <ToolbarButton icon="mail" label="علامت خوانده‌نشده" onClick={onMarkUnread} compact />
        {targets.archive && folderKind !== "archive" && (
          <ToolbarButton icon="archive" label="بایگانی" onClick={() => onMove(targets.archive, "بایگانی")} compact />
        )}
        {targets.spam && folderKind !== "spam" && (
          <ToolbarButton icon="spam" label="هرزنامه" onClick={() => onMove(targets.spam, "هرزنامه")} compact />
        )}
        {targets.trash && folderKind !== "trash" && (
          <ToolbarButton icon="trash" label="حذف" onClick={() => onMove(targets.trash, "زباله‌دان")} compact danger />
        )}
      </div>

      {/* Headers */}
      <div className="border-b border-white/8 p-5">
        <h3 className="text-lg font-bold leading-8 text-white">{message.subject}</h3>
        <div className="mt-3 flex flex-wrap items-baseline gap-x-3 gap-y-1 text-sm">
          <span className="font-bold text-white/85">{message.from.name || message.from.address}</span>
          {message.from.name && <span dir="ltr" className="text-xs text-white/40">{message.from.address}</span>}
          <span className="mr-auto text-xs text-white/35">{new Date(message.date).toLocaleString("fa-IR")}</span>
        </div>
        <p className="mt-1.5 text-xs text-white/40">
          به: <span dir="ltr">{message.to.map((a) => a.address).join("، ") || "—"}</span>
        </p>
        {message.cc.length > 0 && (
          <p className="mt-1 text-xs text-white/40">
            رونوشت: <span dir="ltr">{message.cc.map((a) => a.address).join("، ")}</span>
          </p>
        )}
      </div>

      {/* Attachments */}
      {message.attachments.length > 0 && (
        <div className="flex flex-wrap gap-2 border-b border-white/8 p-4">
          {message.attachments.map((a) => (
            <a
              key={a.index}
              href={api.mailbox.attachmentUrl(folder, message.uid, a.index)}
              download={a.fileName}
              className="flex max-w-full items-center gap-2 rounded-xl border border-white/10 px-3 py-2 text-xs text-white/70 transition hover:border-white/25 hover:bg-white/5"
            >
              <AdminIcon name="paperclip" className="h-3.5 w-3.5 shrink-0 text-white/40" />
              <span className="truncate font-bold" dir="auto">{a.fileName}</span>
              <span className="shrink-0 text-white/35">{formatBytes(a.size)}</span>
            </a>
          ))}
        </div>
      )}

      {message.hadRemoteContent && (
        <p className="border-b border-white/8 bg-amber-500/[0.06] px-5 py-2.5 text-xs text-amber-300/85">
          تصاویر این ایمیل برای حفظ حریم خصوصی بارگذاری نشدند. تصاویر پیوست‌شده را می‌توانید از فهرست پیوست‌ها دانلود کنید.
        </p>
      )}

      {/* Body */}
      <div className="min-h-[240px] flex-1 overflow-auto p-5">
        <MailBody html={message.htmlBody} text={message.textBody} className="h-[60vh]" />
      </div>
    </Card>
  );
}

function ToolbarButton({
  icon,
  label,
  onClick,
  disabled,
  primary,
  compact,
  danger,
}: {
  icon: string;
  label: string;
  onClick: () => void;
  disabled?: boolean;
  primary?: boolean;
  compact?: boolean;
  danger?: boolean;
}) {
  const base = "flex h-9 items-center gap-2 rounded-lg px-3 text-xs font-bold transition disabled:opacity-40";
  const tone = primary
    ? "bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-white enabled:hover:brightness-110"
    : danger
      ? "border border-white/10 text-rose-300/80 enabled:hover:bg-rose-500/10 enabled:hover:text-rose-300"
      : "border border-white/10 text-white/60 enabled:hover:bg-white/5 enabled:hover:text-white";

  return (
    <button onClick={onClick} disabled={disabled} title={label} className={`${base} ${tone}`}>
      <AdminIcon name={icon} className="h-4 w-4 shrink-0" />
      {/* On narrow panes the icon carries the meaning and the title attribute covers the rest. */}
      <span className={compact ? "hidden 2xl:inline" : ""}>{label}</span>
    </button>
  );
}

function formatBytes(size: number): string {
  if (size <= 0) return "";
  if (size < 1024) return `${size} B`;
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(0)} KB`;
  return `${(size / (1024 * 1024)).toFixed(1)} MB`;
}
