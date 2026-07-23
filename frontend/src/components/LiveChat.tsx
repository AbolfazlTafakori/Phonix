"use client";

import { Fragment, useEffect, useRef, useState } from "react";
import { usePathname } from "next/navigation";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { CustomerChatThread, ChatMessage } from "@/lib/types";

function timeOf(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return d.toLocaleTimeString("fa-IR", { hour: "2-digit", minute: "2-digit" });
}

function dayKey(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "" : `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
}

// "امروز" / "دیروز" / a Persian date for the day separators between message groups.
function dayLabel(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  const startOf = (x: Date) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const diff = Math.round((startOf(new Date()) - startOf(d)) / 86400000);
  if (diff <= 0) return "امروز";
  if (diff === 1) return "دیروز";
  return d.toLocaleDateString("fa-IR");
}

function HeadsetIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M5 13a7 7 0 0 1 14 0" />
      <rect x="3.4" y="12" width="3.6" height="6.2" rx="1.8" />
      <rect x="17" y="12" width="3.6" height="6.2" rx="1.8" />
      <path d="M18.8 18.2v1.3a3 3 0 0 1-3 3H13" />
      <circle cx="12" cy="22.5" r="1.15" />
    </svg>
  );
}

// Telegram-style delivery ticks: a clock while sending, a single check once the server has it, and an
// overlapping double check once support has read it.
function Ticks({ state }: { state: "sending" | "sent" | "read" }) {
  if (state === "sending") {
    return (
      <svg viewBox="0 0 24 24" className="h-3 w-3 opacity-70" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
        <circle cx="12" cy="12" r="9" />
        <path d="M12 7.5V12l2.5 2" />
      </svg>
    );
  }
  return (
    <svg viewBox="0 0 16 12" className={`h-3 w-[19px] ${state === "read" ? "opacity-100" : "opacity-70"}`} fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <path d="M1.5 7 4.7 10.2 10.7 3.2" />
      {state === "read" && <path d="M7.6 10.2 13.6 3.2" />}
    </svg>
  );
}

export default function LiveChat() {
  const pathname = usePathname();
  const { user, ready } = useAuth();
  const [open, setOpen] = useState(false);
  const [conv, setConv] = useState<CustomerChatThread | null>(null);
  const [unread, setUnread] = useState(0);
  const [text, setText] = useState("");
  const [sending, setSending] = useState(false);
  // The newest support reply previewed above the bubble while the panel is closed (Intercom-style toast).
  const [notif, setNotif] = useState<{ body: string; time: string } | null>(null);
  const [sessionReady, setSessionReady] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  const sendingRef = useRef(false);
  // Highest admin message id we've already surfaced as a toast, so the same reply never pops twice.
  const notifiedRef = useRef(0);

  // The launcher shows for everyone (guests included) everywhere except the admin panel; the actual
  // conversation still needs a signed-in customer, so a guest who opens it gets a login prompt.
  const hidden = !ready || pathname.startsWith("/admin");

  // Other parts of the site (e.g. the product-page support card) can open the chat panel remotely.
  useEffect(() => {
    const openChat = () => { setOpen(true); setNotif(null); };
    window.addEventListener("phonix:open-chat", openChat);
    return () => window.removeEventListener("phonix:open-chat", openChat);
  }, []);

  // Once per browser session, archive any leftover thread so the customer starts with an empty chat.
  useEffect(() => {
    if (hidden || !user) return;
    const uid = String(user.id);
    const key = "phonix_chat_session";
    if (sessionStorage.getItem(key) === uid) {
      setSessionReady(true);
      return;
    }
    let alive = true;
    api.chat.resetMine().catch(() => {}).finally(() => {
      if (!alive) return;
      sessionStorage.setItem(key, uid);
      setConv(null);
      setUnread(0);
      setSessionReady(true);
    });
    return () => { alive = false; };
  }, [hidden, user?.id]);

  // While closed: poll the thread so the bubble badge and the toast preview stay current even on other pages.
  useEffect(() => {
    if (hidden || open || !sessionReady) return;
    let alive = true;
    const tick = async () => {
      try {
        const c = await api.chat.mine();
        if (!alive) return;
        if (!c) { setUnread(0); return; }
        const unreadAdmin = c.messages.filter((m) => m.fromAdmin && m.id > c.userReadUpTo);
        setUnread(unreadAdmin.length);
        const last = unreadAdmin[unreadAdmin.length - 1];
        if (last && last.id > notifiedRef.current) {
          notifiedRef.current = last.id;
          setNotif({ body: last.body, time: timeOf(last.createdAtUtc) });
        }
      } catch { /* keep last state on a transient error */ }
    };
    tick();
    const id = setInterval(tick, 12000);
    return () => { alive = false; clearInterval(id); };
  }, [hidden, open, sessionReady]);

  // While open: refresh the thread, clear the toast, and keep the read marker current.
  useEffect(() => {
    if (hidden || !open || !sessionReady) return;
    setNotif(null);
    let alive = true;
    const tick = async () => {
      if (sendingRef.current) return;
      try {
        const c = await api.chat.mine();
        if (!alive || sendingRef.current) return;
        setConv(c ?? null);
        setUnread(0);
        if (c) {
          const lastAdmin = c.messages.filter((m) => m.fromAdmin).pop();
          if (lastAdmin) notifiedRef.current = Math.max(notifiedRef.current, lastAdmin.id);
          api.chat.readMine().catch(() => {});
        }
      } catch { /* keep last state on a transient error */ }
    };
    tick();
    const id = setInterval(tick, 2500);
    return () => { alive = false; clearInterval(id); };
  }, [hidden, open, sessionReady]);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight });
  }, [conv?.messages.length, open]);

  if (hidden) return null;

  async function send(e: React.FormEvent) {
    e.preventDefault();
    const body = text.trim();
    if (!body || sending) return;

    const optimistic: ChatMessage = { id: -Date.now(), fromAdmin: false, authorName: "", body, createdAtUtc: new Date().toISOString() };
    setConv((prev) =>
      prev
        ? { ...prev, messages: [...prev.messages, optimistic] }
        : { id: 0, userId: user?.id ?? 0, userName: "", status: "Open", createdAtUtc: optimistic.createdAtUtc, lastMessageAtUtc: optimistic.createdAtUtc, userReadUpTo: 0, adminReadUpTo: 0, messages: [optimistic] },
    );
    setText("");
    setSending(true);
    sendingRef.current = true;
    try {
      const c = await api.chat.send(body);
      setConv(c);
    } catch {
      setText(body);
      setConv((prev) => (prev ? { ...prev, messages: prev.messages.filter((m) => m.id !== optimistic.id) } : prev));
    } finally {
      setSending(false);
      sendingRef.current = false;
    }
  }

  // Telegram-style read receipt for the customer's own messages.
  function tickFor(m: ChatMessage) {
    if (m.id <= 0) return <Ticks state="sending" />;
    const read = !!conv && conv.adminReadUpTo >= m.id;
    return <Ticks state={read ? "read" : "sent"} />;
  }

  const msgs = conv?.messages ?? [];
  const lastMsg = msgs[msgs.length - 1];
  // Support has read the customer's latest, still-unanswered message → show the typing hint.
  const showTyping = open && !!conv && !!lastMsg && !lastMsg.fromAdmin && lastMsg.id > 0 && conv.adminReadUpTo >= lastMsg.id;

  return (
    <div className="no-print fixed bottom-4 right-4 z-[60] flex flex-col items-start gap-3" dir="rtl">
      {open && (
        <div className="flex h-[28rem] max-h-[72vh] w-[22rem] max-w-[calc(100vw-2rem)] flex-col overflow-hidden rounded-2xl border border-[var(--chat-border)] bg-[var(--chat-surface)] shadow-[0_24px_60px_-20px_rgba(0,0,0,0.45)]">
          <div className="flex items-center gap-2.5 border-b border-[var(--chat-border)] bg-gradient-to-l from-[#ff5a1f]/18 via-[#ef233c]/8 to-transparent px-4 py-3">
            <span className="relative grid h-9 w-9 shrink-0 place-items-center rounded-full bg-gradient-to-br from-[#ff7a3c] to-[#ef233c] text-white shadow-[0_0_16px_-4px_rgba(239,35,60,0.7)]">
              <HeadsetIcon className="h-5 w-5" />
              <span className="absolute -bottom-0.5 -right-0.5 h-3 w-3 rounded-full border-2 border-[var(--chat-surface)] bg-emerald-500" />
            </span>
            <div className="min-w-0 flex-1">
              <p className="text-sm font-bold text-[var(--chat-ink)]">پشتیبانی فونیکس</p>
              <p className="flex items-center gap-1.5 text-[11px] text-emerald-500">
                <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" /> آنلاین · معمولاً چند دقیقه
              </p>
            </div>
            <button onClick={() => setOpen(false)} aria-label="بستن" className="grid h-8 w-8 place-items-center rounded-full text-[var(--chat-ink-2)] transition hover:bg-[var(--chat-border)] hover:text-[var(--chat-ink)]">
              <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M18 6 6 18M6 6l12 12" /></svg>
            </button>
          </div>

          {!user ? (
            <div className="flex flex-1 flex-col items-center justify-center gap-4 px-6 text-center">
              <span className="grid h-14 w-14 place-items-center rounded-full bg-gradient-to-br from-[#ff7a3c] to-[#ef233c] text-white">
                <HeadsetIcon className="h-7 w-7" />
              </span>
              <p className="text-sm leading-7 text-[var(--chat-ink-2)]">برای گفتگو با پشتیبانی ابتدا وارد حساب خود شوید.</p>
              <a href="/login" className="grid h-11 w-full place-items-center rounded-xl bg-gradient-to-l from-[#ff5a1f] to-[#ef233c] text-sm font-bold text-white transition hover:brightness-110">
                ورود / ثبت‌نام
              </a>
            </div>
          ) : (<>
          <div ref={scrollRef} className="flex-1 space-y-3 overflow-y-auto px-3.5 py-4">
            {msgs.length === 0 ? (
              <div className="grid h-full place-items-center px-4 text-center">
                <p className="text-sm leading-7 text-[var(--chat-muted)]">سلام! اگر سؤال یا مورد مهمی دارید بنویسید؛ تیم پشتیبانی همین‌جا پاسخ می‌دهد.</p>
              </div>
            ) : (
              msgs.map((m, i) => {
                const prev = msgs[i - 1];
                const showDay = !prev || dayKey(prev.createdAtUtc) !== dayKey(m.createdAtUtc);
                return (
                  <Fragment key={m.id}>
                    {showDay && (
                      <div className="flex justify-center py-1">
                        <span className="rounded-full bg-[var(--chat-bubble)] px-3 py-1 text-[10px] text-[var(--chat-muted)]">{dayLabel(m.createdAtUtc)}</span>
                      </div>
                    )}
                    <div className={`flex ${m.fromAdmin ? "justify-end" : "justify-start"}`}>
                      <div className={`max-w-[80%] rounded-2xl px-3.5 py-2 text-sm leading-6 ${m.fromAdmin ? "rounded-bl-md bg-[var(--chat-bubble)] text-[var(--chat-ink)]" : "rounded-br-md bg-gradient-to-l from-[#ff5a1f] to-[#ef233c] text-white"}`}>
                        {m.fromAdmin && <p className="mb-0.5 text-[10px] font-bold text-[#9db4ff]">{m.authorName}</p>}
                        <p className="whitespace-pre-wrap break-words">{m.body}</p>
                        <p className="mt-1 flex items-center justify-end gap-1 text-[10px] opacity-70">
                          {!m.fromAdmin && tickFor(m)}
                          <span>{timeOf(m.createdAtUtc)}</span>
                        </p>
                      </div>
                    </div>
                  </Fragment>
                );
              })
            )}

            {showTyping && (
              <div className="flex justify-end">
                <div className="flex items-center gap-1 rounded-2xl rounded-bl-md bg-[var(--chat-bubble)] px-4 py-3">
                  {[0, 200, 400].map((d) => (
                    <span key={d} className="h-1.5 w-1.5 rounded-full bg-[var(--chat-muted)] motion-safe:animate-pulse" style={{ animationDelay: `${d}ms`, animationDuration: "1s" }} />
                  ))}
                </div>
              </div>
            )}
          </div>

          <form onSubmit={send} className="flex items-center gap-2 border-t border-[var(--chat-border)] p-2.5">
            <input
              value={text}
              onChange={(e) => setText(e.target.value)}
              placeholder="پیام خود را بنویسید…"
              className="h-11 flex-1 rounded-xl border border-[var(--chat-border)] bg-[var(--chat-surface-2)] px-3.5 text-sm text-[var(--chat-ink)] outline-none transition placeholder:text-[var(--chat-muted)] focus:border-[#ff5a1f]/50"
            />
            <button
              type="submit"
              disabled={sending || !text.trim()}
              aria-label="ارسال"
              className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-gradient-to-l from-[#ff5a1f] to-[#ef233c] text-white transition hover:brightness-110 disabled:opacity-40"
            >
              <svg viewBox="0 0 24 24" className="h-5 w-5 -scale-x-100" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M22 2 11 13M22 2l-7 20-4-9-9-4 20-7Z" /></svg>
            </button>
          </form>
          </>)}
        </div>
      )}

      {!open && notif && (
        <div
          onClick={() => { setOpen(true); setNotif(null); }}
          className="relative flex w-[20rem] max-w-[calc(100vw-2rem)] cursor-pointer gap-3 rounded-2xl border border-[var(--chat-border)] bg-[var(--chat-surface)] p-3.5 shadow-[0_24px_50px_-18px_rgba(0,0,0,0.5)]"
        >
          <button
            onClick={(e) => { e.stopPropagation(); setNotif(null); }}
            aria-label="بستن"
            className="absolute left-2 top-2 grid h-5 w-5 place-items-center rounded-full text-[var(--chat-muted)] transition hover:bg-[var(--chat-border)] hover:text-[var(--chat-ink)]"
          >
            <svg viewBox="0 0 24 24" className="h-3.5 w-3.5" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round"><path d="M18 6 6 18M6 6l12 12" /></svg>
          </button>
          <span className="relative grid h-9 w-9 shrink-0 place-items-center rounded-full bg-gradient-to-br from-[#ff7a3c] to-[#ef233c] text-white">
            <HeadsetIcon className="h-[18px] w-[18px]" />
            <span className="absolute -bottom-0.5 -right-0.5 h-2.5 w-2.5 rounded-full border-2 border-[var(--chat-surface)] bg-emerald-500" />
          </span>
          <div className="min-w-0 flex-1">
            <div className="flex items-center justify-between gap-2 pl-4">
              <b className="text-[12.5px] font-medium text-[var(--chat-ink)]">پشتیبانی فونیکس</b>
              <span className="text-[10px] text-[var(--chat-muted)]">{notif.time}</span>
            </div>
            <p className="mt-1 line-clamp-2 text-[12.5px] leading-6 text-[var(--chat-ink-2)]">{notif.body}</p>
          </div>
        </div>
      )}

      <button
        onClick={() => setOpen((o) => !o)}
        aria-label="گفتگوی زنده"
        className="relative grid h-14 w-14 place-items-center rounded-full bg-gradient-to-br from-[#ff5a1f] to-[#ef233c] text-white shadow-[0_14px_36px_-10px_rgba(239,35,60,0.8)] transition hover:brightness-110 active:scale-95"
      >
        {!open && unread > 0 && (
          <span aria-hidden className="absolute inset-0 rounded-full border-2 border-[#ef233c]/50 motion-safe:animate-ping" />
        )}
        {open ? (
          <svg viewBox="0 0 24 24" className="relative h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M18 6 6 18M6 6l12 12" /></svg>
        ) : (
          <HeadsetIcon className="relative h-7 w-7" />
        )}
        {!open && unread > 0 && (
          <span className="absolute -right-1 -top-1 grid h-6 min-w-6 place-items-center rounded-full border-2 border-[var(--chat-surface)] bg-white px-1 text-[11px] font-bold text-[#ef233c]">
            {unread > 9 ? "۹+" : unread.toLocaleString("fa-IR")}
          </span>
        )}
      </button>
    </div>
  );
}
