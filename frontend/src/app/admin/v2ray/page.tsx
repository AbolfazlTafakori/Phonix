"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { V2RayInbound, V2RayPanelInfo, V2RayProvider, V2RayProviderInfo } from "@/lib/types";
import { Card, PageHeader, Spinner, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

// Owner-only page for wiring up the Xray/V2Ray panels the shop provisions accounts on. Phase one: add a
// panel through a two-step wizard (pick provider → enter URL + admin login, verified by a real sign-in),
// list configured panels, re-test and remove them.

const URL_HINT = "https://sub.example.com:8080/webpath";

export default function AdminV2RayPage() {
  const [providers, setProviders] = useState<V2RayProviderInfo[]>([]);
  const [panels, setPanels] = useState<V2RayPanelInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [adding, setAdding] = useState(false);

  async function load() {
    try {
      const [pv, pn] = await Promise.all([api.v2ray.providers(), api.v2ray.panels()]);
      setProviders(pv);
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
    return (
      <div className="grid place-items-center py-24">
        <Spinner className="h-8 w-8" />
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        title="تنظیمات پنل v2ray"
        desc="پنل‌های Xray که فروشگاه روی آن‌ها اکانت می‌سازد. فقط مالک به این بخش دسترسی دارد."
        action={
          !adding && (
            <button
              onClick={() => setAdding(true)}
              className="flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
            >
              <AdminIcon name="plus" className="h-4 w-4" />
              افزودن پنل جدید
            </button>
          )
        }
      />

      {error && !adding && <Card className="mb-5 p-5 text-center text-rose-400">{error}</Card>}

      {adding ? (
        <AddPanelWizard
          providers={providers}
          onCancel={() => setAdding(false)}
          onAdded={(panel) => {
            setPanels((p) => [...p, panel]);
            setAdding(false);
          }}
        />
      ) : panels.length === 0 ? (
        <Card className="p-12 text-center">
          <div className="mx-auto mb-4 grid h-14 w-14 place-items-center rounded-2xl bg-white/5 text-white/40">
            <AdminIcon name="cpu" className="h-7 w-7" />
          </div>
          <p className="font-bold text-white">هنوز پنلی اضافه نشده است</p>
          <p className="mx-auto mt-1.5 max-w-md text-sm text-white/45">
            برای فروش سرویس V2Ray، ابتدا پنل Xray خود را اضافه کنید تا فروشگاه بتواند روی آن اکانت بسازد.
          </p>
          <button
            onClick={() => setAdding(true)}
            className="mt-5 inline-flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110"
          >
            <AdminIcon name="plus" className="h-4 w-4" />
            افزودن پنل جدید
          </button>
        </Card>
      ) : (
        <div className="space-y-3">
          {panels.map((panel) => (
            <PanelRow
              key={panel.id}
              panel={panel}
              providers={providers}
              onChange={(next) => setPanels((p) => p.map((x) => (x.id === next.id ? next : x)))}
              onRemove={() => setPanels((p) => p.filter((x) => x.id !== panel.id))}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function providerName(providers: V2RayProviderInfo[], provider: V2RayProvider): string {
  return providers.find((p) => p.provider === provider)?.name ?? provider;
}

function PanelRow({
  panel,
  providers,
  onChange,
  onRemove,
}: {
  panel: V2RayPanelInfo;
  providers: V2RayProviderInfo[];
  onChange: (p: V2RayPanelInfo) => void;
  onRemove: () => void;
}) {
  const [busy, setBusy] = useState<"test" | "delete" | null>(null);
  const [msg, setMsg] = useState("");
  const [msgOk, setMsgOk] = useState(false);
  const [creating, setCreating] = useState(false);
  const [inbounds, setInbounds] = useState<V2RayInbound[] | null>(null);
  const [inboundsBusy, setInboundsBusy] = useState(false);
  const [inboundsErr, setInboundsErr] = useState("");

  async function toggleInbounds() {
    if (inbounds !== null) {
      setInbounds(null);
      return;
    }
    setInboundsBusy(true);
    setInboundsErr("");
    try {
      setInbounds(await api.v2ray.inbounds(panel.id));
    } catch (e) {
      setInboundsErr(e instanceof Error ? e.message : "خواندن اینباندها ناموفق بود");
    } finally {
      setInboundsBusy(false);
    }
  }

  async function test() {
    setBusy("test");
    setMsg("");
    try {
      const r = await api.v2ray.testStored(panel.id);
      setMsgOk(true);
      setMsg(`اتصال موفق بود · ${formatNumber(r.inboundCount)} اینباند`);
      onChange({ ...panel, lastCheckOk: true, lastCheckError: "", inboundCount: r.inboundCount, lastCheckAtUtc: new Date().toISOString() });
    } catch (e) {
      setMsgOk(false);
      const text = e instanceof Error ? e.message : "اتصال ناموفق بود";
      setMsg(text);
      onChange({ ...panel, lastCheckOk: false, lastCheckError: text, lastCheckAtUtc: new Date().toISOString() });
    } finally {
      setBusy(null);
    }
  }

  async function remove() {
    if (!confirm("این پنل حذف شود؟ اکانت‌هایی که روی خود پنل ساخته شده‌اند حذف نمی‌شوند.")) return;
    setBusy("delete");
    try {
      await api.v2ray.remove(panel.id);
      onRemove();
    } catch (e) {
      setMsgOk(false);
      setMsg(e instanceof Error ? e.message : "حذف ناموفق بود");
      setBusy(null);
    }
  }

  return (
    <Card className="p-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <span className="rounded-lg bg-[#3a64f2]/15 px-2.5 py-1 text-xs font-bold text-[#8aa6ff]">
              {providerName(providers, panel.provider)}
            </span>
            <StatusPill ok={panel.lastCheckOk} hasChecked={Boolean(panel.lastCheckAtUtc)} />
          </div>
          <p dir="ltr" className="mt-2 break-all text-right text-sm font-bold text-white">{panel.url}</p>
          <p className="mt-1 text-xs text-white/45">
            کاربر: <span dir="ltr">{panel.username}</span>
            {panel.lastCheckOk && ` · ${formatNumber(panel.inboundCount)} اینباند`}
          </p>
          {!panel.lastCheckOk && panel.lastCheckError && (
            <p className="mt-1.5 text-xs leading-6 text-rose-400">{panel.lastCheckError}</p>
          )}
        </div>

        <div className="flex shrink-0 items-center gap-2">
          <button
            onClick={toggleInbounds}
            disabled={busy !== null}
            className="flex h-9 items-center gap-2 rounded-lg border border-white/10 px-3.5 text-xs font-bold text-white/70 transition hover:bg-white/5 hover:text-white disabled:opacity-60"
          >
            {inboundsBusy ? <Spinner className="h-4 w-4" /> : <AdminIcon name="grid" className="h-4 w-4" />}
            {inbounds !== null ? "بستن اینباندها" : "مشاهده اینباندها"}
          </button>
          <button
            onClick={() => setCreating((v) => !v)}
            disabled={busy !== null}
            className="flex h-9 items-center gap-2 rounded-lg border border-white/10 px-3.5 text-xs font-bold text-[#8aa6ff] transition hover:bg-white/5 disabled:opacity-60"
          >
            <AdminIcon name="plus" className="h-4 w-4" />
            ساخت اکانت تست
          </button>
          <button
            onClick={test}
            disabled={busy !== null}
            className="flex h-9 items-center gap-2 rounded-lg border border-white/10 px-3.5 text-xs font-bold text-white/70 transition hover:bg-white/5 hover:text-white disabled:opacity-60"
          >
            {busy === "test" ? <Spinner className="h-4 w-4" /> : <AdminIcon name="refresh" className="h-4 w-4" />}
            تست اتصال
          </button>
          <button
            onClick={remove}
            disabled={busy !== null}
            className="flex h-9 items-center gap-2 rounded-lg border border-white/10 px-3.5 text-xs font-bold text-rose-300/80 transition hover:bg-rose-500/10 hover:text-rose-300 disabled:opacity-60"
          >
            <AdminIcon name="trash" className="h-4 w-4" />
            حذف
          </button>
        </div>
      </div>
      {msg && <p className={`mt-3 text-xs leading-6 ${msgOk ? "text-emerald-400" : "text-rose-400"}`}>{msg}</p>}
      {inboundsErr && <p className="mt-3 text-xs leading-6 text-rose-400">{inboundsErr}</p>}

      {inbounds !== null && (
        <div className="mt-4 rounded-xl border border-white/10 bg-white/[0.02] p-4">
          <p className="mb-3 text-sm font-bold text-white">اینباندها / لوکیشن‌های پنل ({formatNumber(inbounds.length)})</p>
          {inbounds.length === 0 ? (
            <p className="text-xs text-white/45">هیچ اینباندی روی این پنل نیست.</p>
          ) : (
            <ul className="divide-y divide-white/5">
              {inbounds.map((ib) => (
                <li key={ib.id} className="flex items-center justify-between gap-3 py-2.5">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-bold text-white">
                      {ib.remark || `اینباند ${formatNumber(ib.id)}`}
                    </p>
                    <p className="mt-0.5 text-[11px] text-white/40" dir="ltr">
                      #{ib.id} · {ib.protocol} · :{ib.port}
                    </p>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    <span className="rounded-full bg-white/10 px-2 py-0.5 text-[11px] font-bold text-white/60">
                      {formatNumber(ib.clientCount)} کاربر
                    </span>
                    {ib.enable ? (
                      <span className="rounded-full bg-emerald-500/15 px-2 py-0.5 text-[11px] font-bold text-emerald-400">فعال</span>
                    ) : (
                      <span className="rounded-full bg-white/10 px-2 py-0.5 text-[11px] font-bold text-white/45">غیرفعال</span>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {creating && <CreateClientForm panelId={panel.id} onClose={() => setCreating(false)} />}
    </Card>
  );
}

// Manually create an account on this panel — the same call order fulfilment will make. Zero means unlimited
// for traffic, IP limit and duration (matching the panel). Duration is in days: a month is 30 days here, a
// year 365, so the presets follow the plan rules.
function CreateClientForm({ panelId, onClose }: { panelId: number; onClose: () => void }) {
  const [email, setEmail] = useState("");
  const [totalGb, setTotalGb] = useState("0");
  const [limitIp, setLimitIp] = useState("0");
  const [durationDays, setDurationDays] = useState("30");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [result, setResult] = useState<{ uuid: string; subId: string; inboundsAdded: number } | null>(null);

  // Which inbound(s) to create on — exactly what a plan will store. Loaded once when the form opens.
  const [inbounds, setInbounds] = useState<V2RayInbound[] | null>(null);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [inboundsErr, setInboundsErr] = useState("");

  useEffect(() => {
    api.v2ray
      .inbounds(panelId)
      .then((list) => {
        setInbounds(list);
        // Pre-select every enabled inbound so a quick test works out of the box; the operator narrows it.
        setSelected(new Set(list.filter((i) => i.enable).map((i) => i.id)));
      })
      .catch((e) => setInboundsErr(e instanceof Error ? e.message : "خواندن اینباندها ناموفق بود"));
  }, [panelId]);

  function toggle(id: number) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const presets = [
    { label: "۱ ماه", days: 30 },
    { label: "۲ ماه", days: 60 },
    { label: "۳ ماه", days: 90 },
    { label: "۶ ماه", days: 180 },
    { label: "۱ سال", days: 365 },
    { label: "نامحدود", days: 0 },
  ];

  async function submit() {
    setError("");
    setResult(null);
    if (!email.trim()) {
      setError("نام (Email) اکانت را وارد کنید.");
      return;
    }
    if (selected.size === 0) {
      setError("حداقل یک اینباند (لوکیشن) انتخاب کنید.");
      return;
    }
    setBusy(true);
    try {
      const r = await api.v2ray.addClient(panelId, {
        email: email.trim(),
        totalGb: Math.max(0, Number(totalGb) || 0),
        limitIp: Math.max(0, Number(limitIp) || 0),
        durationDays: Math.max(0, Number(durationDays) || 0),
        inboundIds: [...selected],
      });
      setResult({ uuid: r.uuid, subId: r.subId, inboundsAdded: r.inboundsAdded });
    } catch (e) {
      setError(e instanceof Error ? e.message : "ساخت اکانت ناموفق بود");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mt-4 rounded-xl border border-white/10 bg-white/[0.02] p-4">
      <div className="mb-3 flex items-center justify-between">
        <p className="text-sm font-bold text-white">ساخت اکانت آزمایشی</p>
        <button onClick={onClose} className="text-xs text-white/45 transition hover:text-white">بستن</button>
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
        <label className="block sm:col-span-3">
          <span className="mb-1.5 block text-xs font-medium text-white/55">نام اکانت (Email)</span>
          <input value={email} onChange={(e) => setEmail(e.target.value)} dir="ltr" placeholder="reza-1m" className={`${inputCls} text-left`} />
        </label>
        <label className="block">
          <span className="mb-1.5 block text-xs font-medium text-white/55">حجم (گیگابایت) · ۰ = نامحدود</span>
          <input value={totalGb} onChange={(e) => setTotalGb(e.target.value)} dir="ltr" inputMode="numeric" className={`${inputCls} text-left`} />
        </label>
        <label className="block">
          <span className="mb-1.5 block text-xs font-medium text-white/55">محدودیت IP · ۰ = نامحدود</span>
          <input value={limitIp} onChange={(e) => setLimitIp(e.target.value)} dir="ltr" inputMode="numeric" className={`${inputCls} text-left`} />
        </label>
        <label className="block">
          <span className="mb-1.5 block text-xs font-medium text-white/55">مدت (روز) · خالی/۰ = نامحدود</span>
          <input value={durationDays} onChange={(e) => setDurationDays(e.target.value)} dir="ltr" inputMode="numeric" className={`${inputCls} text-left`} />
        </label>
      </div>

      <div className="mt-2.5 flex flex-wrap gap-1.5">
        {presets.map((p) => (
          <button
            key={p.label}
            onClick={() => setDurationDays(String(p.days))}
            className={`rounded-lg border px-2.5 py-1 text-[11px] font-bold transition ${
              Number(durationDays) === p.days ? "border-transparent bg-[#3a64f2]/20 text-[#8aa6ff]" : "border-white/10 text-white/55 hover:text-white"
            }`}
          >
            {p.label}
          </button>
        ))}
      </div>

      {/* Inbound (location) selection — the account is created ONLY on the checked ones, exactly like a plan
          would specify. Not "select all" any more. */}
      <div className="mt-4">
        <p className="mb-2 text-xs font-medium text-white/55">اینباند(ها) / لوکیشنی که اکانت روی آن ساخته شود</p>
        {inboundsErr ? (
          <p className="text-xs leading-6 text-rose-400">{inboundsErr}</p>
        ) : inbounds === null ? (
          <div className="flex items-center gap-2 text-xs text-white/45"><Spinner className="h-4 w-4" /> در حال خواندن اینباندها…</div>
        ) : inbounds.length === 0 ? (
          <p className="text-xs text-white/45">اینباندی روی این پنل نیست.</p>
        ) : (
          <div className="flex flex-wrap gap-2">
            {inbounds.map((ib) => {
              const on = selected.has(ib.id);
              return (
                <button
                  key={ib.id}
                  onClick={() => toggle(ib.id)}
                  disabled={!ib.enable}
                  className={`flex items-center gap-2 rounded-lg border px-3 py-1.5 text-xs font-bold transition ${
                    !ib.enable
                      ? "cursor-not-allowed border-white/8 text-white/30"
                      : on
                        ? "border-transparent bg-[#3a64f2]/20 text-[#8aa6ff]"
                        : "border-white/10 text-white/55 hover:text-white"
                  }`}
                >
                  <span className={`grid h-4 w-4 place-items-center rounded border text-[10px] ${on ? "border-[#8aa6ff] bg-[#3a64f2]/40 text-white" : "border-white/25"}`}>
                    {on ? "✓" : ""}
                  </span>
                  {ib.remark || `اینباند ${formatNumber(ib.id)}`}
                  <span dir="ltr" className="text-white/35">#{ib.id}</span>
                </button>
              );
            })}
          </div>
        )}
      </div>

      {error && <p className="mt-3 text-xs leading-6 text-rose-400">{error}</p>}

      {result && (
        <div className="mt-3 space-y-1.5 rounded-lg border border-emerald-500/20 bg-emerald-500/[0.06] p-3 text-xs text-emerald-300">
          <p className="font-bold">اکانت روی {formatNumber(result.inboundsAdded)} اینباند ساخته شد.</p>
          <p dir="ltr" className="break-all text-emerald-300/80">UUID: {result.uuid}</p>
          <p dir="ltr" className="break-all text-emerald-300/80">subId: {result.subId}</p>
        </div>
      )}

      <button
        onClick={submit}
        disabled={busy}
        className="mt-3 flex h-10 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
      >
        {busy && <Spinner />}
        ساخت اکانت
      </button>
    </div>
  );
}

function StatusPill({ ok, hasChecked }: { ok: boolean; hasChecked: boolean }) {
  if (!hasChecked) return <span className="rounded-full bg-white/10 px-2.5 py-1 text-[11px] font-bold text-white/50">بررسی‌نشده</span>;
  return ok ? (
    <span className="rounded-full bg-emerald-500/15 px-2.5 py-1 text-[11px] font-bold text-emerald-400">متصل</span>
  ) : (
    <span className="rounded-full bg-rose-500/15 px-2.5 py-1 text-[11px] font-bold text-rose-400">اتصال ناموفق</span>
  );
}

function AddPanelWizard({
  providers,
  onCancel,
  onAdded,
}: {
  providers: V2RayProviderInfo[];
  onCancel: () => void;
  onAdded: (panel: V2RayPanelInfo) => void;
}) {
  const [step, setStep] = useState<1 | 2>(1);
  const [provider, setProvider] = useState<V2RayProvider | null>(null);
  const [url, setUrl] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [apiToken, setApiToken] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [okMsg, setOkMsg] = useState("");

  const selectedName = useMemo(() => (provider ? providers.find((p) => p.provider === provider)?.name ?? provider : ""), [provider, providers]);

  async function save() {
    if (!provider) return;
    setError("");
    setOkMsg("");
    if (!url.trim() || (!apiToken.trim() && (!username.trim() || !password))) {
      setError("آدرس پنل و سپس توکن API یا نام کاربری و گذرواژه را وارد کنید.");
      return;
    }
    setBusy(true);
    try {
      // The backend connects to the panel and reads its inbounds before it will save, so a successful add is
      // proof the connection actually works.
      const panel = await api.v2ray.add({ provider, url: url.trim(), username: username.trim(), password, apiToken: apiToken.trim() });
      onAdded(panel);
    } catch (e) {
      setError(e instanceof Error ? e.message : "افزودن پنل ناموفق بود");
    } finally {
      setBusy(false);
    }
  }

  async function testOnly() {
    if (!provider) return;
    setError("");
    setOkMsg("");
    if (!url.trim() || (!apiToken.trim() && (!username.trim() || !password))) {
      setError("آدرس پنل و سپس توکن API یا نام کاربری و گذرواژه را وارد کنید.");
      return;
    }
    setBusy(true);
    try {
      const r = await api.v2ray.test({ provider, url: url.trim(), username: username.trim(), password, apiToken: apiToken.trim() });
      setOkMsg(`اتصال موفق بود · ${formatNumber(r.inboundCount)} اینباند پیدا شد. حالا می‌توانید ذخیره کنید.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "اتصال ناموفق بود");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card className="p-5 sm:p-6">
      <div className="mb-5 flex items-center justify-between">
        <div className="flex items-center gap-2 text-sm">
          <StepDot n={1} active={step === 1} done={step > 1} label="نوع پنل" />
          <span className="h-px w-8 bg-white/10" />
          <StepDot n={2} active={step === 2} done={false} label="اطلاعات ورود" />
        </div>
        <button onClick={onCancel} className="text-sm text-white/50 transition hover:text-white">انصراف</button>
      </div>

      {step === 1 ? (
        <div>
          <p className="mb-4 text-sm text-white/70">نوع پنلی که می‌خواهید اضافه کنید را انتخاب کنید:</p>
          <div className="grid gap-3 sm:grid-cols-2">
            {providers.map((p) => (
              <button
                key={p.provider}
                disabled={!p.available}
                onClick={() => setProvider(p.provider)}
                className={`flex items-center justify-between gap-3 rounded-2xl border p-4 text-right transition ${
                  !p.available
                    ? "cursor-not-allowed border-white/8 opacity-50"
                    : provider === p.provider
                      ? "border-[#3a64f2] bg-[#3a64f2]/10"
                      : "border-white/10 hover:border-white/25 hover:bg-white/5"
                }`}
              >
                <span className="flex items-center gap-3">
                  <span className="grid h-10 w-10 place-items-center rounded-xl bg-white/5 text-white/70">
                    <AdminIcon name="cpu" className="h-5 w-5" />
                  </span>
                  <span className="font-bold text-white">{p.name}</span>
                </span>
                {!p.available ? (
                  <span className="rounded-full bg-white/10 px-2.5 py-1 text-[11px] font-bold text-white/50">به‌زودی</span>
                ) : provider === p.provider ? (
                  <AdminIcon name="check" className="h-5 w-5 text-[#8aa6ff]" />
                ) : null}
              </button>
            ))}
          </div>
          <div className="mt-6 flex justify-end">
            <button
              disabled={!provider}
              onClick={() => setStep(2)}
              className="rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-7 py-2.5 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-50"
            >
              بعدی
            </button>
          </div>
        </div>
      ) : (
        <div className="space-y-4">
          <p className="text-sm text-white/70">
            اطلاعات پنل <span className="font-bold text-white">{selectedName}</span> را وارد کنید. فروشگاه با همین آدرس و
            اطلاعات ورود وارد پنل می‌شود و اکانت‌ها را می‌سازد.
          </p>

          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-white/55">آدرس کامل پنل (URL)</span>
            <input
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              dir="ltr"
              placeholder={URL_HINT}
              className={`${inputCls} text-left`}
            />
            <span className="mt-1.5 block text-[11px] leading-5 text-white/40">
              با http یا https، همراه پورت و وب‌پس (در صورت وجود). نمونه: <span dir="ltr">{URL_HINT}</span>
            </span>
          </label>

          {/* Preferred path. A token skips the panel's CSRF/session handshake entirely, which is what the
              username/password route has to negotiate on every call. */}
          <label className="block rounded-xl border border-[#3a64f2]/25 bg-[#3a64f2]/[0.06] p-4">
            <span className="mb-1.5 block text-xs font-bold text-[#8aa6ff]">توکن API پنل (روش پیشنهادی)</span>
            <input
              value={apiToken}
              onChange={(e) => setApiToken(e.target.value)}
              dir="ltr"
              autoComplete="off"
              placeholder="از پنل: Settings → Security → API Token"
              className={`${inputCls} text-left`}
            />
            <span className="mt-1.5 block text-[11px] leading-5 text-white/45">
              اگر توکن بدهید، نیازی به نام کاربری و گذرواژه نیست و اتصال پایدارتر است. در نسخه ۳.۴ پنل،
              درخواست‌های دارای توکن از سد محافظ CSRF عبور می‌کنند و خطای ۴۰۳ رخ نمی‌دهد.
            </span>
          </label>

          <p className="text-center text-[11px] text-white/35">— یا با نام کاربری و گذرواژه —</p>

          <div className="grid gap-4 sm:grid-cols-2">
            <label className="block">
              <span className="mb-1.5 block text-xs font-medium text-white/55">نام کاربری پنل</span>
              <input value={username} onChange={(e) => setUsername(e.target.value)} dir="ltr" autoComplete="off" className={`${inputCls} text-left`} />
            </label>
            <label className="block">
              <span className="mb-1.5 block text-xs font-medium text-white/55">گذرواژه پنل</span>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                dir="ltr"
                autoComplete="new-password"
                className={`${inputCls} text-left`}
              />
            </label>
          </div>

          <p className="rounded-xl border border-white/8 bg-white/[0.02] px-4 py-3 text-[11px] leading-6 text-white/45">
            گذرواژه به‌صورت رمزنگاری‌شده ذخیره می‌شود و هیچ‌گاه به مرورگر بازگردانده نمی‌شود. این اطلاعات فقط سمت سرور و
            برای ورود به پنل شما استفاده می‌شود.
          </p>

          {error && <p className="text-sm leading-7 text-rose-400">{error}</p>}
          {okMsg && <p className="text-sm leading-7 text-emerald-400">{okMsg}</p>}

          <div className="flex flex-wrap items-center gap-2 border-t border-white/8 pt-4">
            <button
              onClick={save}
              disabled={busy}
              className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-7 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
            >
              {busy && <Spinner />}
              ورود و ذخیره پنل
            </button>
            <button
              onClick={testOnly}
              disabled={busy}
              className="flex h-11 items-center gap-2 rounded-xl border border-white/10 px-5 text-sm font-bold text-white/65 transition hover:bg-white/5 hover:text-white disabled:opacity-60"
            >
              تست اتصال
            </button>
            <button onClick={() => setStep(1)} disabled={busy} className="mr-auto text-sm text-white/45 transition hover:text-white disabled:opacity-60">
              → بازگشت
            </button>
          </div>
        </div>
      )}
    </Card>
  );
}

function StepDot({ n, active, done, label }: { n: number; active: boolean; done: boolean; label: string }) {
  return (
    <span className="flex items-center gap-2">
      <span
        className={`grid h-7 w-7 place-items-center rounded-full text-xs font-bold transition ${
          done ? "bg-emerald-500/20 text-emerald-400" : active ? "bg-[#3a64f2] text-white" : "bg-white/10 text-white/50"
        }`}
      >
        {done ? "✓" : n}
      </span>
      <span className={`text-xs font-bold ${active || done ? "text-white" : "text-white/45"}`}>{label}</span>
    </span>
  );
}

function formatNumber(n: number): string {
  return n.toLocaleString("fa-IR");
}
