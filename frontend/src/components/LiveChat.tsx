"use client";

import { useEffect, useRef, useState } from "react";
import { usePathname } from "next/navigation";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { CustomerChatThread, ChatMessage } from "@/lib/types";

function timeOf(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return d.toLocaleTimeString("fa-IR", { hour: "2-digit", minute: "2-digit" });
}

export default function LiveChat() {
  const pathname = usePathname();
  const { user, ready } = useAuth();
  const [open, setOpen] = useState(false);
  const [conv, setConv] = useState<CustomerChatThread | null>(null);
  const [unread, setUnread] = useState(0);
  const [text, setText] = useState("");
  const [sending, setSending] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  // Lets the background poll skip a tick mid-send so it can't briefly wipe the optimistic message.
  const sendingRef = useRef(false);

  // The widget belongs to the storefront/account experience, never the admin panel.
  const hidden = !ready || !user || pathname.startsWith("/admin");

  // Poll for unread support replies while the panel is closed, so the bubble shows a badge even after the
  // customer has moved to another page.
  useEffect(() => {
    if (hidden || open) return;
    let alive = true;
    const tick = () => api.chat.myUnread().then((n) => { if (alive) setUnread(n); }).catch(() => {});
    tick();
    const id = setInterval(tick, 12000);
    return () => { alive = false; clearInterval(id); };
  }, [hidden, open]);

  // While open, refresh the thread and keep the customer's read marker current.
  useEffect(() => {
    if (hidden || !open) return;
    let alive = true;
    const tick = async () => {
      if (sendingRef.current) return; // don't clobber the in-flight optimistic send
      try {
        const c = await api.chat.mine();
        if (!alive || sendingRef.current) return;
        setConv(c ?? null);
        setUnread(0);
        if (c) api.chat.readMine().catch(() => {});
      } catch { /* keep last state on a transient error */ }
    };
    tick();
    const id = setInterval(tick, 2500);
    return () => { alive = false; clearInterval(id); };
  }, [hidden, open]);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight });
  }, [conv?.messages.length, open]);

  if (hidden) return null;

  async function send(e: React.FormEvent) {
    e.preventDefault();
    const body = text.trim();
    if (!body || sending) return;

    // Optimistic UI: show the message and clear the box immediately, then reconcile with the server's
    // canonical thread. This is what makes sending feel instant instead of waiting for the round-trip.
    const optimistic: ChatMessage = { id: -Date.now(), fromAdmin: false, authorName: "", body, createdAtUtc: new Date().toISOString() };
    setConv((prev) =>
      prev
        ? { ...prev, messages: [...prev.messages, optimistic] }
        : { id: 0, userId: user?.id ?? 0, userName: "", status: "Open", createdAtUtc: optimistic.createdAtUtc, lastMessageAtUtc: optimistic.createdAtUtc, userReadUpTo: 0, messages: [optimistic] },
    );
    setText("");
    setSending(true);
    sendingRef.current = true;
    try {
      const c = await api.chat.send(body);
      setConv(c);
    } catch {
      // restore the failed message so the customer can retry, and drop the optimistic bubble.
      setText(body);
      setConv((prev) => (prev ? { ...prev, messages: prev.messages.filter((m) => m.id !== optimistic.id) } : prev));
    } finally {
      setSending(false);
      sendingRef.current = false;
    }
  }

  return (
    <div className="fixed bottom-4 right-4 z-[60] flex flex-col items-end gap-3" dir="rtl">
      {open && (
        <div className="flex h-[28rem] max-h-[72vh] w-[22rem] max-w-[calc(100vw-2rem)] flex-col overflow-hidden rounded-2xl border border-white/10 bg-[#0d0d15] shadow-[0_24px_60px_-20px_rgba(0,0,0,0.8)]">
          <div className="flex items-center justify-between gap-2 border-b border-white/8 bg-gradient-to-l from-[#e60053]/20 to-transparent px-4 py-3">
            <div>
              <p className="text-sm font-bold text-white">گفتگوی زنده با پشتیبانی</p>
              <p className="text-[11px] text-white/50">معمولاً در چند دقیقه پاسخ می‌دهیم</p>
            </div>
            <button onClick={() => setOpen(false)} aria-label="بستن" className="grid h-8 w-8 place-items-center rounded-full text-white/60 transition hover:bg-white/10 hover:text-white">
              <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M18 6 6 18M6 6l12 12" /></svg>
            </button>
          </div>

          <div ref={scrollRef} className="flex-1 space-y-3 overflow-y-auto px-3.5 py-4">
            {!conv || conv.messages.length === 0 ? (
              <div className="grid h-full place-items-center px-4 text-center">
                <p className="text-sm leading-7 text-white/45">سلام! اگر سؤال یا مورد مهمی دارید بنویسید؛ تیم پشتیبانی همین‌جا پاسخ می‌دهد.</p>
              </div>
            ) : (
              conv.messages.map((m) => (
                <div key={m.id} className={`flex ${m.fromAdmin ? "justify-end" : "justify-start"}`}>
                  <div className={`max-w-[80%] rounded-2xl px-3.5 py-2 text-sm leading-6 ${m.fromAdmin ? "bg-[#1c2740] text-white/90" : "bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white"}`}>
                    {m.fromAdmin && <p className="mb-0.5 text-[10px] font-bold text-[#9db4ff]">{m.authorName}</p>}
                    <p className="whitespace-pre-wrap break-words">{m.body}</p>
                    <p className="mt-1 text-[10px] opacity-60">{timeOf(m.createdAtUtc)}</p>
                  </div>
                </div>
              ))
            )}
          </div>

          <form onSubmit={send} className="flex items-center gap-2 border-t border-white/8 p-2.5">
            <input
              value={text}
              onChange={(e) => setText(e.target.value)}
              placeholder="پیام خود را بنویسید…"
              className="h-11 flex-1 rounded-xl border border-white/10 bg-[#15151f] px-3.5 text-sm text-white outline-none transition focus:border-[#e60053]/50"
            />
            <button
              type="submit"
              disabled={sending || !text.trim()}
              aria-label="ارسال"
              className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white transition hover:brightness-110 disabled:opacity-40"
            >
              <svg viewBox="0 0 24 24" className="h-5 w-5 -scale-x-100" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M22 2 11 13M22 2l-7 20-4-9-9-4 20-7Z" /></svg>
            </button>
          </form>
        </div>
      )}

      <button
        onClick={() => setOpen((o) => !o)}
        aria-label="گفتگوی زنده"
        className="relative grid h-14 w-14 place-items-center rounded-full bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white shadow-[0_14px_36px_-10px_rgba(230,0,83,0.8)] transition hover:brightness-110 active:scale-95"
      >
        {open ? (
          <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M18 6 6 18M6 6l12 12" /></svg>
        ) : (
          <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5Z" /></svg>
        )}
        {!open && unread > 0 && (
          <span className="absolute -right-1 -top-1 grid h-6 min-w-6 place-items-center rounded-full border-2 border-[#0b0b12] bg-white px-1 text-[11px] font-bold text-[#e60053]">
            {unread > 9 ? "۹+" : unread.toLocaleString("fa-IR")}
          </span>
        )}
      </button>
    </div>
  );
}
