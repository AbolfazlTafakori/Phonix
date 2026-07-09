"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Category, CategoryInput } from "@/lib/types";
import { formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, Toggle, StatusBadge, Modal, DataTable, inputCls, type Column } from "@/components/admin/ui";
import ImageField from "@/components/admin/ImageField";
import AdminIcon from "@/components/admin/AdminIcon";

const empty: CategoryInput = { name: "", slug: "", icon: "", description: "", isActive: true, sortOrder: 0 };

export default function AdminCategoriesPage() {
  const [categories, setCategories] = useState<Category[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<CategoryInput>(empty);
  const [saving, setSaving] = useState(false);

  async function load() {
    try {
      setCategories(await api.categories.list());
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  function openNew() {
    setEditingId(null);
    setForm({ ...empty, sortOrder: categories.length + 1 });
    setModalOpen(true);
  }

  function openEdit(c: Category) {
    setEditingId(c.id);
    setForm({ name: c.name, slug: c.slug, icon: c.icon, description: c.description ?? "", isActive: c.isActive, sortOrder: c.sortOrder });
    setModalOpen(true);
  }

  async function submit() {
    setSaving(true);
    try {
      if (editingId === null) {
        const created = await api.categories.create(form);
        setCategories((prev) => [...prev, created]);
      } else {
        const updated = await api.categories.update(editingId, form);
        setCategories((prev) => prev.map((c) => (c.id === editingId ? updated : c)));
      }
      setModalOpen(false);
    } finally {
      setSaving(false);
    }
  }

  async function toggleActive(c: Category) {
    const updated = await api.categories.update(c.id, {
      name: c.name,
      slug: c.slug,
      icon: c.icon,
      description: c.description ?? "",
      isActive: !c.isActive,
      sortOrder: c.sortOrder,
    });
    setCategories((prev) => prev.map((x) => (x.id === c.id ? updated : x)));
  }

  async function remove(c: Category) {
    if (!confirm(`دسته‌بندی «${c.name}» حذف شود؟`)) return;
    await api.categories.remove(c.id);
    setCategories((prev) => prev.filter((x) => x.id !== c.id));
  }

  const set = <K extends keyof CategoryInput>(key: K, value: CategoryInput[K]) =>
    setForm((prev) => ({ ...prev, [key]: value }));

  const columns: Column<Category>[] = [
    {
      header: "دسته‌بندی",
      primary: true,
      cell: (c) => (
        <div className="flex items-center gap-3">
          <span className="grid h-11 w-11 shrink-0 place-items-center overflow-hidden rounded-lg bg-white/5">
            {c.icon ? <img src={c.icon} alt={c.name} className="h-8 w-8 object-contain" /> : <AdminIcon name="grid" className="h-5 w-5 text-white/40" />}
          </span>
          <span className="font-medium">{c.name}</span>
        </div>
      ),
    },
    { header: "نامک", cell: (c) => <span dir="ltr" className="font-mono text-white/55">{c.slug || "—"}</span> },
    { header: "محصولات", td: "text-white/70", cell: (c) => formatNumber(c.productCount) },
    { header: "ترتیب", td: "text-white/55", cell: (c) => formatNumber(c.sortOrder) },
    {
      header: "وضعیت",
      cell: (c) => (
        <button onClick={() => toggleActive(c)} title="تغییر وضعیت">
          <StatusBadge status={c.isActive ? "فعال" : "غیرفعال"} />
        </button>
      ),
    },
    {
      header: "عملیات",
      full: true,
      cell: (c) => (
        <div className="flex items-center gap-2">
          <button
            onClick={() => openEdit(c)}
            className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-white/60 transition hover:border-[#3a64f2]/50 hover:text-[#6f93ff]"
          >
            <AdminIcon name="edit" className="h-4 w-4" />
          </button>
          <button
            onClick={() => remove(c)}
            className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-white/60 transition hover:border-rose-500/50 hover:text-rose-400"
          >
            <AdminIcon name="trash" className="h-4 w-4" />
          </button>
        </div>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="دسته‌بندی‌ها"
        desc={`${formatNumber(categories.length)} دسته‌بندی`}
        action={
          <button
            onClick={openNew}
            className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
          >
            <AdminIcon name="plus" className="h-4 w-4" />
            دسته‌بندی جدید
          </button>
        }
      />

      {loading ? (
        <div className="grid place-items-center py-24">
          <Spinner className="h-8 w-8" />
        </div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : (
        <Card className="overflow-hidden">
          <DataTable columns={columns} rows={categories} rowKey={(c) => c.id} minWidth={680} empty="دسته‌بندی‌ای یافت نشد" />
        </Card>
      )}

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title={editingId === null ? "دسته‌بندی جدید" : "ویرایش دسته‌بندی"}>
        <div className="grid gap-5">
          <div className="grid gap-5 sm:grid-cols-2">
            <label>
              <span className="mb-2 block text-sm text-white/70">نام دسته‌بندی</span>
              <input value={form.name} onChange={(e) => set("name", e.target.value)} className={inputCls} placeholder="مثلاً موسیقی" />
            </label>
            <label>
              <span className="mb-2 block text-sm text-white/70">نامک (slug)</span>
              <input value={form.slug} onChange={(e) => set("slug", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="music" />
            </label>
          </div>
          <label>
            <span className="mb-2 block text-sm text-white/70">توضیح کوتاه <span className="text-white/40">(زیر عنوان در صفحه دسته‌بندی‌ها)</span></span>
            <textarea
              value={form.description}
              onChange={(e) => set("description", e.target.value)}
              rows={2}
              className={`${inputCls} resize-none`}
              placeholder="مثلاً اشتراک‌های اپل موزیک، اسپاتیفای و پادکست‌های برتر"
            />
          </label>
          <ImageField label="آیکن / لوگوی دسته‌بندی" aspect="square" value={form.icon} onChange={(v) => set("icon", v)} className="w-40" />
          <label className="w-40">
            <span className="mb-2 block text-sm text-white/70">ترتیب نمایش</span>
            <input type="number" dir="ltr" value={form.sortOrder} onChange={(e) => set("sortOrder", Number(e.target.value))} className={`${inputCls} text-left`} />
          </label>
          <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
            <span className="text-sm text-white/80">دسته‌بندی فعال باشد</span>
            <Toggle checked={form.isActive} onChange={(v) => set("isActive", v)} />
          </label>
        </div>

        <div className="mt-6 flex gap-3">
          <button
            onClick={submit}
            disabled={saving || !form.name.trim()}
            className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-8 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50"
          >
            {saving ? <Spinner /> : "ذخیره"}
          </button>
          <button
            onClick={() => setModalOpen(false)}
            className="h-11 rounded-xl border border-white/10 px-8 text-sm font-bold text-white/80 transition hover:bg-white/5"
          >
            انصراف
          </button>
        </div>
      </Modal>
    </div>
  );
}
