"use client";

import { useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";

export default function ReviewForm({ productId }: { productId: number }) {
  const { user, ready } = useAuth();
  const [rating, setRating] = useState(5);
  const [hover, setHover] = useState(0);
  const [body, setBody] = useState("");
  const [sending, setSending] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState("");

  async function submit() {
    if (!user) return;
    if (!body.trim()) {
      setError("لطفاً متن نظر را بنویسید.");
      return;
    }
    setSending(true);
    setError("");
    try {
      await api.comments.submit({ productId, body: body.trim(), rating });
      setDone(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در ارسال نظر");
    } finally {
      setSending(false);
    }
  }

  if (!ready) {
    return <div className="h-40 rounded-2xl border border-[var(--hl-border)] hl-card" />;
  }

  if (!user) {
    return (
      <div className="rounded-2xl border border-[var(--hl-border)] hl-card p-6 text-center">
        <p className="text-sm leading-7 text-[var(--hl-ink-2)]">برای ثبت نظر و امتیاز ابتدا باید وارد حساب کاربری خود شوید.</p>
        <Link
          href="/login"
          className="mt-4 inline-block rounded-xl bg-gradient-to-l from-[#ff7a2e] to-[#f0392c] px-6 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
        >
          ورود / ثبت‌نام
        </Link>
      </div>
    );
  }

  if (done) {
    return (
      <div className="rounded-2xl border border-emerald-500/30 bg-emerald-500/10 p-6 text-center">
        <p className="text-lg font-bold text-emerald-400">✓ نظر شما ثبت شد</p>
        <p className="mt-2 text-sm text-[var(--hl-ink-2)]">نظر شما پس از بررسی و تأیید توسط تیم پشتیبانی نمایش داده می‌شود.</p>
      </div>
    );
  }

  return (
    <div className="rounded-2xl border border-[var(--hl-border)] hl-card p-6">
      <h3 className="mb-1 text-lg font-bold text-[var(--hl-ink)]">ثبت نظر و امتیاز</h3>
      <p className="mb-4 text-xs text-[var(--hl-muted)]">
        به نام <span className="font-bold text-[var(--hl-ink-2)]">{user.username}</span>
      </p>

      <div className="mb-4 flex items-center gap-2">
        <span className="text-sm text-[var(--hl-ink-2)]">امتیاز شما:</span>
        <div dir="ltr" className="flex items-center gap-1">
          {[1, 2, 3, 4, 5].map((i) => (
            <button
              key={i}
              type="button"
              onMouseEnter={() => setHover(i)}
              onMouseLeave={() => setHover(0)}
              onClick={() => setRating(i)}
              className={`text-2xl transition ${i <= (hover || rating) ? "text-amber-400" : "text-[var(--hl-border)] hover:text-[var(--hl-muted)]"}`}
            >
              ★
            </button>
          ))}
        </div>
      </div>

      <textarea
        value={body}
        onChange={(e) => setBody(e.target.value)}
        rows={4}
        placeholder="نظر خود را درباره این محصول بنویسید..."
        className="w-full rounded-xl border border-[var(--hl-border)] hl-card px-4 py-3 text-sm text-[var(--hl-ink)] outline-none focus:border-[#f0392c]"
      />

      {error && <p className="mt-2 text-sm text-rose-400">{error}</p>}

      <button
        onClick={submit}
        disabled={sending}
        className="mt-4 h-11 rounded-xl bg-gradient-to-l from-[#ff7a2e] to-[#f0392c] px-8 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
      >
        {sending ? "در حال ارسال..." : "ارسال نظر"}
      </button>
    </div>
  );
}
