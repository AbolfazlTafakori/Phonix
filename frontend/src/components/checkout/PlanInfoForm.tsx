"use client";

import { useState } from "react";
import type { PlanInfoSettings, PlanInputField } from "@/lib/types";

export type PlanInfoValue = { values: Record<string, string>; note: string };

export const emptyPlanInfoValue = (): PlanInfoValue => ({ values: {}, note: "" });

// True once every required field for the plan has a non-empty value — used by checkout to gate the order.
export function isPlanInfoComplete(plan: PlanInfoSettings, value: PlanInfoValue): boolean {
  return plan.inputFields.every((f) => !f.required || (value.values[f.label] ?? "").trim().length > 0);
}

function inputType(t: PlanInputField["type"]): string {
  if (t === "email") return "email";
  if (t === "phone") return "tel";
  return "text"; // password is shown as text — the customer is intentionally sharing it
}

export default function PlanInfoForm({
  title,
  plan,
  value,
  onChange,
}: {
  title: string;
  plan: PlanInfoSettings;
  value: PlanInfoValue;
  onChange: (v: PlanInfoValue) => void;
}) {
  const [tutorialOpen, setTutorialOpen] = useState(false);
  const setField = (label: string, v: string) => onChange({ ...value, values: { ...value.values, [label]: v } });

  return (
    <div className="rounded-2xl border border-[var(--hl-border)] bg-white p-5" dir="rtl">
      <h3 className="text-lg font-bold text-[var(--hl-ink)]">اطلاعات موردنیاز سرویس</h3>
      <p className="mt-1 text-xs text-[var(--hl-muted)]">{title}</p>

      {plan.warningText && (
        <div className="mt-4 flex gap-2.5 rounded-xl border border-amber-500/30 bg-amber-500/[0.08] px-3.5 py-3">
          <span className="text-amber-300">⚠</span>
          <p className="text-xs leading-7 text-amber-100/85">{plan.warningText}</p>
        </div>
      )}

      {plan.tutorialText && (
        <div className="mt-4">
          <button
            type="button"
            onClick={() => setTutorialOpen((o) => !o)}
            className="flex w-full items-center justify-between rounded-xl border border-[var(--hl-border)] bg-[var(--hl-border)]/20 px-4 py-3 text-sm font-bold text-[var(--hl-ink)] transition hover:bg-[var(--hl-border)]/40"
          >
            <span className="flex items-center gap-2"><span className="text-[#6f93ff]">▷</span> آموزش: چطور انجام دهم؟</span>
            <span className={`text-[var(--hl-muted)] transition-transform ${tutorialOpen ? "" : "-rotate-90"}`}>▾</span>
          </button>
          {tutorialOpen && (
            <div className="mt-2 rounded-xl border border-[var(--hl-border)] bg-white p-4 text-sm leading-8 text-[var(--hl-ink-2)] whitespace-pre-wrap">
              {plan.tutorialText}
            </div>
          )}
        </div>
      )}

      <div className="mt-4 space-y-4">
        {plan.inputFields.map((f) => (
          <div key={f.label}>
            <label className="mb-1.5 block text-sm text-[var(--hl-ink-2)]">
              {f.label} {f.required && <span className="text-[#e60053]">*</span>}
            </label>
            {f.type === "textarea" ? (
              <textarea
                value={value.values[f.label] ?? ""}
                onChange={(e) => setField(f.label, e.target.value)}
                rows={3}
                className="w-full resize-none rounded-xl border border-[var(--hl-border)] bg-white px-3.5 py-2.5 text-sm text-[var(--hl-ink)] outline-none transition focus:border-[#e60053]/50"
              />
            ) : (
              <input
                type={inputType(f.type)}
                dir={f.type === "email" || f.type === "phone" || f.type === "password" ? "ltr" : "rtl"}
                value={value.values[f.label] ?? ""}
                onChange={(e) => setField(f.label, e.target.value)}
                className="w-full rounded-xl border border-[var(--hl-border)] bg-white px-3.5 py-2.5 text-sm text-[var(--hl-ink)] outline-none transition focus:border-[#e60053]/50"
              />
            )}
          </div>
        ))}

        {plan.allowNotes && (
          <div>
            <label className="mb-1.5 block text-sm text-[var(--hl-ink-2)]">توضیحات <span className="text-xs text-[var(--hl-muted)]">(اختیاری)</span></label>
            <textarea
              value={value.note}
              onChange={(e) => onChange({ ...value, note: e.target.value })}
              rows={2}
              placeholder="اگر نکته‌ای دارید بنویسید…"
              className="w-full resize-none rounded-xl border border-[var(--hl-border)] bg-white px-3.5 py-2.5 text-sm text-[var(--hl-ink)] outline-none transition focus:border-[#e60053]/50 placeholder:text-[var(--hl-muted)]"
            />
          </div>
        )}
      </div>
    </div>
  );
}
