"use client";

import { useState, type ReactNode } from "react";
import { toFa } from "@/lib/format";
import type { Product, Comment } from "@/lib/types";
import Stars from "@/components/Stars";
import ReviewForm from "@/components/ReviewForm";
import RichText from "@/components/RichText";

const ACTIVATION_STEPS = [
  { title: "دریافت اطلاعات", desc: "بلافاصله پس از پرداخت، اطلاعات اشتراک در بخش «سفارشات من» برای شما ثبت می‌شود." },
  { title: "ورود به سرویس", desc: "با اطلاعات دریافتی وارد اپلیکیشن یا وب‌سایت سرویس موردنظر شوید." },
  { title: "تنظیم پروفایل", desc: "پروفایل اختصاصی خود را انتخاب کنید و تنظیمات دلخواه را اعمال کنید." },
  { title: "لذت استفاده", desc: "اشتراک شما فعال است؛ در صورت هرگونه مشکل، پشتیبانی ۲۴/۷ کنار شماست." },
];

/** Tabbed content: description (+specs), features, activation steps, and reviews. */
export default function ProductTabs({ product, comments }: { product: Product; comments: Comment[] }) {
  const topLevel = comments.filter((c) => c.parentId == null);
  const included = product.features.filter((f) => f.included);

  const tabs: { key: string; label: string }[] = [
    { key: "desc", label: "توضیحات محصول" },
    ...(product.features.length > 0 ? [{ key: "features", label: "ویژگی‌ها" }] : []),
    { key: "activation", label: "نحوه فعال‌سازی" },
    { key: "reviews", label: `نظرات کاربران (${toFa(topLevel.length)})` },
  ];
  const [active, setActive] = useState("desc");

  return (
    <section className="mt-10 rounded-[22px] border bg-[var(--ac-panel-bg)]" style={{ borderColor: "var(--ac-panel-border)", boxShadow: "var(--ac-panel-shadow)" }}>
      {/* tab bar */}
      <div className="flex gap-1 overflow-x-auto border-b px-3 pt-3" style={{ borderColor: "var(--ac-divider)" }}>
        {tabs.map((t) => {
          const on = active === t.key;
          return (
            <button
              key={t.key}
              type="button"
              onClick={() => setActive(t.key)}
              className="shrink-0 rounded-t-xl border-b-2 px-4 py-3 text-[13px] font-bold transition md:px-5"
              style={on
                ? { borderColor: "var(--ac-menu-active-border)", background: "var(--ac-menu-active-bg)", color: "var(--ac-menu-active-text)" }
                : { borderColor: "transparent", color: "var(--ac-text)" }}
            >
              {t.label}
            </button>
          );
        })}
      </div>

      <div className="p-5 md:p-8">
        {active === "desc" && (
          <div className="grid gap-6 lg:grid-cols-[1fr_320px]">
            <div>
              <h2 className="mb-4 text-lg font-black" style={{ color: "var(--ac-title)" }}>درباره {product.name}</h2>
              <RichText content={product.description} />
              {product.warning && (
                <div className="mt-6 rounded-xl border border-amber-500/40 bg-amber-500/[0.08] p-5">
                  <p className="flex items-center gap-2 text-sm font-black text-amber-500">⚠ مطالعه اجباری</p>
                  <p className="mt-2 text-sm leading-8" style={{ color: "var(--ac-text)" }}>{product.warning}</p>
                </div>
              )}
            </div>
            {included.length > 0 && (
              <aside className="h-fit rounded-xl border p-5" style={{ borderColor: "var(--ac-panel-border)", background: "var(--ac-menu-hover)" }}>
                <h3 className="mb-4 text-[14px] font-black" style={{ color: "var(--ac-title)" }}>مشخصات</h3>
                <ul className="space-y-3">
                  {included.map((f) => (
                    <li key={f.text} className="flex items-start gap-2.5 text-[13px] leading-6" style={{ color: "var(--ac-text)" }}>
                      <span className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full" style={{ background: "#FF6A2B" }} />
                      {f.text}
                    </li>
                  ))}
                </ul>
              </aside>
            )}
          </div>
        )}

        {active === "features" && (
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {product.features.map((f) => (
              <div key={f.text} className="flex items-center gap-3 rounded-xl border p-4" style={{ borderColor: "var(--ac-panel-border)", background: f.included ? "var(--ac-menu-hover)" : "transparent" }}>
                <span className={`grid h-9 w-9 shrink-0 place-items-center rounded-full text-sm font-black ${f.included ? "bg-emerald-500/15 text-emerald-500" : "bg-rose-500/10 text-rose-400"}`}>
                  {f.included ? "✓" : "✕"}
                </span>
                <span className={`text-[13px] font-bold leading-6 ${f.included ? "" : "line-through opacity-60"}`} style={{ color: "var(--ac-text)" }}>{f.text}</span>
              </div>
            ))}
          </div>
        )}

        {active === "activation" && (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {ACTIVATION_STEPS.map((s, i) => (
              <div key={s.title} className="rounded-xl border p-5 text-center" style={{ borderColor: "var(--ac-panel-border)" }}>
                <span className="mx-auto grid h-12 w-12 place-items-center rounded-full text-[18px] font-black text-white" style={{ background: "var(--ac-btn)" }}>
                  {toFa(i + 1)}
                </span>
                <h3 className="mt-3 text-[14px] font-black" style={{ color: "var(--ac-title)" }}>{s.title}</h3>
                <p className="mt-2 text-[12px] leading-6" style={{ color: "var(--ac-muted)" }}>{s.desc}</p>
              </div>
            ))}
          </div>
        )}

        {active === "reviews" && (
          <div className="grid gap-6 lg:grid-cols-[1fr_360px]">
            <div className="space-y-4">
              {topLevel.length === 0 ? (
                <p className="rounded-xl border p-6 text-sm" style={{ borderColor: "var(--ac-panel-border)", color: "var(--ac-muted)" }}>
                  هنوز نظری ثبت نشده است. اولین نفری باشید که نظر می‌دهد!
                </p>
              ) : (
                topLevel.map((c) => <ReviewCard key={c.id} comment={c} replies={comments.filter((r) => r.parentId === c.id)} />)
              )}
            </div>
            <ReviewForm productId={product.id} />
          </div>
        )}
      </div>
    </section>
  );
}

function ReviewCard({ comment, replies }: { comment: Comment; replies: Comment[] }) {
  return (
    <div className="rounded-xl border p-5" style={{ borderColor: "var(--ac-panel-border)" }}>
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <span className="grid h-9 w-9 place-items-center rounded-full text-sm font-bold text-white" style={{ background: "var(--ac-btn)" }}>
            {comment.userName.charAt(0)}
          </span>
          <div>
            <p className="text-sm font-bold" style={{ color: "var(--ac-title)" }}>{comment.userName}</p>
            <p className="text-xs" style={{ color: "var(--ac-muted)" }}>{comment.date}</p>
          </div>
        </div>
        {comment.rating > 0 && <Stars value={comment.rating} />}
      </div>
      <p className="mt-3 text-sm leading-7" style={{ color: "var(--ac-text)" }}>{comment.body}</p>
      {replies.map((r) => (
        <div key={r.id} className="mt-3 rounded-lg p-4" style={{ background: "var(--ac-menu-hover)" }}>
          <p className="text-xs font-bold" style={{ color: "#F2551F" }}>{r.userName}{r.isAdminReply ? " (پشتیبانی)" : ""}</p>
          <p className="mt-1.5 text-sm leading-7" style={{ color: "var(--ac-text)" }}>{r.body}</p>
        </div>
      ))}
    </div>
  );
}

/** Small reusable trust item used by the page-level trust row. */
export function TrustItem({ icon, title, desc }: { icon: ReactNode; title: string; desc: string }) {
  return (
    <div className="flex shrink-0 basis-[46%] snap-start flex-col items-center gap-1.5 px-3 py-4 text-center sm:basis-auto">
      <span className="opacity-90" style={{ color: "var(--ac-muted)" }}>{icon}</span>
      <p className="text-[12.5px] font-bold" style={{ color: "var(--ac-text)" }}>{title}</p>
      <p className="text-[11px] leading-5" style={{ color: "var(--ac-muted)" }}>{desc}</p>
    </div>
  );
}
