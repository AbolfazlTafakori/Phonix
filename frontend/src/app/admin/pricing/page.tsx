"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { Category, Product, PricingSettings, Plan } from "@/lib/types";
import { formatToman, formatNumber } from "@/lib/format";
import { Card, PageHeader, Spinner, Toggle, StatusBadge, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

type Tab = "products" | "fees" | "plans";

const tabs: { key: Tab; label: string }[] = [
  { key: "products", label: "قیمت محصولات" },
  { key: "fees", label: "هزینه‌ها و کارمزدها" },
  { key: "plans", label: "پلن‌های اشتراک" },
];

function finalOf(price: number, discount: number) {
  return discount > 0 ? Math.round(price * (1 - discount / 100)) : price;
}

export default function AdminPricingPage() {
  const [tab, setTab] = useState<Tab>("products");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [categories, setCategories] = useState<Category[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [settings, setSettings] = useState<PricingSettings | null>(null);
  const [plans, setPlans] = useState<Plan[]>([]);

  useEffect(() => {
    (async () => {
      try {
        const [c, p, s, pl] = await Promise.all([
          api.categories.list(),
          api.products.list(),
          api.pricing.getSettings(),
          api.pricing.getPlans(),
        ]);
        setCategories(c);
        setProducts(p);
        setSettings(s);
        setPlans(pl);
      } catch (e) {
        setError(e instanceof Error ? e.message : "خطا در بارگذاری اطلاعات");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  return (
    <div>
      <PageHeader title="قیمت‌گذاری" desc="کنترل کامل قیمت محصولات، کارمزدها و پلن‌های اشتراک" />

      <div className="mb-6 flex flex-wrap gap-2">
        {tabs.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`rounded-xl border px-5 py-2 text-sm font-bold transition ${
              tab === t.key
                ? "border-transparent bg-gradient-to-l from-[#e60053] to-[#9c0038] text-white"
                : "border-white/10 text-white/60 hover:text-white"
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {loading ? (
        <div className="grid place-items-center py-24">
          <Spinner className="h-8 w-8" />
        </div>
      ) : error ? (
        <Card className="p-8 text-center text-rose-400">{error}</Card>
      ) : (
        <>
          {tab === "products" && (
            <ProductPricing categories={categories} products={products} setProducts={setProducts} />
          )}
          {tab === "fees" && settings && <FeesPanel settings={settings} setSettings={setSettings} />}
          {tab === "plans" && <PlansPanel plans={plans} setPlans={setPlans} />}
        </>
      )}
    </div>
  );
}

function ProductPricing({
  categories,
  products,
  setProducts,
}: {
  categories: Category[];
  products: Product[];
  setProducts: (updater: (prev: Product[]) => Product[]) => void;
}) {
  const [draft, setDraft] = useState<Record<number, { price: number; discount: number }>>(() =>
    Object.fromEntries(products.map((p) => [p.id, { price: p.price, discount: p.discountPercent }])),
  );
  const [busy, setBusy] = useState<number | null>(null);
  const [savingAll, setSavingAll] = useState(false);

  const grouped = useMemo(() => {
    const withCat = categories
      .map((c) => ({ category: c.name, items: products.filter((p) => p.categoryId === c.id) }))
      .filter((g) => g.items.length > 0);
    const known = new Set(categories.map((c) => c.id));
    const orphans = products.filter((p) => !known.has(p.categoryId));
    if (orphans.length) withCat.push({ category: "بدون دسته", items: orphans });
    return withCat;
  }, [categories, products]);

  const isDirty = (p: Product) => {
    const d = draft[p.id];
    return d && (d.price !== p.price || d.discount !== p.discountPercent);
  };
  const dirtyList = products.filter(isDirty);

  const set = (id: number, key: "price" | "discount", value: number) =>
    setDraft((prev) => ({ ...prev, [id]: { ...prev[id], [key]: value } }));

  async function save(p: Product) {
    const d = draft[p.id];
    setBusy(p.id);
    try {
      const updated = await api.products.updatePrice(p.id, { price: d.price, discountPercent: d.discount });
      setProducts((prev) => prev.map((x) => (x.id === p.id ? updated : x)));
    } finally {
      setBusy(null);
    }
  }

  async function saveAll() {
    setSavingAll(true);
    try {
      for (const p of dirtyList) {
        const d = draft[p.id];
        const updated = await api.products.updatePrice(p.id, { price: d.price, discountPercent: d.discount });
        setProducts((prev) => prev.map((x) => (x.id === p.id ? updated : x)));
      }
    } finally {
      setSavingAll(false);
    }
  }

  return (
    <div className="space-y-6 pb-24">
      {grouped.map((group) => (
        <Card key={group.category} className="overflow-hidden">
          <div className="flex items-center justify-between border-b border-white/8 px-6 py-4">
            <h3 className="text-base font-bold text-white">{group.category}</h3>
            <span className="text-xs text-white/40">{formatNumber(group.items.length)} محصول</span>
          </div>

          <div className="divide-y divide-white/5">
            {group.items.map((p) => {
              const d = draft[p.id] ?? { price: p.price, discount: p.discountPercent };
              const final = finalOf(d.price, d.discount);
              const dirty = isDirty(p);
              return (
                <div key={p.id} className="flex flex-col gap-4 p-4 sm:flex-row sm:flex-wrap sm:items-center sm:px-6">
                  <div className="flex w-full items-center gap-3 sm:w-auto sm:min-w-[220px] sm:flex-1">
                    <img src={p.image} alt={p.name} className="h-11 w-11 shrink-0 rounded-lg object-cover" />
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-medium text-white">{p.name}</p>
                      <p className="font-mono text-xs text-white/40">{p.sku || "—"}</p>
                    </div>
                    <div className="flex shrink-0 items-center gap-1.5">
                      <StatusBadge status={p.isActive ? "فعال" : "ناموجود"} />
                      {p.featured && (
                        <span className="rounded-full bg-[#e60053]/15 px-2.5 py-1 text-[11px] font-medium text-[#ff5a8a]">ویژه</span>
                      )}
                    </div>
                  </div>

                  <div className="grid w-full grid-cols-2 gap-3 sm:flex sm:w-auto sm:items-end sm:gap-4">
                    <label className="sm:w-32">
                      <span className="mb-1 block text-[11px] text-white/45">قیمت پایه</span>
                      <input
                        type="number"
                        dir="ltr"
                        value={d.price}
                        onChange={(e) => set(p.id, "price", Number(e.target.value))}
                        className={`${inputCls} h-10 text-left`}
                      />
                    </label>

                    <label className="sm:w-24">
                      <span className="mb-1 block text-[11px] text-white/45">تخفیف ٪</span>
                      <input
                        type="number"
                        dir="ltr"
                        min={0}
                        max={100}
                        value={d.discount}
                        onChange={(e) => set(p.id, "discount", Math.min(100, Math.max(0, Number(e.target.value))))}
                        className={`${inputCls} h-10 text-left`}
                      />
                    </label>

                    <div className="col-span-2 sm:w-36">
                      <span className="mb-1 block text-[11px] text-white/45">قیمت نهایی</span>
                      <p className="text-sm font-bold text-emerald-400">{formatToman(final)}</p>
                    </div>

                    <button
                      onClick={() => save(p)}
                      disabled={!dirty || busy === p.id}
                      className={`col-span-2 grid h-10 w-full place-items-center rounded-xl text-sm font-bold transition sm:w-24 ${
                        dirty
                          ? "bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-white hover:brightness-110"
                          : "cursor-default border border-white/10 text-white/30"
                      }`}
                    >
                      {busy === p.id ? <Spinner /> : "ذخیره"}
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        </Card>
      ))}

      {dirtyList.length > 0 && (
        <div className="fixed bottom-5 left-5 right-5 z-30 mx-auto flex max-w-md items-center justify-between gap-4 rounded-2xl border border-white/10 bg-[#15151f] px-5 py-3 shadow-2xl lg:right-72">
          <span className="text-sm text-white/70">{formatNumber(dirtyList.length)} تغییر ذخیره‌نشده</span>
          <button
            onClick={saveAll}
            disabled={savingAll}
            className="flex h-10 items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-6 text-sm font-bold text-white transition hover:brightness-110"
          >
            {savingAll ? <Spinner /> : "ذخیره همه"}
          </button>
        </div>
      )}
    </div>
  );
}

function FeesPanel({
  settings,
  setSettings,
}: {
  settings: PricingSettings;
  setSettings: (s: PricingSettings) => void;
}) {
  const [draft, setDraft] = useState<PricingSettings>(settings);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  const set = <K extends keyof PricingSettings>(key: K, value: PricingSettings[K]) =>
    setDraft((prev) => ({ ...prev, [key]: value }));

  async function save() {
    setSaving(true);
    setSaved(false);
    try {
      const updated = await api.pricing.updateSettings(draft);
      setSettings(updated);
      setDraft(updated);
      setSaved(true);
    } finally {
      setSaving(false);
    }
  }

  const percentFields: { key: keyof PricingSettings; label: string; hint: string }[] = [
    { key: "referralCommissionPercent", label: "پورسانت معرف", hint: "درصد پورسانت از خرید زیرمجموعه" },
    { key: "vatPercent", label: "مالیات بر ارزش افزوده", hint: "روی قیمت نهایی اعمال می‌شود" },
    { key: "gatewayFeePercent", label: "کارمزد درگاه پرداخت", hint: "درصد کارمزد درگاه" },
    { key: "cancellationPenaltyPercent", label: "جریمه لغو سفارش", hint: "درصد کسرشده هنگام لغو و بازگشت به کیف پول" },
  ];
  const amountFields: { key: keyof PricingSettings; label: string; hint: string }[] = [
    { key: "minWalletCharge", label: "حداقل شارژ کیف پول", hint: "کمترین مبلغ مجاز برای شارژ" },
    { key: "minWithdraw", label: "حداقل برداشت", hint: "کمترین مبلغ مجاز برای برداشت" },
  ];

  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <Card className="p-6 lg:col-span-2">
        <h3 className="mb-5 text-lg font-bold text-white">کارمزدها و درصدها</h3>
        <div className="grid gap-5 sm:grid-cols-2">
          {percentFields.map((f) => (
            <div key={f.key}>
              <label className="mb-2 block text-sm text-white/70">{f.label}</label>
              <div className="relative">
                <input
                  type="number"
                  dir="ltr"
                  value={draft[f.key] as number}
                  onChange={(e) => set(f.key, Number(e.target.value) as never)}
                  className={`${inputCls} text-left pl-9`}
                />
                <span className="absolute left-3 top-1/2 -translate-y-1/2 text-sm text-white/40">٪</span>
              </div>
              <p className="mt-1.5 text-xs text-white/40">{f.hint}</p>
            </div>
          ))}
          {amountFields.map((f) => (
            <div key={f.key}>
              <label className="mb-2 block text-sm text-white/70">{f.label}</label>
              <div className="relative">
                <input
                  type="number"
                  dir="ltr"
                  value={draft[f.key] as number}
                  onChange={(e) => set(f.key, Number(e.target.value) as never)}
                  className={`${inputCls} text-left pl-12`}
                />
                <span className="absolute left-3 top-1/2 -translate-y-1/2 text-sm text-white/40">تومان</span>
              </div>
              <p className="mt-1.5 text-xs text-white/40">{formatToman(draft[f.key] as number)} · {f.hint}</p>
            </div>
          ))}
        </div>

        <div className="mt-6 flex items-center gap-3">
          <button
            onClick={save}
            disabled={saving}
            className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-8 text-sm font-bold text-white transition hover:brightness-110"
          >
            {saving ? <Spinner /> : "ذخیره تغییرات"}
          </button>
          {saved && <span className="text-sm font-medium text-emerald-400">✓ ذخیره شد</span>}
        </div>
      </Card>

      <Card className="h-fit p-6">
        <h3 className="mb-5 text-lg font-bold text-white">واحد و نمایش</h3>
        <label className="mb-2 block text-sm text-white/70">واحد پول</label>
        <select
          value={draft.currency}
          onChange={(e) => set("currency", e.target.value)}
          className={inputCls}
        >
          <option className="bg-[#15151f]">تومان</option>
          <option className="bg-[#15151f]">ریال</option>
        </select>

        <label className="mb-2 mt-5 block text-sm text-white/70">یادآوری تمدید اشتراک (ساعت پیش از انقضا)</label>
        <input
          type="number"
          dir="ltr"
          min={0}
          value={draft.subscriptionReminderHoursBefore}
          onChange={(e) => set("subscriptionReminderHoursBefore", Math.max(0, Number(e.target.value)) as never)}
          className={`${inputCls} text-left`}
        />
        <p className="mt-1.5 text-xs text-white/40">۰ = غیرفعال · پیش‌فرض ۴۸ ساعت</p>

        <label className="mt-5 flex cursor-pointer items-center justify-between">
          <span className="text-sm text-white/80">نمایش قیمت قبل از تخفیف</span>
          <Toggle checked={draft.showOriginalPrice} onChange={(v) => set("showOriginalPrice", v)} />
        </label>
      </Card>
    </div>
  );
}

function PlansPanel({
  plans,
  setPlans,
}: {
  plans: Plan[];
  setPlans: (updater: (prev: Plan[]) => Plan[]) => void;
}) {
  const [draft, setDraft] = useState<Record<number, Plan>>(() =>
    Object.fromEntries(plans.map((p) => [p.id, p])),
  );
  const [busy, setBusy] = useState<number | null>(null);
  const [adding, setAdding] = useState(false);

  const set = <K extends keyof Plan>(id: number, key: K, value: Plan[K]) =>
    setDraft((prev) => ({ ...prev, [id]: { ...prev[id], [key]: value } }));

  const dirty = (p: Plan) => {
    const d = draft[p.id];
    return d && (d.label !== p.label || d.months !== p.months || d.price !== p.price || d.discountPercent !== p.discountPercent);
  };

  async function save(p: Plan) {
    const d = draft[p.id];
    setBusy(p.id);
    try {
      const updated = await api.pricing.updatePlan(p.id, {
        label: d.label,
        months: d.months,
        price: d.price,
        discountPercent: d.discountPercent,
      });
      setPlans((prev) => prev.map((x) => (x.id === p.id ? updated : x)));
      setDraft((prev) => ({ ...prev, [p.id]: updated }));
    } finally {
      setBusy(null);
    }
  }

  async function remove(p: Plan) {
    setBusy(p.id);
    try {
      await api.pricing.removePlan(p.id);
      setPlans((prev) => prev.filter((x) => x.id !== p.id));
    } finally {
      setBusy(null);
    }
  }

  async function add() {
    setAdding(true);
    try {
      const created = await api.pricing.createPlan({ label: "پلن جدید", months: 1, price: 100000, discountPercent: 0 });
      setPlans((prev) => [...prev, created]);
      setDraft((prev) => ({ ...prev, [created.id]: created }));
    } finally {
      setAdding(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="grid gap-4 md:grid-cols-2">
        {plans.map((p) => {
          const d = draft[p.id] ?? p;
          const final = finalOf(d.price, d.discountPercent);
          return (
            <Card key={p.id} className="p-5">
              <div className="grid grid-cols-2 gap-4">
                <label>
                  <span className="mb-1.5 block text-xs text-white/45">عنوان</span>
                  <input value={d.label} onChange={(e) => set(p.id, "label", e.target.value)} className={`${inputCls} h-10`} />
                </label>
                <label>
                  <span className="mb-1.5 block text-xs text-white/45">مدت (ماه)</span>
                  <input type="number" dir="ltr" value={d.months} onChange={(e) => set(p.id, "months", Number(e.target.value))} className={`${inputCls} h-10 text-left`} />
                </label>
                <label>
                  <span className="mb-1.5 block text-xs text-white/45">قیمت</span>
                  <input type="number" dir="ltr" value={d.price} onChange={(e) => set(p.id, "price", Number(e.target.value))} className={`${inputCls} h-10 text-left`} />
                </label>
                <label>
                  <span className="mb-1.5 block text-xs text-white/45">تخفیف ٪</span>
                  <input type="number" dir="ltr" min={0} max={100} value={d.discountPercent} onChange={(e) => set(p.id, "discountPercent", Math.min(100, Math.max(0, Number(e.target.value))))} className={`${inputCls} h-10 text-left`} />
                </label>
              </div>

              <div className="mt-4 flex items-center justify-between border-t border-white/8 pt-4">
                <div>
                  <span className="text-xs text-white/45">قیمت نهایی</span>
                  <p className="text-lg font-bold text-emerald-400">{formatToman(final)}</p>
                </div>
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => save(p)}
                    disabled={!dirty(p) || busy === p.id}
                    className={`grid h-9 w-20 place-items-center rounded-lg text-sm font-bold transition ${
                      dirty(p) ? "bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-white hover:brightness-110" : "cursor-default border border-white/10 text-white/30"
                    }`}
                  >
                    {busy === p.id ? <Spinner /> : "ذخیره"}
                  </button>
                  <button
                    onClick={() => remove(p)}
                    className="grid h-9 w-9 place-items-center rounded-lg border border-white/10 text-white/55 transition hover:border-rose-500/50 hover:text-rose-400"
                  >
                    <AdminIcon name="trash" className="h-4 w-4" />
                  </button>
                </div>
              </div>
            </Card>
          );
        })}
      </div>

      <button
        onClick={add}
        disabled={adding}
        className="flex h-12 w-full items-center justify-center gap-2 rounded-2xl border border-dashed border-white/15 text-sm font-medium text-white/60 transition hover:border-[#e60053]/50 hover:text-white"
      >
        {adding ? <Spinner /> : <AdminIcon name="plus" className="h-4 w-4" />}
        افزودن پلن جدید
      </button>
    </div>
  );
}
