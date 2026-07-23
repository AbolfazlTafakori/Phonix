"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { V2RayCategory, V2RayInbound, V2RayPanelInfo, V2RayPlan, V2RayPlanInput } from "@/lib/types";
import { formatToman } from "@/lib/format";
import { Card, PageHeader, Spinner, Toggle, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

// Owner-only management of the SEPARATE V2Ray catalogue: categories and the plans under them, kept apart
// from the ordinary products because they are many and panel-bound. Each plan carries its full spec —
// panel + specific inbound(s) + traffic + duration + IP limit + price — so a purchase provisions from the
// plan alone.

function fmt(n: number): string {
  return n.toLocaleString("fa-IR");
}

export default function AdminV2RayPlansPage() {
  const [categories, setCategories] = useState<V2RayCategory[]>([]);
  const [plans, setPlans] = useState<V2RayPlan[]>([]);
  const [panels, setPanels] = useState<V2RayPanelInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [editing, setEditing] = useState<V2RayPlan | "new" | null>(null);

  async function load() {
    try {
      const [c, p, pn] = await Promise.all([
        api.v2ray.categories.list(),
        api.v2ray.plans.list(),
        api.v2ray.panels(),
      ]);
      setCategories(c);
      setPlans(p);
      setPanels(pn);
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  if (loading) {
    return <div className="grid place-items-center py-24"><Spinner className="h-8 w-8" /></div>;
  }

  return (
    <div>
      <PageHeader
        title="پلن‌های v2ray"
        desc="دسته‌بندی‌ها و پلن‌های فروش سرویس V2Ray، جدا از محصولات عادی."
        action={
          !editing && categories.length > 0 && panels.length > 0 && (
            <button
              onClick={() => setEditing("new")}
              className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
            >
              <AdminIcon name="plus" className="h-4 w-4" />
              پلن جدید
            </button>
          )
        }
      />

      {error && <Card className="mb-5 p-5 text-center text-rose-400">{error}</Card>}

      {panels.length === 0 && (
        <Card className="mb-5 border-amber-500/20 bg-amber-500/[0.06] p-5">
          <p className="font-bold text-amber-300">هنوز پنلی اضافه نشده است</p>
          <p className="mt-1 text-sm text-white/50">ابتدا از «تنظیمات پنل v2ray» یک پنل اضافه کنید تا بتوانید پلن بسازید.</p>
        </Card>
      )}

      <CategoryManager categories={categories} onChange={setCategories} onPlansChanged={load} />

      {editing ? (
        <PlanForm
          plan={editing === "new" ? null : editing}
          categories={categories}
          panels={panels}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            load();
          }}
        />
      ) : (
        <PlanList
          plans={plans}
          categories={categories}
          panels={panels}
          onEdit={setEditing}
          onDeleted={(id) => setPlans((p) => p.filter((x) => x.id !== id))}
        />
      )}
    </div>
  );
}

function CategoryManager({
  categories,
  onChange,
  onPlansChanged,
}: {
  categories: V2RayCategory[];
  onChange: (c: V2RayCategory[]) => void;
  onPlansChanged: () => void;
}) {
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function add() {
    if (!name.trim()) return;
    setBusy(true);
    setError("");
    try {
      const c = await api.v2ray.categories.add({ name: name.trim(), icon: "", sortOrder: categories.length, active: true });
      onChange([...categories, c]);
      setName("");
    } catch (e) {
      setError(e instanceof Error ? e.message : "افزودن دسته ناموفق بود");
    } finally {
      setBusy(false);
    }
  }

  async function remove(id: number) {
    if (!confirm("این دسته‌بندی و همه‌ی پلن‌های داخلش حذف شوند؟")) return;
    try {
      await api.v2ray.categories.remove(id);
      onChange(categories.filter((c) => c.id !== id));
      onPlansChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : "حذف ناموفق بود");
    }
  }

  async function toggleActive(cat: V2RayCategory) {
    const next = { ...cat, active: !cat.active };
    onChange(categories.map((c) => (c.id === cat.id ? next : c)));
    try {
      await api.v2ray.categories.update(cat.id, { name: cat.name, icon: cat.icon, sortOrder: cat.sortOrder, active: next.active });
    } catch {
      onChange(categories.map((c) => (c.id === cat.id ? cat : c)));
    }
  }

  return (
    <Card className="mb-5 p-5">
      <p className="mb-3 text-sm font-bold text-white">دسته‌بندی‌ها</p>
      <div className="mb-3 flex flex-wrap gap-2">
        {categories.length === 0 ? (
          <p className="text-xs text-white/40">هنوز دسته‌بندی‌ای نیست. برای ساخت پلن اول یک دسته اضافه کنید.</p>
        ) : (
          categories.map((c) => (
            <span
              key={c.id}
              className={`flex items-center gap-2 rounded-xl border px-3 py-1.5 text-xs font-bold ${
                c.active ? "border-white/10 text-white/80" : "border-white/8 text-white/40"
              }`}
            >
              {c.name}
              <span className="text-white/35">({fmt(c.planCount)})</span>
              <button onClick={() => toggleActive(c)} title={c.active ? "غیرفعال کن" : "فعال کن"} className="text-white/40 hover:text-white">
                {c.active ? "◉" : "○"}
              </button>
              <button onClick={() => remove(c.id)} title="حذف" className="text-white/40 hover:text-rose-300">✕</button>
            </span>
          ))
        )}
      </div>
      <div className="flex gap-2">
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && add()}
          placeholder="نام دسته‌بندی جدید"
          className={`${inputCls} h-10 max-w-xs`}
        />
        <button onClick={add} disabled={busy || !name.trim()} className="h-10 rounded-xl border border-white/10 px-4 text-sm font-bold text-white/70 transition hover:bg-white/5 disabled:opacity-50">
          {busy ? <Spinner className="h-4 w-4" /> : "افزودن"}
        </button>
      </div>
      {error && <p className="mt-2 text-xs text-rose-400">{error}</p>}
    </Card>
  );
}

function PlanList({
  plans,
  categories,
  panels,
  onEdit,
  onDeleted,
}: {
  plans: V2RayPlan[];
  categories: V2RayCategory[];
  panels: V2RayPanelInfo[];
  onEdit: (p: V2RayPlan) => void;
  onDeleted: (id: number) => void;
}) {
  const catName = (id: number) => categories.find((c) => c.id === id)?.name ?? "—";
  const panelLabel = (id: number) => {
    const p = panels.find((x) => x.id === id);
    if (!p) return "پنل حذف‌شده";
    try {
      return new URL(p.url).host;
    } catch {
      return p.url;
    }
  };

  async function remove(id: number) {
    if (!confirm("این پلن حذف شود؟")) return;
    try {
      await api.v2ray.plans.remove(id);
      onDeleted(id);
    } catch {
      /* ignore; the row stays */
    }
  }

  if (plans.length === 0) {
    return <Card className="p-12 text-center text-sm text-white/40">هنوز پلنی ساخته نشده است.</Card>;
  }

  // Group by category so the many plans stay organized, mirroring the storefront.
  const byCategory = categories.map((c) => ({ category: c, items: plans.filter((p) => p.categoryId === c.id) }));

  return (
    <div className="space-y-6">
      {byCategory.map(({ category, items }) =>
        items.length === 0 ? null : (
          <div key={category.id}>
            <p className="mb-2.5 flex items-center gap-2 text-sm font-bold text-white/80">
              <span className="h-4 w-1 rounded-full bg-gradient-to-b from-[#ef233c] to-[#ff5a1f]" />
              {category.name}
            </p>
            <div className="space-y-2.5">
              {items.map((p) => (
                <Card key={p.id} className="flex flex-wrap items-center justify-between gap-3 p-4">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="font-bold text-white">{p.title}</span>
                      {!p.active && <span className="rounded-full bg-white/10 px-2 py-0.5 text-[10px] font-bold text-white/45">غیرفعال</span>}
                    </div>
                    <p className="mt-1 text-xs text-white/45">
                      {p.volumeGb === 0 ? "حجم نامحدود" : `${fmt(p.volumeGb)} گیگ`}
                      {" · "}
                      {p.durationDays === 0 ? "بدون انقضا" : `${fmt(p.durationDays)} روز`}
                      {" · "}
                      {p.ipLimit === 0 ? "IP نامحدود" : `${fmt(p.ipLimit)} کاربر`}
                      {" · "}
                      {panelLabel(p.panelId)} · {fmt(p.inboundIds.length)} اینباند
                    </p>
                  </div>
                  <div className="flex shrink-0 items-center gap-3">
                    <div className="text-left">
                      {p.discountPercent > 0 && <span className="block text-[11px] text-white/35 line-through">{formatToman(p.price)}</span>}
                      <span className="font-bold text-emerald-400">{formatToman(p.finalPrice)}</span>
                    </div>
                    <button onClick={() => onEdit(p)} className="grid h-9 w-9 place-items-center rounded-lg border border-white/10 text-white/60 transition hover:bg-white/5 hover:text-white">
                      <AdminIcon name="edit" className="h-4 w-4" />
                    </button>
                    <button onClick={() => remove(p.id)} className="grid h-9 w-9 place-items-center rounded-lg border border-white/10 text-rose-300/80 transition hover:bg-rose-500/10">
                      <AdminIcon name="trash" className="h-4 w-4" />
                    </button>
                  </div>
                </Card>
              ))}
            </div>
          </div>
        ),
      )}
    </div>
  );
}

function PlanForm({
  plan,
  categories,
  panels,
  onClose,
  onSaved,
}: {
  plan: V2RayPlan | null;
  categories: V2RayCategory[];
  panels: V2RayPanelInfo[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState<V2RayPlanInput>({
    categoryId: plan?.categoryId ?? categories[0]?.id ?? 0,
    title: plan?.title ?? "",
    description: plan?.description ?? "",
    panelId: plan?.panelId ?? panels[0]?.id ?? 0,
    inboundIds: plan?.inboundIds ?? [],
    volumeGb: plan?.volumeGb ?? 0,
    durationDays: plan?.durationDays ?? 30,
    ipLimit: plan?.ipLimit ?? 0,
    price: plan?.price ?? 0,
    discountPercent: plan?.discountPercent ?? 0,
    active: plan?.active ?? true,
    sortOrder: plan?.sortOrder ?? 0,
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  // The chosen panel's inbounds, loaded live so the operator picks exact locations for this plan.
  const [inbounds, setInbounds] = useState<V2RayInbound[] | null>(null);
  const [inboundsErr, setInboundsErr] = useState("");

  useEffect(() => {
    if (!form.panelId) return;
    setInbounds(null);
    setInboundsErr("");
    api.v2ray
      .inbounds(form.panelId)
      .then(setInbounds)
      .catch((e) => setInboundsErr(e instanceof Error ? e.message : "خواندن اینباندها ناموفق بود"));
  }, [form.panelId]);

  function set<K extends keyof V2RayPlanInput>(key: K, value: V2RayPlanInput[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  function toggleInbound(id: number) {
    setForm((f) => ({
      ...f,
      inboundIds: f.inboundIds.includes(id) ? f.inboundIds.filter((x) => x !== id) : [...f.inboundIds, id],
    }));
  }

  const presets = [
    { label: "۱ ماه", days: 30 },
    { label: "۳ ماه", days: 90 },
    { label: "۶ ماه", days: 180 },
    { label: "۱ سال", days: 365 },
    { label: "نامحدود", days: 0 },
  ];

  async function save() {
    setError("");
    if (!form.title.trim()) return setError("عنوان پلن را وارد کنید.");
    if (!form.categoryId) return setError("دسته‌بندی را انتخاب کنید.");
    if (!form.panelId) return setError("پنل را انتخاب کنید.");
    if (form.inboundIds.length === 0) return setError("حداقل یک اینباند (لوکیشن) انتخاب کنید.");
    setBusy(true);
    try {
      if (plan) await api.v2ray.plans.update(plan.id, form);
      else await api.v2ray.plans.add(form);
      onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : "ذخیره پلن ناموفق بود");
    } finally {
      setBusy(false);
    }
  }

  const panelHost = (p: V2RayPanelInfo) => {
    try {
      return new URL(p.url).host;
    } catch {
      return p.url;
    }
  };

  return (
    <Card className="p-5 sm:p-6">
      <div className="mb-5 flex items-center justify-between">
        <p className="font-bold text-white">{plan ? "ویرایش پلن" : "پلن جدید"}</p>
        <button onClick={onClose} className="text-sm text-white/50 transition hover:text-white">انصراف</button>
      </div>

      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-white/55">عنوان پلن</span>
            <input value={form.title} onChange={(e) => set("title", e.target.value)} className={inputCls} placeholder="مثلاً VPN یک‌ماهه ۵۰ گیگ" />
          </label>
          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-white/55">دسته‌بندی</span>
            <select value={form.categoryId} onChange={(e) => set("categoryId", Number(e.target.value))} className={inputCls}>
              {categories.map((c) => <option key={c.id} value={c.id} className="bg-[#15151f]">{c.name}</option>)}
            </select>
          </label>
        </div>

        <label className="block">
          <span className="mb-1.5 block text-xs font-medium text-white/55">توضیحات (اختیاری)</span>
          <textarea value={form.description} onChange={(e) => set("description", e.target.value)} rows={2} className="w-full rounded-xl border border-white/10 bg-[#0d0d15] px-3 py-2 text-sm text-white outline-none focus:border-[#3a64f2]" />
        </label>

        <div className="grid gap-4 sm:grid-cols-3">
          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-white/55">حجم (گیگ) · ۰=نامحدود</span>
            <input value={form.volumeGb} onChange={(e) => set("volumeGb", Math.max(0, Number(e.target.value) || 0))} dir="ltr" inputMode="numeric" className={`${inputCls} text-left`} />
          </label>
          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-white/55">مدت (روز) · ۰=نامحدود</span>
            <input value={form.durationDays} onChange={(e) => set("durationDays", Math.max(0, Number(e.target.value) || 0))} dir="ltr" inputMode="numeric" className={`${inputCls} text-left`} />
          </label>
          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-white/55">محدودیت IP · ۰=نامحدود</span>
            <input value={form.ipLimit} onChange={(e) => set("ipLimit", Math.max(0, Number(e.target.value) || 0))} dir="ltr" inputMode="numeric" className={`${inputCls} text-left`} />
          </label>
        </div>
        <div className="flex flex-wrap gap-1.5">
          {presets.map((p) => (
            <button
              key={p.label}
              onClick={() => set("durationDays", p.days)}
              className={`rounded-lg border px-2.5 py-1 text-[11px] font-bold transition ${
                form.durationDays === p.days ? "border-transparent bg-[#3a64f2]/20 text-[#8aa6ff]" : "border-white/10 text-white/55 hover:text-white"
              }`}
            >
              {p.label}
            </button>
          ))}
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-white/55">قیمت (تومان)</span>
            <input value={form.price} onChange={(e) => set("price", Math.max(0, Number(e.target.value) || 0))} dir="ltr" inputMode="numeric" className={`${inputCls} text-left`} />
          </label>
          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-white/55">تخفیف (٪)</span>
            <input value={form.discountPercent} onChange={(e) => set("discountPercent", Math.min(100, Math.max(0, Number(e.target.value) || 0)))} dir="ltr" inputMode="numeric" className={`${inputCls} text-left`} />
          </label>
        </div>

        {/* Provisioning target: which panel + exactly which inbounds this plan creates the account on. */}
        <div className="rounded-xl border border-white/8 bg-white/[0.02] p-4">
          <p className="mb-3 text-sm font-bold text-white">محل ساخت اکانت</p>
          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-white/55">پنل</span>
            <select value={form.panelId} onChange={(e) => { set("panelId", Number(e.target.value)); set("inboundIds", []); }} className={inputCls}>
              {panels.map((p) => <option key={p.id} value={p.id} className="bg-[#15151f]">{panelHost(p)}</option>)}
            </select>
          </label>

          <div className="mt-3">
            <span className="mb-2 block text-xs font-medium text-white/55">اینباند(ها) / لوکیشن این پلن</span>
            {inboundsErr ? (
              <p className="text-xs leading-6 text-rose-400">{inboundsErr}</p>
            ) : inbounds === null ? (
              <div className="flex items-center gap-2 text-xs text-white/45"><Spinner className="h-4 w-4" /> در حال خواندن اینباندها…</div>
            ) : inbounds.length === 0 ? (
              <p className="text-xs text-white/45">اینباندی روی این پنل نیست.</p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {inbounds.map((ib) => {
                  const on = form.inboundIds.includes(ib.id);
                  return (
                    <button
                      key={ib.id}
                      onClick={() => toggleInbound(ib.id)}
                      disabled={!ib.enable}
                      className={`flex items-center gap-2 rounded-lg border px-3 py-1.5 text-xs font-bold transition ${
                        !ib.enable ? "cursor-not-allowed border-white/8 text-white/30" : on ? "border-transparent bg-[#3a64f2]/20 text-[#8aa6ff]" : "border-white/10 text-white/55 hover:text-white"
                      }`}
                    >
                      <span className={`grid h-4 w-4 place-items-center rounded border text-[10px] ${on ? "border-[#8aa6ff] bg-[#3a64f2]/40 text-white" : "border-white/25"}`}>{on ? "✓" : ""}</span>
                      {ib.remark || `اینباند ${fmt(ib.id)}`}
                      <span dir="ltr" className="text-white/35">#{ib.id}</span>
                    </button>
                  );
                })}
              </div>
            )}
          </div>
        </div>

        <div className="flex items-center justify-between rounded-xl border border-white/8 px-4 py-3">
          <span className="text-sm text-white/70">پلن فعال باشد</span>
          <Toggle checked={form.active} onChange={(v) => set("active", v)} />
        </div>

        {error && <p className="text-sm leading-7 text-rose-400">{error}</p>}

        <div className="flex items-center gap-2 border-t border-white/8 pt-4">
          <button onClick={save} disabled={busy} className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-7 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60">
            {busy && <Spinner />}
            {plan ? "ذخیره تغییرات" : "ساخت پلن"}
          </button>
          <button onClick={onClose} disabled={busy} className="mr-auto text-sm text-white/45 transition hover:text-white">انصراف</button>
        </div>
      </div>
    </Card>
  );
}
