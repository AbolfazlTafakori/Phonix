"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { PaymentMethod, PaymentMethodInput, PaymentSettings, PaymentType } from "@/lib/types";
import { formatToman } from "@/lib/format";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

const typeLabels: Record<PaymentType, string> = { Card: "کارت بانکی", Crypto: "ارز دیجیتال", Gateway: "درگاه پرداخت" };
const typeOptions: PaymentType[] = ["Card", "Crypto", "Gateway"];

const valueLabel: Record<PaymentType, string> = { Card: "شماره کارت", Crypto: "آدرس کیف پول", Gateway: "شناسه درگاه" };
const networkLabel: Record<PaymentType, string> = { Card: "بانک", Crypto: "شبکه", Gateway: "ارائه‌دهنده" };

export default function AdminPaymentsPage() {
  const [methods, setMethods] = useState<PaymentMethod[]>([]);
  const [drafts, setDrafts] = useState<Record<number, PaymentMethodInput>>({});
  const [settings, setSettings] = useState<PaymentSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState<number | null>(null);
  const [adding, setAdding] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const [m, s] = await Promise.all([api.paymentMethods.list(), api.paymentSettings.get()]);
        setMethods(m);
        setDrafts(Object.fromEntries(m.map((x) => [x.id, stripId(x)])));
        setSettings(s);
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const setField = <K extends keyof PaymentMethodInput>(id: number, key: K, value: PaymentMethodInput[K]) =>
    setDrafts((p) => ({ ...p, [id]: { ...p[id], [key]: value } }));
  const dirty = (m: PaymentMethod) => JSON.stringify(drafts[m.id]) !== JSON.stringify(stripId(m));

  async function save(m: PaymentMethod) {
    setBusy(m.id);
    try {
      const u = await api.paymentMethods.update(m.id, drafts[m.id]);
      setMethods((p) => p.map((x) => (x.id === m.id ? u : x)));
      setDrafts((p) => ({ ...p, [m.id]: stripId(u) }));
    } finally {
      setBusy(null);
    }
  }
  async function remove(m: PaymentMethod) {
    if (!confirm(`روش «${m.title}» حذف شود؟`)) return;
    setBusy(m.id);
    try {
      await api.paymentMethods.remove(m.id);
      setMethods((p) => p.filter((x) => x.id !== m.id));
    } finally {
      setBusy(null);
    }
  }
  async function add() {
    setAdding(true);
    try {
      const created = await api.paymentMethods.create({
        type: "Card",
        title: "روش جدید",
        holder: "",
        value: "",
        network: "",
        sheba: "",
        accountNumber: "",
        instructions: "",
        feePercent: 0,
        isActive: true,
        sortOrder: methods.length + 1,
      });
      setMethods((p) => [...p, created]);
      setDrafts((p) => ({ ...p, [created.id]: stripId(created) }));
    } finally {
      setAdding(false);
    }
  }

  return (
    <div>
      <PageHeader
        title="روش‌های پرداخت"
        desc="کارت بانکی، کیف پول ارز دیجیتال و درگاه‌های پرداخت"
        action={
          <button onClick={add} disabled={adding} className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110">
            {adding ? <Spinner /> : <AdminIcon name="plus" className="h-4 w-4" />}
            روش جدید
          </button>
        }
      />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : (
        <div className="space-y-6">
          <div className="grid gap-5 lg:grid-cols-2">
            {methods.map((m) => {
              const d = drafts[m.id];
              if (!d) return null;
              return (
                <Card key={m.id} className="p-5">
                  <div className="mb-4 flex items-center justify-between">
                    <span className="rounded-lg bg-[#3a64f2]/15 px-3 py-1 text-xs font-bold text-[#6f93ff]">{typeLabels[d.type]}</span>
                    <label className="flex items-center gap-2 text-xs text-white/60">
                      نمایش
                      <Toggle checked={d.isActive} onChange={(v) => setField(m.id, "isActive", v)} />
                    </label>
                  </div>
                  <div className="grid gap-3">
                    <div className="grid grid-cols-2 gap-3">
                      <Field label="نوع">
                        <select value={d.type} onChange={(e) => setField(m.id, "type", e.target.value as PaymentType)} className={`${inputCls} h-10`}>
                          {typeOptions.map((t) => <option key={t} value={t} className="bg-[#15151f]">{typeLabels[t]}</option>)}
                        </select>
                      </Field>
                      <Field label="عنوان">
                        <input value={d.title} onChange={(e) => setField(m.id, "title", e.target.value)} className={`${inputCls} h-10`} />
                      </Field>
                      <Field label="صاحب حساب / برچسب">
                        <input value={d.holder} onChange={(e) => setField(m.id, "holder", e.target.value)} className={`${inputCls} h-10`} />
                      </Field>
                      <Field label={networkLabel[d.type]}>
                        <input value={d.network} onChange={(e) => setField(m.id, "network", e.target.value)} className={`${inputCls} h-10`} />
                      </Field>
                    </div>
                    <Field label={valueLabel[d.type]}>
                      <input value={d.value} onChange={(e) => setField(m.id, "value", e.target.value)} dir="ltr" className={`${inputCls} h-10 text-left`} />
                    </Field>
                    {d.type === "Card" && (
                      <div className="grid grid-cols-2 gap-3">
                        <Field label="شماره شبا (اختیاری)">
                          <input value={d.sheba} onChange={(e) => setField(m.id, "sheba", e.target.value)} dir="ltr" placeholder="IR..." className={`${inputCls} h-10 text-left`} />
                        </Field>
                        <Field label="شماره حساب (اختیاری)">
                          <input value={d.accountNumber} onChange={(e) => setField(m.id, "accountNumber", e.target.value)} dir="ltr" className={`${inputCls} h-10 text-left`} />
                        </Field>
                      </div>
                    )}
                    <Field label="راهنمای پرداخت">
                      <textarea rows={2} value={d.instructions} onChange={(e) => setField(m.id, "instructions", e.target.value)} className={`${inputCls} h-auto py-2.5`} />
                    </Field>
                    <Field label="کارمزد / مالیات (٪)">
                      <input type="number" dir="ltr" min={0} value={d.feePercent} onChange={(e) => setField(m.id, "feePercent", Math.max(0, Number(e.target.value)))} className={`${inputCls} h-10 text-left`} />
                    </Field>
                    <div className="flex items-center gap-2">
                      <button
                        onClick={() => save(m)}
                        disabled={!dirty(m) || busy === m.id}
                        className={`grid h-10 flex-1 place-items-center rounded-xl text-sm font-bold transition ${dirty(m) ? "bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-white hover:brightness-110" : "cursor-default border border-white/10 text-white/30"}`}
                      >
                        {busy === m.id ? <Spinner /> : "ذخیره"}
                      </button>
                      <button onClick={() => remove(m)} className="grid h-10 w-10 place-items-center rounded-xl border border-white/10 text-white/55 transition hover:border-rose-500/50 hover:text-rose-400">
                        <AdminIcon name="trash" className="h-4 w-4" />
                      </button>
                    </div>
                  </div>
                </Card>
              );
            })}
          </div>

          {settings && <PaymentSettingsCard settings={settings} setSettings={setSettings} />}
        </div>
      )}
    </div>
  );
}

function PaymentSettingsCard({ settings, setSettings }: { settings: PaymentSettings; setSettings: (s: PaymentSettings) => void }) {
  const [draft, setDraft] = useState<PaymentSettings>(settings);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  const set = <K extends keyof PaymentSettings>(key: K, value: PaymentSettings[K]) => setDraft((p) => ({ ...p, [key]: value }));

  async function save() {
    setSaving(true);
    setSaved(false);
    try {
      const u = await api.paymentSettings.update(draft);
      setSettings(u);
      setDraft(u);
      setSaved(true);
    } finally {
      setSaving(false);
    }
  }

  return (
    <Card className="p-6">
      <h3 className="mb-1 text-lg font-bold text-white">اتصال تلگرام و تأیید پرداخت</h3>
      <p className="mb-5 text-sm text-white/45">رسیدها به ربات تلگرام ارسال و تأیید دستی از سایت یا تلگرام انجام می‌شود.</p>

      <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
        <span className="text-sm text-white/80">فعال‌سازی ربات تلگرام</span>
        <Toggle checked={draft.telegramEnabled} onChange={(v) => set("telegramEnabled", v)} />
      </label>

      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <Field label="توکن ربات (Bot Token)">
          <input value={draft.telegramBotToken} onChange={(e) => set("telegramBotToken", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="123456:ABC-DEF..." />
        </Field>
        <Field label="شناسه چت ادمین (Chat ID)">
          <input value={draft.telegramChatId} onChange={(e) => set("telegramChatId", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="-1001234567890" />
        </Field>
      </div>

      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
          <span className="text-sm text-white/80">الزام ارسال رسید</span>
          <Toggle checked={draft.requireReceipt} onChange={(v) => set("requireReceipt", v)} />
        </label>
        <Field label="تأیید خودکار زیر مبلغ (۰ = غیرفعال)">
          <input type="number" dir="ltr" value={draft.autoApproveUnder} onChange={(e) => set("autoApproveUnder", Number(e.target.value))} className={`${inputCls} text-left`} />
        </Field>
      </div>
      {draft.autoApproveUnder > 0 && (
        <p className="mt-2 text-xs text-white/45">تراکنش‌های زیر {formatToman(draft.autoApproveUnder)} به‌صورت خودکار تأیید می‌شوند.</p>
      )}

      <div className="mt-6 flex items-center gap-3">
        <button onClick={save} disabled={saving} className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 text-sm font-bold text-white transition hover:brightness-110">
          {saving ? <Spinner /> : "ذخیره تنظیمات"}
        </button>
        {saved && <span className="text-sm font-medium text-emerald-400">✓ ذخیره شد</span>}
      </div>
    </Card>
  );
}

function stripId<T extends { id: number }>(item: T): Omit<T, "id"> {
  const { id, ...rest } = item;
  void id;
  return rest;
}
