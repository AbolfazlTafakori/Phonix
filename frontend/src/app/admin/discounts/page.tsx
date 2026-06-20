"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { DiscountCode, DiscountCodeInput, DiscountType } from "@/lib/types";
import { formatToman, formatNumber, toFa } from "@/lib/format";
import { Card, PageHeader, Spinner, Toggle, StatusBadge, Modal, DataTable, Field, inputCls, type Column } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

const emptyForm = (): DiscountCodeInput => ({
  code: "",
  type: "Percent",
  value: 10,
  minOrder: 0,
  maxDiscount: 0,
  usageLimit: 0,
  isActive: true,
  expiresAt: null,
});

const typeLabel: Record<DiscountType, string> = { Percent: "درصدی", Fixed: "مبلغ ثابت" };

const HOUR = 3600 * 1000;

// remaining whole days/hours from now until an ISO expiry (for prefilling the edit form).
function remainingDH(expiresAt: string | null): { days: number; hours: number } {
  if (!expiresAt) return { days: 0, hours: 0 };
  const ms = new Date(expiresAt).getTime() - Date.now();
  if (ms <= 0) return { days: 0, hours: 0 };
  const totalHours = Math.ceil(ms / HOUR);
  return { days: Math.floor(totalHours / 24), hours: totalHours % 24 };
}

function expiryLabel(expiresAt: string | null): string {
  if (!expiresAt) return "بدون انقضا";
  const ms = new Date(expiresAt).getTime() - Date.now();
  if (ms <= 0) return "منقضی شده";
  const { days, hours } = remainingDH(expiresAt);
  if (days > 0) return `${toFa(days)} روز و ${toFa(hours)} ساعت`;
  return `${toFa(Math.max(1, hours))} ساعت`;
}

export default function AdminDiscountsPage() {
  const [codes, setCodes] = useState<DiscountCode[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<DiscountCodeInput>(emptyForm());
  const [expDays, setExpDays] = useState(0);
  const [expHours, setExpHours] = useState(0);
  const [saving, setSaving] = useState(false);

  async function load() {
    try {
      setCodes(await api.discounts.list());
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری");
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => {
    load();
  }, []);

  const set = <K extends keyof DiscountCodeInput>(key: K, value: DiscountCodeInput[K]) => setForm((f) => ({ ...f, [key]: value }));

  function openNew() {
    setEditingId(null);
    setForm(emptyForm());
    setExpDays(0);
    setExpHours(0);
    setModalOpen(true);
  }
  function openEdit(c: DiscountCode) {
    setEditingId(c.id);
    setForm({ code: c.code, type: c.type, value: c.value, minOrder: c.minOrder, maxDiscount: c.maxDiscount, usageLimit: c.usageLimit, isActive: c.isActive, expiresAt: c.expiresAt });
    const { days, hours } = remainingDH(c.expiresAt);
    setExpDays(days);
    setExpHours(hours);
    setModalOpen(true);
  }
  async function submit() {
    setSaving(true);
    try {
      const totalHours = expDays * 24 + expHours;
      const expiresAt = totalHours > 0 ? new Date(Date.now() + totalHours * HOUR).toISOString() : null;
      const body = { ...form, expiresAt };
      if (editingId === null) {
        const created = await api.discounts.create(body);
        setCodes((c) => [created, ...c]);
      } else {
        const updated = await api.discounts.update(editingId, body);
        setCodes((c) => c.map((x) => (x.id === editingId ? updated : x)));
      }
      setModalOpen(false);
    } finally {
      setSaving(false);
    }
  }
  async function remove(c: DiscountCode) {
    if (!confirm(`کد «${c.code}» حذف شود؟`)) return;
    await api.discounts.remove(c.id);
    setCodes((prev) => prev.filter((x) => x.id !== c.id));
  }

  const valueText = (c: DiscountCode) => (c.type === "Percent" ? `${toFa(c.value)}٪` : formatToman(c.value));

  const columns: Column<DiscountCode>[] = [
    { header: "کد", primary: true, cell: (c) => <span className="font-mono font-bold text-white" dir="ltr">{c.code}</span> },
    { header: "نوع", td: "text-white/65", cell: (c) => typeLabel[c.type] },
    { header: "مقدار", cell: (c) => valueText(c) },
    { header: "حداقل سفارش", td: "text-white/65", cell: (c) => (c.minOrder > 0 ? formatToman(c.minOrder) : "—") },
    { header: "استفاده", td: "text-white/70", cell: (c) => `${formatNumber(c.usedCount)} / ${c.usageLimit > 0 ? formatNumber(c.usageLimit) : "∞"}` },
    { header: "انقضا", td: "text-white/65", cell: (c) => expiryLabel(c.expiresAt) },
    { header: "وضعیت", cell: (c) => <StatusBadge status={c.isActive ? "فعال" : "غیرفعال"} /> },
    {
      header: "عملیات",
      full: true,
      cell: (c) => (
        <div className="flex items-center gap-2">
          <button onClick={() => openEdit(c)} className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-white/60 transition hover:border-[#3a64f2]/50 hover:text-[#6f93ff]">
            <AdminIcon name="edit" className="h-4 w-4" />
          </button>
          <button onClick={() => remove(c)} className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-white/60 transition hover:border-rose-500/50 hover:text-rose-400">
            <AdminIcon name="trash" className="h-4 w-4" />
          </button>
        </div>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="کدهای تخفیف"
        desc={`${formatNumber(codes.length)} کد`}
        action={
          <button onClick={openNew} className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110">
            <AdminIcon name="plus" className="h-4 w-4" />
            افزودن کد
          </button>
        }
      />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : (
        <Card className="overflow-hidden">
          <DataTable columns={columns} rows={codes} rowKey={(c) => c.id} minWidth={820} empty="هنوز کد تخفیفی ثبت نشده است" />
        </Card>
      )}

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title={editingId === null ? "افزودن کد تخفیف" : "ویرایش کد تخفیف"}>
        <div className="grid gap-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="کد تخفیف">
              <input value={form.code} onChange={(e) => set("code", e.target.value.toUpperCase())} dir="ltr" placeholder="WELCOME10" className={`${inputCls} text-left font-mono`} />
            </Field>
            <Field label="نوع تخفیف">
              <select value={form.type} onChange={(e) => set("type", e.target.value as DiscountType)} className={inputCls}>
                <option value="Percent" className="bg-[#15151f]">درصدی</option>
                <option value="Fixed" className="bg-[#15151f]">مبلغ ثابت</option>
              </select>
            </Field>
            <Field label={form.type === "Percent" ? "درصد تخفیف (٪)" : "مبلغ تخفیف (تومان)"}>
              <input type="number" dir="ltr" value={form.value} onChange={(e) => set("value", Number(e.target.value))} className={`${inputCls} text-left`} />
            </Field>
            {form.type === "Percent" && (
              <Field label="سقف تخفیف (تومان، ۰ = بی‌نهایت)">
                <input type="number" dir="ltr" value={form.maxDiscount} onChange={(e) => set("maxDiscount", Number(e.target.value))} className={`${inputCls} text-left`} />
              </Field>
            )}
            <Field label="حداقل مبلغ سفارش (تومان)">
              <input type="number" dir="ltr" value={form.minOrder} onChange={(e) => set("minOrder", Number(e.target.value))} className={`${inputCls} text-left`} />
            </Field>
            <Field label="سقف دفعات استفاده (۰ = نامحدود)">
              <input type="number" dir="ltr" value={form.usageLimit} onChange={(e) => set("usageLimit", Number(e.target.value))} className={`${inputCls} text-left`} />
            </Field>
          </div>

          <div className="rounded-xl border border-white/8 p-4">
            <p className="mb-1 text-sm font-bold text-white">مدت اعتبار</p>
            <p className="mb-3 text-xs text-white/40">از زمان ذخیره محاسبه می‌شود. هر دو صفر = بدون انقضا.</p>
            <div className="grid grid-cols-2 gap-4">
              <Field label="روز">
                <input type="number" dir="ltr" min={0} value={expDays} onChange={(e) => setExpDays(Math.max(0, Number(e.target.value)))} className={`${inputCls} text-left`} />
              </Field>
              <Field label="ساعت">
                <input type="number" dir="ltr" min={0} max={23} value={expHours} onChange={(e) => setExpHours(Math.max(0, Number(e.target.value)))} className={`${inputCls} text-left`} />
              </Field>
            </div>
          </div>

          <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
            <span className="text-sm text-white/80">فعال</span>
            <Toggle checked={form.isActive} onChange={(v) => set("isActive", v)} />
          </label>

          <div className="flex gap-3">
            <button onClick={submit} disabled={saving || !form.code.trim()} className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-8 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50">
              {saving ? <Spinner /> : "ذخیره کد"}
            </button>
            <button onClick={() => setModalOpen(false)} className="h-11 rounded-xl border border-white/10 px-8 text-sm font-bold text-white/80 transition hover:bg-white/5">انصراف</button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
