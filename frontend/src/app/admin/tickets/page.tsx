"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { Ticket, TicketStatus } from "@/lib/types";
import { formatNumber } from "@/lib/format";
import { ticketStatusLabel } from "@/lib/labels";
import { Card, PageHeader, Spinner, StatusBadge, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

type Filter = "all" | TicketStatus;

export default function AdminTicketsPage() {
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [filter, setFilter] = useState<Filter>("Open");
  const [dept, setDept] = useState<string>("all");
  const [selected, setSelected] = useState<Ticket | null>(null);
  const [reply, setReply] = useState("");
  const [busy, setBusy] = useState(false);
  const [composing, setComposing] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        setTickets(await api.tickets.list());
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const counts = useMemo(
    () => ({
      Open: tickets.filter((t) => t.status === "Open").length,
      Answered: tickets.filter((t) => t.status === "Answered").length,
      Closed: tickets.filter((t) => t.status === "Closed").length,
    }),
    [tickets],
  );
  const shown = tickets
    .filter((t) => filter === "all" || t.status === filter)
    .filter((t) => dept === "all" || t.department === dept);

  // Departments present across the tickets, so the filter always reflects what actually exists.
  const departments = useMemo(
    () => Array.from(new Set(tickets.map((t) => t.department).filter(Boolean))),
    [tickets],
  );

  function apply(t: Ticket) {
    setTickets((p) => p.map((x) => (x.id === t.id ? t : x)));
    setSelected(t);
  }

  async function sendReply() {
    if (!selected || !reply.trim()) return;
    setBusy(true);
    try {
      apply(await api.tickets.reply(selected.id, reply.trim(), true));
      setReply("");
    } finally {
      setBusy(false);
    }
  }
  async function close(t: Ticket) {
    setBusy(true);
    try {
      await api.tickets.close(t.id);
      const updated = { ...t, status: "Closed" as TicketStatus };
      setTickets((p) => p.map((x) => (x.id === t.id ? updated : x)));
      setSelected(updated);
    } finally {
      setBusy(false);
    }
  }

  const filters: { key: Filter; label: string; count?: number }[] = [
    { key: "Open", label: "باز", count: counts.Open },
    { key: "Answered", label: "پاسخ داده شده", count: counts.Answered },
    { key: "Closed", label: "بسته شده", count: counts.Closed },
    { key: "all", label: "همه" },
  ];

  function onCreated(t: Ticket) {
    setTickets((p) => [t, ...p]);
    setComposing(false);
    setFilter("all");
    setSelected(t);
  }

  return (
    <div>
      <PageHeader
        title="تیکت‌های پشتیبانی"
        desc="پاسخ به کاربران و مدیریت تیکت‌ها"
        action={
          !selected && (
            <button
              onClick={() => setComposing((v) => !v)}
              className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
            >
              <AdminIcon name="plus" className="h-4 w-4" />
              تیکت جدید برای کاربر
            </button>
          )
        }
      />

      {composing && !selected && <NewTicketForm onCreated={onCreated} onCancel={() => setComposing(false)} />}

      {selected ? (
        <Card className="p-5">
          <button onClick={() => setSelected(null)} className="mb-4 text-sm text-[#e60053] hover:underline">→ بازگشت به لیست</button>
          <div className="flex items-center justify-between border-b border-white/8 pb-3">
            <div>
              <p className="font-bold text-white">{selected.subject}</p>
              <p className="text-xs text-white/40">{selected.code} · {selected.userName} · {selected.department}</p>
            </div>
            <StatusBadge status={ticketStatusLabel[selected.status]} />
          </div>

          {selected.attachment && (
            <a href={selected.attachment} target="_blank" rel="noreferrer" className="mt-3 inline-flex items-center gap-2 rounded-lg border border-white/10 px-3 py-2 text-xs font-bold text-[#6f93ff] transition hover:bg-white/5">
              <AdminIcon name="image" className="h-4 w-4" />
              مشاهده فایل پیوست کاربر
            </a>
          )}

          <div className="mt-4 space-y-3">
            {selected.messages.map((m, i) => (
              <div key={i} className={`rounded-xl p-3 ${m.isAdmin ? "border-r-2 border-[#e60053]/40 bg-white/[0.03]" : "bg-[#0d0d15]"}`}>
                <p className={`text-xs font-bold ${m.isAdmin ? "text-[#ff5a8a]" : "text-white/70"}`}>{m.author} · {m.date}</p>
                <p className="mt-1.5 text-sm leading-7 text-white/80">{m.body}</p>
              </div>
            ))}
          </div>

          {selected.status !== "Closed" && (
            <>
              <div className="mt-4 flex items-start gap-2">
                <textarea value={reply} onChange={(e) => setReply(e.target.value)} rows={2} placeholder="پاسخ پشتیبانی..." className="flex-1 rounded-xl border border-white/10 bg-[#0d0d15] px-3 py-2 text-sm text-white outline-none focus:border-[#3a64f2]" />
                <button onClick={sendReply} disabled={busy} className="grid h-10 w-20 place-items-center rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-sm font-bold text-white">{busy ? <Spinner /> : "ارسال"}</button>
              </div>
              <button onClick={() => close(selected)} disabled={busy} className="mt-3 rounded-lg border border-white/10 px-4 py-2 text-xs font-bold text-white/70 transition hover:bg-white/5">بستن تیکت</button>
            </>
          )}
        </Card>
      ) : (
        <>
          <div className="mb-5 flex flex-wrap gap-2">
            {filters.map((f) => (
              <button
                key={f.key}
                onClick={() => setFilter(f.key)}
                className={`flex items-center gap-2 rounded-full border px-4 py-1.5 text-sm font-medium transition ${
                  filter === f.key ? "border-transparent bg-white/10 text-white" : "border-white/10 text-white/60 hover:text-white"
                }`}
              >
                {f.label}
                {f.count ? <span className="rounded-full bg-[#e60053]/20 px-1.5 text-[11px] font-bold text-[#ff5a8a]">{formatNumber(f.count)}</span> : null}
              </button>
            ))}
          </div>

          {departments.length > 0 && (
            <div className="mb-5 flex flex-wrap items-center gap-2">
              <span className="text-xs text-white/40">دپارتمان:</span>
              {[{ key: "all", label: "همه" }, ...departments.map((d) => ({ key: d, label: d }))].map((d) => (
                <button
                  key={d.key}
                  onClick={() => setDept(d.key)}
                  className={`rounded-full border px-3.5 py-1 text-xs font-medium transition ${
                    dept === d.key ? "border-transparent bg-white/10 text-white" : "border-white/10 text-white/60 hover:text-white"
                  }`}
                >
                  {d.label}
                </button>
              ))}
            </div>
          )}

          {loading ? (
            <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
          ) : error ? (
            <Card className="p-8 text-center text-rose-400">{error}</Card>
          ) : shown.length === 0 ? (
            <Card className="p-12 text-center text-white/40">تیکتی در این وضعیت نیست</Card>
          ) : (
            <div className="space-y-3">
              {shown.map((t) => (
                <button key={t.id} onClick={() => setSelected(t)} className="flex w-full items-center justify-between gap-3 rounded-2xl border border-white/8 bg-[#15151f]/80 p-4 text-right transition hover:border-white/20">
                  <div>
                    <p className="font-bold text-white">{t.subject}</p>
                    <p className="text-xs text-white/40">{t.code} · {t.userName} · {t.department} · {t.date}</p>
                  </div>
                  <StatusBadge status={ticketStatusLabel[t.status]} />
                </button>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function NewTicketForm({ onCreated, onCancel }: { onCreated: (t: Ticket) => void; onCancel: () => void }) {
  const [username, setUsername] = useState("");
  const [subject, setSubject] = useState("");
  const [department, setDepartment] = useState("فنی");
  const [body, setBody] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function submit() {
    setError("");
    if (!username.trim() || !subject.trim() || !body.trim()) {
      setError("نام کاربری، موضوع و متن پیام الزامی است.");
      return;
    }
    setBusy(true);
    try {
      // resolve the username to a user id (exact, case-insensitive match) before opening the ticket.
      const matches = await api.users.list({ search: username.trim() });
      const user = matches.find((u) => u.username.toLowerCase() === username.trim().toLowerCase());
      if (!user) {
        setError("کاربری با این نام کاربری پیدا نشد.");
        return;
      }
      const ticket = await api.tickets.createForUser({ userId: user.id, subject: subject.trim(), department, body: body.trim() });
      onCreated(ticket);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در ایجاد تیکت");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card className="mb-5 space-y-4 p-5">
      <div className="flex items-center justify-between">
        <p className="font-bold text-white">باز کردن تیکت برای یک کاربر</p>
        <button onClick={onCancel} className="text-sm text-white/50 hover:text-white">انصراف</button>
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <label>
          <span className="mb-2 block text-sm text-white/70">نام کاربری گیرنده</span>
          <input value={username} onChange={(e) => setUsername(e.target.value)} dir="ltr" placeholder="username" className={`${inputCls} text-left`} />
        </label>
        <label>
          <span className="mb-2 block text-sm text-white/70">دپارتمان</span>
          <select value={department} onChange={(e) => setDepartment(e.target.value)} className={inputCls}>
            {["فنی", "مالی"].map((d) => <option key={d} value={d} className="bg-[#15151f]">{d}</option>)}
          </select>
        </label>
      </div>
      <label className="block">
        <span className="mb-2 block text-sm text-white/70">موضوع</span>
        <input value={subject} onChange={(e) => setSubject(e.target.value)} className={inputCls} />
      </label>
      <label className="block">
        <span className="mb-2 block text-sm text-white/70">متن پیام</span>
        <textarea value={body} onChange={(e) => setBody(e.target.value)} rows={3} className="w-full rounded-xl border border-white/10 bg-[#0d0d15] px-3 py-2 text-sm text-white outline-none focus:border-[#3a64f2]" />
      </label>
      {error && <p className="text-sm text-rose-400">{error}</p>}
      <button
        onClick={submit}
        disabled={busy}
        className="flex h-11 w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60 sm:w-auto sm:px-8"
      >
        {busy ? <Spinner /> : "ارسال تیکت به کاربر"}
      </button>
    </Card>
  );
}
