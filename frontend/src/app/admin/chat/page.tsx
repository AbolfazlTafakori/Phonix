"use client";

import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import type { ChatConversation, ConversationSummary } from "@/lib/types";
import { PageHeader, Spinner } from "@/components/admin/ui";

function timeOf(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return d.toLocaleString("fa-IR", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });
}

export default function AdminChatPage() {
  const [list, setList] = useState<ConversationSummary[]>([]);
  const [activeId, setActiveId] = useState<number | null>(null);
  const [active, setActive] = useState<ChatConversation | null>(null);
  const [text, setText] = useState("");
  const [loading, setLoading] = useState(true);
  const [sending, setSending] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let alive = true;
    const tick = () => api.chat.list().then((l) => { if (alive) { setList(l); setLoading(false); } }).catch(() => setLoading(false));
    tick();
    const id = setInterval(tick, 5000);
    return () => { alive = false; clearInterval(id); };
  }, []);

  useEffect(() => {
    if (activeId === null) { setActive(null); return; }
    let alive = true;
    const tick = async () => {
      try {
        const c = await api.chat.get(activeId);
        if (!alive) return;
        setActive(c);
        api.chat.read(activeId).catch(() => {});
      } catch { /* keep last state */ }
    };
    tick();
    const id = setInterval(tick, 4000);
    return () => { alive = false; clearInterval(id); };
  }, [activeId]);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight });
  }, [active?.messages.length, activeId]);

  async function reply(e: React.FormEvent) {
    e.preventDefault();
    const body = text.trim();
    if (!body || !activeId || sending) return;
    setSending(true);
    try {
      const c = await api.chat.reply(activeId, body);
      setActive(c);
      setText("");
      api.chat.list().then(setList).catch(() => {});
    } catch { /* leave text for retry */ } finally {
      setSending(false);
    }
  }

  async function close() {
    if (!activeId) return;
    await api.chat.close(activeId).catch(() => {});
    api.chat.list().then(setList).catch(() => {});
    api.chat.get(activeId).then(setActive).catch(() => {});
  }

  return (
    <div>
      <PageHeader title="گفتگوی زنده" desc="پیام‌های زنده‌ی کاربران را اینجا ببینید و پاسخ دهید." />

      <div className="grid gap-4 lg:grid-cols-[340px_1fr]">
        <div className={`${activeId !== null ? "hidden lg:block" : ""} rounded-2xl border border-white/8 bg-[#15151f]/80`}>
          <div className="border-b border-white/8 px-4 py-3 text-sm font-bold text-white">گفتگوها</div>
          {loading ? (
            <div className="grid place-items-center py-16"><Spinner className="h-7 w-7" /></div>
          ) : list.length === 0 ? (
            <p className="px-4 py-12 text-center text-sm text-white/40">هنوز گفتگویی شروع نشده است</p>
          ) : (
            <div className="max-h-[68vh] overflow-y-auto">
              {list.map((c) => (
                <button
                  key={c.id}
                  onClick={() => setActiveId(c.id)}
                  className={`flex w-full items-start gap-3 border-b border-white/5 px-4 py-3 text-right transition ${activeId === c.id ? "bg-white/[0.06]" : "hover:bg-white/[0.03]"}`}
                >
                  <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-sm font-bold text-white">
                    {c.userName.charAt(0)}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center justify-between gap-2">
                      <p className="truncate text-sm font-bold text-white">{c.userName}</p>
                      {c.unread > 0 && <span className="grid h-5 min-w-5 place-items-center rounded-full bg-[#e60053] px-1 text-[10px] font-bold text-white">{c.unread.toLocaleString("fa-IR")}</span>}
                    </div>
                    <p className="truncate text-xs text-white/45">{c.lastPreview || "—"}</p>
                  </div>
                </button>
              ))}
            </div>
          )}
        </div>

        <div className={`${activeId === null ? "hidden lg:flex" : "flex"} h-[72vh] flex-col overflow-hidden rounded-2xl border border-white/8 bg-[#0d0d15]`}>
          {!active ? (
            <div className="grid flex-1 place-items-center text-sm text-white/40">یک گفتگو را انتخاب کنید</div>
          ) : (
            <>
              <div className="flex items-center justify-between gap-2 border-b border-white/8 px-4 py-3">
                <div className="flex items-center gap-3">
                  <button onClick={() => setActiveId(null)} aria-label="بازگشت" className="grid h-8 w-8 place-items-center rounded-full text-white/60 transition hover:bg-white/10 lg:hidden">
                    <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m9 18 6-6-6-6" /></svg>
                  </button>
                  <div>
                    <p className="text-sm font-bold text-white">{active.userName}</p>
                    <p className="text-[11px] text-white/45">{active.status === "Open" ? "باز" : "بسته‌شده"}</p>
                  </div>
                </div>
                <button onClick={close} className="rounded-lg border border-white/10 px-3 py-1.5 text-xs font-medium text-white/70 transition hover:border-rose-500/50 hover:text-rose-400">
                  بستن گفتگو
                </button>
              </div>

              <div ref={scrollRef} className="flex-1 space-y-3 overflow-y-auto px-4 py-4">
                {active.messages.map((m) => (
                  <div key={m.id} className={`flex ${m.fromAdmin ? "justify-start" : "justify-end"}`}>
                    <div className={`max-w-[75%] rounded-2xl px-3.5 py-2 text-sm leading-6 ${m.fromAdmin ? "bg-[#1c2740] text-white/90" : "bg-white/[0.06] text-white/90"}`}>
                      <p className="whitespace-pre-wrap break-words">{m.body}</p>
                      <p className="mt-1 text-[10px] opacity-50">{timeOf(m.createdAtUtc)}</p>
                    </div>
                  </div>
                ))}
              </div>

              <form onSubmit={reply} className="flex items-center gap-2 border-t border-white/8 p-3">
                <input
                  value={text}
                  onChange={(e) => setText(e.target.value)}
                  placeholder="پاسخ خود را بنویسید…"
                  className="h-11 flex-1 rounded-xl border border-white/10 bg-[#15151f] px-3.5 text-sm text-white outline-none transition focus:border-[#3a64f2]/50"
                />
                <button
                  type="submit"
                  disabled={sending || !text.trim()}
                  className="grid h-11 shrink-0 place-items-center rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-40"
                >
                  ارسال
                </button>
              </form>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
