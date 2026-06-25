"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { ticketStatusLabel } from "@/lib/labels";
import { PageTitle, Panel } from "@/components/account/Panel";
import { StatusBadge } from "@/components/admin/ui";
import ImageField from "@/components/admin/ImageField";
import type { Ticket, TicketPriority } from "@/lib/types";

const departments = ["فنی", "مالی"];
const priorities: { value: TicketPriority; label: string }[] = [
  { value: "Low", label: "کم" },
  { value: "Medium", label: "متوسط" },
  { value: "High", label: "زیاد" },
];
const priorityLabel: Record<TicketPriority, string> = { Low: "کم", Medium: "متوسط", High: "زیاد" };
const inputCls = "h-11 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none focus:border-[#3e3af2]";

export default function TicketsPage() {
  const { user } = useAuth();
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [loading, setLoading] = useState(true);
  const [openForm, setOpenForm] = useState(false);
  const [selected, setSelected] = useState<Ticket | null>(null);

  const [subject, setSubject] = useState("");
  const [department, setDepartment] = useState(departments[0]);
  const [priority, setPriority] = useState<TicketPriority>("Medium");
  const [attachment, setAttachment] = useState("");
  const [body, setBody] = useState("");
  const [reply, setReply] = useState("");
  const [replyAttachment, setReplyAttachment] = useState("");
  const [busy, setBusy] = useState(false);

  async function load() {
    if (!user) return;
    try {
      setTickets(await api.tickets.forUser(user.id));
    } catch {
      // keep current values if tickets can't be refreshed
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => {
    load();
  }, [user]);

  async function create() {
    if (!user || !subject.trim() || !body.trim()) return;
    setBusy(true);
    try {
      const t = await api.tickets.create({ subject: subject.trim(), department, body: body.trim(), priority, attachment: attachment || undefined });
      setTickets((p) => [t, ...p]);
      setSubject("");
      setBody("");
      setAttachment("");
      setPriority("Medium");
      setOpenForm(false);
      setSelected(t);
    } finally {
      setBusy(false);
    }
  }

  async function sendReply() {
    if (!selected || (!reply.trim() && !replyAttachment)) return;
    setBusy(true);
    try {
      const t = await api.tickets.reply(selected.id, reply.trim() || "(فایل پیوست)", false, replyAttachment || undefined);
      setSelected(t);
      setTickets((p) => p.map((x) => (x.id === t.id ? t : x)));
      setReply("");
      setReplyAttachment("");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <div className="mb-6 flex items-center justify-between gap-4">
        <PageTitle title="تیکت پشتیبانی" />
        <button onClick={() => setOpenForm((v) => !v)} className="h-11 shrink-0 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-6 text-sm font-bold text-white transition hover:brightness-110">
          {openForm ? "بستن" : "ثبت تیکت جدید"}
        </button>
      </div>

      {openForm && (
        <Panel className="mb-6">
          <div className="grid gap-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <input value={subject} onChange={(e) => setSubject(e.target.value)} placeholder="موضوع" className={inputCls} />
              <select value={department} onChange={(e) => setDepartment(e.target.value)} className={inputCls}>
                {departments.map((d) => <option key={d} className="bg-[#15151f]">{d}</option>)}
              </select>
            </div>
            <div>
              <label className="mb-2 block text-sm text-white/70">سطح اهمیت</label>
              <select value={priority} onChange={(e) => setPriority(e.target.value as TicketPriority)} className={`${inputCls} sm:w-1/2`}>
                {priorities.map((p) => <option key={p.value} value={p.value} className="bg-[#15151f]">{p.label}</option>)}
              </select>
            </div>
            <div>
              <label className="mb-2 block text-sm text-white/70">متن پیام</label>
              <textarea value={body} onChange={(e) => setBody(e.target.value)} rows={4} placeholder="شرح مشکل یا سوال خود را بنویسید..." className="w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 py-3 text-sm text-white outline-none focus:border-[#3e3af2]" />
            </div>
            <div>
              <label className="mb-2 block text-sm text-white/70">فایل پیوست (اختیاری)</label>
              <div className="w-[120px]">
                <ImageField value={attachment} onChange={setAttachment} aspect="square" />
              </div>
            </div>
            <button onClick={create} disabled={busy} className="h-11 w-fit rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60">
              {busy ? "در حال ارسال..." : "ارسال تیکت"}
            </button>
          </div>
        </Panel>
      )}

      {loading ? (
        <Panel><div className="grid h-24 place-items-center"><span className="inline-block h-7 w-7 animate-spin rounded-full border-2 border-white/20 border-t-[#e60053]" /></div></Panel>
      ) : selected ? (
        <Panel>
          <button onClick={() => setSelected(null)} className="mb-4 text-sm text-[#e60053] hover:underline">→ بازگشت به لیست</button>
          <div className="flex items-center justify-between border-b border-white/8 pb-3">
            <div>
              <p className="font-bold text-white">{selected.subject}</p>
              <p className="text-xs text-white/40">
                {selected.code} · {selected.department} · اهمیت: {priorityLabel[selected.priority] ?? "متوسط"}
              </p>
            </div>
            <StatusBadge status={ticketStatusLabel[selected.status]} />
          </div>

          {selected.attachment && (
            <a href={selected.attachment} target="_blank" rel="noreferrer" className="mt-3 inline-flex items-center gap-2 rounded-lg border border-white/10 px-3 py-2 text-xs font-bold text-[#6f93ff] transition hover:bg-white/5">
              مشاهده فایل پیوست
            </a>
          )}

          <div className="mt-4 space-y-3">
            {selected.messages.map((m, i) => (
              <div key={i} className={`rounded-xl p-3 ${m.isAdmin ? "border-r-2 border-[#e60053]/40 bg-white/[0.03]" : "bg-[#0d0d15]"}`}>
                <p className={`text-xs font-bold ${m.isAdmin ? "text-[#ff5a8a]" : "text-white/70"}`}>{m.author} · {m.date}</p>
                <p className="mt-1.5 text-sm leading-7 text-white/80">{m.body}</p>
                {m.attachment && (
                  <a href={m.attachment} target="_blank" rel="noreferrer" className="mt-2 inline-flex items-center gap-2 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-[#6f93ff] transition hover:bg-white/5">
                    مشاهده فایل پیوست
                  </a>
                )}
              </div>
            ))}
          </div>

          {selected.status !== "Closed" && (
            <div className="mt-4">
              <div className="flex items-start gap-2">
                <textarea value={reply} onChange={(e) => setReply(e.target.value)} rows={2} placeholder="پاسخ شما..." className="flex-1 rounded-xl border border-white/10 bg-[#0d0d15] px-3 py-2 text-sm text-white outline-none focus:border-[#3e3af2]" />
                <button onClick={sendReply} disabled={busy} className="grid h-10 w-20 place-items-center rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-sm font-bold text-white disabled:opacity-60">ارسال</button>
              </div>
              <div className="mt-2">
                <span className="mb-1 block text-xs text-white/50">فایل پیوست (اختیاری)</span>
                <div className="w-[110px]">
                  <ImageField value={replyAttachment} onChange={setReplyAttachment} aspect="square" />
                </div>
              </div>
            </div>
          )}
        </Panel>
      ) : tickets.length === 0 ? (
        <Panel><p className="py-8 text-center text-white/60">تیکتی ثبت نکرده‌اید.</p></Panel>
      ) : (
        <div className="space-y-3">
          {tickets.map((t) => (
            <button key={t.id} onClick={() => setSelected(t)} className="flex w-full items-center justify-between gap-3 rounded-2xl border border-white/8 bg-[#15151f]/80 p-4 text-right transition hover:border-white/20">
              <div>
                <p className="font-bold text-white">{t.subject}</p>
                <p className="text-xs text-white/40">{t.code} · {t.department} · {t.date}</p>
              </div>
              <StatusBadge status={ticketStatusLabel[t.status]} />
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
