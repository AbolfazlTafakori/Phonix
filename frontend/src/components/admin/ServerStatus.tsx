import { serverResources, serverInfo } from "@/data/admin";
import { Card } from "./ui";
import AdminIcon from "./AdminIcon";

function Gauge({ used, accent, icon }: { used: number; accent: string; icon: string }) {
  const r = 30;
  const c = 2 * Math.PI * r;
  const off = c - (used / 100) * c;
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
        />
      </svg>
      <AdminIcon name={icon} className="h-5 w-5" />
    </div>
  );
}

export default function ServerStatus() {
  return (
    <Card className="p-6">
      <div className="mb-5 flex items-center justify-between">
        <div>
          <h3 className="text-lg font-bold text-white">وضعیت سرور</h3>
          <p className="text-sm text-white/45">منابع سیستم به‌صورت لحظه‌ای</p>
        </div>
        <span className="flex items-center gap-2 rounded-full bg-emerald-500/15 px-3 py-1 text-xs font-bold text-emerald-400">
          <span className="h-2 w-2 animate-pulse rounded-full bg-emerald-400" />
          {serverInfo.status}
        </span>
      </div>

      <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-4">
        {serverResources.map((res) => (
          <div key={res.label} className="flex items-center gap-4 rounded-xl bg-white/[0.03] p-4">
            <div style={{ color: res.accent }}>
              <Gauge used={res.used} accent={res.accent} icon={res.icon} />
            </div>
            <div className="min-w-0">
              <p className="text-2xl font-bold text-white">٪{res.used}</p>
              <p className="truncate text-sm font-medium text-white/75">{res.label}</p>
              <p className="truncate text-xs text-white/40">{res.detail}</p>
            </div>
          </div>
        ))}
      </div>

      <div className="mt-5 grid grid-cols-2 gap-3 border-t border-white/8 pt-5 sm:grid-cols-4">
        {[
          { k: "آپتایم", v: serverInfo.uptime },
          { k: "بار سیستم", v: serverInfo.load },
          { k: "درخواست‌ها", v: serverInfo.requests },
          { k: "وضعیت", v: serverInfo.status },
        ].map((s) => (
          <div key={s.k}>
            <p className="text-xs text-white/40">{s.k}</p>
            <p className="mt-1 text-sm font-bold text-white">{s.v}</p>
          </div>
        ))}
      </div>
    </Card>
  );
}
