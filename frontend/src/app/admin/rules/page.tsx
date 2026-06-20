"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { AdvancedSettings } from "@/lib/types";
import { Card, PageHeader, Spinner, inputCls } from "@/components/admin/ui";

export default function AdminRulesPage() {
  const [data, setData] = useState<AdvancedSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    api.advancedSettings
      .get()
      .then(setData)
      .finally(() => setLoading(false));
  }, []);

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
        title="قوانین و مقررات"
        desc="متن قوانین سایت که در صفحه‌ی عمومی /terms نمایش داده می‌شود."
        action={
          data && (
            <div className="flex items-center gap-3">
              {saved && <span className="text-sm font-medium text-emerald-400">✓ ذخیره شد</span>}
              <button onClick={save} disabled={saving} className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110">
                {saving ? <Spinner /> : "ذخیره"}
              </button>
            </div>
          )
        }
      />

      {loading || !data ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : (
        <Card className="p-6">
          <textarea
            rows={20}
            value={data.terms}
            onChange={(e) => setData({ ...data, terms: e.target.value })}
            placeholder="قوانین و مقررات، شرایط استفاده، سیاست بازگشت وجه و... را اینجا بنویسید."
            className={`${inputCls} h-auto py-3 leading-8`}
          />
          <p className="mt-2 text-xs text-white/45">هر خط جدید در صفحه‌ی عمومی حفظ می‌شود.</p>
        </Card>
      )}
    </div>
  );
}
