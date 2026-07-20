"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api } from "@/lib/api";
import type { MailFolder, MailMessage, MailSummary, MailboxSettings } from "@/lib/types";
import { formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";
import MailReadingPane from "./MailReadingPane";
import MailComposer, { type ComposeDraft } from "./MailComposer";
import MailboxSettingsModal from "./MailboxSettingsModal";

const PAGE_SIZE = 25;

const folderIcon: Record<MailFolder["kind"], string> = {
  inbox: "inbox",
  sent: "send",
  drafts: "edit",
  trash: "trash",
  spam: "spam",
  archive: "archive",
  other: "mail",
};

export default function AdminMailboxPage() {
  const [folders, setFolders] = useState<MailFolder[]>([]);
  const [folder, setFolder] = useState("INBOX");
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [unreadOnly, setUnreadOnly] = useState(false);

  const [items, setItems] = useState<MailSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [open, setOpen] = useState<MailMessage | null>(null);
  const [openUid, setOpenUid] = useState<number | null>(null);
  const [draft, setDraft] = useState<ComposeDraft | null>(null);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [settings, setSettings] = useState<MailboxSettings | null>(null);
  const [notice, setNotice] = useState("");

  // The search box drives a server-side IMAP SEARCH, so it is debounced rather than fired per keystroke.
  const [searchInput, setSearchInput] = useState("");
  useEffect(() => {
    const id = setTimeout(() => {
      setSearch(searchInput);
      setPage(1);
    }, 400);
    return () => clearTimeout(id);
  }, [searchInput]);

  const loadFolders = useCallback(async () => {
    try {
      setFolders(await api.mailbox.folders());
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در خواندن پوشه‌ها");
    }
  }, []);

  // A request in flight is abandoned when a newer one starts, so switching folders quickly can never leave
  // the slower response painting over the newer one.
  const requestId = useRef(0);
  const loadMessages = useCallback(async () => {
    const id = ++requestId.current;
    setLoading(true);
    setError("");
    try {
      const result = await api.mailbox.list({ folder, page, pageSize: PAGE_SIZE, search, unreadOnly });
      if (id !== requestId.current) return;
      setItems(result.items);
      setTotal(result.total);
    } catch (e) {
      if (id !== requestId.current) return;
      setItems([]);
      setTotal(0);
      setError(e instanceof Error ? e.message : "خطا در خواندن ایمیل‌ها");
    } finally {
      if (id === requestId.current) setLoading(false);
    }
  }, [folder, page, search, unreadOnly]);

  useEffect(() => {
    loadFolders();
    api.mailbox.settings.get().then(setSettings).catch(() => setSettings(null));
  }, [loadFolders]);

  useEffect(() => {
    loadMessages();
  }, [loadMessages]);

  async function refreshAll() {
    await Promise.all([loadFolders(), loadMessages()]);
  }

  const current = folders.find((f) => f.name === folder);
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  // Where "archive", "spam" and "delete" actually send a message. Resolved from what the server reports it
  // has rather than assumed, so the buttons are hidden on a server that has no such folder instead of
  // failing when pressed.
  const targets = useMemo(() => {
    const byKind = (kind: MailFolder["kind"]) => folders.find((f) => f.kind === kind)?.name;
    return { archive: byKind("archive"), spam: byKind("spam"), trash: byKind("trash") };
  }, [folders]);

  async function openMessage(summary: MailSummary) {
    setOpenUid(summary.uid);
    setOpen(null);
    try {
      const message = await api.mailbox.get(folder, summary.uid);
      setOpen(message);
      // Opening marks read, the way every mail client does. The row updates locally so the list does not
      // have to be refetched just to un-bold one line.
      if (!summary.seen) {
        await api.mailbox.setSeen(folder, summary.uid, true).catch(() => undefined);
        setItems((prev) => prev.map((m) => (m.uid === summary.uid ? { ...m, seen: true } : m)));
        setFolders((prev) => prev.map((f) => (f.name === folder ? { ...f, unread: Math.max(0, f.unread - 1) } : f)));
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در خواندن ایمیل");
      setOpenUid(null);
    }
  }

  async function toggleFlag(summary: MailSummary) {
    const next = !summary.flagged;
    setItems((prev) => prev.map((m) => (m.uid === summary.uid ? { ...m, flagged: next } : m)));
    try {
      await api.mailbox.setFlagged(folder, summary.uid, next);
    } catch {
      setItems((prev) => prev.map((m) => (m.uid === summary.uid ? { ...m, flagged: !next } : m)));
    }
  }

  async function markUnread(uid: number) {
    await api.mailbox.setSeen(folder, uid, false).catch(() => undefined);
    setItems((prev) => prev.map((m) => (m.uid === uid ? { ...m, seen: false } : m)));
    setOpen(null);
    setOpenUid(null);
    loadFolders();
  }

  async function moveTo(uid: number, target: string | undefined, label: string) {
    if (!target) return;
    try {
      await api.mailbox.move(folder, uid, target);
      setItems((prev) => prev.filter((m) => m.uid !== uid));
      setTotal((t) => Math.max(0, t - 1));
      setOpen(null);
      setOpenUid(null);
      setNotice(`پیام به «${label}» منتقل شد.`);
      loadFolders();
    } catch (e) {
      setError(e instanceof Error ? e.message : "انتقال پیام ناموفق بود");
    }
  }

  function replyTo(message: MailMessage) {
    setDraft({
      to: [message.from.address].filter(Boolean),
      cc: [],
      subject: message.subject.toLowerCase().startsWith("re:") ? message.subject : `Re: ${message.subject}`,
      body: `\n\n${quote(message)}`,
      replyToFolder: folder,
      inReplyToUid: message.uid,
    });
  }

  function forward(message: MailMessage) {
    setDraft({
      to: [],
      cc: [],
      subject: message.subject.toLowerCase().startsWith("fwd:") ? message.subject : `Fwd: ${message.subject}`,
      body: `\n\n${quote(message)}`,
    });
  }

  const configured = settings?.enabled && settings.imapHost;

  return (
    <div>
      <PageHeader
        title="صندوق ایمیل"
        desc="خواندن و پاسخ به ایمیل‌هایی که کاربران به آدرس فروشگاه می‌فرستند"
        action={
          <div className="flex flex-wrap items-center gap-2">
            <button
              onClick={refreshAll}
              className="flex h-10 items-center gap-2 rounded-xl border border-white/10 px-4 text-sm font-bold text-white/70 transition hover:bg-white/5 hover:text-white"
            >
              <AdminIcon name="refresh" className="h-4 w-4" />
              تازه‌سازی
            </button>
            {settings && (
              <button
                onClick={() => setSettingsOpen(true)}
                className="flex h-10 items-center gap-2 rounded-xl border border-white/10 px-4 text-sm font-bold text-white/70 transition hover:bg-white/5 hover:text-white"
              >
                <AdminIcon name="settings" className="h-4 w-4" />
                تنظیمات
              </button>
            )}
            <button
              onClick={() => setDraft({ to: [], cc: [], subject: "", body: "" })}
              className="flex h-10 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 text-sm font-bold text-white transition hover:brightness-110"
            >
              <AdminIcon name="plus" className="h-4 w-4" />
              نوشتن ایمیل
            </button>
          </div>
        }
      />

      {notice && (
        <div className="mb-4 flex items-center justify-between gap-3 rounded-xl border border-emerald-500/20 bg-emerald-500/10 px-4 py-2.5 text-sm text-emerald-300">
          {notice}
          <button onClick={() => setNotice("")} className="text-emerald-300/60 hover:text-emerald-200">✕</button>
        </div>
      )}

      {settings && !configured && (
        <Card className="mb-5 flex flex-wrap items-center justify-between gap-3 border-amber-500/20 bg-amber-500/[0.06] p-5">
          <div>
            <p className="font-bold text-amber-300">صندوق ایمیل هنوز فعال نشده است</p>
            <p className="mt-1 text-sm text-white/50">
              اطلاعات IMAP و SMTP صندوق را وارد کنید تا ایمیل‌های دریافتی اینجا نمایش داده شود.
            </p>
          </div>
          <button
            onClick={() => setSettingsOpen(true)}
            className="rounded-xl border border-amber-400/30 px-4 py-2 text-sm font-bold text-amber-300 transition hover:bg-amber-400/10"
          >
            تنظیم صندوق
          </button>
        </Card>
      )}

      <div className="grid gap-4 lg:grid-cols-[220px_minmax(0,1fr)] xl:grid-cols-[220px_380px_minmax(0,1fr)]">
        {/* Folder rail */}
        <Card className="h-max p-2">
          {folders.length === 0 ? (
            <p className="p-4 text-center text-sm text-white/35">پوشه‌ای یافت نشد</p>
          ) : (
            folders.map((f) => (
              <button
                key={f.name}
                onClick={() => {
                  setFolder(f.name);
                  setPage(1);
                  setOpen(null);
                  setOpenUid(null);
                }}
                className={`flex w-full items-center gap-2.5 rounded-xl px-3 py-2.5 text-sm transition ${
                  folder === f.name ? "bg-white/10 font-bold text-white" : "text-white/60 hover:bg-white/5 hover:text-white"
                }`}
              >
                <AdminIcon name={folderIcon[f.kind]} className="h-4 w-4 shrink-0" />
                <span className="flex-1 truncate text-right">{f.title}</span>
                {f.unread > 0 && (
                  <span className="rounded-full bg-[#e60053]/20 px-1.5 text-[11px] font-bold text-[#ff5a8a]">
                    {formatNumber(f.unread)}
                  </span>
                )}
              </button>
            ))
          )}
        </Card>

        {/* Message list */}
        <Card className={`flex flex-col overflow-hidden ${openUid !== null ? "hidden xl:flex" : ""}`}>
          <div className="flex flex-wrap items-center gap-2 border-b border-white/8 p-3">
            <div className="relative flex-1 min-w-[160px]">
              <AdminIcon name="search" className="pointer-events-none absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 text-white/30" />
              <input
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                placeholder="جستجو در فرستنده، موضوع و متن..."
                className={`${inputCls} h-10 pr-9`}
              />
            </div>
            <button
              onClick={() => {
                setUnreadOnly((v) => !v);
                setPage(1);
              }}
              className={`h-10 whitespace-nowrap rounded-xl border px-3 text-xs font-bold transition ${
                unreadOnly ? "border-transparent bg-white/10 text-white" : "border-white/10 text-white/55 hover:text-white"
              }`}
            >
              خوانده‌نشده
            </button>
          </div>

          <div className="min-h-[320px] flex-1">
            {loading ? (
              <div className="grid place-items-center py-24">
                <Spinner className="h-8 w-8" />
              </div>
            ) : error ? (
              <p className="p-8 text-center text-sm leading-7 text-rose-400">{error}</p>
            ) : items.length === 0 ? (
              <p className="p-12 text-center text-sm text-white/35">
                {search || unreadOnly ? "ایمیلی با این فیلتر پیدا نشد" : "این پوشه خالی است"}
              </p>
            ) : (
              <ul className="divide-y divide-white/5">
                {items.map((m) => (
                  <li key={m.uid}>
                    <div
                      className={`flex w-full items-start gap-2 px-3 py-3 transition ${
                        openUid === m.uid ? "bg-white/[0.06]" : "hover:bg-white/[0.03]"
                      }`}
                    >
                      <button
                        onClick={() => toggleFlag(m)}
                        title={m.flagged ? "برداشتن ستاره" : "ستاره‌دار کردن"}
                        className={`mt-0.5 shrink-0 transition ${m.flagged ? "text-amber-400" : "text-white/20 hover:text-white/50"}`}
                      >
                        <AdminIcon name="star" className="h-4 w-4" />
                      </button>
                      <button onClick={() => openMessage(m)} className="min-w-0 flex-1 text-right">
                        <div className="flex items-baseline justify-between gap-2">
                          <span className={`truncate text-sm ${m.seen ? "text-white/65" : "font-bold text-white"}`}>
                            {m.from.name || m.from.address || "(نامشخص)"}
                          </span>
                          <span className="shrink-0 text-[11px] text-white/35">{shortDate(m.date)}</span>
                        </div>
                        <p className={`mt-0.5 truncate text-sm ${m.seen ? "text-white/55" : "font-bold text-white/90"}`}>
                          {m.subject}
                        </p>
                        <p className="mt-0.5 truncate text-xs text-white/35">{m.preview}</p>
                      </button>
                      {m.hasAttachments && <AdminIcon name="paperclip" className="mt-1 h-3.5 w-3.5 shrink-0 text-white/30" />}
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>

          {totalPages > 1 && (
            <div className="flex items-center justify-between border-t border-white/8 px-3 py-2.5 text-xs text-white/50">
              <button
                disabled={page <= 1}
                onClick={() => setPage((p) => p - 1)}
                className="rounded-lg border border-white/10 px-3 py-1.5 font-bold transition enabled:hover:bg-white/5 disabled:opacity-35"
              >
                جدیدتر
              </button>
              <span>
                صفحه {formatNumber(page)} از {formatNumber(totalPages)} · {formatNumber(total)} پیام
              </span>
              <button
                disabled={page >= totalPages}
                onClick={() => setPage((p) => p + 1)}
                className="rounded-lg border border-white/10 px-3 py-1.5 font-bold transition enabled:hover:bg-white/5 disabled:opacity-35"
              >
                قدیمی‌تر
              </button>
            </div>
          )}
        </Card>

        {/* Reading pane */}
        <div className={`lg:col-span-2 xl:col-span-1 ${openUid === null ? "hidden xl:block" : ""}`}>
          {openUid === null ? (
            <Card className="grid h-full min-h-[320px] place-items-center p-12 text-center text-sm text-white/30">
              یک ایمیل را از فهرست انتخاب کنید
            </Card>
          ) : !open ? (
            <Card className="grid h-full min-h-[320px] place-items-center p-12">
              <Spinner className="h-8 w-8" />
            </Card>
          ) : (
            <MailReadingPane
              message={open}
              folder={folder}
              folderKind={current?.kind ?? "other"}
              targets={targets}
              onClose={() => {
                setOpen(null);
                setOpenUid(null);
              }}
              onReply={() => replyTo(open)}
              onForward={() => forward(open)}
              onMarkUnread={() => markUnread(open.uid)}
              onMove={(target, label) => moveTo(open.uid, target, label)}
            />
          )}
        </div>
      </div>

      {draft && (
        <MailComposer
          draft={draft}
          onClose={() => setDraft(null)}
          onSent={() => {
            setDraft(null);
            setNotice("ایمیل ارسال شد.");
            refreshAll();
          }}
        />
      )}

      {settingsOpen && settings && (
        <MailboxSettingsModal
          initial={settings}
          onClose={() => setSettingsOpen(false)}
          onSaved={(next) => {
            setSettings(next);
            refreshAll();
          }}
        />
      )}
    </div>
  );
}

// Today shows a time, this year shows a day and month, anything older shows the year — the same compression
// a mail client uses so the date column stays narrow and still tells you what you need.
function shortDate(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  const now = new Date();
  const sameDay = date.toDateString() === now.toDateString();
  if (sameDay) return date.toLocaleTimeString("fa-IR", { hour: "2-digit", minute: "2-digit" });
  if (date.getFullYear() === now.getFullYear())
    return date.toLocaleDateString("fa-IR", { month: "short", day: "numeric" });
  return date.toLocaleDateString("fa-IR", { year: "numeric", month: "numeric", day: "numeric" });
}

// The quoted original beneath a reply. Plain text on purpose — the composer sends plain text, so quoting
// the HTML body would only put unrendered markup in the customer's mail.
function quote(message: MailMessage): string {
  const source = message.textBody || stripTags(message.htmlBody);
  const when = new Date(message.date).toLocaleString("fa-IR");
  const who = message.from.name ? `${message.from.name} <${message.from.address}>` : message.from.address;
  const quoted = source.split("\n").slice(0, 200).map((line) => `> ${line}`).join("\n");
  return `در ${when}، ${who} نوشت:\n${quoted}`;
}

function stripTags(html: string): string {
  return html
    .replace(/<br\s*\/?>/gi, "\n")
    .replace(/<\/p>/gi, "\n")
    .replace(/<[^>]+>/g, "")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}
