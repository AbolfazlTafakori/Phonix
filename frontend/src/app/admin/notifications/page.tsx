"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { AdminNotification, User } from "@/lib/types";
import { Card, PageHeader, Spinner, Field, inputCls } from "@/components/admin/ui";

function fmtDate(iso: string): string {
  try {
    return new Date(iso).toLocaleString("fa-IR", { dateStyle: "short", timeStyle: "short" });
  } catch {
    return "";
  }
}

export default function AdminNotificationsPage() {
  const [items, setItems] = useState<AdminNotification[]>([]);
  const [users, setUsers] = useState<User[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  // compose
  const [target, setTarget] = useState<"all" | number>("all");
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [link, setLink] = useState("");
  const [sending, setSending] = useState(false);
  const [note, setNote] = useState<{ ok: boolean; text: string } | null>(null);
  const [busy, setBusy] = useState<number | null>(null);

  async function load() {
    try {
      const [list, us] = await Promise.all([api.notifications.all(), api.users.list().catch(() => [] as User[])]);
      setItems(list);
      setUsers(us);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری");
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => {
    load();
  }, []);

  const userName = useMemo(() => Object.fromEntries(users.map((u) => [u.id, u.name || u.username])), [users]);

  async function send() {
    if (!title.trim()) {
      setNote({ ok: false, text: "عنوان پیام را وارد کنید." });
      return;
    }
    setSending(true);
    setNote(null);
    try {
      await api.notifications.send({
        userId: target === "all" ? null : target,
        title: title.trim(),
        body: body.trim(),
        link: link.trim() || null,
      });
      setTitle("");
      setBody("");
      setLink("");
      await load();
      setNote({ ok: true, text: target === "all" ? "پیام عمومی برای همه کاربران ارسال شد." : "پیام برای کاربر ارسال شد." });
    } catch (e) {
      setNote({ ok: false, text: e instanceof Error ? e.message : "ارسال ناموفق بود." });
    } finally {
      setSending(false);
    }
  }

  async function remove(id: number) {
    if (!confirm("این پیام حذف شود؟")) return;
    setBusy(id);
    try {
      await api.notifications.remove(id);
      setItems((p) => p.filter((x) => x.id !== id));
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <PageHeader title="پیام‌ها و اعلان‌ها" desc="ارسال پیام خصوصی به یک کاربر یا اعلان عمومی به همه‌ی کاربران" />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : (
        <div className="grid gap-6 lg:grid-cols-[360px_1fr]">
          <Card className="h-fit p-6">
            <h3 className="mb-4 text-lg font-bold text-white">ارسال پیام جدید</h3>
            <div className="grid gap-4">
              <Field label="گیرنده">
                <select
                  value={String(target)}
                  onChange={(e) => setTarget(e.target.value === "all" ? "all" : Number(e.target.value))}
                  className={`${inputCls} h-11`}
                >
                  <option value="all" className="bg-[#15151f]">همه کاربران (عمومی)</option>
                  {users.map((u) => (
                    <option key={u.id} value={u.id} className="bg-[#15151f]">{u.name || u.username} ({u.username})</option>
                  ))}
                </select>
              </Field>
              <Field label="عنوان">
                <input value={title} onChange={(e) => setTitle(e.target.value)} className={`${inputCls} h-11`} />
              </Field>
              <Field label="متن پیام">
                <textarea rows={4} value={body} onChange={(e) => setBody(e.target.value)} className={`${inputCls} h-auto py-3`} />
              </Field>
              <Field label="لینک (اختیاری)">
                <input value={link} onChange={(e) => setLink(e.target.value)} dir="ltr" placeholder="/account/orders" className={`${inputCls} h-11 text-left`} />
              </Field>
              <button
                onClick={send}
                disabled={sending || !title.trim()}
                className="h-11 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
              >
                {sending ? "در حال ارسال..." : "ارسال پیام"}
              </button>
              {note && <p className={`text-sm ${note.ok ? "text-emerald-400" : "text-rose-400"}`}>{note.text}</p>}
            </div>
          </Card>

          <Card className="h-fit p-6">
            <h3 className="mb-4 text-lg font-bold text-white">پیام‌های ارسال‌شده</h3>
            {items.length === 0 ? (
              <p className="py-8 text-center text-sm text-white/45">هنوز پیامی ارسال نشده است.</p>
            ) : (
              <ul className="space-y-2">
                {items.map((n) => (
                  <li key={n.id} className="flex items-start justify-between gap-3 rounded-xl border border-white/8 bg-white/[0.03] px-4 py-3">
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="text-sm font-bold text-white">{n.title}</span>
                        <span className={`rounded-md px-2 py-0.5 text-[11px] font-bold ${n.userId === null ? "bg-[#3a64f2]/20 text-[#8fa9ff]" : "bg-white/10 text-white/55"}`}>
                          {n.userId === null ? "عمومی" : userName[n.userId] ?? `کاربر ${n.userId}`}
                        </span>
                      </div>
                      {n.body && <p className="mt-1 text-xs leading-6 text-white/55">{n.body}</p>}
                      <p className="mt-1 text-[11px] text-white/35" dir="ltr">{fmtDate(n.createdAtUtc)}</p>
                    </div>
                    <button
                      onClick={() => remove(n.id)}
                      disabled={busy === n.id}
                      className="shrink-0 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/55 transition hover:border-rose-500/50 hover:text-rose-400 disabled:opacity-60"
                    >
                      حذف
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>
      )}
    </div>
  );
}
