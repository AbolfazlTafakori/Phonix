"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { HomeCategory, HomeCategoryInput, Showcase, ShowcaseInput, Product, Category } from "@/lib/types";
import { useSiteContent } from "@/components/admin/useSiteContent";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";
import ImageField from "@/components/admin/ImageField";
import AdminIcon from "@/components/admin/AdminIcon";

type Tab = "sections" | "categories" | "showcase";

const tabs: { key: Tab; label: string }[] = [
  { key: "sections", label: "عنوان‌ها و آمار" },
  { key: "categories", label: "دسته‌بندی‌های صفحه" },
  { key: "showcase", label: "محصولات پرفروش" },
];

export default function AdminHomePage() {
  const [tab, setTab] = useState<Tab>("sections");

  return (
    <div>
      <PageHeader title="بخش‌های صفحه اصلی" desc="عنوان بخش‌ها، آمار، کارت‌های دسته‌بندی و محصولات پرفروش" />

      <div className="mb-6 flex flex-wrap gap-2">
        {tabs.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`rounded-xl border px-5 py-2 text-sm font-bold transition ${
              tab === t.key ? "border-transparent bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white" : "border-white/10 text-white/60 hover:text-white"
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === "sections" && <SectionsPanel />}
      {tab === "categories" && <CategoriesPanel />}
      {tab === "showcase" && <ShowcasePanel />}
    </div>
  );
}

function SectionsPanel() {
  const { content, setContent, loading, error, saving, saved, save } = useSiteContent();

  const setTitle = (key: "categoriesTitle" | "bestSellersTitle" | "blogTitle", value: string) =>
    setContent((c) => (c ? { ...c, sections: { ...c.sections, [key]: value } } : c));

  const setStat = (i: number, key: "value" | "label" | "icon", value: string) =>
    setContent((c) =>
      c ? { ...c, stats: c.stats.map((s, idx) => (idx === i ? { ...s, [key]: value } : s)) } : c,
    );

  if (loading) return <Centered><Spinner className="h-8 w-8" /></Centered>;
  if (error || !content) return <Card className="p-8 text-center text-rose-400">{error || "محتوا یافت نشد"}</Card>;

  return (
    <div className="grid gap-6 lg:grid-cols-2">
      <Card className="p-6">
        <h3 className="mb-5 text-lg font-bold text-white">عنوان بخش‌ها</h3>
        <div className="grid gap-4">
          <Field label="عنوان بخش دسته‌بندی‌ها">
            <input value={content.sections.categoriesTitle} onChange={(e) => setTitle("categoriesTitle", e.target.value)} className={inputCls} />
          </Field>
          <Field label="عنوان بخش محصولات پرفروش">
            <input value={content.sections.bestSellersTitle} onChange={(e) => setTitle("bestSellersTitle", e.target.value)} className={inputCls} />
          </Field>
          <Field label="عنوان بخش بلاگ">
            <input value={content.sections.blogTitle} onChange={(e) => setTitle("blogTitle", e.target.value)} className={inputCls} />
          </Field>
        </div>
      </Card>

      <Card className="p-6">
        <h3 className="mb-5 text-lg font-bold text-white">آمار (نوار اعتماد)</h3>
        <div className="space-y-4">
          {content.stats.map((s, i) => (
            <div key={i} className="rounded-xl bg-white/[0.03] p-3">
              <Field label="عنوان">
                <input value={s.label} onChange={(e) => setStat(i, "label", e.target.value)} className={`${inputCls} h-10`} />
              </Field>
              <div className="mt-3 grid grid-cols-2 gap-3">
                <Field label="آدرس آیکن (اختیاری)">
                  <input value={s.icon ?? ""} onChange={(e) => setStat(i, "icon", e.target.value)} dir="ltr" className={`${inputCls} h-10 text-left`} placeholder="/figma/icon-secure.png" />
                </Field>
                <Field label="عدد (اگر آیکن ندارد)">
                  <input value={s.value ?? ""} onChange={(e) => setStat(i, "value", e.target.value)} dir="ltr" className={`${inputCls} h-10 text-left`} placeholder="+10,000" />
                </Field>
              </div>
            </div>
          ))}
        </div>
      </Card>

      <div className="flex items-center gap-3 lg:col-span-2">
        <button
          onClick={save}
          disabled={saving}
          className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 text-sm font-bold text-white transition hover:brightness-110"
        >
          {saving ? <Spinner /> : "ذخیره تغییرات"}
        </button>
        {saved && <span className="text-sm font-medium text-emerald-400">✓ ذخیره شد</span>}
      </div>
    </div>
  );
}

// Maps a card's stored href back to the "اتصال به دسته" dropdown value so the current selection is visible
// (an uncontrolled select always showed the placeholder, making it look like the link never saved). Anything
// that isn't the all-products or a /products?cat=N link counts as a custom link.
function hrefToCatValue(href: string): string {
  if (href === "/products" || href === "/films") return "all";
  const m = href.match(/[?&]cat=(\d+)/);
  return m ? m[1] : "";
}

function CategoriesPanel() {
  const [items, setItems] = useState<HomeCategory[]>([]);
  const [drafts, setDrafts] = useState<Record<number, HomeCategoryInput>>({});
  const [categories, setCategories] = useState<Category[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<number | null>(null);
  const [adding, setAdding] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const [data, cats] = await Promise.all([api.homeCategories.list(), api.categories.list().catch(() => [])]);
        setItems(data);
        setCategories(cats);
        setDrafts(Object.fromEntries(data.map((c) => [c.id, stripId(c)])));
      } catch {
        // leave the section empty if it can't load
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const setField = <K extends keyof HomeCategoryInput>(id: number, key: K, value: HomeCategoryInput[K]) =>
    setDrafts((p) => ({ ...p, [id]: { ...p[id], [key]: value } }));
  const dirty = (c: HomeCategory) => JSON.stringify(drafts[c.id]) !== JSON.stringify(stripId(c));

  async function save(c: HomeCategory) {
    setBusy(c.id);
    try {
      const u = await api.homeCategories.update(c.id, drafts[c.id]);
      setItems((p) => p.map((x) => (x.id === c.id ? u : x)));
      setDrafts((p) => ({ ...p, [c.id]: stripId(u) }));
    } finally {
      setBusy(null);
    }
  }
  async function remove(c: HomeCategory) {
    if (!confirm(`کارت «${c.title}» حذف شود؟`)) return;
    setBusy(c.id);
    try {
      await api.homeCategories.remove(c.id);
      setItems((p) => p.filter((x) => x.id !== c.id));
    } finally {
      setBusy(null);
    }
  }
  async function add() {
    setAdding(true);
    try {
      const created = await api.homeCategories.create({ title: "کارت جدید", icon: "", href: "/products", iconClass: "", sortOrder: items.length + 1, isActive: true });
      setItems((p) => [...p, created]);
      setDrafts((p) => ({ ...p, [created.id]: stripId(created) }));
    } finally {
      setAdding(false);
    }
  }

  if (loading) return <Centered><Spinner className="h-8 w-8" /></Centered>;

  return (
    <div>
      <div className="mb-4 flex justify-end">
        <AddButton onClick={add} adding={adding} label="کارت جدید" />
      </div>
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {items.map((c) => {
          const d = drafts[c.id];
          if (!d) return null;
          return (
            <Card key={c.id} className="p-4">
              <div className="flex items-center justify-between">
                <span className="text-xs text-white/45">نمایش کارت</span>
                <Toggle checked={d.isActive} onChange={(v) => setField(c.id, "isActive", v)} />
              </div>
              <div className="mt-3 grid gap-3">
                <ImageField label="آیکن" aspect="square" value={d.icon} onChange={(v) => setField(c.id, "icon", v)} />
                <Field label="عنوان">
                  <input value={d.title} onChange={(e) => setField(c.id, "title", e.target.value)} className={`${inputCls} h-10`} />
                </Field>
                <Field label="اتصال به دسته">
                  <select
                    value={hrefToCatValue(d.href)}
                    onChange={(e) => setField(c.id, "href", e.target.value === "all" ? "/products" : e.target.value ? `/products?cat=${e.target.value}` : "")}
                    className={`${inputCls} h-10`}
                  >
                    <option value="" className="bg-[#15151f]">— لینک دلخواه —</option>
                    <option value="all" className="bg-[#15151f]">همه محصولات</option>
                    {categories.map((cat) => <option key={cat.id} value={cat.id} className="bg-[#15151f]">{cat.name}</option>)}
                  </select>
                </Field>
                <div className="grid grid-cols-2 gap-3">
                  <Field label="لینک">
                    <input value={d.href} onChange={(e) => setField(c.id, "href", e.target.value)} dir="ltr" className={`${inputCls} h-10 text-left`} />
                  </Field>
                  <Field label="ترتیب">
                    <input type="number" dir="ltr" value={d.sortOrder} onChange={(e) => setField(c.id, "sortOrder", Number(e.target.value))} className={`${inputCls} h-10 text-left`} />
                  </Field>
                </div>
                <RowActions onSave={() => save(c)} onDelete={() => remove(c)} dirty={dirty(c)} busy={busy === c.id} />
              </div>
            </Card>
          );
        })}
      </div>
    </div>
  );
}

function ShowcasePanel() {
  const [items, setItems] = useState<Showcase[]>([]);
  const [drafts, setDrafts] = useState<Record<number, ShowcaseInput>>({});
  const [products, setProducts] = useState<Product[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<number | null>(null);
  const [adding, setAdding] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const [data, prods] = await Promise.all([api.showcase.list(), api.products.list().catch(() => [])]);
        setItems(data);
        setProducts(prods);
        setDrafts(Object.fromEntries(data.map((s) => [s.id, stripId(s)])));
      } catch {
        // leave the section empty if it can't load
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const setField = <K extends keyof ShowcaseInput>(id: number, key: K, value: ShowcaseInput[K]) =>
    setDrafts((p) => ({ ...p, [id]: { ...p[id], [key]: value } }));

  // Picking a real product auto-fills the link (and name/image if empty) so the card always points to a
  // valid product page — no need to type the URL by hand.
  function pickProduct(id: number, productId: number) {
    const prod = products.find((x) => x.id === productId);
    if (!prod) return;
    setDrafts((p) => ({
      ...p,
      [id]: {
        ...p[id],
        href: `/products/detail?id=${prod.id}`,
        name: p[id].name && p[id].name !== "محصول جدید" ? p[id].name : prod.name,
        image: p[id].image || prod.image,
      },
    }));
  }
  const dirty = (s: Showcase) => JSON.stringify(drafts[s.id]) !== JSON.stringify(stripId(s));

  async function save(s: Showcase) {
    setBusy(s.id);
    try {
      const u = await api.showcase.update(s.id, drafts[s.id]);
      setItems((p) => p.map((x) => (x.id === s.id ? u : x)));
      setDrafts((p) => ({ ...p, [s.id]: stripId(u) }));
    } finally {
      setBusy(null);
    }
  }
  async function remove(s: Showcase) {
    if (!confirm(`کارت «${s.name}» حذف شود؟`)) return;
    setBusy(s.id);
    try {
      await api.showcase.remove(s.id);
      setItems((p) => p.filter((x) => x.id !== s.id));
    } finally {
      setBusy(null);
    }
  }
  async function add() {
    setAdding(true);
    try {
      const created = await api.showcase.create({ name: "محصول جدید", image: "", logo: null, href: "#", sortOrder: items.length + 1, isActive: true });
      setItems((p) => [...p, created]);
      setDrafts((p) => ({ ...p, [created.id]: stripId(created) }));
    } finally {
      setAdding(false);
    }
  }

  if (loading) return <Centered><Spinner className="h-8 w-8" /></Centered>;

  return (
    <div>
      <div className="mb-4 flex justify-end">
        <AddButton onClick={add} adding={adding} label="محصول جدید" />
      </div>
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {items.map((s) => {
          const d = drafts[s.id];
          if (!d) return null;
          return (
            <Card key={s.id} className="p-4">
              <div className="flex items-center justify-between">
                <span className="text-xs text-white/45">نمایش کارت</span>
                <Toggle checked={d.isActive} onChange={(v) => setField(s.id, "isActive", v)} />
              </div>
              <div className="mt-3 grid gap-3">
                <ImageField label="تصویر" aspect="square" value={d.image} onChange={(v) => setField(s.id, "image", v)} />
                <ImageField label="لوگو (اختیاری)" aspect="logo" value={d.logo} onChange={(v) => setField(s.id, "logo", v || null)} />
                <Field label="نام">
                  <input value={d.name} onChange={(e) => setField(s.id, "name", e.target.value)} className={`${inputCls} h-10`} />
                </Field>
                <Field label="اتصال به محصول">
                  <select value="" onChange={(e) => e.target.value && pickProduct(s.id, Number(e.target.value))} className={`${inputCls} h-10`}>
                    <option value="" className="bg-[#15151f]">— انتخاب محصول —</option>
                    {products.map((prod) => <option key={prod.id} value={prod.id} className="bg-[#15151f]">{prod.name}</option>)}
                  </select>
                </Field>
                <Field label="لینک">
                  <input value={d.href} onChange={(e) => setField(s.id, "href", e.target.value)} dir="ltr" className={`${inputCls} h-10 text-left`} />
                </Field>
                <RowActions onSave={() => save(s)} onDelete={() => remove(s)} dirty={dirty(s)} busy={busy === s.id} />
              </div>
            </Card>
          );
        })}
      </div>
    </div>
  );
}

function Centered({ children }: { children: React.ReactNode }) {
  return <div className="grid place-items-center py-24">{children}</div>;
}

function AddButton({ onClick, adding, label }: { onClick: () => void; adding: boolean; label: string }) {
  return (
    <button
      onClick={onClick}
      disabled={adding}
      className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
    >
      {adding ? <Spinner /> : <AdminIcon name="plus" className="h-4 w-4" />}
      {label}
    </button>
  );
}

function RowActions({ onSave, onDelete, dirty, busy }: { onSave: () => void; onDelete: () => void; dirty: boolean; busy: boolean }) {
  return (
    <div className="flex items-center gap-2">
      <button
        onClick={onSave}
        disabled={!dirty || busy}
        className={`grid h-10 flex-1 place-items-center rounded-xl text-sm font-bold transition ${
          dirty ? "bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-white hover:brightness-110" : "cursor-default border border-white/10 text-white/30"
        }`}
      >
        {busy ? <Spinner /> : "ذخیره"}
      </button>
      <button
        onClick={onDelete}
        className="grid h-10 w-10 place-items-center rounded-xl border border-white/10 text-white/55 transition hover:border-rose-500/50 hover:text-rose-400"
      >
        <AdminIcon name="trash" className="h-4 w-4" />
      </button>
    </div>
  );
}

function stripId<T extends { id: number }>(item: T): Omit<T, "id"> {
  const { id, ...rest } = item;
  void id;
  return rest;
}
