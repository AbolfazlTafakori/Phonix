"use client";

import { useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { WireGuardInterface, WireGuardPanelInfo, WireGuardProvider, WireGuardProviderInfo } from "@/lib/types";
import { Card, PageHeader, Spinner, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

// Owner-only page for wiring up the W-UI panels (WireGuard / AmneziaWG / OpenVPN) the shop provisions
// customers on. Same shape as the V2Ray page: add a panel through a two-step wizard (pick provider → enter
// URL + credentials, verified by a real call), list configured panels, view their tunnels, create a test
// customer, re-test, edit and remove.

const URL_HINT = "https://1.2.3.4:41873/webBasePath";

export default function AdminWireGuardPage() {
  const [providers, setProviders] = useState<WireGuardProviderInfo[]>([]);
  const [panels, setPanels] = useState<WireGuardPanelInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [adding, setAdding] = useState(false);

  async function load() {
    try {
      const [pv, pn] = await Promise.all([api.wireguard.providers(), api.wireguard.panels()]);
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
        title="تنظیمات پنل WireGuard (W-UI)"
        desc="پنل‌های W-UI که فروشگاه روی آن‌ها مشتری وایرگارد / AmneziaWG / OpenVPN می‌سازد. فقط مالک به این بخش دسترسی دارد."
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
            برای فروش سرویس WireGuard، ابتدا پنل W-UI خود را اضافه کنید تا فروشگاه بتواند روی آن مشتری بسازد.
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

function providerName(providers: WireGuardProviderInfo[], provider: WireGuardProvider): string {
  return providers.find((p) => p.provider === provider)?.name ?? provider;
}

// A tunnel's label: "wg0 · WireGuard (AmneziaWG)" — the panel's own name plus what it runs.
function protocolLabel(i: { protocol: string; mode: string }): string {
  if (i.protocol === "wireguard") return i.mode === "amnezia" ? "AmneziaWG" : "WireGuard";
  if (i.protocol === "openvpn") return "OpenVPN";
  return i.protocol;
}

function PanelRow({
  panel,
  providers,
  onChange,
  onRemove,
}: {
  panel: WireGuardPanelInfo;
  providers: WireGuardProviderInfo[];
  onChange: (p: WireGuardPanelInfo) => void;
  onRemove: () => void;
}) {
  const [busy, setBusy] = useState<"test" | "delete" | null>(null);
  const [msg, setMsg] = useState("");
  const [msgOk, setMsgOk] = useState(false);
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState(false);
  const [interfaces, setInterfaces] = useState<WireGuardInterface[] | null>(null);
  const [interfacesBusy, setInterfacesBusy] = useState(false);
  const [interfacesErr, setInterfacesErr] = useState("");

  async function toggleInterfaces() {
    if (interfaces !== null) {
      setInterfaces(null);
      return;
    }
    setInterfacesBusy(true);
    setInterfacesErr("");
    try {
      setInterfaces(await api.wireguard.interfaces(panel.id));
    } catch (e) {
      setInterfacesErr(e instanceof Error ? e.message : "خواندن تانل‌ها ناموفق بود");
    } finally {
      setInterfacesBusy(false);
    }
  }

  async function test() {
    setBusy("test");
    setMsg("");
    try {
      const r = await api.wireguard.testStored(panel.id);
      setMsgOk(true);
      setMsg(`اتصال موفق بود · ${formatNumber(r.interfaceCount)} تانل${r.version ? ` · نسخه ${r.version}` : ""}`);
      onChange({
        ...panel,
        lastCheckOk: true,
        lastCheckError: "",
        interfaceCount: r.interfaceCount,
        panelVersion: r.version || panel.panelVersion,
        lastCheckAtUtc: new Date().toISOString(),
      });
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
    if (!confirm("این پنل حذف شود؟ مشتری‌هایی که روی خود پنل ساخته شده‌اند حذف نمی‌شوند.")) return;
    setBusy("delete");
    try {
      await api.wireguard.remove(panel.id);
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
            {panel.panelVersion && (
              <span dir="ltr" className="rounded-full bg-white/10 px-2 py-0.5 text-[11px] font-bold text-white/50">v{panel.panelVersion}</span>
            )}
          </div>
          <p className="mt-2 truncate text-sm font-bold text-white">{panel.name || providerName(providers, panel.provider)}</p>
          <p dir="ltr" className="mt-0.5 break-all text-right text-xs text-white/40">{panel.url}</p>
          <p className="mt-1 text-xs text-white/45">
            {panel.hasApiToken ? "اتصال با توکن دسترسی" : <>کاربر: <span dir="ltr">{panel.username}</span></>}
            {panel.lastCheckOk && ` · ${formatNumber(panel.interfaceCount)} تانل`}
          </p>
          {!panel.lastCheckOk && panel.lastCheckError && (
            <p className="mt-1.5 text-xs leading-6 text-rose-400">{panel.lastCheckError}</p>
          )}
        </div>

        <div className="flex shrink-0 flex-wrap items-center justify-end gap-2">
          <button
            onClick={toggleInterfaces}
            disabled={busy !== null}
            className="flex h-9 items-center gap-2 rounded-lg border border-white/10 px-3.5 text-xs font-bold text-white/70 transition hover:bg-white/5 hover:text-white disabled:opacity-60"
          >
            {interfacesBusy ? <Spinner className="h-4 w-4" /> : <AdminIcon name="grid" className="h-4 w-4" />}
            {interfaces !== null ? "بستن تانل‌ها" : "مشاهده تانل‌ها"}
          </button>
          <button
            onClick={() => setCreating((v) => !v)}
            disabled={busy !== null}
            className="flex h-9 items-center gap-2 rounded-lg border border-white/10 px-3.5 text-xs font-bold text-[#8aa6ff] transition hover:bg-white/5 disabled:opacity-60"
          >
            <AdminIcon name="plus" className="h-4 w-4" />
            ساخت مشتری تست
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
            onClick={() => setEditing((v) => !v)}
            disabled={busy !== null}
            className="flex h-9 items-center gap-2 rounded-lg border border-white/10 px-3.5 text-xs font-bold text-white/70 transition hover:bg-white/5 hover:text-white disabled:opacity-60"
          >
            <AdminIcon name="edit" className="h-4 w-4" />
            ویرایش
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
      {interfacesErr && <p className="mt-3 text-xs leading-6 text-rose-400">{interfacesErr}</p>}

      {interfaces !== null && (
        <div className="mt-4 rounded-xl border border-white/10 bg-white/[0.02] p-4">
          <p className="mb-3 text-sm font-bold text-white">تانل‌ها / سرورهای پنل ({formatNumber(interfaces.length)})</p>
          {interfaces.length === 0 ? (
            <p className="text-xs text-white/45">هیچ تانلی روی این پنل نیست.</p>
          ) : (
            <ul className="divide-y divide-white/5">
              {interfaces.map((i) => (
                <li key={i.id} className="flex items-center justify-between gap-3 py-2.5">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-bold text-white">
                      {i.name || `تانل ${formatNumber(i.id)}`}
                      <span className="mr-2 text-xs font-medium text-white/45">{protocolLabel(i)}</span>
                    </p>
                    <p className="mt-0.5 text-[11px] text-white/40" dir="ltr">
                      #{i.id} · {i.endpointHost}:{i.listenPort}
                      {i.nodeName && ` · ${i.nodeName}`}
                    </p>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    <span className="rounded-full bg-white/10 px-2 py-0.5 text-[11px] font-bold text-white/60">
                      {formatNumber(i.clients)} مشتری · {formatNumber(i.devices)} دستگاه
                    </span>
                    {i.capacity > 0 && (
                      <span className="rounded-full bg-white/10 px-2 py-0.5 text-[11px] font-bold text-white/50" dir="ltr">
                        {formatNumber(i.allocated)}/{formatNumber(i.capacity)}
                      </span>
                    )}
                    {!i.enabled ? (
                      <span className="rounded-full bg-white/10 px-2 py-0.5 text-[11px] font-bold text-white/45">غیرفعال</span>
                    ) : i.running ? (
                      <span className="rounded-full bg-emerald-500/15 px-2 py-0.5 text-[11px] font-bold text-emerald-400">در حال اجرا</span>
                    ) : (
                      <span className="rounded-full bg-amber-500/15 px-2 py-0.5 text-[11px] font-bold text-amber-300">بالا نیامده</span>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {creating && <CreateClientForm panelId={panel.id} onClose={() => setCreating(false)} />}

      {editing && (
        <EditPanelForm
          panel={panel}
          providers={providers}
          onClose={() => setEditing(false)}
          onSaved={(next) => {
            onChange(next);
            setEditing(false);
          }}
        />
      )}
    </Card>
  );
}

// The shared credential + identity fields of the add wizard and the edit form.
type PanelDraft = {
  url: string;
  username: string;
  password: string;
  apiToken: string;
  name: string;
  remark: string;
  flag: string;
  capacity: string;
};

function IdentityFields({ draft, set }: { draft: PanelDraft; set: <K extends keyof PanelDraft>(k: K, v: PanelDraft[K]) => void }) {
  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <label className="block">
        <span className="mb-1.5 block text-xs font-medium text-white/55">نام سرور</span>
        <input value={draft.name} onChange={(e) => set("name", e.target.value)} placeholder="مثلاً آلمان وایرگارد" className={inputCls} />
      </label>
      <label className="block">
        <span className="mb-1.5 block text-xs font-medium text-white/55">ریمارک (روی نام مشتری)</span>
        <input value={draft.remark} onChange={(e) => set("remark", e.target.value)} dir="ltr" placeholder="Germany" className={`${inputCls} text-left`} />
      </label>
      <label className="block">
        <span className="mb-1.5 block text-xs font-medium text-white/55">پرچم / کد کشور</span>
        <input value={draft.flag} onChange={(e) => set("flag", e.target.value)} dir="ltr" placeholder="DE" className={`${inputCls} text-left`} />
      </label>
      <label className="block">
        <span className="mb-1.5 block text-xs font-medium text-white/55">ظرفیت (تعداد مشتری) · ۰=نامحدود</span>
        <input value={draft.capacity} onChange={(e) => set("capacity", e.target.value)} dir="ltr" inputMode="numeric" className={`${inputCls} text-left`} />
      </label>
    </div>
  );
}

// Edit an already-saved panel. Same verify-before-save rule as adding one: the backend re-tests the
// connection before persisting. Password/apiToken start blank — they're never sent to the browser — so
// leaving them empty on save keeps the stored credential; typing a new value replaces it.
function EditPanelForm({
  panel,
  providers,
  onClose,
  onSaved,
}: {
  panel: WireGuardPanelInfo;
  providers: WireGuardProviderInfo[];
  onClose: () => void;
  onSaved: (panel: WireGuardPanelInfo) => void;
}) {
  const [draft, setDraft] = useState<PanelDraft>({
    url: panel.url,
    username: panel.username,
    password: "",
    apiToken: "",
    name: panel.name,
    remark: panel.remark,
    flag: panel.flag,
    capacity: String(panel.capacity),
  });
  // Credentials are never sent to the browser, so the form starts masked (proving one is stored) instead of
  // an empty box the operator might mistake for "no credential saved".
  const [changeToken, setChangeToken] = useState(!panel.hasApiToken);
  const [changePassword, setChangePassword] = useState(!panel.hasPassword);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const set = <K extends keyof PanelDraft>(k: K, v: PanelDraft[K]) => setDraft((d) => ({ ...d, [k]: v }));

  async function save() {
    setError("");
    if (!draft.url.trim()) {
      setError("آدرس پنل را وارد کنید.");
      return;
    }
    if (!draft.apiToken.trim() && !panel.hasApiToken && (!draft.username.trim() || (!draft.password && !panel.hasPassword))) {
      setError("توکن دسترسی یا نام کاربری و گذرواژه پنل را وارد کنید.");
      return;
    }
    setBusy(true);
    try {
      const updated = await api.wireguard.update(panel.id, {
        provider: panel.provider,
        url: draft.url.trim(),
        username: draft.username.trim(),
        password: draft.password,
        apiToken: draft.apiToken.trim(),
        name: draft.name.trim(),
        remark: draft.remark.trim(),
        flag: draft.flag.trim(),
        capacity: Math.max(0, Number(draft.capacity) || 0),
      });
      onSaved(updated);
    } catch (e) {
      setError(e instanceof Error ? e.message : "ویرایش ناموفق بود");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mt-4 rounded-xl border border-white/10 bg-white/[0.02] p-4">
      <div className="mb-3 flex items-center justify-between">
        <p className="text-sm font-bold text-white">ویرایش پنل {providerName(providers, panel.provider)}</p>
        <button onClick={onClose} className="text-xs text-white/45 transition hover:text-white">بستن</button>
      </div>

      <div className="space-y-4">
        <label className="block">
          <span className="mb-1.5 block text-xs font-medium text-white/55">آدرس کامل پنل (URL)</span>
          <input value={draft.url} onChange={(e) => set("url", e.target.value)} dir="ltr" placeholder={URL_HINT} className={`${inputCls} text-left`} />
        </label>

        <div className="rounded-xl border border-[#3a64f2]/25 bg-[#3a64f2]/[0.06] p-4">
          <div className="mb-1.5 flex items-center justify-between">
            <span className="text-xs font-bold text-[#8aa6ff]">توکن دسترسی پنل (API Token)</span>
            {panel.hasApiToken && (
              <button
                type="button"
                onClick={() => {
                  setChangeToken((v) => !v);
                  set("apiToken", "");
                }}
                className="text-[11px] font-bold text-[#8aa6ff] transition hover:text-white"
              >
                {changeToken ? "انصراف از تغییر" : "تغییر توکن"}
              </button>
            )}
          </div>
          {changeToken ? (
            <input
              value={draft.apiToken}
              onChange={(e) => set("apiToken", e.target.value)}
              dir="ltr"
              autoComplete="off"
              placeholder="wui_…"
              className={`${inputCls} text-left`}
            />
          ) : (
            <input value="••••••••••••" disabled dir="ltr" className={`${inputCls} text-left text-white/40`} />
          )}
        </div>

        <p className="text-center text-[11px] text-white/35">— یا با نام کاربری و گذرواژه —</p>

        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-white/55">نام کاربری پنل</span>
            <input value={draft.username} onChange={(e) => set("username", e.target.value)} dir="ltr" autoComplete="off" className={`${inputCls} text-left`} />
          </label>
          <div className="block">
            <div className="mb-1.5 flex items-center justify-between">
              <span className="text-xs font-medium text-white/55">گذرواژه پنل</span>
              {panel.hasPassword && (
                <button
                  type="button"
                  onClick={() => {
                    setChangePassword((v) => !v);
                    set("password", "");
                  }}
                  className="text-[11px] font-bold text-[#8aa6ff] transition hover:text-white"
                >
                  {changePassword ? "انصراف از تغییر" : "تغییر گذرواژه"}
                </button>
              )}
            </div>
            {changePassword ? (
              <input
                type="password"
                value={draft.password}
                onChange={(e) => set("password", e.target.value)}
                dir="ltr"
                autoComplete="new-password"
                className={`${inputCls} text-left`}
              />
            ) : (
              <input value="••••••••••••" disabled dir="ltr" className={`${inputCls} text-left text-white/40`} />
            )}
          </div>
        </div>

        <IdentityFields draft={draft} set={set} />

        {error && <p className="text-sm leading-7 text-rose-400">{error}</p>}

        <div className="flex items-center gap-2 border-t border-white/8 pt-4">
          <button
            onClick={save}
            disabled={busy}
            className="flex h-10 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
          >
            {busy && <Spinner />}
            ذخیره تغییرات
          </button>
          <button onClick={onClose} disabled={busy} className="text-sm text-white/45 transition hover:text-white disabled:opacity-60">
            انصراف
          </button>
        </div>
      </div>
    </div>
  );
}

// Manually create a customer on this panel — the same call fulfilment will make. Zero means unlimited for
// traffic and duration (matching W-UI); the device limit is at least 1. Duration is in days: a month is 30
// days here, a year 365, so the presets follow the plan rules.
function CreateClientForm({ panelId, onClose }: { panelId: number; onClose: () => void }) {
  const [name, setName] = useState("");
  const [totalGb, setTotalGb] = useState("0");
  const [deviceLimit, setDeviceLimit] = useState("1");
  const [durationDays, setDurationDays] = useState("30");
  const [startOnFirstUse, setStartOnFirstUse] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [result, setResult] = useState<{ clientId: number; subId: string; interfacesAdded: number; subscriptionUrl: string } | null>(null);

  // Which tunnel(s) to place the customer on — exactly what a plan will store. Loaded once when the form opens.
  const [interfaces, setInterfaces] = useState<WireGuardInterface[] | null>(null);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [interfacesErr, setInterfacesErr] = useState("");

  useEffect(() => {
    api.wireguard
      .interfaces(panelId)
      .then((list) => {
        setInterfaces(list);
        // Pre-select every enabled tunnel so a quick test works out of the box; the operator narrows it.
        setSelected(new Set(list.filter((i) => i.enabled).map((i) => i.id)));
      })
      .catch((e) => setInterfacesErr(e instanceof Error ? e.message : "خواندن تانل‌ها ناموفق بود"));
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
    if (!name.trim()) {
      setError("نام مشتری را وارد کنید.");
      return;
    }
    if (selected.size === 0) {
      setError("حداقل یک تانل (سرور) انتخاب کنید.");
      return;
    }
    setBusy(true);
    try {
      const r = await api.wireguard.addClient(panelId, {
        name: name.trim(),
        totalGb: Math.max(0, Number(totalGb) || 0),
        deviceLimit: Math.max(1, Number(deviceLimit) || 1),
        durationDays: Math.max(0, Number(durationDays) || 0),
        startOnFirstUse,
        interfaceIds: [...selected],
      });
      setResult({ clientId: r.clientId, subId: r.subId, interfacesAdded: r.interfacesAdded, subscriptionUrl: r.subscriptionUrl });
    } catch (e) {
      setError(e instanceof Error ? e.message : "ساخت مشتری ناموفق بود");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mt-4 rounded-xl border border-white/10 bg-white/[0.02] p-4">
      <div className="mb-3 flex items-center justify-between">
        <p className="text-sm font-bold text-white">ساخت مشتری آزمایشی</p>
        <button onClick={onClose} className="text-xs text-white/45 transition hover:text-white">بستن</button>
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
        <label className="block sm:col-span-3">
          <span className="mb-1.5 block text-xs font-medium text-white/55">نام مشتری</span>
          <input value={name} onChange={(e) => setName(e.target.value)} dir="ltr" placeholder="reza-1m" className={`${inputCls} text-left`} />
        </label>
        <label className="block">
          <span className="mb-1.5 block text-xs font-medium text-white/55">حجم (گیگابایت) · ۰ = نامحدود</span>
          <input value={totalGb} onChange={(e) => setTotalGb(e.target.value)} dir="ltr" inputMode="numeric" className={`${inputCls} text-left`} />
        </label>
        <label className="block">
          <span className="mb-1.5 block text-xs font-medium text-white/55">تعداد دستگاه · حداقل ۱</span>
          <input value={deviceLimit} onChange={(e) => setDeviceLimit(e.target.value)} dir="ltr" inputMode="numeric" className={`${inputCls} text-left`} />
        </label>
        <label className="block">
          <span className="mb-1.5 block text-xs font-medium text-white/55">مدت (روز) · خالی/۰ = نامحدود</span>
          <input value={durationDays} onChange={(e) => setDurationDays(e.target.value)} dir="ltr" inputMode="numeric" className={`${inputCls} text-left`} />
        </label>
      </div>

      <div className="mt-2.5 flex flex-wrap items-center gap-1.5">
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
        {/* W-UI's start-on-first-use: the clock begins at the first handshake rather than at purchase. */}
        <label className="mr-auto flex items-center gap-2 text-[11px] text-white/60">
          <input type="checkbox" checked={startOnFirstUse} onChange={(e) => setStartOnFirstUse(e.target.checked)} className="h-4 w-4 accent-[#3a64f2]" />
          شروع زمان از اولین اتصال
        </label>
      </div>

      {/* Tunnel selection — the customer is created ONLY on the checked ones, exactly like a plan would specify. */}
      <div className="mt-4">
        <p className="mb-2 text-xs font-medium text-white/55">تانل(ها) / سرورهایی که مشتری روی آن ساخته شود</p>
        {interfacesErr ? (
          <p className="text-xs leading-6 text-rose-400">{interfacesErr}</p>
        ) : interfaces === null ? (
          <div className="flex items-center gap-2 text-xs text-white/45"><Spinner className="h-4 w-4" /> در حال خواندن تانل‌ها…</div>
        ) : interfaces.length === 0 ? (
          <p className="text-xs text-white/45">تانلی روی این پنل نیست.</p>
        ) : (
          <div className="flex flex-wrap gap-2">
            {interfaces.map((i) => {
              const on = selected.has(i.id);
              return (
                <button
                  key={i.id}
                  onClick={() => toggle(i.id)}
                  disabled={!i.enabled}
                  className={`flex items-center gap-2 rounded-lg border px-3 py-1.5 text-xs font-bold transition ${
                    !i.enabled
                      ? "cursor-not-allowed border-white/8 text-white/30"
                      : on
                        ? "border-transparent bg-[#3a64f2]/20 text-[#8aa6ff]"
                        : "border-white/10 text-white/55 hover:text-white"
                  }`}
                >
                  <span className={`grid h-4 w-4 place-items-center rounded border text-[10px] ${on ? "border-[#8aa6ff] bg-[#3a64f2]/40 text-white" : "border-white/25"}`}>
                    {on ? "✓" : ""}
                  </span>
                  {i.name || `تانل ${formatNumber(i.id)}`}
                  <span className="text-white/35">{protocolLabel(i)}</span>
                  <span dir="ltr" className="text-white/35">#{i.id}</span>
                </button>
              );
            })}
          </div>
        )}
      </div>

      {error && <p className="mt-3 text-xs leading-6 text-rose-400">{error}</p>}

      {result && (
        <div className="mt-3 space-y-1.5 rounded-lg border border-emerald-500/20 bg-emerald-500/[0.06] p-3 text-xs text-emerald-300">
          <p className="font-bold">مشتری روی {formatNumber(result.interfacesAdded)} تانل ساخته شد.</p>
          <p dir="ltr" className="break-all text-emerald-300/80">client id: {result.clientId}</p>
          {result.subId && <p dir="ltr" className="break-all text-emerald-300/80">subId: {result.subId}</p>}
          {result.subscriptionUrl ? (
            <p dir="ltr" className="break-all font-bold text-emerald-200">لینک اشتراک: {result.subscriptionUrl}</p>
          ) : (
            <p className="text-amber-300/80">لینک اشتراک خوانده نشد — سرویس Subscription را در تنظیمات پنل W-UI فعال کنید.</p>
          )}
        </div>
      )}

      <button
        onClick={submit}
        disabled={busy}
        className="mt-3 flex h-10 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
      >
        {busy && <Spinner />}
        ساخت مشتری
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
  providers: WireGuardProviderInfo[];
  onCancel: () => void;
  onAdded: (panel: WireGuardPanelInfo) => void;
}) {
  const [step, setStep] = useState<1 | 2>(1);
  const [provider, setProvider] = useState<WireGuardProvider | null>(null);
  const [draft, setDraft] = useState<PanelDraft>({ url: "", username: "", password: "", apiToken: "", name: "", remark: "", flag: "", capacity: "0" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [okMsg, setOkMsg] = useState("");

  const set = <K extends keyof PanelDraft>(k: K, v: PanelDraft[K]) => setDraft((d) => ({ ...d, [k]: v }));
  const selectedName = useMemo(() => (provider ? providers.find((p) => p.provider === provider)?.name ?? provider : ""), [provider, providers]);

  const body = () =>
    provider && {
      provider,
      url: draft.url.trim(),
      username: draft.username.trim(),
      password: draft.password,
      apiToken: draft.apiToken.trim(),
      name: draft.name.trim(),
      remark: draft.remark.trim(),
      flag: draft.flag.trim(),
      capacity: Math.max(0, Number(draft.capacity) || 0),
    };

  function validate(): boolean {
    if (!draft.url.trim() || (!draft.apiToken.trim() && (!draft.username.trim() || !draft.password))) {
      setError("آدرس پنل و سپس توکن دسترسی یا نام کاربری و گذرواژه را وارد کنید.");
      return false;
    }
    return true;
  }

  async function save() {
    const b = body();
    if (!b) return;
    setError("");
    setOkMsg("");
    if (!validate()) return;
    setBusy(true);
    try {
      // The backend connects to the panel and reads its tunnels before it will save, so a successful add is
      // proof the connection actually works.
      onAdded(await api.wireguard.add(b));
    } catch (e) {
      setError(e instanceof Error ? e.message : "افزودن پنل ناموفق بود");
    } finally {
      setBusy(false);
    }
  }

  async function testOnly() {
    const b = body();
    if (!b) return;
    setError("");
    setOkMsg("");
    if (!validate()) return;
    setBusy(true);
    try {
      const r = await api.wireguard.test(b);
      setOkMsg(`اتصال موفق بود · ${formatNumber(r.interfaceCount)} تانل پیدا شد${r.version ? ` · نسخه ${r.version}` : ""}. حالا می‌توانید ذخیره کنید.`);
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
            اطلاعات ورود به پنل وصل می‌شود و مشتری‌ها را می‌سازد.
          </p>

          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-white/55">آدرس کامل پنل (URL)</span>
            <input value={draft.url} onChange={(e) => set("url", e.target.value)} dir="ltr" placeholder={URL_HINT} className={`${inputCls} text-left`} />
            <span className="mt-1.5 block text-[11px] leading-5 text-white/40">
              همان «Access URL» که نصاب W-UI چاپ می‌کند: با http یا https، همراه پورت و WebBasePath. نمونه: <span dir="ltr">{URL_HINT}</span>
            </span>
          </label>

          {/* Preferred path. W-UI issues `wui_` tokens for machines; a username/password login is a browser
              session bound to a cookie and blocked outright when the admin has two-factor on. */}
          <label className="block rounded-xl border border-[#3a64f2]/25 bg-[#3a64f2]/[0.06] p-4">
            <span className="mb-1.5 block text-xs font-bold text-[#8aa6ff]">توکن دسترسی پنل (روش پیشنهادی)</span>
            <input
              value={draft.apiToken}
              onChange={(e) => set("apiToken", e.target.value)}
              dir="ltr"
              autoComplete="off"
              placeholder="wui_…"
              className={`${inputCls} text-left`}
            />
            <span className="mt-1.5 block text-[11px] leading-5 text-white/45">
              همان توکنی که نصاب W-UI در پایان نصب چاپ می‌کند، یا یک توکن جدید از پنل: Nodes → Access tokens. اگر
              کاربر پنل ورود دومرحله‌ای دارد، فقط با توکن می‌توان وصل شد.
            </span>
          </label>

          <p className="text-center text-[11px] text-white/35">— یا با نام کاربری و گذرواژه —</p>

          <div className="grid gap-4 sm:grid-cols-2">
            <label className="block">
              <span className="mb-1.5 block text-xs font-medium text-white/55">نام کاربری پنل</span>
              <input value={draft.username} onChange={(e) => set("username", e.target.value)} dir="ltr" autoComplete="off" className={`${inputCls} text-left`} />
            </label>
            <label className="block">
              <span className="mb-1.5 block text-xs font-medium text-white/55">گذرواژه پنل</span>
              <input
                type="password"
                value={draft.password}
                onChange={(e) => set("password", e.target.value)}
                dir="ltr"
                autoComplete="new-password"
                className={`${inputCls} text-left`}
              />
            </label>
          </div>

          <IdentityFields draft={draft} set={set} />

          <p className="rounded-xl border border-white/8 bg-white/[0.02] px-4 py-3 text-[11px] leading-6 text-white/45">
            توکن و گذرواژه به‌صورت رمزنگاری‌شده ذخیره می‌شوند و هیچ‌گاه به مرورگر بازگردانده نمی‌شوند. لینک اشتراک
            مشتری را خود پنل W-UI می‌سازد؛ کافی است سرویس Subscription در تنظیمات پنل فعال باشد.
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
              اتصال و ذخیره پنل
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
