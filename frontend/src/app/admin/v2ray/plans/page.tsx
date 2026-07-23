"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { V2RayCategory, V2RayInbound, V2RayPanelInfo, V2RayPlan, V2RayPlanInput } from "@/lib/types";
import { formatToman } from "@/lib/format";
import { Card, Modal, PageHeader, Spinner, inputCls } from "@/components/admin/ui";
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
  // panelId -> inboundId -> protocol, so a plan card can show what it actually sells without another fetch.
  const [protocols, setProtocols] = useState<Record<number, Record<number, string>>>({});
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

      // One inbound read per panel (there are only a handful), best-effort: a panel that is unreachable just
      // leaves its plans without a protocol label rather than failing the page.
      const map: Record<number, Record<number, string>> = {};
      await Promise.all(
        pn.map(async (panel) => {
          try {
            const list = await api.v2ray.inbounds(panel.id);
            map[panel.id] = Object.fromEntries(list.map((i) => [i.id, i.protocol]));
          } catch {
            map[panel.id] = {};
          }
        }),
      );
      setProtocols(map);
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
          protocols={protocols}
          onEdit={setEditing}
          onChanged={load}
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

// Plans grouped by SERVER, mirroring how an operator thinks about them: each panel is a location and its
// plans sit under it. The card carries the whole spec at a glance plus the row of actions.
function PlanList({
  plans,
  categories,
  panels,
  protocols,
  onEdit,
  onChanged,
}: {
  plans: V2RayPlan[];
  categories: V2RayCategory[];
  panels: V2RayPanelInfo[];
  protocols: Record<number, Record<number, string>>;
  onEdit: (p: V2RayPlan) => void;
  onChanged: () => void;
}) {
  const [busyId, setBusyId] = useState<number | null>(null);

  const catName = (id: number) => categories.find((c) => c.id === id)?.name ?? "";
  const serverLabel = (panel: V2RayPanelInfo) => {
    if (panel.name.trim()) return panel.name;
    try {
      return new URL(panel.url).host;
    } catch {
      return panel.url;
    }
  };

  // The protocol a plan sells, read from the inbounds it is mapped to (e.g. "vless").
  const protocolOf = (p: V2RayPlan) => {
    const byInbound = protocols[p.panelId] ?? {};
    const found = [...new Set(p.inboundIds.map((id) => byInbound[id]).filter(Boolean))];
    return found.join(" · ");
  };

  const toInput = (p: V2RayPlan): V2RayPlanInput => ({
    categoryId: p.categoryId,
    title: p.title,
    description: p.description,
    panelId: p.panelId,
    inboundIds: p.inboundIds,
    protocol: p.protocol,
    network: p.network,
    volumeGb: p.volumeGb,
    durationDays: p.durationDays,
    ipLimit: p.ipLimit,
    quantity: p.quantity,
    price: p.price,
    discountPercent: p.discountPercent,
    active: p.active,
    sortOrder: p.sortOrder,
  });

  async function run(id: number, work: () => Promise<unknown>) {
    setBusyId(id);
    try {
      await work();
      onChanged();
    } catch {
      /* the list reloads on the next successful action */
    } finally {
      setBusyId(null);
    }
  }

  const toggleActive = (p: V2RayPlan) =>
    run(p.id, () => api.v2ray.plans.update(p.id, { ...toInput(p), active: !p.active }));

  const duplicate = (p: V2RayPlan) =>
    run(p.id, () => api.v2ray.plans.add({ ...toInput(p), title: p.title + " (کپی)", sortOrder: p.sortOrder + 1 }));

  const remove = (p: V2RayPlan) => {
    if (!confirm("پلن «" + p.title + "» حذف شود؟")) return;
    return run(p.id, () => api.v2ray.plans.remove(p.id));
  };

  // Reordering swaps this plan's position with its neighbour INSIDE the same server group, so a move never
  // jumps a plan onto another server.
  async function move(p: V2RayPlan, group: V2RayPlan[], delta: -1 | 1) {
    const i = group.findIndex((x) => x.id === p.id);
    const j = i + delta;
    if (i < 0 || j < 0 || j >= group.length) return;
    const other = group[j];
    await run(p.id, async () => {
      await api.v2ray.plans.update(p.id, { ...toInput(p), sortOrder: other.sortOrder });
      await api.v2ray.plans.update(other.id, { ...toInput(other), sortOrder: p.sortOrder });
    });
  }

  if (plans.length === 0) {
    return <Card className="p-12 text-center text-sm text-white/40">هنوز پلنی ساخته نشده است.</Card>;
  }

  const groups = panels
    .map((panel) => ({ panel, items: plans.filter((p) => p.panelId === panel.id) }))
    .filter((g) => g.items.length > 0);

  // Plans whose panel was deleted still need somewhere to appear so they can be fixed or removed.
  const orphans = plans.filter((p) => !panels.some((x) => x.id === p.panelId));

  return (
    <div className="space-y-8">
      {groups.map(({ panel, items }) => (
        <section key={panel.id}>
          <div className="mb-3 flex items-center justify-between gap-3">
            <div className="flex items-center gap-2">
              <span className={`h-2 w-2 rounded-full ${panel.lastCheckOk ? "bg-emerald-400" : "bg-white/25"}`} />
              <span className="text-sm font-bold text-white">{serverLabel(panel)}</span>
              {panel.flag && (
                <span className="rounded bg-white/10 px-1.5 py-0.5 text-[10px] font-bold text-white/60" dir="ltr">
                  {panel.flag}
                </span>
              )}
            </div>
            <span className="text-xs text-white/40">{fmt(items.length)} پلن</span>
          </div>

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5">
            {items.map((p) => (
              <PlanCard
                key={p.id}
                plan={p}
                protocol={protocolOf(p)}
                category={catName(p.categoryId)}
                busy={busyId === p.id}
                onEdit={() => onEdit(p)}
                onToggle={() => toggleActive(p)}
                onDuplicate={() => duplicate(p)}
                onRemove={() => remove(p)}
                onUp={() => move(p, items, -1)}
                onDown={() => move(p, items, 1)}
              />
            ))}
          </div>
        </section>
      ))}

      {orphans.length > 0 && (
        <section>
          <p className="mb-3 text-sm font-bold text-amber-300">پلن‌های بدون سرور (پنل حذف شده)</p>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5">
            {orphans.map((p) => (
              <PlanCard
                key={p.id}
                plan={p}
                protocol=""
                category={catName(p.categoryId)}
                busy={busyId === p.id}
                onEdit={() => onEdit(p)}
                onToggle={() => toggleActive(p)}
                onDuplicate={() => duplicate(p)}
                onRemove={() => remove(p)}
              />
            ))}
          </div>
        </section>
      )}
    </div>
  );
}

function PlanCard({
  plan,
  protocol,
  category,
  busy,
  onEdit,
  onToggle,
  onDuplicate,
  onRemove,
  onUp,
  onDown,
}: {
  plan: V2RayPlan;
  protocol: string;
  category: string;
  busy: boolean;
  onEdit: () => void;
  onToggle: () => void;
  onDuplicate: () => void;
  onRemove: () => void;
  onUp?: () => void;
  onDown?: () => void;
}) {
  const volume = plan.volumeGb === 0 ? "نامحدود" : fmt(plan.volumeGb) + " گیگ";
  const duration = plan.durationDays === 0 ? "بدون انقضا" : fmt(plan.durationDays) + " روز";

  return (
    <Card className={`flex flex-col p-4 transition ${busy ? "opacity-60" : ""} ${plan.active ? "" : "opacity-70"}`}>
      <div className="flex items-start justify-between gap-2">
        <span
          className={`rounded-md px-2 py-0.5 text-[10px] font-bold ${
            plan.active ? "bg-emerald-500/15 text-emerald-400" : "bg-white/10 text-white/45"
          }`}
        >
          {plan.active ? "فعال" : "غیرفعال"}
        </span>
        <div className="flex min-w-0 items-start gap-1.5 text-right">
          <span className="min-w-0">
            <span className="block truncate text-sm font-bold text-white">{plan.title}</span>
            {protocol && <span className="block text-xs text-white/45" dir="ltr">{protocol}</span>}
          </span>
          <AdminIcon name="layers" className="mt-0.5 h-4 w-4 shrink-0 text-white/35" />
        </div>
      </div>

      <p className="mt-3 text-right text-sm text-white/70">
        {duration} {volume}
      </p>
      <p className="mt-1 text-right text-[11px] text-white/35">
        {plan.ipLimit === 0 ? "IP نامحدود" : fmt(plan.ipLimit) + " کاربر"}
        {category && " · " + category}
        {" · " + formatToman(plan.finalPrice)}
      </p>

      <div className="mt-4 flex items-center justify-between gap-1 border-t border-white/8 pt-3">
        <ActionButton icon="trash" title="حذف" onClick={onRemove} disabled={busy} danger />
        <ActionButton icon="chevron-down" title="پایین‌تر" onClick={onDown} disabled={busy || !onDown} />
        <ActionButton icon="chevron-up" title="بالاتر" onClick={onUp} disabled={busy || !onUp} />
        <ActionButton
          icon="eye"
          title={plan.active ? "غیرفعال کن" : "فعال کن"}
          onClick={onToggle}
          disabled={busy}
          on={plan.active}
        />
        <ActionButton icon="copy" title="کپی" onClick={onDuplicate} disabled={busy} />
        <ActionButton icon="edit" title="ویرایش" onClick={onEdit} disabled={busy} />
      </div>
    </Card>
  );
}

function ActionButton({
  icon,
  title,
  onClick,
  disabled,
  danger,
  on,
}: {
  icon: string;
  title: string;
  onClick?: () => void;
  disabled?: boolean;
  danger?: boolean;
  on?: boolean;
}) {
  const tone = danger
    ? "bg-rose-500/15 text-rose-300 enabled:hover:bg-rose-500/25"
    : on
      ? "bg-emerald-500/15 text-emerald-400 enabled:hover:bg-emerald-500/25"
      : "text-white/45 enabled:hover:bg-white/8 enabled:hover:text-white";
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      onClick={onClick}
      disabled={disabled}
      className={`grid h-8 w-8 place-items-center rounded-full transition disabled:opacity-30 ${tone}`}
    >
      <AdminIcon name={icon} className="h-4 w-4" />
    </button>
  );
}

// The plan editor, laid out like the panel operators already know: a two-column grid whose reading order in
// RTL is نام | نوع پروتکل, نوع شبکه | قیمت, روز | حجم, محدودیت آی‌پی | اینباند, تعداد | دسته‌بندی, سرورها,
// then توضیحات across the bottom. Shown in a modal for both create and edit.
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
    protocol: plan?.protocol ?? "",
    network: plan?.network ?? "",
    volumeGb: plan?.volumeGb ?? 0,
    durationDays: plan?.durationDays ?? 30,
    ipLimit: plan?.ipLimit ?? 0,
    quantity: plan?.quantity ?? 0,
    price: plan?.price ?? 0,
    discountPercent: plan?.discountPercent ?? 0,
    active: plan?.active ?? true,
    sortOrder: plan?.sortOrder ?? 0,
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  // The chosen panel's inbounds, loaded live so the operator picks real locations rather than typing an id.
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

  // Picking an inbound fills the protocol from what the panel actually serves, so the labels start out
  // truthful; the operator can still override them.
  function toggleInbound(id: number) {
    setForm((f) => {
      const on = f.inboundIds.includes(id);
      const next = on ? f.inboundIds.filter((x) => x !== id) : [...f.inboundIds, id];
      const proto = !on && !f.protocol ? (inbounds?.find((i) => i.id === id)?.protocol ?? "") : f.protocol;
      return { ...f, inboundIds: next, protocol: proto };
    });
  }

  async function save() {
    setError("");
    if (!form.title.trim()) return setError("نام پلن را وارد کنید.");
    if (!form.categoryId) return setError("دسته‌بندی را انتخاب کنید.");
    if (!form.panelId) return setError("سرور را انتخاب کنید.");
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

  const panelLabel = (p: V2RayPanelInfo) => {
    if (p.name.trim()) return p.name + (p.flag ? " " + p.flag : "");
    try {
      return new URL(p.url).host;
    } catch {
      return p.url;
    }
  };

  const num = (v: number) => (v === 0 ? "" : String(v));

  return (
    <Modal open onClose={busy ? () => undefined : onClose} title={plan ? "ویرایش پلن" : "ایجاد پلن اشتراکی"} size="2xl">
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="نام">
          <input value={form.title} onChange={(e) => set("title", e.target.value)} className={inputCls} placeholder="۵ گیگ یک کاربر" />
        </Field>
        <Field label="نوع پروتکل">
          <input value={form.protocol} onChange={(e) => set("protocol", e.target.value)} dir="ltr" placeholder="vless" className={inputCls + " text-left"} />
        </Field>

        <Field label="نوع شبکه">
          <input value={form.network} onChange={(e) => set("network", e.target.value)} dir="ltr" placeholder="tcp" className={inputCls + " text-left"} />
        </Field>
        <Field label="قیمت (تومان)">
          <input value={num(form.price)} onChange={(e) => set("price", Math.max(0, Number(e.target.value) || 0))} dir="ltr" inputMode="numeric" className={inputCls + " text-left"} />
        </Field>

        <Field label="روز · ۰ = بدون انقضا">
          <input value={num(form.durationDays)} onChange={(e) => set("durationDays", Math.max(0, Number(e.target.value) || 0))} dir="ltr" inputMode="numeric" placeholder="30" className={inputCls + " text-left"} />
        </Field>
        <Field label="حجم (گیگ) · ۰ = نامحدود">
          <input value={num(form.volumeGb)} onChange={(e) => set("volumeGb", Math.max(0, Number(e.target.value) || 0))} dir="ltr" inputMode="numeric" placeholder="5" className={inputCls + " text-left"} />
        </Field>

        <Field label="محدودیت آی‌پی · ۰ = نامحدود">
          <input value={num(form.ipLimit)} onChange={(e) => set("ipLimit", Math.max(0, Number(e.target.value) || 0))} dir="ltr" inputMode="numeric" placeholder="2" className={inputCls + " text-left"} />
        </Field>
        <Field label="تعداد (موجودی) · ۰ = نامحدود">
          <input value={num(form.quantity)} onChange={(e) => set("quantity", Math.max(0, Number(e.target.value) || 0))} dir="ltr" inputMode="numeric" className={inputCls + " text-left"} />
        </Field>

        <Field label="دسته‌بندی">
          <select value={form.categoryId} onChange={(e) => set("categoryId", Number(e.target.value))} className={inputCls}>
            {categories.map((c) => <option key={c.id} value={c.id} className="bg-[#15151f]">{c.name}</option>)}
          </select>
        </Field>
        <Field label="سرور">
          <select
            value={form.panelId}
            onChange={(e) => {
              set("panelId", Number(e.target.value));
              set("inboundIds", []);
            }}
            className={inputCls}
          >
            {panels.map((p) => <option key={p.id} value={p.id} className="bg-[#15151f]">{panelLabel(p)}</option>)}
          </select>
        </Field>
      </div>

      {/* The inbound(s) this plan provisions on — WizWiz's "آیدی سطر کانکشن", but chosen from what the panel
          actually has rather than typed from memory. */}
      <div className="mt-4">
        <span className="mb-2 block text-xs font-medium text-white/55">آیدی سطر کانکشن (اینباند)</span>
        {inboundsErr ? (
          <p className="text-xs leading-6 text-rose-400">{inboundsErr}</p>
        ) : inbounds === null ? (
          <div className="flex items-center gap-2 text-xs text-white/45"><Spinner className="h-4 w-4" /> در حال خواندن اینباندها…</div>
        ) : inbounds.length === 0 ? (
          <p className="text-xs text-white/45">اینباندی روی این سرور نیست.</p>
        ) : (
          <div className="flex flex-wrap gap-2">
            {inbounds.map((ib) => {
              const on = form.inboundIds.includes(ib.id);
              return (
                <button
                  key={ib.id}
                  type="button"
                  onClick={() => toggleInbound(ib.id)}
                  disabled={!ib.enable}
                  className={
                    "flex items-center gap-2 rounded-lg border px-3 py-1.5 text-xs font-bold transition " +
                    (!ib.enable
                      ? "cursor-not-allowed border-white/8 text-white/30"
                      : on
                        ? "border-transparent bg-[#3a64f2]/20 text-[#8aa6ff]"
                        : "border-white/10 text-white/55 hover:text-white")
                  }
                >
                  <span className={"grid h-4 w-4 place-items-center rounded border text-[10px] " + (on ? "border-[#8aa6ff] bg-[#3a64f2]/40 text-white" : "border-white/25")}>
                    {on ? "✓" : ""}
                  </span>
                  {ib.remark || "اینباند " + fmt(ib.id)}
                  <span dir="ltr" className="text-white/35">#{ib.id}</span>
                </button>
              );
            })}
          </div>
        )}
      </div>

      <div className="mt-4">
        <span className="mb-1.5 block text-xs font-medium text-white/55">توضیحات</span>
        <textarea
          value={form.description}
          onChange={(e) => set("description", e.target.value)}
          rows={3}
          className="w-full rounded-xl border border-white/10 bg-[#0d0d15] px-3 py-2 text-sm leading-7 text-white outline-none focus:border-[#3a64f2]"
        />
      </div>

      {error && <p className="mt-3 text-sm leading-7 text-rose-400">{error}</p>}

      <div className="mt-5 flex items-center gap-2 border-t border-white/8 pt-4">
        <button
          onClick={save}
          disabled={busy}
          className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-8 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
        >
          {busy && <Spinner />}
          {plan ? "ذخیره" : "ایجاد"}
        </button>
        <button onClick={onClose} disabled={busy} className="h-11 rounded-xl border border-white/10 px-6 text-sm font-bold text-white/60 transition hover:bg-white/5 hover:text-white disabled:opacity-60">
          لغو
        </button>
      </div>
    </Modal>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-xs font-medium text-white/55">{label}</span>
      {children}
    </label>
  );
}
