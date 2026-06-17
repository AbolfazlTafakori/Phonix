"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { HeroSlide, HeroSlideInput } from "@/lib/types";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";
import ImageField from "@/components/admin/ImageField";
import AdminIcon from "@/components/admin/AdminIcon";

export default function AdminHeroPage() {
  const [slides, setSlides] = useState<HeroSlide[]>([]);
  const [drafts, setDrafts] = useState<Record<number, HeroSlideInput>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState<number | null>(null);
  const [adding, setAdding] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const data = await api.hero.list();
        setSlides(data);
        setDrafts(Object.fromEntries(data.map((s) => [s.id, toInput(s)])));
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const setField = <K extends keyof HeroSlideInput>(id: number, key: K, value: HeroSlideInput[K]) =>
    setDrafts((prev) => ({ ...prev, [id]: { ...prev[id], [key]: value } }));

  const dirty = (s: HeroSlide) => JSON.stringify(drafts[s.id]) !== JSON.stringify(toInput(s));

  async function save(s: HeroSlide) {
    setBusy(s.id);
    try {
      const updated = await api.hero.update(s.id, drafts[s.id]);
      setSlides((prev) => prev.map((x) => (x.id === s.id ? updated : x)));
      setDrafts((prev) => ({ ...prev, [s.id]: toInput(updated) }));
    } finally {
      setBusy(null);
    }
  }

  async function remove(s: HeroSlide) {
    if (!confirm(`اسلاید «${s.title}» حذف شود؟`)) return;
    setBusy(s.id);
    try {
      await api.hero.remove(s.id);
      setSlides((prev) => prev.filter((x) => x.id !== s.id));
    } finally {
      setBusy(null);
    }
  }

  async function add() {
    setAdding(true);
    try {
      const created = await api.hero.create({
        title: "اسلاید جدید",
        description: "",
        image: "",
        logo: "",
        buttonText: "مطالعه بیشتر",
        buttonLink: "#",
        sortOrder: slides.length + 1,
        isActive: true,
      });
      setSlides((prev) => [...prev, created]);
      setDrafts((prev) => ({ ...prev, [created.id]: toInput(created) }));
    } finally {
      setAdding(false);
    }
  }

  return (
    <div>
      <PageHeader
        title="اسلایدر اصلی"
        desc="بنرهای معرفی برند در بالای صفحه اصلی"
        action={
          <button
            onClick={add}
            disabled={adding}
            className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
          >
            {adding ? <Spinner /> : <AdminIcon name="plus" className="h-4 w-4" />}
            اسلاید جدید
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
        <div className="space-y-5">
          {slides.map((s) => {
            const d = drafts[s.id];
            if (!d) return null;
            return (
              <Card key={s.id} className="p-5">
                <div className="grid gap-5 lg:grid-cols-[260px_1fr]">
                  <div className="space-y-3">
                    <ImageField label="تصویر اصلی" aspect="wide" value={d.image} onChange={(v) => setField(s.id, "image", v)} />
                    <ImageField label="لوگو (اختیاری)" aspect="logo" value={d.logo} onChange={(v) => setField(s.id, "logo", v)} />
                    <label className="flex items-center justify-between rounded-xl bg-white/[0.03] px-3 py-2.5">
                      <span className="text-sm text-white/80">نمایش اسلاید</span>
                      <Toggle checked={d.isActive} onChange={(v) => setField(s.id, "isActive", v)} />
                    </label>
                  </div>

                  <div className="grid gap-4">
                    <div className="grid gap-4 sm:grid-cols-2">
                      <Field label="عنوان">
                        <input value={d.title} onChange={(e) => setField(s.id, "title", e.target.value)} className={inputCls} />
                      </Field>
                      <Field label="ترتیب نمایش">
                        <input type="number" dir="ltr" value={d.sortOrder} onChange={(e) => setField(s.id, "sortOrder", Number(e.target.value))} className={`${inputCls} text-left`} />
                      </Field>
                    </div>

                    <Field label="متن توضیحات">
                      <textarea rows={4} value={d.description} onChange={(e) => setField(s.id, "description", e.target.value)} className={`${inputCls} h-auto py-3`} />
                    </Field>

                    <div className="grid gap-4 sm:grid-cols-2">
                      <Field label="متن دکمه">
                        <input value={d.buttonText} onChange={(e) => setField(s.id, "buttonText", e.target.value)} className={inputCls} />
                      </Field>
                      <Field label="لینک دکمه">
                        <input value={d.buttonLink} onChange={(e) => setField(s.id, "buttonLink", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="#" />
                      </Field>
                    </div>

                    <div className="flex items-center gap-3">
                      <button
                        onClick={() => save(s)}
                        disabled={!dirty(s) || busy === s.id}
                        className={`flex h-11 items-center gap-2 rounded-xl px-8 text-sm font-bold transition ${
                          dirty(s) ? "bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-white hover:brightness-110" : "cursor-default border border-white/10 text-white/30"
                        }`}
                      >
                        {busy === s.id ? <Spinner /> : "ذخیره"}
                      </button>
                      <button
                        onClick={() => remove(s)}
                        className="grid h-11 w-11 place-items-center rounded-xl border border-white/10 text-white/55 transition hover:border-rose-500/50 hover:text-rose-400"
                      >
                        <AdminIcon name="trash" className="h-5 w-5" />
                      </button>
                    </div>
                  </div>
                </div>
              </Card>
            );
          })}

          {slides.length === 0 && <Card className="p-12 text-center text-white/40">هنوز اسلایدی اضافه نشده است</Card>}
        </div>
      )}
    </div>
  );
}

function toInput(s: HeroSlide): HeroSlideInput {
  const { id, ...rest } = s;
  void id;
  return rest;
}
