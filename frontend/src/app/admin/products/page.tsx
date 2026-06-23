"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Product, ProductInput, Category, ProductFeature, ProductPlanInput } from "@/lib/types";
import { formatToman, formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, Toggle, StatusBadge, Modal, DataTable, Field, inputCls, type Column } from "@/components/admin/ui";
import ImageField from "@/components/admin/ImageField";
import AdminIcon from "@/components/admin/AdminIcon";

const emptyForm = (categoryId: number): ProductInput => ({
  name: "",
  categoryId,
  price: 0,
  discountPercent: 0,
  stock: 0,
  isActive: true,
  featured: false,
  image: "",
  sku: "",
  description: "",
  warning: "",
  requiredLevel: 1,
  deliveryTemplate: "",
  features: [
    { text: "تحویل آنی پس از پرداخت", included: true },
    { text: "پشتیبانی ۲۴ ساعته", included: true },
    { text: "گارانتی بازگشت وجه", included: true },
  ],
  plans: [],
});

const emptyPlan = (type: string): ProductPlanInput => ({ type, months: 1, price: 0, discountPercent: 0, isActive: true });

export default function AdminProductsPage() {
  const [products, setProducts] = useState<Product[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [planTypes, setPlanTypes] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<ProductInput>(emptyForm(0));
  const [saving, setSaving] = useState(false);

  async function load() {
    try {
      const [p, c, t] = await Promise.all([api.products.list(), api.categories.list(), api.planTypes.list()]);
      setProducts(p);
      setCategories(c);
      setPlanTypes(t);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری");
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => {
    load();
  }, []);

  const set = <K extends keyof ProductInput>(key: K, value: ProductInput[K]) => setForm((f) => ({ ...f, [key]: value }));

  function openNew() {
    setEditingId(null);
    setForm(emptyForm(categories[0]?.id ?? 0));
    setModalOpen(true);
  }
  function openEdit(p: Product) {
    setEditingId(p.id);
    setForm({
      name: p.name,
      categoryId: p.categoryId,
      price: p.price,
      discountPercent: p.discountPercent,
      stock: p.stock,
      isActive: p.isActive,
      featured: p.featured,
      image: p.image,
      sku: p.sku,
      description: p.description,
      warning: p.warning,
      requiredLevel: p.requiredLevel,
      deliveryTemplate: p.deliveryTemplate,
      features: p.features.map((f) => ({ ...f })),
      plans: p.plans.map((pl) => ({ type: pl.type, months: pl.months, price: pl.price, discountPercent: pl.discountPercent, isActive: pl.isActive })),
    });
    setModalOpen(true);
  }
  async function submit() {
    setSaving(true);
    try {
      if (editingId === null) {
        const created = await api.products.create(form);
        setProducts((p) => [...p, created]);
      } else {
        const updated = await api.products.update(editingId, form);
        setProducts((p) => p.map((x) => (x.id === editingId ? updated : x)));
      }
      setModalOpen(false);
    } finally {
      setSaving(false);
    }
  }
  async function remove(p: Product) {
    if (!confirm(`محصول «${p.name}» حذف شود؟`)) return;
    await api.products.remove(p.id);
    setProducts((prev) => prev.filter((x) => x.id !== p.id));
  }

  // feature editor helpers
  const setFeat = (i: number, key: keyof ProductFeature, value: string | boolean) =>
    setForm((f) => ({ ...f, features: f.features.map((ft, idx) => (idx === i ? { ...ft, [key]: value } : ft)) }));
  const addFeat = () => setForm((f) => ({ ...f, features: [...f.features, { text: "", included: true }] }));
  const removeFeat = (i: number) => setForm((f) => ({ ...f, features: f.features.filter((_, idx) => idx !== i) }));

  // plan editor helpers
  const setPlan = <K extends keyof ProductPlanInput>(i: number, key: K, value: ProductPlanInput[K]) =>
    setForm((f) => ({ ...f, plans: f.plans.map((pl, idx) => (idx === i ? { ...pl, [key]: value } : pl)) }));
  const addPlan = () => setForm((f) => ({ ...f, plans: [...f.plans, emptyPlan(planTypes[0] ?? "")] }));
  const removePlan = (i: number) => setForm((f) => ({ ...f, plans: f.plans.filter((_, idx) => idx !== i) }));

  const columns: Column<Product>[] = [
    {
      header: "محصول",
      primary: true,
      cell: (p) => (
        <div className="flex items-center gap-3">
          {p.image ? <img src={p.image} alt={p.name} className="h-11 w-11 rounded-lg object-cover" /> : <span className="grid h-11 w-11 place-items-center rounded-lg bg-white/5"><AdminIcon name="box" className="h-5 w-5 text-white/30" /></span>}
          <div>
            <p className="font-medium">{p.name}</p>
            <p className="font-mono text-xs text-white/40">{p.sku || "—"}</p>
          </div>
        </div>
      ),
    },
    { header: "دسته", td: "text-white/65", cell: (p) => p.categoryName },
    { header: "قیمت", cell: (p) => formatToman(p.finalPrice) },
    { header: "موجودی", td: "text-white/70", cell: (p) => formatNumber(p.stock) },
    { header: "ویژگی‌ها", td: "text-white/60", cell: (p) => `${formatNumber(p.features.filter((f) => f.included).length)} مورد` },
    { header: "وضعیت", cell: (p) => <StatusBadge status={p.isActive ? "فعال" : "ناموجود"} /> },
    {
      header: "عملیات",
      full: true,
      cell: (p) => (
        <div className="flex items-center gap-2">
          <button onClick={() => openEdit(p)} className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-white/60 transition hover:border-[#3a64f2]/50 hover:text-[#6f93ff]">
            <AdminIcon name="edit" className="h-4 w-4" />
          </button>
          <button onClick={() => remove(p)} className="grid h-8 w-8 place-items-center rounded-lg border border-white/10 text-white/60 transition hover:border-rose-500/50 hover:text-rose-400">
            <AdminIcon name="trash" className="h-4 w-4" />
          </button>
        </div>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="محصولات"
        desc={`${formatNumber(products.length)} محصول`}
        action={
          <button onClick={openNew} className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110">
            <AdminIcon name="plus" className="h-4 w-4" />
            افزودن محصول
          </button>
        }
      />

      {loading ? (
        <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : (
        <Card className="overflow-hidden">
          <DataTable columns={columns} rows={products} rowKey={(p) => p.id} minWidth={820} empty="محصولی یافت نشد" />
        </Card>
      )}

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title={editingId === null ? "افزودن محصول" : "ویرایش محصول"} size="3xl">
        {/* the shared Modal now caps height and scrolls its own body, so no inner scroll container is needed. */}
        <div className="grid gap-5">
          <ImageField label="تصویر محصول" aspect="wide" value={form.image} onChange={(v) => set("image", v)} className="w-48" />

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="نام محصول">
              <input value={form.name} onChange={(e) => set("name", e.target.value)} className={inputCls} />
            </Field>
            <Field label="کد (SKU)">
              <input value={form.sku} onChange={(e) => set("sku", e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
            </Field>
            <Field label="دسته‌بندی">
              <select value={form.categoryId} onChange={(e) => set("categoryId", Number(e.target.value))} className={inputCls}>
                {categories.map((c) => <option key={c.id} value={c.id} className="bg-[#15151f]">{c.name}</option>)}
              </select>
            </Field>
            <Field label="موجودی">
              <input type="number" dir="ltr" value={form.stock} onChange={(e) => set("stock", Number(e.target.value))} className={`${inputCls} text-left`} />
            </Field>
            <Field label="قیمت (تومان)">
              <input type="number" dir="ltr" value={form.price} onChange={(e) => set("price", Number(e.target.value))} className={`${inputCls} text-left`} />
            </Field>
            <Field label="تخفیف (٪)">
              <input type="number" dir="ltr" min={0} max={100} value={form.discountPercent} onChange={(e) => set("discountPercent", Math.min(100, Math.max(0, Number(e.target.value))))} className={`${inputCls} text-left`} />
            </Field>
          </div>

          <Field label="توضیحات">
            <textarea rows={3} value={form.description} onChange={(e) => set("description", e.target.value)} className={`${inputCls} h-auto py-3`} />
          </Field>

          <Field label="مطالعه اجباری / هشدار">
            <textarea rows={2} value={form.warning} onChange={(e) => set("warning", e.target.value)} placeholder="مثلاً: از تغییر رمز اکانت خودداری کنید." className={`${inputCls} h-auto py-3`} />
          </Field>

          <Field label="سطح احراز هویت موردنیاز">
            <select value={form.requiredLevel} onChange={(e) => set("requiredLevel", Number(e.target.value))} className={`${inputCls} h-12`}>
              <option value={1} className="bg-[#15151f]">سطح ۱ — کارت بانکی</option>
              <option value={2} className="bg-[#15151f]">سطح ۲ — کارت ملی</option>
            </select>
            <p className="mt-1.5 text-xs text-white/45">برای خرید این محصول، کاربر باید حداقل این سطح احراز هویت را داشته باشد. (به کاربر نمایش داده نمی‌شود)</p>
          </Field>

          <Field label="قالب پیش‌فرض تحویل (اختیاری)">
            <textarea rows={3} value={form.deliveryTemplate} onChange={(e) => set("deliveryTemplate", e.target.value)} placeholder="متن آماده‌ای که هنگام تحویل این محصول در فرم تحویل پیش‌نویس می‌شود. مثلاً: نام کاربری: ___ / رمز: ___" className={`${inputCls} h-auto py-3 font-mono`} />
            <p className="mt-1.5 text-xs text-white/45">هنگام تحویل سفارش‌های این محصول، این متن به‌صورت خودکار در کادر «محتوای تحویل» قرار می‌گیرد و قابل ویرایش است.</p>
          </Field>

          <div className="grid grid-cols-2 gap-3">
            <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
              <span className="text-sm text-white/80">فعال</span>
              <Toggle checked={form.isActive} onChange={(v) => set("isActive", v)} />
            </label>
            <label className="flex cursor-pointer items-center justify-between rounded-xl bg-white/[0.03] px-4 py-3">
              <span className="text-sm text-white/80">محصول ویژه</span>
              <Toggle checked={form.featured} onChange={(v) => set("featured", v)} />
            </label>
          </div>

          <div className="rounded-xl border border-white/8 p-4">
            <div className="mb-3 flex items-center justify-between">
              <h4 className="text-sm font-bold text-white">ویژگی‌های محصول</h4>
              <button onClick={addFeat} className="flex items-center gap-1.5 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/80 transition hover:bg-white/5">
                <AdminIcon name="plus" className="h-3.5 w-3.5" /> افزودن ویژگی
              </button>
            </div>
            <div className="space-y-2">
              {form.features.map((ft, i) => (
                <div key={i} className="flex items-center gap-2">
                  <button
                    onClick={() => setFeat(i, "included", !ft.included)}
                    title={ft.included ? "دارد" : "ندارد"}
                    className={`grid h-9 w-9 shrink-0 place-items-center rounded-lg border transition ${ft.included ? "border-emerald-500/40 bg-emerald-500/15 text-emerald-400" : "border-white/10 text-white/40"}`}
                  >
                    <AdminIcon name={ft.included ? "check" : "close"} className="h-4 w-4" />
                  </button>
                  <input value={ft.text} onChange={(e) => setFeat(i, "text", e.target.value)} className="h-9 flex-1 rounded-lg border border-white/10 bg-[#0d0d15] px-3 text-sm text-white outline-none focus:border-[#3a64f2]" placeholder="مثلاً کیفیت 4K Ultra HD" />
                  <button onClick={() => removeFeat(i)} className="grid h-9 w-9 shrink-0 place-items-center rounded-lg border border-white/10 text-white/50 transition hover:border-rose-500/50 hover:text-rose-400">
                    <AdminIcon name="trash" className="h-4 w-4" />
                  </button>
                </div>
              ))}
              {form.features.length === 0 && <p className="text-xs text-white/40">ویژگی‌ای اضافه نشده است</p>}
            </div>
          </div>

          <div className="rounded-xl border border-white/8 p-4">
            <div className="mb-1 flex items-center justify-between">
              <h4 className="text-sm font-bold text-white">پلن‌های قیمت‌گذاری</h4>
              <button onClick={addPlan} className="flex items-center gap-1.5 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/80 transition hover:bg-white/5">
                <AdminIcon name="plus" className="h-3.5 w-3.5" /> افزودن پلن
              </button>
            </div>
            <p className="mb-3 text-xs text-white/40">نوع سرویس و مدت اشتراک را تعیین کنید؛ کاربر هنگام خرید یکی را انتخاب می‌کند.</p>
            <div className="space-y-2">
              {form.plans.map((pl, i) => (
                <div key={i} className="rounded-xl border border-white/8 bg-white/[0.02] p-3 sm:p-4">
                  <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                    <Field label="نوع سرویس">
                      <select value={pl.type} onChange={(e) => setPlan(i, "type", e.target.value)} className={inputCls}>
                        {planTypes.map((t) => <option key={t} value={t} className="bg-[#15151f]">{t}</option>)}
                        {pl.type && !planTypes.includes(pl.type) && <option value={pl.type} className="bg-[#15151f]">{pl.type}</option>}
                      </select>
                    </Field>
                    <Field label="مدت (ماه)">
                      <input type="number" dir="ltr" min={1} value={pl.months} onChange={(e) => setPlan(i, "months", Math.max(1, Number(e.target.value)))} className={`${inputCls} text-left`} />
                    </Field>
                    <Field label="قیمت (تومان)">
                      <input type="number" dir="ltr" value={pl.price} onChange={(e) => setPlan(i, "price", Number(e.target.value))} className={`${inputCls} text-left`} />
                    </Field>
                    <Field label="تخفیف (٪)">
                      <input type="number" dir="ltr" min={0} max={100} value={pl.discountPercent} onChange={(e) => setPlan(i, "discountPercent", Math.min(100, Math.max(0, Number(e.target.value))))} className={`${inputCls} text-left`} />
                    </Field>
                  </div>
                  <div className="mt-3 flex items-center justify-between border-t border-white/8 pt-3">
                    <label className="flex cursor-pointer items-center gap-2 text-sm text-white/75">
                      <Toggle checked={pl.isActive} onChange={(v) => setPlan(i, "isActive", v)} />
                      فعال
                    </label>
                    <button onClick={() => removePlan(i)} className="flex items-center gap-1.5 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/60 transition hover:border-rose-500/50 hover:text-rose-400">
                      <AdminIcon name="trash" className="h-3.5 w-3.5" /> حذف پلن
                    </button>
                  </div>
                </div>
              ))}
              {form.plans.length === 0 && <p className="text-xs text-white/40">پلنی تعریف نشده است؛ در این حالت قیمت پایه محصول اعمال می‌شود.</p>}
            </div>
          </div>

          <div className="flex gap-3">
            <button onClick={submit} disabled={saving || !form.name.trim()} className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-8 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50">
              {saving ? <Spinner /> : "ذخیره محصول"}
            </button>
            <button onClick={() => setModalOpen(false)} className="h-11 rounded-xl border border-white/10 px-8 text-sm font-bold text-white/80 transition hover:bg-white/5">انصراف</button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
