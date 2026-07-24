"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Product, ProductInput, Category, ProductFeature, ProductFaq, ProductPlanInput, PlanInputField, PlanFieldType } from "@/lib/types";
import { formatToman, formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, Toggle, StatusBadge, Modal, DataTable, Field, inputCls, type Column } from "@/components/admin/ui";
import ImageField from "@/components/admin/ImageField";
import AdminIcon from "@/components/admin/AdminIcon";
import MarkdownEditor from "@/components/admin/MarkdownEditor";

const emptyForm = (categoryId: number): ProductInput => ({
  name: "",
  categoryId,
  price: 0,
  discountPercent: 0,
  stock: 0,
  isActive: true,
  featured: false,
  image: "",
  logo: "",
  listImage: "",
  gallery: [],
  sku: "",
  description: "",
  warning: "",
  requiredLevel: 1,
  v2rayCategoryId: 0,
  deliveryTemplate: "",
  priceUsd: 0,
  features: [
    { text: "تحویل آنی پس از پرداخت", included: true },
    { text: "پشتیبانی ۲۴ ساعته", included: true },
    { text: "گارانتی بازگشت وجه", included: true },
  ],
  faq: [],
  plans: [],
});

const emptyPlanInfo = { collectsInfo: false, collectSeatInfo: false, seatInfoHint: "", seatInfoEditLimit: 0, inputFields: [], warningText: "", tutorialText: "", tutorialMedia: [], allowNotes: false } as const;
const emptyPlan = (type: string): ProductPlanInput => ({ type, months: 1, price: 0, priceUsd: 0, discountPercent: 0, isActive: true, userCount: 0, rules: "", ...emptyPlanInfo, inputFields: [], tutorialMedia: [] });

// Parse an uploaded product-content .md file into { description, faq } so the admin
// doesn't retype anything. Convention (matches the files in seo-content/): the description
// block and the FAQ block are separated by a horizontal rule (`---`), FAQ items are a bold
// question line (**...**) followed by the answer text. Falls back to a «سوالات متداول/FAQ»
// heading when no rule is present.
function parseProductMd(text: string): { description: string; faq: ProductFaq[] } {
  const raw = text.replace(/\r\n/g, "\n");
  let descBlock = raw;
  let faqBlock = "";
  const byRule = raw.split(/\n-{3,}\n/);
  if (byRule.length > 1) {
    descBlock = byRule[0];
    faqBlock = byRule.slice(1).join("\n");
  } else {
    const m = raw.match(/\n#{1,6}[^\n]*(FAQ|سوالات متداول)[^\n]*\n/i);
    if (m && m.index != null) {
      descBlock = raw.slice(0, m.index);
      faqBlock = raw.slice(m.index);
    }
  }
  // Drop wrapper/instruction headings from the description so only the real content remains.
  const description = descBlock
    .split("\n")
    .filter((l) => !/^#{1,6}\s*(محتوای|بخش)/.test(l.trim()))
    .join("\n")
    .replace(/^\n+/, "")
    .replace(/\n+$/, "")
    .trim();
  // Each **bold** line is a question; the text until the next **bold** line is its answer.
  const faq: ProductFaq[] = [];
  const re = /\*\*(.+?)\*\*[^\n]*\n([\s\S]*?)(?=\n\s*\*\*|$)/g;
  let mm: RegExpExecArray | null;
  while ((mm = re.exec(faqBlock)) !== null) {
    const question = mm[1].replace(/^[\s]*[۰-۹0-9]+\s*[)．.،\-]\s*/, "").trim();
    const answer = mm[2].trim();
    if (question && answer) faq.push({ question, answer });
  }
  return { description, faq };
}

export default function AdminProductsPage() {
  const [products, setProducts] = useState<Product[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [planTypes, setPlanTypes] = useState<string[]>([]);
  const [rate, setRate] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<ProductInput>(emptyForm(0));
  const [saving, setSaving] = useState(false);

  async function load() {
    try {
      const [p, c, t, u] = await Promise.all([
        api.products.list(),
        api.categories.list(),
        api.planTypes.list(),
        api.pricing.usdRate().catch(() => null),
      ]);
      setProducts(p);
      setCategories(c);
      setPlanTypes(t);
      setRate(u?.tomanPerUsd ?? 0);
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

  // The V2Ray catalogue is owner-only; a non-owner simply gets nothing and the selector stays hidden.
  useEffect(() => {
    api.v2ray.categories
      .list()
      .then((list) => setV2rayCats(list.map((c) => ({ id: c.id, name: c.name }))))
      .catch(() => setV2rayCats([]));
  }, []);

  function openNew() {
    setEditingId(null);
    setImportMsg("");
    setForm(emptyForm(categories[0]?.id ?? 0));
    setModalOpen(true);
  }
  function openEdit(p: Product) {
    setEditingId(p.id);
    setImportMsg("");
    setForm({
      name: p.name,
      categoryId: p.categoryId,
      price: p.price,
      discountPercent: p.discountPercent,
      stock: p.stock,
      isActive: p.isActive,
      featured: p.featured,
      v2rayCategoryId: p.v2rayCategoryId,
      image: p.image,
      logo: p.logo,
      listImage: p.listImage ?? "",
      gallery: p.gallery ?? [],
      sku: p.sku,
      description: p.description,
      warning: p.warning,
      requiredLevel: p.requiredLevel,
      deliveryTemplate: p.deliveryTemplate,
      priceUsd: p.priceUsd ?? 0,
      features: p.features.map((f) => ({ ...f })),
      faq: (p.faq ?? []).map((f) => ({ ...f })),
      plans: p.plans.map((pl) => ({
        type: pl.type, months: pl.months, price: pl.price, priceUsd: pl.priceUsd ?? 0, discountPercent: pl.discountPercent, isActive: pl.isActive, userCount: pl.userCount ?? 0, rules: pl.rules ?? "",
        collectsInfo: pl.collectsInfo ?? false,
        collectSeatInfo: pl.collectSeatInfo ?? false,
        seatInfoHint: pl.seatInfoHint ?? "",
        seatInfoEditLimit: pl.seatInfoEditLimit ?? 0,
        inputFields: (pl.inputFields ?? []).map((fld) => ({ ...fld })),
        warningText: pl.warningText ?? "",
        tutorialText: pl.tutorialText ?? "",
        tutorialMedia: (pl.tutorialMedia ?? []).map((m) => ({ ...m })),
        allowNotes: pl.allowNotes ?? false,
      })),
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

  // FAQ editor helpers
  const setFaq = (i: number, key: "question" | "answer", value: string) =>
    setForm((f) => ({ ...f, faq: f.faq.map((q, idx) => (idx === i ? { ...q, [key]: value } : q)) }));
  const addFaq = () => setForm((f) => ({ ...f, faq: [...f.faq, { question: "", answer: "" }] }));
  const removeFaq = (i: number) => setForm((f) => ({ ...f, faq: f.faq.filter((_, idx) => idx !== i) }));

  // Import a product-content .md file: auto-fills the description + FAQ fields (no manual entry).
  const [v2rayCats, setV2rayCats] = useState<{ id: number; name: string }[]>([]);
  const [importMsg, setImportMsg] = useState("");
  function importMd(file: File) {
    const reader = new FileReader();
    reader.onload = () => {
      const { description, faq } = parseProductMd(String(reader.result ?? ""));
      if (!description && faq.length === 0) {
        setImportMsg("⚠ محتوایی از فایل خوانده نشد. قالب فایل را بررسی کنید.");
        return;
      }
      setForm((f) => ({
        ...f,
        description: description || f.description,
        faq: faq.length > 0 ? faq : f.faq,
      }));
      const parts = [description ? "توضیحات" : "", faq.length ? `${faq.length} سوال متداول` : ""].filter(Boolean);
      setImportMsg(`✓ ${parts.join(" و ")} از فایل بارگذاری شد. بررسی و سپس ذخیره کنید.`);
    };
    reader.onerror = () => setImportMsg("⚠ خواندن فایل ناموفق بود.");
    reader.readAsText(file, "utf-8");
  }

  // plan editor helpers
  const setPlan = <K extends keyof ProductPlanInput>(i: number, key: K, value: ProductPlanInput[K]) =>
    setForm((f) => ({ ...f, plans: f.plans.map((pl, idx) => (idx === i ? { ...pl, [key]: value } : pl)) }));
  const addPlan = () => setForm((f) => ({ ...f, plans: [...f.plans, emptyPlan(planTypes[0] ?? "")] }));
  const removePlan = (i: number) => setForm((f) => ({ ...f, plans: f.plans.filter((_, idx) => idx !== i) }));

  // per-plan customer-input field helpers
  const mapPlan = (i: number, fn: (pl: ProductPlanInput) => ProductPlanInput) =>
    setForm((f) => ({ ...f, plans: f.plans.map((pl, idx) => (idx === i ? fn(pl) : pl)) }));
  const setPlanField = (pi: number, fi: number, key: keyof PlanInputField, value: string | boolean) =>
    mapPlan(pi, (pl) => ({ ...pl, inputFields: pl.inputFields.map((fld, j) => (j === fi ? { ...fld, [key]: value } : fld)) }));
  const addPlanField = (pi: number) =>
    mapPlan(pi, (pl) => ({ ...pl, inputFields: [...pl.inputFields, { label: "", type: "text" as PlanFieldType, required: true, sensitive: false }] }));
  const removePlanField = (pi: number, fi: number) =>
    mapPlan(pi, (pl) => ({ ...pl, inputFields: pl.inputFields.filter((_, j) => j !== fi) }));

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

  // Search over the product list. It matches the product's own identity (name, SKU, category) AND its plans,
  // so «۶ ماهه» or «اشتراکی» finds every product that sells such a plan — that's how an admin actually looks
  // for something here. Digits are normalized so a Persian-typed «۶» matches a plan stored as 6.
  const norm = (s: string) => s.replace(/[۰-۹]/g, (d) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(d))).toLowerCase().trim();
  const [query, setQuery] = useState("");
  const q = norm(query);
  const visible = q
    ? products.filter((p) =>
        [p.name, p.sku, p.categoryName, ...p.plans.map((pl) => `${pl.type} ${pl.months} ماهه ${pl.userCount || ""}`)]
          .some((field) => norm(field ?? "").includes(q)))
    : products;

  return (
    <div>
      <PageHeader
        title="محصولات"
        desc={q ? `${formatNumber(visible.length)} از ${formatNumber(products.length)} محصول` : `${formatNumber(products.length)} محصول`}
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
          {/* search bar — scoped to this table only; it filters the rows below and nothing else on the panel */}
          <div className="border-b border-white/8 p-3">
            <div className="relative">
              <AdminIcon name="search" className="pointer-events-none absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 text-white/30" />
              <input
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="جست‌وجو در محصولات و پلن‌ها (نام، SKU، دسته، نوع پلن، مدت)"
                className={`${inputCls} pr-10`}
              />
              {query && (
                <button
                  onClick={() => setQuery("")}
                  aria-label="پاک کردن جست‌وجو"
                  className="absolute left-3 top-1/2 -translate-y-1/2 text-xs font-bold text-white/40 transition hover:text-white/80"
                >
                  ✕
                </button>
              )}
            </div>
          </div>
          <DataTable columns={columns} rows={visible} rowKey={(p) => p.id} minWidth={820} empty="محصولی یافت نشد" />
        </Card>
      )}

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title={editingId === null ? "افزودن محصول" : "ویرایش محصول"} size="3xl">
        {/* the shared Modal now caps height and scrolls its own body, so no inner scroll container is needed. */}
        <div className="grid gap-5">
          <div className="flex flex-wrap gap-5">
            <ImageField label="تصویر محصول" aspect="wide" value={form.image} onChange={(v) => set("image", v)} className="w-48" />
            <ImageField label="لوگو سرویس" aspect="square" value={form.logo} onChange={(v) => set("logo", v)} className="w-24" />
            <ImageField label="تصویر کارت لیست (افقی ۲:۱)" aspect="wide" value={form.listImage} onChange={(v) => set("listImage", v)} className="w-48" />
          </div>
          <p className="-mt-2 text-xs text-white/45">«تصویر کارت لیست» مخصوص نمایش در فهرست محصولات است و می‌تواند با تصویر صفحهٔ محصول متفاوت باشد؛ بهترین ابعاد ۸۰۰×۴۰۰ (افقی ۲:۱). خالی بماند، از لوگو یا تصویر محصول استفاده می‌شود.</p>

          <Field label="گالری تصاویر">
            <div className="flex flex-wrap gap-3">
              {form.gallery.map((img, i) => (
                <div key={i} className="group relative">
                  <ImageField aspect="square" value={img} onChange={(v) => { const g = [...form.gallery]; if (v) g[i] = v; else g.splice(i, 1); set("gallery", g); }} className="w-20" />
                </div>
              ))}
              <ImageField aspect="square" value="" onChange={(v) => { if (v) set("gallery", [...form.gallery, v]); }} className="w-20" />
            </div>
          </Field>

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
            <Field label="قیمت دلاری ($) — اختیاری">
              <input type="number" dir="ltr" min={0} step={0.01} value={form.priceUsd || ""} onChange={(e) => set("priceUsd", Math.max(0, Number(e.target.value)))} placeholder="مثلاً 4.99" className={`${inputCls} text-left`} />
              <p className="mt-1.5 text-xs text-white/45">اگر پر شود، قیمت تومانی خودکار از نرخ زندهٔ دلار (نوبیتکس) محاسبه و لحظه‌ای به‌روزرسانی می‌شود و فیلد «قیمت (تومان)» نادیده گرفته می‌شود.</p>
            </Field>
          </div>

          <Field label="توضیحات">
            <MarkdownEditor value={form.description} onChange={(v) => set("description", v)} />
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

          {v2rayCats.length > 0 && (
            <Field label="اتصال به پلن‌های V2Ray (اختیاری)">
              <select
                value={form.v2rayCategoryId}
                onChange={(e) => set("v2rayCategoryId", Number(e.target.value))}
                className={`${inputCls} h-12`}
              >
                <option value={0} className="bg-[#15151f]">— محصول عادی (پلن‌های خودش) —</option>
                {v2rayCats.map((c) => (
                  <option key={c.id} value={c.id} className="bg-[#15151f]">{c.name}</option>
                ))}
              </select>
              <p className="mt-1.5 text-xs text-white/45">
                اگر یک دسته‌بندی V2Ray انتخاب کنید، این محصول لوگو و توضیحات و بقیه‌ی نمایش را از همین‌جا می‌گیرد،
                ولی پلن‌های قابل‌انتخابش از پلن‌های همان دسته خوانده می‌شود. هر پلنی که بعداً به آن دسته اضافه کنید،
                خودکار اینجا هم می‌آید.
              </p>
            </Field>
          )}

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
              <h4 className="text-sm font-bold text-white">سوالات متداول (FAQ)</h4>
              <button onClick={addFaq} className="flex items-center gap-1.5 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/80 transition hover:bg-white/5">
                <AdminIcon name="plus" className="h-3.5 w-3.5" /> افزودن سوال
              </button>
            </div>
            <p className="mb-3 text-xs text-white/40">هر سوال/پاسخ به‌صورت آکاردئون در صفحه محصول نمایش داده شده و به‌شکل FAQPage schema برای گوگل و موتورهای پاسخ‌گوی هوش مصنوعی ارسال می‌شود.</p>
            <div className="space-y-3">
              {form.faq.map((q, i) => (
                <div key={i} className="rounded-lg border border-white/10 p-3">
                  <div className="mb-2 flex items-center gap-2">
                    <input value={q.question} onChange={(e) => setFaq(i, "question", e.target.value)} className="h-9 flex-1 rounded-lg border border-white/10 bg-[#0d0d15] px-3 text-sm font-bold text-white outline-none focus:border-[#3a64f2]" placeholder="سوال، مثلاً: آیا اکانت قابل تمدید است؟" />
                    <button onClick={() => removeFaq(i)} className="grid h-9 w-9 shrink-0 place-items-center rounded-lg border border-white/10 text-white/50 transition hover:border-rose-500/50 hover:text-rose-400">
                      <AdminIcon name="trash" className="h-4 w-4" />
                    </button>
                  </div>
                  <textarea value={q.answer} onChange={(e) => setFaq(i, "answer", e.target.value)} rows={2} className="w-full resize-none rounded-lg border border-white/10 bg-[#0d0d15] px-3 py-2 text-sm text-white outline-none focus:border-[#3a64f2]" placeholder="پاسخ کامل و مفید به این سوال…" />
                </div>
              ))}
              {form.faq.length === 0 && <p className="text-xs text-white/40">سوالی اضافه نشده است</p>}
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
                  {(() => {
                    const usdPriced = (pl.priceUsd ?? 0) > 0;
                    const planToman = usdPriced && rate > 0 ? Math.round(pl.priceUsd * rate) : pl.price;
                    return (
                  <div className="grid grid-cols-2 gap-3 sm:grid-cols-6">
                    <Field label="نوع سرویس">
                      <select value={pl.type} onChange={(e) => setPlan(i, "type", e.target.value)} className={inputCls}>
                        {planTypes.map((t) => <option key={t} value={t} className="bg-[#15151f]">{t}</option>)}
                        {pl.type && !planTypes.includes(pl.type) && <option value={pl.type} className="bg-[#15151f]">{pl.type}</option>}
                      </select>
                    </Field>
                    <Field label="مدت (ماه)">
                      <input type="number" dir="ltr" min={1} value={pl.months} onChange={(e) => setPlan(i, "months", Math.max(1, Number(e.target.value)))} className={`${inputCls} text-left`} />
                    </Field>
                    <Field label="قیمت دلاری ($)">
                      <input type="number" dir="ltr" min={0} step={0.01} value={pl.priceUsd || ""} placeholder="—" onChange={(e) => setPlan(i, "priceUsd", Math.max(0, Number(e.target.value)))} className={`${inputCls} text-left`} />
                    </Field>
                    <Field label="قیمت (تومان)">
                      <input type="number" dir="ltr" value={planToman} disabled={usdPriced} onChange={(e) => setPlan(i, "price", Number(e.target.value))} className={`${inputCls} text-left ${usdPriced ? "opacity-50" : ""}`} />
                    </Field>
                    <Field label="تخفیف (٪)">
                      <input type="number" dir="ltr" min={0} max={100} value={pl.discountPercent} onChange={(e) => setPlan(i, "discountPercent", Math.min(100, Math.max(0, Number(e.target.value))))} className={`${inputCls} text-left`} />
                    </Field>
                    <Field label="تعداد کاربر">
                      <input type="number" dir="ltr" min={0} value={pl.userCount || ""} placeholder="—" onChange={(e) => setPlan(i, "userCount", Math.max(0, Number(e.target.value)))} className={`${inputCls} text-left`} />
                    </Field>
                  </div>
                    );
                  })()}
                  <div className="mt-3 flex items-center justify-between border-t border-white/8 pt-3">
                    <label className="flex cursor-pointer items-center gap-2 text-sm text-white/75">
                      <Toggle checked={pl.isActive} onChange={(v) => setPlan(i, "isActive", v)} />
                      فعال
                    </label>
                    <button onClick={() => removePlan(i)} className="flex items-center gap-1.5 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/60 transition hover:border-rose-500/50 hover:text-rose-400">
                      <AdminIcon name="trash" className="h-3.5 w-3.5" /> حذف پلن
                    </button>
                  </div>

                  {/* per-plan: rules the buyer must read & accept at checkout */}
                  <div className="mt-3 border-t border-dashed border-white/10 pt-3">
                    <label className="text-sm font-bold text-white">قوانین این پلن</label>
                    <p className="mt-0.5 text-[11px] text-white/45">اگر پر شود، مشتری هنگام خرید باید آن را بخواند و تأیید کند. (خالی = بدون مرحله‌ی تأیید)</p>
                    <textarea
                      value={pl.rules}
                      onChange={(e) => setPlan(i, "rules", e.target.value)}
                      rows={3}
                      placeholder="مثلاً: تغییر رمز، خروج از سایر دستگاه‌ها یا اشتراک‌گذاری اکانت ممنوع است…"
                      className={`${inputCls} mt-2 resize-none`}
                    />
                  </div>

                  {/* per-plan: information collected from the customer at checkout */}
                  <div className="mt-3 border-t border-dashed border-white/10 pt-3">
                    <label className="flex cursor-pointer items-center justify-between gap-2">
                      <span>
                        <span className="text-sm font-bold text-white">اطلاعات موردنیاز از مشتری</span>
                        <span className="mt-0.5 block text-[11px] text-white/45">اگر روشن باشد، هنگام خرید این پلن از مشتری پرسیده می‌شود</span>
                      </span>
                      <Toggle checked={pl.collectsInfo} onChange={(v) => setPlan(i, "collectsInfo", v)} />
                    </label>

                    {pl.collectsInfo && (
                      <div className="mt-3 space-y-3">
                        <div className="space-y-2">
                          {pl.inputFields.map((fld, fi) => (
                            <div key={fi} className="rounded-lg border border-white/8 bg-white/[0.02] p-2.5">
                              <div className="flex flex-wrap items-center gap-2">
                                <input
                                  value={fld.label}
                                  onChange={(e) => setPlanField(i, fi, "label", e.target.value)}
                                  placeholder="عنوان فیلد (مثلاً ایمیل اکانت)"
                                  className={`${inputCls} min-w-0 flex-1`}
                                />
                                <select
                                  value={fld.type}
                                  onChange={(e) => setPlanField(i, fi, "type", e.target.value)}
                                  className={`${inputCls} w-full sm:w-28`}
                                >
                                  <option value="text" className="bg-[#15151f]">متن</option>
                                  <option value="email" className="bg-[#15151f]">ایمیل</option>
                                  <option value="password" className="bg-[#15151f]">رمز</option>
                                  <option value="phone" className="bg-[#15151f]">تلفن</option>
                                  <option value="textarea" className="bg-[#15151f]">متن بلند</option>
                                </select>
                              </div>
                              <div className="mt-2 flex flex-wrap items-center gap-4">
                                <label className="flex cursor-pointer items-center gap-1.5 text-xs text-white/70">
                                  <Toggle checked={fld.required} onChange={(v) => setPlanField(i, fi, "required", v)} /> اجباری
                                </label>
                                <label className="flex cursor-pointer items-center gap-1.5 text-xs text-white/70">
                                  <Toggle checked={fld.sensitive || fld.type === "password"} onChange={(v) => setPlanField(i, fi, "sensitive", v)} /> حساس (رمزنگاری)
                                </label>
                                <button onClick={() => removePlanField(i, fi)} className="mr-auto text-xs font-bold text-white/45 transition hover:text-rose-400">حذف فیلد</button>
                              </div>
                            </div>
                          ))}
                          <button onClick={() => addPlanField(i)} className="w-full rounded-lg border border-dashed border-white/15 py-2 text-xs font-bold text-white/65 transition hover:border-white/30 hover:text-white">+ افزودن فیلد</button>
                        </div>

                        <Field label="متن هشدار (همیشه بالای فرم دیده می‌شود)">
                          <textarea value={pl.warningText} onChange={(e) => setPlan(i, "warningText", e.target.value)} rows={2} placeholder="مثلاً: کد دومرحله‌ای اکانت را خاموش کنید." className={`${inputCls} resize-none`} />
                        </Field>
                        <Field label="متن آموزش (داخل منوی کشویی)">
                          <textarea value={pl.tutorialText} onChange={(e) => setPlan(i, "tutorialText", e.target.value)} rows={3} placeholder="مرحله‌به‌مرحله توضیح دهید مشتری چه کند…" className={`${inputCls} resize-none`} />
                        </Field>

                        <label className="flex cursor-pointer items-center justify-between gap-2">
                          <span className="text-sm text-white/75">کادر توضیحات اختیاری برای مشتری</span>
                          <Toggle checked={pl.allowNotes} onChange={(v) => setPlan(i, "allowNotes", v)} />
                        </label>
                      </div>
                    )}
                  </div>

                  {/* per-plan: information collected from the buyer AFTER delivery, once per seat. Separate from
                      the checkout block above — that one asks before payment, this one asks in the panel. */}
                  <div className="mt-3 border-t border-dashed border-white/10 pt-3">
                    <label className="flex cursor-pointer items-center justify-between gap-2">
                      <span>
                        <span className="text-sm font-bold text-white">دریافت اطلاعات هر پروفایل پس از تحویل</span>
                        <span className="mt-0.5 block text-[11px] text-white/45">
                          اگر روشن باشد، خریدارِ این پلن برای هر پروفایل (کاربر) جداگانه یک تصویر و توضیح در پنل خود
                          ارسال می‌کند و در صفحه‌ی «اطلاعات کاربران اکانت‌ها» برای بررسی می‌آید
                        </span>
                      </span>
                      <Toggle checked={pl.collectSeatInfo} onChange={(v) => setPlan(i, "collectSeatInfo", v)} />
                    </label>

                    {pl.collectSeatInfo && (
                      <div className="mt-3 space-y-3">
                        <Field label="متن راهنما (داخل کادر پنل کاربر)">
                          <textarea
                            value={pl.seatInfoHint}
                            onChange={(e) => setPlan(i, "seatInfoHint", e.target.value)}
                            rows={2}
                            placeholder="مثلاً: یک اسکرین‌شات از صفحه‌ی تنظیمات اکانت و ایمیل خود را بفرستید."
                            className={`${inputCls} resize-none`}
                          />
                        </Field>
                        <Field label="تعداد ویرایش مجاز پس از تأیید">
                          <input
                            value={pl.seatInfoEditLimit}
                            onChange={(e) => setPlan(i, "seatInfoEditLimit", Math.max(0, Number(e.target.value) || 0))}
                            type="number"
                            min={0}
                            max={20}
                            dir="ltr"
                            className={inputCls}
                          />
                          <p className="mt-1 text-[11px] text-white/45">
                            ۰ یعنی پس از تأیید ادمین قفل می‌شود. عدد بزرگ‌تر یعنی کاربر همان تعداد بار می‌تواند
                            اطلاعات را عوض کند؛ هر تغییر دوباره به صف بررسی برمی‌گردد. (پیش از اولین تأیید، ویرایش
                            همیشه آزاد است و از این سهمیه کم نمی‌شود.)
                          </p>
                        </Field>
                      </div>
                    )}
                  </div>
                </div>
              ))}
              {form.plans.length === 0 && <p className="text-xs text-white/40">پلنی تعریف نشده است؛ در این حالت قیمت پایه محصول اعمال می‌شود.</p>}
            </div>
          </div>

          <div className="rounded-xl border border-dashed border-white/15 p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h4 className="text-sm font-bold text-white">بارگذاری از فایل Markdown (.md)</h4>
                <p className="mt-1 text-xs text-white/45">فایل محتوای محصول را آپلود کنید تا «توضیحات» و «سوالات متداول» به‌صورت خودکار در فرم پر شوند و نیازی به ورود دستی نباشد. مقادیر فعلی جایگزین می‌شوند؛ پس از بررسی، «ذخیره محصول» را بزنید.</p>
              </div>
              <label className="shrink-0 cursor-pointer rounded-lg border border-white/10 px-4 py-2 text-xs font-bold text-white/80 transition hover:bg-white/5">
                انتخاب فایل .md
                <input type="file" accept=".md,.markdown,text/markdown,text/plain" className="hidden" onChange={(e) => { const f = e.target.files?.[0]; if (f) importMd(f); e.target.value = ""; }} />
              </label>
            </div>
            {importMsg && <p className={`mt-2 text-xs font-bold ${importMsg.startsWith("✓") ? "text-emerald-400" : "text-amber-400"}`}>{importMsg}</p>}
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
