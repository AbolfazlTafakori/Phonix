"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api } from "@/lib/api";
import type { MailConversation, MailFolder, MailMessage, MailSummary, MailboxSettings } from "@/lib/types";
import { formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";
import MailReadingPane from "./MailReadingPane";
import MailThread from "./MailThread";
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

  const [settingsOpen, setSettingsOpen] = useState(false);
  const [settings, setSettings] = useState<MailboxSettings | null>(null);
  const [draft, setDraft] = useState<ComposeDraft | null>(null);
  const [notice, setNotice] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [total, setTotal] = useState(0);

  // The inbox is shown as conversations (INBOX + Sent, threaded); every other folder stays a flat message
  // list, because "Sent" or "Trash" as threads would only hide which folder a message is actually in.
  const current = folders.find((f) => f.name === folder);
  const isInbox = (current?.kind ?? (folder === "INBOX" ? "inbox" : "other")) === "inbox";

  // Conversation-mode state
  const [convos, setConvos] = useState<MailConversation[]>([]);
  const [openConvId, setOpenConvId] = useState<string | null>(null);

  // Flat-mode state
  const [items, setItems] = useState<MailSummary[]>([]);
  const [open, setOpen] = useState<MailMessage | null>(null);
  const [openUid, setOpenUid] = useState<number | null>(null);

  // Debounced search — both modes hit the server, so fire on a pause, not per keystroke.
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

  const requestId = useRef(0);
  const loadList = useCallback(async () => {
    const id = ++requestId.current;
    setLoading(true);
    setError("");
    try {
      if (isInbox) {
        const result = await api.mailbox.conversations({ page, pageSize: PAGE_SIZE, search, unreadOnly });
        if (id !== requestId.current) return;
        setConvos(result.items);
        setTotal(result.total);
      } else {
        const result = await api.mailbox.list({ folder, page, pageSize: PAGE_SIZE, search, unreadOnly });
        if (id !== requestId.current) return;
        setItems(result.items);
        setTotal(result.total);
      }
    } catch (e) {
      if (id !== requestId.current) return;
      setConvos([]);
      setItems([]);
      setTotal(0);
      setError(e instanceof Error ? e.message : "خطا در خواندن ایمیل‌ها");
    } finally {
      if (id === requestId.current) setLoading(false);
    }
  }, [isInbox, folder, page, search, unreadOnly]);

  useEffect(() => {
    loadFolders();
    api.mailbox.settings.get().then(setSettings).catch(() => setSettings(null));
  }, [loadFolders]);

  useEffect(() => {
    loadList();
  }, [loadList]);

  async function refreshAll() {
    await Promise.all([loadFolders(), loadList()]);
  }

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const somethingOpen = isInbox ? openConvId !== null : openUid !== null;

  const targets = useMemo(() => {
    const byKind = (kind: MailFolder["kind"]) => folders.find((f) => f.kind === kind)?.name;
    return { archive: byKind("archive"), spam: byKind("spam"), trash: byKind("trash") };
  }, [folders]);

  function selectFolder(name: string) {
    setFolder(name);
    setPage(1);
    setOpen(null);
    setOpenUid(null);
    setOpenConvId(null);
  }

  // ── Flat-mode actions (non-inbox folders) ─────────────────────────────────────────────────────────
  async function openMessage(summary: MailSummary) {
    setOpenUid(summary.uid);
    setOpen(null);
    try {
      const message = await api.mailbox.get(folder, summary.uid);
      setOpen(message);
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

      {/* Folder bar — a single horizontal row so the list and thread get the full width beneath it. Scrolls
          sideways on a narrow screen rather than wrapping into a tall block. */}
      {folders.length > 0 && (
        <div className="mb-4 flex gap-2 overflow-x-auto pb-1">
          {folders.map((f) => (
            <button
              key={f.name}
              onClick={() => selectFolder(f.name)}
              className={`flex shrink-0 items-center gap-2 rounded-xl border px-4 py-2 text-sm transition ${
                folder === f.name
                  ? "border-transparent bg-white/10 font-bold text-white"
                  : "border-white/8 text-white/60 hover:bg-white/5 hover:text-white"
              }`}
            >
              <AdminIcon name={folderIcon[f.kind]} className="h-4 w-4 shrink-0" />
              <span className="whitespace-nowrap">{f.title}</span>
              {f.unread > 0 && (
                <span className="rounded-full bg-[#e60053]/20 px-1.5 text-[11px] font-bold text-[#ff5a8a]">
                  {formatNumber(f.unread)}
                </span>
              )}
            </button>
          ))}
        </div>
      )}

      <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,440px)_minmax(0,1fr)]">
        {/* List column */}
        <Card className={`flex flex-col overflow-hidden ${somethingOpen ? "hidden xl:flex" : ""}`}>
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
              <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
            ) : error ? (
              <p className="p-8 text-center text-sm leading-7 text-rose-400">{error}</p>
            ) : isInbox ? (
              convos.length === 0 ? (
                <p className="p-12 text-center text-sm text-white/35">
                  {search || unreadOnly ? "گفتگویی با این فیلتر پیدا نشد" : "هنوز گفتگویی نیست"}
                </p>
              ) : (
                <ul className="divide-y divide-white/5">
                  {convos.map((c) => (
                    <li key={c.id}>
                      <button
                        onClick={() => setOpenConvId(c.id)}
                        className={`flex w-full items-start gap-2 px-3 py-3 text-right transition ${
                          openConvId === c.id ? "bg-white/[0.06]" : "hover:bg-white/[0.03]"
                        }`}
                      >
                        <div className="min-w-0 flex-1">
                          <div className="flex items-baseline justify-between gap-2">
                            <span className={`truncate text-sm ${c.unread > 0 ? "font-bold text-white" : "text-white/65"}`}>
                              {c.party.name || c.party.address || "(نامشخص)"}
                            </span>
                            <span className="flex shrink-0 items-center gap-1.5">
                              {c.count > 1 && <span className="rounded-full bg-white/10 px-1.5 text-[10px] font-bold text-white/50">{formatNumber(c.count)}</span>}
                              <span className="text-[11px] text-white/35">{shortDate(c.date)}</span>
                            </span>
                          </div>
                          <p className={`mt-0.5 truncate text-sm ${c.unread > 0 ? "font-bold text-white/90" : "text-white/55"}`}>
                            {!c.lastFromCustomer && <span className="text-[#8aa6ff]">↩ </span>}
                            {c.subject}
                          </p>
                          <p className="mt-0.5 truncate text-xs text-white/35">{c.preview}</p>
                        </div>
                        <div className="mt-0.5 flex shrink-0 flex-col items-center gap-1">
                          {c.unread > 0 && <span className="h-2 w-2 rounded-full bg-[#e60053]" />}
                          {c.hasAttachments && <AdminIcon name="paperclip" className="h-3.5 w-3.5 text-white/30" />}
                          {c.flagged && <AdminIcon name="star" className="h-3.5 w-3.5 text-amber-400" />}
                        </div>
                      </button>
                    </li>
                  ))}
                </ul>
              )
            ) : items.length === 0 ? (
              <p className="p-12 text-center text-sm text-white/35">
                {search || unreadOnly ? "ایمیلی با این فیلتر پیدا نشد" : "این پوشه خالی است"}
              </p>
            ) : (
              <ul className="divide-y divide-white/5">
                {items.map((m) => (
                  <li key={m.uid}>
                    <div className={`flex w-full items-start gap-2 px-3 py-3 transition ${openUid === m.uid ? "bg-white/[0.06]" : "hover:bg-white/[0.03]"}`}>
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
                        <p className={`mt-0.5 truncate text-sm ${m.seen ? "text-white/55" : "font-bold text-white/90"}`}>{m.subject}</p>
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
              <button disabled={page <= 1} onClick={() => setPage((p) => p - 1)} className="rounded-lg border border-white/10 px-3 py-1.5 font-bold transition enabled:hover:bg-white/5 disabled:opacity-35">جدیدتر</button>
              <span>صفحه {formatNumber(page)} از {formatNumber(totalPages)}</span>
              <button disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} className="rounded-lg border border-white/10 px-3 py-1.5 font-bold transition enabled:hover:bg-white/5 disabled:opacity-35">قدیمی‌تر</button>
            </div>
          )}
        </Card>

        {/* Detail column */}
        <div className={`${!somethingOpen ? "hidden xl:block" : ""}`}>
          {isInbox ? (
            openConvId === null ? (
              <Card className="grid h-full min-h-[320px] place-items-center p-12 text-center text-sm text-white/30">
                یک گفتگو را از فهرست انتخاب کنید
              </Card>
            ) : (
              <MailThread
                id={openConvId}
                onClose={() => setOpenConvId(null)}
                onChanged={() => {
                  loadFolders();
                  loadList();
                }}
              />
            )
          ) : openUid === null ? (
            <Card className="grid h-full min-h-[320px] place-items-center p-12 text-center text-sm text-white/30">
              یک ایمیل را از فهرست انتخاب کنید
            </Card>
          ) : !open ? (
            <Card className="grid h-full min-h-[320px] place-items-center p-12"><Spinner className="h-8 w-8" /></Card>
          ) : (
            <MailReadingPane
              message={open}
              folder={folder}
              folderKind={current?.kind ?? "other"}
              targets={targets}
              onClose={() => { setOpen(null); setOpenUid(null); }}
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
          onSent={() => { setDraft(null); setNotice("ایمیل ارسال شد."); refreshAll(); }}
        />
      )}

      {settingsOpen && settings && (
        <MailboxSettingsModal
          initial={settings}
          onClose={() => setSettingsOpen(false)}
          onSaved={(next) => { setSettings(next); refreshAll(); }}
        />
      )}
    </div>
  );
}

function shortDate(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  const now = new Date();
  if (date.toDateString() === now.toDateString()) return date.toLocaleTimeString("fa-IR", { hour: "2-digit", minute: "2-digit" });
  if (date.getFullYear() === now.getFullYear()) return date.toLocaleDateString("fa-IR", { month: "short", day: "numeric" });
  return date.toLocaleDateString("fa-IR", { year: "numeric", month: "numeric", day: "numeric" });
}

// Plain-text quote beneath a forward/flat reply. The thread view shows history inline, so this is only used
// by the flat (non-inbox) reading pane.
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
