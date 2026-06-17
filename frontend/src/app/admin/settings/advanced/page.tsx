"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { AdvancedSettings } from "@/lib/types";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";

export default function AdvancedSettingsPage() {
  const [data, setData] = useState<AdvancedSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        setData(await api.advancedSettings.get());
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const set = <K extends keyof AdvancedSettings>(key: K, value: AdvancedSettings[K]) =>
    setData((d) => (d ? { ...d, [key]: value } : d));

  async function save() {
    if (!data) return;
    setSaving(true);
    setSaved(false);
    try {
      setData(await api.advancedSettings.update(data));
      setSaved(true);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div>
      <PageHeader
        title="تنظیمات پیشرفته"
        desc="سئو، حالت تعمیر و اسکریپت‌های فنی سایت"
        action={
          data && (
            <div className="flex items-center gap-3">
              {saved && <span className="text-sm font-medium text-emerald-400">✓ ذخیره شد</span>}
              <button
                onClick={save}
                disabled={saving}
                className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110"
              >
                {saving ? <Spinner /> : "ذخیره تغییرات"}
              </button>
            </div>
          )
        }
      />

      {loading ? (
        <div className="grid place-items-center py-24">
          <Spinner className="h-8 w-8" />
        </div>
      ) : error || !data ? (
        <Card className="p-8 text-center text-rose-400">{error || "اطلاعات یافت نشد"}</Card>
      ) : (
        <div className="grid gap-6 lg:grid-cols-2">
          <Card className="p-6">
            <h3 className="mb-5 text-lg font-bold text-white">سئو (SEO)</h3>
            <div className="grid gap-4">
              <Field label="عنوان متا (title)">
                <input value={data.metaTitle} onChange={(e) => set("metaTitle", e.target.value)} className={inputCls} />
              </Field>
              <Field label="توضیحات متا (description)">
                <textarea rows={3} value={data.metaDescription} onChange={(e) => set("metaDescription", e.target.value)} className={`${inputCls} h-auto py-3`} />
              </Field>
              <Field label="کلمات کلیدی (با کاما جدا کنید)">
                <input value={data.metaKeywords} onChange={(e) => set("metaKeywords", e.target.value)} className={inputCls} />
              </Field>
            </div>
          </Card>

          <div className="space-y-6">
            <Card className="p-6">
              <h3 className="mb-4 text-lg font-bold text-white">حالت تعمیر</h3>
              <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
                <span className="text-sm text-white/80">فعال‌سازی حالت تعمیر سایت</span>
                <Toggle checked={data.maintenanceMode} onChange={(v) => set("maintenanceMode", v)} />
              </label>
              {data.maintenanceMode && (
                <p className="mt-2 text-xs text-amber-400/80">با فعال بودن این گزینه، فروشگاه برای بازدیدکنندگان بسته می‌شود؛ پنل مدیریت همچنان در دسترس است.</p>
              )}
              <div className="mt-4 grid gap-4">
                <Field label="عنوان حالت تعمیر">
                  <input value={data.maintenanceTitle} onChange={(e) => set("maintenanceTitle", e.target.value)} className={inputCls} />
                </Field>
                <Field label="پیام حالت تعمیر">
                  <textarea rows={2} value={data.maintenanceMessage} onChange={(e) => set("maintenanceMessage", e.target.value)} className={`${inputCls} h-auto py-3`} />
                </Field>
              </div>
            </Card>

            <Card className="p-6">
              <h3 className="mb-4 text-lg font-bold text-white">اسکریپت‌ها و آنالیتیکس</h3>
              <div className="grid gap-4">
                <Field label="شناسه گوگل آنالیتیکس">
                  <input value={data.analyticsId} onChange={(e) => set("analyticsId", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="G-XXXXXXXXXX" />
                </Field>
                <Field label="اسکریپت سفارشی (head)">
                  <textarea rows={4} value={data.customHeadScript} onChange={(e) => set("customHeadScript", e.target.value)} dir="ltr" className={`${inputCls} h-auto py-3 text-left font-mono text-xs`} placeholder="<script>...</script>" />
                </Field>
              </div>
            </Card>
          </div>
        </div>
      )}
    </div>
  );
}
