"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { ServerStatus as ServerStatusData } from "@/lib/types";
import { toFa } from "@/lib/format";
import { Card, Spinner } from "./ui";
import AdminIcon from "./AdminIcon";

const POLL_MS = 5000;

function Gauge({ used, accent, icon }: { used: number; accent: string; icon: string }) {
  const r = 30;
  const c = 2 * Math.PI * r;
  const off = c - (Math.min(100, Math.max(0, used)) / 100) * c;
  return (
    <div className="relative grid h-[76px] w-[76px] place-items-center">
      <svg className="absolute -rotate-90" width="76" height="76" viewBox="0 0 76 76">
        <circle cx="38" cy="38" r={r} fill="none" stroke="rgba(255,255,255,0.08)" strokeWidth="7" />
        <circle
          cx="38"
          cy="38"
          r={r}
          fill="none"
          stroke={accent}
          strokeWidth="7"
          strokeLinecap="round"
          strokeDasharray={c}
          strokeDashoffset={off}
          style={{ transition: "stroke-dashoffset 0.6s ease" }}
        />
      </svg>
      <AdminIcon name={icon} className="h-5 w-5" />
    </div>
  );
}

export default function ServerStatus() {
  const [data, setData] = useState<ServerStatusData | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    let active = true;

    const load = async () => {
      try {
        const d = await api.serverStatus.get();
        if (active) {
          setData(d);
          setError(false);
        }
      } catch {
        if (active) setError(true);
      }
    };

    load();
    const id = setInterval(load, POLL_MS);
    return () => {
      active = false;
      clearInterval(id);
    };
  }, []);

  // Initial fetch: show a loading skeleton (or an error if the very first call failed and we have no data yet).
  if (!data) {
    return (
      <Card className="grid place-items-center p-6 py-16">
        {error ? (
          <p className="text-sm text-rose-400">خطا در دریافت وضعیت سرور</p>
        ) : (
          <Spinner className="h-8 w-8" />
        )}
      </Card>
    );
  }

  const online = data.status === "Online";
  const ramPercent = data.ramTotalMb > 0 ? Math.round((data.ramUsedMb / data.ramTotalMb) * 100) : 0;

  const resources = [
    {
      label: "پردازنده (CPU)",
      used: Math.round(data.cpuPercent),
      detail: "بار پردازشی برنامه",
      accent: "#3a64f2",
      icon: "cpu",
    },
    {
      label: "حافظه (RAM)",
      used: ramPercent,
      detail: `${toFa(data.ramUsedMb)} از ${toFa(data.ramTotalMb)} مگابایت`,
      accent: "#a855f7",
      icon: "ram",
    },
  ];

  const info = [
    { k: "آپتایم", v: `${toFa(data.uptimeDays)} روز و ${toFa(data.uptimeHours)} ساعت` },
    { k: "حافظه مصرفی", v: `${toFa(data.ramUsedMb)} مگابایت` },
    { k: "بار پردازنده", v: `٪${toFa(Math.round(data.cpuPercent))}` },
    { k: "وضعیت", v: online ? "آنلاین" : data.status },
  ];

  return (
    <Card className="p-6">
      <div className="mb-5 flex items-center justify-between">
        <div>
          <h3 className="text-lg font-bold text-white">وضعیت سرور</h3>
          <p className="text-sm text-white/45">منابع سیستم به‌صورت لحظه‌ای</p>
        </div>
        <span
          className={`flex items-center gap-2 rounded-full px-3 py-1 text-xs font-bold ${
            online ? "bg-emerald-500/15 text-emerald-400" : "bg-rose-500/15 text-rose-400"
          }`}
        >
          <span className={`h-2 w-2 rounded-full ${online ? "animate-pulse bg-emerald-400" : "bg-rose-400"}`} />
          {online ? "آنلاین" : data.status}
        </span>
      </div>

      <div className="grid gap-5 sm:grid-cols-2">
        {resources.map((res) => (
          <div key={res.label} className="flex items-center gap-4 rounded-xl bg-white/[0.03] p-4">
            <div style={{ color: res.accent }}>
              <Gauge used={res.used} accent={res.accent} icon={res.icon} />
            </div>
            <div className="min-w-0">
              <p className="text-2xl font-bold text-white">٪{toFa(res.used)}</p>
              <p className="truncate text-sm font-medium text-white/75">{res.label}</p>
              <p className="truncate text-xs text-white/40">{res.detail}</p>
            </div>
          </div>
        ))}
      </div>

      <div className="mt-5 grid grid-cols-2 gap-3 border-t border-white/8 pt-5 sm:grid-cols-4">
        {info.map((s) => (
          <div key={s.k}>
            <p className="text-xs text-white/40">{s.k}</p>
            <p className="mt-1 text-sm font-bold text-white">{s.v}</p>
          </div>
        ))}
      </div>
    </Card>
  );
}
