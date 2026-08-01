"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { ClusterStatus, ClusterEvent } from "@/lib/types";
import { toFa } from "@/lib/format";
import { Card, PageHeader, Spinner, Modal } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

const POLL_MS = 5000;

const roleMeta: Record<ClusterStatus["role"], { label: string; cls: string }> = {
  Primary: { label: "Primary", cls: "bg-emerald-500/15 text-emerald-400" },
  Standby: { label: "Standby", cls: "bg-sky-500/15 text-sky-400" },
  Recovering: { label: "Recovering", cls: "bg-amber-500/15 text-amber-400" },
  Standalone: { label: "Standalone", cls: "bg-white/10 text-white/60" },
};

const eventLevelCls: Record<ClusterEvent["level"], string> = {
  info: "bg-sky-500/15 text-sky-400",
  warning: "bg-amber-500/15 text-amber-400",
  error: "bg-rose-500/15 text-rose-400",
  critical: "bg-rose-500/25 text-rose-300",
};

// "۴۵ ثانیه پیش" / "۳ دقیقه پیش" — same spirit as ServerStatus's uptimeLabel: only the unit that matters.
function agoLabel(iso: string | null): string {
  if (!iso) return "—";
  const seconds = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
  if (seconds < 60) return `${toFa(seconds)} ثانیه پیش`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${toFa(minutes)} دقیقه پیش`;
  const hours = Math.round(minutes / 60);
  return `${toFa(hours)} ساعت پیش`;
}

// "۴۵ ثانیه پیش" for the event log; falls back to a full date once it's more than a day old.
function eventTimeLabel(iso: string): string {
  const d = new Date(iso);
  const seconds = Math.max(0, Math.round((Date.now() - d.getTime()) / 1000));
  if (seconds < 60) return `${toFa(seconds)} ثانیه پیش`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${toFa(minutes)} دقیقه پیش`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${toFa(hours)} ساعت پیش`;
  return d.toLocaleString("fa-IR");
}

export default function ClusterPage() {
  const [data, setData] = useState<ClusterStatus | null>(null);
  const [events, setEvents] = useState<ClusterEvent[]>([]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState<"promote" | "recover" | "resync" | "bootstrap" | null>(null);
  const [actionError, setActionError] = useState("");
  const [confirmAction, setConfirmAction] = useState<"promote" | "recover" | null>(null);

  const [configMode, setConfigMode] = useState<"primary" | "standby">("primary");
  const [configPeerUrl, setConfigPeerUrl] = useState("");
  const [configSecret, setConfigSecret] = useState("");
  const [configBusy, setConfigBusy] = useState(false);
  const [configError, setConfigError] = useState("");
  const [configNotice, setConfigNotice] = useState("");

  async function load() {
    try {
      const status = await api.cluster.status();
      setData(status);
      setError("");
      setConfigPeerUrl((prev) => (prev ? prev : status.peerUrl ?? ""));
      if (status.clusterEnabled) {
        try { setEvents(await api.cluster.events()); } catch { /* غیر بحرانی */ }
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در دریافت وضعیت خوشه");
    }
  }

  useEffect(() => {
    load();
    const id = setInterval(load, POLL_MS);
    return () => clearInterval(id);
  }, []);

  async function submitConfig(mode?: "primary" | "standby") {
    setConfigBusy(true);
    setConfigError("");
    setConfigNotice("");
    try {
      const body: { mode?: string; peerUrl?: string; secret?: string } = {};
      if (mode) body.mode = mode;
      if (configPeerUrl.trim()) body.peerUrl = configPeerUrl.trim();
      if (configSecret.trim()) body.secret = configSecret.trim();
      const res = await api.cluster.updateConfig(body);
      setConfigSecret("");
      setConfigNotice(res?.warning ?? "تنظیمات با موفقیت اعمال شد.");
      await load();
    } catch (e) {
      setConfigError(e instanceof Error ? e.message : "خطا در اعمال تنظیمات");
    } finally {
      setConfigBusy(false);
    }
  }

  async function runAction(action: "promote" | "recover" | "resync" | "bootstrap") {
    setBusy(action);
    setActionError("");
    try {
      if (action === "promote") await api.cluster.promote();
      else if (action === "recover") await api.cluster.recover();
      else if (action === "bootstrap") await api.cluster.bootstrap();
      else await api.cluster.resync();
      await load();
    } catch (e) {
      setActionError(e instanceof Error ? e.message : "خطا در انجام عملیات");
    } finally {
      setBusy(null);
      setConfirmAction(null);
    }
  }

  if (!data) {
    return (
      <div>
        <PageHeader title="مدیریت خوشه (HA)" desc="نقش این سرور، وضعیت همگام‌سازی و اقدامات دستی خوشه" />
        <Card className="grid place-items-center p-6 py-16">
          {error ? <p className="text-sm text-rose-400">{error}</p> : <Spinner className="h-8 w-8" />}
        </Card>
      </div>
    );
  }

  const role = roleMeta[data.role];

  return (
    <div>
      <PageHeader title="مدیریت خوشه (HA)" desc="نقش این سرور، وضعیت همگام‌سازی و اقدامات دستی خوشه" />

      {!data.clusterEnabled ? (
        <Card className="p-8">
          <div className="text-center">
            <AdminIcon name="activity" className="mx-auto mb-3 h-8 w-8 text-white/25" />
            <p className="text-sm font-bold text-white/70">خوشه‌بندی روی این سرور پیکربندی نشده است</p>
            <p className="mt-1 text-sm text-white/40">
              این سرور در حالت Standalone اجرا می‌شود. حالت و آدرس سرور مقابل را زیر تنظیم کنید تا بدون نیاز به
              ترمینال یا ری‌استارت فعال شود.
            </p>
          </div>

          {configError && <p className="mt-5 rounded-xl bg-rose-500/10 p-3 text-sm text-rose-400">{configError}</p>}
          {configNotice && <p className="mt-5 rounded-xl bg-emerald-500/10 p-3 text-sm text-emerald-400">{configNotice}</p>}

          <div className="mx-auto mt-5 grid max-w-xl gap-4">
            <div>
              <label className="mb-1.5 block text-xs text-white/40">حالت این سرور</label>
              <select
                value={configMode}
                onChange={(e) => setConfigMode(e.target.value as "primary" | "standby")}
                className="h-11 w-full rounded-xl border border-white/15 bg-white/[0.03] px-3 text-sm text-white focus:border-white/30 focus:outline-none"
              >
                <option value="primary">Primary</option>
                <option value="standby">Standby</option>
              </select>
            </div>
            <div>
              <label className="mb-1.5 block text-xs text-white/40">آدرس سرور مقابل (Peer URL)</label>
              <input
                dir="ltr"
                value={configPeerUrl}
                onChange={(e) => setConfigPeerUrl(e.target.value)}
                placeholder="https://peer.example.com"
                className="h-11 w-full rounded-xl border border-white/15 bg-white/[0.03] px-3 text-sm text-white placeholder:text-white/25 focus:border-white/30 focus:outline-none"
              />
            </div>
            <div>
              <label className="mb-1.5 block text-xs text-white/40">کلید امنیتی مشترک (Secret)</label>
              <input
                dir="ltr"
                type="password"
                value={configSecret}
                onChange={(e) => setConfigSecret(e.target.value)}
                placeholder="باید روی هر دو سرور یکسان باشد"
                className="h-11 w-full rounded-xl border border-white/15 bg-white/[0.03] px-3 text-sm text-white placeholder:text-white/25 focus:border-white/30 focus:outline-none"
              />
            </div>
            <button
              onClick={() => submitConfig(configMode)}
              disabled={configBusy || !configPeerUrl.trim() || !configSecret.trim()}
              className="h-11 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-40"
            >
              {configBusy ? "..." : "فعال‌سازی خوشه"}
            </button>
          </div>
        </Card>
      ) : (
        <>
          <Card className="p-6">
            <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
              <div>
                <h3 className="text-lg font-bold text-white">نقش این سرور</h3>
                <p className="text-sm text-white/45">
                  {data.nodeId ? <span dir="ltr">{data.nodeId}</span> : "بدون شناسه"}
                </p>
              </div>
              <span className={`flex items-center gap-2 rounded-full px-4 py-1.5 text-sm font-bold ${role.cls}`}>
                <span className={`h-2 w-2 rounded-full ${data.role === "Primary" ? "animate-pulse bg-emerald-400" : "bg-current"}`} />
                {role.label}
              </span>
            </div>

            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <div className="rounded-xl bg-white/[0.03] p-4">
                <p className="text-xs text-white/40">سرور مقابل (Peer)</p>
                <p dir="ltr" className="mt-1 truncate text-sm font-bold text-white">{data.peerUrl ?? "—"}</p>
              </div>
              <div className="rounded-xl bg-white/[0.03] p-4">
                <p className="text-xs text-white/40">وضعیت اتصال</p>
                <p className={`mt-1 flex items-center gap-1.5 text-sm font-bold ${data.peerReachable ? "text-emerald-400" : "text-rose-400"}`}>
                  <span className={`h-1.5 w-1.5 rounded-full ${data.peerReachable ? "bg-emerald-400" : "bg-rose-400"}`} />
                  {data.peerReachable ? "متصل" : "قطع"}
                </p>
              </div>
              <div className="rounded-xl bg-white/[0.03] p-4">
                <p className="text-xs text-white/40">آخرین همگام‌سازی موفق</p>
                <p className="mt-1 text-sm font-bold text-white">{agoLabel(data.lastSyncUtc)}</p>
              </div>
              <div className="rounded-xl bg-white/[0.03] p-4">
                <p className="text-xs text-white/40">آخرین تماس با سرور مقابل</p>
                <p className="mt-1 text-sm font-bold text-white">{agoLabel(data.lastPeerContactUtc)}</p>
              </div>
            </div>

            <div className="mt-4 grid gap-4 sm:grid-cols-3">
              <div className="rounded-xl bg-white/[0.03] p-4">
                <p className="text-xs text-white/40">رویدادهای در انتظار همگام‌سازی</p>
                <p className={`mt-1 text-2xl font-bold ${data.pendingCount > 0 ? "text-amber-400" : "text-white"}`}>
                  {toFa(data.pendingCount)}
                </p>
              </div>
              <div className="rounded-xl bg-white/[0.03] p-4">
                <p className="text-xs text-white/40">رویدادهای ناموفق (Dead-letter)</p>
                <p className={`mt-1 text-2xl font-bold ${data.deadLetterCount > 0 ? "text-rose-400" : "text-white"}`}>
                  {toFa(data.deadLetterCount)}
                </p>
              </div>
              <div className="rounded-xl bg-white/[0.03] p-4">
                <p className="text-xs text-white/40">سلامت کلی خوشه</p>
                <p className={`mt-1 text-sm font-bold ${data.peerReachable && data.pendingCount === 0 && data.deadLetterCount === 0 ? "text-emerald-400" : "text-amber-400"}`}>
                  {data.peerReachable && data.pendingCount === 0 && data.deadLetterCount === 0 ? "سالم" : data.peerReachable ? "در حال همگام‌سازی" : "نیازمند بررسی"}
                </p>
              </div>
            </div>
          </Card>

          <Card className="mt-4 p-6">
            <h3 className="mb-1 text-lg font-bold text-white">گزارش دقیق</h3>
            <p className="mb-5 text-sm text-white/45">تاریخچه ترفیع/تنزل و وضعیت لاین‌ داده‌ها برای عیب‌یابی دقیق‌تر.</p>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <div className="rounded-xl bg-white/[0.03] p-4">
                <p className="text-xs text-white/40">آخرین Failover خودکار</p>
                <p className="mt-1 text-sm font-bold text-white">{agoLabel(data.lastFailoverAtUtc)}</p>
              </div>
              <div className="rounded-xl bg-white/[0.03] p-4">
                <p className="text-xs text-white/40">آخرین ترفیع دستی</p>
                <p className="mt-1 text-sm font-bold text-white">{agoLabel(data.lastPromotedAtUtc)}</p>
              </div>
              <div className="rounded-xl bg-white/[0.03] p-4">
                <p className="text-xs text-white/40">آخرین تنزل</p>
                <p className="mt-1 text-sm font-bold text-white">{agoLabel(data.lastDemotedAtUtc)}</p>
              </div>
              <div className="rounded-xl bg-white/[0.03] p-4">
                <p className="text-xs text-white/40">رزرو بازه شناسه (Id Band)</p>
                <p className={`mt-1 text-sm font-bold ${data.idBandApplied ? "text-emerald-400" : "text-white/50"}`}>
                  {data.idBandApplied ? "انجام‌شده" : "—"}
                </p>
              </div>
              <div className="rounded-xl bg-white/[0.03] p-4 sm:col-span-2">
                <p className="text-xs text-white/40">Data Epoch این سرور</p>
                <p dir="ltr" className="mt-1 truncate text-sm font-bold text-white">{data.dataEpoch ?? "—"}</p>
              </div>
              <div className="rounded-xl bg-white/[0.03] p-4 sm:col-span-2">
                <p className="text-xs text-white/40">آخرین Data Epoch همگام‌شده با سرور مقابل</p>
                <p dir="ltr" className="mt-1 truncate text-sm font-bold text-white">{data.peerDataEpoch ?? "—"}</p>
              </div>
            </div>
          </Card>

          <Card className="mt-4 p-6">
            <h3 className="mb-1 text-lg font-bold text-white">اقدامات دستی</h3>
            <p className="mb-5 text-sm text-white/45">
              ترفیع به Primary و شروع بازیابی، اقدامات حساس هستند و نیاز به تأیید دارند؛ فقط زمانی که همگام‌سازی
              کامل شده باشد ترفیع انجام می‌شود.
            </p>

            {actionError && <p className="mb-4 rounded-xl bg-rose-500/10 p-3 text-sm text-rose-400">{actionError}</p>}

            <div className="flex flex-wrap gap-3">
              <button
                onClick={() => setConfirmAction("promote")}
                disabled={busy !== null || data.role !== "Recovering"}
                className="rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-5 py-2.5 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-40"
              >
                {busy === "promote" ? "..." : "ترفیع به Primary"}
              </button>
              <button
                onClick={() => setConfirmAction("recover")}
                disabled={busy !== null || data.role !== "Primary"}
                className="rounded-xl border border-white/15 px-5 py-2.5 text-sm font-bold text-white/80 transition hover:bg-white/10 disabled:opacity-40"
              >
                {busy === "recover" ? "..." : "شروع بازیابی (Recovery)"}
              </button>
              <button
                onClick={() => runAction("resync")}
                disabled={busy !== null}
                className="rounded-xl border border-white/15 px-5 py-2.5 text-sm font-bold text-white/80 transition hover:bg-white/10 disabled:opacity-50"
              >
                {busy === "resync" ? "..." : "همگام‌سازی دستی"}
              </button>
              {data.role === "Standby" && (
                <button
                  onClick={() => runAction("bootstrap")}
                  disabled={busy !== null}
                  className="rounded-xl border border-white/15 px-5 py-2.5 text-sm font-bold text-white/80 transition hover:bg-white/10 disabled:opacity-50"
                  title="دریافت اسنپ‌شات کامل از Primary و راه‌اندازی اولیه این سرور Standby"
                >
                  {busy === "bootstrap" ? "..." : "راه‌اندازی اولیه از Primary"}
                </button>
              )}
              <button
                onClick={load}
                disabled={busy !== null}
                className="rounded-xl border border-white/15 px-5 py-2.5 text-sm font-bold text-white/80 transition hover:bg-white/10 disabled:opacity-50"
              >
                به‌روزرسانی وضعیت
              </button>
            </div>
          </Card>

          <Card className="mt-4 p-6">
            <h3 className="mb-1 text-lg font-bold text-white">تنظیمات دستی (بدون ترمینال)</h3>
            <p className="mb-5 text-sm text-white/45">
              تغییر آدرس سرور مقابل یا کلید امنیتی، بدون نیاز به ری‌استارت اعمال می‌شود. فیلد کلید را خالی
              بگذارید تا مقدار فعلی حفظ شود.
            </p>

            {configError && <p className="mb-4 rounded-xl bg-rose-500/10 p-3 text-sm text-rose-400">{configError}</p>}
            {configNotice && <p className="mb-4 rounded-xl bg-emerald-500/10 p-3 text-sm text-emerald-400">{configNotice}</p>}

            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <label className="mb-1.5 block text-xs text-white/40">آدرس سرور مقابل (Peer URL)</label>
                <input
                  dir="ltr"
                  value={configPeerUrl}
                  onChange={(e) => setConfigPeerUrl(e.target.value)}
                  placeholder="https://peer.example.com"
                  className="h-11 w-full rounded-xl border border-white/15 bg-white/[0.03] px-3 text-sm text-white placeholder:text-white/25 focus:border-white/30 focus:outline-none"
                />
              </div>
              <div>
                <label className="mb-1.5 block text-xs text-white/40">چرخش کلید امنیتی مشترک (Secret)</label>
                <input
                  dir="ltr"
                  type="password"
                  value={configSecret}
                  onChange={(e) => setConfigSecret(e.target.value)}
                  placeholder="خالی = بدون تغییر"
                  className="h-11 w-full rounded-xl border border-white/15 bg-white/[0.03] px-3 text-sm text-white placeholder:text-white/25 focus:border-white/30 focus:outline-none"
                />
              </div>
            </div>
            <button
              onClick={() => submitConfig()}
              disabled={configBusy || (!configPeerUrl.trim() && !configSecret.trim())}
              className="mt-4 h-11 rounded-xl border border-white/15 px-5 text-sm font-bold text-white/80 transition hover:bg-white/10 disabled:opacity-40"
            >
              {configBusy ? "..." : "اعمال تغییرات"}
            </button>
            <p className="mt-2 text-xs text-white/30">
              نکته: اگر کلید امنیتی روی این سرور تغییر کند، باید روی سرور مقابل هم دقیقاً همین مقدار تنظیم شود
              وگرنه ارتباط بین دو سرور قطع می‌شود.
            </p>
          </Card>

          <Card className="mt-4 p-6">
            <h3 className="mb-1 text-lg font-bold text-white">گزارش رویدادهای اخیر</h3>
            <p className="mb-5 text-sm text-white/45">آخرین رویدادهای خوشه (ترفیع/تنزل، خطاهای همگام‌سازی، تغییر تنظیمات).</p>
            {events.length === 0 ? (
              <p className="rounded-xl bg-white/[0.03] p-4 text-center text-sm text-white/40">رویدادی ثبت نشده است.</p>
            ) : (
              <div className="max-h-96 space-y-2 overflow-y-auto">
                {events.map((ev, i) => (
                  <div key={i} className="flex items-start gap-3 rounded-xl bg-white/[0.03] p-3">
                    <span className={`mt-0.5 shrink-0 rounded-full px-2 py-0.5 text-[11px] font-bold ${eventLevelCls[ev.level]}`}>
                      {ev.level}
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className="text-sm text-white/80">{ev.message}</p>
                      <p className="mt-0.5 text-xs text-white/30">{eventTimeLabel(ev.atUtc)}</p>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Card>
        </>
      )}

      <Modal
        open={confirmAction !== null}
        onClose={() => busy === null && setConfirmAction(null)}
        title={confirmAction === "promote" ? "ترفیع به Primary" : "شروع بازیابی (Recovery)"}
      >
        <p className="text-sm leading-7 text-white/75">
          {confirmAction === "promote"
            ? "این سرور به Primary ترفیع می‌یابد و سرور مقابل به Standby تنزل داده می‌شود. این کار فقط زمانی که سرور مقابل در دسترس باشد انجام می‌شود."
            : "این سرور به حالت Recovering منتقل می‌شود: فقط‌خواندنی خواهد شد تا زمانی که با سرور مقابل کاملاً همگام شود."}
        </p>
        <div className="mt-6 flex gap-3">
          <button
            onClick={() => confirmAction && runAction(confirmAction)}
            disabled={busy !== null}
            className="h-11 flex-1 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-60"
          >
            {busy !== null ? "در حال انجام..." : "تأیید"}
          </button>
          <button
            onClick={() => setConfirmAction(null)}
            disabled={busy !== null}
            className="h-11 flex-1 rounded-xl border border-white/15 text-sm font-bold text-white/80 transition hover:bg-white/10 disabled:opacity-60"
          >
            انصراف
          </button>
        </div>
      </Modal>
    </div>
  );
}
