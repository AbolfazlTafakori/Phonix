"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { HeroSlide, HeroSlideInput } from "@/lib/types";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";
import ImageField from "@/components/admin/ImageField";
import AdminIcon from "@/components/admin/AdminIcon";
import { HERO_TRUST_ICONS, heroTrustIconNode } from "@/components/heroTrustIcons";

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

  const setTrustItem = (id: number, i: number, key: "icon" | "label", value: string) =>
    setDrafts((prev) => ({
      ...prev,
      [id]: { ...prev[id], trust: (prev[id].trust ?? []).map((t, idx) => (idx === i ? { ...t, [key]: value } : t)) },
    }));
  const addTrustItem = (id: number) =>
    setDrafts((prev) => ({ ...prev, [id]: { ...prev[id], trust: [...(prev[id].trust ?? []), { icon: "check", label: "" }] } }));
  const removeTrustItem = (id: number, i: number) =>
    setDrafts((prev) => ({ ...prev, [id]: { ...prev[id], trust: (prev[id].trust ?? []).filter((_, idx) => idx !== i) } }));

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
        eyebrow: "",
        badge: "",
        priceFrom: null,
        oldPrice: null,
        buttonText: "خرید اشتراک",
        buttonLink: "#",
        secondaryButtonText: "",
        secondaryButtonLink: "",
        accentColor: "#e60053",
        accentScale: 1,
        trust: [
          { icon: "bolt", label: "تحویل آنی" },
          { icon: "shield", label: "گارانتی کامل" },
          { icon: "lock", label: "پرداخت امن" },
          { icon: "headset", label: "پشتیبانی ۲۴/۷" },
        ],
        trustColor: "",
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
                      <Field label="متن دکمه اصلی">
                        <input value={d.buttonText} onChange={(e) => setField(s.id, "buttonText", e.target.value)} className={inputCls} />
                      </Field>
                      <Field label="لینک دکمه اصلی">
                        <input value={d.buttonLink} onChange={(e) => setField(s.id, "buttonLink", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="#" />
                      </Field>
                    </div>

                    <div className="grid gap-4 sm:grid-cols-2">
                      <Field label="متن دکمه دوم (اختیاری)">
                        <input value={d.secondaryButtonText} onChange={(e) => setField(s.id, "secondaryButtonText", e.target.value)} className={inputCls} placeholder="مشاهده پلن‌ها" />
                      </Field>
                      <Field label="لینک دکمه دوم">
                        <input value={d.secondaryButtonLink} onChange={(e) => setField(s.id, "secondaryButtonLink", e.target.value)} dir="ltr" className={`${inputCls} text-left`} placeholder="#" />
                      </Field>
                    </div>

                    <Field label="متن چیپ بالای عنوان (اختیاری)">
                      <input value={d.eyebrow} onChange={(e) => setField(s.id, "eyebrow", e.target.value)} className={inputCls} placeholder="اکانت اوریجینال · گارانتی کامل" />
                    </Field>

                    <div className="grid gap-4 sm:grid-cols-3">
                      <Field label="قیمت از (تومان)">
                        <input
                          type="number"
                          dir="ltr"
                          value={d.priceFrom ?? ""}
                          onChange={(e) => setField(s.id, "priceFrom", e.target.value === "" ? null : Number(e.target.value))}
                          className={`${inputCls} text-left`}
                          placeholder="99000"
                        />
                      </Field>
                      <Field label="قیمت قبل تخفیف (اختیاری)">
                        <input
                          type="number"
                          dir="ltr"
                          value={d.oldPrice ?? ""}
                          onChange={(e) => setField(s.id, "oldPrice", e.target.value === "" ? null : Number(e.target.value))}
                          className={`${inputCls} text-left`}
                          placeholder="125000"
                        />
                      </Field>
                      <Field label="برچسب ریبون (اختیاری)">
                        <input value={d.badge} onChange={(e) => setField(s.id, "badge", e.target.value)} className={inputCls} placeholder="۲۰٪ تخفیف" />
                      </Field>
                    </div>

                    <Field label="رنگ اکسنت (هاله و ریبون)">
                      <div className="flex items-center gap-3">
                        <input
                          type="color"
                          value={/^#[0-9a-f]{6}$/i.test(d.accentColor) ? d.accentColor : "#e60053"}
                          onChange={(e) => setField(s.id, "accentColor", e.target.value)}
                          className="h-11 w-14 shrink-0 cursor-pointer rounded-xl border border-white/10 bg-[#0d0d15] p-1"
                          aria-label="انتخاب رنگ اکسنت"
                        />
                        <input
                          value={d.accentColor}
                          onChange={(e) => setField(s.id, "accentColor", e.target.value)}
                          dir="ltr"
                          className={`${inputCls} text-left`}
                          placeholder="#e60053"
                        />
                      </div>
                    </Field>

                    <Field label="اندازهٔ هالهٔ نورانی">
                      <div className="flex items-center gap-3">
                        <input
                          type="range"
                          min={50}
                          max={200}
                          step={5}
                          value={Math.round((d.accentScale ?? 1) * 100)}
                          onChange={(e) => setField(s.id, "accentScale", Number(e.target.value) / 100)}
                          className="h-2 flex-1 cursor-pointer accent-[#e60053]"
                          aria-label="اندازهٔ اکسنت"
                        />
                        <span className="w-12 shrink-0 text-left text-xs font-medium text-white/60" dir="ltr">
                          {Math.round((d.accentScale ?? 1) * 100)}%
                        </span>
                      </div>
                    </Field>

                    <Field label="رنگ هالهٔ نشان‌ها (خالی = رنگ اکسنت)">
                      <div className="flex items-center gap-3">
                        <input
                          type="color"
                          value={/^#[0-9a-f]{6}$/i.test(d.trustColor) ? d.trustColor : (/^#[0-9a-f]{6}$/i.test(d.accentColor) ? d.accentColor : "#e60053")}
                          onChange={(e) => setField(s.id, "trustColor", e.target.value)}
                          className="h-11 w-14 shrink-0 cursor-pointer rounded-xl border border-white/10 bg-[#0d0d15] p-1"
                          aria-label="انتخاب رنگ هالهٔ نشان‌ها"
                        />
                        <input
                          value={d.trustColor}
                          onChange={(e) => setField(s.id, "trustColor", e.target.value)}
                          dir="ltr"
                          className={`${inputCls} text-left`}
                          placeholder="خالی = رنگ اکسنت"
                        />
                      </div>
                    </Field>

                    <div className="rounded-xl border border-white/10 p-4">
                      <div className="mb-3 flex items-center justify-between">
                        <span className="text-sm font-bold text-white/80">نشان‌های اعتماد (این بنر)</span>
                        <button
                          onClick={() => addTrustItem(s.id)}
                          className="flex items-center gap-1.5 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/80 transition hover:bg-white/5"
                        >
                          <AdminIcon name="plus" className="h-3.5 w-3.5" />
                          افزودن
                        </button>
                      </div>
                      <div className="grid gap-2">
                        {(d.trust ?? []).map((t, i) => (
                          <div key={i} className="flex items-center gap-2 rounded-lg bg-white/[0.03] p-2">
                            <svg viewBox="0 0 24 24" className="h-5 w-5 shrink-0" style={{ color: d.trustColor?.trim() || d.accentColor || "#e60053" }} fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
                              {heroTrustIconNode(t.icon)}
                            </svg>
                            <select value={t.icon} onChange={(e) => setTrustItem(s.id, i, "icon", e.target.value)} className={`${inputCls} h-10 w-32 shrink-0`}>
                              {HERO_TRUST_ICONS.map((opt) => (
                                <option key={opt.key} value={opt.key} className="bg-[#15151f]">{opt.label}</option>
                              ))}
                            </select>
                            <input value={t.label} onChange={(e) => setTrustItem(s.id, i, "label", e.target.value)} className={`${inputCls} h-10`} placeholder="متن نشان" />
                            <button
                              onClick={() => removeTrustItem(s.id, i)}
                              className="grid h-10 w-10 shrink-0 place-items-center rounded-lg border border-white/10 text-white/55 transition hover:border-rose-500/50 hover:text-rose-400"
                              aria-label="حذف نشان"
                            >
                              <AdminIcon name="trash" className="h-4 w-4" />
                            </button>
                          </div>
                        ))}
                        {(d.trust ?? []).length === 0 && (
                          <p className="py-2 text-center text-xs text-white/40">نشانی نیست — پیش‌فرض‌ها نمایش داده می‌شوند.</p>
                        )}
                      </div>
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
