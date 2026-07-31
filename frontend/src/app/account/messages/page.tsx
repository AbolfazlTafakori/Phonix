"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { PageTitle, Panel } from "@/components/account/Panel";
import type { Notification } from "@/lib/types";

function fmtDate(iso: string): string {
  try { return new Date(iso).toLocaleString("fa-IR", { dateStyle: "short", timeStyle: "short" }); }
  catch { return ""; }
}

export default function MessagesPage() {
  const { user } = useAuth();
  const [items, setItems] = useState<Notification[]>([]);
  const [tab, setTab] = useState<"private" | "public">("private");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!user) return;
    let alive = true;
    api.notifications.mine()
      .then((list) => {
        if (!alive) return;
        setItems(list);
        // Opening the inbox clears the unread badge, same as the header bell does.
        if (list.some((n) => !n.isRead)) api.notifications.markRead().catch(() => {});
      })
      .catch(() => {})
      .finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, [user]);

  const shown = items.filter((n) => (tab === "public" ? n.isPublic : !n.isPublic));

  return (
    <div>
      <PageTitle title="پیام‌ها" desc="اطلاع‌رسانی‌ها و پیام‌های شما." />

      <div className="mb-4 flex gap-1 border-b" style={{ borderColor: "var(--ac-divider)" }}>
        {([["private", "پیام‌های خصوصی"], ["public", "پیام‌های عمومی"]] as const).map(([key, label]) => {
          const on = tab === key;
          return (
            <button
              key={key}
              type="button"
              onClick={() => setTab(key)}
              className="relative px-4 py-3 text-[13px] font-bold transition sm:text-[14px]"
              style={{ color: on ? "var(--ac-title)" : "var(--ac-muted)" }}
            >
              {label}
              <span className="absolute inset-x-2 -bottom-px h-0.5 rounded-full transition-opacity" style={{ background: "var(--ac-btn)", opacity: on ? 1 : 0 }} />
            </button>
          );
        })}
      </div>

      {loading ? (
        <Panel>
          <div className="grid h-24 place-items-center">
            <span className="inline-block h-7 w-7 animate-spin rounded-full border-2 border-[rgba(166,102,45,0.2)] border-t-[#FF5A1F]" />
          </div>
        </Panel>
      ) : shown.length === 0 ? (
        <Panel>
          <p className="py-8 text-center" style={{ color: "var(--ac-muted)" }}>پیامی وجود ندارد.</p>
        </Panel>
      ) : (
        <div className="flex flex-col gap-3">
          {shown.map((n) => (
            <Panel key={n.id}>
              <div className="flex items-start justify-between gap-3">
                <h3 className="flex min-w-0 items-center gap-2 text-[14px] font-black" style={{ color: "var(--ac-title)" }}>
                  {!n.isRead && <span className="h-2 w-2 shrink-0 rounded-full bg-[#F2551F]" />}
                  <span className="truncate">{n.title}</span>
                </h3>
                <span className="shrink-0 text-[11px]" dir="ltr" style={{ color: "var(--ac-muted)" }}>{fmtDate(n.createdAtUtc)}</span>
              </div>
              {n.body && <p className="mt-2 whitespace-pre-wrap text-[13px] leading-7" style={{ color: "var(--ac-text)" }}>{n.body}</p>}
              {n.link && (
                <Link href={n.link} className="mt-3 inline-block text-[13px] font-bold transition hover:opacity-70" style={{ color: "var(--hl-orange-text)" }}>
                  مشاهده ‹
                </Link>
              )}
            </Panel>
          ))}
        </div>
      )}
    </div>
  );
}
