"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { Comment, CommentStatus } from "@/lib/types";
import { formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, StatusBadge } from "@/components/admin/ui";
import Stars from "@/components/Stars";
import AdminIcon from "@/components/admin/AdminIcon";

const statusLabel: Record<CommentStatus, string> = { Pending: "در انتظار", Approved: "تایید شده", Rejected: "رد شده" };
type Filter = "Pending" | "Approved" | "Rejected" | "all";

export default function AdminCommentsPage() {
  const [comments, setComments] = useState<Comment[]>([]);
  const [productNames, setProductNames] = useState<Record<number, string>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [filter, setFilter] = useState<Filter>("Pending");
  const [busy, setBusy] = useState<number | null>(null);
  const [replyFor, setReplyFor] = useState<number | null>(null);
  const [replyText, setReplyText] = useState("");

  useEffect(() => {
    (async () => {
      try {
        const [cs, ps] = await Promise.all([api.comments.list(), api.products.list()]);
        setComments(cs);
        setProductNames(Object.fromEntries(ps.map((p) => [p.id, p.name])));
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const topLevel = useMemo(() => comments.filter((c) => c.parentId == null), [comments]);
  const counts = useMemo(
    () => ({
      Pending: topLevel.filter((c) => c.status === "Pending").length,
      Approved: topLevel.filter((c) => c.status === "Approved").length,
      Rejected: topLevel.filter((c) => c.status === "Rejected").length,
    }),
    [topLevel],
  );
  const shown = filter === "all" ? topLevel : topLevel.filter((c) => c.status === filter);

  async function setStatus(c: Comment, status: CommentStatus) {
    setBusy(c.id);
    try {
      if (status === "Approved") await api.comments.approve(c.id);
      else await api.comments.reject(c.id);
      setComments((prev) => prev.map((x) => (x.id === c.id ? { ...x, status } : x)));
    } finally {
      setBusy(null);
    }
  }
  async function remove(c: Comment) {
    if (!confirm("این نظر حذف شود؟")) return;
    setBusy(c.id);
    try {
      await api.comments.remove(c.id);
      setComments((prev) => prev.filter((x) => x.id !== c.id && x.parentId !== c.id));
    } finally {
      setBusy(null);
    }
  }
  async function sendReply(c: Comment) {
    if (!replyText.trim()) return;
    setBusy(c.id);
    try {
      const reply = await api.comments.reply(c.id, replyText.trim());
      setComments((prev) => [...prev, reply]);
      setReplyFor(null);
      setReplyText("");
    } finally {
      setBusy(null);
    }
  }

  const filters: { key: Filter; label: string; count?: number }[] = [
    { key: "Pending", label: "در انتظار", count: counts.Pending },
    { key: "Approved", label: "تایید شده", count: counts.Approved },
    { key: "Rejected", label: "رد شده", count: counts.Rejected },
    { key: "all", label: "همه" },
  ];

  return (
    <div>
      <PageHeader title="نظرات و امتیازها" desc="نظرات کاربران پس از تأیید شما زیر محصول نمایش داده می‌شوند" />

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

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : shown.length === 0 ? (
        <Card className="p-12 text-center text-white/40">نظری در این وضعیت وجود ندارد</Card>
      ) : (
        <div className="space-y-4">
          {shown.map((c) => {
            const replies = comments.filter((r) => r.parentId === c.id);
            return (
              <Card key={c.id} className="p-5">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div className="flex items-center gap-3">
                    <span className="grid h-10 w-10 place-items-center rounded-full bg-gradient-to-br from-[#6d28d9] to-[#e60053] text-sm font-bold text-white">
                      {c.userName.charAt(0)}
                    </span>
                    <div>
                      <p className="text-sm font-bold text-white">{c.userName}</p>
                      <p className="text-xs text-white/40">{productNames[c.productId] ?? `محصول #${c.productId}`} · {c.date}</p>
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    {c.rating > 0 && <Stars value={c.rating} />}
                    <StatusBadge status={statusLabel[c.status]} />
                  </div>
                </div>

                <p className="mt-3 text-sm leading-7 text-white/80">{c.body}</p>

                {replies.map((r) => (
                  <div key={r.id} className="mt-3 rounded-xl border-r-2 border-[#e60053]/40 bg-white/[0.03] p-3">
                    <p className="text-xs font-bold text-[#ff5a8a]">{r.userName}</p>
                    <p className="mt-1 text-sm leading-7 text-white/70">{r.body}</p>
                  </div>
                ))}

                {replyFor === c.id && (
                  <div className="mt-3 flex items-start gap-2">
                    <textarea
                      value={replyText}
                      onChange={(e) => setReplyText(e.target.value)}
                      rows={2}
                      placeholder="پاسخ شما..."
                      className="flex-1 rounded-xl border border-white/10 bg-[#0d0d15] px-3 py-2 text-sm text-white outline-none focus:border-[#3a64f2]"
                    />
                    <button onClick={() => sendReply(c)} disabled={busy === c.id} className="grid h-10 w-20 place-items-center rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-sm font-bold text-white">
                      {busy === c.id ? <Spinner /> : "ارسال"}
                    </button>
                  </div>
                )}

                <div className="mt-4 flex flex-wrap items-center gap-2 border-t border-white/8 pt-4">
                  {c.status !== "Approved" && (
                    <button onClick={() => setStatus(c, "Approved")} disabled={busy === c.id} className="flex h-9 items-center gap-1.5 rounded-lg bg-emerald-500/15 px-4 text-xs font-bold text-emerald-400 transition hover:bg-emerald-500/25">
                      <AdminIcon name="check" className="h-4 w-4" /> تایید
                    </button>
                  )}
                  {c.status !== "Rejected" && (
                    <button onClick={() => setStatus(c, "Rejected")} disabled={busy === c.id} className="flex h-9 items-center gap-1.5 rounded-lg bg-rose-500/15 px-4 text-xs font-bold text-rose-400 transition hover:bg-rose-500/25">
                      <AdminIcon name="close" className="h-4 w-4" /> رد
                    </button>
                  )}
                  <button onClick={() => { setReplyFor(replyFor === c.id ? null : c.id); setReplyText(""); }} className="flex h-9 items-center gap-1.5 rounded-lg border border-white/10 px-4 text-xs font-bold text-white/75 transition hover:bg-white/5">
                    <AdminIcon name="chat" className="h-4 w-4" /> پاسخ
                  </button>
                  <button onClick={() => remove(c)} className="mr-auto grid h-9 w-9 place-items-center rounded-lg border border-white/10 text-white/55 transition hover:border-rose-500/50 hover:text-rose-400">
                    <AdminIcon name="trash" className="h-4 w-4" />
                  </button>
                </div>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
